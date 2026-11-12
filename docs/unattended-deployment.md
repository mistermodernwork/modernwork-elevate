# Per-user configuration from the command line

> **Most organizations do not need this.** Install the MSI per device and pre-configure tenants
> with the machine policy — see [deployment.md](deployment.md). The command-line switches below
> are for the remaining cases: a per-user entry written by a logon script or an Intune script
> running in the user's context, without touching the machine policy.

The app can write one tenant entry into the calling user's configuration and exit, without
showing any UI:

```cmd
"%ProgramFiles%\ModernWork Elevate\ModernWorkElevate.exe" --tenant-id <tenant-guid> --client-id <client-guid> [--cloud Global|China] [--label "Contoso"] [--ticket-system "ServiceNow"]
```

Run it **as the user** (not as SYSTEM): it writes to that user's `%APPDATA%\ModernWorkElevate`.

## Arguments

| Argument | Required | Notes |
| --- | --- | --- |
| `--tenant-id <guid>` | yes | Directory (tenant) id |
| `--client-id <guid>` | yes | Application (client) id of your organization's App Registration |
| `--cloud <Global\|China>` | no | Defaults to `Global` |
| `--label <text>` | no | Tenant alias in the sign-in picker and tenant list |
| `--ticket-system <text>` | no | e.g. `ServiceNow`, prefilled into the activation form |

Run it once per tenant to configure several. An entry for a tenant that is already configured is
replaced, not duplicated.

The first four go into `appsettings.local.json` and take effect at the next app start. The ticket
system goes into `settings.json` — it is workflow rather than auth configuration. A running
instance keeps the settings it loaded, so configure while the app is not running, or restart it
afterwards.

A machine policy always wins: a policy entry for the same tenant replaces the per-user entry, and
with `LockTenants` per-user entries are ignored entirely.

## Exit codes

| Code | Meaning |
| --- | --- |
| `0` | Registration written |
| `1` | Arguments missing or not valid GUIDs |
| `2` | Configuration file could not be read or written |
| `3` | Windows older than Windows 11 25H2 (build 26200) |

The app is a GUI executable and has no console, so the exit code is the only feedback. Both ids
are validated before anything is written: a typo fails with code `1` and leaves the configuration
untouched.

## Your organization's logo

The app shows `%APPDATA%\ModernWorkElevate\logo.png` at the top right of the panel, if it exists.
Copy a PNG there in the user's context, or let users pick one under **Settings → Appearance →
Logo**, which writes the same file.

- **PNG only**, ideally with a transparent background that reads on the light and the dark theme.
- It is drawn 32 px tall and at most 140 px wide. 96 px of height keeps it sharp up to 300 %
  display scaling. Crop it tightly.
- A file the app cannot decode shows no logo, and the reason is in the log.
