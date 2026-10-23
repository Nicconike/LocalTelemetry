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
            { value: 'opt3', label: undefined as any },
            { value: undefined as any, label: undefined as any },
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
        expect(document.querySelectorAll('option')).toHaveLength(4);
        expect(document.body.textContent).toContain('Test Select');

        unmount(component);
        component = mount(Select, { target: document.body, props: {} });
        await tick();
    });

    it('renders inline select without label and handles onchange event', async () => {
        let changedVal = '';
        component = mount(Select, {
            target: document.body,
            props: {
                id: 'inline-sel',
                value: 'two',
                inline: true,
                onchange: (e: Event) => {
                    changedVal = (e.target as HTMLSelectElement).value;
                },
                options: [
                    { value: 'one', label: 'One' },
                    { value: 'two', label: 'Two' },
                    { value: undefined as any, label: 'Three' },
                    { value: 'four', label: '' },
                ],
            },
        });
        await tick();

        const wrap = document.querySelector('.sel-wrap.inline');
        expect(wrap).not.toBeNull();
        expect(document.querySelector('label')).toBeNull();

        const select = document.querySelector('select') as HTMLSelectElement;
        expect(select.value).toBe('two');

        select.value = 'one';
        select.dispatchEvent(new Event('change', { bubbles: true }));
        await tick();
        expect(changedVal).toBe('one');
    });
});
