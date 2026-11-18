# Changelog

All notable changes to ModernWork Elevate. Versions follow [semantic versioning](https://semver.org). Each
entry also ships inside the app as its "what's new" text (`packaging/release-notes/<version>.md`).

## 1.4.1

First release of the 1.4 line; contains everything listed under 1.4.0.

- 1.4.0 was tagged but never published: the release workflow had become invalid (GitHub evaluates
  `${{ }}` even inside comments of a `run:` block, and an empty one rejected the whole file), so
  the tag started no build. Fixed; the app code is the same as in 1.4.0.

## 1.4.0

- **Optional check for new versions.** Settings → Updates → *Check for new versions* (off by
  default) asks GitHub once a day for the latest release and shows a notice only when a newer
  version exists; its button opens the release page. Nothing is downloaded or installed. New
  machine policy `UpdateCheck` (ADMX *Check GitHub for new versions*): 1 = on, 0 = never contact
  GitHub. The earlier `AutomaticUpdatesEnabled` setting — written as true without asking — no
  longer counts as consent. The unused download/restart flow of the update window is removed.
  `PRIVACY.md` names GitHub as the one optional destination.
- **Installer detects .NET 10.** The MSI's runtime check never found an installed runtime — WiX put
  the search depth on the directory row, not the file row, so `System.Private.CoreLib.dll` was never
  looked for in the version folder — and every install needed `SKIPRUNTIMECHECK=1`. The check now
  reads the version of `dotnet.exe`; the property is only needed when the runtime comes later.
- **Waiting for approval.** Your own activation requests that an approver has not decided yet are
  listed above ACTIVE — for directory roles, PIM for Groups and Azure resource roles, read from
  Microsoft, so they survive a restart and include requests made in the portal. While something
  waits, the app checks every 15 seconds; an approval, denial, expiry or cancellation raises a
  notification, and an approved role switches to ACTIVE at once. No new permission: the app reads
  the request status only, so the notification does not name the approver.
- Release builds can be signed with Azure Artifact Signing: the release workflow signs in with
  GitHub OIDC (environment `release`) instead of importing a `.pfx` secret, which new publicly
  trusted certificates no longer allow. `build.ps1` / `sign.ps1` gain `-ArtifactSigningMetadata` and
  `-ArtifactSigningDlib`; certificate signing with `-CertificateThumbprint` is unchanged. Without
  configuration, releases stay unsigned. Setup: `docs/code-signing-wdac.md`.
- Tests moved from xunit 2.9.3 to xunit v3 (`xunit.v3` 4.0.3) on Microsoft.Testing.Platform:
  `dotnet test` runs in the .NET 10 SDK's MTP mode (`global.json` → `test.runner`), coverage comes
  from `coverlet.MTP` 10.1.1 with its settings in `src/ModernWorkElevate.Tests/testconfig.json`
  (replaces `coverlet.runsettings`). `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk` and
  `coverlet.collector` are no longer needed. Tests pass `TestContext.Current.CancellationToken`
  to async calls (xunit analyzer xUnit1051). No change to the app.
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
