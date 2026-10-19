# :package: Building & Packaging (Inno Setup 7, WinGet & Scoop)

This guide explains how to produce production builds of **LocalTelemetry**, compile application binaries, configure Inno Setup 7, generate WinGet and Scoop manifests, and execute uninstallation scripts.

## :hammer: Building Production Binaries

### :code-xml: Svelte 5 Frontend Build

Compile the Svelte 5 settings panel single-page application:

```powershell
cd src/LocalTelemetry.App/Settings/wwwroot
bun install
bun run build
cd ../../../..
```

### :box: Application Binary Compilation

`LocalTelemetry.App.csproj` includes automated MSBuild targets (`CopyNotifier` and `CopyWwwroot`) alongside `PreserveNewest` deployment of `PawnIO_setup.exe` and `Uninstall.bat`:

```powershell
# Framework-dependent build (.NET 10 Desktop Runtime required, used in release pipeline)
dotnet publish src/LocalTelemetry.App/LocalTelemetry.App.csproj -c Release -r win-x64 --no-self-contained -p:PublishSingleFile=true -o ./publish/

# Optional: self-contained build (bundles the entire .NET 10 runtime)
dotnet publish src/LocalTelemetry.App/LocalTelemetry.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ./publish/
```

This single command produces the following deployment files in `./publish/`:

- `LocalTelemetry.exe`: Main application executable.
- `LocalTelemetry.Notifier.exe`: Standalone toast notification helper executable.
- `wwwroot/`: Embedded Svelte 5 frontend assets.
- `PawnIO_setup.exe`: Hardware kernel sensor driver setup executable.
- `Uninstall.bat`: Automated administrative driver and service cleanup script for portable mode.

---

## :shield: Compiling the Inno Setup 7 Installer

LocalTelemetry uses **Inno Setup 7** to build its official installer executable (`LocalTelemetrySetup.exe`).

The configuration script is located at [`setup.iss`](https://github.com/Nicconike/LocalTelemetry/blob/master/setup.iss) in the root repository folder.

### :list-checks: Inno Setup Lifecycle Controls

- **Running Process Detection (`CloseApplicationsFilter`)**: Inspects whether `LocalTelemetry.exe` or `LocalTelemetry.Notifier.exe` is active, prompting to terminate them before updating.
- **Automatic .NET 10 Runtime Bootstrapper**: Checks whether the .NET 10 Desktop Runtime is installed (`IsDotNet10DesktopInstalled`). If missing, it automatically downloads and installs Microsoft's official runtime package (~55 MB) with a progress bar.
- **PawnIO Driver Provisioning**: Stops and deletes any stale `PawnIO` service, executes `PawnIO_setup.exe -install -silent`, retries if Windows returns error 1072 (service marked for deletion), and flags for reboot if needed.
- **Scheduled Task Registration**: Uses the Task Scheduler COM API to create `LocalTelemetry Startup` with `TASK_RUNLEVEL_HIGHEST`, `DisallowStartIfOnBatteries = false`, `StopIfGoingOnBatteries = false`, and `ExecutionTimeLimit = "PT0S"` (infinite runtime).
- **App Mode Marker**: Creates `{app}\app.mode` so the application routes user settings to `%LOCALAPPDATA%\LocalTelemetry`.
- **Complete Driver & Registry Cleanup on Uninstall**: Closes processes, removes the scheduled task, stops the service, and invokes PowerShell with `pnputil.exe /delete-driver /uninstall /force` to purge `pawnio.inf` from the Windows DriverStore. If Windows returns code `3010`, it warns the user that a restart is required to release the driver from memory.
- **User Data Prompt**: Interactively asks the user whether to preserve or remove user configuration (`settings.json`) and bandwidth history (`internet_usage.jsonl`).

### :terminal: Compiling via ISCC CLI

If Inno Setup 7 is installed on your system (via `winget install --id JRSoftware.InnoSetup.7`):

```powershell
& "C:\Program Files\Inno Setup 7\ISCC.exe" /DAppVersion=1.0.0-rc.1 /DAppVersionInfoVersion=1.0.0.0 setup.iss
```

This compiles `LocalTelemetrySetup.exe` into the repository root folder.

---

## :trash: Portable Mode Cleanup Script

For portable installations, [`Uninstall.bat`](https://github.com/Nicconike/LocalTelemetry/blob/master/src/LocalTelemetry.App/Uninstall.bat) provides complete system cleanup:

1. **UAC Auto-Elevation**: Requests administrator privileges via `powershell -Command "Start-Process '%~dpnx0' -Verb RunAs"`.
2. **Process Termination**: Terminates `LocalTelemetry.exe` and `LocalTelemetry.Notifier.exe` using `taskkill /F`.
3. **PawnIO Service Removal**: Stops and deletes the `PawnIO` service via `sc stop PawnIO` and `sc delete PawnIO`.
4. **DriverStore Purge**: Scans `pnputil.exe /enum-drivers` for `pawnio.inf` and executes `pnputil /delete-driver <oem.inf> /uninstall /force`. If Windows returns exit code `3010`, a message box informs the user that a restart is required.
5. **Registry Cleanup**: Removes startup keys from `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` and the `LocalTelemetry Startup` scheduled task.
6. **Residual Folder Removal**: Deletes `C:\Program Files\PawnIO`, `%ProgramData%\LocalTelemetry`, and `%LocalAppData%\Temp\PawnIo`.

---

## :package-check: WinGet Packaging Specifications

The repository maintains the Windows Package Manager manifest in `packaging/winget/localtelemetry.yaml`:

- **Package Identifier**: `Nicconike.LocalTelemetry`
- **Installer Type**: `inno`
- **Silent Switches**: `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-`
- **Automated Workflow**: When a release is published via `.github/workflows/release.yml`, the workflow calculates the SHA-256 hash of `LocalTelemetrySetup.exe`, updates `packaging/winget/localtelemetry.yaml`, and submits the release to the WinGet Community Repository via Microsoft's official `wingetcreate` CLI utility.

---

## :inbox: Scoop Packaging Specifications

The repository hosts a direct Scoop bucket manifest at `bucket/localtelemetry.json`:

- **Manifest**: Specifies binary extraction for `LocalTelemetry-win-x64.zip`, start menu shortcut creation, and GitHub release tracking.
- **Automated Workflow**: When a release is published, `build.ps1` updates `bucket/localtelemetry.json` with the new version and SHA-256 hash, and commits the update directly to `master`.
- **Installation**: Users install directly from this repository without requiring third-party bucket approvals:

  ```powershell
  scoop bucket add localtelemetry https://github.com/Nicconike/LocalTelemetry
  scoop install localtelemetry
  ```

---

## :archive: Generating Portable ZIP Package

The portable release package bundles the `./publish/*` directory (including `Uninstall.bat`):

```powershell
Compress-Archive -Path ./publish/* -DestinationPath ./LocalTelemetry-win-x64.zip -Force
```
