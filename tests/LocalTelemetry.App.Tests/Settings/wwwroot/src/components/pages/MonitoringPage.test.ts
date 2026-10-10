import { mount, unmount, tick } from 'svelte';
import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { settings } from '../../../../../../../src/LocalTelemetry.App/Settings/wwwroot/src/lib/store';
import MonitoringPage from '../../../../../../../src/LocalTelemetry.App/Settings/wwwroot/src/components/pages/MonitoringPage.svelte';

describe('MonitoringPage.svelte', () => {
    let component: ReturnType<typeof mount> | undefined;

    beforeEach(() => {
        document.body.innerHTML = '';
        settings.set({
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
                logCpuMode: 0,
                logGpuMode: 0,
                logRamMode: 0,
                logNetMode: 0,
                logDiskMode: 0,
                logBatteryMode: 0,
            },
        } as any);
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
        settings.subscribe((s) => (currentVal = s.systemLogLevel))();
        expect(currentVal).toBe('Debug');
    });
});
