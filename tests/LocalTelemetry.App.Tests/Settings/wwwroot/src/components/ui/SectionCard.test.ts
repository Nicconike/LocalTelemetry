import { mount, unmount, tick } from 'svelte';
import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import SectionCard from '../../../../../../../src/LocalTelemetry.App/Settings/wwwroot/src/components/ui/SectionCard.svelte';

describe('SectionCard.svelte', () => {
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

    it('renders title and description', async () => {
        component = mount(SectionCard, {
            target: document.body,
            props: {
                title: 'Startup Settings',
                description: 'Configure Windows startup behavior',
            },
        });
        await tick();

        expect(document.body.textContent).toContain('Startup Settings');
        expect(document.body.textContent).toContain('Configure Windows startup behavior');
    });

    it('renders card without title', async () => {
        component = mount(SectionCard, {
            target: document.body,
            props: {},
        });
        await tick();

        expect(document.querySelector('.card')).not.toBeNull();
        expect(document.querySelector('.card-head')).toBeNull();
    });
});
