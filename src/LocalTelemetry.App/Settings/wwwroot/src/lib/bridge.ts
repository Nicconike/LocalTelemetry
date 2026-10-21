import { pristineSettings } from './store.ts';
import type { BridgeEventType, BridgeMessageType } from "./types.ts";

function getWebView() {
    return (globalThis as typeof globalThis & { window?: Window }).window?.chrome?.webview ?? null;
}

function formatErrorDetail(error?: unknown): string | undefined {
    if (error instanceof Error) {
        return `${error.name}: ${error.message}\n${error.stack ?? ''}`;
    }
    if (error !== undefined && error !== null) {
        return typeof error === 'object' ? JSON.stringify(error) : String(error);
    }
    return undefined;
}

export function post(type: BridgeMessageType, payload: unknown = null): void {
    const wv = getWebView();
    if (!wv) return;
    const msg = payload === null ? { type } : { type, payload };
    wv.postMessage(msg);
}

export function onMessage(type: BridgeEventType, handler: (payload: unknown) => void): () => void {
    const wv = getWebView();
    if (!wv) return () => { };

    function listener(event: MessageEvent): void {
        try {
            const data = event.data;
            if (data?.type === type) handler(data.payload ?? data);
        } catch (err) {
            console.warn('Ignoring malformed bridge message', err);
        }
    }

    wv.addEventListener('message', listener);
    return () => wv.removeEventListener('message', listener);
}

export function requestSettings(): void { post('getSettings'); }

export function logError(message: string, error?: unknown): void {
    console.error(message, error);
    const detail = formatErrorDetail(error);
    post('log', { level: 'error', message, detail });
}

export function logWarn(message: string, detail?: unknown): void {
    console.warn(message, detail);
    post('log', { level: 'warn', message, detail: formatErrorDetail(detail) });
}

export function saveSettings(payload: unknown): void {
    if (payload) {
        try {
            pristineSettings.set(JSON.stringify(payload));
        } catch (err) {
            logError('Failed to serialize pristine settings payload', err);
        }
    }
    post('saveSettings', payload);
}

export function requestNics(): void { post('getNics'); }

export function requestTrafficMonths(): void { post('getTrafficMonths'); }

export function requestSystemInfo(): void { post('getSystemInfo'); }

export function requestTrafficHistoryAll(): void { post('getTrafficHistoryAll'); }

export function requestHistoryData(year: number, month: number): void { post('getHistoryData', { year, month }); }

export function requestHistoryMonths(): void { post('getHistoryMonths'); }

export function importDatContent(content: string): void { post('importDat', { content }); }
export function importWindowsUsage(startDate: string, endDate: string): void { post('importWindowsUsage', { startDate, endDate }); }

export function openFolder(target: 'app' | 'data'): void { post('openFolder', { target }); }
export function openUrl(url: string): void { post('openUrl', { url }); }
export function checkForUpdates(force = true): void { post('checkForUpdates', { force }); }
