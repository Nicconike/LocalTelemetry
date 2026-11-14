/**
 * Standalone tool to generate and preview release notes and changelogs locally
 * grouped into clean functional domains.
 *
 * Usage:
 *   bun run changelog
 *   bun run changelog --from v1.2.0 --to HEAD
 */
import { execSync } from 'node:child_process';
import { generateNotes } from '@semantic-release/release-notes-generator';
import {
    compareCommitGroups,
    createChangelogTransform,
    formatReleaseDate,
    STANDARD_COMMIT_RANKS,
    STANDARD_RELEASE_RULES,
} from './changelog-domains.js';

function getLatestTag(): string {
    try {
        return execSync('git describe --tags --abbrev=0', { encoding: 'utf-8' }).trim();
    } catch {
        return 'HEAD~10';
    }
}

function getCommitDate(ref: string): string {
    try {
        return execSync(`git log -1 --format=%cI ${ref}`, { encoding: 'utf-8' }).trim();
    } catch {
        return new Date().toISOString();
    }
}

function getRepoUrl(): string {
    try {
        const url = execSync('git config --get remote.origin.url', { encoding: 'utf-8' }).trim();
        return url.replace(/\.git$/i, '');
    } catch {
        return 'https://github.com/Nicconike/LocalTelemetry';
    }
}

function getCommitsBetween(
    fromTag: string,
    toRef: string = 'HEAD',
): Array<{ message: string; hash: string; committerDate?: string }> {
    const raw = execSync(`git log ${fromTag}..${toRef} --format="COMMIT_START%n%H%n%cI%n%B%nCOMMIT_END"`, {
        encoding: 'utf-8',
    });

    const commits: Array<{ message: string; hash: string; committerDate?: string }> = [];
    const chunks = raw.split('COMMIT_START\n');

    for (const chunk of chunks) {
        if (!chunk.trim()) continue;
        const endIdx = chunk.indexOf('\nCOMMIT_END');
        const content = endIdx >= 0 ? chunk.substring(0, endIdx) : chunk;
        const lines = content.split('\n');
        const hash = lines[0]?.trim();
        const committerDate = lines[1]?.trim();
        const message = lines.slice(2).join('\n').trim();

        if (hash && message) {
            commits.push({ hash, message, committerDate });
        }
    }

    return commits;
}

async function main() {
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

    const domainConfig = {
        preset: 'conventionalcommits',
        writerOpts: {
            transform: createChangelogTransform(),
            commitGroupsSort: compareCommitGroups,
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

    if (format === 'standard' || format === 'changelog') {
        const standardNotes = await generateNotes(standardConfig, context);
        console.log(standardNotes);
    } else if (format === 'domains' || format === 'github' || format === 'release') {
        const domainNotes = await generateNotes(domainConfig, context);
        console.log(domainNotes);
    } else {
        const [standardNotes, domainNotes] = await Promise.all([
            generateNotes(standardConfig, context),
            generateNotes(domainConfig, context),
        ]);
        console.log('================================================================');
        console.log('1. CHANGELOG.md Format (Commit-based Conventional Types):');
        console.log('================================================================');
        console.log(standardNotes);
        console.log('================================================================');
        console.log('2. GitHub Releases Format (Functional Domains):');
        console.log('================================================================');
        console.log(domainNotes);
    }
}

main().catch((err) => {
    console.error('Failed to preview changelog:', err);
    process.exit(1);
});
