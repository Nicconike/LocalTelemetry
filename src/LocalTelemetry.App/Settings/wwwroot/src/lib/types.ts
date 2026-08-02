// ── DTO types mirroring C# SettingsDto.cs
export interface SettingsDto {
    runAtStartup: boolean;
    startMinimized: boolean;
    minimizeToTray: boolean;
    enableFileLogging: boolean;
    monitoring: MonitoringDto;
    overlay: OverlayDto;
    alerts: AlertsDto;
    netUsage: NetUsageDto;
    windowTheme: string;
}

export interface MonitoringDto {
    intervalMs: number;
    useFahrenheit: boolean;
    useNetBits: boolean;
    preferredNic: string;
    hasBattery: boolean;
    trackCpu: boolean;
    trackGpu: boolean;
    trackRam: boolean;
    trackNet: boolean;
    trackDisk: boolean;
    trackBattery: boolean;
    gpuUsageSource: string;
    logCpuMode: number;
    logGpuMode: number;
    logRamMode: number;
    logNetMode: number;
    logDiskMode: number;
    logBatteryMode: number;
}

export interface OverlayDto {
    visible: boolean;
    doubleClickAction: string;
    position: string;
    offsetX: number;
    opacity: number;
    scale: number;
    bgColor: string;
    textColor: string;
    fontSizePx: number;
    fontBold: boolean;
    labelColor: string;
    metricColors: Record<string, string>;
    defaultMetricColors: Record<string, string>;
    followWindowsTheme: boolean;
    row1: string[];
    userCustomizedMetricColors: string[];
}

export interface AlertsDto {
    enabled: boolean;
    alertCpuTemp: boolean;
    alertGpuTemp: boolean;
    alertCpuUsage: boolean;
    alertRamUsage: boolean;
    alertGpuUsage: boolean;
    alertBatteryLow: boolean;
    alertCpuFreq: boolean;
    alertCpuPower: boolean;
    alertGpuFreq: boolean;
    alertGpuPower: boolean;
    cpuUsageMaxPct: number;
    ramUsageMaxPct: number;
    gpuUsageMaxPct: number;
    cpuTempMaxC: number;
    gpuTempMaxC: number;
    batteryLowPct: number;
    cpuFreqMinMhz: number;
    cpuPowerMaxW: number;
    gpuFreqMinMhz: number;
    gpuPowerMaxW: number;
    showToastNotif: boolean;
    flashOverlay: boolean;
    cooldownSecs: number;
    fireOncePerSession: boolean;
}

export interface NetUsageDto {
    enabled: boolean;
}

// ── SystemInfo ────────────────────────────────────────────
export interface GpuInfo {
    name: string;
    vendor: string;
    dedicated: boolean;
    vramGb?: string | null;
    driver?: string | null;
    tdpW?: string | null;
}

export interface DiskInfo {
    model: string;
    vendor: string;
    busType: string;
    sizeGb?: string | null;
    diskIndex?: number | null;
    boot: boolean;
}

export interface SystemInfo {
    version: string;
    buildDate: string;
    deviceName: string;
    os: string;
    cpu: string;
    cpuVendor: string;
    cpuCores: number;
    cpuThreads: number;
    cpuBaseSpeedMhz: number;
    cpuMaxSpeedMhz: number;
    cpuSocket: string;
    cpuTdpWatts?: number | null;
    gpus: GpuInfo[];
    installedRamGb: number;
    ramGb?: string | null;
    ramMfr?: string | null;
    ramSpeed?: string | null;
    ramSlots: number;
    ramModules?: unknown[];
    disk: string;
    disks: DiskInfo[];
    motherboardMfr: string;
    motherboardModel: string;
    motherboardVersion: string;
    motherboardSerial: string;
    bios: string;
    biosUefi: boolean;
    systemModel: string;
    ramType: string;
    nics: string[];
    systemType: string;
    batteryManufacturer?: string;
    batteryDeviceName?: string;
    batteryDesignCapacity?: string;
    batteryFullChargedCapacity?: string;
    psu?: string;
    psuCapacity?: string;
}

// ── Traffic history ───────────────────────────────────────
export interface TrafficDay {
    date: string;
    downBytes: number;
    upBytes: number;
    interfaceName?: string;
    source?: string;
}

export interface TrafficHistoryAllPayload {
    records: TrafficDay[];
    todayDown: number;
    todayUp: number;
}

export interface TrafficTodayPayload {
    downBytes: number;
    upBytes: number;
}

export interface DatImportResult {
    daysImported: number;
    error?: string;
}

// ── Theme types ───────────────────────────────────────────
export interface Theme {
    value: string;
    label: string;
    vars: Record<string, string>;
}

// ── Bridge envelope types ─────────────────────────────────
export type BridgeMessageType =
    | "getSettings"
    | "saveSettings"
    | "getNics"
    | "getTrafficMonths"
    | "getSystemInfo"
    | "getTrafficHistoryAll"
    | "getHistoryData"
    | "getHistoryMonths"
    | "importDat"
    | "openUrl";

export type BridgeEventType =
    | "settings"
    | "saved"
    | "nics"
    | "systemInfo"
    | "trafficHistoryAll"
    | "trafficHistory"
    | "trafficMonths"
    | "trafficToday"
    | "importDatResult";

export interface BridgeMessage<T = unknown> {
    type: BridgeMessageType;
    payload?: T;
}

export interface BridgeEvent<T = unknown> {
    type: BridgeEventType;
    payload?: T;
    error?: string;
}
