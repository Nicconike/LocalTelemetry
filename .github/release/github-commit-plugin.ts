import fs from 'node:fs';
import path from 'node:path';
import process from 'node:process';
import { execFileSync } from 'node:child_process';
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

    logger.log(`Checking files for GitHub API release commit on ${owner}/${repo}:${branch}...`);

    const existingFiles = files.filter((file) => {
        const fullPath = path.resolve(cwd, file);
        return fs.existsSync(fullPath);
    });

    if (existingFiles.length === 0) {
        logger.warn('No files found to commit via GitHub API.');
        return;
    }

    if (isDryRun) {
        logger.log(`[DRY RUN] Would commit ${existingFiles.length} file(s) via GitHub REST API:`);
        for (const file of existingFiles) {
            logger.log(`  - ${file}`);
        }
        return;
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
        return;
    }

    // 4. Create commit (omit author/committer so GitHub signs with authenticated app)
    const commitMessage = `chore(release): ${nextRelease.version} [skip ci]`;
    const newCommit = await octokit.rest.git.createCommit({
        owner,
        repo,
        message: commitMessage,
        tree: newTree.data.sha,
        parents: [parentSha],
    });

    logger.log(`Created verified release commit via GitHub API: ${newCommit.data.sha}`);

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
        const message = err instanceof Error ? err.message : String(err);
        logger.warn(`Failed to sync local git working tree: ${message}`);
    }

    // 7. Point nextRelease.gitHead to the new commit so semantic-release tags it
    nextRelease.gitHead = newCommit.data.sha;
    logger.log(`Set nextRelease.gitHead to ${newCommit.data.sha}`);
}
