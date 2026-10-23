import { mount, unmount, tick } from 'svelte';
import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import Toggle from '../../../../../../../src/LocalTelemetry.App/Settings/wwwroot/src/components/ui/Toggle.svelte';

describe('Toggle.svelte', () => {
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

    it('renders label and handles check state changes', async () => {
        let checked = false;
        component = mount(Toggle, {
            target: document.body,
            props: {
                label: 'Test Toggle',
                checked,
            },
        });
        await tick();

        const input = document.querySelector('input[type="checkbox"]') as HTMLInputElement;
        expect(input).not.toBeNull();
        expect(document.body.textContent).toContain('Test Toggle');

        input.checked = true;
        input.dispatchEvent(new Event('change'));
        await tick();
    });

    it('renders optional description when provided', async () => {
        component = mount(Toggle, {
            target: document.body,
            props: {
                label: 'Speed Unit',
                description: 'Display network speed in bits/s',
                checked: false,
            },
        });
        await tick();

        expect(document.body.textContent).toContain('Speed Unit');
        expect(document.body.textContent).toContain('Display network speed in bits/s');
        const descSpan = document.querySelector('.desc');
        expect(descSpan).not.toBeNull();
        expect(descSpan?.textContent).toBe('Display network speed in bits/s');
    });

    it('renders description without label and handles disabled state', async () => {
        component = mount(Toggle, {
            target: document.body,
            props: {
                description: 'Only description here',
                checked: true,
                disabled: true,
            },
        });
        await tick();

        const input = document.querySelector('input[type="checkbox"]') as HTMLInputElement;
        expect(input).not.toBeNull();
        expect(input.disabled).toBe(true);
        expect(document.querySelector('.lbl')).toBeNull();
        expect(document.querySelector('.desc')?.textContent).toBe('Only description here');
    });
});
