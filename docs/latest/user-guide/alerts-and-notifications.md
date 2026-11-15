# :bell-ring: Threshold Alerts & Toast Notifications

LocalTelemetry features a proactive alert engine designed to warn you whenever temperatures, power draw, memory limits, or bandwidth consumption exceed your safety thresholds.

## :package-check: Alert Delivery Mechanisms

When a hardware metric crosses an enabled threshold, LocalTelemetry dispatches warnings through three coordinated channels:

1. **In-Overlay Warning Color**: When a threshold breach persists, the overlay displays the affected metric value in bright Coral Red (`#FF4444`). The metric keeps this warning color until the value normalizes.
2. **Visual Taskbar Flashing**: The overlay flashes briefly with a translucent orange warning tint (`rgba(255, 80, 0, 0.24)`). This notifies you instantly without taking window focus.
3. **Desktop Toast Notifications**: An animated toast notification slides in near the bottom-right corner of your screen via the helper process (`LocalTelemetry.Notifier.exe`).

![Standalone Toast Alert Notification](/images/toast-alert.png)
*Non-intrusive desktop toast alert notifying about elevated hardware temperature.*

## :hat-glasses: Anti-Focus-Stealing Architecture

Desktop toast popups are handled by a dedicated standalone process (`LocalTelemetry.Notifier.exe`) communicating with the main application via named pipes.

### :shield-question-mark: Why is this important?

- **Zero Gaming Interruptions**: Standard Windows toast popups can minimize full-screen games or steal keyboard/mouse focus. LocalTelemetry's notification windows use a non-activating desktop overlay window style so toasts never take focus, minimize games, or disrupt keyboard typing.
- **Isolated Animation Performance**: Slide-in and fade-out animations run on a separate UI thread in the helper process, preventing frame drops in the core telemetry polling loop.
- **On-Demand Process Lifecycle**: The notifier helper process (`LocalTelemetry.Notifier.exe`) is dynamically spawned only when threshold alerts and toast notifications are active in Settings. If alerts or toast popups are turned off, the process is automatically terminated to eliminate background RAM usage and idle thread overhead.

::: tip Technical Implementation
To inspect the asynchronous Named Pipe IPC protocol, notification packet serialization, and `WS_EX_NOACTIVATE` window styling, see the [Notifier & Named Pipe IPC](/developer-guide/notifier-ipc) developer guide.
:::

## :monitor-cog: Configurable Hardware Thresholds

In **Settings -> Alerts**, switch on **Enable Threshold Alerts** to customize sensor limits.

::: tip Automatic Safety Defaults
When you enable threshold alerts for the first time, LocalTelemetry enables only one safety default automatically:

- **CPU Temp**: Enabled at 90°C.

All other sensor alerts remain disabled until you turn them on. You can adjust or disable this default at any time.
:::

![Alerts Configuration Interface](/images/alerts-settings.png)
*Alerts configuration tab for setting hardware limits, bandwidth quotas, and delivery options.*

### :cpu: CPU Alerts

- **CPU Usage**: Alerts when total processor utilization exceeds the specified limit (`50%` to `100%`).
- **CPU Temp**: Alerts when package temperature exceeds safe thresholds (`60°C` to `100°C`).
- **CPU Frequency**: Low-threshold warning that triggers when core clock speed drops **below** the minimum target (`200 MHz` to `2000 MHz`) due to thermal or power throttling.
- **CPU Package Power**: Alerts when package power draw exceeds the wattage limit (`20 W` to `250 W`).

### :memory-stick: RAM Alerts

- **RAM Usage**: Alerts when total system memory utilization exceeds the configured limit (`50%` to `100%`).

### :gpu: GPU Alerts

- **GPU Usage**: Alerts when graphics core load exceeds the specified threshold (`50%` to `100%`).
- **GPU VRAM Usage**: Alerts when dedicated video memory consumption exceeds the limit (`1,024 MB` to `32,768 MB`).
- **GPU Temp**: Alerts when GPU core temperature exceeds the safety threshold (`60°C` to `100°C`).
- **GPU Frequency**: Low-threshold warning that triggers when GPU core clocks drop **below** the minimum target (`100 MHz` to `2000 MHz`) due to graphics throttling.
- **GPU Power**: Alerts when total board power draw exceeds the wattage threshold (`20 W` to `400 W`).

### :wifi: Network Bandwidth Quotas

- **Daily Internet Usage**: Alerts when total transferred data across the active network adapter exceeds your daily budget (`5 GB` to `500 GB`).
- **Monthly Internet Usage**: Alerts when cumulative monthly data transfer crosses your ISP bandwidth cap (`100 GB` to `5,000 GB`).

### :battery-warning: Battery Alerts

::: info Hardware Dependency
The Battery alert section appears only on systems with a detected battery (`hasBattery`). Desktop computers automatically hide this section.
:::

- **Battery Low**: Alerts when laptop battery charge drops below the warning level (`5%` to `50%`) while actively discharging. Warnings are automatically suppressed while connected to AC power.

## :shield-alert: Anti-Spike Filtering & Normalization Hysteresis

To prevent false alarms and notification flapping, LocalTelemetry implements standard hardware monitoring safeguards:

### :timer: Sustained Breach Duration Filter

Momentary CPU turbo boosts can exceed thermal targets for 1 to 2 seconds during application startup. LocalTelemetry requires a sensor to breach its threshold continuously for a sustained duration before triggering notifications.
Configure this duration with the **Sustained breach duration** slider (`1 s` to `10 s`, default: `3 s`). Transient spikes below this duration are ignored.

### :thermometer-snowflake: 10°C Thermal Normalization Hysteresis

When a temperature breach triggers an alert, the warning state remains active until temperature drops at least **10°C below** the threshold:

- For example, if CPU Temp threshold is `90°C`, the alert triggers when temperature stays at or above 90°C for 3 seconds.
- The alert clears and the Coral Red overlay color resets only when CPU temperature cools to **80°C or below**.
- This hysteresis prevents repeated notifications when temperatures fluctuate rapidly near the limit.
- Non-temperature metrics (Usage, Power, Battery) use a 2-unit hysteresis band.

### :file-text: Structured Alert Audit Logging

When an alert triggers or normalizes, LocalTelemetry writes structured entries to the active application log file (`%LOCALAPPDATA%\LocalTelemetry\logs\`):

- **Breach Warning**: `[WARNING] CPU Temperature reached 92°C (threshold: 90°C) [Actions: Toast, Overlay Flash]`
- **Normalization Info**: `[INFO] CPU Temperature normalized to 78°C (threshold: 90°C)`

## :octagon-alert: Alert Actions & Cooldown Controls

Under the **Actions** section in **Settings -> Alerts**, you can fine-tune notification behavior:

- **Show Tray Notification**: Toggles desktop toast popup notifications on or off.
- **Flash Overlay Briefly**: Toggles in-place visual flashing on the taskbar overlay.
- **Sustained breach duration**: Sets how many continuous seconds a sensor must breach its limit before triggering (`1 s` to `10 s`, default: `3 s`).
- **Cooldown between alerts**: Configures the delay before repeat notifications are sent for the same sensor (`5 s` to `120 s`, default: `60 s`).
- **Only fire once per session (ignores cooldown)**: Ignores cooldown and fires an alert only once per application run until LocalTelemetry is restarted.
