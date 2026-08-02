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

Publish `LocalTelemetry.App` as a self-contained single executable targeting `win-x64`:

```powershell
dotnet publish src/LocalTelemetry.App/LocalTelemetry.App.csproj -c Release -r win-x64 --self-contained true -o ./publish/
```

Publish `LocalTelemetry.Notifier`:

```powershell
dotnet publish src/LocalTelemetry.Notifier/LocalTelemetry.Notifier.csproj -c Release -r win-x64 --self-contained true -o ./publish/
```


## 📦 2. Compiling the Inno Setup Installer (`setup.iss`)

LocalTelemetry uses **Inno Setup 6** to build its installer executable (`LocalTelemetrySetup.exe`).

The script is located at `setup.iss` in the root repository folder.

### Key `setup.iss` Configurations

- **App ID**: `{A8E4C0D1-8523-4876-9231-18F17A634289}`
- **ArchitecturesAllowed**: `x64compatible`
- **PrivilegesRequired**: `admin`
- **Output Directory**: `Output/LocalTelemetrySetup.exe`

### Compiling via ISCC CLI

If Inno Setup 6 is installed on your system:

```powershell
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" setup.iss
```

This generates `Output\LocalTelemetrySetup.exe` ready for distribution.


## 📦 3. Generating Portable ZIP Package

To create the portable release distribution:

```powershell
Compress-Archive -Path ./publish/* -DestinationPath ./publish/LocalTelemetry-win-x64.zip -Force
```
