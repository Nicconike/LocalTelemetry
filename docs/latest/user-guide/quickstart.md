# :rocket: Quickstart Guide

Get up and running with **LocalTelemetry** in less than 2 minutes.

## :localtelemetry: Launching LocalTelemetry

Launch LocalTelemetry using your desktop shortcut or by running `LocalTelemetry.exe`:

- The **Settings Window** opens immediately on your screen for initial configuration.
- The **LocalTelemetry icon** appears in your Windows notification area (System Tray).
- Hovering over the tray icon displays a live hardware tooltip summary with current CPU, RAM, and GPU utilization (e.g. `CPU 7%  RAM 50%  GPU 0%`).

![Tray Icon Tooltip](/images/tray-icon-tooltip.png)
*Live hardware summary tooltip displayed when hovering over the system tray icon.*

> [!NOTE]
> LocalTelemetry requires Administrator privileges to access low-level CPU and GPU hardware sensors. When launched manually, Windows displays a standard User Account Control (UAC) prompt. To start automatically and silently at boot without a UAC prompt, enable **Start with Windows** in Settings.

## :panel-bottom: Enabling the Taskbar Overlay

By default on a fresh installation, the taskbar overlay is hidden until enabled. You can turn it on in two ways:

1. **Via Settings**: Navigate to **Overlay** in the settings dashboard and toggle **Show Overlay** on.
2. **Via System Tray**: Right-click the **System Tray Icon** and click **Show Overlay**.

![System Tray Context Menu](/images/tray-menu-show.png)
*System tray context menu offering quick access to show the overlay, open settings, or quit.*

Once enabled, the overlay immediately anchors onto your Windows taskbar:

![Default Taskbar Overlay](/images/overlay-default.png)
*Default out-of-the-box taskbar overlay displaying 5 dual-row columns with automatic hardware brand colors applied and some metrics configured with custom colors.*

## :settings: Configuring Key Preferences

Explore the key configuration tabs in the settings dashboard to tailor LocalTelemetry to your setup:

### :layout-dashboard: Customize Displayed Metrics (Layout vs Monitoring)

- **Layout Tab**: Drag and drop metrics to customize column order, add or remove sensors (such as NVMe disk throughput or laptop battery vitals), or click **Reset Layout to Defaults** to restore the standard 5-column layout.
- **Monitoring Tab**: Toggle individual hardware polling components (CPU, GPU, RAM, Disk, Network, Battery) on or off to eliminate unused background queries, and configure CPU calculation modes (*Average* vs *Max Active Core* clocks; *Overall* vs *Max Thread* usage).

### :proportions: Adjust Overlay Position & Scale

- **Position & Offset**: Position the overlay dynamically (*Left Side* or *Right Side* on centered taskbars; *Next to Apps* or *Before System Tray* on left-aligned taskbars), and adjust horizontal offset.
- **Scale & Opacity**: Adjust overlay scaling (`80%` to `200%`) and transparency (`20%` to `100%`) to match your desktop aesthetic.
- **Double-Click Action**: Choose what happens when double-clicking the overlay (`None`, `Open Task Manager`, or `Open Settings`).

### :paintbrush: Personalize Colors & Themes

- **Window Themes**: Switch between curated dark and light theme presets for the settings dashboard, or click **Reset to Default Theme**.
- **Metric & Group Colors**: Customize individual metric colors or entire component palettes, or click **Reset All to Defaults** to re-apply detected hardware vendor brand colors.

### :bell-check: Configure Threshold Warnings

- **Threshold Limits**: Define warning triggers for CPU/GPU temperatures, memory limits, and daily/monthly bandwidth quotas.
- **Notification Channels**: Enable in-place text flashing on the taskbar overlay and non-intrusive desktop toast notifications.

### :power: Enable Silent Startup

- **Start with Windows**: Register an elevated Windows Task Scheduler task for silent background startup on logon without UAC prompts.
- **Show System Tray Icon**: Toggle the notification area icon on or off if you prefer running exclusively with the taskbar overlay.
- **Polling Interval & Units**: Choose your preferred sensor refresh rate (`0.5 s` to `5 s`) and toggle **Show Temperatures in °F**.

## :sport-shoe: Next Steps

Continue exploring LocalTelemetry's features and architecture:

- Explore overlay docking, context menus, and mouse controls in the [System Tray & Taskbar Overlay Guide](./tray-and-overlay.md).
- Reference all supported hardware sensors and calculation modes in the [Telemetry Metrics & Sensors Guide](./metrics-and-sensors.md).
- Track daily and monthly bandwidth usage in the [Traffic & Network History Guide](./network-history.md).
- Configure temperature and resource warning limits in the [Threshold Alerts & Toast Notifications Guide](./alerts-and-notifications.md).
- Learn about all 10 configuration tabs in the [Settings & Customization Guide](./customization.md).
- Find answers to common questions and diagnostic steps in the [Troubleshooting & FAQ Guide](./troubleshooting.md).
