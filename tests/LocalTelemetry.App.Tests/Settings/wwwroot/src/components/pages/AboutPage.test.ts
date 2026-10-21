import { mount, unmount, tick } from 'svelte';
import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { systemInfo, settings } from '../../../../../../../src/LocalTelemetry.App/Settings/wwwroot/src/lib/store';
import * as bridge from '../../../../../../../src/LocalTelemetry.App/Settings/wwwroot/src/lib/bridge';
import AboutPage from '../../../../../../../src/LocalTelemetry.App/Settings/wwwroot/src/components/pages/AboutPage.svelte';

describe('AboutPage.svelte', () => {
    let component: ReturnType<typeof mount> | undefined;
    let wv: EventTarget;

    beforeEach(() => {
        document.body.innerHTML = '';
        wv = new EventTarget();
        (wv as any).postMessage = vi.fn();
        (globalThis as any).window.chrome = { webview: wv };

        systemInfo.set({
            version: '1.2.3.0',
            buildDate: '2026-10-21 14:30',
            deploymentMode: 'Standard',
            isStandardMode: true,
            exeDir: 'C:\\Program Files\\LocalTelemetry',
            dataDir: 'C:\\Users\\User\\AppData\\Local\\LocalTelemetry',
            logsDir: 'C:\\Users\\User\\AppData\\Local\\LocalTelemetry\\logs',
            targetRuntime: '.NET 10.0',
        } as any);

        settings.set({
            updates: {
                checkFrequency: 'weekly',
                includePrereleases: false,
            }
        } as any);
    });

    afterEach(() => {
        if (component) {
            unmount(component);
            component = undefined;
        }
        delete (globalThis as any).window.chrome;
        vi.restoreAllMocks();
    });

    it('renders application title, cleaned version, and standard directories', async () => {
        component = mount(AboutPage, { target: document.body });
        await tick();

        expect(document.body.textContent).toContain('LocalTelemetry');
        expect(document.body.textContent).toContain('v1.2.3');
        expect(document.body.textContent).toContain('Installation Folder');
        expect(document.body.textContent).toContain('Settings & Data Folder');
        expect(document.body.textContent).toContain('Logs Folder');
    });

    it('renders portable mode directory when isStandardMode is false', async () => {
        systemInfo.set({
            version: '2.0.0-rc.1',
            buildDate: '21-10-2026, 02:30 PM',
            deploymentMode: 'Portable',
            isStandardMode: false,
            exeDir: 'D:\\LocalTelemetry_Portable',
            logsDir: 'D:\\LocalTelemetry_Portable\\logs',
        } as any);

        component = mount(AboutPage, { target: document.body });
        await tick();

        expect(document.body.textContent).toContain('v2.0.0-rc.1');
        expect(document.body.textContent).toContain('Portable Application Folder');
    });

    it('triggers openFolder and openUrl when directory and link buttons are clicked', async () => {
        const openFolderSpy = vi.spyOn(bridge, 'openFolder').mockImplementation(() => { });
        const openUrlSpy = vi.spyOn(bridge, 'openUrl').mockImplementation(() => { });

        component = mount(AboutPage, { target: document.body });
        await tick();

        const installBtn = Array.from(document.body.querySelectorAll('button')).find(b => b.textContent?.includes('Installation Folder'));
        installBtn?.click();
        expect(openFolderSpy).toHaveBeenCalledWith('app');

        const docsBtn = Array.from(document.body.querySelectorAll('button')).find(b => b.textContent?.includes('Documentation'));
        docsBtn?.click();
        expect(openUrlSpy).toHaveBeenCalledWith('https://nicconike.github.io/LocalTelemetry/');
    });

    it('handles checking for updates and receives update check result payload', async () => {
        const checkUpdatesSpy = vi.spyOn(bridge, 'checkForUpdates').mockImplementation(() => { });
        const openUrlSpy = vi.spyOn(bridge, 'openUrl').mockImplementation(() => { });

        component = mount(AboutPage, { target: document.body });
        await tick();

        const checkBtn = Array.from(document.body.querySelectorAll('button')).find(b => b.textContent?.includes('Check for Updates'));
        expect(checkBtn).toBeDefined();

        checkBtn!.click();
        await tick();
        expect(checkUpdatesSpy).toHaveBeenCalledWith(true);

        // Simulate incoming updateAvailable message via bridge
        wv.dispatchEvent(new MessageEvent('message', {
            data: {
                type: 'updateCheckResult',
                payload: {
                    isUpdateAvailable: true,
                    latestVersion: '1.3.0',
                    releaseUrl: 'https://github.com/Nicconike/LocalTelemetry/releases/tag/v1.3.0',
                }
            }
        }));
        await tick();

        expect(document.body.textContent).toContain('v1.3.0 Available');
        const downloadBtn = Array.from(document.body.querySelectorAll('button')).find(b => b.textContent?.includes('Download'));
        downloadBtn?.click();
        expect(openUrlSpy).toHaveBeenCalledWith('https://github.com/Nicconike/LocalTelemetry/releases/tag/v1.3.0');
    });

    it('displays error badge when update check returns an error message', async () => {
        component = mount(AboutPage, { target: document.body });
        await tick();

        wv.dispatchEvent(new MessageEvent('message', {
            data: {
                type: 'updateCheckResult',
                payload: {
                    isUpdateAvailable: false,
                    errorMessage: 'Network timeout',
                }
            }
        }));
        await tick();

        expect(document.body.textContent).toContain('Network timeout');
    });

    it('displays up to date badge when no update is available', async () => {
        component = mount(AboutPage, { target: document.body });
        await tick();

        wv.dispatchEvent(new MessageEvent('message', {
            data: {
                type: 'updateCheckResult',
                payload: {
                    isUpdateAvailable: false,
                    errorMessage: null,
                }
            }
        }));
        await tick();

        expect(document.body.textContent).toContain('Up to date');
    });
});
