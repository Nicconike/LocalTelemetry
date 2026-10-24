import { mount, unmount, tick } from 'svelte';
import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { settings, nics } from '../../../../../../../src/LocalTelemetry.App/Settings/wwwroot/src/lib/store';
import NetworkPage from '../../../../../../../src/LocalTelemetry.App/Settings/wwwroot/src/components/pages/NetworkPage.svelte';

describe('NetworkPage.svelte', () => {
    let component: ReturnType<typeof mount> | undefined;

    beforeEach(() => {
        document.body.innerHTML = '';
        settings.set({
            monitoring: {
                useNetBits: false,
                preferredNic: 'auto',
            },
            netUsage: { enabled: false },
        } as any);
        nics.set([
            'Ethernet',
            'Wi-Fi',
        ]);
    });

    afterEach(() => {
        if (component) {
            unmount(component);
            component = undefined;
        }
    });

    it('renders network page settings', async () => {
        component = mount(NetworkPage, { target: document.body });
        await tick();

        expect(document.body.textContent).toContain('Network');
        expect(document.body.textContent).toContain('Traffic History');
        expect(document.body.textContent).toContain('Record Daily Traffic History');
    });

    it('renders correctly when nics is null or undefined', async () => {
        nics.set(null as any);
        component = mount(NetworkPage, { target: document.body });
        await tick();

        expect(document.body.textContent).toContain('Network');
        const select = document.querySelector('select') as HTMLSelectElement;
        expect(select.options).toHaveLength(1);
        expect(select.options[0].text).toContain('Auto');
    });

    it('renders empty when settings is null', async () => {
        settings.set(null as any);
        component = mount(NetworkPage, { target: document.body });
        await tick();

        expect(document.querySelector('.page')).toBeNull();
    });
});
