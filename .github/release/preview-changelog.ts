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
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
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
    } catch (err) {
        console.debug?.(`Failed to resolve latest tag, defaulting to HEAD~10: ${err}`);
        return 'HEAD~10';
    }
}

function getPreviousTag(tag: string): string {
    const gitBin = resolveGitBinary();
    try {
        return execFileSync(gitBin, ['describe', '--tags', '--abbrev=0', `${tag}^`], { encoding: 'utf-8' }).trim();
    } catch (err) {
        console.debug?.(`Failed to resolve previous tag for ${tag}, defaulting to HEAD~10: ${err}`);
        return 'HEAD~10';
    }
}

function resolveGhBinary(): string {
    if (process.platform === 'win32') {
        const progFiles = process.env.ProgramFiles ?? String.raw`C:\Program Files`;
        const candidates = [
            path.join(progFiles, 'GitHub CLI', 'gh.exe'),
            String.raw`C:\Program Files\GitHub CLI\gh.exe`,
        ];
        for (const candidate of candidates) {
            if (fs.existsSync(candidate)) return candidate;
        }
    }
    return 'gh';
}

function updateGitHubRelease(tag: string, notes: string): void {
    const tempFile = path.join(os.tmpdir(), `release-notes-${tag}-${Date.now()}.md`);
    fs.writeFileSync(tempFile, notes, 'utf-8');
    const ghBin = resolveGhBinary();
    try {
        console.log(`Updating GitHub Release ${tag} via ${ghBin}...`);
        execFileSync(ghBin, ['release', 'edit', tag, '--notes-file', tempFile], {
            stdio: 'inherit',
            env: process.env,
        });
        console.log(`Successfully updated GitHub Release ${tag} with domain-grouped release notes.`);
    } finally {
        try {
            if (fs.existsSync(tempFile)) {
                fs.unlinkSync(tempFile);
            }
        } catch (err) {
            console.warn(`Failed to clean temporary changelog file ${tempFile}:`, err);
        }
    }
}

function getCommitDate(ref: string): string {
    try {
        const gitBin = resolveGitBinary();
        return execFileSync(gitBin, ['log', '-1', '--format=%cI', ref], { encoding: 'utf-8' }).trim();
    } catch (err) {
        console.debug?.(`Failed to retrieve commit date for ref ${ref}: ${err}`);
        return new Date().toISOString();
    }
}

function getRepoUrl(): string {
    try {
        const gitBin = resolveGitBinary();
        const raw = execFileSync(gitBin, ['config', '--get', 'remote.origin.url'], { encoding: 'utf-8' });
        return cleanRepoUrl(raw);
    } catch (err) {
        console.debug?.(`Failed to retrieve git remote URL: ${err}`);
        return 'https://github.com/Nicconike/LocalTelemetry';
    }
}

function getCommitsBetween(
    fromTag: string,
    toRef: string = 'HEAD',
): Array<{ hash: string; shortHash: string; message: string; committerDate?: string; authorDate?: string }> {
    const gitBin = resolveGitBinary();
    const raw = execFileSync(
        gitBin,
        ['log', `${fromTag}..${toRef}`, '--format=COMMIT_START%x00%H%x00%h%x00%cI%x00%aI%x00%B%x00COMMIT_END'],
        { encoding: 'utf-8' },
    );

    const commits: Array<{ hash: string; shortHash: string; message: string; committerDate?: string; authorDate?: string }> = [];
    const chunks = raw.split('COMMIT_START\0');

    for (const chunk of chunks) {
        if (!chunk.trim()) continue;
        const endIdx = chunk.indexOf('\0COMMIT_END');
        const content = endIdx >= 0 ? chunk.substring(0, endIdx) : chunk;
        const parts = content.split('\0');
        const hash = parts[0]?.trim();
        const shortHash = parts[1]?.trim() || hash?.substring(0, 7) || '';
        const committerDate = parts[2]?.trim();
        const authorDate = parts[3]?.trim();
        const message = parts.slice(4).join('\0').trim();

        if (hash && message) {
            commits.push({ hash, shortHash, message, committerDate, authorDate });
        }
    }

    return commits;
}

interface CliOptions {
    fromTag: string;
    toRef: string;
    format: string;
    updateReleaseTag: string;
}

function parseCliArgs(args: string[]): CliOptions {
    let fromTag = '';
    let toRef = 'HEAD';
    let format = 'all';
    let updateReleaseTag = '';

    for (let i = 0; i < args.length; i++) {
        const arg = args[i];
        const next = args[i + 1];
        if (arg === '--from' && next) {
            fromTag = next;
            i++;
        } else if (arg === '--to' && next) {
            toRef = next;
            i++;
        } else if ((arg === '--format' || arg === '--mode') && next) {
            format = next.toLowerCase();
            i++;
        } else if (arg === '--update-release' && next) {
            updateReleaseTag = next;
            i++;
        }
    }

    if (updateReleaseTag) {
        toRef = updateReleaseTag;
        if (!fromTag) fromTag = getPreviousTag(toRef);
    } else if (!fromTag) {
        fromTag = getLatestTag();
    }

    return { fromTag, toRef, format, updateReleaseTag };
}

function createStandardConfig(releaseDateIso: string) {
    return {
        preset: 'conventionalcommits',
        presetConfig: STANDARD_RELEASE_RULES,
        writerOpts: {
            commitGroupsSort: (a: { title?: string }, b: { title?: string }) =>
                (STANDARD_COMMIT_RANKS[b.title ?? ''] ?? 0) - (STANDARD_COMMIT_RANKS[a.title ?? ''] ?? 0),
            commitsSort: (a: { scope?: string; subject?: string }, b: { scope?: string; subject?: string }) => {
                const scopeDiff = (b.scope ?? '').localeCompare(a.scope ?? '');
                return scopeDiff !== 0 ? scopeDiff : (b.subject ?? '').localeCompare(a.subject ?? '');
            },
            formatDate: formatReleaseDate,
            finalizeContext: (context: Record<string, unknown>) => {
                context.date = formatReleaseDate(releaseDateIso);
                return context;
            },
        },
    };
}

function displayReleaseNotes(format: string, standardNotes: string, domainNotes: string): void {
    if (format === 'standard' || format === 'changelog') {
        console.log(standardNotes);
        return;
    }
    if (format === 'domains' || format === 'github' || format === 'release') {
        console.log(domainNotes);
        return;
    }
    console.log('================================================================');
    console.log('1. CHANGELOG.md Format (Commit-based Conventional Types):');
    console.log('================================================================');
    console.log(standardNotes);
    console.log('================================================================');
    console.log('2. GitHub Releases Format (Functional Domains with Sub-Bullets):');
    console.log('================================================================');
    console.log(domainNotes);
}

async function main(): Promise<void> {
    const { fromTag, toRef, format, updateReleaseTag } = parseCliArgs(process.argv.slice(2));

    console.log(`Generating release notes: ${fromTag} -> ${toRef}\n`);

    const commits = getCommitsBetween(fromTag, toRef);
    if (commits.length === 0) {
        console.log(`No commits found between ${fromTag} and ${toRef}.`);
        return;
    }

    const version = toRef === 'HEAD' ? 'Unreleased' : toRef.replace(/^v/, '');
    const releaseDateIso = getCommitDate(toRef);
    const repoUrl = getRepoUrl();

    const domainNotes = generateGitHubReleaseNotes({
        fromTag,
        toRef,
        version,
        date: releaseDateIso,
        repositoryUrl: repoUrl,
        commits,
    });

    if (updateReleaseTag) {
        if (!domainNotes.trim()) {
            console.warn(`No domain release notes generated for ${updateReleaseTag}.`);
            return;
        }
        updateGitHubRelease(updateReleaseTag, domainNotes);
        return;
    }

    const standardConfig = createStandardConfig(releaseDateIso);
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

    const standardNotes = await generateNotes(standardConfig, context);
    displayReleaseNotes(format, standardNotes, domainNotes);
}

try {
    await main();
} catch (err) {
    console.error('Failed to preview changelog:', err);
    process.exit(1);
}
