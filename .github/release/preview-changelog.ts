/**
 * Standalone tool to generate and preview release notes and changelogs locally.
 *
 * Demonstrates:
 * 1. CHANGELOG.md format: Conventional commit type groupings (Features, Bug Fixes, etc.)
 * 2. GitHub Releases format: Functional domain groupings with body sub-bullet expansion.
 *
 * Usage:
 *   bun preview-changelog.ts
 *   bun preview-changelog.ts --from v1.3.0-beta.2 --to v1.3.0-beta.3
 *   bun preview-changelog.ts --format github
 *   bun preview-changelog.ts --format changelog
 */
import process from 'node:process';
import { execFileSync } from 'node:child_process';
import { generateNotes } from '@semantic-release/release-notes-generator';
import {
    cleanRepoUrl,
    formatReleaseDate,
    generateGitHubReleaseNotes,
    resolveGitBinary,
    STANDARD_COMMIT_RANKS,
    STANDARD_RELEASE_RULES,
} from './changelog-domains.js';

function getLatestTag(): string {
    try {
        const gitBin = resolveGitBinary();
        return execFileSync(gitBin, ['describe', '--tags', '--abbrev=0'], { encoding: 'utf-8' }).trim();
    } catch {
        return 'HEAD~10';
    }
}

function getCommitDate(ref: string): string {
    try {
        const gitBin = resolveGitBinary();
        return execFileSync(gitBin, ['log', '-1', '--format=%cI', ref], { encoding: 'utf-8' }).trim();
    } catch {
        return new Date().toISOString();
    }
}

function getRepoUrl(): string {
    try {
        const gitBin = resolveGitBinary();
        const raw = execFileSync(gitBin, ['config', '--get', 'remote.origin.url'], { encoding: 'utf-8' });
        return cleanRepoUrl(raw);
    } catch {
        return 'https://github.com/Nicconike/LocalTelemetry';
    }
}

function getCommitsBetween(
    fromTag: string,
    toRef: string = 'HEAD',
): Array<{ hash: string; shortHash: string; message: string; committerDate?: string }> {
    const gitBin = resolveGitBinary();
    const raw = execFileSync(
        gitBin,
        ['log', `${fromTag}..${toRef}`, '--format=COMMIT_START%x00%H%x00%h%x00%cI%x00%B%x00COMMIT_END'],
        { encoding: 'utf-8' },
    );

    const commits: Array<{ hash: string; shortHash: string; message: string; committerDate?: string }> = [];
    const chunks = raw.split('COMMIT_START\0');

    for (const chunk of chunks) {
        if (!chunk.trim()) continue;
        const endIdx = chunk.indexOf('\0COMMIT_END');
        const content = endIdx >= 0 ? chunk.substring(0, endIdx) : chunk;
        const parts = content.split('\0');
        const hash = parts[0]?.trim();
        const shortHash = parts[1]?.trim() || hash?.substring(0, 7) || '';
        const committerDate = parts[2]?.trim();
        const message = parts.slice(3).join('\0').trim();

        if (hash && message) {
            commits.push({ hash, shortHash, message, committerDate });
        }
    }

    return commits;
}

async function main(): Promise<void> {
    const args = process.argv.slice(2);
    let fromTag = '';
    let toRef = 'HEAD';
    let format = 'all';

    for (let i = 0; i < args.length; i++) {
        if (args[i] === '--from' && args[i + 1]) {
            fromTag = args[++i];
        } else if (args[i] === '--to' && args[i + 1]) {
            toRef = args[++i];
        } else if ((args[i] === '--format' || args[i] === '--mode') && args[i + 1]) {
            format = args[++i].toLowerCase();
        }
    }

    if (!fromTag) {
        fromTag = getLatestTag();
    }

    console.log(`Generating preview notes: ${fromTag} -> ${toRef}\n`);

    const commits = getCommitsBetween(fromTag, toRef);
    if (commits.length === 0) {
        console.log(`No commits found between ${fromTag} and ${toRef}.`);
        return;
    }

    const version = toRef === 'HEAD' ? 'Unreleased' : toRef.replace(/^v/, '');
    const releaseDateIso = getCommitDate(toRef);
    const repoUrl = getRepoUrl();

    // Standard conventional configuration used for CHANGELOG.md
    const standardConfig = {
        preset: 'conventionalcommits',
        presetConfig: STANDARD_RELEASE_RULES,
        writerOpts: {
            commitGroupsSort: (a: { title?: string }, b: { title?: string }) =>
                (STANDARD_COMMIT_RANKS[b.title ?? ''] ?? 0) - (STANDARD_COMMIT_RANKS[a.title ?? ''] ?? 0),
            commitsSort: (a: { scope?: string; subject?: string }, b: { scope?: string; subject?: string }) => {
                const scopeCompare = (b.scope ?? '').localeCompare(a.scope ?? '');
                if (scopeCompare !== 0) return scopeCompare;
                return (b.subject ?? '').localeCompare(a.subject ?? '');
            },
            formatDate: formatReleaseDate,
            finalizeContext: (context: Record<string, unknown>) => {
                context.date = formatReleaseDate(releaseDateIso);
                return context;
            },
        },
    };

    const context = {
        commits,
        lastRelease: {
            version: fromTag.replace(/^v/, ''),
            gitTag: fromTag,
            gitHead: fromTag,
        },
        nextRelease: {
            version,
            gitTag: toRef,
            gitHead: toRef,
        },
        options: {
            repositoryUrl: repoUrl,
        },
        cwd: process.cwd(),
    };

    const domainNotes = generateGitHubReleaseNotes({
        fromTag,
        toRef,
        version,
        date: releaseDateIso,
        repositoryUrl: repoUrl,
        commits,
    });

    if (format === 'standard' || format === 'changelog') {
        const standardNotes = await generateNotes(standardConfig, context);
        console.log(standardNotes);
    } else if (format === 'domains' || format === 'github' || format === 'release') {
        console.log(domainNotes);
    } else {
        const standardNotes = await generateNotes(standardConfig, context);
        console.log('================================================================');
        console.log('1. CHANGELOG.md Format (Commit-based Conventional Types):');
        console.log('================================================================');
        console.log(standardNotes);
        console.log('================================================================');
        console.log('2. GitHub Releases Format (Functional Domains with Sub-Bullets):');
        console.log('================================================================');
        console.log(domainNotes);
    }
}

try {
    await main();
} catch (err) {
    console.error('Failed to preview changelog:', err);
    process.exit(1);
}
