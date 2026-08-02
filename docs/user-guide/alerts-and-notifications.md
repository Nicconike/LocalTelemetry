# Threshold Alerts & Toast Notifications

LocalTelemetry includes a customizable alerting system to warn you whenever hardware temperatures or usage exceed safe operational boundaries.

## Alert Types

LocalTelemetry supports two distinct types of alerts:

1. **Visual Flashing Alert**: The corresponding metric on the taskbar overlay flashes in a bright warning color (e.g. bright red or amber) to draw immediate attention without interrupting your workflow.
2. **Desktop Toast Notification**: A non-intrusive Windows toast popup window launched via `LocalTelemetry.Notifier.exe`.

![Standalone Toast Alert Notification](/images/toast-alert.png)
*Figure: Standalone desktop toast warning popup.*


## Configuring Alert Thresholds

In **Settings -> Alerts**, you can enable and set individual limits:

- **CPU Temp Threshold**: Default `85°C`. Triggered when CPU temperature exceeds this limit.
- **GPU Temp Threshold**: Default `83°C`. Triggered when GPU core temperature exceeds this limit.
- **CPU Usage Threshold**: Default `95%`. Triggered on sustained high CPU load.
- **RAM Usage Threshold**: Default `90%`. Triggered when system memory is near exhaustion.


## Alert Cooldown & Throttle Controls

To prevent spamming notifications while gaming or rendering:
- **Cooldown Interval**: Set minimum seconds between repeat notifications (Default: `30 seconds`).
- **Duration**: Duration of visual text flashing in the taskbar overlay.


## The Standalone Notifier (`LocalTelemetry.Notifier`)

Desktop notifications are handled by a dedicated lightweight helper process (`LocalTelemetry.Notifier.exe`).

### Why a separate process?
By isolating notifications in a secondary process:
- Toast popups never steal focus from full-screen games or applications.
- Heavy notification window animations do not affect the main overlay polling thread.
