# Deployment guide — MSI, Intune and Configuration Manager

*Deutsch: [deployment.de.md](deployment.de.md)*

This guide covers everything an endpoint team needs to roll ModernWork Elevate out to managed
Windows devices: prerequisites, the App Registration, building and signing, installing, detection
rules, central configuration through policy, upgrades and removal.

## 1. Prerequisites

| Requirement | Details |
| --- | --- |
| Operating system | Windows 11 25H2 (build 26200) or later, x64 or ARM64. Enforced by an MSI launch condition and again at app start. |
| Runtime | .NET Runtime 10 or later (framework-dependent; `RollForward=LatestMajor` also runs on a newer major). The Windows Desktop Runtime is **not** needed. The MSI refuses to install without it unless `SKIPRUNTIMECHECK=1` is set. |
| Licensing | Microsoft Entra ID P2 or Entra ID Governance in every tenant (PIM's own requirement). |
| App Registration | One registration your organization owns, consented in every tenant — see section 2. |
| Network | Microsoft Entra sign-in endpoints (`login.microsoftonline.com`, `login.microsoft.com`, `device.login.microsoftonline.com`, the `aadcdn` CDNs), `graph.microsoft.com`, `management.azure.com`. Exclude `device.login.microsoftonline.com` from TLS inspection — it carries device authentication for the Primary Refresh Token. The app's **Settings → Diagnostics → Network check** tests all of them. |

The app makes **no** connection to GitHub or any other third party.

## 2. App Registration (once per organization)

Create it with the script (requires the Microsoft Graph PowerShell SDK and rights to create
applications and grant admin consent):

```powershell
.\scripts\create-app-registration.ps1 -DisplayName "ModernWork Elevate" -SignInAudience AzureADMyOrg
```

or manually following [app-registration-setup.md](app-registration-setup.md). The essentials:

- **Platform:** Mobile and desktop applications, redirect URI
  `ms-appx-web://microsoft.aad.brokerplugin/<client-id>`.
- **Allow public client flows: No.** The WAM broker does not need it; with it off, Entra refuses
  device-code and username/password requests for the app.
- **Delegated permissions only** — Microsoft Graph: `User.Read`,
  `RoleEligibilitySchedule.Read.Directory`, `RoleAssignmentSchedule.ReadWrite.Directory`,
  `RoleManagementPolicy.Read.Directory`, `RoleManagementPolicy.Read.AzureADGroup`,
  `PrivilegedAccess.ReadWrite.AzureADGroup`, `Group.Read.All`; Azure Service Management:
  `user_impersonation` (optional, only for PIM for Azure resources).
- **Grant admin consent** in every tenant the app is used in.

Recommended Conditional Access: require phishing-resistant MFA for the admin accounts, block the
device-code flow tenant-wide, and protect privileged roles with an authentication context with
sign-in frequency "every time" ([fresh-sign-in.md](fresh-sign-in.md)).

## 3. Build and sign

On a Windows build machine with the .NET SDK 10 and access to a NuGet feed:

```powershell
pwsh -NoProfile -File .\build\build.ps1 -CertificateThumbprint <SHA1> -SignUnsignedDependencies
```

| Parameter | Purpose |
| --- | --- |
| `-Runtime win-x64\|win-arm64` | Target architecture (default `win-x64`). |
| `-CertificateThumbprint` | Code-signing certificate in `CurrentUser\My` or `LocalMachine\My`. Without it, everything stays unsigned. |
| `-SignUnsignedDependencies` | Also signs third-party binaries without a valid signature (Avalonia, Serilog, …) — recommended for WDAC/AppLocker. |
| `-TimestampUrl` | RFC 3161 timestamp service (default DigiCert). Use an internal one if a proxy blocks it. |
| `-NuGetSource` | Internal NuGet feed instead of nuget.org (for networks without direct access). |
| `-SkipTests`, `-SkipInstaller` | Shorten the run. |

Output in `artifacts\msi`:

| File | Use |
| --- | --- |
| `ModernWorkElevate-<version>-<arch>.msi` | The installer |
| `Detect-ModernWorkElevate.ps1` | Detection script, already set to this version |
| `Remove-ModernWorkElevateUserData.ps1` | Optional per-user clean-up |
| `policy\` | ADMX/ADML templates |

`artifacts\signature-report-<rid>.csv` lists every binary with its signature status — the basis for
WDAC rules ([code-signing-wdac.md](code-signing-wdac.md)).

**Behind a proxy that answers `407`:** use `-NuGetSource <internal feed>`, restore once on a network
with internet access (later builds use the package cache), or request a proxy exception for
`api.nuget.org` and `globalcdn.nuget.org`.

## 4. Install

```cmd
msiexec /i ModernWorkElevate-<version>-x64.msi /qn /norestart /l*v "%TEMP%\ModernWorkElevate-install.log"
```

| Property | Effect |
| --- | --- |
| `AUTOSTART=1` | Autostart for every user (`HKLM\...\Run`). Without it, the app enables a per-user autostart (`HKCU\...\Run`) on its first start for each user; users can switch it off in the tray menu. Remembered across upgrades. |
| `SKIPRUNTIMECHECK=1` | Skip the .NET Runtime check (e.g. runtime deployed later in the same task sequence). |

The MSI installs to `C:\Program Files\ModernWork Elevate` with an all-users Start-menu shortcut and
closes running instances on upgrade and uninstall.

### Intune (Win32 app)

1. Wrap the MSI with the Microsoft Win32 Content Prep Tool (`IntuneWinAppUtil.exe`).
2. Install command: `msiexec /i "ModernWorkElevate-<version>-x64.msi" /qn /norestart`
3. Uninstall command: `msiexec /x {ProductCode} /qn /norestart` (Intune fills the product code in).
4. Install behavior: **System**. Device restart behavior: **No specific action**.
5. Requirements: Windows 11 25H2 or later; add a requirement or dependency for the .NET Runtime 10.
6. Detection: see section 5.
7. Assign to **devices**.

### Configuration Manager

Create an application with a Windows Installer deployment type from the MSI (the product code and
the uninstall command are read automatically). Install for **system**, whether or not a user is
logged on. Detection: Windows Installer product code or the detection script.

## 5. Detection rules

| Tool | Rule |
| --- | --- |
| Intune | MSI product code, **or** registry `HKLM\SOFTWARE\ModernWorkElevate`, value `Version`, type *Version*, *greater than or equal to* the version you roll out, **or** the script `Detect-ModernWorkElevate.ps1` |
| Configuration Manager | Windows Installer product code, **or** the script `Detect-ModernWorkElevate.ps1` (PowerShell) |

The script reports *installed* when the registered version is at least the one built into it and
the executable exists; it reads the 64-bit registry view, so it also works from a 32-bit host.

## 6. Central configuration (machine policy)

All settings live under `HKLM\SOFTWARE\Policies\ModernWorkElevate` and apply to every user of the
device. The app reads HKLM only; a user cannot change or weaken them.

| Policy | Registry | Default |
| --- | --- | --- |
| Pre-configured tenants and App Registrations | `Tenants\<tenant-id>` (REG_SZ) = `ClientId[;Label[;Cloud]]` | none |
| Lock the tenant list | `LockTenants` (DWORD) = `1` | off |

- A policy entry replaces a user's entry for the same tenant.
- With `LockTenants`, only policy tenants exist; users cannot add, change or remove tenants.
- Invalid entries are skipped and logged — a typo never stops the app.
- Changes apply at the next app start.

Device-code sign-in and a token cache on disk are **not configurable** — both are permanently off.

**Group Policy:** copy `packaging/policy/ModernWorkElevate.admx` to the Central Store
(`\\<domain>\SYSVOL\<domain>\Policies\PolicyDefinitions`) and the ADML files to its `en-US` and
`de-DE` subfolders. Path: *Computer Configuration → Administrative Templates → ModernWork Elevate*.

**Intune:** *Devices → Configuration → Import ADMX* — upload `ModernWorkElevate.admx` with the `en-US`
ADML, then create a profile from *Templates → Imported Administrative templates*. Without ADMX, a
platform script or remediation can write the values from `packaging/scripts/Example-Policy.reg`.

Example:

```reg
[HKEY_LOCAL_MACHINE\SOFTWARE\Policies\ModernWorkElevate]
"LockTenants"=dword:00000001

[HKEY_LOCAL_MACHINE\SOFTWARE\Policies\ModernWorkElevate\Tenants]
"00000000-0000-0000-0000-000000000001"="00000000-0000-0000-0000-0000000000aa;Contoso;Global"
```

Per-user entries from a script are possible too: [unattended-deployment.md](unattended-deployment.md).

## 7. Upgrade

Deploy the newer MSI the same way — it is a major upgrade that replaces the old version in place.
User settings, accounts and sign-ins are kept. Downgrades are blocked. Raise the detection rule's
version (the build script already did so in its copy of the detection script).

## 8. Uninstall and user data

```cmd
msiexec /x {ProductCode} /qn /norestart
```

User data stays, because a per-machine installer running as SYSTEM cannot reach users' profiles or
their token-broker sign-ins. To remove it:

| When | Command | Effect |
| --- | --- | --- |
| Before uninstalling, as the user | `Remove-ModernWorkElevateUserData.ps1` | Stops the user's instance, signs the app's accounts out of the Windows token broker, deletes both data folders and the autostart value |
| After uninstalling, as SYSTEM | `Remove-ModernWorkElevateUserData.ps1 -AllProfiles` | Deletes the data folders of all local profiles and the autostart value of loaded profiles (no broker sign-out) |

Both support `-WhatIf`.

## 9. Network, proxy and tenant restrictions

The app has **no proxy setting of its own**. It follows the Windows proxy configuration — static
proxy, PAC file or WPAD — like Edge and the Windows account broker (WAM) do. So the common setup
works as it is: Microsoft 365 and Azure endpoints go **direct**, and only the sign-in hosts pass the
proxy so it can inject the **tenant restrictions v2** header.

| Traffic | Hosts (Global cloud) | Who sends it | Route in the usual setup |
| --- | --- | --- | --- |
| Interactive and silent sign-in | `login.microsoftonline.com`, `login.microsoft.com`, `login.windows.net`, `login.live.com` | Windows account broker (WAM) | Proxy — header injection with TLS inspection |
| Sign-in page assets | `aadcdn.msauth.net`, `aadcdn.msftauth.net` | WAM | Direct or proxy |
| MSAL metadata (instance discovery) | `login.microsoftonline.com` | the app | Proxy |
| PIM for directory roles and groups | `graph.microsoft.com` | the app | Direct |
| PIM for Azure resources | `management.azure.com` | the app | Direct |

China cloud: `login.chinacloudapi.cn`, `microsoftgraph.chinacloudapi.cn`, `management.chinacloudapi.cn`.

**WPAD and PAC.** With *Automatically detect settings* (WPAD via DHCP option 252 or DNS `wpad.<domain>`)
or a PAC URL in the Windows proxy settings, Windows evaluates the script per host for WAM and the app
alike; the app reads the settings of the signed-in user, not `netsh winhttp`. WPAD and PAC
evaluation need the service **WinHTTP Web Proxy Auto-Discovery Service** (`WinHttpAutoProxySvc`) —
hardening baselines such as CIS L2 disable it; then neither works and the requests go direct or
fail. To see what the app will do for a host, run in PowerShell 7 as the user:
`[System.Net.Http.HttpClient]::DefaultProxy.GetProxy([uri]'https://login.microsoftonline.com/')`
(the same host as result means direct).

**Proxy authentication.** Where the PAC sends a request through an authenticating proxy, the app
answers the challenge with the signed-in user's Windows logon (Kerberos/NTLM via SSPI) — the same
as Edge. The app never sees, asks for or stores a password. A proxy that accepts only basic
authentication or its own portal login cannot be passed silently; the network check then shows
`Proxy requires authentication (HTTP 407)`. Exempt the hosts above from proxy authentication or
send them direct.

**TLS inspection.** Fine for the sign-in hosts — tenant restrictions v2 needs it there. The device
must trust the inspecting CA in the machine store; WAM and the app both use the Windows store.
Don't inspect `graph.microsoft.com` and `management.azure.com` if you can avoid it: Microsoft
recommends sending Microsoft 365 traffic direct.

**Tenant restrictions v2 and admin accounts in other tenants.** The header lets sign-ins reach only
the tenants your cross-tenant access policy allows — usually just your own. An admin account in a
customer or second tenant is then refused by Entra with **AADSTS5000211** (v1 header:
AADSTS500021), and the app says *"Your network's tenant restrictions block sign-in to this
tenant"*. An admin of **your own** tenant allows it under **Entra ID → External Identities →
Cross-tenant access settings → Organizational settings → Add organization** (the other tenant) →
**Tenant restrictions**: allow the users or groups, and under *External applications* allow
**ModernWork Elevate's App Registration in that tenant** plus **Microsoft Graph**
(`00000003-0000-0000-c000-000000000000`) and **Windows Azure Service Management API**
(`797f4846-ba00-4fd7-ba43-dac1f8f63013`). Remove the v1 headers (`Restrict-Access-To-Tenants`,
`Restrict-Access-Context`, `sec-Restrict-Tenant-Access-Policy: restrict-msa`) on the proxy when
you switch to v2 — they conflict. WAM sometimes hides
the AADSTS code; then the sign-in log of the target tenant names the block.

**Checking it.** **Settings → Diagnostics → Network check** probes every host the app and the
sign-in need, shows the proxy per group and names a host that is routed differently ("direct" or
"via …") — that is how you see that the PAC sends sign-in through the proxy and Graph direct.
**Copy report** gives the network team a text without user data.

## 10. Footprint

| Location | Content |
| --- | --- |
| `C:\Program Files\ModernWork Elevate` | Program files (read-only for users) |
| `HKLM\SOFTWARE\ModernWorkElevate` | `Version`, `InstallPath`, `Autostart` — detection and inventory |
| `HKLM\SOFTWARE\Policies\ModernWorkElevate` | Machine policy |
| `HKLM\...\CurrentVersion\Run` | Only with `AUTOSTART=1` |
| `%APPDATA%\ModernWorkElevate` | `settings.json`, `accounts.json`, `appsettings.local.json`, favourites, logo — no secrets; ACL user + SYSTEM only |
| `%LOCALAPPDATA%\ModernWorkElevate\logs` | Logs, 7 days — no tokens, no UPNs, no justification text |
| `HKCU\...\CurrentVersion\Run` | Value `ModernWork Elevate` while per-user autostart is on |

No service, no scheduled task, no tokens or passwords on disk.

## 11. Troubleshooting

| Symptom | Cause / fix |
| --- | --- |
| MSI: "requires Windows 11 version 25H2" | Device below build 26200. |
| MSI: "requires the .NET Runtime 10" | Deploy the runtime first or set `SKIPRUNTIMECHECK=1` with a dependency. |
| App exits immediately, exit code `3` | Windows below build 26200. |
| Consent dialog at sign-in | A requested Graph permission is not admin-consented in that tenant — compare the list in section 2 with *Enterprise applications → (app) → Permissions*. A broader permission (e.g. `RoleManagement.ReadWrite.Directory`) does not satisfy a request for a narrower one. |
| "Couldn't load eligibilities for this tenant" | See the log (`%LOCALAPPDATA%\ModernWorkElevate\logs`); the first line shows the running version. |
| Blank sign-in window | A blocked Microsoft endpoint — run the network check and send the copied report to the network team. |
| Build: `NU1301 … 407` | Proxy authentication — see section 3. |
| "Your network's tenant restrictions block sign-in to this tenant" | Tenant restrictions v2 (AADSTS5000211) — allow the tenant in your cross-tenant access settings, see section 9. |
| Network check: `Proxy requires authentication (HTTP 407)` | The proxy does not accept the Windows logon for that host — see section 9. |
| "Azure resource roles unavailable: … could not be reached" | `management.azure.com` blocked by proxy or firewall — see section 9. |
