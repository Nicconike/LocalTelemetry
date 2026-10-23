import { mount, unmount, tick } from 'svelte';
import { get } from 'svelte/store';
import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import {
    allTrafficHistory,
    trafficToday,
    windowsImportResult,
    importResult
} from '../../../../../../../src/LocalTelemetry.App/Settings/wwwroot/src/lib/store';
import * as bridge from '../../../../../../../src/LocalTelemetry.App/Settings/wwwroot/src/lib/bridge';
import TrafficHistoryPage from '../../../../../../../src/LocalTelemetry.App/Settings/wwwroot/src/components/pages/TrafficHistoryPage.svelte';

describe('TrafficHistoryPage.svelte', () => {
    let component: ReturnType<typeof mount> | undefined;

    beforeEach(() => {
        document.body.innerHTML = '';
        windowsImportResult.set(null);
        importResult.set(null);
        trafficToday.set({ downBytes: 104857600, upBytes: 52428800 });

        const now = new Date();
        const year = now.getFullYear();
        const month = String(now.getMonth() + 1).padStart(2, '0');

        allTrafficHistory.set([
            {
                date: `01-${month}-${year}`,
                interfaceName: 'Ethernet',
                source: 'LocalTelemetry',
                downBytes: 5_000_000_000,
                upBytes: 2_000_000_000
            },
            {
                date: `01-${month}-${year}`,
                interfaceName: 'WiFi',
                source: 'WindowsImport',
                downBytes: 25_000_000_000,
                upBytes: 10_000_000_000
            },
            {
                date: `02-${month}-${year}`,
                interfaceName: 'Ethernet',
                source: 'DatImport',
                downBytes: 60_000_000_000,
                upBytes: 50_000_000_000
            },
            {
                date: `03-${month}-${year}`,
                interfaceName: 'Ethernet',
                source: 'LocalTelemetry',
                downBytes: 120_000_000_000,
                upBytes: 90_000_000_000
            }
        ]);
    });

    afterEach(() => {
        if (component) {
            unmount(component);
            component = undefined;
        }
        vi.restoreAllMocks();
    });

    it('renders traffic history header, today card, and navigation', async () => {
        component = mount(TrafficHistoryPage, { target: document.body });
        await tick();

        expect(document.body.textContent).toContain('Traffic History');
        expect(document.body.textContent).toContain('Today');
        expect(document.body.textContent).toContain('104.86MB');
    });

    it('navigates between previous and next months', async () => {
        component = mount(TrafficHistoryPage, { target: document.body });
        await tick();

        const navBtns = document.body.querySelectorAll('.nav-btn') as NodeListOf<HTMLButtonElement>;
        expect(navBtns).toHaveLength(2);

        // Click previous month
        navBtns[0].click();
        await tick();

        // Click next month
        navBtns[1].click();
        await tick();

        expect(document.body.querySelector('.month-label')?.textContent).toBeTruthy();
    });

    it('filters traffic records by network interface', async () => {
        component = mount(TrafficHistoryPage, { target: document.body });
        await tick();

        const filterBtns = Array.from(document.body.querySelectorAll('.filter-btn')) as HTMLButtonElement[];
        const wifiBtn = filterBtns.find(b => b.textContent?.trim() === 'WiFi');
        expect(wifiBtn).toBeDefined();

        wifiBtn!.click();
        await tick();
        expect(wifiBtn!.classList.contains('active')).toBe(true);

        const allBtn = filterBtns.find(b => b.textContent?.trim() === 'All');
        allBtn!.click();
        await tick();
        expect(allBtn!.classList.contains('active')).toBe(true);
    });

    it('selects a calendar day and shows breakdown details', async () => {
        component = mount(TrafficHistoryPage, { target: document.body });
        await tick();

        const dayCells = Array.from(document.body.querySelectorAll('.day-cell:not(.empty)')) as HTMLButtonElement[];
        expect(dayCells.length).toBeGreaterThan(0);

        // Click day 1
        dayCells[0].click();
        await tick();

        expect(document.body.textContent).toContain('Breakdown');
        expect(document.body.textContent).toContain('Ethernet');
        expect(document.body.textContent).toContain('WiFi');
        expect(document.body.textContent).toContain('Windows');

        // Click day 1 again to deselect
        dayCells[0].click();
        await tick();

        expect(document.body.textContent).not.toContain('Breakdown');
    });

    it('handles Windows usage import presets and triggers bridge call', async () => {
        const importUsageSpy = vi.spyOn(bridge, 'importWindowsUsage').mockImplementation(() => { });

        component = mount(TrafficHistoryPage, { target: document.body });
        await tick();

        const presetBtns = Array.from(document.body.querySelectorAll('.preset-btn')) as HTMLButtonElement[];
        const preset30 = presetBtns.find(b => b.textContent?.includes('Last 30 Days'));
        expect(preset30).toBeDefined();

        preset30!.click();
        await tick();

        const importBtn = Array.from(document.body.querySelectorAll('.action-btn')).find(b => b.textContent?.includes('Import from Windows')) as HTMLButtonElement;
        expect(importBtn).toBeDefined();

        importBtn.click();
        await tick();
        expect(importUsageSpy).toHaveBeenCalled();
    });

    it('displays windows import result status when set in store', async () => {
        component = mount(TrafficHistoryPage, { target: document.body });
        await tick();

        windowsImportResult.set({
            success: true,
            daysImported: 5,
            downBytes: 5000000000,
            upBytes: 1000000000,
            message: 'Imported successfully'
        });
        await tick();

        expect(document.body.textContent).toContain('Imported 5 day(s)');

        // Error status
        windowsImportResult.set({
            success: false,
            daysImported: 0,
            downBytes: 0,
            upBytes: 0,
            message: 'SRUM access denied'
        });
        await tick();

        expect(document.body.textContent).toContain('SRUM access denied');
    });

    it('handles .dat file import file input change', async () => {
        const importDatSpy = vi.spyOn(bridge, 'importDatContent').mockImplementation(() => { });

        component = mount(TrafficHistoryPage, { target: document.body });
        await tick();

        const fileInput = document.body.querySelector('#dat-file-input') as HTMLInputElement;
        expect(fileInput).not.toBeNull();

        const file = new File(['15-10-2026 Ethernet 1000 500\n'], 'backup.dat', { type: 'text/plain' });
        Object.defineProperty(fileInput, 'files', {
            value: [file],
            configurable: true
        });

        fileInput.dispatchEvent(new Event('change'));
        await tick();

        importResult.set({ daysImported: 1, error: null } as any);
        await tick();
        expect(document.body.textContent).toContain('Imported 1 day(s)');

        // Test import result error
        importResult.set({ daysImported: 0, error: 'Database corrupt' } as any);
        await tick();
        expect(document.body.textContent).toContain('Database corrupt');

        // Test empty file selection
        Object.defineProperty(fileInput, 'files', {
            value: [],
            configurable: true
        });
        fileInput.dispatchEvent(new Event('change'));
        await tick();

        // Test windows import result with 0 days imported and empty message
        windowsImportResult.set({
            success: true,
            daysImported: 0,
            downBytes: 0,
            upBytes: 0,
            message: ''
        });
        await tick();
        expect(document.body.textContent).toContain('Selected date range is already up to date in LocalTelemetry.');
    });

    it('handles date input oninput to clear windowsImportResult and displays dateRangeError when start is after end', async () => {
        component = mount(TrafficHistoryPage, { target: document.body });
        await tick();

        windowsImportResult.set({
            success: true,
            daysImported: 2,
            downBytes: 100,
            upBytes: 100,
            message: 'Imported'
        });
        await tick();

        const endDateInput = document.body.querySelector<HTMLInputElement>('#win-end-date');
        expect(endDateInput).not.toBeNull();

        // Trigger oninput on end date -> clears windowsImportResult
        endDateInput!.dispatchEvent(new Event('input', { bubbles: true }));
        await tick();
        expect(get(windowsImportResult)).toBeNull();

        // Set start date after end date to trigger dateRangeError
        const startDateInput = document.body.querySelector<HTMLInputElement>('#win-start-date');
        expect(startDateInput).not.toBeNull();

        startDateInput!.value = '2026-10-25';
        startDateInput!.dispatchEvent(new Event('input', { bubbles: true }));
        endDateInput!.value = '2026-10-20';
        endDateInput!.dispatchEvent(new Event('input', { bubbles: true }));
        await tick();

        expect(document.body.querySelector('.validation-error')).not.toBeNull();
    });

    it('handles valid .dat file selection and imports content', async () => {
        const importDatSpy = vi.spyOn(bridge, 'importDatContent').mockImplementation(() => { });
        const readSpy = vi.spyOn(FileReader.prototype, 'readAsText').mockImplementation(function (this: FileReader) {
            Object.defineProperty(this, 'result', { value: '1,01-10-2026,Ethernet,100,50', configurable: true });
            this.onload?.({ target: this } as any);
        });

        component = mount(TrafficHistoryPage, { target: document.body });
        await tick();

        const fileInput = document.body.querySelector<HTMLInputElement>('#dat-file-input');
        expect(fileInput).not.toBeNull();

        const file = new File(['1,01-10-2026,Ethernet,100,50'], 'backup.dat', { type: 'text/plain' });
        Object.defineProperty(fileInput, 'files', {
            value: [file],
            configurable: true
        });

        fileInput!.dispatchEvent(new Event('change', { bubbles: true }));
        await tick();

        expect(readSpy).toHaveBeenCalled();
        expect(importDatSpy).toHaveBeenCalled();
        readSpy.mockRestore();
    });
});
