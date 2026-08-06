import { defineConfig, type HeadConfig } from 'vitepress';
import { withMermaid } from 'vitepress-plugin-mermaid';
import { version } from '../package.json';

// Retrieve Measurement ID from environment variable
const gaId = process.env.GA_MEASUREMENT_ID;

// Subpath base for GitHub Pages project sites (e.g. /LocalTelemetry/)
const base = process.env.VITE_BASE || '/';

const headConfigs: HeadConfig[] = [
    ['link', { rel: 'icon', type: 'image/x-icon', href: `${base}app.ico` }],
    ['meta', { name: 'theme-color', content: '#0284c7' }],
    ['meta', { name: 'og:type', content: 'website' }],
    ['meta', { name: 'og:title', content: 'LocalTelemetry Documentation' }],
    ['meta', { name: 'og:description', content: 'Embed real-time CPU, GPU, RAM, Disk and Network telemetry directly into your Windows Taskbar.' }]
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

export default withMermaid(
    defineConfig({
        title: 'LocalTelemetry',
        description: 'Lightweight, real-time Windows taskbar hardware monitoring utility',
        lang: 'en-US',
        base,
        cleanUrls: true,
        lastUpdated: true,
        head: headConfigs,

        mermaid: {
            theme: 'dark',
            themeVariables: {
                fontFamily: 'Inter, system-ui, sans-serif',
                fontSize: '13px'
            },
            flowchart: {
                useMaxWidth: true,
                htmlLabels: true,
                padding: 16
            },
            sequence: {
                useMaxWidth: true,
                showSequenceNumbers: true
            }
        },

        themeConfig: {
            siteTitle: 'LocalTelemetry',
            logo: '/app.ico',

            nav: [
                { text: 'Home', link: '/' },
                { text: 'User Guide', link: '/user-guide/' },
                { text: 'Developer Guide', link: '/developer-guide/' },
                {
                    text: `v${version}`,
                    items: [
                        { text: `v${version} (Prerelease)`, link: '/user-guide/' },
                        { text: 'Changelog', link: 'https://github.com/Nicconike/LocalTelemetry/blob/master/CHANGELOG.md' },
                        { text: 'App Releases', link: 'https://github.com/Nicconike/LocalTelemetry/releases' }
                    ]
                }
            ],

            sidebar: {
                '/user-guide/': [
                    {
                        text: 'Getting Started',
                        items: [
                            { text: 'Overview & Introduction', link: '/user-guide/' },
                            { text: 'Installation & Setup', link: '/user-guide/installation' },
                            { text: 'Quickstart Guide', link: '/user-guide/quickstart' }
                        ]
                    },
                    {
                        text: 'Features & Customization',
                        items: [
                            { text: 'System Tray & Taskbar Overlay', link: '/user-guide/tray-and-overlay' },
                            { text: 'Telemetry Metrics & Sensors', link: '/user-guide/metrics-and-sensors' },
                            { text: 'Threshold Alerts & Toast Notifications', link: '/user-guide/alerts-and-notifications' },
                            { text: 'Svelte 5 Settings & Customization', link: '/user-guide/customization' },
                            { text: 'Traffic & Network History', link: '/user-guide/network-history' }
                        ]
                    },
                    {
                        text: 'Help & Support',
                        items: [
                            { text: 'Troubleshooting & FAQ', link: '/user-guide/troubleshooting' }
                        ]
                    }
                ],

                '/developer-guide/': [
                    {
                        text: 'Developer Overview',
                        items: [
                            { text: 'Architecture & System Overview', link: '/developer-guide/' },
                            { text: 'Dev Environment Setup', link: '/developer-guide/setup' },
                            { text: 'Repository Structure', link: '/developer-guide/repository-structure' }
                        ]
                    },
                    {
                        text: 'Technical Deep Dives',
                        items: [
                            { text: 'Core Engine & App Backend (.NET 10)', link: '/developer-guide/backend-architecture' },
                            { text: 'Hardware Drivers & PawnIo Integration', link: '/developer-guide/hardware-drivers' },
                            { text: 'Win32 Taskbar Hooking & Overlay', link: '/developer-guide/taskbar-overlay-interop' },
                            { text: 'Svelte 5 Frontend & WebView2 Bridge', link: '/developer-guide/frontend-webview2' },
                            { text: 'Standalone Notifier IPC Subsystem', link: '/developer-guide/notifier-ipc' }
                        ]
                    },
                    {
                        text: 'Release & Contribution',
                        items: [
                            { text: 'Building & Inno Setup Packaging', link: '/developer-guide/building-and-packaging' },
                            { text: 'CI/CD Pipelines & GitHub Workflows', link: '/developer-guide/ci-cd' },
                            { text: 'Contributing & Coding Standards', link: '/developer-guide/contributing' }
                        ]
                    }
                ]
            },

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

            socialLinks: [
                { icon: 'github', link: 'https://github.com/Nicconike/LocalTelemetry' }
            ],

            footer: {
                message: 'Released under the GNU General Public License v3.0.',
                copyright: 'Copyright © 2026 Nicconike'
            }
        }
    })
);
