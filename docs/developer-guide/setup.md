# Development Environment Setup

This guide details how to clone, set up, build and run **LocalTelemetry** from source code on your local machine.


## 🛠️ Prerequisites

Before building LocalTelemetry, ensure you have the following installed:

1. **Operating System**: Windows 10/11 (64-bit x64).
2. **.NET 10 SDK**: Download and install [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
3. **Bun JavaScript Runtime**: Install [Bun](https://bun.sh/) for building the Svelte 5 frontend:
   ```powershell
   powershell -c "irm bun.sh/install.ps1 | iex"
   ```
4. **IDE / Editor**:
   - **Visual Studio 2022+** (with *.NET Desktop Development* workload enabled)
   - Or **VS Code** with *C# Dev Kit* and *Svelte* extensions.

> [!IMPORTANT]
> **Bun Requirement**: The frontend MUST be built using **Bun** (`bun install` & `bun run build`). Do not use `npm`, `yarn` or `pnpm` to maintain `bun.lock` integrity.


## 🚀 Step-by-Step Setup

### Step 1: Clone the Repository

```powershell
git clone https://github.com/Nicconike/LocalTelemetry.git
cd LocalTelemetry
```

### Step 2: Install & Build Frontend Dependencies

Navigate to the frontend directory inside `src/LocalTelemetry.App/Settings/wwwroot`:

```powershell
cd src/LocalTelemetry.App/Settings/wwwroot
bun install
bun run build
cd ../../../..
```

This compiles the Svelte 5 single-page application into `dist/` which is embedded into the WPF binary.

### Step 3: Build the .NET Solution

Build the backend solution from the root repository directory:

```powershell
dotnet build LocalTelemetry.sln --configuration Debug
```

### Step 4: Run the Application

Execute the application target:

```powershell
dotnet run --project src/LocalTelemetry.App
```


## 💻 Running the Documentation Site Locally

To run this VitePress documentation site locally:

```powershell
cd docs
bun install
bun run docs:dev
```

Open your browser to `http://localhost:5173` to preview changes in real time.
