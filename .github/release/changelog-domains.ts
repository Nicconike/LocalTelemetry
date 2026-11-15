import fs from 'node:fs';
import process from 'node:process';
import { execFileSync } from 'node:child_process';

/**
 * Functional domain categorization and release notes generator for GitHub Releases. *
 * CHANGELOG.md remains in standard Conventional Commits format, while GitHub Releases
 * exclusively receive domain-categorized entries with sub-bullet expansion.
 */

export const DOMAINS = {
    OVERLAY: 'Taskbar Overlay',
    LAYOUT: 'Layout Presets & Metrics',
    HARDWARE: 'Hardware Telemetry & Sensors',
    TRAFFIC: 'Network & Traffic Monitoring',
    ALERTS: 'Alerts & Notifications',
    THEME: 'Appearance & Theming',
    LIFECYCLE: 'General & Application Lifecycle',
    INSTALLER: 'Installation & Packaging',
    BUILD: 'Build System & Dependencies',
    CI: 'Continuous Integration & Workflows',
    TESTS: 'Tests & Test Coverage',
    DOCS: 'Documentation',
} as const;

export const DOMAIN_RANKS: Record<string, number> = {
    [DOMAINS.OVERLAY]: 120,
    [DOMAINS.LAYOUT]: 110,
    [DOMAINS.HARDWARE]: 100,
    [DOMAINS.TRAFFIC]: 90,
    [DOMAINS.ALERTS]: 80,
    [DOMAINS.THEME]: 70,
    [DOMAINS.LIFECYCLE]: 60,
    [DOMAINS.INSTALLER]: 50,
    [DOMAINS.BUILD]: 40,
    [DOMAINS.CI]: 30,
    [DOMAINS.TESTS]: 20,
    [DOMAINS.DOCS]: 10,
};

export const STANDARD_RELEASE_RULES = {
    types: [
        { type: 'feat', section: 'Features' },
        { type: 'fix', section: 'Bug Fixes' },
        { type: 'perf', section: 'Performance Improvements' },
        { type: 'revert', section: 'Reverts' },
        { type: 'refactor', section: 'Code Refactoring' },
        { type: 'build', section: 'Build System' },
        { type: 'ci', section: 'Continuous Integration' },
        { type: 'test', section: 'Tests' },
        { type: 'style', section: 'Styles' },
        { type: 'docs', section: 'Documentation' },
        { type: 'chore', hidden: true },
    ],
};

export const STANDARD_COMMIT_RANKS: Record<string, number> = {
    Features: 12,
    'Bug Fixes': 11,
    'Performance Improvements': 10,
    Reverts: 9,
    'Code Refactoring': 8,
    'Build System': 7,
    'Continuous Integration': 6,
    Tests: 5,
    Styles: 4,
    Documentation: 3,
};

const SCOPE_DOMAIN_RULES: Array<{ keywords: string[]; domain: string }> = [
    {
        keywords: ['overlay', 'embedder', 'taskbar', 'band', 'mousehook', 'placement', 'tray'],
        domain: DOMAINS.OVERLAY,
    },
    {
        keywords: ['layout', 'preset', 'presets', 'metric', 'metrics', 'column', 'columns'],
        domain: DOMAINS.LAYOUT,
    },
    {
        keywords: [
            'hardware', 'sensor', 'sensors', 'disk', 'disks', 'nvme', 'sata', 'scsi',
            'smn', 'pawnio', 'battery', 'gpu', 'cpu', 'adl', 'nvml', 'wddm', 'igcl',
            'inventory', 'thermal', 'temp',
        ],
        domain: DOMAINS.HARDWARE,
    },
    {
        keywords: ['traffic', 'network', 'srum', 'netusage', 'bandwidth', 'importer', 'history'],
        domain: DOMAINS.TRAFFIC,
    },
    {
        keywords: ['alerts', 'alert', 'notifier', 'notification', 'notifications', 'hysteresis', 'toast'],
        domain: DOMAINS.ALERTS,
    },
    {
        keywords: ['appearance', 'theme', 'themes', 'color', 'colors', 'palette', 'accent', 'contrast', 'styling'],
        domain: DOMAINS.THEME,
    },
    {
        keywords: [
            'app', 'core', 'general', 'lifecycle', 'settings', 'diagnostics',
            'logging', 'log', 'logs', 'mutex', 'single-instance', 'portable', 'bridge',
        ],
        domain: DOMAINS.LIFECYCLE,
    },
    {
        keywords: ['installer', 'setup', 'packaging', 'package', 'winget', 'inno', 'uninstaller', 'issrc'],
        domain: DOMAINS.INSTALLER,
    },
    {
        keywords: ['build', 'deps', 'dependabot', 'compiler', 'bundler', 'packages', 'props'],
        domain: DOMAINS.BUILD,
    },
    {
        keywords: ['ci', 'workflows', 'workflow', 'sonar', 'codecov', 'codeql', 'security', 'scorecard'],
        domain: DOMAINS.CI,
    },
    {
        keywords: ['test', 'tests', 'coverage', 'unit-test', 'vitest', 'xunit'],
        domain: DOMAINS.TESTS,
    },
    {
        keywords: ['docs', 'readme', 'documentation', 'guide', 'guides', 'manual'],
        domain: DOMAINS.DOCS,
    },
];

const KNOWN_SCOPE_PREFIXES = new Set([
    'overlay', 'taskbar', 'embedder', 'layout', 'preset', 'presets', 'metric', 'metrics',
    'hardware', 'sensor', 'sensors', 'disk', 'nvme', 'sata', 'gpu', 'cpu', 'battery', 'pawnio',
    'traffic', 'network', 'srum', 'alerts', 'alert', 'notifier', 'appearance', 'theme', 'themes',
    'app', 'core', 'general', 'settings', 'lifecycle', 'diagnostics', 'installer', 'setup',
    'packaging', 'ci', 'workflows', 'build', 'deps', 'sonar', 'quality', 'test', 'tests', 'docs',
    'guides', 'bridge',
]);

export function resolveGitBinary(): string {
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

function textContainsKeyword(text: string, keywords: string[]): boolean {
    if (!text) return false;
    const tokenSet = new Set(text.toLowerCase().split(/[^a-z0-9_-]+/));
    return keywords.some((kw) => tokenSet.has(kw));
}

function findDomainByKeywords(text?: string): string | null {
    if (!text) return null;
    for (const rule of SCOPE_DOMAIN_RULES) {
        if (textContainsKeyword(text, rule.keywords)) {
            return rule.domain;
        }
    }
    return null;
}

function isReleaseChore(type: string, scope: string, subject: string): boolean {
    if (scope !== 'release') {
        return false;
    }
    return type === 'chore' || /^\d+\.\d+\.\d+/.test(subject);
}

const ENGINEERING_TYPE_DOMAINS: Readonly<Record<string, string>> = {
    test: DOMAINS.TESTS,
    build: DOMAINS.BUILD,
    ci: DOMAINS.CI,
    docs: DOMAINS.DOCS,
};

/**
 * Normalizes repository URL from SSH or HTTPS into standard web HTTPS URL.
 */
export function cleanRepoUrl(rawUrl?: string | null): string {
    let url = (rawUrl ?? '').trim();
    url = url.replace(/^git@github\.com:/i, 'https://github.com/');
    url = url.replace(/\.git$/i, '');
    if (!url.startsWith('http://') && !url.startsWith('https://')) {
        url = 'https://github.com/Nicconike/LocalTelemetry';
    }
    return url;
}

/**
 * Determines the target functional domain for a commit or bullet entry.
 * Returns null if the entry should be omitted from release notes (e.g. routine chores).
 */
export function resolveDomain(
    type?: string | null,
    scope?: string | null,
    subject?: string | null,
): string | null {
    const rawType = (type ?? '').toLowerCase().trim();
    const rawScope = (scope ?? '').toLowerCase().trim();
    const rawSubject = (subject ?? '').toLowerCase().trim();

    if (isReleaseChore(rawType, rawScope, rawSubject)) {
        return null;
    }

    const engineeringDomain = ENGINEERING_TYPE_DOMAINS[rawType];
    if (engineeringDomain) {
        return engineeringDomain;
    }

    const scopeDomain = findDomainByKeywords(rawScope);
    if (scopeDomain) {
        return scopeDomain;
    }

    if (rawType === 'chore') {
        return null;
    }

    const subjectDomain = findDomainByKeywords(rawSubject);
    if (subjectDomain) {
        return subjectDomain;
    }

    if (rawType === 'style') {
        return null;
    }

    return DOMAINS.LIFECYCLE;
}

/**
 * Normalizes subject text into ASD-STE100 active-voice sentence format.
 * Strips redundant leading scope prefixes inside domain sections.
 */
export function cleanSubject(subject: string): string {
    let clean = (subject ?? '').trim();

    // Standardize Dependabot bump strings
    const dependabotMatch = /^bump the ([\w-]+) group across \d+ director\w+ with \d+ updates?$/i.exec(clean);
    if (dependabotMatch) {
        const groupName = dependabotMatch[1].replace(/-deps$/, '').replaceAll('-', ' ');
        return `Bump ${groupName} dependencies`;
    }

    // Strip redundant leading scope prefix using the known scope set
    const colonIdx = clean.indexOf(':');
    if (colonIdx > 0 && colonIdx < 30) {
        const potentialScope = clean.substring(0, colonIdx).trim().toLowerCase();
        if (KNOWN_SCOPE_PREFIXES.has(potentialScope)) {
            clean = clean.substring(colonIdx + 1).trim();
        }
    }

    if (!clean) return clean;

    // Capitalize first character
    clean = clean.charAt(0).toUpperCase() + clean.slice(1);

    // Remove trailing period if present for markdown list consistency
    clean = clean.replace(/\.$/, '');

    return clean;
}

/**
 * Formats a date object, ISO string, or timestamp into DD-MM-YYYY format.
 */
export function formatReleaseDate(rawDate?: string | Date | number | null): string {
    if (!rawDate) return '';
    if (typeof rawDate === 'string' && /^\d{2}-\d{2}-\d{4}$/.test(rawDate)) {
        return rawDate;
    }
    const d = new Date(rawDate);
    if (Number.isNaN(d.getTime())) return String(rawDate);
    const day = String(d.getUTCDate()).padStart(2, '0');
    const month = String(d.getUTCMonth() + 1).padStart(2, '0');
    const year = d.getUTCFullYear();
    return `${day}-${month}-${year}`;
}

export interface DomainEntry {
    domain: string;
    subject: string;
    hash: string;
    shortHash: string;
}

const BULLET_REGEX = /^[-*]\s+([a-z]+)(?:\(([^)]+)\))?:\s*(.+)$/i;
const HEADER_REGEX = /^([a-z]+)(?:\(([^)]+)\))?:\s*(.+)$/i;

function extractSubBullets(
    bodyLines: string[],
    fullHash: string,
    shortHash: string,
): DomainEntry[] {
    const entries: DomainEntry[] = [];
    for (const rawLine of bodyLines) {
        const line = rawLine.trim();
        const bulletMatch = BULLET_REGEX.exec(line);
        if (bulletMatch) {
            const domain = resolveDomain(bulletMatch[1], bulletMatch[2] || '', bulletMatch[3]);
            if (domain) {
                entries.push({
                    domain,
                    subject: cleanSubject(bulletMatch[3]),
                    hash: fullHash,
                    shortHash,
                });
            }
        }
    }
    return entries;
}

function parseSingleHeaderEntry(
    headerLine: string,
    fullHash: string,
    shortHash: string,
): DomainEntry[] {
    const headerMatch = HEADER_REGEX.exec(headerLine);
    if (headerMatch) {
        const domain = resolveDomain(headerMatch[1], headerMatch[2] || '', headerMatch[3]);
        if (domain) {
            return [
                {
                    domain,
                    subject: cleanSubject(headerMatch[3]),
                    hash: fullHash,
                    shortHash,
                },
            ];
        }
        return [];
    }

    if (!/^(?:merge|chore)\b/i.test(headerLine)) {
        return [
            {
                domain: DOMAINS.LIFECYCLE,
                subject: cleanSubject(headerLine),
                hash: fullHash,
                shortHash,
            },
        ];
    }

    return [];
}

/**
 * Parses commit message headers and multiline bodies.
 * Expands sub-bullets into individual domain entries when present.
 */
export function parseCommitEntries(commit: {
    hash: string;
    shortHash?: string;
    message?: string;
    header?: string;
    body?: string;
}): DomainEntry[] {
    const fullHash = commit.hash;
    const shortHash = commit.shortHash || fullHash.substring(0, 7);
    const fullMessage = (commit.message || [commit.header, commit.body].filter(Boolean).join('\n\n')).trim();

    if (!fullMessage) return [];

    const lines = fullMessage.split(/\r?\n/);
    const headerLine = lines[0].trim();
    const bodyLines = lines.slice(1);

    const subBulletEntries = extractSubBullets(bodyLines, fullHash, shortHash);
    if (subBulletEntries.length > 0) {
        return subBulletEntries;
    }

    return parseSingleHeaderEntry(headerLine, fullHash, shortHash);
}

/**
 * Fetches commits between two git references directly using the local git binary with null-byte separation.
 */
export function getCommitsFromGit(
    fromRef: string,
    toRef: string = 'HEAD',
    cwd: string = process.cwd(),
): Array<{ hash: string; shortHash: string; message: string; committerDate: string }> {
    try {
        const gitBin = resolveGitBinary();
        const raw = execFileSync(
            gitBin,
            ['log', `${fromRef}..${toRef}`, '--format=COMMIT_START%x00%H%x00%h%x00%cI%x00%B%x00COMMIT_END'],
            { cwd, encoding: 'utf-8' },
        );

        const commits: Array<{ hash: string; shortHash: string; message: string; committerDate: string }> = [];
        const chunks = raw.split('COMMIT_START\0');

        for (const chunk of chunks) {
            if (!chunk.trim()) continue;
            const endIdx = chunk.indexOf('\0COMMIT_END');
            const content = endIdx >= 0 ? chunk.substring(0, endIdx) : chunk;
            const parts = content.split('\0');
            const hash = parts[0]?.trim();
            const shortHash = parts[1]?.trim() || hash?.substring(0, 7) || '';
            const committerDate = parts[2]?.trim() || '';
            const message = parts.slice(3).join('\0').trim();

            if (hash && message) {
                commits.push({ hash, shortHash, message, committerDate });
            }
        }

        return commits;
    } catch {
        return [];
    }
}

export interface CommitInput {
    hash: string;
    shortHash?: string;
    message?: string;
    header?: string;
    body?: string;
}

export interface GenerateNotesOptions {
    fromTag?: string;
    toRef?: string;
    version?: string;
    date?: string;
    repositoryUrl?: string;
    commits?: CommitInput[];
    cwd?: string;
}

function collectDomainGroups(commits: CommitInput[], repoUrl: string): Map<string, string[]> {
    const domainGroups = new Map<string, string[]>();

    for (const commit of commits) {
        const entries = parseCommitEntries(commit);
        for (const entry of entries) {
            const list = domainGroups.get(entry.domain) || [];
            const commitLink = `[${entry.shortHash}](${repoUrl}/commit/${entry.hash})`;
            const line = `* ${entry.subject} (${commitLink})`;

            if (!list.includes(line)) {
                list.push(line);
            }
            domainGroups.set(entry.domain, list);
        }
    }

    return domainGroups;
}

function formatCompareHeader(
    version: string,
    fromTag: string | undefined,
    toRef: string,
    date: string | undefined,
    repoUrl: string,
): string {
    const formattedDate = date ? formatReleaseDate(date) : formatReleaseDate(new Date());
    const targetTag = toRef.startsWith('v') ? toRef : `v${version}`;

    let compareLink: string;
    if (fromTag) {
        compareLink = `[${version}](${repoUrl}/compare/${fromTag}...${targetTag})`;
    } else {
        compareLink = `[${version}](${repoUrl}/releases/tag/v${version})`;
    }

    return `## ${compareLink} (${formattedDate})\n`;
}

/**
 * Generates domain-grouped markdown release notes for GitHub Releases.
 */
export function generateGitHubReleaseNotes(options: GenerateNotesOptions): string {
    const {
        fromTag,
        toRef = 'HEAD',
        version = 'Unreleased',
        date,
        repositoryUrl = 'https://github.com/Nicconike/LocalTelemetry',
        cwd = process.cwd(),
    } = options;

    const repoUrl = cleanRepoUrl(repositoryUrl);
    let commits = options.commits;

    if (!commits || commits.length === 0) {
        if (fromTag) {
            commits = getCommitsFromGit(fromTag, toRef, cwd);
        }
    }

    if (!commits || commits.length === 0) {
        return '';
    }

    const domainGroups = collectDomainGroups(commits, repoUrl);
    if (domainGroups.size === 0) {
        return '';
    }

    const sortedDomains = Array.from(domainGroups.keys()).sort(
        (a, b) => (DOMAIN_RANKS[b] ?? 0) - (DOMAIN_RANKS[a] ?? 0),
    );

    const output: string[] = [formatCompareHeader(version, fromTag, toRef, date, repoUrl)];

    for (const domain of sortedDomains) {
        const items = domainGroups.get(domain);
        if (!items || items.length === 0) continue;

        output.push(`### ${domain}\n`);
        for (const item of items) {
            output.push(item);
        }
        output.push('');
    }

    return output.join('\n').trim();
}

/**
 * Semantic-release publish plugin hook.
 * Replaces context.nextRelease.notes with domain-grouped release notes
 * immediately before @semantic-release/github publishes the release.
 */
export function publish(_pluginConfig: unknown, context: Record<string, any>): void {
    const { nextRelease, lastRelease, options, logger, cwd = process.cwd() } = context;

    const fromTag = lastRelease?.gitTag || (lastRelease?.version ? `v${lastRelease.version}` : undefined);
    const toRef = nextRelease?.gitHead || 'HEAD';
    const version = nextRelease?.version || '1.0.0';
    const repositoryUrl = cleanRepoUrl(options?.repositoryUrl);

    const existingDateMatch = /## \[.+?\]\(.+?\) \((.+?)\)/.exec(nextRelease?.notes ?? '');
    const releaseDate = existingDateMatch ? existingDateMatch[1] : undefined;

    const domainNotes = generateGitHubReleaseNotes({
        fromTag,
        toRef,
        version,
        date: releaseDate,
        repositoryUrl,
        cwd,
    });

    if (domainNotes?.trim()) {
        if (nextRelease) {
            nextRelease.notes = domainNotes;
        }
        logger?.log?.('Updated nextRelease.notes to domain-grouped format for GitHub Release.');
    }
}
