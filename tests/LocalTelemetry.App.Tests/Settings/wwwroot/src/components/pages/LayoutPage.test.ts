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
                hasBattery: true,
            },
            overlay: {
                row1: ['cpu_pct', 'ram_pct', 'gpu_pct'],
            },
        } as any);
        systemInfo.set({
            disks: [
                { displayType: 'NVMe', busType: 'NVMe' },
                { displayType: 'NVMe', busType: 'NVMe' },
                { displayType: '', busType: 'SCSI' },
            ]
        } as any);
    });

    afterEach(() => {
        if (component) {
            unmount(component);
            component = undefined;
        }
    });

    it('renders layout page sections with disk and battery metrics', async () => {
        component = mount(LayoutPage, { target: document.body });
        await tick();

        expect(document.body.textContent).toContain('Widget Metric Layout');
        expect(document.body.textContent).toContain('CPU Usage');
        expect(document.body.textContent).toContain('RAM Usage');
        expect(document.body.textContent).toContain('GPU Usage');
    });

    it('moves metric down and up via action buttons', async () => {
        component = mount(LayoutPage, { target: document.body });
        await tick();

        const moveDownBtns = document.body.querySelectorAll('button[title="Move down"]') as NodeListOf<HTMLButtonElement>;
        expect(moveDownBtns).toHaveLength(3);

        // Click first item move down: 'cpu_pct' should swap with 'ram_pct'
        moveDownBtns[0].click();
        await tick();

        let rows = document.body.querySelectorAll('.metric-name');
        expect(rows[0].textContent).toContain('RAM Usage');
        expect(rows[1].textContent).toContain('CPU Usage');

        // Click second item move up: 'cpu_pct' swaps back with 'ram_pct'
        const moveUpBtns = document.body.querySelectorAll('button[title="Move up"]') as NodeListOf<HTMLButtonElement>;
        moveUpBtns[1].click();
        await tick();

        rows = document.body.querySelectorAll('.metric-name');
        expect(rows[0].textContent).toContain('CPU Usage');
        expect(rows[1].textContent).toContain('RAM Usage');
    });

    it('removes a metric when remove button is clicked', async () => {
        component = mount(LayoutPage, { target: document.body });
        await tick();

        const removeBtns = document.body.querySelectorAll('button.btn-remove') as NodeListOf<HTMLButtonElement>;
        expect(removeBtns).toHaveLength(3);

        removeBtns[1].click(); // remove ram_pct
        await tick();

        const rows = document.body.querySelectorAll('.metric-name');
        expect(rows).toHaveLength(2);
        expect(Array.from(rows).some(r => r.textContent?.includes('RAM Usage'))).toBe(false);
    });

    it('adds a metric from the select dropdown', async () => {
        component = mount(LayoutPage, { target: document.body });
        await tick();

        const select = document.body.querySelector('select.add-select') as HTMLSelectElement;
        expect(select).not.toBeNull();

        select.value = 'battery_pct';
        select.dispatchEvent(new Event('change'));
        await tick();

        expect(document.body.textContent).toContain('Battery');
    });

    it('resets layout to default metrics when reset button is clicked', async () => {
        component = mount(LayoutPage, { target: document.body });
        await tick();

        const resetBtn = document.body.querySelector('button.reset-btn') as HTMLButtonElement | null;
        expect(resetBtn).not.toBeNull();

        resetBtn!.click();
        await tick();

        const rows = document.body.querySelectorAll('.metric-name');
        expect(rows).toHaveLength(10);
    });

    it('handles drag and drop events between rows', async () => {
        component = mount(LayoutPage, { target: document.body });
        await tick();

        const metricRows = document.body.querySelectorAll('.metric-row') as NodeListOf<HTMLElement>;
        expect(metricRows).toHaveLength(3);

        const dataTransfer = {
            effectAllowed: '',
            dropEffect: '',
            setData: () => { },
            getData: () => '0'
        };

        const createDragEvent = (type: string) => {
            const ev = new Event(type, { bubbles: true, cancelable: true });
            Object.defineProperty(ev, 'dataTransfer', { value: dataTransfer });
            return ev;
        };

        // Drag start row 0
        metricRows[0].dispatchEvent(createDragEvent('dragstart'));
        // Drag over row 2
        metricRows[2].dispatchEvent(createDragEvent('dragover'));
        // Drag leave
        metricRows[2].dispatchEvent(new Event('dragleave', { bubbles: true }));
        // Drop on row 1
        metricRows[1].dispatchEvent(createDragEvent('drop'));
        // Drag end
        metricRows[0].dispatchEvent(new Event('dragend', { bubbles: true }));
        await tick();

        // Drop when not dragging (dragIdx is -1)
        metricRows[0].dispatchEvent(createDragEvent('drop'));
        await tick();

        expect(document.body.querySelectorAll('.metric-row')).toHaveLength(3);
    });

    it('ignores empty selection when adding metric', async () => {
        component = mount(LayoutPage, { target: document.body });
        await tick();

        const select = document.body.querySelector('select.add-select') as HTMLSelectElement;
        expect(select).not.toBeNull();

        select.value = '';
        select.dispatchEvent(new Event('change'));
        await tick();

        expect(document.body.querySelectorAll('.metric-row')).toHaveLength(3);
    });
});
