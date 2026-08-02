import { get } from 'svelte/store';
import {
    settings,
    pristineSettings,
    saveStatus,
    nics,
    activePage,
    allTrafficHistory,
    trafficHistory,
    trafficToday,
    trafficMonths,
    trafficCache,
    importResult
} from '../../../../../../src/LocalTelemetry.App/Settings/wwwroot/src/lib/store';

describe('store.ts', () => {
    it('stores should have correct default values', () => {
        expect(get(settings)).toBeNull();
        expect(get(pristineSettings)).toBe('');
        expect(get(saveStatus)).toBe('idle');
        expect(get(nics)).toEqual([]);
        expect(get(activePage)).toBe('general');
        expect(get(allTrafficHistory)).toEqual([]);
        expect(get(trafficHistory)).toEqual([]);
        expect(get(trafficToday)).toEqual({ downBytes: 0, upBytes: 0 });
        expect(get(trafficMonths)).toEqual([]);
        expect(get(importResult)).toBeNull();
    });

    it('stores should allow updating values', () => {
        activePage.set('monitoring');
        expect(get(activePage)).toBe('monitoring');

        saveStatus.set('saving');
        expect(get(saveStatus)).toBe('saving');

        nics.set(['Ethernet', 'WiFi']);
        expect(get(nics)).toEqual(['Ethernet', 'WiFi']);
    });

    it('trafficCache should operate as a Map', () => {
        trafficCache.clear();
        trafficCache.set('2026-08', [{ date: '2026-08-01', downBytes: 100, upBytes: 200 }]);
        expect(trafficCache.has('2026-08')).toBe(true);
        expect(trafficCache.get('2026-08')).toHaveLength(1);
    });
});
