# Svelte 5 Settings & Customization

LocalTelemetry features a modern settings interface built using **Svelte 5 Runes**, **TypeScript** and **Vite** rendered inside a native **WebView2** window.

## Accessing the Settings Interface

- Double-click the **System Tray Icon**.
- Or right-click the tray icon and choose **Open Settings**.

The window opens with a dark-mode theme by default, featuring a responsive sidebar navigation menu.

## Customizable Categories

### 1. General (`GeneralPage.svelte`)
- **Start with Windows**: Enables automatic launch at Windows login.
- **Start minimized**: Launch into the system tray without opening the settings window.
- **Minimize to Tray on close**: Closing the settings window keeps the app running in the tray.
- **Polling interval**: Set hardware polling to `0.5 s`, `1 s`, `2 s` or `5 s`.
- **Units**: Toggle temperatures between °C and °F.

![General settings](/images/general-settings.png)
*Figure: General settings page.*

### 2. Taskbar Layout (`LayoutPage.svelte`)
- A single ordered list of metrics shown in the taskbar overlay.
- **Add / Remove**: Add metrics from the picker or remove them from the list.
- **Reorder**: Drag metrics (or use the up/down arrows) to change their order in the widget.

![Taskbar layout settings](/images/layout-settings.png)
*Figure: Taskbar layout reordering page.*

### 3. Appearance & Theme (`AppearancePage.svelte`)
- **Theme**: Choose from **12 built-in themes** (6 dark + 6 light variants).
- **Metric Colors**: Assign individual colors to each metric (e.g. CPU in Cyan, GPU in Blue, RAM in Purple).

### 4. Monitoring (`MonitoringPage.svelte`)
- **Component toggles**: Enable or disable each hardware component (CPU, GPU, RAM, disks, network, battery).
- **Enable Metrics Logging**: Toggle periodic metric logging to disk.

![Monitoring settings](/images/monitoring-settings.png)
*Figure: Monitoring settings page.*

### 5. System & Autostart
- **Start with Windows** is implemented as a **Windows Scheduled Task** named `LocalTelemetry Startup`, created with `schtasks.exe` (`/SC ONLOGON /RL HIGHEST`). Running elevated at logon avoids UAC prompts at startup. A legacy `HKCU\...\Run` registry key is only cleaned up for older installations.

### 6. Deployment Mode
There is no "Run Mode" switch in the UI. The mode is derived from an `app.mode` marker file next to the executable:
- **Normal (Installed)**: The installer writes `app.mode`; settings and logs are stored in `%LOCALAPPDATA%\LocalTelemetry`.
- **Portable**: Without the marker (e.g. running from a build output), settings are stored next to the executable.

### 7. Configuration Backup & Restore
- **Export Settings**: Export your settings to a `settings.json` backup file.
- **Import Settings**: Load configuration from a saved JSON file.
- **Reset Defaults**: Restore LocalTelemetry factory default settings.
