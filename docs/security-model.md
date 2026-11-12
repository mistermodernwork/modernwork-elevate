# Security model and design decisions

*Deutsch: [security-model.de.md](security-model.de.md)*

ModernWork Elevate is a privileged-access tool: it holds sign-ins of administrator accounts and changes role
assignments in Microsoft Entra. Every design decision below follows from that.

## Threat model in brief

| Threat | Mitigation |
| --- | --- |
| Credential theft from disk | No passwords, no tokens on disk. Refresh tokens stay in the Windows token broker (WAM), access tokens only in memory. |
| Phishing of admin sign-ins | No device-code flow, no username/password (ROPC) flow — refused in code; "Allow public client flows" stays off so Entra refuses them too. Windows Hello and passkeys through WAM. |
| Tampering with the program | Per-machine install in Program Files, read-only for standard users; signed binaries; fit for WDAC/AppLocker publisher and path rules. |
| Tampering with configuration by another user or malware in another profile | Per-user data folders with a protected ACL (user + SYSTEM only). Machine policy in HKLM, writable by administrators only, overrides every per-user setting. |
| Over-privileged app | Delegated permissions only, least privilege; no client secret, no application permissions. Graph and ARM tokens are requested separately. |
| Silent use of a stale session | Recommended: authentication context with sign-in frequency "every time" on privileged roles ([fresh-sign-in.md](fresh-sign-in.md)); the app flags roles without one. |
| Data leakage through logs | No tokens, no justification text, no UPNs or mail addresses in logs — object ids, tenant ids and ticket numbers only. |
| Supply chain | NuGet audit on all transitive packages; vulnerable dependencies pinned to patched versions; Dependabot; signature report for every build. |
| Data leakage to third parties | No telemetry, no update server, no backend. Network traffic goes to Microsoft Entra, Microsoft Graph and Azure Resource Manager only. |

## Decisions

### 1. Sign-in only through the Windows broker

MSAL.NET with `Microsoft.Identity.Client.Broker` (WAM). The app has no password field, never reads
passwords from browsers, the Edge password manager or Windows Credential Manager, never autofills
and never runs scripted logins. Convenience comes from device SSO, Windows Hello for Business and
passkeys — all inside the broker.

The interactive sign-in always shows the account picker (`ListOperatingSystemAccounts = false`),
so the Windows session account is never used by accident: admins typically sign in with a separate
admin identity.

### 2. No tokens on disk

`TokenCacheFactory` registers no persistent MSAL cache; leftover `msal*.cache` files are deleted at
start. Because MSAL then forgets its accounts when the process ends, a separate, discovery-only
broker client with `ListOperatingSystemAccounts = true` asks WAM after a restart which accounts the
app has used; the account is matched by object id from `accounts.json`. That client never runs an
interactive request.

### 3. No device code, no ROPC

`ModernWorkElevateOptions.DeviceCodeAllowed` is a constant `false`; `MsalAuthService` refuses the flow, also
for accounts added by older builds. Nothing — no file, policy or registry value — can enable it.

### 4. Per-machine MSI, no self-update

The WiX v5 MSI installs to `C:\Program Files\ModernWork Elevate`, supports silent install and in-place major
upgrades, blocks downgrades and closes running instances. Updates come from the organization's
software distribution; the app never contacts an update server.

### 5. Machine policy beats user configuration

`HKLM\SOFTWARE\Policies\ModernWorkElevate` carries the tenant list and a lock. It is applied after every JSON
configuration layer; the lock flag has a private setter the configuration binder cannot write.
Invalid policy entries are skipped and logged instead of stopping the app.

### 6. Per-user data with a user-only ACL

`%APPDATA%\ModernWorkElevate` (settings, account list, per-user tenants, favourites, logo) and
`%LOCALAPPDATA%\ModernWorkElevate` (logs) are created at start with a protected ACL: the user and SYSTEM, no
inherited entries.

### 7. Robust Graph reads

Graph returns one schedule instance per eligibility path (direct, through each group, split
schedules); the app collapses them to one row per role, group and scope. When a tenant rejects an
`$expand` combination, the read falls back to smaller expansions instead of losing the list.

### 8. Windows 11 design

The window frame, corners and shadow come from Windows (DWM); the UI uses Segoe UI Variable, the
Windows icon font and the user's accent colour. Avalonia renders the UI; the native libraries it
needs are listed in [code-signing-wdac.md](code-signing-wdac.md).

### 9. System proxy, Windows logon for proxy authentication

The app carries no proxy setting and follows Windows (static proxy, PAC, WPAD), so a PAC that routes
only the sign-in hosts through a proxy for tenant restrictions v2 works unchanged. Where a proxy
asks for authentication, `SystemProxy` lets Windows answer with the logon session (Kerberos/NTLM
via SSPI); no password passes through the app. Details: [deployment.md](deployment.md#9-network-proxy-and-tenant-restrictions).

## Known limitations

- **UI language is English.** German exists for the user guide, policy templates and rollout docs.
  Light, dark and system theme are selectable.
- **Third-party binaries without Authenticode signature** (Avalonia, Serilog, …) must be signed with
  `build.ps1 -SignUnsignedDependencies` or allowed by hash in WDAC/AppLocker.
- **Per-user autostart** is turned on at the first start for each user (HKCU); alternatively
  `AUTOSTART=1` on the MSI for all users.
- **Federated identity providers** that always sign in the everyday account: there is deliberately
  no device-code fallback; use *Use another account* in the WAM picker or sign out of the IdP
  session first.
