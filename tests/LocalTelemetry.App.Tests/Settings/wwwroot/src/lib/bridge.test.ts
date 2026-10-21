import { get } from 'svelte/store';
import { pristineSettings } from '../../../../../../src/LocalTelemetry.App/Settings/wwwroot/src/lib/store';
import {
    post,
    onMessage,
    requestSettings,
    saveSettings,
    requestNics,
    requestTrafficMonths,
    requestSystemInfo,
    requestTrafficHistoryAll,
    requestHistoryData,
    requestHistoryMonths,
    importDatContent,
    importWindowsUsage,
    openFolder,
    openUrl,
    checkForUpdates,
    logError,
    logWarn
} from '../../../../../../src/LocalTelemetry.App/Settings/wwwroot/src/lib/bridge';

describe('bridge.ts', () => {
    let mockPostMessage: ReturnType<typeof vi.fn>;
    let mockAddEventListener: ReturnType<typeof vi.fn>;
    let mockRemoveEventListener: ReturnType<typeof vi.fn>;
    let messageListener: (event: MessageEvent) => void;

    beforeEach(() => {
        mockPostMessage = vi.fn();
        mockAddEventListener = vi.fn((event: string, listener: EventListener) => {
            if (event === 'message') {
                messageListener = listener as (event: MessageEvent) => void;
            }
        });
        mockRemoveEventListener = vi.fn();

        (window as unknown as { chrome: unknown }).chrome = {
            webview: {
                postMessage: mockPostMessage,
                addEventListener: mockAddEventListener,
                removeEventListener: mockRemoveEventListener
            }
        };
    });

    it('post helper calls window.chrome.webview.postMessage when available', () => {
        post('getSettings');
        expect(mockPostMessage).toHaveBeenCalledWith({ type: 'getSettings' });
    });

    it('post helper includes payload when provided', () => {
        post('saveSettings', { theme: 'dark' });
        expect(mockPostMessage).toHaveBeenCalledWith({
            type: 'saveSettings',
            payload: { theme: 'dark' }
        });
    });

    it('saveSettings updates pristineSettings store and posts message', () => {
        const payload = { windowTheme: 'dark', monitoring: {} };
        saveSettings(payload);

        expect(get(pristineSettings)).toBe(JSON.stringify(payload));
        expect(mockPostMessage).toHaveBeenCalledWith({
            type: 'saveSettings',
            payload
        });
    });

    it('onMessage subscribes, handles events, handles errors and unsubscribes', () => {
        const handler = vi.fn();
        const unsubscribe = onMessage('settings', handler);

        expect(mockAddEventListener).toHaveBeenCalledWith('message', expect.any(Function));

        // Simulate valid message event dispatch
        messageListener({ data: { type: 'settings', payload: { test: true } } } as MessageEvent);
        expect(handler).toHaveBeenCalledWith({ test: true });

        // Simulate message event without payload (fallback to data)
        messageListener({ data: { type: 'settings' } } as MessageEvent);

        // Simulate malformed getter error to trigger catch block
        const warnSpy = vi.spyOn(console, 'warn').mockImplementation(() => { });
        const throwingEvent = {
            get data() {
                throw new Error('Malformed message');
            }
        } as unknown as MessageEvent;

        messageListener(throwingEvent);
        expect(warnSpy).toHaveBeenCalled();
        warnSpy.mockRestore();

        unsubscribe();
        expect(mockRemoveEventListener).toHaveBeenCalledWith('message', expect.any(Function));
    });

    it('all request functions dispatch expected IPC actions', () => {
        requestSettings();
        expect(mockPostMessage).toHaveBeenCalledWith({ type: 'getSettings' });

        requestNics();
        expect(mockPostMessage).toHaveBeenCalledWith({ type: 'getNics' });

        requestTrafficMonths();
        expect(mockPostMessage).toHaveBeenCalledWith({ type: 'getTrafficMonths' });

        requestSystemInfo();
        expect(mockPostMessage).toHaveBeenCalledWith({ type: 'getSystemInfo' });

        requestTrafficHistoryAll();
        expect(mockPostMessage).toHaveBeenCalledWith({ type: 'getTrafficHistoryAll' });

        requestHistoryData(2026, 8);
        expect(mockPostMessage).toHaveBeenCalledWith({
            type: 'getHistoryData',
            payload: { year: 2026, month: 8 }
        });

        requestHistoryMonths();
        expect(mockPostMessage).toHaveBeenCalledWith({ type: 'getHistoryMonths' });

        importDatContent('dat_data');
        expect(mockPostMessage).toHaveBeenCalledWith({
            type: 'importDat',
            payload: { content: 'dat_data' }
        });

        importWindowsUsage('01-01-2026', '31-01-2026');
        expect(mockPostMessage).toHaveBeenCalledWith({
            type: 'importWindowsUsage',
            payload: { startDate: '01-01-2026', endDate: '31-01-2026' }
        });

        openFolder('data');
        expect(mockPostMessage).toHaveBeenCalledWith({
            type: 'openFolder',
            payload: { target: 'data' }
        });

        checkForUpdates(true);
        expect(mockPostMessage).toHaveBeenCalledWith({
            type: 'checkForUpdates',
            payload: { force: true }
        });

        openUrl('https://example.com');
        expect(mockPostMessage).toHaveBeenCalledWith({
            type: 'openUrl',
            payload: { url: 'https://example.com' }
        });
    });

    it('saveSettings handles serialization errors gracefully', () => {
        const circular: Record<string, unknown> = {};
        circular.self = circular;

        const errorSpy = vi.spyOn(console, 'error').mockImplementation(() => { });
        saveSettings(circular);

        expect(errorSpy).toHaveBeenCalled();
        expect(mockPostMessage).toHaveBeenCalledWith({
            type: 'saveSettings',
            payload: circular
        });
        errorSpy.mockRestore();
    });

    it('returns early when chrome.webview is undefined', () => {
        delete (window as unknown as { chrome?: unknown }).chrome;

        post('getSettings');
        const unsub = onMessage('saved', vi.fn());
        unsub();
    });

    it('logError and logWarn dispatch log messages via IPC and write to console', () => {
        const errorSpy = vi.spyOn(console, 'error').mockImplementation(() => { });
        const warnSpy = vi.spyOn(console, 'warn').mockImplementation(() => { });

        logError('Something failed', new Error('details'));
        expect(errorSpy).toHaveBeenCalled();
        expect(mockPostMessage).toHaveBeenCalledWith({
            type: 'log',
            payload: {
                level: 'error',
                message: 'Something failed',
                detail: expect.stringContaining('Error: details')
            }
        });

        logWarn('Warning occurred', 'simple string');
        expect(warnSpy).toHaveBeenCalled();
        expect(mockPostMessage).toHaveBeenCalledWith({
            type: 'log',
            payload: {
                level: 'warn',
                message: 'Warning occurred',
                detail: 'simple string'
            }
        });

        errorSpy.mockRestore();
        warnSpy.mockRestore();
    });
});

