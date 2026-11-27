import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import {
    CHANGE_SECTIONS,
    cleanRepoUrl,
    cleanSubject,
    DOMAINS,
    formatReleaseDate,
    generateGitHubReleaseNotes,
    parseCommitEntries,
    parseOwnerAndRepo,
    resolveChangeCategory,
    resolveDomain,
    resolveDomainLabel,
} from '../../.github/release/changelog-domains.js';

describe('changelog-domains', () => {
    describe('cleanRepoUrl', () => {
        it('normalizes SSH git URLs to HTTPS', () => {
            assert.equal(
                cleanRepoUrl('git@github.com:Nicconike/LocalTelemetry.git'),
                'https://github.com/Nicconike/LocalTelemetry',
            );
        });

        it('normalizes HTTPS git URLs with .git suffix', () => {
            assert.equal(
                cleanRepoUrl('https://github.com/Nicconike/LocalTelemetry.git'),
                'https://github.com/Nicconike/LocalTelemetry',
            );
        });

        it('falls back to default URL when null or empty', () => {
            assert.equal(cleanRepoUrl(''), 'https://github.com/Nicconike/LocalTelemetry');
            assert.equal(cleanRepoUrl(undefined), 'https://github.com/Nicconike/LocalTelemetry');
        });
    });

    describe('parseOwnerAndRepo', () => {
        it('parses owner and repo from standard HTTPS URL', () => {
            const res = parseOwnerAndRepo('https://github.com/Nicconike/LocalTelemetry');
            assert.deepEqual(res, { owner: 'Nicconike', repo: 'LocalTelemetry' });
        });

        it('parses owner and repo from SSH URL', () => {
            const res = parseOwnerAndRepo('git@github.com:Nicconike/LocalTelemetry.git');
            assert.deepEqual(res, { owner: 'Nicconike', repo: 'LocalTelemetry' });
        });

        it('returns null for non-github or invalid URL', () => {
            assert.equal(parseOwnerAndRepo('invalid-url-without-slash'), null);
        });
    });

    describe('resolveDomain', () => {
        it('resolves taskbar overlay domain from scope and keywords', () => {
            assert.equal(resolveDomain('feat', 'overlay', 'embed child window'), DOMAINS.OVERLAY);
            assert.equal(resolveDomain('fix', 'taskbar', 'reposition band'), DOMAINS.OVERLAY);
            assert.equal(resolveDomain('refactor', 'embedder', 'cleanup interop'), DOMAINS.OVERLAY);
        });

        it('resolves layout presets domain', () => {
            assert.equal(resolveDomain('feat', 'layout', 'add preset tiles'), DOMAINS.LAYOUT);
            assert.equal(resolveDomain('fix', 'metrics', 'fix row calculation'), DOMAINS.LAYOUT);
        });

        it('resolves hardware telemetry domain', () => {
            assert.equal(resolveDomain('feat', 'hardware', 'query NVMe temperature'), DOMAINS.HARDWARE);
            assert.equal(resolveDomain('fix', 'gpu', 'handle driver absence'), DOMAINS.HARDWARE);
            assert.equal(resolveDomain('refactor', 'pawnio', 'sandbox MSR calls'), DOMAINS.HARDWARE);
        });

        it('resolves traffic and network domain', () => {
            assert.equal(resolveDomain('feat', 'traffic', 'import JSONL usage history'), DOMAINS.TRAFFIC);
            assert.equal(resolveDomain('fix', 'network', 'filter inactive adapters'), DOMAINS.TRAFFIC);
        });

        it('resolves alerts and notifications domain', () => {
            assert.equal(resolveDomain('feat', 'alerts', 'add hysteresis threshold'), DOMAINS.ALERTS);
            assert.equal(resolveDomain('fix', 'notifier', 'pipe reconnection'), DOMAINS.ALERTS);
        });

        it('resolves appearance and themes domain', () => {
            assert.equal(resolveDomain('feat', 'theme', 'add laser crimson palette'), DOMAINS.THEME);
            assert.equal(resolveDomain('style', 'appearance', 'contrast borders'), DOMAINS.THEME);
        });

        it('resolves lifecycle and app domain', () => {
            assert.equal(resolveDomain('feat', 'app', 'single instance mutex'), DOMAINS.LIFECYCLE);
            assert.equal(resolveDomain('refactor', 'bridge', 'dispatch IPC messages'), DOMAINS.LIFECYCLE);
            assert.equal(resolveDomain('feat', 'settings', 'portable directory path'), DOMAINS.LIFECYCLE);
        });

        it('resolves packaging and installer domain', () => {
            assert.equal(resolveDomain('feat', 'installer', 'compile Inno Setup 7'), DOMAINS.INSTALLER);
            assert.equal(resolveDomain('chore', 'winget', 'bump manifest'), DOMAINS.INSTALLER);
        });

        it('resolves build system & dependencies domain distinctly from CI', () => {
            assert.equal(resolveDomain('build', 'deps', 'bump Microsoft.Web.WebView2'), DOMAINS.BUILD);
            assert.equal(resolveDomain('build', '', 'update global.json'), DOMAINS.BUILD);
        });

        it('resolves CI & workflows domain distinctly from build and tests', () => {
            assert.equal(resolveDomain('ci', 'workflows', 'restrict token permissions'), DOMAINS.CI);
            assert.equal(resolveDomain('ci', 'sonar', 'configure coverage feeder'), DOMAINS.CI);
            assert.equal(resolveDomain('ci', 'release', 'prevent duplicate trailing newlines and expand test coverage'), DOMAINS.CI);
        });

        it('resolves tests & test coverage domain distinctly from CI', () => {
            assert.equal(resolveDomain('test', 'coverage', 'assert embedder seams'), DOMAINS.TESTS);
            assert.equal(resolveDomain('test', '', 'unit test runner setup'), DOMAINS.TESTS);
        });

        it('resolves documentation domain', () => {
            assert.equal(resolveDomain('docs', 'guides', 'update developer architecture'), DOMAINS.DOCS);
            assert.equal(resolveDomain('docs', 'readme', 'refresh badges'), DOMAINS.DOCS);
        });

        it('excludes routine chores without feature scopes', () => {
            assert.equal(resolveDomain('chore', '', '1.3.0-beta.3 [skip ci]'), null);
            assert.equal(resolveDomain('chore', 'release', '1.3.0-beta.3 [skip ci]'), null);
            assert.equal(resolveDomain('style', '', 'format whitespace'), null);
        });
    });

    describe('resolveChangeCategory', () => {
        it('resolves breaking changes first', () => {
            const cat = resolveChangeCategory('feat', true, 'overlay', 'breaking api change');
            assert.equal(cat?.id, 'breaking');
        });

        it('resolves conventional commit categories', () => {
            assert.equal(resolveChangeCategory('feat')?.id, 'feat');
            assert.equal(resolveChangeCategory('fix')?.id, 'fix');
            assert.equal(resolveChangeCategory('perf')?.id, 'perf');
            assert.equal(resolveChangeCategory('refactor')?.id, 'refactor');
            assert.equal(resolveChangeCategory('build')?.id, 'build');
            assert.equal(resolveChangeCategory('ci')?.id, 'ci');
            assert.equal(resolveChangeCategory('test')?.id, 'test');
            assert.equal(resolveChangeCategory('docs')?.id, 'docs');
        });

        it('maps dependency chores to build', () => {
            const cat = resolveChangeCategory('chore', false, 'deps', 'bump svelte');
            assert.equal(cat?.id, 'build');
        });

        it('omits release chores and generic styles', () => {
            assert.equal(resolveChangeCategory('chore', false, 'release', '1.3.0-beta.4'), null);
            assert.equal(resolveChangeCategory('style', false, '', 'format code'), null);
        });
    });

    describe('resolveDomainLabel', () => {
        it('resolves clean domain labels from scopes', () => {
            assert.equal(resolveDomainLabel('feat', 'layout', ''), 'Layout Presets');
            assert.equal(resolveDomainLabel('feat', 'overlay', ''), 'Taskbar Overlay');
            assert.equal(resolveDomainLabel('feat', 'traffic', ''), 'Network Traffic');
            assert.equal(resolveDomainLabel('feat', 'theme', ''), 'Appearance & Themes');
            assert.equal(resolveDomainLabel('build', 'deps', ''), 'Dependencies');
            assert.equal(resolveDomainLabel('ci', 'sync', ''), 'PR Synchronization');
            assert.equal(resolveDomainLabel('ci', 'release', ''), 'Release Pipeline');
            assert.equal(resolveDomainLabel('test', 'coverage', ''), 'Tests & Coverage');
            assert.equal(resolveDomainLabel('docs', 'guides', ''), 'Documentation');
        });
    });

    describe('cleanSubject', () => {
        it('strips redundant leading scope prefixes', () => {
            assert.equal(cleanSubject('overlay: embed window'), 'Embed window');
            assert.equal(cleanSubject('layout: add presets'), 'Add presets');
            assert.equal(cleanSubject('hardware: detect sensors'), 'Detect sensors');
            assert.equal(cleanSubject('bridge: dispatch messages'), 'Dispatch messages');
        });

        it('capitalizes the first character and strips trailing period', () => {
            assert.equal(cleanSubject('update taskbar placement.'), 'Update taskbar placement');
        });

        it('standardizes dependabot group bump descriptions', () => {
            assert.equal(
                cleanSubject('bump the dotnet-deps group across 3 directories with 4 updates'),
                'Bump dotnet dependencies',
            );
        });
    });

    describe('formatReleaseDate', () => {
        it('formats ISO strings into DD-MM-YYYY', () => {
            assert.equal(formatReleaseDate('2026-11-14T22:56:05Z'), '14-11-2026');
        });

        it('preserves existing DD-MM-YYYY formatted dates', () => {
            assert.equal(formatReleaseDate('14-11-2026'), '14-11-2026');
        });
    });

    describe('parseCommitEntries', () => {
        it('expands multiline body sub-bullets into distinct domain entries', () => {
            const commit = {
                hash: '20507fdd629ed5a2e5205b7bfaf871bfd0ad98a6',
                shortHash: '20507fd',
                message: [
                    'feat(layout,traffic,overlay): add layout presets, traffic import, and themes',
                    '',
                    '- feat(layout): add layout preset customization and sorting',
                    '- feat(traffic): add JSONL historical network traffic import',
                    '- feat(theme): introduce Laser Crimson Cyberpunk theme',
                    '- feat(overlay): enhance Win32 taskbar embedder positioning',
                    '- test(coverage): add unit tests for layout presets and themes',
                    '- docs(guides): update architecture specification',
                ].join('\n'),
            };

            const entries = parseCommitEntries(commit);

            assert.equal(entries.length, 6);
            assert.deepEqual(entries[0], {
                category: CHANGE_SECTIONS.FEAT,
                domain: 'Layout Presets',
                subject: 'Add layout preset customization and sorting',
                hash: commit.hash,
                shortHash: commit.shortHash,
            });
            assert.deepEqual(entries[1], {
                category: CHANGE_SECTIONS.FEAT,
                domain: 'Network Traffic',
                subject: 'Add JSONL historical network traffic import',
                hash: commit.hash,
                shortHash: commit.shortHash,
            });
            assert.deepEqual(entries[2], {
                category: CHANGE_SECTIONS.FEAT,
                domain: 'Appearance & Themes',
                subject: 'Introduce Laser Crimson Cyberpunk theme',
                hash: commit.hash,
                shortHash: commit.shortHash,
            });
            assert.deepEqual(entries[3], {
                category: CHANGE_SECTIONS.FEAT,
                domain: 'Taskbar Overlay',
                subject: 'Enhance Win32 taskbar embedder positioning',
                hash: commit.hash,
                shortHash: commit.shortHash,
            });
            assert.deepEqual(entries[4], {
                category: CHANGE_SECTIONS.TEST,
                domain: 'Tests & Coverage',
                subject: 'Add unit tests for layout presets and themes',
                hash: commit.hash,
                shortHash: commit.shortHash,
            });
            assert.deepEqual(entries[5], {
                category: CHANGE_SECTIONS.DOCS,
                domain: 'Documentation',
                subject: 'Update architecture specification',
                hash: commit.hash,
                shortHash: commit.shortHash,
            });
        });

        it('correctly categorizes keyword sub-bullets containing tests and fixes', () => {
            const commit = {
                hash: 'ec1b5d15b96b785f49bdf0f57c38a070e8117b64',
                shortHash: 'ec1b5d1',
                message: [
                    'fix(overlay): apply placement offset and avoid ExplorerPatcher weather overlap on Win10',
                    '',
                    '- Detect adjacent weather widgets and ReBar deskbands to eliminate overlay overlap.',
                    '- Add unit tests for Windows 10 deskband clipping and placement offset calculations.',
                    '- Update documentation for Windows 10 deskbands and uninstaller options.',
                ].join('\n'),
            };

            const entries = parseCommitEntries(commit);
            assert.equal(entries.length, 3);
            assert.equal(entries[0].category, CHANGE_SECTIONS.FIX);
            assert.equal(entries[0].subject, 'Detect adjacent weather widgets and ReBar deskbands to eliminate overlay overlap');
            assert.equal(entries[1].category, CHANGE_SECTIONS.TEST);
            assert.equal(entries[1].subject, 'Add unit tests for Windows 10 deskband clipping and placement offset calculations');
            assert.equal(entries[2].category, CHANGE_SECTIONS.DOCS);
            assert.equal(entries[2].subject, 'Update documentation for Windows 10 deskbands and uninstaller options');
        });

        it('falls back to header scope when no body sub-bullets exist', () => {
            const commit = {
                hash: 'bb672c28fde60aa5c785f12a76f671ce43199162',
                shortHash: 'bb672c2',
                message: 'fix(overlay): dismiss context menu on outside click',
            };

            const entries = parseCommitEntries(commit);
            assert.equal(entries.length, 1);
            assert.deepEqual(entries[0], {
                category: CHANGE_SECTIONS.FIX,
                domain: 'Taskbar Overlay',
                subject: 'Dismiss context menu on outside click',
                hash: commit.hash,
                shortHash: commit.shortHash,
            });
        });

        it('omits routine chore release commits', () => {
            const commit = {
                hash: 'daff7864c04bcf400e619e8d89f09531ab419e24',
                shortHash: 'daff786',
                message: 'chore(release): 1.3.0-beta.3 [skip ci]',
            };

            const entries = parseCommitEntries(commit);
            assert.equal(entries.length, 0);
        });
    });

    describe('generateGitHubReleaseNotes', () => {
        it('assembles sorted markdown release notes grouped by type with distinct SemVer sections', () => {
            const commits = [
                {
                    hash: '1111111111111111111111111111111111111111',
                    shortHash: '1111111',
                    message: 'feat(overlay): embed child window into taskbar',
                },
                {
                    hash: '2222222222222222222222222222222222222222',
                    shortHash: '2222222',
                    message: 'fix(layout): fix custom preset tile calculation',
                },
                {
                    hash: '3333333333333333333333333333333333333333',
                    shortHash: '3333333',
                    message: 'ci(workflows): enforce app token attribution',
                },
                {
                    hash: '4444444444444444444444444444444444444444',
                    shortHash: '4444444',
                    message: 'test(taskbar): assert simulated automation exception',
                },
            ];

            const notes = generateGitHubReleaseNotes({
                fromTag: 'v1.0.0',
                toRef: 'v1.1.0',
                version: '1.1.0',
                date: '14-11-2026',
                repositoryUrl: 'https://github.com/Nicconike/LocalTelemetry',
                commits,
            });

            assert.ok(notes.includes('## [1.1.0](https://github.com/Nicconike/LocalTelemetry/compare/v1.0.0...v1.1.0) (14-11-2026)'));
            assert.ok(notes.includes('### 🚀 Features\n\n* **Taskbar Overlay**: Embed child window into taskbar ([1111111](https://github.com/Nicconike/LocalTelemetry/commit/1111111111111111111111111111111111111111))'));
            assert.ok(notes.includes('### 🐛 Bug Fixes\n\n* **Layout Presets**: Fix custom preset tile calculation ([2222222](https://github.com/Nicconike/LocalTelemetry/commit/2222222222222222222222222222222222222222))'));
            assert.ok(notes.includes('### 🤖 Continuous Integration & Workflows\n\n* **CI Workflows**: Enforce app token attribution ([3333333](https://github.com/Nicconike/LocalTelemetry/commit/3333333333333333333333333333333333333333))'));
            assert.ok(notes.includes('### 🧪 Tests & Test Coverage\n\n* **Taskbar Overlay**: Assert simulated automation exception ([4444444](https://github.com/Nicconike/LocalTelemetry/commit/4444444444444444444444444444444444444444))'));

            // Verify priority ordering: Features (90) -> Bug Fixes (80) -> CI (30) -> Tests (20)
            const featIdx = notes.indexOf('### 🚀 Features');
            const fixIdx = notes.indexOf('### 🐛 Bug Fixes');
            const ciIdx = notes.indexOf('### 🤖 Continuous Integration & Workflows');
            const testIdx = notes.indexOf('### 🧪 Tests & Test Coverage');

            assert.ok(featIdx < fixIdx);
            assert.ok(fixIdx < ciIdx);
            assert.ok(ciIdx < testIdx);
        });

        it('groups multiple items under the same domain as nested sub-bullets', () => {
            const commits = [
                {
                    hash: 'aaaaaaa111111111111111111111111111111111',
                    shortHash: 'aaaaaaa',
                    message: 'build(deps): bump dompurify to 3.4.17',
                },
                {
                    hash: 'bbbbbbb222222222222222222222222222222222',
                    shortHash: 'bbbbbbb',
                    message: 'build(deps): bump svelte to 5.57.2',
                },
                {
                    hash: 'ccccccc333333333333333333333333333333333',
                    shortHash: 'ccccccc',
                    message: 'build: update global.json SDK',
                },
            ];

            const notes = generateGitHubReleaseNotes({
                fromTag: 'v1.0.0',
                toRef: 'v1.1.0',
                version: '1.1.0',
                date: '14-11-2026',
                repositoryUrl: 'https://github.com/Nicconike/LocalTelemetry',
                commits,
            });

            // Dependencies has 2 items -> nested sub-bullets
            assert.ok(notes.includes('* **Dependencies**\n  * Bump dompurify to 3.4.17 ([aaaaaaa](https://github.com/Nicconike/LocalTelemetry/commit/aaaaaaa111111111111111111111111111111111))\n  * Bump svelte to 5.57.2 ([bbbbbbb](https://github.com/Nicconike/LocalTelemetry/commit/bbbbbbb222222222222222222222222222222222))'));
            // Build System has 1 item -> single line with colon
            assert.ok(notes.includes('* **Build System**: Update global.json SDK ([ccccccc](https://github.com/Nicconike/LocalTelemetry/commit/ccccccc333333333333333333333333333333333))'));
        });
    });
});
