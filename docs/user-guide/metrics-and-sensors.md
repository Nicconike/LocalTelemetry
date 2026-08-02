# Telemetry Metrics & Hardware Sensors

LocalTelemetry polls hardware metrics directly using low-level kernel drivers and native vendor APIs. This page details all supported metrics and hardware sensors.


## 1. CPU Metrics

LocalTelemetry monitors Intel Core / Core Ultra and AMD Ryzen processors:

| Metric Name         | Display ID  | Description                                                    |
| :------------------ | :---------- | :------------------------------------------------------------- |
| **CPU Usage**       | `cpu_usage` | Total CPU load percentage across all physical & logical cores. |
| **CPU Temperature** | `cpu_temp`  | CPU Package / Tdie temperature in Celsius (°C).                |
| **CPU Clock Speed** | `cpu_freq`  | Current average or core max clock frequency in GHz/MHz.        |
| **CPU Power**       | `cpu_power` | CPU Package Power draw in Watts (W) via RAPL registers.        |


## 2. GPU Metrics

Supports **NVIDIA GeForce**, **AMD Radeon** and **Intel Arc / UHD / Iris Xe** graphics cards:

| Metric Name         | Display ID  | Supported Hardware        | Description                              |
| :------------------ | :---------- | :------------------------ | :--------------------------------------- |
| **GPU Usage**       | `gpu_usage` | All GPUs                  | Graphics core utilization percentage.    |
| **GPU Temperature** | `gpu_temp`  | NVIDIA, AMD, Intel        | GPU Core / Edge temperature in °C.       |
| **GPU VRAM Usage**  | `gpu_vram`  | NVIDIA, AMD, Intel        | Dedicated VRAM consumption in MB / GB.   |
| **GPU Power**       | `gpu_power` | NVIDIA (NVAPI), AMD (ADL) | Total GPU board power draw in Watts (W). |
| **GPU Clock Speed** | `gpu_freq`  | NVIDIA, AMD               | GPU Core clock frequency in MHz.         |


## 3. RAM (Memory) Metrics

| Metric Name            | Display ID  | Description                                     |
| :--------------------- | :---------- | :---------------------------------------------- |
| **RAM Usage %**        | `ram_usage` | Percentage of physical memory currently used.   |
| **RAM Used (GB)**      | `ram_used`  | Total active RAM consumption in Gigabytes (GB). |
| **RAM Available (GB)** | `ram_avail` | Free physical memory available to Windows.      |


## 4. Storage (Disk) Metrics

| Metric Name          | Display ID   | Description                                        |
| :------------------- | :----------- | :------------------------------------------------- |
| **Disk Read Speed**  | `disk_read`  | Active disk read throughput in KB/s or MB/s.       |
| **Disk Write Speed** | `disk_write` | Active disk write throughput in KB/s or MB/s.      |
| **Disk Activity %**  | `disk_usage` | Active disk time percentage across primary drives. |


## 5. Network Metrics

| Metric Name              | Display ID  | Description                                              |
| :----------------------- | :---------- | :------------------------------------------------------- |
| **Download Speed**       | `net_down`  | Live incoming internet throughput (e.g. `12.5 MB/s`).    |
| **Upload Speed**         | `net_up`    | Live outgoing internet throughput (e.g. `2.1 MB/s`).     |
| **Daily Data Sent/Recv** | `net_total` | Aggregated daily data transfer logged by LocalTelemetry. |


## Polling Rate Configuration

By default, LocalTelemetry polls hardware sensors every **1000 ms** (1 second).

You can adjust the polling interval in **Settings -> Monitoring**:
- **Fast**: `500 ms` (High responsiveness)
- **Normal**: `1000 ms` (Recommended balance)
- **Battery Saver**: `2000 ms - 5000 ms` (Minimizes CPU wakeups)
