import { mount, unmount, tick } from 'svelte';
import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { trafficHistory, allTrafficHistory, trafficToday, trafficMonths } from '../../../../../../../src/LocalTelemetry.App/Settings/wwwroot/src/lib/store';
import TrafficHistoryPage from '../../../../../../../src/LocalTelemetry.App/Settings/wwwroot/src/components/pages/TrafficHistoryPage.svelte';

describe('TrafficHistoryPage.svelte', () => {
    let component: ReturnType<typeof mount> | undefined;

    beforeEach(() => {
        document.body.innerHTML = '';
        trafficHistory.set([]);
        allTrafficHistory.set([]);
        trafficToday.set({ downBytes: 1024, upBytes: 2048 });
        trafficMonths.set([]);
    });

    afterEach(() => {
        if (component) {
            unmount(component);
            component = undefined;
        }
    });

    it('renders traffic history statistics page', async () => {
        component = mount(TrafficHistoryPage, { target: document.body });
        await tick();

        expect(document.body.textContent).toContain('Traffic History');
    });
});
