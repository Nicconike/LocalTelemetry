import type { BridgeEventType, BridgeMessageType } from "./types.ts";

function getWebView() {
    return (globalThis as typeof globalThis & { window?: Window }).window?.chrome?.webview ?? null;
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
            console.warn('[LT] bridge.js: ignoring malformed message', err);
        }
    }

    wv.addEventListener('message', listener);
    return () => wv.removeEventListener('message', listener);
}

export function requestSettings(): void { post('getSettings'); }

import { pristineSettings } from './store.ts';

export function saveSettings(payload: unknown): void {
    if (payload) {
        try {
            pristineSettings.set(JSON.stringify(payload));
        } catch { }
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

export function openUrl(url: string): void { post('openUrl', { url }); }
