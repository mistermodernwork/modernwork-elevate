# Contributing to ModernWork Elevate

Thanks for your interest in contributing. This document covers how to build the project, the conventions we follow, and how pull requests are reviewed.

## License grant

By submitting a pull request to this repository, you agree to license your contribution under the [MIT License](LICENSE) — the same terms as the rest of the project. This is the standard GitHub "inbound=outbound" model; no separate Contributor License Agreement is required.

## Build & test

Requires the **.NET 10 SDK** (pinned in `global.json`) on Windows. Core and the tests also build on
Linux with `-p:EnableWindowsTargeting=true`.

```powershell
git clone https://github.com/mistermodernwork/modernwork-elevate.git
cd modernwork-elevate
dotnet restore
dotnet build -c Release -warnaserror
dotnet test
```

The build enforces warnings-as-errors and runs StyleCop analyzers; both must pass before opening a PR.

The full pipeline — tests, publish, signing, MSI, detection script — is one command:

```powershell
pwsh -NoProfile -File .\build\build.ps1 [-CertificateThumbprint <SHA1> -SignUnsignedDependencies]
```

See [docs/deployment.md](docs/deployment.md) for every parameter.

The tray icons, the app icon and the App Registration logo under
`src/ModernWorkElevate.App.Avalonia/Assets/` are generated from their `.svg` sources — edit
the SVG, then run `python3 build/generate-icons.py` and commit the regenerated `.ico` and
`.png` files. The script needs `pip install cairosvg pillow` and runs on Windows, macOS and
Linux.

There are two per status, `-onlight` and `-ondark`, named for the taskbar they are drawn
for; `TrayPopupController` picks between them from `SystemUsesLightTheme`. Edit both, or
the two halves drift.

Admin-facing files:

- `scripts/create-app-registration.ps1` (PowerShell 7) provisions the Entra App Registration. It
  needs the `Microsoft.Graph.Applications` module and reads the delegated scopes from
  `src/ModernWorkElevate.App.Avalonia/appsettings.json` rather than repeating them — when the app's
  permission surface changes, that file is the only place to edit.
- `packaging/msi/` — the WiX v5 installer (not part of the solution; built by `build.ps1`).
- `packaging/policy/` — ADMX/ADML templates for the machine policy.
- `packaging/scripts/` — detection script, user-data clean-up, example policy. They must stay
  runnable on **Windows PowerShell 5.1** (what Intune and Configuration Manager run); keep them ASCII
  or UTF-8 with BOM.

In these scripts, a path that comes from a parameter goes through `-LiteralPath` (`Test-Path`,
`Copy-Item`). Without it, `[` and `]` are wildcards, so `Contoso[dark].png` is reported missing or
silently not copied, even under `$ErrorActionPreference = 'Stop'`.

## Code conventions

### Language

- **Code, identifiers, code comments**: English.
- **UI text** (Views, ViewModels, toasts, tray menu, tooltips): English.
- **Commit messages**: English, [Conventional Commits](https://www.conventionalcommits.org/) style (`feat:`, `fix:`, `chore:`, `docs:`, `refactor:`, `test:`).

### Architecture

```text
src/ModernWorkElevate.App.Avalonia  →  Avalonia views, ViewModels, tray   (UI only)
src/ModernWorkElevate.Core          →  Auth, Graph, models, services      (no UI deps)
src/ModernWorkElevate.Tests         →  xUnit, Moq                         (tests against Core only)
```

`ModernWorkElevate.Core` **must not** reference Avalonia, WPF, or any other UI toolkit. This is the layering boundary that keeps tests simple and the core logic UI-agnostic.

### Graph and ARM access

All Microsoft Graph and Azure Resource Manager calls go through the service layer (`PimRoleService`, `PimGroupService`, `PimAzureResourceService`, `PolicyService`). Do not call `HttpClient` directly against `graph.microsoft.com` or `management.azure.com` — this would bypass our auth, retry, and claims-challenge handling. ARM clients come from `IArmClientFactory`.

### Async & cancellation

- All I/O-bound methods are async (`Task` / `Task<T>`). No `.Result`, no `.Wait()`, no `.GetAwaiter().GetResult()` outside of explicit sync-only contexts (`Main`, static constructors).
- All public async methods in `ModernWorkElevate.Core` accept `CancellationToken ct = default` as the last parameter. UI-triggered operations pass a token with a sensible timeout (e.g. 30 s for Graph calls).

### Nullable reference types

`<Nullable>enable</Nullable>` is set in every project. Do not introduce `#nullable disable` pragmas.

### Error handling

Graph errors are mapped to user-friendly messages via `PimErrorMapper`. Do not surface stack traces in the UI. Exceptions are kept in logs at DEBUG level.

## Security conventions

ModernWork Elevate is a privileged-access tool — please take these constraints seriously:

- **Per-machine install, per-user data.** The MSI owns Program Files and its detection key; the app itself never writes HKLM and only reads `HKLM\SOFTWARE\Policies\ModernWorkElevate`. No Windows services, no scheduled tasks. Per-user autostart goes via `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.
- **No tokens on disk, no device code, no passwords.** WAM is the only sign-in path. Do not add a token cache, a device-code or ROPC flow, a password field, or anything that reads stored credentials — such PRs will be declined.
- **No hardcoded tenant or client IDs**, and nothing organization-specific (hostnames, proxy names, certificate thumbprints, UPNs). Real identifiers come from the machine policy or the per-user config; `appsettings.local.json.sample` carries placeholders.
- **Never log access tokens, ID tokens, refresh tokens, or justification text.** Justifications may contain sensitive incident information.
- **User identifiers in logs** are limited to the OID (object ID); never UPN or mail in plaintext.
- Ticket numbers may be logged (they are not sensitive themselves).

## Tests

- Unit tests for `ModernWorkElevate.Core` use JSON fixtures for Graph responses (`src/ModernWorkElevate.Tests/Fixtures/`).
- Coverage target: **70 % lines for `ModernWorkElevate.Core`**. UI code is intentionally not covered — coverage of UI is theatre.
- Integration tests that hit a real tenant are marked `[Trait("Category","Integration")]` and do not run in CI.

## Pull request process

1. **Open an issue first** for non-trivial changes — this avoids wasted work if the change does not fit the project's direction.
2. **Branch from `master`** (`feature/<topic>` or `fix/<topic>`) and keep the PR focused on a single concern.
3. **CI must be green** — build, tests, and the coverage gate on `ModernWorkElevate.Core`.
4. **Review** by a maintainer. Once approved, the maintainer merges (squash-merge by default).

## Out of scope for v1

Pull requests in these areas need discussion first — they are tracked as backlog rather than as accepted contributions:

- Approval workflows (approver view, pending requests)
- Renewal or extension of expiring assignments (`action: "selfExtend"`)
- Bulk activation of multiple roles in a single step
