import { mount, unmount, tick } from 'svelte';
import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { settings } from '../../../../../../../src/LocalTelemetry.App/Settings/wwwroot/src/lib/store';
import AlertsPage from '../../../../../../../src/LocalTelemetry.App/Settings/wwwroot/src/components/pages/AlertsPage.svelte';

describe('AlertsPage.svelte', () => {
    let component: ReturnType<typeof mount> | undefined;

    beforeEach(() => {
        document.body.innerHTML = '';
        settings.set({
            alerts: {
                enabled: true,
                alertCpuTemp: true,
                alertGpuTemp: false,
                alertCpuUsage: true,
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
        } as any);
    });

    afterEach(() => {
        if (component) {
            unmount(component);
            component = undefined;
        }
    });

    it('renders threshold alert settings', async () => {
        component = mount(AlertsPage, { target: document.body });
        await tick();

        expect(document.body.textContent).toContain('Threshold Alerts');
        expect(document.body.textContent).toContain('Enable Threshold Alerts');
    });

    it('displays notice when alerts enabled but no metric threshold is active', async () => {
        settings.set({
            alerts: {
                enabled: true,
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
        } as any);

        component = mount(AlertsPage, { target: document.body });
        await tick();

        expect(document.body.textContent).toContain('No metrics are currently selected');
    });
});
