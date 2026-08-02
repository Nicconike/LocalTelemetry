export function debounce<T extends (...args: unknown[]) => void>(fn: T, ms: number = 400): (...args: Parameters<T>) => void {
    let timer: ReturnType<typeof setTimeout> | undefined;
    return (...args: Parameters<T>) => {
        clearTimeout(timer);
        timer = setTimeout(() => fn(...args), ms);
    };
}

export function clamp(val: number, min: number, max: number): number {
    return Math.min(Math.max(val, min), max);
}

export function formatBytes(bytes: number): string {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1048576) return `${(bytes / 1024).toFixed(1)} KB`;
    if (bytes < 1073741824) return `${(bytes / 1048576).toFixed(1)} MB`;
    return `${(bytes / 1073741824).toFixed(2)} GB`;
}

export function formatTrafficBytes(bytes: number): string {
    if (bytes >= 1_000_000_000_000) return `${(bytes / 1_000_000_000_000).toFixed(2)}TB`;
    if (bytes >= 1_000_000_000) return `${(bytes / 1_000_000_000).toFixed(2)}GB`;
    if (bytes >= 1_000_000) return `${(bytes / 1_000_000).toFixed(2)}MB`;
    if (bytes >= 1_000) return `${(bytes / 1_000).toFixed(2)}KB`;
    return `${bytes}B`;
}

export function deepClone<T>(obj: T): T {
    return structuredClone(obj);
}
