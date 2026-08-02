# Security Policy

## Supported Versions

| Version            | Supported     |
| ------------------ | ------------- |
| Latest release     | ✅             |
| Pre-release / Beta | ⚠️ Best effort |
| Older releases     | ❌             |

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

LocalTelemetry is a local-only desktop application. Key security properties:

- **No network telemetry**: All hardware data stays on your machine. No data is transmitted externally.
- **No analytics or tracking**: The application does not phone home.
- **Admin privileges**: Required only for PawnIo driver installation (CPU MSR access). The app runs as `asInvoker` by default and elevates programmatically only when needed.
- **WebView2 bridge**: URL navigation from the settings UI is restricted to `https://` and `http://` schemes only.
