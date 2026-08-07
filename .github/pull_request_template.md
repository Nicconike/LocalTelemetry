## Summary

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

> The Project Automation workflow labels PRs/issues and assigns Kind, Scope & Sprint fields on Project #7 from the PR title and changed files. Dependabot PRs are auto-labeled: package bumps get `dependabot` + `dependencies` (Kind Build), GitHub Actions bumps get `dependabot` + `actions` + `scope:ci` (Kind CI); scope is derived from the files they touch.

## Changes

-

## Related Issues

<!-- Closes #123, Fixes #456 -->

## Verification

- [ ] Frontend builds (`cd src/LocalTelemetry.App/Settings/wwwroot && bun run build`)
- [ ] Backend builds (`dotnet build`)
- [ ] No debug code or secrets in the diff
- [ ] PR title follows [Conventional Commits](https://www.conventionalcommits.org/en/v1.0.0/)
