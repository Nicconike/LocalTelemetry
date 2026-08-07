# CI/CD Pipelines & GitHub Workflows

LocalTelemetry uses **GitHub Actions** for continuous integration, code quality scanning, automated builds, documentation deployment, and release packaging.

All workflow actions strictly follow OpenSSF Scorecard supply-chain security standards by pinning 40-character commit SHAs.

---

## ⚙️ Workflows Breakdown

All workflow definition files are located in `.github/workflows/`:

```
.github/workflows/
├── ci.yml          # Continuous Integration build & test pipeline
├── release.yml     # Automated Release & provenance attestation generation
├── docs.yml        # GitHub Pages documentation deployment
├── codeql.yml      # CodeQL static security analysis
├── scorecard.yml   # OpenSSF Scorecard supply-chain security audit
├── sonar.yml       # SonarQube / SonarCloud static analysis
└── project.yml     # Project Automation (labels + Project #7 board fields)
```

---

## 1. Continuous Integration (`ci.yml`)

Triggers on:
- Every `push` to `master` branch.
- Every `pull_request` targeting `master`.

The workflow is split into two jobs:

### CommitLint Job
1. **Checkout Code**: Uses `actions/checkout` with full git history (`fetch-depth: 0`).
2. **Verify Conventional Commit Messages**: Runs `commitlint` (`wagoid/commitlint-github-action`) against `commitlint.config.mjs`.

### CI Job (Windows)
1. **Checkout Code**: Uses `actions/checkout` with full git history (`fetch-depth: 0`).
2. **Setup Node & Bun**: Installs the Bun runtime (`oven-sh/setup-bun`).
3. **Frontend Unit Tests**: Runs Vitest unit tests in `tests/LocalTelemetry.App.Tests/Settings/wwwroot`, emitting a JUnit report consumed by **Codecov Test Analytics**.
4. **Compile Svelte SPA**: Runs `bun install` & `bun run build` in `src/LocalTelemetry.App/Settings/wwwroot`.
5. **Cache .NET Packages**: Caches `~/.nuget/packages` (`actions/cache`).
6. **Setup .NET 10**: Configures .NET 10.0.x SDK (`actions/setup-dotnet`).
7. **Verify C# Code Formatting**: Executes `dotnet format --verify-no-changes`.
8. **Build Solution**: Executes `dotnet build --configuration Debug`.
9. **Run Unit Tests & Coverage**: Runs `dotnet test` per test project (Core, Notifier, App), collecting XPlat Cobertura coverage and JUnit test results for **Codecov Test Analytics**.
10. **Upload Coverage**: Uploads each project's coverage to **Codecov** under a dedicated flag (`core`, `notifier`, `app`).
11. **Upload Test Results**: Uploads JUnit reports - including the Vitest report from the frontend tests - to **Codecov Test Analytics** (`report_type: test_results`).
12. **Upload Artifacts**: Stores the raw test results & coverage as workflow artifacts.

---

## 2. Project Automation (`project.yml`)

Triggers on:
- `issues` opened.
- `pull_request` opened.
- Manual `workflow_dispatch` (backfills all open issues & PRs).

Authenticates with the `PROJECTS` secret (falling back to `PROJECTS_PAT`, then `GITHUB_TOKEN`) using a pinned `actions/github-script` SHA.

For every issue/PR it:
1. **Labels**: Assigns labels from the Conventional Commit type in the title (`feat` → `enhancement`, `fix` → `bug`, `perf` → `perf`, `docs` → `documentation`, etc.). `revert` is intentionally unlabeled so the changelog groups it under *Reverts*.
2. **Scope**: Detects the board Scope (Core, App, Overlay, Monitor, Config, CI) from the changed files (`pulls.listFiles`) and title scope markers, tags every matching `scope:*` label, and sets the single-select Scope field using precedence Core > App > Overlay > Monitor > Config > CI.
3. **Dependabot**: `dependabot[bot]` PRs are auto-labeled by dependabot and re-verified here. Package bumps (NuGet, Bun, dotnet-sdk) get `dependabot` + `dependencies` and Kind `Build`; GitHub Actions bumps get `dependabot` + `actions` + `scope:ci` and Kind `CI`. Scope is always derived from the files they touch (`.github/**`/`global.json` → CI, `*.csproj` → their project, `wwwroot/**` → App).
4. **Board**: Adds the item to Project #7 and sets the **Kind** field from the title, the **Scope** field, and the **Sprint** field (active → next future iteration → creates a new 14-day iteration if none exist).

The changelog (`git-cliff`, `cliff.toml`) groups commits by these same labels so PR labels, Project #7 fields, and CHANGELOG sections stay consistent.

---

## 3. Release Workflow (`release.yml`)

Triggers on pushes to `master` that touch release-relevant paths (`setup.iss`, `src/LocalTelemetry.App/**`, `src/LocalTelemetry.Core/**`, `src/LocalTelemetry.Notifier/**`).

Job-level permissions:
- `contents: read`, `id-token: write` and `attestations: write` (for provenance attestations).

### Workflow Steps
1. **GitHub App Authentication**: Generates a GitHub App installation token (`actions/create-github-app-token`) for committing to protected branches.
2. **Checkout Code**: Full history (`fetch-depth: 0`) using the app token.
3. **Frontend Build**: Compiles the Svelte 5 frontend with Bun.
4. **Calculate Version Tag**: Uses `mathieudutour/github-tag-action` to derive the next version from Conventional Commits since the last tag (Semantic Versioning, `v` prefix). Tag creation is a no-op when there are no new conventional commits.
5. **Fetch Release Tag**: Fetches the freshly created tag so MinVer resolves the exact release version.
6. **Publish .NET Binaries**: Publishes `LocalTelemetry.App` as a single-file executable targeting `win-x64`, overriding MinVer with the tagged version so the About page and file versions match the release.
7. **Compile Inno Setup Installer**: Installs **Inno Setup 7** via Chocolatey and executes `ISCC.exe setup.iss` with the release version defines, producing `LocalTelemetrySetup.exe` in the repository root.
8. **Package Portable Archive & Checksums**: Packages `LocalTelemetry-win-x64.zip` and generates SHA-256 hashes in `checksums.txt`.
9. **Provenance Attestations**: Generates cryptographic build provenance attestations for the installer and portable archive using `actions/attest`.
10. **Publish GitHub Release**: Uploads the installer, portable archive and checksums to GitHub Releases (`softprops/action-gh-release`).
11. **Update CHANGELOG**: Prepends the generated changelog to `CHANGELOG.md` and commits it back to `master` as `protected-auto-commits[bot]`.

---

## 4. Documentation Deployment (`docs.yml`)

Triggers on:
- Pushes to `master` branch or manual `workflow_dispatch`.

### Workflow Steps
1. Installs documentation dependencies in `docs/` via Bun (`bun install`).
2. Builds the VitePress static site with `bun run docs:build`, passing the `GA_MEASUREMENT_ID` environment secret.
3. Uploads the VitePress output artifact (`docs/.vitepress/dist`).
4. Deploys the static site to **GitHub Pages** using `actions/deploy-pages`.

---

## 5. Security & Quality Analysis Workflows

### CodeQL Security Scan (`codeql.yml`)
- Runs automated static code analysis scanning C# code for security vulnerabilities using `github/codeql-action`.

### OpenSSF Scorecard Analysis (`scorecard.yml`)
- Runs supply-chain security analysis on `master` pushes and on branch-protection rule events, uploading SARIF results to GitHub Security Code Scanning.

### SonarQube Quality Scan (`sonar.yml`)
- Executes SonarScanner for .NET analyzing code complexity, duplication, and code smells.
