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

        // Select empty string (-- Select --)
        select.value = '';
        select.dispatchEvent(new Event('change', { bubbles: true }));
        await tick();

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

    it('renders layout presets and row badges correctly', async () => {
        component = mount(LayoutPage, { target: document.body });
        await tick();

        const pills = document.body.querySelectorAll('.preset-pill') as NodeListOf<HTMLButtonElement>;
        expect(pills.length).toBeGreaterThanOrEqual(4);
        expect(document.body.textContent).toContain('Default');
        expect(document.body.textContent).toContain('Gaming');
        expect(document.body.textContent).toContain('Compact');
        expect(document.body.textContent).toContain('Full Telemetry');

        // Verify initial custom indicator exists when row1 does not match a preset
        const customCard = document.body.querySelector('.custom-card');
        expect(customCard).not.toBeNull();
        expect(customCard!.textContent).toContain('Custom');

        // Row badges check (even = Top Row, odd = Bottom Row)
        const rowBadges = document.body.querySelectorAll('.row-badge');
        expect(rowBadges[0].textContent?.trim()).toBe('Top Row');
        expect(rowBadges[1].textContent?.trim()).toBe('Bottom Row');
        expect(rowBadges[2].textContent?.trim()).toBe('Top Row');
    });

    it('switches between presets and updates active preset indicator', async () => {
        component = mount(LayoutPage, { target: document.body });
        await tick();

        const pills = Array.from(document.body.querySelectorAll('.preset-pill')) as HTMLButtonElement[];
        const gamingBtn = pills.find(p => p.textContent?.includes('Gaming'));
        const compactBtn = pills.find(p => p.textContent?.includes('Compact'));
        const defaultBtn = pills.find(p => p.textContent?.includes('Default'));
        const fullBtn = pills.find(p => p.textContent?.includes('Full'));

        expect(gamingBtn).toBeDefined();
        expect(compactBtn).toBeDefined();
        expect(defaultBtn).toBeDefined();
        expect(fullBtn).toBeDefined();

        // 1. Click Gaming preset: CPU top row, GPU bottom row, Total/VRAM, RAM, Network
        gamingBtn!.click();
        await tick();

        let row1 = get(settings)!.overlay.row1;
        expect(row1).toEqual([
            'cpu_pct',   // Col 0 Top: CPU %
            'gpu_pct',   // Col 0 Bot: GPU %
            'cpu_temp',  // Col 1 Top: CPU Temp
            'gpu_temp',  // Col 1 Bot: GPU Temp
            'cpu_power', // Col 2 Top: CPU Power
            'gpu_power', // Col 2 Bot: GPU Power
            'net_total', // Col 3 Top: Net Total
            'gpu_vram',  // Col 3 Bot: GPU VRAM
            'ram_pct',   // Col 4 Top: RAM %
            'ram_used',  // Col 4 Bot: RAM Used
            'net_down',  // Col 5 Top: Net Down
            'net_up',    // Col 5 Bot: Net Up
        ]);
        expect(gamingBtn!.classList.contains('active')).toBe(true);

        // 2. Click Compact preset
        compactBtn!.click();
        await tick();

        row1 = get(settings)!.overlay.row1;
        expect(row1).toEqual(['cpu_pct', 'gpu_pct', 'ram_pct', 'gpu_vram', 'net_down', 'net_up']);
        expect(compactBtn!.classList.contains('active')).toBe(true);

        // 3. Click Full Telemetry preset
        fullBtn!.click();
        await tick();

        row1 = get(settings)!.overlay.row1;
        expect(row1.length).toBeGreaterThan(10);
        expect(fullBtn!.classList.contains('active')).toBe(true);

        // 4. Click Default preset
        defaultBtn!.click();
        await tick();

        row1 = get(settings)!.overlay.row1;
        expect(row1).toContain('cpu_pct');
        expect(row1).toContain('cpu_temp');
        expect(defaultBtn!.classList.contains('active')).toBe(true);

        // 5. Test new presets: Thermals & Power, Workstation, Network & Traffic, Micro
        const thermalsBtn = pills.find(p => p.textContent?.includes('Thermals'));
        const workstationBtn = pills.find(p => p.textContent?.includes('Workstation'));
        const networkBtn = pills.find(p => p.textContent?.includes('Network'));
        const microBtn = pills.find(p => p.textContent?.includes('Micro'));

        expect(thermalsBtn).toBeDefined();
        expect(workstationBtn).toBeDefined();
        expect(networkBtn).toBeDefined();
        expect(microBtn).toBeDefined();

        // Thermals: CPU Usage over Temp, CPU Clock over Power, GPU Usage over Temp, GPU Clock over Power
        thermalsBtn!.click();
        await tick();
        row1 = get(settings)!.overlay.row1;
        expect(row1).toEqual(['cpu_pct', 'cpu_temp', 'cpu_freq', 'cpu_power', 'gpu_pct', 'gpu_temp', 'gpu_freq', 'gpu_power']);
        expect(thermalsBtn!.classList.contains('active')).toBe(true);

        // Workstation: CPU over Temp, RAM % over Used, Net Down over Up, Disk Read over Write
        workstationBtn!.click();
        await tick();
        row1 = get(settings)!.overlay.row1;
        expect(row1).toEqual(['cpu_pct', 'cpu_temp', 'ram_pct', 'ram_used', 'net_down', 'net_up', 'disk_disk0_read', 'disk_disk0_write']);
        expect(workstationBtn!.classList.contains('active')).toBe(true);

        // Network: CPU % over RAM %, Net Down over Up, Net Total over RAM Used
        networkBtn!.click();
        await tick();
        row1 = get(settings)!.overlay.row1;
        expect(row1).toEqual(['cpu_pct', 'ram_pct', 'net_down', 'net_up', 'net_total', 'ram_used']);
        expect(networkBtn!.classList.contains('active')).toBe(true);

        // Micro: CPU % over GPU %, RAM % over Net Down
        microBtn!.click();
        await tick();
        row1 = get(settings)!.overlay.row1;
        expect(row1).toEqual(['cpu_pct', 'gpu_pct', 'ram_pct', 'net_down']);
        expect(microBtn!.classList.contains('active')).toBe(true);

        // 6. Custom edit un-highlights preset and shows 'Custom' badge
        const moveDownBtns = document.body.querySelectorAll('button[title="Move down"]') as NodeListOf<HTMLButtonElement>;
        moveDownBtns[0].click();
        await tick();

        expect(document.body.textContent).toContain('Custom');
        expect(microBtn!.classList.contains('active')).toBe(false);
    });

    it('renders live mini-taskbar preview and allows swapping columns directly when exactly 2 columns exist', async () => {
        settings.set({
            monitoring: { trackCpu: true, trackGpu: true, trackRam: true, trackNet: true },
            overlay: { row1: ['cpu_pct', 'cpu_temp', 'gpu_pct', 'gpu_temp'] },
        } as any);

        component = mount(LayoutPage, { target: document.body });
        await tick();

        // Check mini taskbar widget exists
        const preview = document.body.querySelector('.mini-taskbar-widget');
        expect(preview).not.toBeNull();
        expect(preview!.textContent).toContain('CPU %');

        // Check column cards exist
        const colCards = document.body.querySelectorAll('.column-card');
        expect(colCards).toHaveLength(2);

        // Click swap button in Column 0 (swaps Column 0 and Column 1 when 2 columns exist)
        const swapBtns = document.body.querySelectorAll('button.btn-swap') as NodeListOf<HTMLButtonElement>;
        expect(swapBtns).toHaveLength(2);

        swapBtns[0].click();
        await tick();

        let row1 = get(settings)!.overlay.row1;
        expect(row1[0]).toBe('gpu_pct');
        expect(row1[1]).toBe('gpu_temp');
        expect(row1[2]).toBe('cpu_pct');
        expect(row1[3]).toBe('cpu_temp');
    });

    it('swaps any two columns via column swap buttons when more than 2 columns exist', async () => {
        settings.set({
            monitoring: { trackCpu: true, trackGpu: true, trackRam: true, trackNet: true },
            overlay: { row1: ['cpu_pct', 'cpu_temp', 'gpu_pct', 'gpu_temp', 'ram_pct', 'ram_used'] },
        } as any);

        component = mount(LayoutPage, { target: document.body });
        await tick();

        // 3 columns: Col 0 (CPU), Col 1 (GPU), Col 2 (RAM)
        let swapBtns = document.body.querySelectorAll('button.btn-swap') as NodeListOf<HTMLButtonElement>;
        expect(swapBtns).toHaveLength(3);

        // Click Swap on Col 0 -> activates swap mode
        swapBtns[0].click();
        await tick();

        // Target swap button on Col 2 now says "With Col 1"
        swapBtns = document.body.querySelectorAll('button.btn-swap') as NodeListOf<HTMLButtonElement>;
        expect(swapBtns[2].textContent).toContain('With Col 1');

        // Click target on Col 2 -> Col 0 and Col 2 swap places
        swapBtns[2].click();
        await tick();

        let row1 = get(settings)!.overlay.row1;
        // Col 0 is now RAM
        expect(row1[0]).toBe('ram_pct');
        expect(row1[1]).toBe('ram_used');
        // Col 1 remains GPU
        expect(row1[2]).toBe('gpu_pct');
        expect(row1[3]).toBe('gpu_temp');
        // Col 2 is now CPU
        expect(row1[4]).toBe('cpu_pct');
        expect(row1[5]).toBe('cpu_temp');
    });

    it('removes entire column via column remove button', async () => {
        settings.set({
            monitoring: { trackCpu: true, trackGpu: true, trackRam: true, trackNet: true },
            overlay: { row1: ['cpu_pct', 'cpu_temp', 'gpu_pct', 'gpu_temp'] },
        } as any);

        component = mount(LayoutPage, { target: document.body });
        await tick();

        const removeColBtns = document.body.querySelectorAll('button[title="Remove entire column"]') as NodeListOf<HTMLButtonElement>;
        expect(removeColBtns).toHaveLength(2);

        // Remove Col 0
        removeColBtns[0].click();
        await tick();

        const row1 = get(settings)!.overlay.row1;
        expect(row1).toEqual(['gpu_pct', 'gpu_temp']);
    });

    it('allows a column to contain only 1 metric without shifting subsequent columns', async () => {
        settings.set({
            monitoring: { trackCpu: true, trackGpu: true, trackRam: true },
            overlay: { columns: [['cpu_pct', 'cpu_temp'], ['gpu_pct', 'gpu_temp']] },
        } as any);

        component = mount(LayoutPage, { target: document.body });
        await tick();

        // 4 total remove buttons: cpu_pct, cpu_temp, gpu_pct, gpu_temp
        const removeBtns = document.body.querySelectorAll('button.btn-remove') as NodeListOf<HTMLButtonElement>;
        expect(removeBtns).toHaveLength(4);

        // Remove cpu_temp (index 1, bottom of Col 0)
        removeBtns[1].click();
        await tick();

        const curCols = get(settings)!.overlay.columns;
        expect(curCols).toHaveLength(2);
        // Column 0 now has only 1 metric
        expect(curCols[0]).toEqual(['cpu_pct']);
        // Column 1 was NOT pulled up or altered
        expect(curCols[1]).toEqual(['gpu_pct', 'gpu_temp']);

        // Empty bottom slot should show pair-select
        const pairSelect = document.body.querySelector('select.pair-select') as HTMLSelectElement | null;
        expect(pairSelect).not.toBeNull();

        // Pair ram_pct into Column 0
        pairSelect!.value = 'ram_pct';
        pairSelect!.dispatchEvent(new Event('change', { bubbles: true }));
        await tick();

        const pairedCols = get(settings)!.overlay.columns;
        expect(pairedCols[0]).toEqual(['cpu_pct', 'ram_pct']);
        expect(pairedCols[1]).toEqual(['gpu_pct', 'gpu_temp']);
    });

    it('supports an overlay with only 1 single metric overall', async () => {
        settings.set({
            monitoring: { trackCpu: true },
            overlay: { columns: [['cpu_pct']] },
        } as any);

        component = mount(LayoutPage, { target: document.body });
        await tick();

        const curCols = get(settings)!.overlay.columns;
        expect(curCols).toHaveLength(1);
        expect(curCols[0]).toEqual(['cpu_pct']);

        const rows = document.body.querySelectorAll('.metric-name');
        expect(rows).toHaveLength(1);
        expect(rows[0].textContent).toContain('CPU Usage');
    });
});
