/**
 * semantic-release configuration for LocalTelemetry
 * https://semantic-release.gitbook.io/semantic-release/
 */
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';
import {
    formatReleaseDate,
    STANDARD_COMMIT_RANKS,
    STANDARD_RELEASE_RULES,
} from './changelog-domains.ts';

const __dirname = dirname(fileURLToPath(import.meta.url));
const commitPluginPath = resolve(__dirname, 'github-commit-plugin.ts');
const changelogDomainsPluginPath = resolve(__dirname, 'changelog-domains.ts');

export default {
    branches: [
        'master',
        { name: 'develop', prerelease: 'beta' },
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
                presetConfig: STANDARD_RELEASE_RULES,
                writerOpts: {
                    commitGroupsSort: (a, b) =>
                        (STANDARD_COMMIT_RANKS[b.title] ?? 0) - (STANDARD_COMMIT_RANKS[a.title] ?? 0),
                    commitsSort: (a, b) => {
                        const scopeCompare = (b.scope ?? '').localeCompare(a.scope ?? '');
                        if (scopeCompare !== 0) return scopeCompare;
                        return (b.subject ?? '').localeCompare(a.subject ?? '');
                    },
                    formatDate: formatReleaseDate,
                    finalizeContext: (context) => {
                        if (context.date) {
                            context.date = formatReleaseDate(context.date);
                        }
                        return context;
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
            changelogDomainsPluginPath,
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
