import { mount, unmount, tick } from 'svelte';
import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import Select from '../../../../../../../src/LocalTelemetry.App/Settings/wwwroot/src/components/ui/Select.svelte';

describe('Select.svelte', () => {
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

    it('renders label and select options', async () => {
        const options = [
            { value: 'opt1', label: 'Option 1' },
            { value: 'opt2', label: 'Option 2' },
        ];
        component = mount(Select, {
            target: document.body,
            props: {
                label: 'Test Select',
                value: 'opt1',
                options,
            },
        });
        await tick();

        const select = document.querySelector('select') as HTMLSelectElement;
        expect(select).not.toBeNull();
        expect(select.options.length).toBe(2);
        expect(document.body.textContent).toContain('Test Select');
    });
});
