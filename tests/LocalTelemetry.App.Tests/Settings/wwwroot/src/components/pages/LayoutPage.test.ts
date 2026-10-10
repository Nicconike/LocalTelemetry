import { mount, unmount, tick } from 'svelte';
import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { settings, systemInfo } from '../../../../../../../src/LocalTelemetry.App/Settings/wwwroot/src/lib/store';
import LayoutPage from '../../../../../../../src/LocalTelemetry.App/Settings/wwwroot/src/components/pages/LayoutPage.svelte';

describe('LayoutPage.svelte', () => {
    let component: ReturnType<typeof mount> | undefined;

    beforeEach(() => {
        document.body.innerHTML = '';
        settings.set({
            monitoring: {
                trackCpu: true,
                trackGpu: true,
                trackRam: true,
                trackNet: true,
                trackDisk: true,
                trackBattery: false,
            },
            overlay: {
                row1: ['cpu_pct', 'ram_pct'],
            },
        } as any);
        systemInfo.set({ disks: [] } as any);
    });

    afterEach(() => {
        if (component) {
            unmount(component);
            component = undefined;
        }
    });

    it('renders layout page sections', async () => {
        component = mount(LayoutPage, { target: document.body });
        await tick();

        expect(document.body.textContent).toContain('Widget Metric Layout');
    });

    it('orders add metric dropdown options as CPU then GPU then RAM', async () => {
        component = mount(LayoutPage, { target: document.body });
        await tick();

        const select = document.body.querySelector('select.add-select') as HTMLSelectElement | null;
        expect(select).not.toBeNull();
        const optionValues = Array.from(select!.options).map(o => o.value).filter(Boolean);

        const cpuIndex = optionValues.indexOf('cpu_temp');
        const gpuIndex = optionValues.indexOf('gpu_pct');
        const ramIndex = optionValues.indexOf('ram_used');
        const netIndex = optionValues.indexOf('net_down');

        expect(cpuIndex).toBeGreaterThan(-1);
        expect(gpuIndex).toBeGreaterThan(cpuIndex);
        expect(ramIndex).toBeGreaterThan(gpuIndex);
        expect(netIndex).toBeGreaterThan(ramIndex);
    });
});
