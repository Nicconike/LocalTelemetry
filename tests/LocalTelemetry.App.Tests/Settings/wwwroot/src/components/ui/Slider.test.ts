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

        unmount(component);
        component = mount(Slider, {
            target: document.body,
            props: {
                label: 'NoUnit',
                value: 10,
                unit: '',
            },
        });
        await tick();
        expect(document.body.textContent).toContain('10');

        unmount(component);
        component = mount(Slider, {
            target: document.body,
            props: {
                label: 'NullValues',
                value: null as any,
                unit: null as any,
            },
        });
        await tick();
        expect(document.body.textContent).toContain('NullValues');
    });

    it('renders slider without label and handles onchange event', async () => {
        let changedVal = '';
        component = mount(Slider, {
            target: document.body,
            props: {
                value: 50,
                min: 0,
                max: 100,
                onchange: (e: Event) => {
                    changedVal = (e.target as HTMLInputElement).value;
                },
            },
        });
        await tick();

        const input = document.querySelector('input[type="range"]') as HTMLInputElement;
        expect(input).not.toBeNull();
        expect(input.value).toBe('50');
        expect(document.querySelector('.slider-header')).toBeNull();

        input.value = '75';
        input.dispatchEvent(new Event('change', { bubbles: true }));
        await tick();
        expect(changedVal).toBe('75');

        unmount(component);
        component = mount(Slider, { target: document.body, props: { label: 'DefaultProps' } });
        await tick();
        expect(document.body.textContent).toContain('50');
    });
});
