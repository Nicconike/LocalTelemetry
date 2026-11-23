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

        expect(document.body.textContent).toContain('Visibility & Interaction');
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

    it('renders taskbar space constrained alert banner when flag is active', async () => {
        settings.set({
            showSystemTrayIcon: true,
            overlay: {
                visible: true,
                position: 'left',
                taskbarAlignment: 'center',
                taskbarSpaceConstrained: true,
                availableSlotWidth: 250,
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

        expect(document.body.textContent).toContain('Taskbar space is constrained');
        expect(document.body.textContent).toContain('Open Taskbar Settings');
        expect(document.body.textContent).toContain('Dismiss');

        const openSettingsBtn = Array.from(document.querySelectorAll<HTMLButtonElement>('.banner-btn')).find(
            b => b.textContent?.includes('Open Taskbar Settings')
        );
        expect(openSettingsBtn).toBeDefined();
        openSettingsBtn?.click();
        await tick();
    });

    it('dismisses warning banner when dismiss button is clicked', async () => {
        settings.set({
            showSystemTrayIcon: true,
            overlay: {
                visible: true,
                position: 'left',
                taskbarAlignment: 'center',
                taskbarSpaceConstrained: true,
                availableSlotWidth: 250,
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

        expect(document.body.textContent).toContain('Taskbar space is constrained');
        const dismissBtn = document.querySelector('.banner-btn-dismiss') as HTMLButtonElement;
        expect(dismissBtn).not.toBeNull();
        dismissBtn.click();
        await tick();

        expect(document.body.textContent).not.toContain('Taskbar space is constrained');
    });

    it('resets dismissed state when space constraint clears and recurs', async () => {
        settings.set({
            showSystemTrayIcon: true,
            overlay: {
                visible: true,
                position: 'left',
                taskbarAlignment: 'center',
                taskbarSpaceConstrained: true,
                availableSlotWidth: 250,
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

        const dismissBtn = document.querySelector('.banner-btn-dismiss') as HTMLButtonElement;
        dismissBtn.click();
        await tick();
        expect(document.body.textContent).not.toContain('Taskbar space is constrained');

        // Space clears
        settings.update((s: any) => ({
            ...s,
            overlay: { ...s.overlay, taskbarSpaceConstrained: false },
        }));
        await tick();
        expect(document.body.textContent).not.toContain('Taskbar space is constrained');

        // Space constrained again -> warning reappears
        settings.update((s: any) => ({
            ...s,
            overlay: { ...s.overlay, taskbarSpaceConstrained: true },
        }));
        await tick();
        expect(document.body.textContent).toContain('Taskbar space is constrained');
    });

    it('renders target displays section and handles display selection toggles', async () => {
        settings.set({
            showSystemTrayIcon: true,
            overlay: {
                visible: true,
                position: 'left',
                displayMode: 'custom',
                enabledDisplays: ['primary'],
                detectedDisplays: [
                    { id: 'primary', name: 'Display 1 (Primary - 2048 x 1280)', isPrimary: true, hasTaskbar: true },
                    { id: 'display_2', name: 'Display 2 (1920 x 1080)', isPrimary: false, hasTaskbar: true },
                    { id: 'display_3', name: 'Display 3 (2560 x 1440)', isPrimary: false, hasTaskbar: false },
                ],
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

        expect(document.body.textContent).toContain('Target Displays');
        expect(document.body.textContent).toContain('Display 1 (Primary - 2048 x 1280)');
        expect(document.body.textContent).toContain('Display 2 (1920 x 1080)');
        expect(document.body.textContent).toContain('(No taskbar detected)');

        const checkboxes = document.querySelectorAll('.display-checkbox-item input[type="checkbox"]') as NodeListOf<HTMLInputElement>;
        expect(checkboxes).toHaveLength(3);
        expect(checkboxes[0].checked).toBe(true);
        expect(checkboxes[1].checked).toBe(false);

        // Check Display 2
        checkboxes[1].click();
        await tick();

        let currentSettings: any;
        settings.subscribe(s => currentSettings = s)();
        expect(currentSettings.overlay.enabledDisplays).toContain('display_2');
    });

    it('renders secondary option and shows warning note when only one display detected', async () => {
        settings.set({
            showSystemTrayIcon: true,
            overlay: {
                visible: true,
                position: 'left',
                displayMode: 'secondary',
                enabledDisplays: ['primary'],
                detectedDisplays: [
                    { id: 'primary', name: 'Display 1 (Primary - 2048 x 1280)', isPrimary: true, hasTaskbar: true },
                ],
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

        expect(document.body.textContent).toContain('Secondary Display Only');
        expect(document.body.textContent).toContain('No secondary display detected. The overlay will show on the primary display until another monitor is connected.');
    });
});
