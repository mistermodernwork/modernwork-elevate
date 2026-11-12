# Security policy

ModernWork Elevate is a privileged-access tool. Security reports are taken
seriously and handled before anything else.

## Reporting a vulnerability

**Please do not open a public issue for a vulnerability.**

Use GitHub's private vulnerability reporting: **Security → Report a vulnerability** on this
repository. Include:

- the version (shown in the panel footer and in the first line of the log),
- what an attacker can achieve and under which preconditions,
- steps to reproduce, or a proof of concept.

You will get an acknowledgement within a few working days. Fixes are released as a new MSI version
with release notes in [CHANGELOG.md](CHANGELOG.md); the advisory is published once a fixed version
is available.

## Supported versions

Only the latest release receives security fixes. Organizations deploy updates themselves through
their software distribution — the app does not update itself.

## Security design in brief

- Sign-in exclusively through MSAL with the Windows broker (WAM). No password field, no reading of
  stored credentials, no autofill, no scripted logins.
- No tokens on disk; no device-code flow; no ROPC.
- Delegated permissions only; no client secret, no application permissions.
- Per-machine, read-only program folder; per-user data folders with a user-only ACL.
- Machine policy (HKLM) overrides every per-user setting.
- Logs contain no tokens, no justification text and no UPNs.

Details: [docs/security-model.md](docs/security-model.md).

## Recommendations for operators

- Sign the binaries and the MSI with your own certificate and enforce App Control for Business or
  AppLocker ([docs/code-signing-wdac.md](docs/code-signing-wdac.md)).
- Keep "Allow public client flows" off on the App Registration and block the device-code flow with
  Conditional Access.
- Require phishing-resistant MFA for admin accounts and protect privileged roles with an
  authentication context ([docs/fresh-sign-in.md](docs/fresh-sign-in.md)).
- Consider **Assignment required** on the enterprise application, limited to the admins who use it.
