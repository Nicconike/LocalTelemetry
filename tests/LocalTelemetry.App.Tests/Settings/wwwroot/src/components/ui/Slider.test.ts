import { mount, unmount, tick } from 'svelte';
import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import Slider from '../../../../../../../src/LocalTelemetry.App/Settings/wwwroot/src/components/ui/Slider.svelte';

describe('Slider.svelte', () => {
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

    it('renders label and range input', async () => {
        component = mount(Slider, {
            target: document.body,
            props: {
                label: 'Opacity',
                value: 80,
                min: 0,
                max: 100,
                step: 1,
                unit: '%',
            },
        });
        await tick();

        const input = document.querySelector('input[type="range"]') as HTMLInputElement;
        expect(input).not.toBeNull();
        expect(input.value).toBe('80');
        expect(document.body.textContent).toContain('Opacity');
        expect(document.body.textContent).toContain('80%');
    });
});
