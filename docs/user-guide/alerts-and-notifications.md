# Threshold Alerts & Toast Notifications

LocalTelemetry includes a customizable alerting system that warns whenever hardware temperatures, usage, frequency or power exceed (or drop below) configured thresholds.

## Alert Types

LocalTelemetry supports two types of alerts:

1. **Visual Flashing Alert**: The affected metric on the taskbar overlay flashes in a bright warning color to draw attention without interrupting your workflow.
2. **Desktop Toast Notification**: A non-intrusive Windows toast popup displayed by the `LocalTelemetry.Notifier.exe` helper process.

![Standalone Toast Alert Notification](/images/toast-alert.png)
*Figure: Standalone desktop toast warning popup.*

## Configuring Alert Thresholds

In **Settings -> Alerts**, you can enable and set individual limits. Defaults:

| Alert                        | Setting               | Default           |
| :--------------------------- | :-------------------- | :---------------- |
| **CPU Temperature**          | `CPU Temp Threshold`  | `90°C`            |
| **GPU Temperature**          | `GPU Temp Threshold`  | `88°C`            |
| **CPU Usage**                | `CPU Usage Threshold` | `95%`             |
| **RAM Usage**                | `RAM Usage Threshold` | `92%`             |
| **GPU VRAM Usage**           | `GPU VRAM Threshold`  | `6000 MB`         |
| **CPU Frequency (throttle)** | `CPU Freq Minimum`    | `800 MHz` (below) |
| **CPU Power**                | `CPU Power Maximum`   | `65 W`            |
| **GPU Frequency (throttle)** | `GPU Freq Minimum`    | `300 MHz` (below) |
| **GPU Power**                | `GPU Power Maximum`   | `150 W`           |

Each alert is toggled independently. Frequency alerts fire when the CPU/GPU clock drops below the minimum (thermal throttling), while power alerts fire when package/board power exceeds the maximum.

## Alert Cooldown & Throttle Controls

To prevent notification spam during gaming or rendering:

- **Cooldown Interval**: Minimum seconds between repeat notifications (Default: `60 seconds`).
- **Fire Once Per Session**: When enabled, each alert fires only once per app session and the cooldown is ignored.

## The Standalone Notifier (`LocalTelemetry.Notifier`)

Desktop notifications are handled by a dedicated lightweight helper process (`LocalTelemetry.Notifier.exe`).

### Why a separate process?
By isolating notifications in a secondary process:
- Toast popups never steal focus from full-screen games or applications.
- Heavy notification window animations do not affect the main overlay polling thread.

### How it communicates
The main app launches the notifier with the parent process ID as its first command-line argument (used purely to track the app's lifetime and exit when it closes). Alert payloads (title, body, action) are delivered over a **named pipe** (`LocalTelemetryNotifier`), so no toast data is passed through command-line arguments.
