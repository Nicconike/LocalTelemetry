# Contributing to LocalTelemetry

Thanks for your interest in contributing to LocalTelemetry!

## Prerequisites

- Windows 10/11 (x64)
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Bun](https://bun.sh/) (for frontend builds)
- [Visual Studio 2022+](https://visualstudio.microsoft.com/) or [VS Code](https://code.visualstudio.com/)

## Getting Started

1. Fork and clone the repository
2. Install frontend dependencies:
    ```
    cd src/LocalTelemetry.App/Settings/wwwroot
    bun install
    ```
3. Build the frontend:
    ```
    bun run build
    ```
4. Build the backend from solution root:
    ```
    dotnet build
    ```
5. Run the backend from solution root:
    ```
    dotnet run --project src\LocalTelemetry.App
    ```

## Project Structure

```
src/
├── LocalTelemetry.Core/     # Hardware monitoring, no UI dependency
├── LocalTelemetry.App/      # WPF app, WebView2 settings, tray, overlay
│   └── Settings/wwwroot/    # Svelte 5 frontend (Bun + Vite)
├── LocalTelemetry.Notifier/ # Standalone toast notification helper
docs/                        # VitePress documentation site
```

## Development Rules

- **Bun only** for frontend - never npm/yarn/pnpm
- **Svelte 5 runes** - `$state()`, `$derived()`, `$effect()`, no legacy `$:`
- **`sealed` by default** for C# classes
- **No `var` abuse** - only when type is obvious from RHS
- **XML docs** on all public types/members
- **Never swallow exceptions** - every `catch` must log, rethrow or handle visibly

## Commit Messages

This project follows [Conventional Commits](https://www.conventionalcommits.org/en/v1.0.0/):

```
<type>(<scope>): <summary>
```

**Types**: `feat`, `fix`, `chore`, `docs`, `refactor`, `test`, `ci`, `perf`, `style`, `build`

**Scopes**: `core`, `app`, `overlay`, `monitor`, `config`, `ci` (`app` covers the `settings`/`tray` UI; dependency bumps use `chore(deps)` / `build(deps)`)

Examples:
```
feat(overlay): add GPU temperature display
fix(core): handle missing PawnIo driver gracefully
build(deps): bump NuGet packages
```

## Pull Requests

- One logical change per PR
- Squash and merge
- PRs/issues are auto-labeled and assigned to Project #7 by the [Project Automation workflow](workflows/project.yml): Kind/Scope/Sprint fields come from the PR title and changed files. Dependabot PRs are auto-labeled: package bumps get `dependabot` + `dependencies` (Kind Build), GitHub Actions bumps get `dependabot` + `actions` + `scope:ci` (Kind CI).

## Verification

Before submitting a PR, ensure:

1. **Frontend builds**: `cd src/LocalTelemetry.App/Settings/wwwroot && bun run build`
2. **Backend builds**: `dotnet build` from solution root
3. **No debug code or secrets** in the diff

## Reporting Issues

- Use [GitHub Issues](https://github.com/Nicconike/LocalTelemetry/issues)
- Search existing issues before creating a new one
- Include your Windows version, GPU model and steps to reproduce
