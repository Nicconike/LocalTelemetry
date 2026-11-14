import { generateNotes } from '@semantic-release/release-notes-generator';

/**
 * Functional domain categorization and formatting for changelogs and release notes.
 * Formats release entries in ASD-STE100 Simplified Technical English.
 */

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
    Features: 10,
    'Bug Fixes': 9,
    'Performance Improvements': 8,
    Reverts: 7,
    'Code Refactoring': 6,
    'Build System': 5,
    'Continuous Integration': 4,
    Tests: 3,
    Styles: 2,
    Documentation: 1,
};

export const DOMAINS = {
    OVERLAY: 'Taskbar Overlay & Window Management',
    LAYOUT: 'Overlay Layout Presets & Metrics',
    HARDWARE: 'Hardware Telemetry & Sensor Drivers',
    THEME: 'Theming & User Interface',
    TRAFFIC: 'Network Traffic & History',
    LIFECYCLE: 'Application Lifecycle & Diagnostics',
    INSTALLER: 'Installation & Packaging',
    FEATURES: 'Features & Improvements',
    BUGFIXES: 'Bug Fixes',
    PERFORMANCE: 'Performance Improvements',
    REFACTOR: 'Code Refactoring',
    CI: 'Continuous Integration & Workflows',
    BUILD: 'Build System & Dependencies',
    TESTS: 'Tests & Test Coverage',
    DOCS: 'Documentation',
} as const;

export const DOMAIN_RANKS: Record<string, number> = {
    [DOMAINS.OVERLAY]: 100,
    [DOMAINS.LAYOUT]: 95,
    [DOMAINS.HARDWARE]: 90,
    [DOMAINS.THEME]: 85,
    [DOMAINS.TRAFFIC]: 80,
    [DOMAINS.LIFECYCLE]: 75,
    [DOMAINS.INSTALLER]: 70,
    [DOMAINS.FEATURES]: 65,
    [DOMAINS.BUGFIXES]: 60,
    [DOMAINS.PERFORMANCE]: 55,
    [DOMAINS.REFACTOR]: 50,
    [DOMAINS.CI]: 40,
    [DOMAINS.BUILD]: 30,
    [DOMAINS.TESTS]: 20,
    [DOMAINS.DOCS]: 10,
};

export interface RawCommit {
    type?: string | null;
    scope?: string | null;
    subject?: string | null;
    header?: string | null;
    body?: string | null;
    footer?: string | null;
    hash?: string | null;
    shortHash?: string | null;
    committerDate?: string | Date | null;
    notes?: Array<{ title?: string; text?: string }>;
    references?: Array<{ prefix?: string; issue?: string; owner?: string; repository?: string }>;
    revert?: boolean;
}

export interface TransformedCommit {
    type: string;
    scope?: string | null;
    subject: string;
    shortHash?: string | null;
    committerDate?: string | Date | null;
    notes: Array<{ title?: string; text?: string }>;
    references: Array<{ prefix?: string; issue?: string; owner?: string; repository?: string }>;
}

const SCOPE_DOMAIN_RULES: Array<{ keywords: string[]; domain: string }> = [
    {
        keywords: ['overlay', 'embedder', 'taskbar', 'tray', 'mousehook', 'placement', 'band'],
        domain: DOMAINS.OVERLAY,
    },
    {
        keywords: ['layout', 'preset', 'presets', 'metric', 'metrics', 'column', 'columns'],
        domain: DOMAINS.LAYOUT,
    },
    {
        keywords: [
            'hardware', 'disk', 'disks', 'nvme', 'sata', 'scsi', 'sensor', 'sensors',
            'smn', 'pawnio', 'battery', 'gpu', 'cpu', 'adl', 'nvml', 'wddm', 'igcl',
            'inventory', 'thermal', 'temp',
        ],
        domain: DOMAINS.HARDWARE,
    },
    {
        keywords: ['theme', 'themes', 'color', 'colors', 'appearance', 'palette', 'accent', 'slider', 'ui'],
        domain: DOMAINS.THEME,
    },
    {
        keywords: ['traffic', 'network', 'srum', 'netusage', 'bandwidth', 'importer', 'history'],
        domain: DOMAINS.TRAFFIC,
    },
    {
        keywords: [
            'app', 'lifecycle', 'portable', 'single-instance', 'mutex', 'ipc',
            'notifier', 'logging', 'log', 'logs', 'diagnostics', 'settings',
        ],
        domain: DOMAINS.LIFECYCLE,
    },
    {
        keywords: ['installer', 'setup', 'uninstaller', 'inno', 'package', 'packaging'],
        domain: DOMAINS.INSTALLER,
    },
];

function textContainsKeyword(text: string, keywords: string[]): boolean {
    if (!text) return false;
    const tokens = text.toLowerCase().split(/[^a-z0-9_-]+/);
    return keywords.some((kw) => tokens.includes(kw));
}

/**
 * Determines the target functional domain for a commit.
 * Returns null if the commit should be omitted from release notes (e.g. chores).
 */
export function resolveDomain(
    type?: string | null,
    scope?: string | null,
    subject?: string | null,
): string | null {
    const rawType = (type ?? '').toLowerCase().trim();
    const rawScope = (scope ?? '').toLowerCase().trim();
    const rawSubject = (subject ?? '').toLowerCase().trim();

    // Skip chore commits
    if (rawType === 'chore') {
        return null;
    }

    // Isolated test, CI, build, and docs commits go directly to their respective sections
    if (rawType === 'test') {
        return DOMAINS.TESTS;
    }
    if (rawType === 'ci') {
        return DOMAINS.CI;
    }
    if (rawType === 'build') {
        return DOMAINS.BUILD;
    }
    if (rawType === 'docs') {
        return DOMAINS.DOCS;
    }

    // 1. Check scope first (highest specificity)
    for (const rule of SCOPE_DOMAIN_RULES) {
        if (textContainsKeyword(rawScope, rule.keywords)) {
            return rule.domain;
        }
    }

    // 2. Check subject if scope did not determine domain
    for (const rule of SCOPE_DOMAIN_RULES) {
        if (textContainsKeyword(rawSubject, rule.keywords)) {
            return rule.domain;
        }
    }

    // 3. Fall back to commit type if no domain keyword matched
    switch (rawType) {
        case 'feat':
            return DOMAINS.FEATURES;
        case 'fix':
            return DOMAINS.BUGFIXES;
        case 'perf':
            return DOMAINS.PERFORMANCE;
        case 'refactor':
            return DOMAINS.REFACTOR;
        case 'style':
            return null; // Exclude non-functional styling per repository rules
        default:
            return DOMAINS.FEATURES;
    }
}

/**
 * Normalizes commit subject text into ASD-STE100 active-voice format.
 */
export function cleanSubject(subject: string): string {
    let clean = (subject ?? '').trim();

    // Standardize Dependabot group bump strings
    const dependabotGroupMatch = /^bump the ([\w-]+) group across \d+ director\w+ with \d+ updates?$/i.exec(clean);
    if (dependabotGroupMatch) {
        const groupName = dependabotGroupMatch[1].replace(/-deps$/, '').replace(/-/g, ' ');
        return `Bump ${groupName} dependencies`;
    }

    // Strip redundant leading scope prefixes if duplicated in subject
    clean = clean.replace(/^(?:overlay|hardware|layout|theme|traffic|app|ci|build|test):\s*/i, '');

    if (!clean) return clean;

    // Capitalize first letter
    clean = clean.charAt(0).toUpperCase() + clean.slice(1);

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
    if (isNaN(d.getTime())) return String(rawDate);
    const day = String(d.getUTCDate()).padStart(2, '0');
    const month = String(d.getUTCMonth() + 1).padStart(2, '0');
    const year = d.getUTCFullYear();
    return `${day}-${month}-${year}`;
}

/**
 * Conventional changelog transform function for semantic-release.
 */
export function createChangelogTransform() {
    return (commit: RawCommit): TransformedCommit | null => {
        const domain = resolveDomain(commit.type, commit.scope, commit.subject);
        if (!domain) {
            return null;
        }

        const notes = Array.isArray(commit.notes) ? commit.notes : [];
        const references = Array.isArray(commit.references) ? commit.references : [];
        const shortHash = typeof commit.hash === 'string'
            ? commit.hash.substring(0, 7)
            : (commit.shortHash ?? null);

        const cleanedSubject = cleanSubject(commit.subject ?? commit.header ?? '');

        return {
            type: domain,
            scope: commit.scope ?? '',
            subject: cleanedSubject,
            shortHash,
            committerDate: commit.committerDate,
            notes,
            references,
        };
    };
}

/**
 * Comparator to sort commit groups in strictly descending priority order.
 */
export function compareCommitGroups(
    a: { title?: string },
    b: { title?: string },
    ranks: Record<string, number> = DOMAIN_RANKS,
): number {
    const rankA = ranks[a.title ?? ''] ?? 0;
    const rankB = ranks[b.title ?? ''] ?? 0;
    return rankB - rankA;
}

/**
 * Generates domain-grouped release notes from context.
 */
export async function generateDomainNotes(context: Record<string, any>): Promise<string> {
    // If context.nextRelease.notes already contains a formatted release date header, preserve that exact date
    const existingDateMatch = /## \[.+?\]\(.+?\) \((.+?)\)/.exec(context.nextRelease?.notes ?? '');
    const releaseDate = existingDateMatch ? existingDateMatch[1] : undefined;

    const domainPluginConfig = {
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
            finalizeContext: (ctx: Record<string, unknown>) => {
                if (releaseDate) {
                    ctx.date = formatReleaseDate(releaseDate);
                } else if (ctx.date) {
                    ctx.date = formatReleaseDate(ctx.date as string);
                }
                return ctx;
            },
        },
    };

    const notes = await generateNotes(domainPluginConfig, context as any);
    return notes ?? '';
}

/**
 * Semantic-release publish hook to set domain-grouped release notes for GitHub Releases.
 */
export async function publish(_pluginConfig: unknown, context: Record<string, any>): Promise<void> {
    const domainNotes = await generateDomainNotes(context);
    if (domainNotes && domainNotes.trim()) {
        if (context.nextRelease) {
            context.nextRelease.notes = domainNotes;
        }
        context.logger?.log?.('Updated nextRelease.notes to domain-grouped format for GitHub Release.');
    }
}
