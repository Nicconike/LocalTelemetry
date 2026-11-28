/**
 * Telemetry and analytics helper for Umami Analytics.
 * Captures user actions and interaction metrics without storing sensitive personal data.
 */

declare global {
    interface Window {
        umami?: {
            track: {
                (eventName: string, eventData?: Record<string, unknown>): void;
                (customFunction: (props: Record<string, unknown>) => Record<string, unknown>): void;
            };
        };
    }
}

/**
 * Dispatches a named custom event to Umami.
 */
export function trackEvent(eventName: string, eventData: Record<string, unknown> = {}): void {
    if (typeof window === 'undefined') return;

    const dispatch = () => {
        try {
            if (window.umami && typeof window.umami.track === 'function') {
                window.umami.track(eventName, eventData);
            }
        } catch (err) {
            console.debug('[telemetry] Event dispatch failed:', err);
        }
    };

    if (window.umami) {
        dispatch();
    } else {
        setTimeout(dispatch, 300);
    }
}

/**
 * Dispatches a standard page view event to Umami.
 */
export function trackPageView(pagePath: string, pageTitle: string): void {
    if (typeof window === 'undefined') return;

    const dispatch = () => {
        try {
            if (window.umami && typeof window.umami.track === 'function') {
                window.umami.track((props: Record<string, unknown>) => ({
                    ...props,
                    url: pagePath,
                    title: pageTitle
                }));
            }
        } catch (err) {
            console.debug('[telemetry] Page view dispatch failed:', err);
        }
    };

    if (window.umami) {
        dispatch();
    } else {
        setTimeout(dispatch, 300);
    }
}

/**
 * Dispatches a 404 Not Found event without inflating regular page view counts.
 */
export function track404(brokenPath: string, referrer: string): void {
    trackEvent('page_not_found', {
        broken_path: brokenPath,
        referrer: referrer || '(direct)'
    });
}

let scroll50Tracked = false;
let scroll90Tracked = false;

/**
 * Resets scroll tracking thresholds when changing routes.
 */
export function resetScrollTracking(): void {
    scroll50Tracked = false;
    scroll90Tracked = false;
}

function handleScroll(): void {
    if (scroll90Tracked) return;

    const scrollElement = document.documentElement;
    const totalHeight = scrollElement.scrollHeight - window.innerHeight;
    if (totalHeight <= 0) return;

    const progress = (window.scrollY / totalHeight) * 100;

    if (!scroll50Tracked && progress >= 50) {
        scroll50Tracked = true;
        trackEvent('scroll_depth', {
            depth: 50,
            page_path: window.location.pathname
        });
    }

    if (!scroll90Tracked && progress >= 90) {
        scroll90Tracked = true;
        trackEvent('scroll_depth', {
            depth: 90,
            page_path: window.location.pathname
        });
    }
}

let searchDebounceTimer: ReturnType<typeof setTimeout> | null = null;

function handleInput(e: Event): void {
    const target = e.target as HTMLInputElement | null;
    if (!target) return;

    // Detect search input field in VitePress local search modal
    const isSearchInput =
        target.classList.contains('search-input') ||
        target.classList.contains('DocSearch-Input') ||
        target.closest('.VPLocalSearchBox') !== null;

    if (isSearchInput) {
        const query = target.value.trim();
        if (query.length >= 3) {
            if (searchDebounceTimer) clearTimeout(searchDebounceTimer);
            searchDebounceTimer = setTimeout(() => {
                trackEvent('search_query', {
                    query,
                    page_path: window.location.pathname
                });
            }, 800);
        }
    }
}

function handleClick(e: MouseEvent): void {
    const target = e.target as HTMLElement | null;
    if (!target) return;

    // 1. Code copy button click
    const copyButton = target.closest('button.copy');
    if (copyButton) {
        const codeBlock = copyButton.closest('div[class*="language-"]');
        const languageClass = codeBlock?.className
            .split(' ')
            .find((cls) => cls.startsWith('language-'));
        const language = languageClass ? languageClass.replace('language-', '') : 'plaintext';

        trackEvent('code_copy', {
            language,
            page_path: window.location.pathname
        });
        return;
    }

    // 2. Outbound / External link clicks
    const anchor = target.closest('a');
    if (!anchor || !anchor.href) return;

    const url = anchor.href;
    if (url.includes('github.com/Nicconike/LocalTelemetry/releases')) {
        trackEvent('download_click', {
            target_url: url,
            page_path: window.location.pathname
        });
    } else if (url.includes('discord.gg')) {
        trackEvent('discord_click', {
            target_url: url,
            page_path: window.location.pathname
        });
    } else if (url.includes('github.com/Nicconike/LocalTelemetry')) {
        trackEvent('github_click', {
            target_url: url,
            page_path: window.location.pathname
        });
    } else if (anchor.target === '_blank' || anchor.rel.includes('external')) {
        trackEvent('outbound_click', {
            target_url: url,
            page_path: window.location.pathname
        });
    }
}

let listenersInitialized = false;

/**
 * Initializes global browser event listeners for user action telemetry.
 */
export function initTelemetryListeners(): void {
    if (typeof window === 'undefined' || listenersInitialized) return;
    listenersInitialized = true;

    window.addEventListener('click', handleClick, { capture: true });
    window.addEventListener('input', handleInput, { passive: true });
    window.addEventListener('scroll', handleScroll, { passive: true });
}
