import { mount, unmount, tick } from 'svelte';
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

describe('save-bar alerts regression', () => {
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
});
