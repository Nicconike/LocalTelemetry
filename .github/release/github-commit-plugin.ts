import fs from 'node:fs';
import path from 'node:path';
import process from 'node:process';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { Octokit } from '@octokit/rest';

interface PluginConfig {
    files?: string[];
}

interface PluginContext {
    options?: {
        dryRun?: boolean;
        repositoryUrl?: string;
    };
    branch?: {
        name: string;
    };
    env: Record<string, string | undefined>;
    cwd?: string;
    logger: {
        log: (...args: unknown[]) => void;
        warn: (...args: unknown[]) => void;
        error: (...args: unknown[]) => void;
    };
    nextRelease: {
        version: string;
        gitTag: string;
        gitHead: string;
        notes: string;
    };
}

export interface CommitOptions {
    files: string[];
    message: string;
    branch?: string;
    cwd?: string;
    token?: string;
    owner?: string;
    repo?: string;
    dryRun?: boolean;
    logger?: {
        log: (...args: unknown[]) => void;
        warn: (...args: unknown[]) => void;
        error: (...args: unknown[]) => void;
    };
}

function resolveGitBinary(): string {
    if (process.platform === 'win32') {
        const candidates = [
            String.raw`C:\Program Files\Git\cmd\git.exe`,
            String.raw`C:\Program Files\Git\bin\git.exe`,
            String.raw`C:\Program Files (x86)\Git\cmd\git.exe`,
        ];
        for (const candidate of candidates) {
            if (fs.existsSync(candidate)) {
                return candidate;
            }
        }
    } else {
        const candidates = ['/usr/bin/git', '/usr/local/bin/git', '/bin/git'];
        for (const candidate of candidates) {
            if (fs.existsSync(candidate)) {
                return candidate;
            }
        }
    }
    return 'git';
}

/**
 * Verify conditions for GitHub REST API commit plugin.
 */
export function verifyConditions(_pluginConfig: PluginConfig, context: PluginContext): void {
    const token = context.env.GITHUB_TOKEN || context.env.GH_TOKEN;
    if (!token && !context.options?.dryRun) {
        throw new Error('GITHUB_TOKEN or GH_TOKEN environment variable is required for github-commit-plugin.');
    }
}

/**
 * Commit specific files to GitHub via REST API with verified web-flow signatures.
 */
export async function commitFiles(options: CommitOptions): Promise<string | null> {
    const {
        files,
        message,
        branch = 'master',
        cwd = process.cwd(),
        token = process.env.GITHUB_TOKEN || process.env.GH_TOKEN,
        logger = console,
        dryRun = false,
    } = options;

    if (!token && !dryRun) {
        throw new Error('GITHUB_TOKEN or GH_TOKEN environment variable is required for github-commit-plugin.');
    }

    let owner = options.owner || 'Nicconike';
    let repo = options.repo || 'LocalTelemetry';

    if (!options.owner && process.env.GITHUB_REPOSITORY) {
        const parts = process.env.GITHUB_REPOSITORY.split('/');
        owner = parts[0];
        repo = parts[1];
    }

    logger.log(`Checking files for GitHub API commit on ${owner}/${repo}:${branch}...`);

    const existingFiles = files.filter((file) => {
        const fullPath = path.resolve(cwd, file);
        return fs.existsSync(fullPath);
    });

    if (existingFiles.length === 0) {
        logger.warn('No files found to commit via GitHub API.');
        return null;
    }

    if (dryRun) {
        logger.log(`[DRY RUN] Would commit ${existingFiles.length} file(s) via GitHub REST API:`);
        for (const file of existingFiles) {
            logger.log(`  - ${file}`);
        }
        return null;
    }

    const octokit = new Octokit({ auth: token });

    // 1. Retrieve current branch ref and base tree
    const refData = await octokit.rest.git.getRef({
        owner,
        repo,
        ref: `heads/${branch}`,
    });
    const parentSha = refData.data.object.sha;

    const parentCommit = await octokit.rest.git.getCommit({
        owner,
        repo,
        commit_sha: parentSha,
    });
    const baseTreeSha = parentCommit.data.tree.sha;

    // 2. Upload file blobs concurrently using Promise.all
    const treeEntries: Array<{
        path: string;
        mode: '100644';
        type: 'blob';
        sha: string;
    }> = await Promise.all(
        existingFiles.map(async (file) => {
            const fullPath = path.resolve(cwd, file);
            const content = fs.readFileSync(fullPath);
            const blob = await octokit.rest.git.createBlob({
                owner,
                repo,
                content: content.toString('base64'),
                encoding: 'base64',
            });
            return {
                path: file.replaceAll('\\', '/'),
                mode: '100644' as const,
                type: 'blob' as const,
                sha: blob.data.sha,
            };
        })
    );

    // 3. Create new tree
    const newTree = await octokit.rest.git.createTree({
        owner,
        repo,
        base_tree: baseTreeSha,
        tree: treeEntries,
    });

    if (newTree.data.sha === baseTreeSha) {
        logger.log('No file changes detected between new tree and base tree. Skipping commit.');
        return null;
    }

    // 4. Create commit (omit author/committer so GitHub signs with authenticated app)
    const newCommit = await octokit.rest.git.createCommit({
        owner,
        repo,
        message,
        tree: newTree.data.sha,
        parents: [parentSha],
    });

    logger.log(`Created verified commit via GitHub API: ${newCommit.data.sha}`);

    // 5. Update branch ref
    await octokit.rest.git.updateRef({
        owner,
        repo,
        ref: `heads/${branch}`,
        sha: newCommit.data.sha,
        force: false,
    });

    logger.log(`Updated branch refs/heads/${branch} to ${newCommit.data.sha}`);

    // 6. Synchronize local git working tree on runner
    try {
        const gitBin = resolveGitBinary();
        execFileSync(gitBin, ['fetch', 'origin', branch], { cwd, stdio: 'inherit' });
        execFileSync(gitBin, ['reset', '--hard', `origin/${branch}`], { cwd, stdio: 'inherit' });
        logger.log(`Synchronized local git working tree to ${newCommit.data.sha}`);
    } catch (err: unknown) {
        const errMessage = err instanceof Error ? err.message : String(err);
        logger.warn(`Failed to sync local git working tree: ${errMessage}`);
    }

    return newCommit.data.sha;
}

/**
 * Commit updated release assets to GitHub via REST API.
 * Automatically signed by GitHub web-flow with authenticated GitHub App identity.
 */
export async function prepare(pluginConfig: PluginConfig, context: PluginContext): Promise<void> {
    const { logger, cwd = process.cwd(), nextRelease } = context;
    const isDryRun = Boolean(context.options?.dryRun);

    const token = context.env.GITHUB_TOKEN || context.env.GH_TOKEN;
    let owner = 'Nicconike';
    let repo = 'LocalTelemetry';

    if (context.env.GITHUB_REPOSITORY) {
        const parts = context.env.GITHUB_REPOSITORY.split('/');
        owner = parts[0];
        repo = parts[1];
    } else if (context.options?.repositoryUrl) {
        const match = /github\.com[/:]([^/]+)\/([^/.]+)/.exec(context.options.repositoryUrl);
        if (match) {
            owner = match[1];
            repo = match[2];
        }
    }

    const branch = context.branch?.name || 'master';
    const files = pluginConfig?.files || [
        'CHANGELOG.md',
        'bucket/localtelemetry.json',
        'docs/.vitepress/versions.mjs',
        'docs/package.json',
        'packaging/winget/Nicconike.LocalTelemetry.yaml',
        'packaging/winget/Nicconike.LocalTelemetry.installer.yaml',
        'packaging/winget/Nicconike.LocalTelemetry.locale.en-US.yaml',
        'setup.iss',
        'src/LocalTelemetry.App/Settings/wwwroot/package.json',
    ];

    const commitMessage = `chore(release): ${nextRelease.version} [skip ci]`;

    const sha = await commitFiles({
        files,
        message: commitMessage,
        branch,
        cwd,
        token,
        owner,
        repo,
        dryRun: isDryRun,
        logger,
    });

    if (sha) {
        nextRelease.gitHead = sha;
        logger.log(`Set nextRelease.gitHead to ${sha}`);
    }
}

// Support direct execution via CLI
const isCli =
    process.argv[1] &&
    path.resolve(process.argv[1]).replaceAll('\\', '/') === fileURLToPath(import.meta.url).replaceAll('\\', '/');

if (isCli) {
    const args = process.argv.slice(2);
    let message = 'chore: update release assets [skip ci]';
    const files: string[] = [];

    for (let i = 0; i < args.length; i++) {
        if (args[i] === '--files' && args[i + 1]) {
            files.push(...args[i + 1].split(',').map((f) => f.trim()));
            i++;
        } else if (args[i] === '--message' && args[i + 1]) {
            message = args[i + 1];
            i++;
        }
    }

    if (files.length === 0) {
        console.warn('No files specified via --files. Exiting.');
        process.exit(0);
    }

    await commitFiles({
        files,
        message,
        logger: console,
    });
}

