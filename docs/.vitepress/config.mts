import { execFileSync } from 'node:child_process';
import { join } from 'node:path';
import { defineConfig, type HeadConfig } from 'vitepress';
import type { DefaultTheme } from 'vitepress/theme';
import { withMermaid } from 'vitepress-plugin-mermaid';
import { version as packageVersion } from '../package.json';
import { lucideMarkdownPlugin, lucideFrontmatterPlugin } from './markdown-lucide.js';
import { versions } from './versions.mjs';

const gitCandidates =
    process.platform === 'win32'
        ? [join(process.env.ProgramFiles ?? String.raw`C:\Program Files`, 'Git', 'cmd', 'git.exe')]
        : ['/usr/bin/git', '/usr/local/bin/git'];

const version = (() => {
    for (const git of gitCandidates) {
        try {
            const tag = execFileSync(git, ['describe', '--tags', '--abbrev=0'], { encoding: 'utf-8' }).trim();
            return tag.startsWith('v') ? tag.slice(1) : tag;
        } catch {
            continue; // Try next candidate; fall back below if none resolves.
        }
    }
    return packageVersion || 'dev';
})();
const isPrerelease = /[-+]/.test(version);

// Retrieve Measurement ID from environment variable
const gaId = process.env.GA_MEASUREMENT_ID;

// Subpath base for GitHub Pages project sites (e.g. /LocalTelemetry/)
const rawBase = process.env.VITE_BASE || '/';
const base = rawBase.endsWith('/') ? rawBase : `${rawBase}/`;

const headConfigs: HeadConfig[] = [
    ['link', { rel: 'icon', type: 'image/x-icon', href: `${base}app.ico` }],
    ['meta', { name: 'theme-color', content: '#0284c7' }],
    ['meta', { name: 'og:type', content: 'website' }],
    ['meta', { name: 'og:title', content: 'LocalTelemetry Documentation' }],
    ['meta', { name: 'og:description', content: 'Real-time hardware telemetry displayed on the Windows taskbar' }]
];

if (gaId && gaId !== 'GA-MEASUREMENT_ID') {
    headConfigs.push(
        ['script', { async: '', src: `https://www.googletagmanager.com/gtag/js?id=${gaId}` }],
        [
            'script',
            {},
            `
      window.dataLayer = window.dataLayer || [];
      function gtag(){dataLayer.push(arguments);}
      gtag('js', new Date());
      gtag('config', '${gaId}');
      `
        ]
    );
}

// Sidebar definitions with page links relative to each guide root
const userGuideSidebar = [
    {
        text: 'Getting Started',
        items: [
            { text: 'Overview & Introduction', link: '' },
            { text: 'Quickstart Guide', link: 'quickstart' },
            { text: 'Installation & Setup', link: 'installation' }
        ]
    },
    {
        text: 'Features & Customization',
        items: [
            { text: 'System Tray & Taskbar Overlay', link: 'tray-and-overlay' },
            { text: 'Telemetry Metrics & Sensors', link: 'metrics-and-sensors' },
            { text: 'Threshold Alerts & Toast Notifications', link: 'alerts-and-notifications' },
            { text: 'Traffic & Network History', link: 'network-history' },
            { text: 'Svelte 5 Settings & Customization', link: 'customization' }
        ]
    },
    {
        text: 'Help & Support',
        items: [
            { text: 'Troubleshooting & FAQ', link: 'troubleshooting' }
        ]
    }
];

const developerGuideSidebar = [
    {
        text: 'Developer Overview',
        items: [
            { text: 'Architecture & System Overview', link: '' },
            { text: 'Dev Environment Setup', link: 'setup' },
            { text: 'Repository Structure', link: 'repository-structure' }
        ]
    },
    {
        text: 'Technical Deep Dives',
        items: [
            { text: 'Core Engine & Backend (.NET 10)', link: 'backend-architecture' },
            { text: 'Hardware Drivers & Sensor Polling', link: 'hardware-drivers' },
            { text: 'Win32 Taskbar Hooking & Overlay', link: 'taskbar-overlay-interop' },
            { text: 'Svelte 5 Frontend & WebView2 Bridge', link: 'frontend-webview' },
            { text: 'Standalone Notifier IPC Subsystem', link: 'notifier-ipc' }
        ]
    },
    {
        text: 'Reference & Specifications',
        items: [
            { text: 'C# Core API & Models', link: 'reference/core-api' },
            { text: 'WebView2 & Named Pipe IPC Protocol', link: 'reference/ipc-protocol' },
            { text: 'settings.json Configuration Schema', link: 'reference/settings-schema' }
        ]
    },
    {
        text: 'Release & Contribution',
        items: [
            { text: 'Building & Inno Setup Packaging', link: 'building-and-packaging' },
            { text: 'CI/CD Pipelines & Workflows', link: 'ci-cd' },
            { text: 'Contributing & Coding Standards', link: 'contributing' }
        ]
    }
];

// Expand the relative sidebar definitions into one sidebar per docs version
function buildSidebar(): DefaultTheme.Sidebar {
    const sidebar: DefaultTheme.Sidebar = {};
    for (const { link: versionBase } of versions) {
        const base = versionBase === '/' ? '/' : versionBase;
        sidebar[`${base}user-guide/`] = userGuideSidebar.map((group) => ({
            ...group,
            items: group.items.map(({ text, link }) => ({
                text,
                link: `${base}user-guide/${link}`
            }))
        }));
        sidebar[`${base}developer-guide/`] = developerGuideSidebar.map((group) => ({
            ...group,
            items: group.items.map(({ text, link }) => ({
                text,
                link: `${base}developer-guide/${link}`
            }))
        }));
    }
    return sidebar;
}

export default withMermaid(
    defineConfig({
        title: 'LocalTelemetry',
        description: 'Real-time hardware telemetry displayed on the Windows taskbar',
        lang: 'en-US',
        base,
        cleanUrls: true,
        lastUpdated: true,
        head: headConfigs,

        markdown: {
            headers: {
                level: [2, 3]
            },
            config: (md) => {
                md.use(lucideMarkdownPlugin);
            }
        },

        rewrites: {
            'latest/index.md': 'index.md',
            'latest/user-guide/:path': 'user-guide/:path',
            'latest/developer-guide/:path': 'developer-guide/:path',
            'latest/developer-guide/reference/:path': 'developer-guide/reference/:path'
        },

        mermaid: {
            theme: 'dark',
            themeVariables: {
                fontFamily: 'Inter, system-ui, sans-serif',
                fontSize: '13px'
            },
            flowchart: {
                useMaxWidth: false,
                htmlLabels: true,
                padding: 16
            },
            sequence: {
                useMaxWidth: false,
                showSequenceNumbers: true
            }
        },

        vite: {
            plugins: [lucideFrontmatterPlugin()],
            optimizeDeps: {
                include: ['fastdom', 'mermaid', 'debug', 'dayjs']
            },
            build: {
                chunkSizeWarningLimit: 3000
            }
        },

        themeConfig: {
            siteTitle: 'LocalTelemetry',
            logo: '/app.ico',

            nav: [
                { text: 'Home', link: '/' },
                {
                    text: 'User Guide',
                    link: '/user-guide/',
                    activeMatch: '^/(?:v\\d[^/]+/)?user-guide'
                },
                {
                    text: 'Developer Guide',
                    link: '/developer-guide/',
                    activeMatch: '^/(?:v\\d[^/]+/)?developer-guide'
                },
                {
                    text: 'Latest',
                    items: [
                        ...versions.map((v) => ({ text: v.text, link: v.link })),
                        {
                            items: [
                                { text: 'Changelog', link: 'https://github.com/Nicconike/LocalTelemetry/blob/master/CHANGELOG.md' },
                                { text: 'App Releases', link: 'https://github.com/Nicconike/LocalTelemetry/releases' }
                            ]
                        }
                    ]
                }
            ],

            sidebar: buildSidebar(),

            editLink: {
                pattern: 'https://github.com/Nicconike/LocalTelemetry/edit/master/docs/:path',
                text: 'Edit this page on GitHub'
            },

            docFooter: {
                prev: 'Previous page',
                next: 'Next page'
            },

            lastUpdated: {
                text: 'Last updated',
                formatOptions: {
                    dateStyle: 'medium',
                    timeStyle: 'short'
                }
            },

            search: {
                provider: 'local'
            },

            outline: {
                level: [2, 3],
                label: 'On this page'
            },

            returnToTopLabel: 'Return to top',
            externalLinkIcon: true,

            socialLinks: [
                { icon: 'discord', link: 'https://discord.gg/M2QevSgW3S' },
                { icon: 'github', link: 'https://github.com/Nicconike/LocalTelemetry' }
            ],

            footer: {
                message: 'Released under the GNU General Public License v3.0',
                copyright: 'Copyright © 2026 Nicconike'
            }
        }
    })
);
