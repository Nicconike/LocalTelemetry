import { mount, unmount, tick } from 'svelte';
import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { settings } from '../../../../../../../src/LocalTelemetry.App/Settings/wwwroot/src/lib/store';
import MonitoringPage from '../../../../../../../src/LocalTelemetry.App/Settings/wwwroot/src/components/pages/MonitoringPage.svelte';

function makeMonitoringPayload() {
    return {
        enableFileLogging: true,
        systemLogLevel: 'Info',
        monitoring: {
            trackCpu: true,
            trackGpu: true,
            trackRam: true,
            trackNet: true,
            trackDisk: true,
            trackBattery: false,
            gpuUsageSource: 'driver',
            cpuUsageMode: 'overall',
            cpuFreqMode: 'average',
            logCpuMode: 0,
            logGpuMode: 0,
            logRamMode: 0,
            logNetMode: 0,
            logDiskMode: 0,
            logBatteryMode: 0,
        },
    };
}

describe('MonitoringPage.svelte', () => {
    let component: ReturnType<typeof mount> | undefined;

    beforeEach(() => {
        document.body.innerHTML = '';
        settings.set(makeMonitoringPayload() as any);
    });

    afterEach(() => {
        if (component) {
            unmount(component);
            component = undefined;
        }
    });

    it('renders monitoring settings page', async () => {
        component = mount(MonitoringPage, { target: document.body });
        await tick();

        expect(document.body.textContent).toContain('Monitoring');
        expect(document.body.textContent).toContain('System Logging');
        expect(document.body.textContent).toContain('Log Level');
    });

    it('changes system log level when segmented button is clicked', async () => {
        component = mount(MonitoringPage, { target: document.body });
        await tick();

        const buttons = Array.from(document.body.querySelectorAll('.btn-mode')) as HTMLButtonElement[];
        const debugBtn = buttons.find((b) => b.textContent?.trim() === 'Debug');
        expect(debugBtn).toBeDefined();

        debugBtn?.click();
        await tick();

        let currentVal: string | undefined;
        settings.subscribe((s) => (currentVal = s?.systemLogLevel))();
        expect(currentVal).toBe('Debug');
    });

    it('changes GPU usage source when clicked', async () => {
        component = mount(MonitoringPage, { target: document.body });
        await tick();

        const buttons = Array.from(document.body.querySelectorAll('.btn-mode')) as HTMLButtonElement[];
        const wddmBtn = buttons.find((b) => b.textContent?.trim().includes('WDDM'));
        expect(wddmBtn).toBeDefined();

        wddmBtn?.click();
        await tick();

        let gpuSource: string | undefined;
        settings.subscribe((s) => (gpuSource = s?.monitoring?.gpuUsageSource))();
        expect(gpuSource).toBe('wddm');
    });

    it('changes CPU usage mode when clicked', async () => {
        component = mount(MonitoringPage, { target: document.body });
        await tick();

        const buttons = Array.from(document.body.querySelectorAll('.btn-mode')) as HTMLButtonElement[];
        const maxThreadBtn = buttons.find((b) => b.textContent?.trim().includes('Max Thread'));
        expect(maxThreadBtn).toBeDefined();

        maxThreadBtn?.click();
        await tick();

        let cpuUsageMode: string | undefined;
        settings.subscribe((s) => (cpuUsageMode = s?.monitoring?.cpuUsageMode))();
        expect(cpuUsageMode).toBe('max');
    });

    it('changes CPU clock mode when clicked', async () => {
        component = mount(MonitoringPage, { target: document.body });
        await tick();

        const buttons = Array.from(document.body.querySelectorAll('.btn-mode')) as HTMLButtonElement[];
        const maxCoreBtn = buttons.find((b) => b.textContent?.trim().includes('Max Active Core'));
        expect(maxCoreBtn).toBeDefined();

        maxCoreBtn?.click();
        await tick();

        let cpuFreqMode: string | undefined;
        settings.subscribe((s) => (cpuFreqMode = s?.monitoring?.cpuFreqMode))();
        expect(cpuFreqMode).toBe('max');
    });

    it('changes metric logging mode when clicked', async () => {
        component = mount(MonitoringPage, { target: document.body });
        await tick();

        const cards = Array.from(document.body.querySelectorAll('.log-card'));
        const cpuLoggingCard = cards.find(
            (c) => c.querySelector('.log-label')?.textContent?.trim() === 'CPU'
        );
        expect(cpuLoggingCard).toBeDefined();

        const errorsBtn = Array.from(cpuLoggingCard!.querySelectorAll<HTMLButtonElement>('.btn-mode')).find(
            (b) => b.textContent?.trim() === 'Errors'
        );
        expect(errorsBtn).toBeDefined();

        errorsBtn?.click();
        await tick();

        let cpuLogMode: number | undefined;
        settings.subscribe((s) => (cpuLogMode = s?.monitoring?.logCpuMode))();
        expect(cpuLogMode).toBe(1);
    });

    it('initializes zero modes to 1 when metrics logging is enabled', async () => {
        const payload = makeMonitoringPayload();
        payload.enableFileLogging = false;
        payload.monitoring.trackBattery = true;
        settings.set(payload as any);

        component = mount(MonitoringPage, { target: document.body });
        await tick();

        const fileLogToggle = Array.from(document.body.querySelectorAll('input[type="checkbox"]')).find(
            (input) => input.getAttribute('aria-label') === 'Enable Metrics Logging'
        ) as HTMLInputElement;
        expect(fileLogToggle).toBeDefined();

        fileLogToggle.click();
        await tick();

        let s: any;
        settings.subscribe((val) => (s = val))();
        expect(s.enableFileLogging).toBe(true);
        expect(s.monitoring.logCpuMode).toBe(1);

        // Toggle again when modes are already non-zero
        s.enableFileLogging = false;
        s.monitoring.logCpuMode = 2;
        s.monitoring.logGpuMode = 2;
        s.monitoring.logRamMode = 2;
        s.monitoring.logNetMode = 2;
        s.monitoring.logDiskMode = 2;
        s.monitoring.logBatteryMode = 2;
        settings.set(s);
        await tick();

        fileLogToggle.click();
        await tick();
        // Toggle off to cover false branch of if ($settings.enableFileLogging)
        fileLogToggle.click();
        await tick();
    });

    it('renders battery category when hasBattery is true and auto-enables track flag on mode change', async () => {
        const payload = makeMonitoringPayload();
        (payload.monitoring as any).hasBattery = true;
        payload.monitoring.trackBattery = false;
        payload.monitoring.logBatteryMode = 0;
        settings.set(payload as any);

        component = mount(MonitoringPage, { target: document.body });
        await tick();

        const batteryCard = Array.from(document.body.querySelectorAll('.log-card')).find(
            (c) => c.querySelector('.log-label')?.textContent?.trim() === 'Battery'
        );
        expect(batteryCard).toBeDefined();

        const fullBtn = Array.from(batteryCard!.querySelectorAll<HTMLButtonElement>('.btn-mode')).find(
            (b) => b.textContent?.trim() === 'Full'
        );
        expect(fullBtn).toBeDefined();

        fullBtn?.click();
        await tick();

        let s: any;
        settings.subscribe((val) => (s = val))();
        expect(s.monitoring.logBatteryMode).toBe(2);
        expect(s.monitoring.trackBattery).toBe(true);
    });

    it('renders empty when settings is null', async () => {
        settings.set(null as any);
        component = mount(MonitoringPage, { target: document.body });
        await tick();

        expect(document.querySelector('.page')).toBeNull();
    });
});
