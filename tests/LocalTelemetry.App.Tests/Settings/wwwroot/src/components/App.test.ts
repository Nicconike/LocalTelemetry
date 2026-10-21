import { mount, unmount, tick } from 'svelte';
import { get } from 'svelte/store';
import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { settings, pristineSettings, activePage } from '../../../../../../src/LocalTelemetry.App/Settings/wwwroot/src/lib/store';
import type { SettingsDto } from '../../../../../../src/LocalTelemetry.App/Settings/wwwroot/src/lib/types';
import App from '../../../../../../src/LocalTelemetry.App/Settings/wwwroot/src/components/App.svelte';

function makePayload(): SettingsDto {
    return {
        runAtStartup: false,
        showSystemTrayIcon: true,
        enableFileLogging: true,
        systemLogLevel: 'Info',
        windowTheme: 'default',
        monitoring: {
            intervalMs: 1000,
            useFahrenheit: false,
            useNetBits: false,
            preferredNic: 'auto',
            hasBattery: false,
            trackCpu: true,
            trackGpu: true,
            trackRam: true,
            trackNet: true,
            trackDisk: true,
            trackBattery: false,
            gpuUsageSource: 'driver',
            logCpuMode: 0,
            logGpuMode: 0,
            logRamMode: 0,
            logNetMode: 0,
            logDiskMode: 0,
            logBatteryMode: 0,
        },
        overlay: {
            visible: true,
            doubleClickAction: 'none',
            position: 'left',
            offsetX: 0,
            opacity: 100,
            scale: 100,
            bgColor: '#000000',
            textColor: '#FFFFFF',
            fontSizePx: 18,
            fontBold: false,
            labelColor: '#FFFFFF',
            metricColors: {},
            groupColors: {},
            defaultMetricColors: {},
            followWindowsTheme: true,
            row1: [],
            userCustomizedMetricColors: [],
            userCustomizedGroupColors: [],
        },
        alerts: {
            enabled: false,
            alertCpuTemp: false,
            alertGpuTemp: false,
            alertCpuUsage: false,
            alertRamUsage: false,
            alertGpuUsage: false,
            alertGpuVram: false,
            alertBatteryLow: false,
            alertCpuFreq: false,
            alertCpuPower: false,
            alertGpuFreq: false,
            alertGpuPower: false,
            alertNetDaily: false,
            alertNetMonthly: false,
            cpuUsageMaxPct: 90,
            ramUsageMaxPct: 90,
            gpuUsageMaxPct: 90,
            gpuVramMaxMb: 8192,
            cpuTempMaxC: 90,
            gpuTempMaxC: 90,
            batteryLowPct: 20,
            cpuFreqMinMhz: 400,
            cpuPowerMaxW: 100,
            gpuFreqMinMhz: 400,
            gpuPowerMaxW: 300,
            netDailyMaxGb: 50,
            netMonthlyMaxGb: 1000,
            showToastNotif: true,
            flashOverlay: true,
            cooldownSecs: 30,
            fireOncePerSession: false,
        },
        netUsage: { enabled: false },
        updates: { checkFrequency: 'weekly', includePrereleases: true },
    };
}

function navButton(label: string): HTMLButtonElement {
    const buttons = Array.from(document.querySelectorAll<HTMLButtonElement>('.nav-item'));
    const match = buttons.find((b) => b.textContent?.trim() === label);
    if (!match) throw new Error(`sidebar item not found: ${label}`);
    return match;
}

describe('App.svelte', () => {
    let component: ReturnType<typeof mount> | undefined;

    beforeEach(() => {
        document.body.innerHTML = '';
        settings.set(null);
        pristineSettings.set('');
        activePage.set('alerts');
    });

    afterEach(() => {
        if (component) {
            unmount(component);
            component = undefined;
        }
        delete (globalThis as any).window?.chrome;
    });

    it('navigates directly when there are no unsaved changes', async () => {
        const payload = makePayload();
        settings.set(structuredClone(payload));
        pristineSettings.set(JSON.stringify(payload));

        component = mount(App, { target: document.body });
        await tick();

        navButton('General').click();
        await tick();

        expect(get(activePage)).toBe('general');
        expect(document.querySelector('.nav-modal-overlay')).toBeNull();
    });

    it('shows the unsaved-changes modal and cancel keeps the page', async () => {
        const payload = makePayload();
        settings.set(structuredClone(payload));
        pristineSettings.set(JSON.stringify(payload));

        component = mount(App, { target: document.body });
        await tick();

        const toggle = document.querySelector(
            'input[aria-label="Enable Threshold Alerts"]',
        ) as HTMLInputElement;
        toggle.checked = true;
        toggle.dispatchEvent(new Event('change'));
        await tick();

        navButton('General').click();
        await tick();

        expect(document.querySelector('.nav-modal-overlay')).not.toBeNull();
        expect(get(activePage)).toBe('alerts');

        const cancel = Array.from(document.querySelectorAll<HTMLButtonElement>('.nav-modal button')).find(
            (b) => b.textContent?.trim() === 'Cancel',
        )!;
        cancel.click();
        await tick();

        expect(document.querySelector('.nav-modal-overlay')).toBeNull();
        expect(get(activePage)).toBe('alerts');
    });

    it('discard moves to the target page and reverts unsaved changes', async () => {
        const payload = makePayload();
        settings.set(structuredClone(payload));
        pristineSettings.set(JSON.stringify(payload));

        component = mount(App, { target: document.body });
        await tick();

        const toggle = document.querySelector(
            'input[aria-label="Enable Threshold Alerts"]',
        ) as HTMLInputElement;
        toggle.checked = true;
        toggle.dispatchEvent(new Event('change'));
        await tick();

        navButton('General').click();
        await tick();
        expect(document.querySelector('.nav-modal-overlay')).not.toBeNull();

        const discard = Array.from(document.querySelectorAll<HTMLButtonElement>('.nav-modal button')).find(
            (b) => b.textContent?.trim() === 'Discard',
        )!;
        discard.click();
        await tick();

        expect(get(activePage)).toBe('general');
        expect(document.querySelector('.nav-modal-overlay')).toBeNull();
        expect(get(settings)).toEqual(payload);
    });

    it('toggling alerts on then off hides the save bar', async () => {
        const payload = makePayload();
        settings.set(payload);
        pristineSettings.set(JSON.stringify(payload));

        component = mount(App, { target: document.body });
        await tick();

        const bar = document.querySelector('.save-bar');
        expect(bar).not.toBeNull();

        const toggle = document.querySelector(
            'input[aria-label="Enable Threshold Alerts"]',
        ) as HTMLInputElement;
        expect(toggle).not.toBeNull();

        expect(bar!.classList.contains('visible')).toBe(false);

        toggle.checked = true;
        toggle.dispatchEvent(new Event('change'));
        await tick();
        expect(bar!.classList.contains('visible')).toBe(true);

        toggle.checked = false;
        toggle.dispatchEvent(new Event('change'));
        await tick();
        expect(bar!.classList.contains('visible')).toBe(false);
    });

    it('initializes bridge messages and handles incoming webview events', async () => {
        const postedMessages: any[] = [];
        const wv = new EventTarget();
        (wv as any).postMessage = (msg: any) => postedMessages.push(msg);
        (globalThis as any).window.chrome = {
            webview: wv,
        };

        const payload = makePayload();
        component = mount(App, { target: document.body });
        await tick();

        expect(postedMessages.some(m => m.type === 'getSettings')).toBe(true);

        // Simulate incoming settings message
        wv.dispatchEvent(new MessageEvent('message', {
            data: { type: 'settings', payload }
        }));
        await tick();
        expect(get(settings)).toEqual(payload);

        // Simulate incoming saved message
        wv.dispatchEvent(new MessageEvent('message', {
            data: { type: 'saved' }
        }));
        await tick();

        // Simulate nics
        wv.dispatchEvent(new MessageEvent('message', {
            data: { type: 'nics', payload: ['Ethernet', 'WiFi'] }
        }));
        await tick();

        // Simulate systemInfo
        wv.dispatchEvent(new MessageEvent('message', {
            data: { type: 'systemInfo', payload: { os: 'Windows 11' } }
        }));
        await tick();

        // Simulate trafficHistoryAll
        wv.dispatchEvent(new MessageEvent('message', {
            data: {
                type: 'trafficHistoryAll',
                payload: { records: [{ date: '01-10-2026', interfaceName: 'Ethernet', downBytes: 100, upBytes: 50 }], todayDown: 100, todayUp: 50 }
            }
        }));
        await tick();

        // Simulate trafficToday
        wv.dispatchEvent(new MessageEvent('message', {
            data: { type: 'trafficToday', payload: { downBytes: 200, upBytes: 100 } }
        }));
        await tick();

        // Simulate importDatResult
        wv.dispatchEvent(new MessageEvent('message', {
            data: { type: 'importDatResult', payload: { daysImported: 2 } }
        }));
        await tick();

        // Simulate windowsUsageImportResult
        wv.dispatchEvent(new MessageEvent('message', {
            data: { type: 'windowsUsageImportResult', payload: { success: true, daysImported: 3 } }
        }));
        await tick();

        delete (globalThis as any).window.chrome;
    });

    it('supports saving via Save Changes button and save & move modal action', async () => {
        const wv = new EventTarget() as any;
        wv.postMessage = vi.fn();
        (globalThis as any).window.chrome = { webview: wv };

        const payload = makePayload();
        settings.set(structuredClone(payload));
        pristineSettings.set(JSON.stringify(payload));

        component = mount(App, { target: document.body });
        await tick();

        // Modify settings to make it dirty
        settings.update(s => {
            if (s) s.monitoring.intervalMs = 2500;
            return s;
        });
        await tick();

        const saveBtn = document.querySelector('.save-bar .btn-save') as HTMLButtonElement;
        expect(saveBtn).not.toBeNull();
        saveBtn.click();
        await tick();

        // Simulate saved event from bridge to finish saving
        wv.dispatchEvent(new MessageEvent('message', {
            data: { type: 'saved' }
        }));
        await tick();

        // Re-dirty settings so navigation triggers the modal
        settings.update(s => {
            if (s) s.monitoring.intervalMs = 3000;
            return s;
        });
        await tick();

        // Try navigating while dirty to open nav modal
        navButton('Monitoring').click();
        await tick();

        const saveAndMoveBtn = Array.from(document.querySelectorAll<HTMLButtonElement>('.nav-modal button')).find(
            b => b.textContent?.trim() === 'Save'
        );
        expect(saveAndMoveBtn).toBeDefined();
        saveAndMoveBtn!.click();
        await tick();

        // Simulate saved event after modal save
        wv.dispatchEvent(new MessageEvent('message', {
            data: { type: 'saved' }
        }));
        await tick();

        expect(get(activePage)).toBe('monitoring');
    });

    it('navigates through all application pages', async () => {
        const payload = makePayload();
        settings.set(structuredClone(payload));
        pristineSettings.set(JSON.stringify(payload));

        component = mount(App, { target: document.body });
        await tick();

        const pages = ['Monitoring', 'Overlay', 'Layout', 'Network', 'Traffic', 'Alerts', 'System', 'Appearance', 'About', 'General'];
        for (const p of pages) {
            navButton(p).click();
            await tick();
            expect(get(activePage)).toBe(p.toLowerCase());
        }
    });
});

