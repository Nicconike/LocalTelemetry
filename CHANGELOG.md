# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepa.changelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org) & [Conventional Commits](https://www.conventionalcommits.org/en/v1.0.0/).

## [1.0.0-rc.2](https://github.com/Nicconike/LocalTelemetry/compare/v1.0.0-rc.1...v1.0.0-rc.2) (2026-10-15)

### Bug Fixes

* **ci:** restore required status check names, project automation, and release configuration ([a547306](https://github.com/Nicconike/LocalTelemetry/commit/a547306141c556d0b247b74f236d1eb4bb551f14))
* **ci:** harden release pipeline, add verified bot commits and fix package feeds ([4ec76c2](https://github.com/Nicconike/LocalTelemetry/commit/4ec76c27271710c4f5452971354fb5ef4ed42a8e))

### Continuous Integration

* **workflows:** simplify workflow naming, add PR auto-sync and fix release pipeline ([f4d8533](https://github.com/Nicconike/LocalTelemetry/commit/f4d85338f08983e1e324c93196517dc87f541726))
* **release:** sequence manifest updates in prepare step and support single branch rc ([3b01ec6](https://github.com/Nicconike/LocalTelemetry/commit/3b01ec6474426c9b2f41a1b3c129d7798f270959))
* **release:** migrate release pipeline to semantic-release ([529130a](https://github.com/Nicconike/LocalTelemetry/commit/529130a93fa591d81c7b41ca0fb6281826b972e6))

# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepa.changelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org) & [Conventional Commits](https://www.conventionalcommits.org/en/v1.0.0/).

## 1.0.0-rc.1 (2026-10-13)

### Features

* **settings:** add per-group overlay color customization and navigation guard ([ebdd7ad](https://github.com/Nicconike/LocalTelemetry/commit/ebdd7adf6e79e7e7ff2737ad107d6bbafcdd3b25))
* **app:** implement hardware telemetry engine, taskbar overlay, settings UI, and packaging ([08c0d05](https://github.com/Nicconike/LocalTelemetry/commit/08c0d05f73416599159b935b062b92a84783d419))
* initial release of localtelemetry ([d0b783e](https://github.com/Nicconike/LocalTelemetry/commit/d0b783e154054f8539500c39c64da3e837408bce))
* Enhance installation and startup experience for LocalTelemetry ([b902bbb](https://github.com/Nicconike/LocalTelemetry/commit/b902bbbd475d8ee8a31fb320ff1a8db8eb0a31e8))

### Bug Fixes

* **core:** display dashes for offline network speeds and resolve CI workflows ([313daec](https://github.com/Nicconike/LocalTelemetry/commit/313daec3df5c3e3519d404dedd7d7f1da52d76da))
* **ci:** update frontend test command, change report paths ([e8baa04](https://github.com/Nicconike/LocalTelemetry/commit/e8baa047736cd26ff2b814c7728b21c1a7582be6))
* **ci:** resolve SonarQube vulnerabilities, workflow errors and docs favicon ([43a4cde](https://github.com/Nicconike/LocalTelemetry/commit/43a4cdeb505cdc04c94c4d10b911a386e92813ba))
* **ci:** harden release pipeline, sonarlint alerts, and project automation ([54125e0](https://github.com/Nicconike/LocalTelemetry/commit/54125e0973920aa96e3abc4586a1ea1a4009473e))
* resolve SonarQube issues and improve CI, uninstaller and app robustness; commit bun lockfiles ([b06c0f4](https://github.com/Nicconike/LocalTelemetry/commit/b06c0f484f994f3d0fd7a3f7be3b7506886c3b8d))
* resolve scorecard alerts, codeql false positives, and workflow failures ([2e8288b](https://github.com/Nicconike/LocalTelemetry/commit/2e8288b9d8a984f78ef4d9cf41aed72ac909ce05))
* harden release pipeline, unblock CI dependencies, and fix docs favicon ([85011b1](https://github.com/Nicconike/LocalTelemetry/commit/85011b131faa88756318adb07624ac1dae85b3b3))

### Documentation

* update changelog for v1.0.0-beta.1 ([a07d3ac](https://github.com/Nicconike/LocalTelemetry/commit/a07d3ac3f54880a97131f218030aa475558db293))

### Build System

* **deps-dev:** bump happy-dom from 18.0.1 to 20.11.1 in /tests/LocalTelemetry.App.Tests/Settings/wwwroot in the npm_and_yarn group across 1 directory ([#2](https://github.com/Nicconike/LocalTelemetry/issues/2)) ([7fb2104](https://github.com/Nicconike/LocalTelemetry/commit/7fb2104cf3f662d8de4539ca1bc54dacedadabd4))

### Continuous Integration

* **workflows:** simplify workflow naming, add PR auto-sync and fix release pipeline ([f4d8533](https://github.com/Nicconike/LocalTelemetry/commit/f4d85338f08983e1e324c93196517dc87f541726))
* **security:** update workflow configurations ([6eaccf5](https://github.com/Nicconike/LocalTelemetry/commit/6eaccf53422b1bc04172fb9e840a99aebe6be861))
* **release:** sequence manifest updates in prepare step and support single branch rc ([3b01ec6](https://github.com/Nicconike/LocalTelemetry/commit/3b01ec6474426c9b2f41a1b3c129d7798f270959))
* **release:** migrate release pipeline to semantic-release ([529130a](https://github.com/Nicconike/LocalTelemetry/commit/529130a93fa591d81c7b41ca0fb6281826b972e6))
* prevent release workflow self-trigger and stay on 1.x prereleases ([eb0f1ed](https://github.com/Nicconike/LocalTelemetry/commit/eb0f1edd6deb00eb25a97ac0093d18ced84ed293))
* generate release notes and changelog with git-cliff ([021cc85](https://github.com/Nicconike/LocalTelemetry/commit/021cc8523f98ad2942731cd02f74a593b53c4f76))
* fix invalid secrets context ([00b6380](https://github.com/Nicconike/LocalTelemetry/commit/00b63804a0053d9dc7d8894bcd16f0486e99845e))
* fix dependabot PR runs and sort changelog ([d827cf2](https://github.com/Nicconike/LocalTelemetry/commit/d827cf272f6adad8b8afdb5c7922882dcd1604d7))
* cache sonar scanner and fix token reference ([e7176e5](https://github.com/Nicconike/LocalTelemetry/commit/e7176e5f1ceba483d4a6ca1b9919656379ba1383))
* Add CommitLint ([e9add45](https://github.com/Nicconike/LocalTelemetry/commit/e9add453013f8dcfa8442d1ba6f3c653133b0118))
* add commitlint ([da8f4a0](https://github.com/Nicconike/LocalTelemetry/commit/da8f4a05a5ea33d259a27fe9a8ea7cb29e1ee64c))

### Chores

* **deps)(deps:** bump dotnet-sdk ([#1](https://github.com/Nicconike/LocalTelemetry/issues/1)) ([c1bc79e](https://github.com/Nicconike/LocalTelemetry/commit/c1bc79e09a10d73a9617a25b6c6619fa7b386ddc))
* **ci:** bump the actions-deps group across 1 directory with 4 updates ([#5](https://github.com/Nicconike/LocalTelemetry/issues/5)) ([34f1486](https://github.com/Nicconike/LocalTelemetry/commit/34f1486216f18b463f3dc88de5dff9257b8fedbf))
* Update Scoop manifest for v1.0.0-rc.1 [skip ci] ([43fc430](https://github.com/Nicconike/LocalTelemetry/commit/43fc4303da5a3fadfcc9084eabc217304d80519a))
* Update Changelog for v1.0.0-rc.1 [skip ci] ([cffb6ab](https://github.com/Nicconike/LocalTelemetry/commit/cffb6abc9e738ac6487403540b2bca814a347b6a))
* Update Changelog for v1.0.0-beta.4 [skip ci] ([e1e313b](https://github.com/Nicconike/LocalTelemetry/commit/e1e313bc50121a74c18e63ae17249fc6785d0f25))
* Update Changelog for v1.0.0-beta.3 [skip ci] ([2a9fd30](https://github.com/Nicconike/LocalTelemetry/commit/2a9fd3096db7016bd3609982a50d0acbda17e138))
* Update Changelog for v1.0.0-beta.2 [skip ci] ([dd88167](https://github.com/Nicconike/LocalTelemetry/commit/dd881677ac60d22abf59ebac29063ec28df4ce56))

# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/), this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html) and [Conventional Commits](https://www.conventionalcommits.org/).

## [1.0.0-rc.1] - 11-10-2026

### Features

- implement hardware telemetry engine, taskbar overlay, settings UI, and packaging (app) ([08c0d05](https://github.com/Nicconike/LocalTelemetry/commit/08c0d05f73416599159b935b062b92a84783d419))

### Bug Fixes

- harden release pipeline, sonarlint alerts, and project automation (ci) ([54125e0](https://github.com/Nicconike/LocalTelemetry/commit/54125e0973920aa96e3abc4586a1ea1a4009473e))
- display dashes for offline network speeds and resolve CI workflows (core) ([313daec](https://github.com/Nicconike/LocalTelemetry/commit/313daec3df5c3e3519d404dedd7d7f1da52d76da))
- resolve scorecard alerts, codeql false positives, and workflow failures ([2e8288b](https://github.com/Nicconike/LocalTelemetry/commit/2e8288b9d8a984f78ef4d9cf41aed72ac909ce05))
- harden release pipeline, unblock CI dependencies, and fix docs favicon ([85011b1](https://github.com/Nicconike/LocalTelemetry/commit/85011b131faa88756318adb07624ac1dae85b3b3))

### Build Systems

- bump the actions-deps group across 1 directory with 4 updates (#5) (ci) ([34f1486](https://github.com/Nicconike/LocalTelemetry/commit/34f1486216f18b463f3dc88de5dff9257b8fedbf))

## [1.0.0-beta.4] - 08-08-2026

### Bug Fixes

- resolve SonarQube vulnerabilities, workflow errors and docs favicon (ci) ([43a4cde](https://github.com/Nicconike/LocalTelemetry/commit/43a4cdeb505cdc04c94c4d10b911a386e92813ba))

## [1.0.0-beta.3] - 08-08-2026

### Bug Fixes

- resolve SonarQube issues and improve CI, uninstaller and app robustness; commit bun lockfiles ([b06c0f4](https://github.com/Nicconike/LocalTelemetry/commit/b06c0f484f994f3d0fd7a3f7be3b7506886c3b8d))

## [1.0.0-beta.2] - 07-08-2026

### Features

- Enhance installation and startup experience for LocalTelemetry ([b902bbb](https://github.com/Nicconike/LocalTelemetry/commit/b902bbbd475d8ee8a31fb320ff1a8db8eb0a31e8))

### Documentation

- update changelog for v1.0.0-beta.1 ([a07d3ac](https://github.com/Nicconike/LocalTelemetry/commit/a07d3ac3f54880a97131f218030aa475558db293))

### Build Systems

- chore(deps)(deps): bump dotnet-sdk (#1) ([c1bc79e](https://github.com/Nicconike/LocalTelemetry/commit/c1bc79e09a10d73a9617a25b6c6619fa7b386ddc))

## [1.0.0-beta.1] - 06-08-2026

### Features

- add per-group overlay color customization and navigation guard (settings) ([ebdd7ad](https://github.com/Nicconike/LocalTelemetry/commit/ebdd7adf6e79e7e7ff2737ad107d6bbafcdd3b25))

### Continuous Integration

- fix invalid secrets context ([00b6380](https://github.com/Nicconike/LocalTelemetry/commit/00b63804a0053d9dc7d8894bcd16f0486e99845e))
- fix dependabot PR runs and sort changelog ([d827cf2](https://github.com/Nicconike/LocalTelemetry/commit/d827cf272f6adad8b8afdb5c7922882dcd1604d7))
- generate release notes and changelog with git-cliff ([021cc85](https://github.com/Nicconike/LocalTelemetry/commit/021cc8523f98ad2942731cd02f74a593b53c4f76))
- cache sonar scanner and fix token reference ([e7176e5](https://github.com/Nicconike/LocalTelemetry/commit/e7176e5f1ceba483d4a6ca1b9919656379ba1383))
- prevent release workflow self-trigger and stay on 1.x prereleases ([eb0f1ed](https://github.com/Nicconike/LocalTelemetry/commit/eb0f1edd6deb00eb25a97ac0093d18ced84ed293))
- Add CommitLint ([e9add45](https://github.com/Nicconike/LocalTelemetry/commit/e9add453013f8dcfa8442d1ba6f3c653133b0118))

## [1.0.0-beta.0] - 03-08-2026

### Features

- initial release of localtelemetry ([d0b783e](https://github.com/Nicconike/LocalTelemetry/commit/d0b783e154054f8539500c39c64da3e837408bce))

### Continuous Integration

- update workflow configurations (security) ([6eaccf5](https://github.com/Nicconike/LocalTelemetry/commit/6eaccf53422b1bc04172fb9e840a99aebe6be861))

### Bug Fixes

- fix(ci): update frontend test command, change report paths ([e8baa04](https://github.com/Nicconike/LocalTelemetry/commit/e8baa047736cd26ff2b814c7728b21c1a7582be6))

### Build Systems

- bump happy-dom from 18.0.1 to 20.11.1 in /tests/LocalTelemetry.App.Tests/Settings/wwwroot in the npm_and_yarn group across 1 directory (#2) (deps-dev) ([7fb2104](https://github.com/Nicconike/LocalTelemetry/commit/7fb2104cf3f662d8de4539ca1bc54dacedadabd4))

[1.0.0-rc.1]: https://github.com/Nicconike/LocalTelemetry/compare/v1.0.0-beta.4...v1.0.0-rc.1
[1.0.0-beta.4]: https://github.com/Nicconike/LocalTelemetry/compare/v1.0.0-beta.3...v1.0.0-beta.4
[1.0.0-beta.3]: https://github.com/Nicconike/LocalTelemetry/compare/v1.0.0-beta.2...v1.0.0-beta.3
[1.0.0-beta.2]: https://github.com/Nicconike/LocalTelemetry/compare/v1.0.0-beta.1...v1.0.0-beta.2
[1.0.0-beta.1]: https://github.com/Nicconike/LocalTelemetry/compare/v1.0.0-beta.0...v1.0.0-beta.1
[1.0.0-beta.0]: https://github.com/Nicconike/LocalTelemetry/tree/v1.0.0-beta.0
