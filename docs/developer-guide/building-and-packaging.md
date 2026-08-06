# Building & Packaging (Inno Setup)

This guide explains how to produce production builds of **LocalTelemetry**, compile self-contained binaries and generate the Windows installer setup executable (`LocalTelemetrySetup.exe`).

## 🛠️ 1. Building Production Binaries

### Step 1: Build the Svelte 5 Frontend
```powershell
cd src/LocalTelemetry.App/Settings/wwwroot
bun install
bun run build
cd ../../../..
```

### Step 2: Publish Self-Contained .NET Binaries

Publish `LocalTelemetry.App` as a self-contained single executable targeting `win-x64`. The publish version is driven by MinVer (git tags); for a release build it can be pinned with `-p:MinVerVersionOverride=`:

```powershell
dotnet publish src/LocalTelemetry.App/LocalTelemetry.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ./publish/
```

Publish `LocalTelemetry.Notifier`:

```powershell
dotnet publish src/LocalTelemetry.Notifier/LocalTelemetry.Notifier.csproj -c Release -r win-x64 --self-contained true -o ./publish/
```

## 📦 2. Compiling the Inno Setup Installer (`setup.iss`)

LocalTelemetry uses **Inno Setup** to build its installer executable (`LocalTelemetrySetup.exe`). The CI release workflow installs **Inno Setup 7** via Winget.

The script is located at `setup.iss` in the root repository folder.

### Key `setup.iss` Configurations

- **App ID**: `{{LocalTelemetry_TaskbarApp}}`
- **ArchitecturesAllowed**: `x64compatible`
- **PrivilegesRequired**: `admin`
- **Output Directory**: `.` (repository root, output file `LocalTelemetrySetup.exe`)
- **Minimum OS**: Windows 10 build 19041

### Compiling via ISCC CLI

If Inno Setup is installed on your system:

```powershell
& "C:\Program Files (x86)\Inno Setup 7\ISCC.exe" /DAppVersion=1.0.0-beta /DAppVersionInfoVersion=1.0.0.0 setup.iss
```

This generates `LocalTelemetrySetup.exe` in the repository root, ready for distribution.

## 📦 3. Generating Portable ZIP Package

To create the portable release distribution:

```powershell
Compress-Archive -Path ./publish/* -DestinationPath ./LocalTelemetry-win-x64.zip -Force
Get-FileHash -Path ./LocalTelemetrySetup.exe, ./LocalTelemetry-win-x64.zip -Algorithm SHA256 | Format-Table -AutoSize | Out-String | Set-Content ./checksums.txt
```
