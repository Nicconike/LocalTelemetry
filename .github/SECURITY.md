# Security Policy

## Supported Versions

| Version                         | Supported |
| ------------------------------- | --------- |
| Latest release                  | ✅        |
| Pre-release / Release Candidate | ❌        |
| Older releases                  | ❌        |

## Reporting a Vulnerability

If you discover a security vulnerability in LocalTelemetry, please report it responsibly.

**Do NOT open a public GitHub issue for security vulnerabilities.**

Instead, please use one of the following methods:

1. **GitHub Security Advisories** (preferred): Use the [Report a Vulnerability](https://github.com/Nicconike/LocalTelemetry/security/advisories/new) feature on GitHub.
2. **Email**: Send details to **[github.giving328@passmail.com](mailto:github.giving328@passmail.com)**.

### What to Include

- Description of the vulnerability
- Steps to reproduce
- Affected version(s)
- Potential impact
- Suggested fix (if any)

### Response Timeline

- **Acknowledgment**: Within 48 hours
- **Initial Assessment**: Within 1 week
- **Fix / Disclosure**: Coordinated with reporter

## Security Considerations

LocalTelemetry is a local desktop application. Key security properties:

- **No network telemetry**: All hardware monitoring data stays on your local machine. The application transmits no data externally.
- **No analytics or tracking**: The application does not collect analytics or track user activity.
- **Admin privileges**: Administrator privileges are required only for PawnIo driver installation (CPU MSR access). The application runs with standard user rights (`asInvoker`) by default. The application requests elevation once at launch. The PawnIo driver installs silently without further UAC prompts.
- **WebView2 bridge**: The settings UI restricts URL navigation to `https://` and `http://` schemes only.
