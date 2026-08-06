# Telemetry Metrics & Hardware Sensors

LocalTelemetry polls hardware metrics using native vendor APIs, WMI and WDDM performance counters. This page details all supported metrics and their IDs.

> [!NOTE]
> Metric IDs are the canonical keys used internally. They appear in `LocalTelemetry.Core/Models/Metrics.cs`, in the settings JSON and when wiring metrics in the Layout page.

## 1. CPU Metrics

| Metric Name         | Metric ID   | Description                                                    |
| :------------------ | :---------- | :------------------------------------------------------------- |
| **CPU Usage**       | `cpu_pct`   | Total CPU load percentage across all physical & logical cores. |
| **CPU Temperature** | `cpu_temp`  | CPU package temperature in Celsius (°C).                       |
| **CPU Clock Speed** | `cpu_freq`  | Current effective CPU frequency in GHz.                        |
| **CPU Power**       | `cpu_power` | CPU package power draw in Watts (W) via RAPL.                  |

## 2. GPU Metrics

GPU metrics are read through the vendor's native library; system-wide utilisation is measured via WDDM GPU Engine counters:

- **NVIDIA**: NVML (`nvml.dll`) - ships with every NVIDIA display driver.
- **AMD**: ADL (`atiadlxx.dll`).
- **Intel**: Intel Graphics Control Library (`ControlLib.dll`).

| Metric Name         | Metric ID   | Description                              |
| :------------------ | :---------- | :--------------------------------------- |
| **GPU Usage**       | `gpu_pct`   | Graphics engine utilisation percentage.  |
| **GPU Temperature** | `gpu_temp`  | GPU core temperature in °C.              |
| **GPU VRAM Usage**  | `gpu_vram`  | Dedicated VRAM consumption in MB.        |
| **GPU Clock Speed** | `gpu_freq`  | GPU core clock frequency in MHz.         |
| **GPU Power**       | `gpu_power` | Total GPU board power draw in Watts (W). |

## 3. RAM (Memory) Metrics

| Metric Name       | Metric ID  | Description                                     |
| :---------------- | :--------- | :---------------------------------------------- |
| **RAM Usage %**   | `ram_pct`  | Percentage of physical memory currently used.   |
| **RAM Used (GB)** | `ram_used` | Total active RAM consumption in Gigabytes (GB). |

## 4. Storage (Disk) Metrics

Disks are registered per physical volume as `disk_diskN_read` / `disk_diskN_write` (e.g. `disk0_read`, `disk1_write`):

| Metric Name            | Metric ID          | Description                                    |
| :--------------------- | :----------------- | :--------------------------------------------- |
| **Disk N Read Speed**  | `disk_diskN_read`  | Active read throughput for that disk in MB/s.  |
| **Disk N Write Speed** | `disk_diskN_write` | Active write throughput for that disk in MB/s. |

## 5. Network Metrics

| Metric Name           | Metric ID   | Description                                               |
| :-------------------- | :---------- | :-------------------------------------------------------- |
| **Download Speed**    | `net_down`  | Live incoming network throughput (e.g. `12.5 MB/s`).      |
| **Upload Speed**      | `net_up`    | Live outgoing network throughput (e.g. `2.1 MB/s`).       |
| **Total Transferred** | `net_total` | Cumulative bytes transferred (down + up) since app start. |

## 6. Battery Metrics

Enabled via the **Monitoring** page (`Enable battery monitoring`). Values are read through WMI (`Win32_Battery`) and only populated on systems with a battery:

| Metric Name     | Metric ID      | Description                          |
| :-------------- | :------------- | :----------------------------------- |
| **Battery**     | `battery_pct`  | Remaining battery charge percentage. |
| **Charge Rate** | `battery_rate` | Charge/discharge rate in Watts (W).  |

## Polling Rate Configuration

The default polling interval is **1000 ms** (1 second).

You can adjust the polling interval on the **General** page (`Polling interval` dropdown):
- **0.5 s**: High responsiveness
- **1 s**: Default balance
- **2 s**: Reduced CPU wakeups
- **5 s**: Minimal CPU wakeups

The minimum accepted interval is 100 ms.
