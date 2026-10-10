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
});
