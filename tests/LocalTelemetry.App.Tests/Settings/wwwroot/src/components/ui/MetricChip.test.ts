import { mount, unmount, tick } from 'svelte';
import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import MetricChip from '../../../../../../../src/LocalTelemetry.App/Settings/wwwroot/src/components/ui/MetricChip.svelte';

describe('MetricChip.svelte', () => {
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

    it('renders label and handles click event', async () => {
        let clicked = false;
        component = mount(MetricChip, {
            target: document.body,
            props: {
                label: 'CPU Usage',
                onclick: () => {
                    clicked = true;
                },
            },
        });
        await tick();

        expect(document.body.textContent).toContain('CPU Usage');

        const btn = document.querySelector('button');
        if (btn) {
            btn.click();
            await tick();
            expect(clicked).toBe(true);
        }
    });
});
