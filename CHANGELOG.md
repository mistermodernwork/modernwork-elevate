# Changelog

All notable changes to ModernWork Elevate. Versions follow [semantic versioning](https://semver.org). Each
entry also ships inside the app as its "what's new" text (`packaging/release-notes/<version>.md`).

## Unreleased

- Dependencies: Microsoft.Extensions.* 10.0.15 (.NET 10 servicing), Microsoft.Graph 5.105.0 (pulls
  Microsoft.IdentityModel 8.x instead of the deprecated 7.6), System.Drawing.Common 4.7.3,
  Serilog.Sinks.Console 6.1.1; test tooling updated.
- Links open in the browser only when they are absolute `https` URLs.
- CI runs with a read-only `GITHUB_TOKEN`; the release job reads the tag name from the
  environment instead of interpolating it into the script.

## 1.3.6

First public release; contains everything listed under 1.3.1 and 1.3.2.

- GitHub Actions updated: `actions/checkout` 7, `actions/setup-dotnet` 6, `actions/upload-artifact` 7.
- Release workflow: `SHA256SUMS.txt` is written after hashing instead of inside the same pipeline,
  which locked the file and failed the "Package deployment kit" step.
- Build output (`artifacts/`) is ignored and no longer part of the repository.
- ADMX namespace prefix renamed from `entrapimmanager` to `modernworkelevate`; the namespace and the
  registry paths are unchanged, so existing Group Policy settings keep working.
- Installer default version and detection script aligned with `Directory.Build.props`.

## 1.3.2

- Background reads (refresh timer, policy prefetch, network-change reload) never open a WAM
  window: `InteractiveAuthScope` allows prompts only for Activate, Stop, Extend and Refresh.
  A tenant whose session expired shows "Sign-in required … Click Refresh".
- Refresh renews each account's Graph token first — silently, or with one sign-in window per
  account, sequentially and with a 5-minute budget.
- A dismissed sign-in window suppresses further prompts for that enrollment for 15 seconds.
- The tray popup stays open while a WAM window is open and takes the focus back afterwards; a
  hidden popup is no longer used as the prompt's parent window.
- Activation and deactivation get the 5-minute sign-in budget instead of 30 seconds.
- Fresh-sign-in guide link points at the `master` branch.

## 1.3.1

- New app and tray icon: a blue tile with an upward arrow; status dot unchanged. Icons are
  generated with `build/generate-icons.py` (cairosvg + Pillow) instead of a bash script.
- The list reloads a few seconds after the network comes back (logon, wake-up, VPN), and every
  20 seconds while a tenant shows a load error.
- Transient failures of the background Azure sign-in (no network, broker not ready) no longer park
  the Azure surface for an hour; only failures that need an admin (consent, Conditional Access) do.
- Refresh button and tray menu forget the Azure backoff and ask again; so does a new sign-in.
- Proxy: the app's own requests (Graph, Azure Resource Manager, MSAL metadata, network check)
  answer an authenticating proxy with the Windows logon (Kerberos/NTLM) instead of failing with
  407 — for PAC setups that send only the sign-in hosts through the proxy (tenant restrictions v2).
- Tenant restrictions (AADSTS5000211 / AADSTS500021) get their own message naming the
  cross-tenant access settings; deployment guide has a new network, proxy and tenant restrictions
  section.
- Network check: HTTP 407 counts as a failure (it was "reachable"), and a host routed differently
  from its group is marked "direct" or "via <proxy>".
- Using directives sorted for StyleCop SA1210 after the rename.
- Azure load errors name the cause: Azure Resource Manager unreachable (proxy/firewall), proxy
  authentication (407), a transient sign-in failure, or a generic Azure-specific text.

## 1.3.0

First public release.

- Activation of Entra ID directory roles, PIM for Groups (membership and ownership) and PIM for
  Azure resources across several accounts, tenants and the Global and China clouds.
- Sign-in exclusively through the Windows account broker (WAM); no tokens on disk; no device-code
  or password flows.
- Per-machine WiX MSI with silent install, in-place upgrade and detection script; machine policy
  with ADMX/ADML (tenant list, tenant lock); per-user data with a user-only ACL.
- Windows 11 design with the user's accent colour; one row per role/group/scope.
- Graph queries fall back to smaller `$expand` sets when a tenant rejects them.
- Build script with tests, signing (signtool or Authenticode fallback), signature report and MSI;
  GitHub workflow that publishes signed or unsigned MSIs as a release.
