import { mount, unmount, tick } from 'svelte';
import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { settings } from '../../../../../../../src/LocalTelemetry.App/Settings/wwwroot/src/lib/store';
import OverlayPage from '../../../../../../../src/LocalTelemetry.App/Settings/wwwroot/src/components/pages/OverlayPage.svelte';

describe('OverlayPage.svelte', () => {
    let component: ReturnType<typeof mount> | undefined;

    beforeEach(() => {
        document.body.innerHTML = '';
        settings.set({
            overlay: {
                visible: true,
                position: 'left',
                doubleClickAction: 'none',
                opacity: 100,
                scale: 100,
                fontSizePx: 18,
                fontBold: false,
                offsetX: 0,
            },
        } as any);
    });

    afterEach(() => {
        if (component) {
            unmount(component);
            component = undefined;
        }
    });

    it('renders taskbar overlay settings', async () => {
        component = mount(OverlayPage, { target: document.body });
        await tick();

        expect(document.body.textContent).toContain('Overlay Visibility & Interaction');
    });

    it('disables overlay toggle when system tray icon is not visible to prevent lockout', async () => {
        settings.set({
            showSystemTrayIcon: false,
            overlay: {
                visible: true,
                position: 'left',
                doubleClickAction: 'none',
                opacity: 100,
                scale: 100,
                fontSizePx: 18,
                fontBold: false,
                offsetX: 0,
            },
        } as any);

        component = mount(OverlayPage, { target: document.body });
        await tick();

        const overlayInput = document.querySelector('input[aria-label="Show Overlay"]') as HTMLInputElement;
        expect(overlayInput).not.toBeNull();
        expect(overlayInput.disabled).toBe(true);
        expect(document.body.textContent).toContain('Cannot disable: System Tray Icon is hidden');
    });

    it('renders empty when settings is null', async () => {
        settings.set(null as any);
        component = mount(OverlayPage, { target: document.body });
        await tick();

        expect(document.querySelector('.page')).toBeNull();
    });

    it('adapts position options based on taskbar alignment', async () => {
        settings.set({
            showSystemTrayIcon: true,
            overlay: {
                visible: true,
                position: 'right',
                taskbarAlignment: 'left',
                doubleClickAction: 'none',
                opacity: 100,
                scale: 100,
                fontSizePx: 18,
                fontBold: false,
                offsetX: 0,
            },
        } as any);

        component = mount(OverlayPage, { target: document.body });
        await tick();

        expect(document.body.textContent).toContain('Next to Apps');
        expect(document.body.textContent).toContain('Before System Tray');
    });
});
