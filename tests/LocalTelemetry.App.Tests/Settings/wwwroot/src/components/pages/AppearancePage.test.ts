import { mount, unmount, tick } from 'svelte';
import { get } from 'svelte/store';
import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { settings, systemInfo } from '../../../../../../../src/LocalTelemetry.App/Settings/wwwroot/src/lib/store';
import type { SettingsDto, SystemInfo } from '../../../../../../../src/LocalTelemetry.App/Settings/wwwroot/src/lib/types';
import AppearancePage from '../../../../../../../src/LocalTelemetry.App/Settings/wwwroot/src/components/pages/AppearancePage.svelte';

const BRAND = {
    cpu_pct: '#0068B5',
    gpu_pct: '#76B900',
    ram_pct: '#005BAB',
    net_down: '#006DB6',
    battery_pct: '#005691',
};

function makePayload(): SettingsDto {
    return {
        runAtStartup: false,
        showSystemTrayIcon: true,
        enableFileLogging: false,
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
            metricColors: { ...BRAND },
            groupColors: {},
            defaultMetricColors: { ...BRAND },
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

function groupColorInput(groupName: string): HTMLInputElement | null {
    const rows = document.querySelectorAll('.group-color-row');
    for (const row of rows) {
        const label = row.querySelector('.color-label')?.textContent ?? '';
        if (label.trim() === groupName) {
            return row.querySelector('input[type="color"]');
        }
    }
    return null;
}

function groupHexInput(groupName: string): HTMLInputElement | null {
    const rows = document.querySelectorAll('.group-color-row');
    for (const row of rows) {
        const label = row.querySelector('.color-label')?.textContent ?? '';
        if (label.trim() === groupName) {
            return row.querySelector('input.hex-input');
        }
    }
    return null;
}

describe('AppearancePage.svelte', () => {
    let component: ReturnType<typeof mount> | undefined;

    beforeEach(() => {
        document.body.innerHTML = '';
        settings.set(null);
        systemInfo.set({} as SystemInfo);
    });

    afterEach(() => {
        if (component) {
            unmount(component);
            component = undefined;
        }
    });

    it('group pickers fall back to the detected brand color instead of #cdccca', async () => {
        settings.set(makePayload());
        systemInfo.set({ disks: [] } as unknown as SystemInfo);

        component = mount(AppearancePage, { target: document.body });
        await tick();

        const cpu = groupColorInput('All CPU');
        expect(cpu).not.toBeNull();
        expect(cpu!.value.toLowerCase()).toBe('#0068b5');

        const gpu = groupColorInput('All GPU');
        expect(gpu).not.toBeNull();
        expect(gpu!.value.toLowerCase()).toBe('#76b900');

        const ram = groupColorInput('All RAM');
        expect(ram).not.toBeNull();
        expect(ram!.value.toLowerCase()).toBe('#005bab');

        const network = groupColorInput('All Network');
        expect(network).not.toBeNull();
        expect(network!.value.toLowerCase()).toBe('#006db6');
    });

    it('entering a group hex color applies it and tracks the customization', async () => {
        settings.set(makePayload());
        systemInfo.set({ disks: [] } as unknown as SystemInfo);

        component = mount(AppearancePage, { target: document.body });
        await tick();

        const hex = groupHexInput('All CPU');
        expect(hex).not.toBeNull();
        hex!.value = '#FF0000';
        hex!.dispatchEvent(new Event('blur'));
        await tick();

        expect(groupColorInput('All CPU')!.value.toLowerCase()).toBe('#ff0000');
        expect(get(settings)!.overlay.userCustomizedGroupColors).toContain('cpu');
    });

    it('renders native color inputs with no custom popover or separate dropper buttons', async () => {
        settings.set(makePayload());
        systemInfo.set({ disks: [] } as unknown as SystemInfo);

        component = mount(AppearancePage, { target: document.body });
        await tick();

        // Dedicated dropper buttons must NOT be present on the table rows
        expect(document.querySelectorAll('.dropper-btn')).toHaveLength(0);

        // No custom popover dialogs or backdrops
        expect(document.querySelectorAll('.color-popover')).toHaveLength(0);
        expect(document.querySelectorAll('.color-popover-backdrop')).toHaveLength(0);

        // All color swatches must be standard native input[type="color"] elements
        const colorInputs = document.querySelectorAll('input[type="color"].color-picker');
        expect(colorInputs.length).toBeGreaterThan(0);

        // Changing the color input fires change and updates settings
        const cpuInput = groupColorInput('All CPU');
        expect(cpuInput).not.toBeNull();
        cpuInput!.value = '#123456';
        cpuInput!.dispatchEvent(new Event('input', { bubbles: true }));
        cpuInput!.dispatchEvent(new Event('change', { bubbles: true }));
        await tick();

        expect(get(settings)!.overlay.userCustomizedGroupColors).toContain('cpu');
        expect(groupColorInput('All CPU')!.value.toLowerCase()).toBe('#123456');
    });

    it('renders disk and battery color sections and supports reset buttons', async () => {
        const payload = makePayload();
        payload.monitoring.hasBattery = true;
        payload.overlay.metricColors['cpu_pct'] = '#123456';
        payload.overlay.userCustomizedMetricColors = ['cpu_pct'];
        payload.overlay.groupColors['cpu'] = '#654321';
        payload.overlay.userCustomizedGroupColors = ['cpu'];
        payload.overlay.metricColors['disk_disk0_read'] = '#aabbcc';
        payload.overlay.userCustomizedMetricColors = ['cpu_pct', 'disk_disk0_read'];

        settings.set(payload);
        systemInfo.set({
            disks: [{ displayType: 'NVMe', busType: 'NVMe' }],
            hasBattery: true
        } as unknown as SystemInfo);

        component = mount(AppearancePage, { target: document.body });
        await tick();

        expect(document.body.textContent).toContain('All Disks');
        expect(document.body.textContent).toContain('All Battery');

        // Test individual metric reset button for cpu_pct
        const resetBtns = document.body.querySelectorAll<HTMLButtonElement>('.reset-btn.small');
        expect(resetBtns.length).toBeGreaterThanOrEqual(2);
        resetBtns[0].click();
        await tick();

        expect(get(settings)!.overlay.userCustomizedMetricColors).not.toContain('cpu_pct');

        // Test individual metric reset button for disk_disk0_read
        resetBtns[1].click();
        await tick();

        expect(get(settings)!.overlay.userCustomizedMetricColors).not.toContain('disk_disk0_read');

        // Test group reset button
        const groupResetBtn = document.body.querySelector('.reset-btn.group') as HTMLButtonElement | null;
        expect(groupResetBtn).not.toBeNull();
        groupResetBtn!.click();
        await tick();

        expect(get(settings)!.overlay.userCustomizedGroupColors).not.toContain('cpu');
    });

    it('validates invalid hex inputs on blur', async () => {
        settings.set(makePayload());
        systemInfo.set({ disks: [] } as unknown as SystemInfo);

        component = mount(AppearancePage, { target: document.body });
        await tick();

        const hex = groupHexInput('All CPU');
        expect(hex).not.toBeNull();
        hex!.value = 'not-a-color';
        hex!.dispatchEvent(new Event('blur'));
        await tick();

        expect(hex!.classList.contains('invalid')).toBe(true);
    });
});


