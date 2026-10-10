import { mount, unmount, tick } from 'svelte';
import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { settings } from '../../../../../../../src/LocalTelemetry.App/Settings/wwwroot/src/lib/store';
import GeneralPage from '../../../../../../../src/LocalTelemetry.App/Settings/wwwroot/src/components/pages/GeneralPage.svelte';

describe('GeneralPage.svelte', () => {
    let component: ReturnType<typeof mount> | undefined;

    beforeEach(() => {
        document.body.innerHTML = '';
        settings.set({
            runAtStartup: false,
            showSystemTrayIcon: true,
            enableFileLogging: false,
            monitoring: {
                intervalMs: 1000,
                useFahrenheit: false,
            },
        } as any);
    });

    afterEach(() => {
        if (component) {
            unmount(component);
            component = undefined;
        }
    });

    it('renders general settings page', async () => {
        component = mount(GeneralPage, { target: document.body });
        await tick();

        expect(document.body.textContent).toContain('General');
        expect(document.body.textContent).toContain('Startup');
        expect(document.body.textContent).toContain('Polling Interval');
    });

    it('disables tray icon toggle when overlay is not visible to prevent lockout', async () => {
        settings.set({
            runAtStartup: false,
            showSystemTrayIcon: true,
            enableFileLogging: false,
            overlay: { visible: false },
            monitoring: {
                intervalMs: 1000,
                useFahrenheit: false,
            },
        } as any);

        component = mount(GeneralPage, { target: document.body });
        await tick();

        const trayInput = document.querySelector('input[aria-label="Show System Tray Icon"]') as HTMLInputElement;
        expect(trayInput).not.toBeNull();
        expect(trayInput.disabled).toBe(true);
        expect(document.body.textContent).toContain('Cannot disable: Overlay is hidden');
    });
});
