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

/**
 * Formats a date string ("DD-MM-YYYY", "YYYY-MM-DD", or ISO timestamp) into "DD-MM-YYYY" format.
 */
export function formatDate(dateStr: string | null | undefined): string {
    if (!dateStr) return "-";
    const str = dateStr.trim();
    // DD-MM-YYYY
    const dmy = str.match(/^(\d{1,2})[-/](\d{1,2})[-/](\d{4})/);
    if (dmy) {
        const d = dmy[1].padStart(2, "0");
        const m = dmy[2].padStart(2, "0");
        const y = dmy[3];
        return `${d}-${m}-${y}`;
    }
    // YYYY-MM-DD
    const ymd = str.match(/^(\d{4})[-/](\d{1,2})[-/](\d{1,2})/);
    if (ymd) {
        const y = ymd[1];
        const m = ymd[2].padStart(2, "0");
        const d = ymd[3].padStart(2, "0");
        return `${d}-${m}-${y}`;
    }
    const parsed = new Date(str);
    if (!isNaN(parsed.getTime())) {
        const d = String(parsed.getDate()).padStart(2, "0");
        const m = String(parsed.getMonth() + 1).padStart(2, "0");
        const y = parsed.getFullYear();
        return `${d}-${m}-${y}`;
    }
    return str;
}
