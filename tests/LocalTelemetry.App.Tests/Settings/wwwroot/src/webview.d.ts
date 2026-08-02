interface WebView {
    postMessage(message: unknown): void;
    addEventListener(type: string, listener: (event: MessageEvent) => void): void;
    removeEventListener(type: string, listener: (event: MessageEvent) => void): void;
}

interface Chrome {
    webview: WebView;
}

interface Window {
    chrome?: Chrome;
}
