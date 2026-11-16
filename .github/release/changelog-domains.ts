import fs from 'node:fs';
import process from 'node:process';
import { execFileSync } from 'node:child_process';

/**
 * Functional domain categorization and release notes generator for GitHub Releases.
 *
 * Groups items by SemVer priority ordering:
 * Breaking Changes -> Features -> Bug Fixes -> Performance -> Reverts -> Refactoring ->
 * Build & Dependencies -> CI & Workflows -> Tests -> Documentation.
 *
 * Items within each category are grouped by domain, with multiple items rendered as nested sub-bullets.
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

export const DOMAIN_RANKS: Readonly<Record<string, number>> = {
    'Taskbar Overlay': 120,
    'Layout Presets': 110,
    'Layout Presets & Metrics': 110,
    'Hardware Telemetry': 100,
    'Hardware Sensors': 100,
    'Hardware Telemetry & Sensors': 100,
    'Network Traffic': 90,
    'Network & Traffic Monitoring': 90,
    'Alerts & Notifications': 80,
    'Appearance & Themes': 70,
    'Appearance & Theming': 70,
    'General & Lifecycle': 60,
    'General & Application Lifecycle': 60,
    'Installation & Packaging': 50,
    'Dependencies': 45,
    'Build System': 40,
    'Build System & Dependencies': 40,
    'CI Workflows': 35,
    'PR Synchronization': 34,
    'Release Pipeline': 32,
    'Continuous Integration & Workflows': 30,
    'Tests & Coverage': 20,
    'Tests & Test Coverage': 20,
    'Documentation': 10,
};

export interface ChangeSection {
    id: string;
    title: string;
    rank: number;
}

export const CHANGE_SECTIONS: Record<string, ChangeSection> = {
    BREAKING: { id: 'breaking', title: '🚨 Breaking Changes', rank: 100 },
    FEAT: { id: 'feat', title: '🚀 Features', rank: 90 },
    FIX: { id: 'fix', title: '🐛 Bug Fixes', rank: 80 },
    PERF: { id: 'perf', title: '⚡ Performance Improvements', rank: 70 },
    REVERT: { id: 'revert', title: '⏪ Reverts', rank: 60 },
    REFACTOR: { id: 'refactor', title: '♻️ Code Refactoring', rank: 50 },
    BUILD: { id: 'build', title: '📦 Build System & Dependencies', rank: 40 },
    CI: { id: 'ci', title: '🤖 Continuous Integration & Workflows', rank: 30 },
    TEST: { id: 'test', title: '🧪 Tests & Test Coverage', rank: 20 },
    DOCS: { id: 'docs', title: '📝 Documentation', rank: 10 },
};

const TYPE_CATEGORY_MAP: Readonly<Record<string, ChangeSection>> = {
    breaking: CHANGE_SECTIONS.BREAKING,
    feat: CHANGE_SECTIONS.FEAT,
    feature: CHANGE_SECTIONS.FEAT,
    fix: CHANGE_SECTIONS.FIX,
    perf: CHANGE_SECTIONS.PERF,
    performance: CHANGE_SECTIONS.PERF,
    revert: CHANGE_SECTIONS.REVERT,
    refactor: CHANGE_SECTIONS.REFACTOR,
    build: CHANGE_SECTIONS.BUILD,
    ci: CHANGE_SECTIONS.CI,
    test: CHANGE_SECTIONS.TEST,
    tests: CHANGE_SECTIONS.TEST,
    docs: CHANGE_SECTIONS.DOCS,
    documentation: CHANGE_SECTIONS.DOCS,
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

export const STANDARD_COMMIT_RANKS: Readonly<Record<string, number>> = {
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

const SCOPE_DOMAIN_RULES: ReadonlyArray<{ keywords: readonly string[]; domain: string }> = [
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
        keywords: ['ci', 'workflows', 'workflow', 'sonar', 'codecov', 'codeql', 'security', 'scorecard', 'sync'],
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

const SCOPE_LABEL_MAP: Readonly<Record<string, string>> = {
    deps: 'Dependencies',
    dependabot: 'Dependencies',
    sync: 'PR Synchronization',
    release: 'Release Pipeline',
    pawnio: 'Hardware Sensors',
    msr: 'Hardware Sensors',
    smn: 'Hardware Sensors',
    layout: 'Layout Presets',
    presets: 'Layout Presets',
    overlay: 'Taskbar Overlay',
    embedder: 'Taskbar Overlay',
    taskbar: 'Taskbar Overlay',
    traffic: 'Network Traffic',
    network: 'Network Traffic',
    theme: 'Appearance & Themes',
    appearance: 'Appearance & Themes',
    alerts: 'Alerts & Notifications',
    notifier: 'Alerts & Notifications',
    installer: 'Installation & Packaging',
    setup: 'Installation & Packaging',
    uninstaller: 'Installation & Packaging',
    workflows: 'CI Workflows',
    ci: 'CI Workflows',
    actions: 'CI Workflows',
    test: 'Tests & Coverage',
    tests: 'Tests & Coverage',
    coverage: 'Tests & Coverage',
    docs: 'Documentation',
    guides: 'Documentation',
    readme: 'Documentation',
};

const CANONICAL_DOMAIN_MAP: Readonly<Record<string, string>> = {
    [DOMAINS.TESTS]: 'Tests & Coverage',
    [DOMAINS.CI]: 'CI Workflows',
    [DOMAINS.BUILD]: 'Build System',
    [DOMAINS.HARDWARE]: 'Hardware Telemetry',
    [DOMAINS.TRAFFIC]: 'Network Traffic',
    [DOMAINS.THEME]: 'Appearance & Themes',
    [DOMAINS.LAYOUT]: 'Layout Presets',
    [DOMAINS.OVERLAY]: 'Taskbar Overlay',
    [DOMAINS.ALERTS]: 'Alerts & Notifications',
    [DOMAINS.INSTALLER]: 'Installation & Packaging',
    [DOMAINS.LIFECYCLE]: 'General & Lifecycle',
    [DOMAINS.DOCS]: 'Documentation',
};

const KNOWN_SCOPE_PREFIXES = new Set([
    'overlay', 'taskbar', 'embedder', 'layout', 'preset', 'presets', 'metric', 'metrics',
    'hardware', 'sensor', 'sensors', 'disk', 'nvme', 'sata', 'gpu', 'cpu', 'battery', 'pawnio',
    'traffic', 'network', 'srum', 'alerts', 'alert', 'notifier', 'appearance', 'theme', 'themes',
    'app', 'core', 'general', 'settings', 'lifecycle', 'diagnostics', 'installer', 'setup',
    'packaging', 'ci', 'workflows', 'build', 'deps', 'sonar', 'quality', 'test', 'tests', 'docs',
    'guides', 'bridge', 'release', 'sync',
]);

const KEYWORD_TYPE_MAP: Readonly<Record<string, string>> = {
    add: 'feat',
    implement: 'feat',
    support: 'feat',
    introduce: 'feat',
    fix: 'fix',
    repair: 'fix',
    resolve: 'fix',
    refactor: 'refactor',
    decompose: 'refactor',
    simplify: 'refactor',
    optimize: 'perf',
    tune: 'perf',
    test: 'test',
    assert: 'test',
    mock: 'test',
    doc: 'docs',
    docs: 'docs',
    document: 'docs',
};

const ENGINEERING_TYPE_DOMAINS: Readonly<Record<string, string>> = {
    test: DOMAINS.TESTS,
    build: DOMAINS.BUILD,
    ci: DOMAINS.CI,
    docs: DOMAINS.DOCS,
};

export function resolveGitBinary(): string {
    if (process.platform === 'win32') {
        const candidates = [
            String.raw`C:\Program Files\Git\cmd\git.exe`,
            String.raw`C:\Program Files\Git\bin\git.exe`,
            String.raw`C:\Program Files (x86)\Git\cmd\git.exe`,
        ];
        for (const candidate of candidates) {
            if (fs.existsSync(candidate)) return candidate;
        }
    } else {
        const candidates = ['/usr/bin/git', '/usr/local/bin/git', '/bin/git'];
        for (const candidate of candidates) {
            if (fs.existsSync(candidate)) return candidate;
        }
    }
    return 'git';
}

function textContainsKeyword(text: string, keywords: readonly string[]): boolean {
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
    if (scope !== 'release') return false;
    return type === 'chore' || /^\d+\.\d+\.\d+/.test(subject);
}

export function cleanRepoUrl(rawUrl?: string | null): string {
    let url = (rawUrl ?? '').trim();
    url = url.replace(/^git@github\.com:/i, 'https://github.com/');
    url = url.replace(/\.git$/i, '');
    if (!url.startsWith('http://') && !url.startsWith('https://')) {
        url = 'https://github.com/Nicconike/LocalTelemetry';
    }
    return url;
}

export function resolveDomain(
    type?: string | null,
    scope?: string | null,
    subject?: string | null,
): string | null {
    const rawType = (type ?? '').toLowerCase().trim();
    const rawScope = (scope ?? '').toLowerCase().trim();
    const rawSubject = (subject ?? '').toLowerCase().trim();

    if (isReleaseChore(rawType, rawScope, rawSubject)) return null;

    const engineeringDomain = ENGINEERING_TYPE_DOMAINS[rawType];
    if (engineeringDomain) return engineeringDomain;

    const scopeDomain = findDomainByKeywords(rawScope);
    if (scopeDomain) return scopeDomain;

    if (rawType === 'chore') return null;

    const subjectDomain = findDomainByKeywords(rawSubject);
    if (subjectDomain) return subjectDomain;

    if (rawType === 'style') return null;

    return DOMAINS.LIFECYCLE;
}

export function resolveDomainLabel(
    type?: string | null,
    scope?: string | null,
    subject?: string | null,
): string {
    const rawScope = (scope ?? '').toLowerCase().trim();

    if (rawScope.includes('deps')) return 'Dependencies';
    const labelFromScope = SCOPE_LABEL_MAP[rawScope];
    if (labelFromScope) return labelFromScope;

    const domain = resolveDomain(type, scope, subject);
    if (domain) {
        return CANONICAL_DOMAIN_MAP[domain] || domain;
    }

    if (rawScope) {
        return rawScope.charAt(0).toUpperCase() + rawScope.slice(1);
    }

    return 'General';
}

export function resolveChangeCategory(
    type?: string | null,
    isBreaking = false,
    scope?: string | null,
    subject?: string | null,
): ChangeSection | null {
    if (isBreaking) return CHANGE_SECTIONS.BREAKING;

    const rawType = (type ?? '').toLowerCase().trim();
    const rawScope = (scope ?? '').toLowerCase().trim();
    const rawSubject = (subject ?? '').toLowerCase().trim();

    if (isReleaseChore(rawType, rawScope, rawSubject)) return null;

    if (rawType === 'chore') {
        if (rawScope === 'deps' || rawScope === 'dependabot' || rawScope.includes('deps')) {
            return CHANGE_SECTIONS.BUILD;
        }
        return null;
    }

    if (rawType === 'style') return null;

    return TYPE_CATEGORY_MAP[rawType] || null;
}

export function cleanSubject(subject: string): string {
    let clean = (subject ?? '').trim();

    const dependabotMatch = /^bump the ([\w-]+) group across \d+ director\w+ with \d+ updates?$/i.exec(clean);
    if (dependabotMatch) {
        const groupName = dependabotMatch[1].replace(/-deps$/, '').replaceAll('-', ' ');
        return `Bump ${groupName} dependencies`;
    }

    const colonIdx = clean.indexOf(':');
    if (colonIdx > 0 && colonIdx < 30) {
        const potentialScope = clean.substring(0, colonIdx).trim().toLowerCase();
        if (KNOWN_SCOPE_PREFIXES.has(potentialScope)) {
            clean = clean.substring(colonIdx + 1).trim();
        }
    }

    if (!clean) return clean;

    clean = clean.charAt(0).toUpperCase() + clean.slice(1);
    return clean.replace(/\.$/, '');
}

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
    category: ChangeSection;
    domain: string;
    subject: string;
    hash: string;
    shortHash: string;
}

const BULLET_CONVENTIONAL_REGEX = /^[-*]\s+([a-z]+)(?:!|\(([^)]+)\)!?)?:\s*(.+)$/i;
const BULLET_KEYWORD_REGEX = /^[-*]\s+([A-Za-z]+)\b[:\s]+(.+)$/;
const HEADER_REGEX = /^([a-z]+)(?:!|\(([^)]+)\)!?)?:\s*(.+)$/i;

function extractSubBullets(
    bodyLines: string[],
    fullHash: string,
    shortHash: string,
): DomainEntry[] {
    const entries: DomainEntry[] = [];
    for (const rawLine of bodyLines) {
        const line = rawLine.trim();
        const convMatch = BULLET_CONVENTIONAL_REGEX.exec(line);
        if (convMatch) {
            const rawType = convMatch[1];
            const rawScope = convMatch[2] || '';
            const rawSubject = convMatch[3];
            const isBreaking = line.includes('BREAKING CHANGE:') || rawType.endsWith('!') || line.startsWith('- breaking:') || line.startsWith('* breaking:');

            const category = resolveChangeCategory(rawType, isBreaking, rawScope, rawSubject);
            if (category) {
                entries.push({
                    category,
                    domain: resolveDomainLabel(rawType, rawScope, rawSubject),
                    subject: cleanSubject(rawSubject),
                    hash: fullHash,
                    shortHash,
                });
            }
            continue;
        }

        const kwMatch = BULLET_KEYWORD_REGEX.exec(line);
        if (kwMatch) {
            const verb = kwMatch[1].toLowerCase();
            const rawType = KEYWORD_TYPE_MAP[verb];
            if (!rawType) continue;

            const fullSubject = `${kwMatch[1]} ${kwMatch[2].trim()}`;
            const category = resolveChangeCategory(rawType, false, '', fullSubject);
            if (category) {
                entries.push({
                    category,
                    domain: resolveDomainLabel(rawType, '', fullSubject),
                    subject: cleanSubject(fullSubject),
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
    bodyText: string,
    fullHash: string,
    shortHash: string,
): DomainEntry[] {
    const isBreaking = bodyText.includes('BREAKING CHANGE:') || headerLine.includes('!:');
    const headerMatch = HEADER_REGEX.exec(headerLine);
    if (headerMatch) {
        const rawType = headerMatch[1];
        const rawScope = headerMatch[2] || '';
        const rawSubject = headerMatch[3];

        const category = resolveChangeCategory(rawType, isBreaking, rawScope, rawSubject);
        if (category) {
            return [
                {
                    category,
                    domain: resolveDomainLabel(rawType, rawScope, rawSubject),
                    subject: cleanSubject(rawSubject),
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
                category: CHANGE_SECTIONS.FEAT,
                domain: 'General',
                subject: cleanSubject(headerLine),
                hash: fullHash,
                shortHash,
            },
        ];
    }

    return [];
}

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
    const bodyText = bodyLines.join('\n');

    const subBulletEntries = extractSubBullets(bodyLines, fullHash, shortHash);
    if (subBulletEntries.length > 0) {
        return subBulletEntries;
    }

    return parseSingleHeaderEntry(headerLine, bodyText, fullHash, shortHash);
}

export function getCommitsFromGit(
    fromRef: string,
    toRef = 'HEAD',
    cwd: string = process.cwd(),
): Array<{ hash: string; shortHash: string; message: string; committerDate: string; authorDate: string }> {
    try {
        const gitBin = resolveGitBinary();
        const raw = execFileSync(
            gitBin,
            ['log', `${fromRef}..${toRef}`, '--format=COMMIT_START%x00%H%x00%h%x00%cI%x00%aI%x00%B%x00COMMIT_END'],
            { cwd, encoding: 'utf-8' },
        );

        const commits: Array<{ hash: string; shortHash: string; message: string; committerDate: string; authorDate: string }> = [];
        const chunks = raw.split('COMMIT_START\0');

        for (const chunk of chunks) {
            if (!chunk.trim()) continue;
            const endIdx = chunk.indexOf('\0COMMIT_END');
            const content = endIdx >= 0 ? chunk.substring(0, endIdx) : chunk;
            const parts = content.split('\0');
            const hash = parts[0]?.trim();
            const shortHash = parts[1]?.trim() || hash?.substring(0, 7) || '';
            const committerDate = parts[2]?.trim() || '';
            const authorDate = parts[3]?.trim() || '';
            const message = parts.slice(4).join('\0').trim();

            if (hash && message) {
                commits.push({ hash, shortHash, message, committerDate, authorDate });
            }
        }

        return commits;
    } catch (error) {
        const message = error instanceof Error ? error.message : String(error);
        process.stderr.write(`[release-notes] Warning: unable to retrieve git commits between ${fromRef} and ${toRef}: ${message}\n`);
        return [];
    }
}

export interface CommitInput {
    hash: string;
    shortHash?: string;
    message?: string;
    header?: string;
    body?: string;
    committerDate?: string;
    authorDate?: string;
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

function getTagAuthorDate(tag: string, cwd: string): string | null {
    try {
        const gitBin = resolveGitBinary();
        return execFileSync(gitBin, ['log', '-1', '--format=%aI', tag], { cwd, encoding: 'utf-8' }).trim();
    } catch (error) {
        const message = error instanceof Error ? error.message : String(error);
        process.stderr.write(`[release-notes] Warning: unable to read tag date for ${tag}: ${message}\n`);
        return null;
    }
}

interface DomainItem {
    subject: string;
    link: string;
}

interface CategoryGroup {
    section: ChangeSection;
    domainMap: Map<string, DomainItem[]>;
}

function isCommitEligible(commit: CommitInput, tagTimestamp: number): boolean {
    if (tagTimestamp <= 0 || !commit.authorDate) return true;
    return new Date(commit.authorDate).getTime() > tagTimestamp;
}

function addEntryToCategory(
    categoryGroups: Map<string, CategoryGroup>,
    entry: DomainEntry,
    repoUrl: string,
): void {
    const groupKey = entry.category.id;
    let group = categoryGroups.get(groupKey);
    if (!group) {
        group = { section: entry.category, domainMap: new Map() };
        categoryGroups.set(groupKey, group);
    }

    const commitLink = `[${entry.shortHash}](${repoUrl}/commit/${entry.hash})`;
    const domainList = group.domainMap.get(entry.domain) || [];
    const isDuplicate = domainList.some(
        (item) => item.subject === entry.subject && item.link === commitLink,
    );
    if (!isDuplicate) {
        domainList.push({ subject: entry.subject, link: commitLink });
        group.domainMap.set(entry.domain, domainList);
    }
}

function collectCategoryGroups(
    commits: CommitInput[],
    repoUrl: string,
    fromTagDate?: string | null,
): Map<string, CategoryGroup> {
    const categoryGroups = new Map<string, CategoryGroup>();
    const tagTimestamp = fromTagDate ? new Date(fromTagDate).getTime() : 0;

    for (const commit of commits) {
        if (!isCommitEligible(commit, tagTimestamp)) continue;

        const entries = parseCommitEntries(commit);
        for (const entry of entries) {
            addEntryToCategory(categoryGroups, entry, repoUrl);
        }
    }

    return categoryGroups;
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

function formatDomainEntries(domain: string, items: DomainItem[]): string[] {
    if (items.length === 1) {
        return [`* **${domain}**: ${items[0].subject} (${items[0].link})`];
    }
    const lines = [`* **${domain}**`];
    for (const item of items) {
        lines.push(`  * ${item.subject} (${item.link})`);
    }
    return lines;
}

function renderCategoryGroup(group: CategoryGroup): string[] {
    const lines: string[] = [`### ${group.section.title}\n`];
    const sortedDomains = Array.from(group.domainMap.keys()).sort(
        (a, b) => (DOMAIN_RANKS[b] ?? 0) - (DOMAIN_RANKS[a] ?? 0),
    );

    for (const domain of sortedDomains) {
        const items = group.domainMap.get(domain);
        if (items && items.length > 0) {
            lines.push(...formatDomainEntries(domain, items));
        }
    }
    lines.push('');
    return lines;
}

function resolveReleaseCommits(
    commits: CommitInput[] | undefined,
    fromTag: string | undefined,
    toRef: string,
    cwd: string,
): CommitInput[] {
    if (commits && commits.length > 0) return commits;
    if (fromTag) return getCommitsFromGit(fromTag, toRef, cwd);
    return [];
}

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
    const commits = resolveReleaseCommits(options.commits, fromTag, toRef, cwd);
    if (commits.length === 0) return '';

    const fromTagDate = fromTag ? getTagAuthorDate(fromTag, cwd) : null;
    const categoryGroups = collectCategoryGroups(commits, repoUrl, fromTagDate);
    if (categoryGroups.size === 0) return '';

    const sortedGroups = Array.from(categoryGroups.values()).sort(
        (a, b) => b.section.rank - a.section.rank,
    );

    const output: string[] = [formatCompareHeader(version, fromTag, toRef, date, repoUrl)];
    for (const group of sortedGroups) {
        if (group.domainMap.size > 0) {
            output.push(...renderCategoryGroup(group));
        }
    }

    return output.join('\n').trim();
}

/**
 * Semantic-release GitHub publish plugin wrapper
 */
export async function verifyConditions(pluginConfig: unknown, context: Record<string, any>): Promise<void> {
    const gh = await import('@semantic-release/github');
    return gh.verifyConditions?.(pluginConfig, context);
}

function resolveReleaseParameters(context: Record<string, any>): {
    fromTag?: string;
    toRef: string;
    version: string;
    releaseDate?: string;
    repositoryUrl: string;
    cwd: string;
} {
    const { nextRelease, lastRelease, options, cwd = process.cwd() } = context;
    const fromTag = lastRelease?.gitTag || (lastRelease?.version ? `v${lastRelease.version}` : undefined);
    const toRef = nextRelease?.gitHead || nextRelease?.gitTag || 'HEAD';
    const version = nextRelease?.version || '1.0.0';
    const repositoryUrl = cleanRepoUrl(options?.repositoryUrl);

    const existingDateMatch = /## \[.+?\]\(.+?\) \((.+?)\)/.exec(nextRelease?.notes ?? '');
    const releaseDate = existingDateMatch ? existingDateMatch[1] : undefined;

    return { fromTag, toRef, version, releaseDate, repositoryUrl, cwd };
}

export async function publish(pluginConfig: unknown, context: Record<string, any>): Promise<any> {
    const params = resolveReleaseParameters(context);

    const domainNotes = generateGitHubReleaseNotes({
        fromTag: params.fromTag,
        toRef: params.toRef,
        version: params.version,
        date: params.releaseDate,
        repositoryUrl: params.repositoryUrl,
        cwd: params.cwd,
    });

    const enrichedContext = {
        ...context,
        nextRelease: {
            ...context.nextRelease,
            notes: domainNotes?.trim() ? domainNotes : context.nextRelease.notes,
        },
    };

    const gh = await import('@semantic-release/github');
    return gh.publish?.(pluginConfig, enrichedContext);
}

export async function addChannel(pluginConfig: unknown, context: Record<string, any>): Promise<any> {
    const gh = await import('@semantic-release/github');
    return gh.addChannel?.(pluginConfig, context);
}

export async function success(pluginConfig: unknown, context: Record<string, any>): Promise<void> {
    const gh = await import('@semantic-release/github');
    return gh.success?.(pluginConfig, context);
}

export async function fail(pluginConfig: unknown, context: Record<string, any>): Promise<void> {
    const gh = await import('@semantic-release/github');
    return gh.fail?.(pluginConfig, context);
}

export function parseOwnerAndRepo(rawUrl?: string | null): { owner: string; repo: string } | null {
    if (!rawUrl?.trim()) return null;
    const trimmed = rawUrl.trim();
    const match = /(?:github\.com[/:])([^/]+)\/([^/]+?)(?:\.git)?$/.exec(trimmed);
    if (!match) return null;
    return { owner: match[1], repo: match[2] };
}
