import { mount, unmount, tick } from 'svelte';
import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { activePage, systemInfo, settings } from '../../../../../../src/LocalTelemetry.App/Settings/wwwroot/src/lib/store';
import * as bridge from '../../../../../../src/LocalTelemetry.App/Settings/wwwroot/src/lib/bridge';
import Sidebar from '../../../../../../src/LocalTelemetry.App/Settings/wwwroot/src/components/Sidebar.svelte';

describe('Sidebar.svelte', () => {
    let component: ReturnType<typeof mount> | undefined;
    let wv: EventTarget;

    beforeEach(() => {
        document.body.innerHTML = '';
        activePage.set('general');
        wv = new EventTarget();
        (wv as any).postMessage = vi.fn();
        (globalThis as any).window.chrome = { webview: wv };
        systemInfo.set({ appVersion: '1.3.0-beta.2' } as any);
        settings.set({ updates: { includePrereleases: false, checkFrequency: 'weekly' } } as any);
    });

    afterEach(() => {
        if (component) {
            unmount(component);
            component = undefined;
        }
        delete (globalThis as any).window.chrome;
        vi.restoreAllMocks();
    });

    it('renders sidebar navigation items', async () => {
        component = mount(Sidebar, { target: document.body });
        await tick();

        const items = document.querySelectorAll('.nav-item');
        expect(items.length).toBeGreaterThan(5);
        expect(document.body.textContent).toContain('General');
        expect(document.body.textContent).toContain('Monitoring');
        expect(document.body.textContent).toContain('About');
    });

    it('navigates when clicking a navigation item', async () => {
        let dispatchedPage = '';
        component = mount(Sidebar, {
            target: document.body,
            events: {
                navigate: (e: any) => {
                    dispatchedPage = e.detail;
                }
            }
        });
        await tick();

        const aboutBtn = Array.from(document.querySelectorAll<HTMLButtonElement>('.nav-item')).find((b) =>
            b.textContent?.includes('About')
        );
        expect(aboutBtn).toBeDefined();
        aboutBtn?.click();
        await tick();

        expect(dispatchedPage).toBe('about');
    });

    it('updates active state when activePage changes and handles click without listener', async () => {
        component = mount(Sidebar, { target: document.body });
        await tick();

        activePage.set('network');
        await tick();

        const networkBtn = Array.from(document.querySelectorAll<HTMLButtonElement>('.nav-item')).find((b) =>
            b.textContent?.includes('Network')
        );
        expect(networkBtn?.classList.contains('active')).toBe(true);
        expect(networkBtn?.getAttribute('aria-current')).toBe('page');

        networkBtn?.click();
        await tick();

        activePage.set('none');
        await tick();
        const activeItems = document.querySelectorAll('.nav-item.active');
        expect(activeItems).toHaveLength(0);
    });

    it('renders CTA footer with Star, Discord, and stable version when prereleases disabled', async () => {
        const openUrlSpy = vi.spyOn(bridge, 'openUrl').mockImplementation(() => { });
        let dispatched = '';
        component = mount(Sidebar, {
            target: document.body,
            events: {
                navigate: (e: any) => {
                    dispatched = e.detail;
                }
            }
        });
        await tick();

        const starBtn = document.querySelector<HTMLButtonElement>('.star-btn');
        const discordBtn = document.querySelector<HTMLButtonElement>('.discord-btn');
        const verBtn = document.querySelector<HTMLButtonElement>('.version-btn');

        expect(starBtn).not.toBeNull();
        expect(discordBtn).not.toBeNull();
        // Since includePrereleases is false, stable version is shown (v1.2.0)
        expect(verBtn?.textContent?.trim()).toBe('v1.2.0');

        starBtn?.click();
        expect(openUrlSpy).toHaveBeenCalledWith('https://github.com/Nicconike/LocalTelemetry');

        discordBtn?.click();
        expect(openUrlSpy).toHaveBeenCalledWith('https://discord.gg/M2QevSgW3S');

        verBtn?.click();
        await tick();
        expect(dispatched).toBe('about');
    });

    it('shows latest beta version when includePrereleases is true in settings', async () => {
        settings.set({ updates: { includePrereleases: true, checkFrequency: 'weekly' } } as any);
        component = mount(Sidebar, { target: document.body });
        await tick();

        const verBtn = document.querySelector<HTMLButtonElement>('.version-btn');
        expect(verBtn?.textContent?.trim()).toBe('v1.3.0-beta.2');

        // Simulate update check returning a newer beta
        const msgEvent = new MessageEvent('message', {
            data: {
                type: 'updateCheckResult',
                payload: {
                    isUpdateAvailable: true,
                    isPrerelease: true,
                    latestVersion: '1.3.0-beta.3',
                    currentVersion: '1.3.0-beta.2'
                }
            }
        });
        wv.dispatchEvent(msgEvent);
        await tick();

        expect(verBtn?.textContent?.trim()).toBe('v1.3.0-beta.3');
        expect(verBtn?.classList.contains('has-update')).toBe(true);
    });

    it('shows latest stable version when updateCheckResult returns stable release with prerelease disabled', async () => {
        settings.set({ updates: { includePrereleases: false, checkFrequency: 'weekly' } } as any);
        component = mount(Sidebar, { target: document.body });
        await tick();

        const msgEvent = new MessageEvent('message', {
            data: {
                type: 'updateCheckResult',
                payload: {
                    isUpdateAvailable: false,
                    isPrerelease: false,
                    latestVersion: '1.2.0',
                    currentVersion: '1.3.0-beta.2'
                }
            }
        });
        wv.dispatchEvent(msgEvent);
        await tick();

        const verBtn = document.querySelector<HTMLButtonElement>('.version-btn');
        expect(verBtn?.textContent?.trim()).toBe('v1.2.0');
    });

    it('navigates to about page on version button click', async () => {
        let dispatched = '';
        component = mount(Sidebar, {
            target: document.body,
            events: {
                navigate: (e: any) => {
                    dispatched = e.detail;
                }
            }
        });
        await tick();

        const verBtn = document.querySelector<HTMLButtonElement>('.version-btn');
        expect(verBtn).not.toBeNull();

        verBtn?.click();
        await tick();
        expect(dispatched).toBe('about');
    });
});
