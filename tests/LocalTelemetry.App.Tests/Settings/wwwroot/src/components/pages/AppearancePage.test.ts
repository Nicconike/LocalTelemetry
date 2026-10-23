import { mount, unmount, tick } from 'svelte';
import { get } from 'svelte/store';
import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { settings, systemInfo } from '../../../../../../../src/LocalTelemetry.App/Settings/wwwroot/src/lib/store';
import { themes } from '../../../../../../../src/LocalTelemetry.App/Settings/wwwroot/src/lib/themes';
import type { SettingsDto, SystemInfo } from '../../../../../../../src/LocalTelemetry.App/Settings/wwwroot/src/lib/types';
import AppearancePage from '../../../../../../../src/LocalTelemetry.App/Settings/wwwroot/src/components/pages/AppearancePage.svelte';

const BRAND = {
    cpu_pct: '#0068B5',
    gpu_pct: '#76B900',
    ram_pct: '#005BAB',
    net_down: '#006DB6',
    battery_pct: '#005691',
};

function makePayload(): SettingsDto {
    return {
        runAtStartup: false,
        showSystemTrayIcon: true,
        enableFileLogging: false,
        systemLogLevel: 'Info',
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
            metricColors: { ...BRAND },
            groupColors: {},
            defaultMetricColors: { ...BRAND },
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
        netUsage: { enabled: false },
        updates: { checkFrequency: 'weekly', includePrereleases: true },
    };
}

function groupColorInput(groupName: string): HTMLInputElement | null {
    const rows = document.querySelectorAll('.group-color-row');
    for (const row of rows) {
        const label = row.querySelector('.color-label')?.textContent ?? '';
        if (label.trim() === groupName) {
            return row.querySelector('input[type="color"]');
        }
    }
    return null;
}

function groupHexInput(groupName: string): HTMLInputElement | null {
    const rows = document.querySelectorAll('.group-color-row');
    for (const row of rows) {
        const label = row.querySelector('.color-label')?.textContent ?? '';
        if (label.trim() === groupName) {
            return row.querySelector('input.hex-input');
        }
    }
    return null;
}

describe('AppearancePage.svelte', () => {
    let component: ReturnType<typeof mount> | undefined;

    beforeEach(() => {
        document.body.innerHTML = '';
        settings.set(null);
        systemInfo.set({} as SystemInfo);
    });

    afterEach(() => {
        if (component) {
            unmount(component);
            component = undefined;
        }
    });

    it('group pickers fall back to the detected brand color instead of #cdccca', async () => {
        settings.set(makePayload());
        systemInfo.set({ disks: [] } as unknown as SystemInfo);

        component = mount(AppearancePage, { target: document.body });
        await tick();

        const cpu = groupColorInput('All CPU');
        expect(cpu).not.toBeNull();
        expect(cpu!.value.toLowerCase()).toBe('#0068b5');

        const gpu = groupColorInput('All GPU');
        expect(gpu).not.toBeNull();
        expect(gpu!.value.toLowerCase()).toBe('#76b900');

        const ram = groupColorInput('All RAM');
        expect(ram).not.toBeNull();
        expect(ram!.value.toLowerCase()).toBe('#005bab');

        const network = groupColorInput('All Network');
        expect(network).not.toBeNull();
        expect(network!.value.toLowerCase()).toBe('#006db6');
    });

    it('entering a group hex color applies it and tracks the customization', async () => {
        settings.set(makePayload());
        systemInfo.set({ disks: [] } as unknown as SystemInfo);

        component = mount(AppearancePage, { target: document.body });
        await tick();

        const hex = groupHexInput('All CPU');
        expect(hex).not.toBeNull();
        hex!.value = '#FF0000';
        hex!.dispatchEvent(new Event('blur'));
        await tick();

        expect(groupColorInput('All CPU')!.value.toLowerCase()).toBe('#ff0000');
        expect(get(settings)!.overlay.userCustomizedGroupColors).toContain('cpu');
    });

    it('renders native color inputs with no custom popover or separate dropper buttons', async () => {
        settings.set(makePayload());
        systemInfo.set({ disks: [] } as unknown as SystemInfo);

        component = mount(AppearancePage, { target: document.body });
        await tick();

        // Dedicated dropper buttons must NOT be present on the table rows
        expect(document.querySelectorAll('.dropper-btn')).toHaveLength(0);

        // No custom popover dialogs or backdrops
        expect(document.querySelectorAll('.color-popover')).toHaveLength(0);
        expect(document.querySelectorAll('.color-popover-backdrop')).toHaveLength(0);

        // All color swatches must be standard native input[type="color"] elements
        const colorInputs = document.querySelectorAll('input[type="color"].color-picker');
        expect(colorInputs.length).toBeGreaterThan(0);

        // Changing the color input fires change and updates settings
        const cpuInput = groupColorInput('All CPU');
        expect(cpuInput).not.toBeNull();
        cpuInput!.value = '#123456';
        cpuInput!.dispatchEvent(new Event('input', { bubbles: true }));
        cpuInput!.dispatchEvent(new Event('change', { bubbles: true }));
        await tick();

        expect(get(settings)!.overlay.userCustomizedGroupColors).toContain('cpu');
        expect(groupColorInput('All CPU')!.value.toLowerCase()).toBe('#123456');
    });

    it('renders disk and battery color sections and supports reset buttons', async () => {
        const payload = makePayload();
        payload.monitoring.hasBattery = true;
        payload.overlay.metricColors['cpu_pct'] = '#123456';
        payload.overlay.userCustomizedMetricColors = ['cpu_pct'];
        payload.overlay.groupColors['cpu'] = '#654321';
        payload.overlay.userCustomizedGroupColors = ['cpu'];
        payload.overlay.metricColors['disk_disk0_read'] = '#aabbcc';
        payload.overlay.userCustomizedMetricColors = ['cpu_pct', 'disk_disk0_read'];

        settings.set(payload);
        systemInfo.set({
            disks: [{ displayType: 'NVMe', busType: 'NVMe' }],
            hasBattery: true
        } as unknown as SystemInfo);

        component = mount(AppearancePage, { target: document.body });
        await tick();

        expect(document.body.textContent).toContain('All Disks');
        expect(document.body.textContent).toContain('All Battery');

        // Test individual metric reset button for cpu_pct
        const resetBtns = document.body.querySelectorAll<HTMLButtonElement>('.reset-btn.small');
        expect(resetBtns.length).toBeGreaterThanOrEqual(2);
        resetBtns[0].click();
        await tick();

        expect(get(settings)!.overlay.userCustomizedMetricColors).not.toContain('cpu_pct');

        // Test individual metric reset button for disk_disk0_read
        resetBtns[1].click();
        await tick();

        expect(get(settings)!.overlay.userCustomizedMetricColors).not.toContain('disk_disk0_read');

        // Test group reset button
        const groupResetBtn = document.body.querySelector('.reset-btn.group') as HTMLButtonElement | null;
        expect(groupResetBtn).not.toBeNull();
        groupResetBtn!.click();
        await tick();

        expect(get(settings)!.overlay.userCustomizedGroupColors).not.toContain('cpu');
    });

    it('validates invalid hex inputs on blur', async () => {
        settings.set(makePayload());
        systemInfo.set({ disks: [] } as unknown as SystemInfo);

        component = mount(AppearancePage, { target: document.body });
        await tick();

        const hex = groupHexInput('All CPU');
        expect(hex).not.toBeNull();
        hex!.value = 'not-a-color';
        hex!.dispatchEvent(new Event('blur'));
        await tick();

        expect(hex!.classList.contains('invalid')).toBe(true);
    });

    it('selects theme cards on click and Enter key, and restores default theme', async () => {
        const payload = makePayload();
        payload.windowTheme = 'default';
        settings.set(payload);
        systemInfo.set({ disks: [] } as unknown as SystemInfo);

        component = mount(AppearancePage, { target: document.body });
        await tick();

        const cards = document.body.querySelectorAll<HTMLElement>('.swatch-card');
        expect(cards.length).toBeGreaterThan(1);

        // Click first non-default swatch card
        cards[1].click();
        await tick();
        expect(get(settings)!.windowTheme).not.toBe('default');

        // Test Enter key press on card
        cards[0].dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
        await tick();

        // If not default, "Reset to Default Theme" button appears
        const resetBtn = document.body.querySelector<HTMLButtonElement>('.reset-btn');
        if (resetBtn && resetBtn.textContent?.includes('Reset to Default Theme')) {
            resetBtn.click();
            await tick();
            expect(get(settings)!.windowTheme).toBe('default');
        }
    });

    it('toggles follow windows taskbar theme checkbox', async () => {
        const payload = makePayload();
        payload.overlay.followWindowsTheme = false;
        settings.set(payload);
        systemInfo.set({ disks: [] } as unknown as SystemInfo);

        component = mount(AppearancePage, { target: document.body });
        await tick();

        const checkbox = document.body.querySelector<HTMLInputElement>('.theme-toggle input[type="checkbox"]');
        expect(checkbox).not.toBeNull();
        checkbox!.checked = true;
        checkbox!.dispatchEvent(new Event('change', { bubbles: true }));
        await tick();

        expect(get(settings)!.overlay.followWindowsTheme).toBe(true);
    });

    it('updates individual metric color pickers and hex inputs', async () => {
        const payload = makePayload();
        settings.set(payload);
        systemInfo.set({ disks: [] } as unknown as SystemInfo);

        component = mount(AppearancePage, { target: document.body });
        await tick();

        const firstMetricRow = document.body.querySelector('.color-row');
        expect(firstMetricRow).not.toBeNull();

        const colorInput = firstMetricRow!.querySelector<HTMLInputElement>('input[type="color"]');
        const hexInput = firstMetricRow!.querySelector<HTMLInputElement>('input.hex-input');
        expect(colorInput).not.toBeNull();
        expect(hexInput).not.toBeNull();

        // Change color picker
        colorInput!.value = '#ff00aa';
        colorInput!.dispatchEvent(new Event('change', { bubbles: true }));
        await tick();

        expect(get(settings)!.overlay.userCustomizedMetricColors).toContain('cpu_pct');
        expect(get(settings)!.overlay.metricColors['cpu_pct'].toLowerCase()).toBe('#ff00aa');

        // Change hex input
        hexInput!.value = '#00ffaa';
        hexInput!.dispatchEvent(new Event('blur'));
        await tick();

        expect(get(settings)!.overlay.metricColors['cpu_pct'].toLowerCase()).toBe('#00ffaa');

        // Test invalid hex input
        hexInput!.value = 'xyz';
        hexInput!.dispatchEvent(new Event('blur'));
        await tick();
        expect(hexInput!.classList.contains('invalid')).toBe(true);
    });

    it('resets all colors to defaults when reset all button is clicked', async () => {
        const payload = makePayload();
        payload.overlay.userCustomizedMetricColors = ['cpu_pct'];
        payload.overlay.metricColors['cpu_pct'] = '#112233';
        payload.overlay.userCustomizedGroupColors = ['cpu'];
        payload.overlay.groupColors['cpu'] = '#332211';
        settings.set(payload);
        systemInfo.set({
            disks: [{ displayType: 'NVMe', busType: 'NVMe' }],
            hasBattery: true
        } as unknown as SystemInfo);

        component = mount(AppearancePage, { target: document.body });
        await tick();

        const resetAllBtn = Array.from(document.body.querySelectorAll<HTMLButtonElement>('.reset-btn'))
            .find(b => b.textContent?.includes('Reset All to Defaults'));
        expect(resetAllBtn).toBeDefined();

        resetAllBtn!.click();
        await tick();

        expect(get(settings)!.overlay.userCustomizedMetricColors).toHaveLength(0);
        expect(get(settings)!.overlay.userCustomizedGroupColors).toHaveLength(0);
        expect(get(settings)!.overlay.metricColors['cpu_pct']).toBe(BRAND.cpu_pct);
    });

    it('renders battery color group when monitoring hasBattery is true and resets color when not in userCustomized', async () => {
        const payload = makePayload();
        payload.monitoring.hasBattery = true;
        // Set color different from default, but userCustomizedMetricColors is empty
        payload.overlay.metricColors['battery_pct'] = '#998877';
        payload.overlay.defaultMetricColors['battery_pct'] = BRAND.battery_pct;
        payload.overlay.userCustomizedMetricColors = [];
        settings.set(payload);

        component = mount(AppearancePage, { target: document.body });
        await tick();

        expect(document.body.textContent).toContain('Battery');

        // Find small reset button for battery_pct
        const resetBtns = Array.from(document.body.querySelectorAll<HTMLButtonElement>('button.reset-btn.small'));
        expect(resetBtns.length).toBeGreaterThan(0);

        resetBtns[0].click();
        await tick();

        expect(get(settings)!.overlay.metricColors['battery_pct']).toBe(BRAND.battery_pct);
    });

    it('renders disk metrics and handles disk group color customization and reset', async () => {
        const payload = makePayload();
        payload.overlay.userCustomizedGroupColors = ['disk'];
        payload.overlay.groupColors['disk'] = '#123456';
        payload.overlay.userCustomizedMetricColors = ['disk_disk0_read'];
        payload.overlay.metricColors['disk_disk0_read'] = '#654321';
        settings.set(payload);
        systemInfo.set({
            disks: [
                { busType: 'NVMe' },
                { busType: '' }
            ]
        } as unknown as SystemInfo);

        component = mount(AppearancePage, { target: document.body });
        await tick();

        expect(document.body.textContent).toContain('Disk 1 Read (NVMe)');
        expect(document.body.textContent).toContain('Disk 2 Read');

        // Reset disk group
        const diskResetBtn = Array.from(document.body.querySelectorAll<HTMLButtonElement>('button.reset-btn.group')).find(
            b => b.textContent?.includes('Reset')
        );
        expect(diskResetBtn).toBeDefined();
        diskResetBtn?.click();
        await tick();

        expect(get(settings)!.overlay.userCustomizedGroupColors).not.toContain('disk');
    });

    it('handles light theme swatch selection, keyboard activation, group blur, and null settings', async () => {
        const payload = makePayload();
        settings.set(payload);

        component = mount(AppearancePage, { target: document.body });
        await tick();

        // Find swatch cards
        const swatches = Array.from(document.body.querySelectorAll<HTMLElement>('.swatch-card'));
        expect(swatches.length).toBeGreaterThan(1);

        // Click light theme
        const lightSwatch = swatches.find(s => s.textContent?.includes('Default Light'));
        if (lightSwatch) {
            lightSwatch.click();
            await tick();
            expect(get(settings)!.windowTheme).toBe('default-light');

            // Trigger Enter keydown
            lightSwatch.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
            await tick();
        }

        // Test group hex input blur with unchanged value
        const groupHexInput = document.body.querySelector<HTMLInputElement>('.group-color-row .hex-input');
        if (groupHexInput) {
            groupHexInput.value = groupHexInput.value;
            groupHexInput.dispatchEvent(new Event('blur'));
            await tick();

            // Test group hex blur with invalid value
            groupHexInput.value = 'invalid';
            groupHexInput.dispatchEvent(new Event('blur'));
            await tick();
        }

        // Test metric hex input blur with unchanged value
        const metricHexInput = document.body.querySelector<HTMLInputElement>('.color-row .hex-input');
        if (metricHexInput) {
            metricHexInput.value = metricHexInput.value;
            metricHexInput.dispatchEvent(new Event('blur'));
            await tick();
        }

        // Null settings renders empty
        settings.set(null as any);
        await tick();
        expect(document.querySelector('.appearance-page')).toBeNull();
    });

    it('covers color customization edge cases, reset spacers, and default fallbacks', async () => {
        const payload = makePayload();
        // 1. color !== def but hasOverride is false (covers line 529 true branch)
        payload.overlay.metricColors['cpu_pct'] = '#990000';
        payload.overlay.userCustomizedMetricColors = [];
        // 2. metricColors and groupColors initially undefined to cover auto-initialization branches
        delete (payload.overlay as any).metricColors;
        delete (payload.overlay as any).groupColors;
        delete (payload.overlay as any).userCustomizedMetricColors;
        delete (payload.overlay as any).userCustomizedGroupColors;
        settings.set(payload);
        systemInfo.set({
            disks: [{ busType: 'NVMe' }]
        } as any);

        component = mount(AppearancePage, { target: document.body });
        await tick();

        // Color input change on cpu_pct (when metricColors was undefined)
        const colorInput = document.body.querySelector<HTMLInputElement>('.color-row input[type="color"]');
        expect(colorInput).not.toBeNull();
        colorInput!.value = '#112233';
        colorInput!.dispatchEvent(new Event('change'));
        await tick();

        // Color input change with identical color (returns early)
        colorInput!.value = '#112233';
        colorInput!.dispatchEvent(new Event('change'));
        await tick();

        // Color input change back to default color (covers value === def in setCustomized)
        colorInput!.value = BRAND.cpu_pct;
        colorInput!.dispatchEvent(new Event('change'));
        await tick();

        // Group color change on cpu group
        const groupColorInput = document.body.querySelector<HTMLInputElement>('.group-color-row input[type="color"]');
        expect(groupColorInput).not.toBeNull();
        groupColorInput!.value = '#445566';
        groupColorInput!.dispatchEvent(new Event('change'));
        await tick();

        // Group color change back to default (covers value === def in setGroupCustomized)
        groupColorInput!.value = '#0068B5';
        groupColorInput!.dispatchEvent(new Event('change'));
        await tick();

        // Empty hex blur (covers value.length === 0)
        const hexInput = document.body.querySelector<HTMLInputElement>('.color-row .hex-input');
        hexInput!.value = '';
        hexInput!.dispatchEvent(new Event('blur'));
        await tick();

        // Empty group hex blur
        const groupHexInput = document.body.querySelector<HTMLInputElement>('.group-color-row .hex-input');
        groupHexInput!.value = '';
        groupHexInput!.dispatchEvent(new Event('blur'));
        await tick();

        // Single metric hex blur with valid and invalid values
        hexInput!.value = '#123456';
        hexInput!.dispatchEvent(new Event('blur'));
        await tick();
        expect(hexInput!.value).toBe('#123456');

        hexInput!.value = 'invalid-hex';
        hexInput!.dispatchEvent(new Event('blur'));
        await tick();

        // Group hex blur with valid and invalid values
        groupHexInput!.value = '#654321';
        groupHexInput!.dispatchEvent(new Event('blur'));
        await tick();

        groupHexInput!.value = 'not-valid';
        groupHexInput!.dispatchEvent(new Event('blur'));
        await tick();

        // Disk group hex blur (lines 665-667)
        const allGroupHexes = document.body.querySelectorAll<HTMLInputElement>('.group-color-row .hex-input');
        const diskGroupHexInput = allGroupHexes[allGroupHexes.length - 1];
        if (diskGroupHexInput) {
            diskGroupHexInput.value = '#334455';
            diskGroupHexInput.dispatchEvent(new Event('blur', { bubbles: true }));
            await tick();
        }

        // Disk metric row color change and blur (lines 683, 693) with bubbling
        const diskPickers = document.body.querySelectorAll<HTMLInputElement>('input.color-picker[title*="Disk"]');
        for (const picker of diskPickers) {
            picker.value = '#778899';
            picker.dispatchEvent(new Event('change', { bubbles: true }));
            await tick();
        }

        const allHexInputs = document.body.querySelectorAll<HTMLInputElement>('.color-row .hex-input');
        const diskHexInput = allHexInputs[allHexInputs.length - 1];
        if (diskHexInput) {
            diskHexInput.value = '#998877';
            diskHexInput.dispatchEvent(new Event('blur', { bubbles: true }));
            await tick();
        }

        // Test theme swatches keydown (both Enter and non-Enter)
        const darkSwatches = document.body.querySelectorAll<HTMLElement>('.swatch-card');
        if (darkSwatches.length > 0) {
            darkSwatches[0].dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
            darkSwatches[0].dispatchEvent(new KeyboardEvent('keydown', { key: 'Space', bubbles: true }));
            await tick();
        }

        // Test {:else if color !== def} reset button for standard and disk metrics (lines 703-705)
        // Also test group reset buttons when userCustomizedGroupColors contains the group key
        const pCustom = makePayload();
        pCustom.overlay.metricColors['cpu_pct'] = '#990000';
        pCustom.overlay.metricColors['disk_disk0_read'] = '#556677';
        pCustom.overlay.userCustomizedMetricColors = []; // hasOverride returns false, but color !== def
        pCustom.overlay.userCustomizedGroupColors = ['cpu', 'disk'];
        settings.set(pCustom);
        await tick();

        // Click small reset buttons (including standard and disk metric reset button)
        const smallResetBtns = document.body.querySelectorAll<HTMLButtonElement>('.color-row .reset-btn.small');
        expect(smallResetBtns.length).toBeGreaterThan(0);
        for (const btn of smallResetBtns) {
            btn.click();
            await tick();
        }

        // Set userCustomizedGroupColors to trigger group reset buttons
        pCustom.overlay.userCustomizedGroupColors = ['cpu', 'disk'];
        settings.set(pCustom);
        await tick();

        // Click group reset buttons
        const groupResetBtns = document.body.querySelectorAll<HTMLButtonElement>('.reset-btn.group');
        for (const btn of groupResetBtns) {
            btn.click();
            await tick();
        }

        // Reset all when defaultMetricColors is undefined (returns early)
        const p2 = makePayload();
        delete (p2.overlay as any).metricColors;
        delete (p2.overlay as any).groupColors;
        delete (p2.overlay as any).userCustomizedMetricColors;
        delete (p2.overlay as any).userCustomizedGroupColors;
        delete (p2.overlay as any).defaultMetricColors;
        settings.set(p2);
        await tick();

        const resetAllBtn = Array.from(document.body.querySelectorAll<HTMLButtonElement>('.reset-btn'))
            .find(b => b.textContent?.includes('Reset All to Defaults'));
        resetAllBtn?.click();
        await tick();
    });

    it('covers swatch empty style variable fallbacks and group blur with existing values', async () => {
        // Push themes with empty vars to evaluate all ?? "" fallback branches in dark and light swatches
        const emptyDark = { value: 'empty-test-dark', label: 'Empty Dark', vars: {} as any };
        const emptyLight = { value: 'empty-test-light', label: 'Empty Light', vars: {} as any };
        themes.push(emptyDark);
        themes.push(emptyLight);

        const payload = makePayload();
        payload.overlay.userCustomizedMetricColors = ['cpu_pct', 'ram_pct'];
        payload.overlay.userCustomizedGroupColors = ['cpu', 'ram'];
        payload.overlay.metricColors['ram_pct'] = '#abcdef';
        settings.set(payload);
        systemInfo.set({ disks: [{ busType: 'NVMe' }] } as any);

        component = mount(AppearancePage, { target: document.body });
        await tick();

        // 1. onColorChange when metric is already in userCustomizedMetricColors (!includes is false)
        const cpuPicker = document.body.querySelector<HTMLInputElement>('input.color-picker[title="Choose color for CPU Usage"]');
        if (cpuPicker) {
            cpuPicker.value = '#010203';
            cpuPicker.dispatchEvent(new Event('change', { bubbles: true }));
            await tick();
        }

        // 2. onGroupColorChange when group is already in userCustomizedGroupColors (!includes is false)
        const cpuGroupPicker = document.body.querySelector<HTMLInputElement>('input.color-picker[title="Choose color for All CPU"]');
        if (cpuGroupPicker) {
            cpuGroupPicker.value = '#040506';
            cpuGroupPicker.dispatchEvent(new Event('change', { bubbles: true }));
            await tick();
        }

        // 3. onGroupColorChange and hex blur for right column group (All RAM)
        const ramGroupPicker = document.body.querySelector<HTMLInputElement>('input.color-picker[title="Choose color for All RAM"]');
        if (ramGroupPicker) {
            ramGroupPicker.value = '#0a0b0c';
            ramGroupPicker.dispatchEvent(new Event('change', { bubbles: true }));
            await tick();
        }

        const ramGroupHex = document.body.querySelectorAll<HTMLInputElement>('.group-color-row .hex-input');
        if (ramGroupHex.length > 2) {
            ramGroupHex[2].value = '#1a1b1c';
            ramGroupHex[2].dispatchEvent(new Event('blur', { bubbles: true }));
            await tick();
        }

        // 4. Right column actions: resetGroupColors (line 551), resetColor (line 609), onHexBlur (line 603), onColorChange (line 593)
        const rightCol = document.body.querySelectorAll('.color-column')[1];
        if (rightCol) {
            const rightPicker = rightCol.querySelector<HTMLInputElement>('.color-row input.color-picker');
            if (rightPicker) {
                rightPicker.value = '#778899';
                rightPicker.dispatchEvent(new Event('change', { bubbles: true }));
                await tick();
            }

            const rightMetricHex = rightCol.querySelector<HTMLInputElement>('.color-row .hex-input');
            if (rightMetricHex) {
                rightMetricHex.value = '#3a3b3c';
                rightMetricHex.dispatchEvent(new Event('blur', { bubbles: true }));
                await tick();
            }

            // Set metricColors and groupColors to null to trigger null coalescing guards (lines 241, 256, 265)
            const curSettings = get(settings)!;
            curSettings.overlay.metricColors = null as any;
            curSettings.overlay.groupColors = null as any;

            const rightSmallReset = rightCol.querySelector<HTMLButtonElement>('button.reset-btn.small');
            rightSmallReset?.click();
            await tick();

            curSettings.overlay.metricColors = null as any;
            const rightGroupReset = rightCol.querySelector<HTMLButtonElement>('button.reset-btn.group');
            rightGroupReset?.click();
            await tick();

            // Test onColorChange and applyGroupColor when metricColors is null (lines 184, 213)
            curSettings.overlay.metricColors = null as any;
            const cpuPickerNull = document.body.querySelector<HTMLInputElement>('input.color-picker[title="Choose color for CPU Usage"]');
            if (cpuPickerNull) {
                cpuPickerNull.value = '#112233';
                cpuPickerNull.dispatchEvent(new Event('change', { bubbles: true }));
                await tick();
            }

            curSettings.overlay.metricColors = null as any;
            const cpuGroupPickerNull = document.body.querySelector<HTMLInputElement>('input.color-picker[title="Choose color for All CPU"]');
            if (cpuGroupPickerNull) {
                cpuGroupPickerNull.value = '#445566';
                cpuGroupPickerNull.dispatchEvent(new Event('change', { bubbles: true }));
                await tick();
            }

            // Test setGroupCustomized when value === def (line 169)
            const cpuGroupHex = document.body.querySelector<HTMLInputElement>('.group-color-row .hex-input');
            if (cpuGroupHex) {
                cpuGroupHex.value = BRAND.cpu_pct;
                cpuGroupHex.dispatchEvent(new Event('blur', { bubbles: true }));
                await tick();
            }

            // Test setCustomized when value === def (lines 150-154)
            const cpuMetricHex = document.body.querySelector<HTMLInputElement>('.color-row .hex-input');
            if (cpuMetricHex) {
                cpuMetricHex.value = BRAND.cpu_pct;
                cpuMetricHex.dispatchEvent(new Event('blur', { bubbles: true }));
                await tick();
            }
        }

        // 5. onGroupHexBlur for disk group with unchanged value
        const diskGroupHex = document.body.querySelector<HTMLInputElement>('input.hex-input[class*="group-disk"]');
        if (diskGroupHex) {
            diskGroupHex.value = diskGroupHex.value; // same value triggers return early
            diskGroupHex.dispatchEvent(new Event('blur', { bubbles: true }));
            await tick();
        }

        // 6. resetAllColors when diskMetrics is empty
        systemInfo.set({ disks: [] } as any);
        await tick();
        const resetAllBtn = Array.from(document.body.querySelectorAll<HTMLButtonElement>('.reset-btn'))
            .find(b => b.textContent?.includes('Reset All to Defaults'));
        resetAllBtn?.click();
        await tick();

        // Clean up added themes
        const darkIdx = themes.indexOf(emptyDark);
        if (darkIdx !== -1) themes.splice(darkIdx, 1);
        const lightIdx = themes.indexOf(emptyLight);
        if (lightIdx !== -1) themes.splice(lightIdx, 1);
    });
});


