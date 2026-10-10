import { mount, unmount, tick } from 'svelte';
import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import AboutPage from '../../../../../../../src/LocalTelemetry.App/Settings/wwwroot/src/components/pages/AboutPage.svelte';

describe('AboutPage.svelte', () => {
    let component: ReturnType<typeof mount> | undefined;

    beforeEach(() => {
        document.body.innerHTML = '';
    });

    afterEach(() => {
        if (component) {
            unmount(component);
            component = undefined;
        }
    });

    it('renders application title and details', async () => {
        component = mount(AboutPage, { target: document.body });
        await tick();

        expect(document.body.textContent).toContain('LocalTelemetry');
        expect(document.body.textContent).toContain('Resources');
    });
});
