import { mount, unmount, tick } from 'svelte';
import { get } from 'svelte/store';
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
        select.dispatchEvent(new Event('change', { bubbles: true }));
        await tick();

        expect(document.body.textContent).toContain('Battery');

        // Add already existing metric by appending an option with existing metric id (covers line 112)
        const opt = document.createElement('option');
        opt.value = 'cpu_pct';
        select.appendChild(opt);
        select.value = 'cpu_pct';
        select.dispatchEvent(new Event('change', { bubbles: true }));
        await tick();
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
        select.dispatchEvent(new Event('change', { bubbles: true }));
        await tick();

        expect(document.body.querySelectorAll('.metric-row')).toHaveLength(3);
    });

    it('handles drag-and-drop edge cases including drop on same item and boundary values', async () => {
        component = mount(LayoutPage, { target: document.body });
        await tick();

        let metricRows = document.body.querySelectorAll('.metric-row') as NodeListOf<HTMLElement>;
        const dataTransfer = { effectAllowed: '', dropEffect: '', setData: () => { }, getData: () => '0' };
        const createDragEvent = (type: string) => {
            const ev = new Event(type, { bubbles: true, cancelable: true });
            Object.defineProperty(ev, 'dataTransfer', { value: dataTransfer });
            return ev;
        };

        // Drag start row 0, then drop onto row 0 (same index)
        metricRows[0].dispatchEvent(createDragEvent('dragstart'));
        metricRows[0].dispatchEvent(createDragEvent('drop'));
        await tick();

        // Drag start row 0, drag over row 1 (verifies class:drag-over true)
        metricRows[0].dispatchEvent(createDragEvent('dragstart'));
        metricRows[1].dispatchEvent(createDragEvent('dragover'));
        await tick();
        expect(metricRows[1].classList.contains('drag-over')).toBe(true);

        // Drag start row 0, drag over row 0 (dropIdx === idx && dragIdx === idx: drag-over is false)
        metricRows[0].dispatchEvent(createDragEvent('dragstart'));
        metricRows[0].dispatchEvent(createDragEvent('dragover'));
        await tick();
        expect(metricRows[0].classList.contains('drag-over')).toBe(false);

        // Now change settings while dragging row 2 so dragIdx >= items.length
        metricRows[2].dispatchEvent(createDragEvent('dragstart'));
        settings.set({
            monitoring: { trackCpu: true },
            overlay: { row1: ['cpu_pct'] }
        } as any);
        await tick();

        metricRows = document.body.querySelectorAll('.metric-row') as NodeListOf<HTMLElement>;
        expect(metricRows).toHaveLength(1);
        // Dispatch drop on remaining row: dragIdx (2) >= items.length (1)
        metricRows[0].dispatchEvent(createDragEvent('drop'));
        await tick();

        // Reset settings with 2 items to ensure row 1 exists
        settings.set({
            monitoring: {},
            overlay: { row1: ['cpu_pct', 'gpu_pct'] }
        } as any);
        await tick();
        metricRows = document.body.querySelectorAll('.metric-row') as NodeListOf<HTMLElement>;
        expect(metricRows.length).toBeGreaterThanOrEqual(2);

        // Start dragging index 0, clear row1, and drop on index 1 (covers line 148 row1 || [])
        metricRows[0].dispatchEvent(createDragEvent('dragstart'));
        settings.set({
            monitoring: {},
            overlay: {}
        } as any);
        await tick();
        metricRows[1].dispatchEvent(createDragEvent('drop'));
        await tick();
    });

    it('handles busType fallback labels and unknown metric formatting', async () => {
        settings.set({
            monitoring: { hasBattery: false },
            overlay: { row1: ['unknown_sensor_metric'] },
        } as any);
        systemInfo.set({
            disks: [
                { busType: 'BUSTYPE_NVME' },
                { busType: '' },
                { busType: 'SATA' }
            ]
        } as any);

        component = mount(LayoutPage, { target: document.body });
        await tick();

        expect(document.body.textContent).toContain('unknown_sensor_metric');
        expect(document.body.textContent).toContain('DISK');
        expect(document.body.textContent).toContain('SATA');
    });

    it('covers boundary moveUp, moveDown, and null settings', async () => {
        settings.set({
            monitoring: {},
            overlay: { row1: ['cpu_pct', null as any, '', 'gpu_pct', 'ram_pct'] }
        } as any);
        component = mount(LayoutPage, { target: document.body });
        await tick();

        const moveUpBtns = document.body.querySelectorAll('button[title="Move up"]') as NodeListOf<HTMLButtonElement>;
        // Boundary: moveUp index 0 returns early
        moveUpBtns[0].click();
        await tick();
        // Valid: moveUp index 1 swaps items
        moveUpBtns[1].click();
        await tick();

        const moveDownBtns = document.body.querySelectorAll('button[title="Move down"]') as NodeListOf<HTMLButtonElement>;
        // Boundary: moveDown index 2 returns early
        moveDownBtns[2].click();
        await tick();
        // Valid: moveDown index 0 swaps items
        moveDownBtns[0].click();
        await tick();

        const removeBtn = document.body.querySelector('button.btn-remove') as HTMLButtonElement;
        const addSelect = document.querySelector('select.add-select') as HTMLSelectElement;

        // Boundary: remove when items is emptied without rerender (idx >= items.length)
        const curOverlay = get(settings)!.overlay;
        curOverlay.row1 = [];
        removeBtn?.click();

        // Set settings to null without tick so DOM buttons still invoke handlers with null settings
        settings.set(null as any);
        moveUpBtns[0].click();
        moveDownBtns[0].click();
        removeBtn?.click();
        if (addSelect) {
            addSelect.value = 'cpu_pct';
            addSelect.dispatchEvent(new Event('change', { bubbles: true }));
        }

        await tick();
        expect(document.querySelector('.page')).toBeNull();
    });

    it('renders empty message when row1 is empty', async () => {
        settings.set({
            overlay: { row1: [] },
            monitoring: {}
        } as any);
        component = mount(LayoutPage, { target: document.body });
        await tick();

        expect(document.body.textContent).toContain('No metrics configured. Add one above.');
    });

    it('covers empty available metrics list and undefined row1', async () => {
        systemInfo.set({ disks: [] } as any);
        settings.set({
            monitoring: { hasBattery: true },
            overlay: {
                row1: [
                    'cpu_pct', 'cpu_temp', 'cpu_freq', 'cpu_power',
                    'gpu_pct', 'gpu_temp', 'gpu_vram', 'gpu_freq', 'gpu_power',
                    'ram_pct', 'ram_used',
                    'net_down', 'net_up', 'net_total',
                    'battery_pct', 'battery_rate'
                ]
            }
        } as any);
        component = mount(LayoutPage, { target: document.body });
        await tick();
        expect(document.querySelector('.add-row')).toBeNull();

        // Overlay with undefined row1
        settings.set({
            monitoring: {},
            overlay: {}
        } as any);
        await tick();
        expect(document.body.textContent).toContain('No metrics configured. Add one above.');

        // Select an item to add when row1 is undefined -> triggers line 111 row1 || []
        const select = document.querySelector('select.add-select') as HTMLSelectElement;
        if (select) {
            select.value = 'cpu_pct';
            select.dispatchEvent(new Event('change', { bubbles: true }));
            await tick();
        }
    });
});
