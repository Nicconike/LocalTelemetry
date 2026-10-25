/**
 * semantic-release configuration for LocalTelemetry
 * https://semantic-release.gitbook.io/semantic-release/
 */
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';

const __dirname = dirname(fileURLToPath(import.meta.url));
const commitPluginPath = resolve(__dirname, 'github-commit-plugin.ts');

export default {
    branches: ['master'],
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
                        { type: 'build', section: 'Build System' },
                        { type: 'ci', section: 'Continuous Integration' },
                        { type: 'test', section: 'Tests' },
                        { type: 'style', section: 'Styles' },
                        { type: 'docs', section: 'Documentation' },
                        { type: 'chore', hidden: true },
                    ],
                },
                writerOpts: {
                    commitGroupsSort: (a, b) => {
                        const rank = {
                            'Features': 10,
                            'Bug Fixes': 9,
                            'Performance Improvements': 8,
                            'Reverts': 7,
                            'Code Refactoring': 6,
                            'Build System': 5,
                            'Continuous Integration': 4,
                            'Tests': 3,
                            'Styles': 2,
                            'Documentation': 1,
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
                changelogTitle:
                    '# Changelog\n\nAll notable changes to this project will be documented in this file.\n\nThe format is based on [Keep a Changelog](https://keepachangelog.com), and this project adheres to [Semantic Versioning](https://semver.org) & [Conventional Commits](https://conventionalcommits.org).',
            },
        ],
        [
            '@semantic-release/exec',
            {
                prepareCmd: 'pwsh -File ./.github/release/build.ps1 -Version "${nextRelease.version}" -Tag "${nextRelease.gitTag}"',
            },
        ],
        commitPluginPath,
        [
            '@semantic-release/github',
            {
                assets: [
                    { path: './LocalTelemetrySetup.exe', label: 'LocalTelemetry Setup' },
                    { path: './LocalTelemetry-win-x64.zip', label: 'LocalTelemetry Portable Zip' },
                    { path: './checksums.txt', label: 'SHA-256 Checksums' },
                    { path: './checksums.txt.sigstore.json', label: 'SHA-256 Checksums Sigstore Signature' },
                    { path: './LocalTelemetrySetup.exe.sigstore.json', label: 'LocalTelemetry Setup Sigstore Signature' },
                    { path: './LocalTelemetry-win-x64.zip.sigstore.json', label: 'LocalTelemetry Portable Zip Sigstore Signature' },
                ],
            },
        ],
    ],
};
