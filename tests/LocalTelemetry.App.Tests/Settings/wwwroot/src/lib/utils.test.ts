import { clamp, formatBytes, formatTrafficBytes, deepClone, debounce } from '../../../../../../src/LocalTelemetry.App/Settings/wwwroot/src/lib/utils';

describe('utils.ts', () => {
    it('clamp should constrain numbers within range', () => {
        expect(clamp(5, 0, 10)).toBe(5);
        expect(clamp(-5, 0, 10)).toBe(0);
        expect(clamp(15, 0, 10)).toBe(10);
    });

    it('formatBytes should format sizes accurately', () => {
        expect(formatBytes(500)).toBe('500 B');
        expect(formatBytes(1024)).toBe('1.0 KB');
        expect(formatBytes(1048576)).toBe('1.0 MB');
        expect(formatBytes(1073741824)).toBe('1.00 GB');
    });

    it('formatTrafficBytes should format bandwidth sizes accurately', () => {
        expect(formatTrafficBytes(500)).toBe('500B');
        expect(formatTrafficBytes(1000)).toBe('1.00KB');
        expect(formatTrafficBytes(1_000_000)).toBe('1.00MB');
        expect(formatTrafficBytes(1_000_000_000)).toBe('1.00GB');
        expect(formatTrafficBytes(1_000_000_000_000)).toBe('1.00TB');
    });

    it('deepClone should produce a deep copy', () => {
        const original = { a: 1, b: { c: 'hello' } };
        const copy = deepClone(original);

        expect(copy).toEqual(original);
        expect(copy).not.toBe(original);
        expect(copy.b).not.toBe(original.b);
    });

    it('debounce should delay function execution', async () => {
        vi.useFakeTimers();
        const fn = vi.fn();
        const debounced = debounce(fn, 200);

        debounced('test');
        expect(fn).not.toHaveBeenCalled();

        vi.advanceTimersByTime(100);
        expect(fn).not.toHaveBeenCalled();

        vi.advanceTimersByTime(150);
        expect(fn).toHaveBeenCalledWith('test');
        vi.useRealTimers();
    });
});
