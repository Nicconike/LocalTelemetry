# :file-cog: Configuration Settings Schema Reference

This document specifies the complete configuration schema for LocalTelemetry's settings file (`settings.json`).

---

## :folder: Storage Locations & File Modes

- **Standard Mode (Installed):** `%LOCALAPPDATA%\LocalTelemetry\settings.json`
- **Portable Mode (Zero-Install):** `<AppDirectory>\settings.json`

The mode is determined at launch by checking for the `app.mode` marker file in the application directory.

---

## :file-code: Complete Default Configuration Example

```json
{
  "runAtStartup": false,
  "startMinimized": false,
  "minimizeToTrayOnClose": false,
  "showSystemTrayIcon": true,
  "enableFileLogging": true,
  "systemLogLevel": "Info",
  "windowTheme": "default",
  "monitoring": {
    "pollIntervalMs": 1000,
    "enableCpu": true,
    "enableGpu": true,
    "enableRam": true,
    "enableNet": true,
    "enableDisk": true,
    "enableBattery": true,
    "cpuFreqMode": "average",
    "cpuUsageMode": "overall",
    "useFahrenheit": false,
    "useNetBits": false,
    "preferredNic": "auto",
    "gpuUsageSource": "driver",
    "logCpuMode": 1,
    "logGpuMode": 1,
    "logRamMode": 1,
    "logNetMode": 1,
    "logDiskMode": 1,
    "logBatteryMode": 1
  },
  "overlay": {
    "visible": false,
    "placement": "left",
    "placementOffset": 0,
    "floatX": -1,
    "floatY": -1,
    "doubleClickAction": "settings",
    "opacity": 100,
    "scalePct": 100,
    "fontSizePx": 14.0,
    "fontBold": false,
    "labelColor": "#FFFFFF",
    "valueColor": "#FFFFFF",
    "bgColor": "#1c1b19",
    "metricColors": {
      "cpu_pct": "#00E5FF",
      "cpu_temp": "#00E5FF",
      "cpu_freq": "#00E5FF",
      "cpu_power": "#00E5FF",
      "ram_pct": "#A78BFA",
      "ram_used": "#A78BFA",
      "gpu_pct": "#88CCFF",
      "gpu_temp": "#88CCFF",
      "gpu_vram": "#88CCFF",
      "gpu_freq": "#88CCFF",
      "gpu_power": "#88CCFF",
      "net_down": "#38BDF8",
      "net_up": "#4ADE80",
      "net_total": "#FBBF24",
      "battery_pct": "#80E080",
      "battery_rate": "#80E080"
    },
    "groupColors": {},
    "followWindowsTheme": true,
    "userCustomizedMetricColors": [],
    "userCustomizedGroupColors": [],
    "row1": [
      "cpu_pct",
      "cpu_temp",
      "gpu_pct",
      "gpu_temp",
      "gpu_vram",
      "net_total",
      "ram_pct",
      "ram_used",
      "net_down",
      "net_up"
    ]
  },
  "alerts": {
    "enabled": false,
    "alertCpuTemp": false,
    "alertGpuTemp": false,
    "alertCpuUsage": false,
    "alertRamUsage": false,
    "alertGpuUsage": false,
    "alertGpuVram": false,
    "alertBatteryLow": false,
    "alertCpuFreq": false,
    "alertCpuPower": false,
    "alertGpuFreq": false,
    "alertGpuPower": false,
    "alertNetDaily": false,
    "alertNetMonthly": false,
    "cpuTempMaxC": 90.0,
    "gpuTempMaxC": 85.0,
    "cpuUsageMaxPct": 95.0,
    "ramUsageMaxPct": 90.0,
    "gpuUsageMaxPct": 95.0,
    "gpuVramMaxMb": 6000.0,
    "batteryLowPct": 20.0,
    "cpuFreqMinMhz": 800.0,
    "cpuPowerMaxW": 65.0,
    "gpuFreqMinMhz": 300.0,
    "gpuPowerMaxW": 150.0,
    "netDailyMaxGb": 50.0,
    "netMonthlyMaxGb": 1000.0,
    "showToastNotif": true,
    "flashOverlay": true,
    "cooldownSecs": 60,
    "fireOncePerSession": false
  },
  "netUsage": {
    "enabled": true
  },
  "updates": {
    "checkFrequency": "manual",
    "includePrereleases": true,
    "lastCheckUtc": "0001-01-01T00:00:00Z"
  }
}
```

---

## :list-checks: Configuration Key Specifications

### Root Application Properties

| Property | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| `runAtStartup` | `boolean` | `false` | Registers the elevated `LocalTelemetry Startup` scheduled task. |
| `startMinimized` | `boolean` | `false` | Launches straight into the taskbar overlay and tray without opening settings. |
| `minimizeToTrayOnClose` | `boolean` | `false` | Minimizes settings to the system tray when closing the window. |
| `showSystemTrayIcon` | `boolean` | `true` | Controls system tray notification icon visibility. |
| `enableFileLogging` | `boolean` | `true` | Enables daily metric file logging (`logs/lt_DD-MM-YYYY.log`). System diagnostics remain enabled always. |
| `systemLogLevel` | `string` | `"Info"` | Threshold severity for system diagnostics (`"Off"`, `"Error"`, `"Warning"`, `"Info"`, `"Debug"`). |
| `windowTheme` | `string` | `"default"` | Sets settings UI window theme (`"default"`, `"dark"`, `"light"`). |

---

### `monitoring` (Hardware Polling Configuration)

| Property | Type | Valid Range / Values | Default | Description |
| :--- | :--- | :--- | :--- | :--- |
| `pollIntervalMs` | `integer` | `100` to `10000` | `1000` | Polling loop refresh timer in milliseconds. |
| `enableCpu` | `boolean` | `true`, `false` | `true` | Enables CPU telemetry polling. |
| `enableGpu` | `boolean` | `true`, `false` | `true` | Enables GPU telemetry polling. |
| `enableRam` | `boolean` | `true`, `false` | `true` | Enables RAM telemetry polling. |
| `enableNet` | `boolean` | `true`, `false` | `true` | Enables Network throughput polling. |
| `enableDisk` | `boolean` | `true`, `false` | `true` | Enables Storage disk throughput polling. |
| `enableBattery` | `boolean` | `true`, `false` | `true` | Enables Laptop battery telemetry polling. |
| `cpuFreqMode` | `string` | `"average"`, `"max"` | `"average"` | Effective frequency across all cores vs peak boosted core. |
| `cpuUsageMode` | `string` | `"overall"`, `"max"` | `"overall"` | System-wide utilization vs highest single thread load. |
| `useFahrenheit` | `boolean` | `true`, `false` | `false` | Displays temperatures in Fahrenheit instead of Celsius. |
| `useNetBits` | `boolean` | `true`, `false` | `false` | Displays network rates in bits/s instead of bytes/s. |
| `preferredNic` | `string` | `"auto"` or NIC name | `"auto"` | Selected network adapter for telemetry. |
| `gpuUsageSource` | `string` | `"driver"`, `"wddm"` | `"driver"` | NVML/ADL driver engine time vs Windows WDDM statistics. |
| `logCpuMode` | `integer` | `0` (Off), `1` (Errors), `2` (Full) | `1` | CPU sensor logging verbosity. |
| `logGpuMode` | `integer` | `0` (Off), `1` (Errors), `2` (Full) | `1` | GPU sensor logging verbosity. |
| `logRamMode` | `integer` | `0` (Off), `1` (Errors), `2` (Full) | `1` | RAM sensor logging verbosity. |
| `logNetMode` | `integer` | `0` (Off), `1` (Errors), `2` (Full) | `1` | Network sensor logging verbosity. |
| `logDiskMode` | `integer` | `0` (Off), `1` (Errors), `2` (Full) | `1` | Storage disk sensor logging verbosity. |
| `logBatteryMode` | `integer` | `0` (Off), `1` (Errors), `2` (Full) | `1` | Battery sensor logging verbosity. |

---

### `overlay` (Taskbar Overlay Appearance & Geometry)

| Property | Type | Valid Range / Values | Default | Description |
| :--- | :--- | :--- | :--- | :--- |
| `visible` | `boolean` | `true`, `false` | `false` | Controls taskbar overlay visibility. |
| `placement` | `string` | `"left"`, `"center"`, `"right"` | `"left"` | Taskbar anchor placement. |
| `placementOffset` | `integer` | `-500` to `500` | `0` | Horizontal pixel offset from anchor. |
| `floatX` | `integer` | `-1` (Auto) or coordinate | `-1` | Saved drag position X coordinate for floating mode. |
| `floatY` | `integer` | `-1` (Auto) or coordinate | `-1` | Saved drag position Y coordinate for floating mode. |
| `doubleClickAction` | `string` | `"none"`, `"taskmanager"`, `"settings"` | `"settings"` | Overlay double-click trigger action. |
| `opacity` | `integer` | `10` to `100` | `100` | Layered window opacity percentage. |
| `scalePct` | `integer` | `50` to `200` | `100` | Geometry scaling percentage. |
| `fontSizePx` | `float` | `8.0` to `24.0` | `14.0` | Overlay typography size in pixels. |
| `fontBold` | `boolean` | `true`, `false` | `false` | Bold font weighting toggle. |
| `labelColor` | `string` | Hex string e.g. `"#FFFFFF"` | `"#FFFFFF"` | Base label text color. |
| `valueColor` | `string` | Hex string e.g. `"#FFFFFF"` | `"#FFFFFF"` | Default value text color. |
| `bgColor` | `string` | Hex string e.g. `"#1c1b19"` | `"#1c1b19"` | Layered background transparency color key. |
| `metricColors` | `object` | Key-value hex mapping | Brand defaults | Individual color overrides for each metric. |
| `groupColors` | `object` | Key-value hex mapping | Empty `{}` | Group color overrides for entire component categories. |
| `followWindowsTheme` | `boolean` | `true`, `false` | `true` | Dynamically adapts color key and luminosity to Windows theme. |
| `userCustomizedMetricColors` | `array` | Metric ID strings | `[]` | Metric IDs manually recolored by the user; preserved during vendor color updates. |
| `userCustomizedGroupColors` | `array` | Group key strings | `[]` | Group keys manually recolored by the user. |
| `row1` | `array` | Metric ID strings | 10 default IDs | Column metric layout (even indices = top, odd indices = bottom). |

---

### `alerts` (Threshold Warnings & Actions)

| Property | Type | Valid Range | Default | Description |
| :--- | :--- | :--- | :--- | :--- |
| `enabled` | `boolean` | `true`, `false` | `false` | Master toggle for all threshold alerts. |
| `alertCpuTemp` | `boolean` | `true`, `false` | `false` | High CPU temperature alert toggle. |
| `cpuTempMaxC` | `float` | `40.0` to `115.0` | `90.0` | CPU temperature threshold in °C. |
| `alertCpuFreq` | `boolean` | `true`, `false` | `false` | Low CPU frequency (throttling) alert toggle. |
| `cpuFreqMinMhz` | `float` | `200.0` to `5000.0` | `800.0` | CPU frequency low threshold in MHz. |
| `alertCpuPower` | `boolean` | `true`, `false` | `false` | High CPU package power alert toggle. |
| `cpuPowerMaxW` | `float` | `10.0` to `500.0` | `65.0` | CPU package power threshold in Watts. |
| `alertGpuTemp` | `boolean` | `true`, `false` | `false` | High GPU temperature alert toggle. |
| `gpuTempMaxC` | `float` | `40.0` to `110.0` | `85.0` | GPU temperature threshold in °C. |
| `alertGpuFreq` | `boolean` | `true`, `false` | `false` | Low GPU frequency (throttling) alert toggle. |
| `gpuFreqMinMhz` | `float` | `100.0` to `3500.0` | `300.0` | GPU frequency low threshold in MHz. |
| `alertGpuPower` | `boolean` | `true`, `false` | `false` | High GPU power alert toggle. |
| `gpuPowerMaxW` | `float` | `20.0` to `700.0` | `150.0` | GPU power threshold in Watts. |
| `alertNetDaily` | `boolean` | `true`, `false` | `false` | Daily network bandwidth quota alert toggle. |
| `netDailyMaxGb` | `float` | `1.0` to `1000.0` | `50.0` | Daily data transfer limit in GB. |
| `alertNetMonthly` | `boolean` | `true`, `false` | `false` | Monthly network bandwidth quota alert toggle. |
| `netMonthlyMaxGb` | `float` | `10.0` to `10000.0` | `1000.0` | Monthly data transfer limit in GB. |
| `showToastNotif` | `boolean` | `true`, `false` | `true` | Shows standalone desktop toast popups on alerts. |
| `flashOverlay` | `boolean` | `true`, `false` | `true` | Flashes the taskbar overlay visually on alerts. |
| `cooldownSecs` | `integer` | `5` to `3600` | `60` | Minimum seconds between repeat notifications. |
| `fireOncePerSession` | `boolean` | `true`, `false` | `false` | Alerts trigger only once per application run when true. |

---

### `updates` (GitHub Releases Client Configuration)

| Property | Type | Valid Values | Default | Description |
| :--- | :--- | :--- | :--- | :--- |
| `checkFrequency` | `string` | `"manual"`, `"weekly"`, `"monthly"` | `"manual"` | Interval for automated background update checks. |
| `includePrereleases` | `boolean` | `true`, `false` | `true` | Includes Release Candidate (`-rc`) and beta builds. |
| `lastCheckUtc` | `string` (ISO 8601) | Datetime string | MinValue | Timestamp of the last completed update check. |

---

## :shield-check: Schema Migration & Corruption Safeguards

1. **Schema Migration (`MigrateConfig`)**:
   When new versions add configuration keys, missing keys are dynamically populated with hardware-detected brand defaults and saved back to disk.
2. **Corrupted Configuration Fallback**:
   If `settings.json` fails JSON deserialization:
   - The file is backed up immediately to `settings.json.corrupt`.
   - An error is logged to `logs/lt_system.log`.
   - A fresh default configuration is generated and saved automatically.
