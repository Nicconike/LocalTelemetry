#!/usr/bin/env node
/**
 * Automatically extracts immutable documentation from Git release tags
 * starting from v1.0.0-rc.1 and newer, preparing versioned folders
 * and updating docs/.vitepress/versions.mjs before VitePress builds.
 */
import { execFileSync } from 'node:child_process';
import { existsSync, mkdirSync, readFileSync, writeFileSync, rmSync, readdirSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const root = resolve(__dirname, '../..');
const docsDir = join(root, 'docs');
const versionsFile = join(docsDir, '.vitepress', 'versions.mjs');
const CONTENT_ROOT = ['index.md', 'about', 'user-guide', 'developer-guide'];
const ABS_DOC_LINK = /(?<![:/.])\/(?:about|user-guide|developer-guide)(?:\/|$)/g;

// Only tags >= v1.0.0-rc.1 are included in versioned documentation
const MIN_VERSION = '1.0.0-rc.1';

const gitExecutable = (() => {
    if (process.platform === 'win32') {
        const candidate = join(process.env.ProgramFiles ?? String.raw`C:\Program Files`, 'Git', 'cmd', 'git.exe');
        if (existsSync(candidate)) return candidate;
    }
    return 'git';
})();

function runGit(args) {
    try {
        return execFileSync(gitExecutable, args, { cwd: root, encoding: 'utf-8', stdio: ['ignore', 'pipe', 'ignore'] }).trim();
    } catch {
        return '';
    }
}

/**
 * Parse a semver string into comparable parts without backtracking regexes.
 */
function parseSemVer(v) {
    const clean = v.startsWith('v') ? v.slice(1) : v;
    const [versionPart, prereleasePart] = clean.split('-', 2);
    const numParts = versionPart.split('.');
    if (numParts.length !== 3) return null;

    const major = Number.parseInt(numParts[0], 10);
    const minor = Number.parseInt(numParts[1], 10);
    const patch = Number.parseInt(numParts[2], 10);
    if (Number.isNaN(major) || Number.isNaN(minor) || Number.isNaN(patch)) return null;

    let prereleaseType = null;
    let prereleaseNum = 0;
    if (prereleasePart) {
        const preMatch = /^([a-zA-Z]+)\.?(\d+)?$/.exec(prereleasePart);
        if (preMatch) {
            prereleaseType = preMatch[1];
            prereleaseNum = preMatch[2] ? Number.parseInt(preMatch[2], 10) : 0;
        }
    }

    return { major, minor, patch, prereleaseType, prereleaseNum, raw: clean };
}

function compareSemVer(a, b) {
    const pa = parseSemVer(a);
    const pb = parseSemVer(b);
    if (!pa || !pb) return 0;

    if (pa.major !== pb.major) return pa.major - pb.major;
    if (pa.minor !== pb.minor) return pa.minor - pb.minor;
    if (pa.patch !== pb.patch) return pa.patch - pb.patch;

    // Both are release or both are prerelease
    if (!pa.prereleaseType && pb.prereleaseType) return 1;
    if (pa.prereleaseType && !pb.prereleaseType) return -1;

    if (pa.prereleaseType && pb.prereleaseType) {
        if (pa.prereleaseType !== pb.prereleaseType) {
            return pa.prereleaseType.localeCompare(pb.prereleaseType);
        }
        return pa.prereleaseNum - pb.prereleaseNum;
    }

    return 0;
}

function getValidReleaseTags() {
    const tagOutput = runGit(['tag', '-l', 'v*']);
    if (!tagOutput) return [];

    const rawTags = tagOutput
        .split('\n')
        .map((t) => t.trim())
        .filter(Boolean);

    const uniqueTags = [...new Set(rawTags)];

    return uniqueTags
        .filter((t) => {
            const parsed = parseSemVer(t);
            if (!parsed) return false;
            return compareSemVer(t, MIN_VERSION) >= 0;
        })
        .sort((a, b) => compareSemVer(b, a)); // Newest first
}

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

function extractDocsFromWorkingTree(versionLabel) {
    const targetDir = join(docsDir, `v${versionLabel}`);
    if (existsSync(targetDir)) {
        rmSync(targetDir, { recursive: true, force: true });
    }

    const sourceBase = existsSync(join(docsDir, 'latest')) ? join(docsDir, 'latest') : docsDir;

    function walkDir(dir, relPrefix = '') {
        if (!existsSync(dir)) return;
        const entries = readdirSync(dir, { withFileTypes: true });
        for (const entry of entries) {
            const relPath = relPrefix ? `${relPrefix}/${entry.name}` : entry.name;
            const fullPath = join(dir, entry.name);
            if (entry.isDirectory()) {
                if (CONTENT_ROOT.some((entryRoot) => entryRoot === relPath || relPath.startsWith(`${entryRoot}/`))) {
                    walkDir(fullPath, relPath);
                }
            } else if (entry.isFile() && entry.name.endsWith('.md')) {
                if (CONTENT_ROOT.some((entryRoot) => entryRoot === relPath || relPath.startsWith(`${entryRoot}/`))) {
                    const rawContent = readFileSync(fullPath, 'utf-8');
                    const withRewrittenLinks = rawContent.replace(ABS_DOC_LINK, (match) => `/v${versionLabel}${match}`);
                    const finalContent = injectEditLinkFalse(withRewrittenLinks);

                    const outPath = join(targetDir, relPath);
                    mkdirSync(dirname(outPath), { recursive: true });
                    writeFileSync(outPath, finalContent);
                }
            }
        }
    }

    walkDir(sourceBase);
    return existsSync(join(targetDir, 'index.md'));
}

function extractDocsFromTag(tag, force = false) {
    const versionLabel = tag.replace(/^v/, '');
    const targetDir = join(docsDir, `v${versionLabel}`);

    // If already processed and not forced, skip extraction completely
    if (!force && existsSync(targetDir) && existsSync(join(targetDir, 'index.md'))) {
        console.log(`[build-versions] Version ${tag} is already processed at docs/v${versionLabel}/; skipping.`);
        return true;
    }

    console.log(`[build-versions] New version release detected: extracting docs from ${tag}...`);

    // Detect docs structure inside the tag
    const hasLatestDir = runGit(['ls-tree', '-r', '--name-only', tag, '--', 'docs/latest']) !== '';
    const baseDir = hasLatestDir ? 'docs/latest' : 'docs';

    const treeOutput = runGit(['ls-tree', '-r', '--name-only', tag, '--', baseDir]);
    if (!treeOutput) {
        console.log(`[build-versions] Tag ${tag} not found in git tree, extracting from current docs tree...`);
        return extractDocsFromWorkingTree(versionLabel);
    }

    const files = treeOutput
        .split('\n')
        .map((l) => l.trim())
        .filter(Boolean)
        .filter((f) => f.startsWith(`${baseDir}/`) && f.endsWith('.md'))
        .filter((f) => {
            const rel = f.slice(baseDir.length + 1);
            return CONTENT_ROOT.some((entry) => rel === entry || rel.startsWith(`${entry}/`));
        });

    if (files.length === 0) return false;

    // Clean previous directory if exists
    if (existsSync(targetDir)) {
        rmSync(targetDir, { recursive: true, force: true });
    }

    for (const file of files) {
        const relPath = file.slice(baseDir.length + 1);
        const rawContent = runGit(['show', `${tag}:${file}`]);
        if (!rawContent) continue;

        // Rewrite internal links to /v<versionLabel>/
        const withRewrittenLinks = rawContent.replace(ABS_DOC_LINK, (match) => `/v${versionLabel}${match}`);
        const finalContent = injectEditLinkFalse(withRewrittenLinks);

        const outPath = join(targetDir, relPath);
        mkdirSync(dirname(outPath), { recursive: true });
        writeFileSync(outPath, finalContent);
    }

    return true;
}

function updateVersionsFile(validTags) {
    const seenLinks = new Set();
    const uniqueEntries = [];

    for (const tag of validTags) {
        const label = tag.startsWith('v') ? tag : `v${tag}`;
        const ver = tag.replace(/^v/, '');
        const link = `/v${ver}/`;
        if (!seenLinks.has(link)) {
            seenLinks.add(link);
            uniqueEntries.push({ label, link });
        }
    }

    let latestReleaseTag = null;
    if (validTags[0]) {
        latestReleaseTag = validTags[0].startsWith('v') ? validTags[0] : `v${validTags[0]}`;
    }
    const tagLiteral = latestReleaseTag !== null ? `'${latestReleaseTag}'` : 'null';

    const newContent = [
        '// Auto-generated by scripts/build-versions.mjs. Do not edit by hand.',
        '// Each entry maps a dropdown label to the site path for that docs version.',
        `export const latestReleaseTag = ${tagLiteral};`,
        'export const versions = [',
        `    { text: 'Latest', link: '/' },`,
        ...uniqueEntries.map((e) => `    { text: '${e.label}', link: '${e.link}' },`),
        '];',
        ''
    ].join('\n');

    if (existsSync(versionsFile)) {
        const currentContent = readFileSync(versionsFile, 'utf-8');
        if (currentContent === newContent) {
            return;
        }
    }
    writeFileSync(versionsFile, newContent);
}

function main() {
    const args = process.argv.slice(2);
    const force = args.includes('--force');
    const tagArgIndex = args.indexOf('--tag');
    const extraTag = tagArgIndex !== -1 && args[tagArgIndex + 1] ? args[tagArgIndex + 1] : process.env.RELEASE_TAG;

    console.log(`[build-versions] Scanning for release tags >= v${MIN_VERSION}...`);
    const validTags = getValidReleaseTags();

    if (extraTag) {
        const normalizedTag = extraTag.startsWith('v') ? extraTag : `v${extraTag}`;
        if (compareSemVer(normalizedTag, MIN_VERSION) >= 0 && !validTags.includes(normalizedTag)) {
            validTags.push(normalizedTag);
            validTags.sort((a, b) => compareSemVer(b, a));
        }
    }

    if (validTags.length === 0) {
        console.log(`[build-versions] No release tags >= v${MIN_VERSION} found yet. Serving Latest docs only.`);
        updateVersionsFile([]);
        return;
    }

    console.log(`[build-versions] Found ${validTags.length} valid tag(s): ${validTags.join(', ')}`);

    const successfullyExtracted = [];
    for (const tag of validTags) {
        const ok = extractDocsFromTag(tag, force);
        if (ok) {
            successfullyExtracted.push(tag);
        } else {
            console.warn(`[build-versions] Warning: Could not extract docs from tag ${tag}`);
        }
    }

    updateVersionsFile(successfullyExtracted);
    console.log(`[build-versions] Version check complete. Ready for VitePress.`);
}

main();
