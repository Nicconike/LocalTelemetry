# :laptop: Development Environment Setup

This guide details how to clone, set up, build, and run **LocalTelemetry** from source code on Windows.

## :wrench: Prerequisites & Toolchains

Before building LocalTelemetry, ensure you have the following installed on your Windows machine:

1. **Operating System**: Windows 10 (v2004+ / build 19041+) or Windows 11 (64-bit x64)
2. **.NET 10 SDK**: Install the official [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
3. **Bun JavaScript Runtime**: Install [Bun](https://bun.sh/) for building the Svelte 5 frontend:

   ```powershell
   powershell -c "irm bun.sh/install.ps1 | iex"
   ```

4. **IDE / Editor**:
   - **Visual Studio 2022+** (with *.NET Desktop Development* workload enabled)
   - Or **VS Code** (with *C# Dev Kit* and *Svelte* extensions)

> [!IMPORTANT]
> **Bun Package Manager**: The Svelte 5 frontend MUST be built using **Bun** (`bun install` and `bun run build`) to preserve `bun.lock` integrity. Do not use npm, yarn, or pnpm.

## :terminal: Setup Instructions

### :git-branch: Cloning the Repository

```powershell
git clone https://github.com/Nicconike/LocalTelemetry.git
cd LocalTelemetry
```

### :package: Installing Dependencies & Building Frontend Assets

Navigate to the frontend directory inside `src/LocalTelemetry.App/Settings/wwwroot`:

```powershell
cd src/LocalTelemetry.App/Settings/wwwroot
bun install
bun run build
cd ../../../..
```

This compiles the Svelte 5 single-page application into `dist/`, which is embedded into the WPF binary as WebView2 assets.

### :box: Restoring Dependencies & Building C# Backend

Build the backend solution from the root repository directory:

```powershell
dotnet build LocalTelemetry.sln --configuration Debug
```

### :test-tube: Running the Automated Test Suites

Run both the frontend Vitest unit tests and the backend C# unit tests:

```powershell
# Run frontend Vitest tests
cd tests/LocalTelemetry.App.Tests/Settings/wwwroot
bun install
bun run test
cd ../../../..

# Run backend C# unit tests across all three test suites
dotnet test LocalTelemetry.sln --configuration Debug
```

## :play: Launching in Development Mode

### :terminal: Running the Host Application (Administrator Required)

Launch the application target from an **elevated (Administrator) terminal**:

```powershell
dotnet run --project src/LocalTelemetry.App
```

> [!IMPORTANT]
> **Administrator Privileges Mandatory**: LocalTelemetry's application manifest (`app.manifest`) specifies `<requestedExecutionLevel level="requireAdministrator" uiAccess="false" />` to allow communication with the `PawnIO` kernel sensor driver. Executing `dotnet run` from an unprivileged terminal will result in an elevation failure or UAC error.

### :flame: Running Frontend in Hot-Reloading Mode (Optional)

During rapid UI prototyping, you can launch the Vite local dev server with hot module replacement:

```powershell
cd src/LocalTelemetry.App/Settings/wwwroot
bun run dev
```

The dev server serves the frontend on `http://localhost:5173`. You can inspect and test Svelte components in your browser before compiling production assets.

## :monitor: Running the Documentation Site Locally

To run this VitePress documentation site locally:

```powershell
cd docs
bun install
bun run docs:dev
```

Open your browser to `http://localhost:5173` to preview documentation changes in real time.

## :bug: Common Troubleshooting & Environment Fixes

- **Missing PawnIO Driver on First Launch**: If low-level CPU registers fail to initialize when launching from Visual Studio or CLI, ensure `PawnIO_setup.exe` was copied to the output folder. Running `dotnet build LocalTelemetry.sln` automatically executes the `PreserveNewest` file copy target.
- **Node vs Bun Path Conflicts**: Ensure Bun is added to your user `PATH` environment variable (`%USERPROFILE%\.bun\bin`).
- **WebView2 Runtime Missing**: On Windows 10 development machines lacking WebView2, install the Microsoft Edge WebView2 Evergreen Runtime. Windows 11 includes it natively.
