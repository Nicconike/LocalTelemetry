import { mount, unmount, tick } from 'svelte';
import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { systemInfo } from '../../../../../../../src/LocalTelemetry.App/Settings/wwwroot/src/lib/store';
import SystemPage from '../../../../../../../src/LocalTelemetry.App/Settings/wwwroot/src/components/pages/SystemPage.svelte';

describe('SystemPage.svelte', () => {
    let component: ReturnType<typeof mount> | undefined;

    beforeEach(() => {
        document.body.innerHTML = '';
        systemInfo.set({
            os: 'Windows 11 Pro',
            cpu: 'Intel Core i9-13900K',
            deviceName: 'DESKTOP-LT',
            ramTotalMb: 32768,
            disks: [],
            gpus: [],
        } as any);
    });

    afterEach(() => {
        if (component) {
            unmount(component);
            component = undefined;
        }
    });

    it('renders system information details', async () => {
        component = mount(SystemPage, { target: document.body });
        await tick();

        expect(document.body.textContent).toContain('System');
        expect(document.body.textContent).toContain('Windows 11 Pro');
        expect(document.body.textContent).toContain('Intel Core i9-13900K');
    });
});
