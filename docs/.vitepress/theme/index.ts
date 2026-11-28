import { h, nextTick } from 'vue';
import DefaultTheme from 'vitepress/theme';
import { inBrowser, type EnhanceAppContext } from 'vitepress';
import PageFeedback from './PageFeedback.vue';
import BackToTop from './BackToTop.vue';
import VersionBanner from './VersionBanner.vue';
import MermaidViewer from './MermaidViewer.vue';
import VersionNavDropdown from './VersionNavDropdown.vue';
import { initTelemetryListeners, resetScrollTracking, trackPageView, track404, trackEvent } from './telemetry.js';
import './custom.css';

function extractDocVersion(pathname: string): string {
    const match = pathname.match(/(?:^|\/)(v\d[\w.-]*)(?:\/|$)/);
    return match ? match[1] : 'Latest';
}

let currentDocVersion = typeof window !== 'undefined' ? extractDocVersion(window.location.pathname) : 'Latest';

function dispatchCurrentRouteTelemetry(path: string): void {
    nextTick(() => {
        const is404 =
            document.title.includes('404') ||
            document.title.toLowerCase().includes('page not found') ||
            document.querySelector('.VPNotFound') !== null;

        if (is404) {
            track404(path || window.location.pathname, document.referrer);
        } else {
            trackPageView(path || window.location.pathname, document.title);
        }
    });
}

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
            initTelemetryListeners();

            // Track initial page load after DOM hydration
            dispatchCurrentRouteTelemetry(window.location.pathname);

            // Track client-side route transitions
            router.onAfterRouteChange = (to: string) => {
                resetScrollTracking();

                const newVersion = extractDocVersion(to);
                if (newVersion !== currentDocVersion) {
                    trackEvent('version_switch', {
                        from_version: currentDocVersion,
                        to_version: newVersion,
                        page_path: to
                    });
                    currentDocVersion = newVersion;
                }

                dispatchCurrentRouteTelemetry(to);
            };
        }
    }
};
