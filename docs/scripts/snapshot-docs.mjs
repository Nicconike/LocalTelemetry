#!/usr/bin/env node
/**
 * Freeze a released version of the docs into docs/v<version>/ and register it
 * in docs/.vitepress/versions.mjs so it appears in the docs version dropdown.
 *
 * Usage:
 *   node scripts/snapshot-docs.mjs <version> [--force] [--dry-run]
 *   node scripts/snapshot-docs.mjs --remove <version> [--force] [--dry-run]
 *
 * Examples:
 *   node scripts/snapshot-docs.mjs 1.0.0-rc.1
 *   node scripts/snapshot-docs.mjs v1.0.0-rc.1
 *   node scripts/snapshot-docs.mjs --remove 1.0.0-rc.1
 */
import { execFileSync } from 'node:child_process';
import { existsSync, mkdirSync, readFileSync, writeFileSync, readdirSync, rmSync, statSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const root = resolve(__dirname, '../..');
const docsDir = join(root, 'docs');
const activeDir = join(docsDir, 'latest');
const versionsFile = join(docsDir, '.vitepress', 'versions.mjs');
const latestLink = '/';

const CONTENT_ROOT = ['index.md', 'user-guide', 'developer-guide'];

const gitExecutable = (() => {
    if (process.platform === 'win32') {
        const candidate = join(process.env.ProgramFiles ?? String.raw`C:\Program Files`, 'Git', 'cmd', 'git.exe');
        if (existsSync(candidate)) return candidate;
    }
    return 'git';
})();

function parseArgs() {
    const args = process.argv.slice(2);
    const flags = { force: false, dryRun: false, remove: false, fromLatest: false };
    const positional = [];
    for (const arg of args) {
        if (arg === '--force') flags.force = true;
        else if (arg === '--dry-run') flags.dryRun = true;
        else if (arg === '--remove') flags.remove = true;
        else if (arg === '--from-latest') flags.fromLatest = true;
        else positional.push(arg);
    }
    if (positional.length !== 1) {
        console.error('Usage: node scripts/snapshot-docs.mjs <version> [--force] [--dry-run]');
        console.error('       node scripts/snapshot-docs.mjs --remove <version> [--force] [--dry-run]');
        process.exit(1);
    }
    let version = positional[0];
    if (!version.startsWith('v')) version = `v${version}`;
    return { version, ...flags };
}

function runGit(args) {
    return execFileSync(gitExecutable, args, { cwd: root, encoding: 'utf-8', stdio: ['ignore', 'pipe', 'ignore'] });
}

function tagExists(version) {
    return runGit(['tag', '-l', version]).trim() === version;
}

function* walkMarkdown(dir) {
    for (const name of readdirSync(dir)) {
        const abs = join(dir, name);
        const stat = statSync(abs);
        if (stat.isDirectory()) {
            if (name.startsWith('v') || name === 'node_modules' || name === '.vitepress' || name === 'public') continue;
            yield* walkMarkdown(abs);
        } else if (name.endsWith('.md')) {
            yield name;
        }
    }
}

/**
 * Collect { relPath, content } for every content markdown page.
 */
function collectSource(version, useTag) {
    const out = [];
    if (useTag) {
        // Detect layout: new tags store docs at docs/latest/, legacy tags at docs/ root.
        const hasLatestDir = runGit(['ls-tree', '-r', '--name-only', version, '--', 'docs/latest']).trim() !== '';
        const baseDir = hasLatestDir ? 'docs/latest' : 'docs';
        const files = runGit(['ls-tree', '-r', '--name-only', version, '--', baseDir])
            .split('\n')
            .map((line) => line.trim())
            .filter(Boolean)
            .filter((file) => file.startsWith(`${baseDir}/`) && file.endsWith('.md'))
            .filter((file) => {
                const rel = file.slice(baseDir.length + 1);
                return CONTENT_ROOT.some((entry) => rel === entry || rel.startsWith(`${entry}/`));
            });
        for (const file of files) {
            const rel = file.slice(baseDir.length + 1);
            out.push({ relPath: rel, content: runGit(['show', `${version}:${file}`]) });
        }
        return out;
    }
    for (const entry of CONTENT_ROOT) {
        const abs = join(activeDir, entry);
        if (!existsSync(abs)) continue;
        if (statSync(abs).isDirectory()) {
            for (const rel of walkMarkdown(abs)) {
                out.push({ relPath: join(entry, rel).replaceAll('\\', '/'), content: readFileSync(join(abs, rel), 'utf-8') });
            }
        } else {
            out.push({ relPath: entry, content: readFileSync(abs, 'utf-8') });
        }
    }
    return out;
}

// Matches absolute links to the active docs
const ABS_DOC_LINK = /(?<![:/])\/(?:user-guide|developer-guide)(?:\/|$)/g;

/**
 * Add `editLink: false` to the frontmatter.
 */
function injectEditLinkFalse(content) {
    const fmMatch = /^---\r?\n([\s\S]*?)\r?\n---(?:\r?\n|$)/.exec(content);
    if (fmMatch) {
        const frontmatter = fmMatch[1];
        if (/^editLink:/m.test(frontmatter)) {
            return content;
        }
        const updatedFrontmatter = `---\neditLink: false\n${frontmatter}\n---\n`;
        return `${updatedFrontmatter}${content.slice(fmMatch[0].length)}`;
    }
    return `---\neditLink: false\n---\n\n${content}`;
}

function rewriteContent(content, version) {
    const versionLabel = version.replace(/^v/, '');
    const updated = content.replace(ABS_DOC_LINK, (match) => `/v${versionLabel}${match}`);
    return injectEditLinkFalse(updated);
}

function parseVersionsFile() {
    if (!existsSync(versionsFile)) return [];
    const text = readFileSync(versionsFile, 'utf-8');
    return [...text.matchAll(/\{\s*text:\s*['"]([^'"]+)['"]\s*,\s*link:\s*['"]([^'"]+)['"]\s*\}/g)].map(
        (m) => ({ text: m[1], link: m[2] })
    );
}

function serializeVersions(entries) {
    const lines = [
        '// Auto-generated by scripts/snapshot-docs.mjs. Do not edit by hand.',
        '// Each entry maps a dropdown label to the site path for that docs version.',
        'export const versions = [',
        `    { text: 'Latest', link: '${latestLink}' },`,
        ...entries
            .filter((entry) => entry.link !== latestLink)
            .map((entry) => `    { text: '${entry.text}', link: '${entry.link}' },`),
        '];',
        ''
    ];
    return lines.join('\n');
}

function removeVersion(version, { force, dryRun }) {
    const versionLabel = version.replace(/^v/, '');
    const targetDir = join(docsDir, `v${versionLabel}`);
    const exists = existsSync(targetDir);

    if (!exists && !force) {
        console.error(`No snapshot found at docs/v${versionLabel}/ (use --force to unregister anyway).`);
        process.exit(1);
    }

    const entries = parseVersionsFile();
    const link = `/v${versionLabel}/`;
    const registered = entries.some((entry) => entry.link === link);

    if (!exists && !registered) {
        console.error(`Nothing to remove: docs/v${versionLabel}/ does not exist and is not registered in versions.mjs`);
        process.exit(1);
    }

    console.log(`Removing docs/v${versionLabel}/ and unregistering ${version} from versions.mjs`);

    if (dryRun) {
        console.log(`[dry-run] Would delete docs/v${versionLabel}/`);
        console.log('[dry-run] Would update docs/.vitepress/versions.mjs');
    } else {
        if (exists) {
            rmSync(targetDir, { recursive: true, force: true });
        }
        if (registered) {
            const remaining = entries.filter((entry) => entry.link !== link);
            writeFileSync(versionsFile, serializeVersions(remaining));
            console.log(`Unregistered ${version} from docs/.vitepress/versions.mjs`);
        } else {
            console.log('Version was not registered in versions.mjs');
        }
        console.log(`Done: docs/v${versionLabel}/ removed`);
    }
}

function writeSnapshotFiles(source, targetDir, version, dryRun) {
    let changed = 0;
    let rewritten = 0;
    let editLinkAdded = 0;

    for (const { relPath, content } of source) {
        const outPath = join(targetDir, relPath);
        const linkMatches = [...content.matchAll(ABS_DOC_LINK)].length;
        if (linkMatches > 0) rewritten += linkMatches;

        const updated = rewriteContent(content, version);
        if (updated !== content) {
            const hadEditLink = /^---\r?\n[\s\S]*?^editLink:/m.test(content);
            if (!hadEditLink) editLinkAdded++;
        }

        if (dryRun) {
            console.log(`  [dry-run] ${relPath}`);
        } else {
            mkdirSync(dirname(outPath), { recursive: true });
            writeFileSync(outPath, updated);
        }
        changed++;
    }

    return { changed, rewritten, editLinkAdded };
}

function registerVersion(version, versionLabel, dryRun) {
    if (dryRun) {
        console.log('[dry-run] Would update docs/.vitepress/versions.mjs');
        return;
    }
    const entries = parseVersionsFile();
    const link = `/v${versionLabel}/`;
    if (!entries.some((entry) => entry.link === link)) {
        entries.push({ text: version, link });
        writeFileSync(versionsFile, serializeVersions(entries));
        console.log(`Registered ${version} in docs/.vitepress/versions.mjs`);
    } else {
        console.log('Version already registered in versions.mjs');
    }
}

function main() {
    const { version, force, dryRun, remove, fromLatest } = parseArgs();

    if (remove) {
        removeVersion(version, { force, dryRun });
        return;
    }

    const versionLabel = version.replace(/^v/, '');
    const targetDir = join(docsDir, `v${versionLabel}`);

    if (existsSync(targetDir) && !force) {
        console.error(`Snapshot already exists at docs/v${versionLabel}/ (use --force to rebuild).`);
        process.exit(1);
    }

    const useTag = fromLatest ? false : tagExists(version);
    const source = collectSource(version, useTag);
    if (source.length === 0) {
        const sourceLabel = useTag ? `tag ${version}` : 'docs/latest/';
        console.error(`No content pages found for ${sourceLabel}.`);
        process.exit(1);
    }

    const sourceDesc = useTag ? `git tag ${version}` : 'working tree docs/latest/';
    console.log(`Source: ${sourceDesc}`);
    console.log(`Target: docs/v${versionLabel}/ (${source.length} pages)`);

    const { changed, rewritten, editLinkAdded } = writeSnapshotFiles(source, targetDir, version, dryRun);
    registerVersion(version, versionLabel, dryRun);

    console.log(
        `Done: ${changed} file(s) written, ${rewritten} link(s) rewritten, ${editLinkAdded} page(s) got editLink: false`
    );
    console.log(`(git) Add docs/v${versionLabel}/ to .gitattributes with: docs/v*/ linguist-generated`);
}

main();
