import { mount, unmount, tick } from 'svelte';
import { get } from 'svelte/store';
import { settings, pristineSettings, activePage } from '../../../../../../src/LocalTelemetry.App/Settings/wwwroot/src/lib/store';
import type { SettingsDto } from '../../../../../../src/LocalTelemetry.App/Settings/wwwroot/src/lib/types';
import App from '../../../../../../src/LocalTelemetry.App/Settings/wwwroot/src/components/App.svelte';

function makePayload(): SettingsDto {
    return {
        runAtStartup: false,
        startMinimized: false,
        minimizeToTray: true,
        enableFileLogging: false,
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
            showToastNotif: true,
            flashOverlay: true,
            cooldownSecs: 30,
            fireOncePerSession: false,
        },
        netUsage: { enabled: false },
    };
}

function navButton(label: string): HTMLButtonElement {
    const buttons = Array.from(document.querySelectorAll<HTMLButtonElement>('.nav-item'));
    const match = buttons.find((b) => b.textContent?.trim() === label);
    if (!match) throw new Error(`sidebar item not found: ${label}`);
    return match;
}

describe('navigation guard', () => {
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
});
