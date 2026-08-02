# Svelte 5 Settings & Customization

LocalTelemetry features a modern settings interface built using **Svelte 5 Runes**, **TypeScript** and **Vite** rendered inside a native **WebView2** window.


## Accessing the Settings Interface

- Double-click the **System Tray Icon**.
- Or right-click the tray icon and choose **Open Settings**.

![Svelte 5 Settings Panel](/images/settings.png)
*Figure: Svelte 5 settings panel - Layout drag-and-drop reordering page.*

The window opens with a dark-mode theme by default, featuring a responsive sidebar navigation menu.


## Customizable Categories

### 1. Taskbar Layout (`LayoutPage.svelte`)
- **Metric Drag & Drop**: Drag items or use up/down arrows to reorder how metrics appear horizontally on the taskbar.
- **Row Assignment**: Assign specific metrics to Row 1 or Row 2 in two-line taskbar mode.
- **Unit Display**: Toggle showing unit labels (`°C`, `GB`, `MB/s`, `%`) or compact mode.

### 2. Appearance & Theme (`AppearancePage.svelte`)
- **Metric Colors**: Assign unique hex colors to individual metrics (e.g. CPU Temp in Cyan, GPU in Green, RAM in Purple).
- **Alert Colors**: Set highlight flashing colors for warnings.
- **Font & Size**: Choose font family (Inter, Segoe UI, Roboto) and font size for taskbar readability.
- **Text Style**: Toggle bold, italic or custom padding.

### 3. System & Autostart (`SystemPage.svelte`)
- **Start with Windows**: Toggles automatic launch at Windows login (creates a Registry Run key under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`).
- **Run Mode**: Switch between Normal installation mode and Portable data mode.

### 4. Configuration Backup & Restore
- **Export Settings**: Export your settings to a `settings.json` backup file.
- **Import Settings**: Load configuration from a saved JSON file.
- **Reset Defaults**: Restore LocalTelemetry factory default settings.
