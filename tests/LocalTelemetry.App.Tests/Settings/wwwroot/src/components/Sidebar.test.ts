import { mount, unmount, tick } from 'svelte';
import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { activePage } from '../../../../../../src/LocalTelemetry.App/Settings/wwwroot/src/lib/store';
import Sidebar from '../../../../../../src/LocalTelemetry.App/Settings/wwwroot/src/components/Sidebar.svelte';

describe('Sidebar.svelte', () => {
    let component: ReturnType<typeof mount> | undefined;

    beforeEach(() => {
        document.body.innerHTML = '';
        activePage.set('general');
    });

    afterEach(() => {
        if (component) {
            unmount(component);
            component = undefined;
        }
    });

    it('renders sidebar navigation items', async () => {
        component = mount(Sidebar, { target: document.body });
        await tick();

        const items = document.querySelectorAll('.nav-item');
        expect(items.length).toBeGreaterThan(5);
        expect(document.body.textContent).toContain('General');
        expect(document.body.textContent).toContain('Monitoring');
        expect(document.body.textContent).toContain('About');
    });

    it('navigates when clicking a navigation item', async () => {
        let dispatchedPage = '';
        component = mount(Sidebar, {
            target: document.body,
            events: {
                navigate: (e: any) => {
                    dispatchedPage = e.detail;
                }
            }
        });
        await tick();

        const aboutBtn = Array.from(document.querySelectorAll<HTMLButtonElement>('.nav-item')).find((b) =>
            b.textContent?.includes('About')
        );
        expect(aboutBtn).toBeDefined();
        aboutBtn?.click();
        await tick();

        expect(dispatchedPage).toBe('about');
    });

    it('updates active state when activePage changes and handles click without listener', async () => {
        component = mount(Sidebar, { target: document.body });
        await tick();

        activePage.set('network');
        await tick();

        const networkBtn = Array.from(document.querySelectorAll<HTMLButtonElement>('.nav-item')).find((b) =>
            b.textContent?.includes('Network')
        );
        expect(networkBtn?.classList.contains('active')).toBe(true);
        expect(networkBtn?.getAttribute('aria-current')).toBe('page');

        networkBtn?.click();
        await tick();

        activePage.set('none');
        await tick();
        const activeItems = document.querySelectorAll('.nav-item.active');
        expect(activeItems).toHaveLength(0);
    });
});
