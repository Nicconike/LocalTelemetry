# :book-open: Overview & Introduction

Welcome to the **LocalTelemetry User Guide**!

This documentation walks through installing, configuring, and getting the most out of **LocalTelemetry** on Windows.

## :localtelemetry: What is LocalTelemetry?

**LocalTelemetry** is a native, privacy-first hardware monitoring utility for Windows built with .NET 10 and Svelte 5. It continuously reads low-level hardware sensors and displays real-time performance vitals directly on the Windows taskbar.

Instead of running floating widgets, desktop gadgets, or heavy overlays that obstruct your workspace or full-screen games, LocalTelemetry anchors directly to the taskbar. It operates as a lightweight background process with minimal CPU overhead, only initializing the embedded WebView2 engine when you open the settings interface.

![LocalTelemetry Default Taskbar Overlay](/images/overlay-default.png)
*Default out-of-the-box taskbar overlay displaying 5 dual-row columns with automatic hardware vendor brand colors applied.*

## :zap: Core Capabilities

- :app-window: **Taskbar Overlay**: Displays live system vitals directly on the Windows 10 & 11 taskbar with high-DPI scaling, ClearType typography, and zero desktop window clutter.
- :cpu: **Kernel & Driver-Level Polling**: Direct low-level polling for Intel (RAPL MSR), AMD (Ryzen SMU / ADL), and NVIDIA (NVML) for package wattage, clock speeds, and temperatures.
- :calendar-days: **Gapless Network History**: Reconstructs daily bandwidth from your Windows installation date via Windows SRUM (`SRUDB.dat`) alongside live speed metering.
- :battery-plus: **Laptop Battery Telemetry**: Real-time battery charge percentage and charge/discharge rate in Watts directly on the taskbar overlay, plus manufacturer, device model, designed capacity, and full charge capacity in the System dashboard.
- :bell-ring: **Non-Intrusive Safety Alerts**: In-place taskbar flashing and focus-safe desktop toast notifications that warn of thermal throttling without minimizing full-screen games.
- :palette: **Svelte 5 Customization Dashboard**: Complete control over metric visibility, drag-and-drop sequencing, per-metric colors, and warning thresholds.
- :shield-check: **Privacy-First & Local Storage**: All hardware sensor metrics and bandwidth logs remain strictly on your local machine with zero analytics or tracking. Software updates check the official GitHub Releases API either on-demand or at your configured schedule without transmitting personal telemetry.

## :book-open-text: User Guide Chapters

Explore the step-by-step guides for each feature:

1. [**Installation & Requirements**](./installation.md)
   System requirements, Inno Setup installer vs. Portable ZIP package, and Administrator (UAC) driver privileges.

2. [**Quickstart Guide**](./quickstart.md)
   First launch setup, enabling the taskbar overlay, and basic configuration.

3. [**System Tray & Taskbar Overlay**](./tray-and-overlay.md)
   System tray controls, overlay positioning, layout modes, and Windows Explorer restart recovery.

4. [**Telemetry Metrics & Sensors**](./metrics-and-sensors.md)
   Comprehensive reference of all supported hardware metrics, polling APIs, measurement units, and update rates.

5. [**Threshold Alerts & Toast Notifications**](./alerts-and-notifications.md)
   Configuring warning limits, visual taskbar flashing, and focus-safe desktop toast notifications.

6. [**Settings & Customization**](./customization.md)
   Detailed walkthrough of all 10 settings tabs: General preferences, sensor monitoring, overlay placement, metric layout, appearance themes, alerts, and system specs.

7. [**Traffic & Network History**](./network-history.md)
   Daily internet usage tracking, monthly calendar heatmap, Windows `SRUDB.dat` history backfill, and local JSON Lines storage.

8. [**Troubleshooting & FAQ**](./troubleshooting.md)
   Frequently asked questions, sensor diagnosis, and application log locations.
