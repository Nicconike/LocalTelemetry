# :radio: WebView2 & Named Pipe IPC Protocol Reference

This document specifies the inter-process communication contracts for LocalTelemetry:

1. **WebView2 Bridge**: Bidirectional JSON messaging between C# (`SettingsShell.xaml.cs`) and Svelte 5 (`bridge.ts`).
2. **Named Pipe IPC**: Asynchronous notification pipeline between `LocalTelemetry.exe` and `LocalTelemetry.Notifier.exe`.

---

## :bridge: WebView2 Bridge Architecture

All messages travel over Microsoft WebView2 using asynchronous JSON envelopes:

- **Inbound (JavaScript -> C#):** `window.chrome.webview.postMessage(jsonString)`
- **Outbound (C# -> JavaScript):** `CoreWebView2.PostWebMessageAsJson(jsonString)`

```typescript
interface WebMessageEnvelope<T = unknown> {
    type: string;
    payload?: T;
}
```

---

## :log-in: Inbound WebMessage Catalog (Frontend -> C# Host)

The C# controller handles 11 inbound message types in `SettingsShell.xaml.cs`.

### 1. `getSettings`

Requests active application settings.

- **Payload:** None.
- **Response:** Dispatches outbound `settings` message.

### 2. `saveSettings`

Submits updated configuration for audit diff logging and file persistence.

- **Payload:** `SettingsDto` object matching `settings.json`.
- **Response:** Dispatches outbound `saved` message with optional error string.

### 3. `getNics`

Requests active physical network adapters with default gateways.

- **Payload:** None.
- **Response:** Dispatches outbound `nics` message.

### 4. `getTrafficMonths`

Requests available calendar month strings from historical traffic storage.

- **Payload:** None.
- **Response:** Dispatches outbound `trafficMonths` message (`string[]` e.g. `["2026-10", "2026-09"]`).

### 5. `getSystemInfo`

Requests complete hardware discovery specifications and runtime diagnostic metadata.

- **Payload:** None.
- **Response:** Dispatches outbound `systemInfo` message.

### 6. `openUrl`

Opens an external URL in the default web browser. Only absolute `http://` or `https://` URLs are accepted.

- **Payload:**

```json
{
  "type": "openUrl",
  "payload": {
    "url": "https://github.com/Nicconike/LocalTelemetry/releases"
  }
}
```

### 7. `openFolder`

Opens a verified application directory in Windows File Explorer.

- **Payload:**

```json
{
  "type": "openFolder",
  "payload": {
    "target": "app" // "app" | "data" | "logs"
  }
}
```

| Target | Standard Mode Directory | Portable Mode Directory |
| :--- | :--- | :--- |
| `"app"` | `C:\Program Files\LocalTelemetry` | Application executable folder |
| `"data"` | `%LOCALAPPDATA%\LocalTelemetry` | Application executable folder |
| `"logs"` | `%LOCALAPPDATA%\LocalTelemetry\logs` | `logs\` folder in app directory |

### 8. `getTrafficHistoryAll`

Requests complete historical traffic records and live daily usage totals.

- **Payload:** None.
- **Response:** Dispatches outbound `trafficHistoryAll` message.

### 9. `importDat`

Imports plaintext external backup `.dat` bandwidth logs (`YYYY/MM/DD UpKB/DownloadKB`).

- **Payload:**

```json
{
  "type": "importDat",
  "payload": {
    "content": "raw_dat_records_string"
  }
}
```

- **Response:** Dispatches outbound `importDatResult` message.

### 10. `importWindowsUsage`

Extracts historical bandwidth records from the Windows System Resource Usage Monitor (`SRUDB.dat`) database via ESE engine interop (`WindowsNetworkUsageProvider.ImportFromEseAsync()`).

- **Payload:**

```json
{
  "type": "importWindowsUsage",
  "payload": {
    "startDate": "2026-01-01",
    "endDate": "2026-10-03"
  }
}
```

- **Response:** Dispatches outbound `windowsUsageImportResult` message.

### 11. `checkForUpdates`

Triggers an update check against the GitHub Releases API.

- **Payload:**

```json
{
  "type": "checkForUpdates",
  "payload": {
    "force": true // true = manual check; false = check interval timer
  }
}
```

- **Response:** Dispatches outbound `updateCheckResult` message.

---

## :log-out: Outbound WebMessage Catalog (C# Host -> Frontend)

The C# controller emits 10 outbound event types.

### 1. `settings`

Supplies the current application settings DTO to hydrate the Svelte store.

- **Type:** `"settings"`
- **Payload:** `SettingsDto`

```typescript
interface OverlayDto {
    visible: boolean;
    doubleClickAction: 'settings' | 'taskmanager' | 'none';
    position: 'left' | 'right' | 'beside_apps';
    taskbarAlignment?: 'left' | 'center'; // Dynamically detected Windows 11 alignment
    offsetX: number;
    opacity: number;
    scale: number;
    fontSize: number;
    fontBold: boolean;
    labelColor: string;
    metricColors: Record<string, string>;
    groupColors: Record<string, string>;
    followWindowsTheme: boolean;
    row1: string[];
}

interface UpdateConfigDto {
    checkFrequency: 'manual' | 'weekly' | 'monthly';
    includePrereleases: boolean; // default: false
    lastCheckUtc: string;
}
```

### 2. `saved`

Confirms persistence status of settings modifications.

- **Type:** `"saved"`
- **Payload:** `{ error?: string }`

### 3. `nics`

Supplies active physical network adapters.

- **Type:** `"nics"`
- **Payload:** `string[]` (e.g. `["Ethernet (Intel(R) Ethernet Controller I225-V)"]`)

### 4. `systemInfo`

Supplies comprehensive hardware discovery specifications.

- **Type:** `"systemInfo"`
- **Payload Schema:**

```json
{
  "version": "1.0.0-rc.1",
  "buildDate": "03-10-2026, 04:30 PM",
  "deploymentMode": "Standard",
  "targetRuntime": ".NET 10.0.0",
  "deviceName": "DESKTOP-RIG",
  "os": "Windows 11 Pro 23H2 (Build 22631.4169)",
  "cpu": "Intel(R) Core(TM) i9-14900K",
  "cpuVendor": "Intel",
  "cpuCores": 24,
  "cpuThreads": 32,
  "cpuBaseSpeedMhz": 3200,
  "cpuMaxSpeedMhz": 6000,
  "cpuSocket": "Socket LGA1700",
  "cpuTdpWatts": 125,
  "gpus": [
    {
      "name": "NVIDIA GeForce RTX 4090",
      "vendor": "NVIDIA",
      "dedicated": true,
      "vramGb": "24.0",
      "driver": "560.94",
      "tdpW": "450 W"
    }
  ],
  "installedRamGb": 64.0,
  "ramGb": "63.8",
  "ramMfr": "Corsair",
  "ramSpeed": "6000 MT/s",
  "ramSlots": 4,
  "ramModules": ["Module 1: 32 GB 6000 MT/s", "Module 2: 32 GB 6000 MT/s"],
  "disks": [
    {
      "model": "Samsung SSD 990 PRO 2TB",
      "vendor": "Samsung",
      "busType": "NVMe",
      "mediaType": "SSD",
      "displayType": "NVMe",
      "formattedSize": "1.86 TB",
      "sizeGb": "1907.7",
      "diskIndex": 0,
      "boot": true
    }
  ],
  "motherboardMfr": "ASUSTeK COMPUTER INC.",
  "motherboardModel": "ROG MAXIMUS Z790 HERO",
  "motherboardVersion": "Rev 1.xx",
  "bios": "2402",
  "biosUefi": true,
  "systemType": "Desktop",
  "psu": "Corsair RM1000x",
  "psuCapacity": "1000 W",
  "exeDir": "C:\\Program Files\\LocalTelemetry",
  "dataDir": "C:\\Users\\User\\AppData\\Local\\LocalTelemetry",
  "logsDir": "C:\\Users\\User\\AppData\\Local\\LocalTelemetry\\logs",
  "isStandardMode": true
}
```

### 5. `trafficHistoryAll`

Full database of historical daily records and today's total transferred bandwidth.

- **Type:** `"trafficHistoryAll"`
- **Payload Schema:**

```json
{
  "records": [
    {
      "date": "03-10-2026",
      "downBytes": 4294967296,
      "upBytes": 1073741824,
      "interfaceName": "Ethernet",
      "source": "Session"
    }
  ],
  "todayDown": 4294967296,
  "todayUp": 1073741824
}
```

### 6. `trafficToday`

Dispatched every 5 seconds with live bandwidth increments.

- **Type:** `"trafficToday"`
- **Payload:** `{ "downBytes": 4294967296, "upBytes": 1073741824 }`

### 7. `trafficMonths`

- **Type:** `"trafficMonths"`
- **Payload:** `string[]` (e.g. `["2026-10", "2026-09", "2026-08"]`)

### 8. `importDatResult`

- **Type:** `"importDatResult"`
- **Payload:** `{ "daysImported": 142, "error": null }`

### 9. `windowsUsageImportResult`

Dispatched upon completion of historical Windows SRUM usage extraction.

- **Type:** `"windowsUsageImportResult"`
- **Payload Schema:**

```json
{
  "success": true,
  "daysImported": 180,
  "downBytes": 536870912000,
  "upBytes": 107374182400,
  "message": ""
}
```

### 10. `updateCheckResult`

Dispatched upon completion of an update check.

- **Type:** `"updateCheckResult"`
- **Payload Schema:**

```json
{
  "isUpdateAvailable": true,
  "isPrerelease": false,
  "currentVersion": "1.0.0-rc.1",
  "latestVersion": "1.0.0",
  "releaseUrl": "https://github.com/Nicconike/LocalTelemetry/releases/tag/v1.0.0",
  "releaseNotes": "Official release notes markdown...",
  "publishedAt": "2026-10-03T12:00:00Z",
  "errorMessage": null
}
```

---

## :bell: Standalone Notifier Named Pipe Protocol

The alert engine dispatches toast alert packets to `LocalTelemetry.Notifier.exe` across a local Named Pipe.

### Pipe Endpoint Specification

- **Pipe Name:** `LocalTelemetryNotifier` (path: `\\.\pipe\LocalTelemetryNotifier`)
- **Host / Server:** `LocalTelemetry.Notifier.exe` (`NamedPipeServerStream`)
- **Client:** `LocalTelemetry.App.Services.NotificationClient` (`NamedPipeClientStream`)
- **Connection Configuration:** Timeout of 5000 ms, maximum 3 retries, synchronized via `SemaphoreSlim(1, 1)`
- **Transmission Mode:** UTF-8 JSON Lines terminated with a newline (`\n`)

### IPC Message Contract (`NotificationMessage`)

```csharp
private sealed record NotificationMessage
{
    public string Action { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? Body { get; set; }
    public string? Tag { get; set; }
}
```

### 1. Show Toast Alert Command

Dispatched when a sensor threshold is exceeded and notifications are enabled:

```json
{
  "Action": "ShowToast",
  "Title": "CPU Temperature Critical",
  "Body": "CPU reached 92°C (Threshold: 90°C)",
  "Tag": "cpu_temp"
}
```

### 2. Shutdown Command

Dispatched during application shutdown or when toast notifications are disabled:

```json
{
  "Action": "Shutdown"
}
```

### Window Styling Guard

The toast window in `ToastForm.cs` sets Win32 extended window styles:

- `WS_EX_TOPMOST` (`0x00000008`): Ensures toast visibility above full-screen windows.
- `WS_EX_TOOLWINDOW` (`0x00000080`): Prevents taskbar buttons and Alt+Tab inclusion.
- `WS_EX_NOACTIVATE` (`0x08000000`): Prevents keyboard focus transfer or full-screen game minimization.
