# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com), and this project adheres to [Semantic Versioning](https://semver.org) & [Conventional Commits](https://conventionalcommits.org).

## [1.1.0](https://github.com/Nicconike/LocalTelemetry/compare/v1.0.0...v1.1.0) (2026-10-24)

### Features

* **overlay,hardware:** support left-aligned taskbar placement, AMD SMN telemetry, and selective uninstaller ([124d302](https://github.com/Nicconike/LocalTelemetry/commit/124d3025252fb46d551f4fb9ca4021dadb3be990))

### Code Refactoring

* decompose core hardware services and expand frontend test coverage ([c1a3a39](https://github.com/Nicconike/LocalTelemetry/commit/c1a3a39310384fd97b2168a5e448aa32894e5d69))

### Build System

* **deps-dev:** Bump mermaid from 11.17.2 to 12.2.0 ([0ffc03c](https://github.com/Nicconike/LocalTelemetry/commit/0ffc03ce7690273ce50ae9d71027ede7ed841f22))
* **deps:** remediate frontend vulnerabilities and register all bun ecosystems ([3a9692d](https://github.com/Nicconike/LocalTelemetry/commit/3a9692d9ed455325181632132fd081f8a582e2e3))
* **deps:** bump the frontend-deps group across 1 directory with 5 updates ([#21](https://github.com/Nicconike/LocalTelemetry/issues/21)) ([b34a864](https://github.com/Nicconike/LocalTelemetry/commit/b34a86476f4df6b8ce8568df26eee5650963732e))
* **deps:** bump dompurify to 3.4.17 to remediate security vulns ([903b95a](https://github.com/Nicconike/LocalTelemetry/commit/903b95aadd99bb7cc93b5b5d331f129ff7f80a72))
* **deps:** Bump coverlet.collector and 17 others ([178d172](https://github.com/Nicconike/LocalTelemetry/commit/178d172be482957cba51d5f7f3b4f905d87b9c18))
* **deps:** Bump @lucide/svelte from 1.58.0 to 1.59.0 in /src/LocalTelemetry.App/Settings/wwwroot in the frontend-deps group ([7661f3e](https://github.com/Nicconike/LocalTelemetry/commit/7661f3e7d501f4fc6cba3d4f3d92eecab418ddae))
* align TFM to Windows SDK, pin Xunit.StaFact, and fix CI test resolution ([80a9a7f](https://github.com/Nicconike/LocalTelemetry/commit/80a9a7f0898a65f5fc2a7fa4b7200449d22d0e5f))

### Continuous Integration

* **release:** harden Inno Setup 7 install, automate project lifecycle, and ignore patch updates ([693b34b](https://github.com/Nicconike/LocalTelemetry/commit/693b34bbac80ed88524b332a068e290cd7662a14))
* **frontend:** forward codecov token and raise frontend test coverage above 90% ([1d32c1e](https://github.com/Nicconike/LocalTelemetry/commit/1d32c1e1b58e106a895660ddecbdbac8bc1d638a))
* **deps-dev:** Bump the discord-deps group ([ea8dd7c](https://github.com/Nicconike/LocalTelemetry/commit/ea8dd7c154b99e5f602f97dd235a4d1a98a0830c))
* **deps:** bump the actions-deps group across 1 directory with 9 updates ([#20](https://github.com/Nicconike/LocalTelemetry/issues/20)) ([0b5aedd](https://github.com/Nicconike/LocalTelemetry/commit/0b5aedd54a47b41b35d1eee686af90d28b911401))
* trim path and group suffixes from Dependabot PR titles and update docs lockfile ([8be4bcb](https://github.com/Nicconike/LocalTelemetry/commit/8be4bcb240b7e783cd8055f5bd16fc79c62d9c82))
* prevent premature PR merging and enforce all required status checks ([ea8d880](https://github.com/Nicconike/LocalTelemetry/commit/ea8d88064d07fc8cb3f5a30ca1c02f4da829c85f))
* pin workflow deps, attach release provenance, and update tests ([e4c6c7e](https://github.com/Nicconike/LocalTelemetry/commit/e4c6c7e0e8df5571841fd6ed51f23b5e06b39cfc))
* configure explicit path triggers across workflows ([40e5f5f](https://github.com/Nicconike/LocalTelemetry/commit/40e5f5f33d7e6fe28edb0ba93de00870cd69645c))
* configure codecov bundle analysis and expand mock unit tests ([4e41952](https://github.com/Nicconike/LocalTelemetry/commit/4e419520a18e1157ef709f230353dcb4fedd9c07))

### Tests

* **hardware,ui:** add mock unit tests ([2e617da](https://github.com/Nicconike/LocalTelemetry/commit/2e617da3f378ecda1fd46350f324034ec7a97023))
* **hardware:** isolate elevation checks for PawnIO device tests ([0ffbd4b](https://github.com/Nicconike/LocalTelemetry/commit/0ffbd4bcc3da3f9a2835d7f42b1eb03169456018))
* **coverage:** prevent duplicate sync workflow runs and expand unit tests ([59e8371](https://github.com/Nicconike/LocalTelemetry/commit/59e837139e82986f76348c6eedbe7480dbddf9c2))
* **coverage:** increase backend unit test coverage across Core and App ([c5059c3](https://github.com/Nicconike/LocalTelemetry/commit/c5059c33e8752926a11244dfd2b30b4d00c753d2))
* **coverage:** expand App and Core test coverage and update test dev dependencies ([c61fd0b](https://github.com/Nicconike/LocalTelemetry/commit/c61fd0b0d66277b65fd9ce391779dee1a3d48a00))

## [1.0.0](https://github.com/Nicconike/LocalTelemetry/compare/v1.0.0-rc.2...v1.0.0) (2026-10-19)

### Bug Fixes

* **taskbar:** resolve overlay alignment offsets, release workflows, and analyzer issues ([265d8b9](https://github.com/Nicconike/LocalTelemetry/commit/265d8b928ea16d6e65c07a9aa2ecb614e0671f2a))

### Continuous Integration

* resolve project automation graphql syntax and release workflow conditionals ([08f8e25](https://github.com/Nicconike/LocalTelemetry/commit/08f8e25341f199cc98a1083433486fbe5d283da2))

## [1.0.0-rc.2](https://github.com/Nicconike/LocalTelemetry/compare/v1.0.0-rc.1...v1.0.0-rc.2) (2026-10-15)

### Bug Fixes

* **ci:** restore required status check names, project automation, and release configuration ([a547306](https://github.com/Nicconike/LocalTelemetry/commit/a547306141c556d0b247b74f236d1eb4bb551f14))
* **ci:** harden release pipeline, add verified bot commits and fix package feeds ([4ec76c2](https://github.com/Nicconike/LocalTelemetry/commit/4ec76c27271710c4f5452971354fb5ef4ed42a8e))

### Continuous Integration

* **workflows:** simplify workflow naming, add PR auto-sync and fix release pipeline ([f4d8533](https://github.com/Nicconike/LocalTelemetry/commit/f4d85338f08983e1e324c93196517dc87f541726))
* **release:** sequence manifest updates in prepare step and support single branch rc ([3b01ec6](https://github.com/Nicconike/LocalTelemetry/commit/3b01ec6474426c9b2f41a1b3c129d7798f270959))
* **release:** migrate release pipeline to semantic-release ([529130a](https://github.com/Nicconike/LocalTelemetry/commit/529130a93fa591d81c7b41ca0fb6281826b972e6))

## [1.0.0-rc.1](https://github.com/Nicconike/LocalTelemetry/compare/v1.0.0-beta.4...v1.0.0-rc.1) (2026-10-11)

### Features

* **app:** implement hardware telemetry engine, taskbar overlay, settings UI, and packaging ([08c0d05](https://github.com/Nicconike/LocalTelemetry/commit/08c0d05f73416599159b935b062b92a84783d419))

### Bug Fixes

* **core:** display dashes for offline network speeds and resolve CI workflows ([313daec](https://github.com/Nicconike/LocalTelemetry/commit/313daec3df5c3e3519d404dedd7d7f1da52d76da))
* **ci:** harden release pipeline, sonarlint alerts, and project automation ([54125e0](https://github.com/Nicconike/LocalTelemetry/commit/54125e0973920aa96e3abc4586a1ea1a4009473e))
* resolve scorecard alerts, codeql false positives, and workflow failures ([2e8288b](https://github.com/Nicconike/LocalTelemetry/commit/2e8288b9d8a984f78ef4d9cf41aed72ac909ce05))
* harden release pipeline, unblock CI dependencies, and fix docs favicon ([85011b1](https://github.com/Nicconike/LocalTelemetry/commit/85011b131faa88756318adb07624ac1dae85b3b3))

## [1.0.0-beta.4](https://github.com/Nicconike/LocalTelemetry/compare/v1.0.0-beta.3...v1.0.0-beta.4) (2026-08-08)

### Bug Fixes

* **ci:** resolve SonarQube vulnerabilities, workflow errors and docs favicon ([43a4cde](https://github.com/Nicconike/LocalTelemetry/commit/43a4cdeb505cdc04c94c4d10b911a386e92813ba))

## [1.0.0-beta.3](https://github.com/Nicconike/LocalTelemetry/compare/v1.0.0-beta.2...v1.0.0-beta.3) (2026-08-08)

### Bug Fixes

* resolve SonarQube issues and improve CI, uninstaller and app robustness; commit bun lockfiles ([b06c0f4](https://github.com/Nicconike/LocalTelemetry/commit/b06c0f484f994f3d0fd7a3f7be3b7506886c3b8d))

## [1.0.0-beta.2](https://github.com/Nicconike/LocalTelemetry/compare/v1.0.0-beta.1...v1.0.0-beta.2) (2026-08-08)

### Features

* Enhance installation and startup experience for LocalTelemetry ([b902bbb](https://github.com/Nicconike/LocalTelemetry/commit/b902bbbd475d8ee8a31fb320ff1a8db8eb0a31e8))

### Documentation

* update changelog for v1.0.0-beta.1 ([a07d3ac](https://github.com/Nicconike/LocalTelemetry/commit/a07d3ac3f54880a97131f218030aa475558db293))

## [1.0.0-beta.1](https://github.com/Nicconike/LocalTelemetry/compare/v1.0.0-beta.0...v1.0.0-beta.1) (2026-08-06)

### Features

* **settings:** add per-group overlay color customization and navigation guard ([ebdd7ad](https://github.com/Nicconike/LocalTelemetry/commit/ebdd7adf6e79e7e7ff2737ad107d6bbafcdd3b25))

### Continuous Integration

* prevent release workflow self-trigger and stay on 1.x prereleases ([eb0f1ed](https://github.com/Nicconike/LocalTelemetry/commit/eb0f1edd6deb00eb25a97ac0093d18ced84ed293))
* generate release notes and changelog with git-cliff ([021cc85](https://github.com/Nicconike/LocalTelemetry/commit/021cc8523f98ad2942731cd02f74a593b53c4f76))
* fix invalid secrets context ([00b6380](https://github.com/Nicconike/LocalTelemetry/commit/00b63804a0053d9dc7d8894bcd16f0486e99845e))
* fix dependabot PR runs and sort changelog ([d827cf2](https://github.com/Nicconike/LocalTelemetry/commit/d827cf272f6adad8b8afdb5c7922882dcd1604d7))
* cache sonar scanner and fix token reference ([e7176e5](https://github.com/Nicconike/LocalTelemetry/commit/e7176e5f1ceba483d4a6ca1b9919656379ba1383))
* Add CommitLint ([e9add45](https://github.com/Nicconike/LocalTelemetry/commit/e9add453013f8dcfa8442d1ba6f3c653133b0118))
* add commitlint ([da8f4a0](https://github.com/Nicconike/LocalTelemetry/commit/da8f4a05a5ea33d259a27fe9a8ea7cb29e1ee64c))

## 1.0.0-beta.0 (2026-08-03)

### Features

* initial release of localtelemetry ([d0b783e](https://github.com/Nicconike/LocalTelemetry/commit/d0b783e154054f8539500c39c64da3e837408bce))

### Bug Fixes

* **ci:** update frontend test command, change report paths ([e8baa04](https://github.com/Nicconike/LocalTelemetry/commit/e8baa047736cd26ff2b814c7728b21c1a7582be6))

### Build System

* **deps-dev:** bump happy-dom from 18.0.1 to 20.11.1 in /tests/LocalTelemetry.App.Tests/Settings/wwwroot in the npm_and_yarn group across 1 directory ([#2](https://github.com/Nicconike/LocalTelemetry/issues/2)) ([7fb2104](https://github.com/Nicconike/LocalTelemetry/commit/7fb2104cf3f662d8de4539ca1bc54dacedadabd4))

### Continuous Integration

* **security:** update workflow configurations ([6eaccf5](https://github.com/Nicconike/LocalTelemetry/commit/6eaccf53422b1bc04172fb9e840a99aebe6be861))
