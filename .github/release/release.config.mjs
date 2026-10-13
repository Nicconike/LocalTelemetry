/**
 * semantic-release configuration for LocalTelemetry
 * https://semantic-release.gitbook.io/semantic-release/
 */
export default {
    branches: [
        {
            name: 'master',
            prerelease: 'rc',
        },
    ],
    tagFormat: 'v${version}',
    plugins: [
        [
            '@semantic-release/commit-analyzer',
            {
                preset: 'conventionalcommits',
                releaseRules: [
                    { breaking: true, release: 'major' },
                    { type: 'feat', release: 'minor' },
                    { type: 'fix', release: 'patch' },
                    { type: 'perf', release: 'patch' },
                    { type: 'revert', release: 'patch' },
                    { type: 'refactor', release: false },
                    { type: 'style', release: false },
                    { type: 'test', release: false },
                    { type: 'build', release: false },
                    { type: 'ci', release: false },
                    { type: 'docs', release: false },
                    { type: 'chore', release: false },
                ],
            },
        ],
        [
            '@semantic-release/release-notes-generator',
            {
                preset: 'conventionalcommits',
                presetConfig: {
                    types: [
                        { type: 'feat', section: 'Features' },
                        { type: 'fix', section: 'Bug Fixes' },
                        { type: 'perf', section: 'Performance Improvements' },
                        { type: 'refactor', section: 'Code Refactoring' },
                        { type: 'docs', section: 'Documentation' },
                        { type: 'style', section: 'Styles' },
                        { type: 'test', section: 'Tests' },
                        { type: 'build', section: 'Build System' },
                        { type: 'ci', section: 'Continuous Integration' },
                        { type: 'chore', section: 'Chores' },
                        { type: 'revert', section: 'Reverts' },
                    ],
                },
                writerOpts: {
                    commitGroupsSort: (a, b) => {
                        const rank = {
                            'Features': 11,
                            'Bug Fixes': 10,
                            'Performance Improvements': 9,
                            'Code Refactoring': 8,
                            'Documentation': 7,
                            'Styles': 6,
                            'Tests': 5,
                            'Build System': 4,
                            'Continuous Integration': 3,
                            'Chores': 2,
                            'Reverts': 1,
                        };
                        return (rank[b.title] ?? 0) - (rank[a.title] ?? 0);
                    },
                    commitsSort: (a, b) => {
                        const scopeCompare = (b.scope ?? '').localeCompare(a.scope ?? '');
                        if (scopeCompare !== 0) return scopeCompare;
                        return (b.subject ?? '').localeCompare(a.subject ?? '');
                    },
                },
            },
        ],
        [
            '@semantic-release/changelog',
            {
                changelogFile: 'CHANGELOG.md',
            },
        ],
        [
            '@semantic-release/exec',
            {
                prepareCmd: 'pwsh -File ./.github/release/build.ps1 -Version "${nextRelease.version}" -Tag "${nextRelease.gitTag}"',
                publishCmd: 'pwsh -File ./.github/release/publish.ps1 -Version "${nextRelease.version}" -Tag "${nextRelease.gitTag}"',
            },
        ],
        [
            '@semantic-release/github',
            {
                assets: [
                    { path: './LocalTelemetrySetup.exe', label: 'LocalTelemetry Setup Installer' },
                    { path: './LocalTelemetry-win-x64.zip', label: 'LocalTelemetry Portable Archive' },
                    { path: './checksums.txt', label: 'SHA-256 Checksums' },
                ],
            },
        ],
        [
            '@semantic-release/git',
            {
                assets: [
                    'CHANGELOG.md',
                    'bucket/localtelemetry.json',
                    'packaging/chocolatey/localtelemetry.nuspec',
                    'packaging/chocolatey/tools/**',
                ],
                message: 'chore(release): ${nextRelease.version} [skip ci]\n\n${nextRelease.notes}',
            },
        ],
    ],
};
