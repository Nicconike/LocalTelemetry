# CI/CD Pipelines & GitHub Workflows

LocalTelemetry uses **GitHub Actions** for continuous integration, code quality scanning, automated builds, documentation deployment, and release packaging.

All workflow actions strictly follow OpenSSF Scorecard supply-chain security standards by pinning 40-character commit SHAs.

---

## ⚙️ Workflows Breakdown

All workflow definition files are located in `.github/workflows/`:

```
.github/workflows/
├── ci.yml          # Continuous Integration build & test pipeline
├── release.yml     # Automated Semantic Release & SLSA Attestation generation
├── docs.yml        # GitHub Pages documentation deployment
├── codeql.yml      # CodeQL static security analysis
├── scorecard.yml   # OpenSSF Scorecard supply-chain security audit
├── sonar.yml       # SonarQube / SonarCloud static analysis
└── auto-label.yml  # PR auto-labeling for automated bots
```

---

## 1. Continuous Integration (`ci.yml`)

Triggers on:
- Every `push` to `master` branch.
- Every `pull_request` targeting `master`.

### Workflow Steps
1. **Checkout Code**: Uses `actions/checkout@3d3c42e5...` with full git history (`fetch-depth: 0`).
2. **Setup Node & Bun**: Installs Bun runtime (`oven-sh/setup-bun@0c5077e5...`).
3. **Frontend Unit Tests**: Runs Vitest unit tests in `tests/LocalTelemetry.App.Tests/Settings/wwwroot`.
4. **Compile Svelte SPA**: Runs `bun install` & `bun run build` in `src/LocalTelemetry.App/Settings/wwwroot`.
5. **Verify Commit Messages**: Runs `commitlint` (`wagoid/commitlint-github-action@b948419d...`) against `.commitlintrc.json`.
6. **Setup .NET 10**: Configures .NET 10.0.x SDK (`actions/setup-dotnet@a98b5685...`).
7. **Verify Code Formatting**: Executes `dotnet format --verify-no-changes`.
8. **Build Solution**: Executes `dotnet build --configuration Debug`.
9. **Run Unit Tests & Coverage**: Executes `dotnet test` collecting cross-platform code coverage.
10. **Upload Coverage**: Uploads code coverage reports to **Codecov** (`codecov/codecov-action@fb8b3582...`).

---

## 2. Release Workflow (`release.yml`)

Triggers on:
- Pushing commits to `master` branch.

Permissions required:
- `contents: write`, `issues: write`, `pull-requests: write`, `id-token: write` (for SLSA attestations), `attestations: write`.

### Workflow Steps
1. **GitHub App Authentication**: Generates a GitHub App installation token (`actions/create-github-app-token@bcd2ba49...`) for committing to protected branches.
2. **Frontend Build**: Compiles Svelte 5 frontend with Bun.
3. **Calculate Semantic Tag**: Uses `mathieudutour/github-tag-action@a22cf086...` with Conventional Commits parsing (`custom_release_tag: v1.0.0-beta.1`).
4. **Publish .NET Binaries**: Publishes `LocalTelemetry.App` single-file executable targeting `win-x64`.
5. **Compile Inno Setup Installer**: Installs Inno Setup 6 via Chocolatey (`choco install innosetup`) and executes `ISCC.exe setup.iss` to produce `LocalTelemetrySetup.exe`.
6. **Package Portable Archive & Checksums**: Packages `LocalTelemetry-win-x64.zip` and generates SHA-256 hash checksums in `checksums.txt`.
7. **SLSA Artifact Attestations**: Generates cryptographic build attestations using `actions/attest@508db95d...`.
8. **Publish GitHub Release**: Uploads release assets to GitHub Releases using `softprops/action-gh-release@3d0d9888...`.

---

## 3. Documentation Deployment (`docs.yml`)

Triggers on:
- Pushes to `master` branch or manual `workflow_dispatch`.

### Workflow Steps
1. Installs documentation dependencies in `docs/` via Bun (`bun install`).
2. Builds VitePress static site with `bun run docs:build`, passing `GA_MEASUREMENT_ID` environment secret.
3. Uploads VitePress output artifact (`docs/.vitepress/dist`).
4. Deploys static site to **GitHub Pages** using `actions/deploy-pages@d6db9016...`.

---

## 4. Security & Quality Analysis Workflows

### CodeQL Security Scan (`codeql.yml`)
- Runs automated static code analysis scanning C# code for security vulnerabilities using `github/codeql-action/analyze@f205ea1c...`.

### OpenSSF Scorecard Analysis (`scorecard.yml`)
- Performs weekly and on-push supply-chain security analysis via `ossf/scorecard-action@2d114668...`, uploading SARIF results to GitHub Security Code Scanning.

### SonarQube Quality Scan (`sonar.yml`)
- Executes SonarScanner for .NET analyzing code complexity, duplication, and code smells.
