# Summary

<!-- Brief description of the changes in this PR -->

## Type of Change

<!-- PR title MUST follow Conventional Commits: <type>(<scope>): <summary> -->
<!-- Examples: feat(overlay): add GPU temp display, fix(core): handle missing driver, build(deps): bump packages -->

- [ ] `feat` - New feature
- [ ] `fix` - Bug fix
- [ ] `refactor` - Code restructuring (no behavior change)
- [ ] `build` - Build system or dependency changes
- [ ] `perf` - Performance improvement
- [ ] `docs` - Documentation
- [ ] `ci` - CI/CD changes
- [ ] `test` - Tests
- [ ] `style` - Formatting, no behavior change
- [ ] `chore` - Maintenance (config, tooling)

**Scope**: `core` | `app` | `overlay` | `monitor` | `config` | `ci`

Scope map: `core` → Core, `app` → App (includes `settings`/`tray` UI), `overlay` → Overlay, `monitor` → Monitor, `config` → Config, `ci` → CI (`.github/**`, `global.json`). Dependency bumps use `build(deps)` / `chore(deps)`.

> The Project Automation workflow labels PRs and issues. The workflow assigns Kind, Scope, and Sprint fields on Project #7 from the title and modified files. Dependabot PRs receive automatic labels. Package updates receive `dependabot` and `dependencies` labels (Kind Build). GitHub Actions updates receive `dependabot`, `actions`, and `scope:ci` labels (Kind CI). The automation derives scope from modified files.

## Changes

-

## Related Issues

<!-- Closes #123, Fixes #456 -->

## Verification

- [ ] Frontend builds (`cd src/LocalTelemetry.App/Settings/wwwroot && bun run build`)
- [ ] Frontend tests pass (`cd tests/LocalTelemetry.App.Tests/Settings/wwwroot && bun run test`)
- [ ] Backend builds (`dotnet build --configuration Debug`)
- [ ] Backend tests pass (`dotnet test --configuration Debug`)
- [ ] Code formatting check passes (`dotnet format --verify-no-changes`)
- [ ] No compiler or analyzer warning suppressions added (Zero Suppressions Policy)
- [ ] No debug code or secrets in the diff
- [ ] PR title & commit message follow [Conventional Commits](https://www.conventionalcommits.org)
