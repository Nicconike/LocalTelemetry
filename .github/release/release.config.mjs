/**
 * semantic-release configuration for LocalTelemetry
 * https://semantic-release.gitbook.io/semantic-release/
 */
export default {
    branches: [
        'master',
        {
            name: 'rc',
            prerelease: true,
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
                        { type: 'revert', section: 'Reverts' },
                        { type: 'refactor', section: 'Code Refactoring' },
                        { type: 'docs', section: 'Documentation' },
                        { type: 'style', section: 'Styles' },
                        { type: 'test', section: 'Tests' },
                        { type: 'build', section: 'Build System' },
                        { type: 'ci', section: 'Continuous Integration' },
                        { type: 'chore', section: 'Chores' },
                    ],
                },
                writerOpts: {
                    commitGroupsSort: (a, b) => {
                        const rank = {
                            'Features': 11,
                            'Bug Fixes': 10,
                            'Performance Improvements': 9,
                            'Reverts': 8,
                            'Code Refactoring': 7,
                            'Documentation': 6,
                            'Styles': 5,
                            'Tests': 4,
                            'Build System': 3,
                            'Continuous Integration': 2,
                            'Chores': 1,
                        };
                        return (rank[b.title] ?? 0) - (rank[a.title] ?? 0);
                    },
                    commitsSort: ['-scope', '-subject'],
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
                prepareCmd: 'pwsh -File ./.github/release/build.ps1 -Version "${nextRelease.version}"',
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
                assets: ['CHANGELOG.md', 'bucket/localtelemetry.json', 'packaging/chocolatey/**'],
                message: 'chore(release): ${nextRelease.version} [skip ci]\n\n${nextRelease.notes}',
            },
        ],
    ],
};
