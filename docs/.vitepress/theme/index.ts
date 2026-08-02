import DefaultTheme from 'vitepress/theme';
import { inBrowser, type EnhanceAppContext } from 'vitepress';
import './custom.css';

export default {
    extends: DefaultTheme,
    enhanceApp({ router }: EnhanceAppContext) {
        if (inBrowser) {
            // Track page views on route changes via Google Analytics (GA4)
            router.onAfterRouteChange = (to: string) => {
                if (typeof window !== 'undefined' && (window as any).gtag) {
                    (window as any).gtag('event', 'page_view', {
                        page_path: to
                    });
                }
            };
        }
    }
};
