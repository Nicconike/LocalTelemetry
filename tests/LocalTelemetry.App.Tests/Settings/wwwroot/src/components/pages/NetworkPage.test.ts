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
            { id: 'auto', name: 'Auto-detect Active Interface' },
            { id: 'eth0', name: 'Ethernet' },
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
    });
});
