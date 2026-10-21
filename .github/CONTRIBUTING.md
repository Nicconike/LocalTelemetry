# Contributing to LocalTelemetry

Thank you for contributing to LocalTelemetry!

## Prerequisites

- Windows 10/11 (64-bit x64)
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Bun](https://bun.sh/) (runtime and package manager for frontend)
- [Visual Studio 2022+](https://visualstudio.microsoft.com/) or [VS Code](https://code.visualstudio.com/) with C# Dev Kit

## Getting Started

1. Fork and clone the repository.
2. Install frontend dependencies:

   ```bash
   cd src/LocalTelemetry.App/Settings/wwwroot
   bun install
   ```

3. Install frontend test dependencies:

   ```bash
   cd tests/LocalTelemetry.App.Tests/Settings/wwwroot
   bun install
   ```

4. Build the frontend:

   ```bash
   cd src/LocalTelemetry.App/Settings/wwwroot
   bun run build
   ```

5. Build the backend from the repository root:

   ```bash
   dotnet build
   ```

6. Run the application from the repository root:

   ```bash
   dotnet run --project src\LocalTelemetry.App
   ```

## Project Structure

```bash
src/
├── LocalTelemetry.App/           # WPF WinExe, Svelte settings UI, tray, overlay
│   ├── Overlay/                  # Taskbar overlay window
│   ├── Services/                 # App-level services
│   ├── Settings/wwwroot/         # Svelte 5 frontend (Bun)
│   ├── Tray/                     # System tray icon
│   └── Win32/                    # P/Invoke, native interop
├── LocalTelemetry.Core/          # Hardware monitoring engine, models, config
│   ├── Hardware/                 # NVML, ADL, IGCL, WDDM, PawnIO drivers
│   ├── Config/                   # Configuration models
│   ├── Diagnostics/              # System diagnostics
│   └── Models/                   # Data models
└── LocalTelemetry.Notifier/      # Standalone toast notification helper (Named Pipes)

tests/
├── LocalTelemetry.App.Tests/     # xUnit + Vitest (Settings/wwwroot/)
├── LocalTelemetry.Core.Tests/    # xUnit
└── LocalTelemetry.Notifier.Tests/ # xUnit

docs/                             # VitePress documentation site
```

## Development Rules

### Frontend Standards

- Use **Bun** exclusively for frontend dependencies and scripts. Never use npm, yarn, or pnpm.
- Use **Svelte 5 runes** (`$state`, `$derived`, `$effect`, `$props`). Never use legacy reactive syntax (`$:`).
- Maintain TypeScript strict mode.

### C# and Backend Standards

- Use file-scoped namespaces.
- Use primary constructors where appropriate.
- Mark internal and concrete classes as `sealed` by default.
- Use `ILogger<T>` for structured logging.
- Use the `IOptions<T>` pattern for configuration.
- Add XML documentation comments to all public types and members.
- Never swallow exceptions. Every `catch` block must log, rethrow, or handle the error visibly.

### Zero Suppressions Policy

- Never suppress compiler, SonarLint, or Roslyn warnings.
- Do not use `<NoWarn>`, `#pragma warning disable`, or `[SuppressMessage]`.
- Always fix the underlying code directly.

### Documentation Standards

- Write documentation, explanations, and PR descriptions in ASD-STE100 Simplified Technical English.
- Keep sentences short and direct (maximum 20-25 words per sentence).
- Use active voice and consistent technical terms.

## Commit Messages

This project follows [Conventional Commits](https://www.conventionalcommits.org/en/v1.0.0/):

```bash
<type>(<scope>): <summary>
```

- **Types**: `feat`, `fix`, `chore`, `docs`, `refactor`, `test`, `ci`, `perf`, `style`, `build`
- **Scopes**: `core`, `app`, `overlay`, `monitor`, `config`, `ci`, `deps`
  - `app` covers the application shell, settings UI, and system tray.
  - Dependency updates use `build(deps)` or `chore(deps)`.

Examples:

```bash
feat(overlay): add GPU temperature display
fix(core): handle missing PawnIo driver gracefully
build(deps): bump NuGet packages
```

## Pull Requests

- Keep each pull request focused on one logical change.
- Use Squash and Merge for pull request integration.
- The Project Automation workflow labels pull requests and issues automatically.
- The workflow assigns Kind, Scope, and Sprint fields on Project #7 from the title and changed files.
- Dependabot pull requests receive automatic labels (`dependabot`, `dependencies`, `actions`, `scope:ci`).

## Verification

Before you submit a pull request, complete all of the following checks:

1. **Frontend build**:

   ```bash
   cd src/LocalTelemetry.App/Settings/wwwroot && bun run build
   ```

2. **Frontend tests**:

   ```bash
   cd tests/LocalTelemetry.App.Tests/Settings/wwwroot && bun run test
   ```

3. **Backend build**:

   ```bash
   dotnet build --configuration Debug
   ```

4. **Backend tests**:

   ```bash
   dotnet test --configuration Debug
   ```

5. **Format check**:

   ```bash
   dotnet format --verify-no-changes
   ```

6. **Code hygiene**:
   - Ensure no debug code, credentials, or secrets exist in the diff.
   - Confirm zero warning suppressions exist in changed files.

## Reporting Issues

- Use [GitHub Issues](https://github.com/Nicconike/LocalTelemetry/issues) to report bugs and suggest features.
- Search existing issues before you open a new issue.
- Include your Windows version, system specs, and clear reproduction steps.
