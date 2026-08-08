# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/), this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html) and [Conventional Commits](https://www.conventionalcommits.org/).

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

[1.0.0-beta.3]: https://github.com/Nicconike/LocalTelemetry/compare/v1.0.0-beta.2...v1.0.0-beta.3
[1.0.0-beta.2]: https://github.com/Nicconike/LocalTelemetry/compare/v1.0.0-beta.1...v1.0.0-beta.2
[1.0.0-beta.1]: https://github.com/Nicconike/LocalTelemetry/compare/v1.0.0-beta.0...v1.0.0-beta.1
[1.0.0-beta.0]: https://github.com/Nicconike/LocalTelemetry/tree/v1.0.0-beta.0

