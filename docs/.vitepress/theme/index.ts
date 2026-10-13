import { h } from 'vue';
import DefaultTheme from 'vitepress/theme';
import { inBrowser, type EnhanceAppContext } from 'vitepress';
import PageFeedback from './PageFeedback.vue';
import BackToTop from './BackToTop.vue';
import VersionBanner from './VersionBanner.vue';
import MermaidViewer from './MermaidViewer.vue';
import VersionNavDropdown from './VersionNavDropdown.vue';
import './custom.css';

export default {
    extends: DefaultTheme,
    Layout() {
        return h(DefaultTheme.Layout, null, {
            'doc-before': () => h(VersionBanner),
            'doc-after': () => h(PageFeedback),
            'layout-bottom': () => [h(BackToTop), h(MermaidViewer)]
        });
    },
    enhanceApp({ app, router }: EnhanceAppContext) {
        app.component('VersionNavDropdown', VersionNavDropdown);

        if (inBrowser) {
            // Track page views on client-side route changes via Google Analytics (GA4)
            router.onAfterRouteChange = (to: string) => {
                if (typeof window !== 'undefined' && (window as any).gtag) {
                    (window as any).gtag('event', 'page_view', {
                        page_path: to
                    });
                }
            };

            // Global event tracking for external outbound clicks (GitHub, Releases, Discord)
            window.addEventListener('click', (e: MouseEvent) => {
                const target = (e.target as HTMLElement).closest('a');
                if (!target?.href) return;

                const url = target.href;
                if (url.includes('github.com/Nicconike/LocalTelemetry/releases')) {
                    if ((window as any).gtag) {
                        (window as any).gtag('event', 'download_click', {
                            event_category: 'outbound',
                            event_label: url
                        });
                    }
                } else if (url.includes('discord.gg')) {
                    if ((window as any).gtag) {
                        (window as any).gtag('event', 'discord_click', {
                            event_category: 'outbound',
                            event_label: url
                        });
                    }
                }
            });
        }
    }
};
