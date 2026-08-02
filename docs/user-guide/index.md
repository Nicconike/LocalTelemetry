# User Guide - LocalTelemetry Overview

Welcome to the **LocalTelemetry User Guide**!

This guide provides comprehensive documentation for configuring and operating LocalTelemetry on Windows 10 and 11.


## What is LocalTelemetry?

**LocalTelemetry** is a lightweight, real-time Windows hardware monitoring utility that embeds live system performance metrics directly inside your Windows taskbar.

Instead of taking up desktop space with floating gadgets or overlay windows, LocalTelemetry hooks into the native Windows taskbar (`Shell_TrayWnd`) to display CPU, GPU, RAM, Disk, and Network telemetry right where you need it most.

![LocalTelemetry Taskbar Overlay](/images/overlay.png)
*Real-time hardware telemetry overlay running inside the Windows taskbar.*

## ⚡ Key Feature Highlights

- **📊 Taskbar Embedded**: Displays real-time hardware metrics inside your native Windows taskbar via child window reparenting (`SetParent` interop) or layered float fallback.
- **🌡️ CPU & GPU Telemetry**: Track temperatures, usage percentages, clock speeds, VRAM, and package power in real time.
- **💾 Storage & Memory**: Live RAM consumption and active disk read/write throughput.
- **🌐 Network Monitoring**: Real-time upload/download speeds and aggregated daily bandwidth tracking.
- **🔔 Threshold Alerts**: Visual text flashing animations and desktop toast notifications when safety limits are exceeded.
- **🎨 Svelte 5 Custom UI**: Dark-mode settings interface built with Svelte 5 runes, Vite, and WebView2.
- **🔒 100% Private & Local**: Zero cloud connectivity, no telemetry phoning home, no external API calls. Your data stays strictly on your PC.

> [!NOTE]
> When running silently in the system tray, LocalTelemetry operates with minimal CPU and RAM usage. Opening the settings panel launches an embedded Microsoft WebView2 runtime host to render the Svelte 5 configuration UI.

## 📖 User Guide Chapter Breakdown

Explore the detailed user guides:

1. [**Installation & Requirements**](./installation.md)
   System specifications, installer setup, portable ZIP usage and User Account Control (UAC) permissions.

2. [**Quickstart Guide**](./quickstart.md)
   Get LocalTelemetry up and running in less than 2 minutes.

3. [**System Tray & Taskbar Overlay**](./tray-and-overlay.md)
   Managing overlay positioning, multi-line rendering, high DPI scaling, and system tray controls.

4. [**Telemetry Metrics & Hardware Sensors**](./metrics-and-sensors.md)
   Complete guide to all supported hardware sensors (CPU, GPU, RAM, Disk, Network, Battery).

5. [**Threshold Alerts & Toast Notifications**](./alerts-and-notifications.md)
   Configuring warning limits, visual text flashing, and standalone desktop toast popups.

6. [**Settings & Customization**](./customization.md)
   Customizing metric colors, reordering items, auto-start and configuration backups.

7. [**Traffic & Network History**](./network-history.md)
   Daily bandwidth tracking, ESE log integration and data history calendars.

8. [**Troubleshooting & FAQ**](./troubleshooting.md)
   Solutions for common questions, driver loading issues and system log file locations.
