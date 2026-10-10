# :thermometer-sun: Telemetry Metrics & Hardware Sensors

LocalTelemetry continuously monitors your computer's hardware vitals and displays them directly inside your Windows taskbar. Every metric can be reordered, enabled or disabled, and styled with custom colors in the Settings panel.

This page explains what each metric measures, how it is labeled on the taskbar overlay, and the settings available to customize its display.

---

## :cpu: CPU Metrics

Processor vitals monitor your CPU's workload, package temperature, operating clock speeds, and real-time power draw.

| Metric (Settings Name) | Overlay Label | Typical Display | What It Measures |
| :--------------------- | :------------ | :-------------- | :----------------------------------------------------------------- |
| **CPU Usage**          | `CPU`         | `42%`           | Overall processor utilization percentage across all active threads |
| **CPU Temp**           | `CPU`         | `65°C` / `149°F`| Current CPU package temperature                                    |
| **CPU Frequency**      | `CPU`         | `4.35 GHz`      | Operating clock speed (effective frequency)                        |
| **CPU Package Power**  | `CPU`         | `65 W`          | Real-time CPU power consumption in Watts                           |

### :list-clock: CPU Options (Settings -> Monitoring)

- **Clock Mode**:
  - `Average`: Displays the mean clock speed across all active processor cores.
  - `Max Active Core`: Displays the peak frequency of the highest-clocked core (useful for observing single-core turbo boosts).
- **Usage Source**:
  - `Overall (Task Manager)`: Global average CPU utilization across all threads, matching Windows Task Manager.
  - `Max Thread (Gaming)`: Utilization of the single most loaded core or thread, useful for identifying single-threaded CPU bottlenecks in games.

---

## :gpu: GPU Metrics

Graphics card telemetry tracks GPU core utilization, video memory consumption, core clocks, temperatures, and board power.

| Metric (Settings Name) | Overlay Label | Typical Display | What It Measures |
| :--------------------- | :------------ | :-------------- | :---------------------------------------------  |
| **GPU Usage**          | `GPU`         | `78%`           | Graphics processing unit utilization percentage |
| **GPU Temp**           | `GPU`         | `68°C` / `154°F`| GPU core temperature                            |
| **GPU VRAM Usage**     | `GPU`         | `4120 MB`       | Dedicated video memory (VRAM) currently in use  |
| **GPU Frequency**      | `GPU`         | `2150 MHz`      | Current GPU graphics core clock speed           |
| **GPU Power**          | `GPU`         | `120 W`         | Total graphics card power draw in Watts         |

### :square-activity: GPU Options (Settings -> Monitoring)

- **GPU Usage Source**:
  - `Driver (NVML/ADL)`: Directly queries vendor driver APIs for kernel engine busy time (matches tools like MSI Afterburner and GeForce Experience).
  - `WDDM (Task Manager)`: Queries the Windows graphics engine scheduler, matching Windows Task Manager.

---

## :memory-stick: RAM (Memory) Metrics

Memory telemetry tracks active system memory consumption and overall capacity utilization.

| Metric (Settings Name) | Overlay Label | Typical Display | What It Measures |
| :--------------------- | :------------ | :-------------- | :---------------------------------------------------- |
| **RAM Usage**          | `RAM`         | `58%`           | Percentage of total physical memory currently in use  |
| **RAM Used**           | `RAM`         | `18.5 GB`       | Active physical memory in Gigabytes                   |

---

## :hard-drive: Storage (Disk) Metrics

LocalTelemetry automatically discovers your installed physical storage drives (NVMe SSDs, SATA SSDs, and mechanical HDDs) and tracks active read and write throughput.

| Metric (Settings Name) | Overlay Label | Typical Display | What It Measures |
| :--------------------- | :------------ | :-------------- | :---------------------------------------------------------------- |
| **[Drive] Read** | `NVMeR` / `SATAR` / `HDDR` | `250 KB/s` / `1.2 GB/s` | Active read data throughput for the detected physical disk |
| **[Drive] Write** | `NVMeW` / `SATAW` / `HDDW` | `14.8 MB/s` / `450 KB/s` | Active write data throughput for the detected physical disk |

### :tag: How Disk Labels Work on the Taskbar

The overlay creates concise, readable labels based on your storage drive's bus type and position:

- **Single Drive of a Type**: If your PC has one NVMe SSD, the label displays simply as `NVMeR` (Read) and `NVMeW` (Write) without unnecessary numbering.
- **Multiple Drives of the Same Type**: If you have two NVMe drives or multiple SATA drives, sequential numbers are added to distinguish them (e.g. `NVMe1R`, `NVMe2R`, `SATA1W`, `SATA2W`).
- **Auto-Scaling Speed Units**: Throughput automatically scales to the cleanest unit: `KB/s` for light background I/O, `MB/s` for active transfers, and `GB/s` for high-speed sequential transfers.

---

## :ethernet-port: Network Metrics

Network telemetry monitors real-time transfer rates across your primary network adapter alongside session totals.

| Metric (Settings Name) | Overlay Label | Typical Display | What It Measures |
| :--------------------- | :------------ | :-------------- | :----------------------------------------------------------------------------------- |
| **Download**           | `DOWN↓`       | `1.2 MB/s` (or `9.6 Mb/s`) | Current incoming network throughput (displays `No Internet` when disconnected) |
| **Upload**             | `UP↑`         | `350 KB/s` (or `2.8 Mb/s`) | Current outgoing network throughput (displays `No Internet` when disconnected) |
| **Total Transferred**  | `Total`       | `14.25 GB`      | Cumulative data transferred (download + upload) during the current session (scales from `B` up to `TB`) |

### :network: Network Options (Settings -> Network)

- **Show Speed in Bits/s**: Toggle between bytes per second (`B/s`, `KB/s`, `MB/s`, `GB/s`) and bits per second (`b/s`, `Kb/s`, `Mb/s`, `Gb/s`) to match internet speed test conventions.
- **Adapter Selection**: Choose `Auto (Best Match)` to let LocalTelemetry track whichever adapter is currently active, or pin monitoring to a specific Ethernet or Wi-Fi interface.

---

## :battery-charging: Laptop Battery Metrics

On laptops, handheld PCs, and battery-powered Windows devices, LocalTelemetry provides real-time power analytics.

| Metric (Settings Name) | Overlay Label | Typical Display | What It Measures |
| :--------------------- | :------------ | :-------------- | :----------------------------------------------------------------------------------- |
| **Battery**            | `BAT`         | `85%`           | Remaining battery charge percentage |
| **Charge Rate**        | `BAT`         | `+45.0 W` / `Full` / `-18.5 W` | Real-time battery charging power or discharging rate in Watts |

### :car-battery: Battery Display States

- **Charging on AC Power**: Displays incoming charge wattage prefixed with a plus sign (e.g. `+45.2 W`).
- **Fully Charged on AC Power**: Displays `Full` once the battery reaches 100% and charging completes.
- **Running on Battery**: Displays real-time power consumption prefixed with a minus sign (e.g. `-18.5 W`) so you can monitor your laptop's current energy draw.
- **Desktop Systems**: Battery metrics are automatically hidden when no physical battery is present.

---

## :file-cog: Display & Formatting Settings

You can customize how metrics appear and behave across the settings pages:

- **Polling Interval** (**Settings -> General**): Select how frequently sensors are refreshed:
  - `0.5 s` (500 ms): Fast response for intensive benchmarking or gaming.
  - `1 s` (1000 ms): Default balanced refresh rate.
  - `2 s` (2000 ms): Lower background polling activity.
  - `5 s` (5000 ms): Ultra-low CPU wakeups.
- **Temperature Scale** (**Settings -> General**): Toggle **Show Temperatures in °F** to switch between Celsius (`°C`) and Fahrenheit (`°F`).
- **Metric Order & Visibility** (**Settings -> Layout**): Drag and drop metrics to change their order on the taskbar, or remove metrics you don't need.
- **Colors & Themes** (**Settings -> Appearance**): Customize metric text colors individually, or click **Reset All to Defaults** to apply authentic hardware manufacturer brand colors automatically.

---

> [!TIP]
> **Looking for developer architecture or internal configuration keys?**
> If you are building custom plugins, inspecting raw JSON configuration files, or debugging sensor drivers, see the [**Hardware Drivers Architecture Guide**](../developer-guide/hardware-drivers.md) and [**Backend Architecture Guide**](../developer-guide/backend-architecture.md).
