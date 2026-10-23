import { mount, unmount, tick } from 'svelte';
import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { settings } from '../../../../../../../src/LocalTelemetry.App/Settings/wwwroot/src/lib/store';
import AlertsPage from '../../../../../../../src/LocalTelemetry.App/Settings/wwwroot/src/components/pages/AlertsPage.svelte';

function makeAlertPayload() {
    return {
        monitoring: { hasBattery: false },
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
    };
}

describe('AlertsPage.svelte', () => {
    let component: ReturnType<typeof mount> | undefined;

    beforeEach(() => {
        document.body.innerHTML = '';
        settings.set(makeAlertPayload() as any);
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
        const payload = makeAlertPayload();
        payload.alerts.alertCpuTemp = false;
        payload.alerts.alertCpuUsage = false;
        settings.set(payload as any);

        component = mount(AlertsPage, { target: document.body });
        await tick();

        expect(document.body.textContent).toContain('No metrics are currently selected');
    });

    it('auto-enables sensible defaults when master toggle is turned on with no alerts active', async () => {
        const payload = makeAlertPayload();
        payload.alerts.enabled = false;
        payload.alerts.alertCpuTemp = false;
        payload.alerts.alertCpuUsage = false;
        settings.set(payload as any);

        component = mount(AlertsPage, { target: document.body });
        await tick();

        const masterToggle = document.querySelector('input[type="checkbox"]') as HTMLInputElement;
        expect(masterToggle).not.toBeNull();
        masterToggle.click();
        await tick();

        let currentAlerts: any;
        settings.subscribe((s) => (currentAlerts = s?.alerts))();
        expect(currentAlerts.alertCpuTemp).toBe(true);
        expect(currentAlerts.alertRamUsage).toBe(true);
        expect(currentAlerts.alertGpuTemp).toBe(true);
    });

    it('displays battery alert controls when device has a battery', async () => {
        const payload = makeAlertPayload();
        payload.monitoring.hasBattery = true;
        payload.alerts.alertBatteryLow = true;
        settings.set(payload as any);

        component = mount(AlertsPage, { target: document.body });
        await tick();

        expect(document.body.textContent).toContain('Battery');
        expect(document.body.textContent).toContain('Battery Low');
    });

    it('returns early when turning master toggle off or when alerts is null', async () => {
        const payload = makeAlertPayload();
        payload.alerts.enabled = true;
        settings.set(payload as any);

        component = mount(AlertsPage, { target: document.body });
        await tick();

        const masterToggle = document.querySelector('input[type="checkbox"]') as HTMLInputElement;
        masterToggle.click();
        await tick();

        let currentAlerts: any;
        settings.subscribe((s) => (currentAlerts = s?.alerts))();
        expect(currentAlerts.enabled).toBe(false);

        settings.set(null as any);
        await tick();
        expect(document.querySelector('.page')).toBeNull();
    });
});
