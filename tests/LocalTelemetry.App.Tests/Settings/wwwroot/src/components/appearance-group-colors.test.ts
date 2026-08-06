import { mount, unmount, tick } from 'svelte';
import { get } from 'svelte/store';
import {
    settings,
    systemInfo,
} from '../../../../../../src/LocalTelemetry.App/Settings/wwwroot/src/lib/store';
import type { SettingsDto, SystemInfo } from '../../../../../../src/LocalTelemetry.App/Settings/wwwroot/src/lib/types';
import AppearancePage from '../../../../../../src/LocalTelemetry.App/Settings/wwwroot/src/components/pages/AppearancePage.svelte';

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

describe('AppearancePage group colors', () => {
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
});
