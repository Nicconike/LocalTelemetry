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
        vi.useFakeTimers();
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
        vi.advanceTimersByTime(3500);
        await tick();
        expect(document.body.textContent).toContain('Check for Updates');
        vi.useRealTimers();
    });

    it('formats build date across various date and time formats', async () => {
        // 1. Date only DD-MM-YYYY
        systemInfo.set({ buildDate: '23-10-2026' } as any);
        component = mount(AboutPage, { target: document.body });
        await tick();
        expect(document.body.textContent).toContain('23-10-2026');
        unmount(component);

        // 2. Date only YYYY-MM-DD
        systemInfo.set({ buildDate: '2026-10-23' } as any);
        component = mount(AboutPage, { target: document.body });
        await tick();
        expect(document.body.textContent).toContain('23-10-2026');
        unmount(component);

        // 3. 24-hour time without AM/PM
        systemInfo.set({ buildDate: '23-10-2026, 15:30' } as any);
        component = mount(AboutPage, { target: document.body });
        await tick();
        expect(document.body.textContent).toContain('03:30 PM');
        unmount(component);

        // 4. Raw fallback string
        systemInfo.set({ buildDate: 'CustomBuildTimeStamp' } as any);
        component = mount(AboutPage, { target: document.body });
        await tick();
        expect(document.body.textContent).toContain('CustomBuildTimeStamp');
        unmount(component);

        // 5. YMD with morning time
        systemInfo.set({ buildDate: '2026-10-23 08:15' } as any);
        component = mount(AboutPage, { target: document.body });
        await tick();
        expect(document.body.textContent).toContain('08:15 AM');
        unmount(component);

        // 6. 1.0.0.0 version trimming to 1.0
        systemInfo.set({ version: '1.0.0.0', exeDir: '', dataDir: '', logsDir: '' } as any);
        component = mount(AboutPage, { target: document.body });
        await tick();
        expect(document.body.textContent).toContain('v1.0');
        unmount(component);

        // 7. Missing version renders dash
        systemInfo.set({ version: '' } as any);
        component = mount(AboutPage, { target: document.body });
        await tick();
        expect(document.body.textContent).toContain('v-');
    });

    it('handles update check timeout and auto reset timers', async () => {
        vi.useFakeTimers();
        vi.spyOn(bridge, 'checkForUpdates').mockImplementation(() => { });

        component = mount(AboutPage, { target: document.body });
        await tick();

        const checkBtn = Array.from(document.body.querySelectorAll('button')).find(b => b.textContent?.includes('Check for Updates'));
        checkBtn!.click();
        await tick();

        // Advance by 10s to trigger check timeout
        vi.advanceTimersByTime(10000);
        await tick();
        expect(document.body.textContent).toContain('Request timed out');

        // Advance by 3.5s to trigger autoResetTimer back to idle
        vi.advanceTimersByTime(3500);
        await tick();
        expect(document.body.textContent).toContain('Check for Updates');

        vi.useRealTimers();
    });

    it('opens release URL when update is available and user clicks download', async () => {
        const openUrlSpy = vi.spyOn(bridge, 'openUrl').mockImplementation(() => { });

        component = mount(AboutPage, { target: document.body });
        await tick();

        wv.dispatchEvent(new MessageEvent('message', {
            data: {
                type: 'updateCheckResult',
                payload: {
                    isUpdateAvailable: true,
                    latestVersion: '2.0.0',
                    releaseUrl: 'https://github.com/Nicconike/LocalTelemetry/releases/tag/v2.0.0',
                    errorMessage: null,
                }
            }
        }));
        await tick();

        const updateBtn = Array.from(document.body.querySelectorAll('button')).find(b => b.textContent?.includes('Download'));
        expect(updateBtn).toBeDefined();
        updateBtn?.click();
        await tick();

        expect(openUrlSpy).toHaveBeenCalledWith('https://github.com/Nicconike/LocalTelemetry/releases/tag/v2.0.0');

        // Test latestVersion with 'v' prefix and empty releaseUrl fallback
        wv.dispatchEvent(new MessageEvent('message', {
            data: {
                type: 'updateCheckResult',
                payload: {
                    IsUpdateAvailable: true,
                    latestVersion: 'v2.0.1',
                    releaseUrl: '',
                    errorMessage: null,
                }
            }
        }));
        await tick();
        expect(document.body.textContent).toContain('v2.0.1 Available');
        updateBtn?.click();
        await tick();
        expect(openUrlSpy).toHaveBeenCalledWith('https://github.com/Nicconike/LocalTelemetry/releases');

        // Test autoResetTimer when error is received
        vi.useFakeTimers();
        wv.dispatchEvent(new MessageEvent('message', {
            data: {
                type: 'updateCheckResult',
                payload: { isUpdateAvailable: false, errorMessage: 'Rate limit' }
            }
        }));
        await tick();
        expect(document.body.textContent).toContain('Rate limit');
        vi.advanceTimersByTime(3500);
        await tick();
        expect(document.body.textContent).toContain('Check for Updates');

        // Test checking update timeout (10000ms checkTimeoutTimer)
        const checkBtn = Array.from(document.body.querySelectorAll('button')).find(b => b.textContent?.includes('Check for Updates'));
        checkBtn?.click();
        await tick();
        expect(document.body.textContent).toContain('Checking');
        vi.advanceTimersByTime(10000);
        await tick();
        expect(document.body.textContent).toContain('Request timed out');
        vi.advanceTimersByTime(3500);
        await tick();
        expect(document.body.textContent).toContain('Check for Updates');
        vi.useRealTimers();

        // Test settings without updates
        settings.set({ updates: null } as any);
        await tick();
        expect(document.querySelector('.update-settings-sub')).toBeNull();
    });

    it('covers standard and portable directory fallbacks when paths are undefined', async () => {
        // Standard mode with undefined paths
        systemInfo.set({
            version: undefined,
            buildDate: undefined,
            deploymentMode: undefined,
            isStandardMode: true,
            exeDir: undefined,
            dataDir: undefined,
            logsDir: undefined,
        } as any);

        component = mount(AboutPage, { target: document.body });
        await tick();

        expect(document.body.textContent).toContain('Standard');
        expect(document.body.textContent).toContain('C:\\Program Files\\LocalTelemetry');
        expect(document.body.textContent).toContain('%LocalAppData%\\LocalTelemetry');
        expect(document.body.textContent).toContain('%LocalAppData%\\LocalTelemetry\\logs');

        // Portable mode with undefined paths
        systemInfo.set({
            version: '1.0.0',
            isStandardMode: false,
            exeDir: undefined,
            logsDir: undefined,
        } as any);
        await tick();

        expect(document.body.textContent).toContain('Executable & Data Directory');
        expect(document.body.textContent).toContain('logs');
    });

    it('covers timer clearing on subsequent message and cleanup on unmount', async () => {
        vi.useFakeTimers();
        component = mount(AboutPage, { target: document.body });
        await tick();

        // 1. Dispatch update result (triggers autoResetTimer in upToDate state)
        wv.dispatchEvent(new MessageEvent('message', {
            data: {
                type: 'updateCheckResult',
                payload: { isUpdateAvailable: false }
            }
        }));
        await tick();
        expect(document.body.textContent).toContain('Up to date');

        // Advance 3500ms to fire line 68: updateStatus becomes 'idle'
        vi.advanceTimersByTime(3500);
        await tick();
        expect(document.body.textContent).toContain('Check for Updates');

        // 2. Dispatch error (sets autoResetTimer), then immediately dispatch another message while autoResetTimer is active (covers lines 35-36)
        wv.dispatchEvent(new MessageEvent('message', {
            data: {
                type: 'updateCheckResult',
                payload: { isUpdateAvailable: false, errorMessage: 'Temp error' }
            }
        }));
        await tick();
        expect(document.body.textContent).toContain('Temp error');

        // Immediately dispatch another result to clear active autoResetTimer (lines 35-36)
        wv.dispatchEvent(new MessageEvent('message', {
            data: {
                type: 'updateCheckResult',
                payload: { isUpdateAvailable: true, latestVersion: 'v3.0.0' }
            }
        }));
        await tick();
        expect(document.body.textContent).toContain('v3.0.0 Available');

        // 3. Start update check (sets checkTimeoutTimer) and unmount while active to cover lines 73-74

        wv.dispatchEvent(new MessageEvent('message', {
            data: {
                type: 'updateCheckResult',
                payload: { isUpdateAvailable: true, latestVersion: 'v3.0.0' }
            }
        }));
        await tick();
        expect(document.body.textContent).toContain('v3.0.0 Available');

        // 3. Start update check (sets checkTimeoutTimer) and unmount while active to cover lines 73-74
        const checkBtn = Array.from(document.body.querySelectorAll('button')).find(b => b.textContent?.includes('Check for Updates') || b.textContent?.includes('Download'));
        unmount(component);
        component = undefined;
        vi.useRealTimers();
    });

    it('covers all cleanVersion normalization and build date parsing branches', async () => {
        // 1. Two-component version normalization (e.g. 1.0.0.0 -> 1.0) and dmy without time
        systemInfo.set({
            version: '1.0.0.0',
            buildDate: '25-12-2025',
        } as any);

        component = mount(AboutPage, { target: document.body });
        await tick();
        expect(document.body.textContent).toContain('v1.0');
        expect(document.body.textContent).toContain('25-12-2025');

        // 2. dmy with 24-hour morning time (no AM/PM tag, < 12)
        systemInfo.set({
            version: '1.0',
            buildDate: '25-12-2025 08:45',
        } as any);
        await tick();
        expect(document.body.textContent).toContain('25-12-2025, 08:45 AM');

        // 3. ymd without time
        systemInfo.set({
            version: '2.1',
            buildDate: '2025-12-25',
        } as any);
        await tick();
        expect(document.body.textContent).toContain('25-12-2025');

        // 4. ymd with 24-hour afternoon time (>= 12, no AM/PM)
        systemInfo.set({
            version: '2.1.1',
            buildDate: '2025-12-25 16:20',
        } as any);
        await tick();
        expect(document.body.textContent).toContain('25-12-2025, 04:20 PM');

        // 5. ymd with AM/PM
        systemInfo.set({
            version: '2.1.2',
            buildDate: '2025-12-25 04:20 PM',
        } as any);
        await tick();
        expect(document.body.textContent).toContain('25-12-2025, 04:20 PM');

        // 6. completely unparseable date format
        systemInfo.set({
            version: '3.0',
            buildDate: 'CustomBuildTimestamp',
        } as any);
        await tick();
        expect(document.body.textContent).toContain('CustomBuildTimestamp');
    });

    it('renders Beta badge when available update is a pre-release', async () => {
        component = mount(AboutPage, { target: document.body });
        await tick();

        const msgEvent = new MessageEvent('message', {
            data: {
                type: 'updateCheckResult',
                payload: {
                    isUpdateAvailable: true,
                    isPrerelease: true,
                    latestVersion: '1.2.0-beta.1',
                    releaseUrl: 'https://github.com/Nicconike/LocalTelemetry/releases/tag/v1.2.0-beta.1',
                },
            },
        });
        wv.dispatchEvent(msgEvent);
        await tick();

        expect(document.body.textContent).toContain('v1.2.0-beta.1 Available');
        const betaBadge = document.querySelector('.update-badge.beta');
        expect(betaBadge).not.toBeNull();
        expect(betaBadge?.textContent).toBe('Beta');
    });

    it('does not render Beta badge when available update is a stable release', async () => {
        component = mount(AboutPage, { target: document.body });
        await tick();

        const msgEvent = new MessageEvent('message', {
            data: {
                type: 'updateCheckResult',
                payload: {
                    isUpdateAvailable: true,
                    isPrerelease: false,
                    latestVersion: '1.2.0',
                    releaseUrl: 'https://github.com/Nicconike/LocalTelemetry/releases/tag/v1.2.0',
                },
            },
        });
        wv.dispatchEvent(msgEvent);
        await tick();

        expect(document.body.textContent).toContain('v1.2.0 Available');
        const betaBadge = document.querySelector('.update-badge.beta');
        expect(betaBadge).toBeNull();
    });
});
