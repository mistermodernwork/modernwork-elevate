# ModernWork Elevate — user guide

> *[Diese Anleitung auf Deutsch](user-guide.de.md)*
>
> For the people who use the app. Setting it up for a tenant is an admin task and lives in
> [app-registration-setup.md](app-registration-setup.md) and
> [unattended-deployment.md](unattended-deployment.md).

## What this app is for

Your admin roles are not switched on all the time. Microsoft Entra Privileged Identity Management
(PIM) hands them out on request, for a few hours at a time, so an account that is compromised while
you are not working is not an admin account. Turning a role on is called **activating** it.

Doing that in the Azure portal means opening a browser, finding the PIM blade, and clicking through
several screens. This app does the same thing from an icon next to your clock.

**It cannot give you anything you do not already have.** It activates roles you are *eligible* for —
which someone assigned to you beforehand. If a role you expect is missing, this app is not where it
gets fixed; ask whoever administers your tenant.

## Install it

Your IT department installs the app for the whole device (an MSI, usually through Intune or
Configuration Manager). It lands in `C:\Program Files\ModernWork Elevate`, with a Start-menu entry
for everyone on the device. You need no rights of your own to use it, and nothing of it can be
changed from your account.

The first time it starts for you, the app turns **Start with Windows** on. You can switch that off
under **Settings → Behavior** or in the tray menu, and the app never turns it back on.

The app has no window of its own in the taskbar. It lives in the notification area — the icons next
to the clock. Windows likes to hide new icons there: if you cannot see it, click the **^** arrow,
and drag the icon onto the visible part of the bar to keep it there.

## Connect your account

Click the tray icon. The first time, the panel says **Welcome to ModernWork Elevate**.

What happens next depends on how your IT set it up:

- **Tenants pre-configured by policy** — the app already knows your tenant. Click **Add account**,
  then **Sign in**.
- **Nothing pre-configured** — **Set up your first tenant**: enter the tenant id and the client id
  of your organization's App Registration. Your admin provides both — there is no way to guess
  them. A tenant that is already there is refused with "This tenant is already configured. Use the
  gear on its card to change it." That done, add the account.

If your IT locked the tenant list, you cannot add or change tenants yourself; the app says so.

**Sign in** opens the standard Windows account picker. Pick the admin account you hold the roles
with — for many people that is a separate account from the one they log into Windows with. There is
no password prompt inside the app: Windows itself does the sign-in (with Windows Hello, a passkey or
your organization's sign-in page), and the app never sees your password.

You can connect several accounts across several tenants. There is nothing to switch between —
everything is shown in one list, grouped by tenant.

## Reading the tray icon

The icon answers "do I have privileges right now?" without opening anything. Hover it for the
detail.

| Icon | Meaning |
| --- | --- |
| **Red** | Not signed in — no account connected, or the session expired and needs a new sign-in |
| **Grey** | Signed in, nothing active. The normal resting state |
| **Green** | At least one role is active. The tooltip counts them |
| **Amber** | An active role is about to expire. The tooltip names it and counts down |

The blue arrow tile is drawn to suit your taskbar, light or dark, and follows it if you change the Windows
theme. Only the small dot carries a meaning.

## The panel

Clicking the icon opens the panel. Top to bottom:

**ACTIVE** — what is switched on right now, each with a countdown bar; shown only while something
is. This section is the reason to look at the app when you are not activating anything.

**PINNED** — roles and saved scope sets you marked with a star. Yours to arrange; it stays across
restarts.

**RECENT** — the last three roles you activated. Fills itself, no maintenance.

**ELIGIBILITIES** — everything you may activate, grouped per tenant. Groups fold, and the app
remembers which ones you left closed.

The **search box** searches role name, role type, tenant *and* Azure scope in one go — typing a
subscription name finds the roles that apply to it. The **⟳** next to ELIGIBILITIES forces a
refresh, though the list keeps itself current on its own: activations, expiries and roles ended
elsewhere show up without you asking. **⚙** at the bottom right opens Settings.

At the very bottom sits the version you are running — quote it when you report a problem. Clicking
it opens its release notes; **mistermodernwork** and the GitHub mark next to it open the author's
profile and the project page. The app opens these only when you click; it never contacts GitHub
itself.

## Activate a role

Click a role. The activation form slides in.

**Duration** — a slider in half-hour steps. Its maximum is not the app's decision: it is the policy
your admin set for that role, so different roles offer different maximums. The starting value is
yours to set under **Settings → Behavior → Default activation duration**.

**Justification** — why you need it. This is written into your tenant's audit log and read by real
people, so "work" helps nobody later. If you keep typing the same sentence, save it with **+ Save
current** under **FAVOURITES**: it comes back for that role as a one-click favourite.

**Ticket number / Ticket system** — asked only when the role's policy requires it. The ticket system
is prefilled per tenant if your admin configured one; the number is yours to type.

Then **Activate**. The role moves to ACTIVE within a few seconds.

### Notices worth reading

**ℹ This activation requires approval** — you are not getting the role now. The request goes to an
approver and waits. The row stays visible as pending, and the role activates when they say yes.
Nothing more to do on your side; chase the approver, not the app.

**ℹ This activation may require additional verification (MFA)** — Windows asks you to confirm during
the activation.

**⚠ This role activates without asking you to sign in again** — the role's settings in your tenant
ask for no fresh sign-in, so nothing makes you prove it is you before the role becomes active.
**How to set it up** opens the guide for your PIM admin
([docs/fresh-sign-in.md](fresh-sign-in.md)). The notice stays until the role requires an
authentication context. For most Azure resource roles the app cannot read the role's settings and
shows neither notice.

**⚠ This group can carry directory roles** — on a PIM-for-Groups entry. Group membership can carry
admin roles with it, so activating this may grant more than the group's name suggests. Worth knowing
what you are switching on.

## Azure roles: choosing where the role applies

An Azure role you hold on a management group applies to everything beneath it — which can be every
subscription your company owns. Activating all of that to work on one is unnecessary exposure, so
the app asks where.

Those roles get an **Activate on** button. It opens a list with a search box at the top, then
**subscriptions**, then **management groups**, and last **Entire scope — everything listed above**.
That order is deliberate: smallest blast radius first.

**Nothing is preselected — not even the scope your eligibility sits on.** Activate stays unavailable
until you answer. Taking everything is available at the bottom of the list, but it is a decision you
make rather than what happens if you leave the form alone.

Every ticked scope becomes its own activation. Tick three subscriptions and ACTIVE shows three rows,
each with its own countdown, each stoppable on its own.

Resource groups are not offered. Nor are individual resources — those exist in the portal, but the
list would be endless and the app does not try.

### Save a set of scopes

Working in the same two subscriptions for weeks is normal, and re-ticking them daily is not.

Tick what you need, give it a name ("Project Contoso" — optional), and save it. It appears under
**FAVOURITE SCOPES** in that role's panel. One click ticks exactly those scopes; clicking a
different set **replaces** the selection rather than adding to it, so sets cannot quietly pile up.

Star a set and it also appears under **PINNED** on the main page, next to your pinned roles — one
click from the front page to a filled-in form. Pinning a role and pinning a scope set are the same
gesture on purpose.

Saved sets are stored on your machine (`scope-favorites.json`), never in the tenant. Nobody else
sees them.

## End a role early

Each ACTIVE row has a **stop** button. Use it when you are done — the whole point of PIM is that
privileges are not lying around.

**For the first five minutes the button is grey and does nothing.** That is not a bug and not the
app being careful: Microsoft refuses to deactivate a role that has been active for less than five
minutes. Hover the button and it tells you how long is left. After that it turns red and works.

Not ending a role costs nothing — it expires by itself.

## Before a role expires

Shortly before an active role runs out, a notification tells you, and the tray icon turns amber. The
countdown bar on the row shifts colour as it gets close.

Both the warning and how early it fires are under **Settings → Notifications**.

## Give yourself more time

PIM cannot extend a running activation. That is not something this app left out — Microsoft's API
refuses a second activation of a role you still hold, whatever end time you ask for, and the Entra
portal has no button for it either.

The **↻ Extend time** button therefore does the only thing that gets you more time: it ends the
running activation and immediately requests a new one for the full duration. You will find it next
to the stop button on every ACTIVE row, and on the expiry warning.

Before you press it:

- **You lose the role for a moment.** The app waits until Microsoft has really let go — usually a
  few seconds, occasionally a few minutes — and only then asks for it again. Don't press it
  in the middle of something that breaks when your permissions blink.
- **It comes prefilled.** For an Azure role, the subscription or management group the expiring
  activation runs on is already ticked, and the justification is the one you gave last time with
  `- extend time` appended. Both are yours to change before you submit. The reason is only
  remembered while the app runs — after a restart, or for a role activated elsewhere, the field
  starts empty.
- **It is a new activation.** Own justification, own entry in the audit log, exactly as if you had
  started from the eligibility.
- **The first five minutes are blocked**, for the same reason the stop button is.
- **Roles that need approval don't offer it.** Ending the activation would leave you waiting for an
  approver with no access at all. There, the only path is to let it expire and request again — so
  tell your approver beforehand.

## Settings

The gear icon, bottom right.

**APPEARANCE** — Theme: System, Light or Dark. **Logo** puts your organization's logo at the top
right of the panel: **Choose…** takes a PNG, **Remove** takes it away again.

**BEHAVIOR** — Start with Windows. Default activation duration (the
slider's starting value, not a limit — the limit is your admin's policy).

**NOTIFICATIONS** — Notify before expiry, and how early.


**DIAGNOSTICS** — Log detail, Open log folder, and a **Network check** that probes every endpoint
the app and the Windows sign-in need. **Copy report** puts the result on the clipboard in a form you
can paste into a ticket. Reach for this when sign-in fails in a locked-down network.

**TENANTS** — one card per tenant: its App Registration, its ticket system, and the accounts signed
into it. You can give an account a short alias ("EADM") instead of reading a long UPN everywhere,
and drag tenants into the order you want. The gear on a card shows its tenant id, which you can
select and copy. **Remove tenant** under the gear removes the tenant with its accounts and their
sign-ins; an account alone is removed under its pencil. Both ask for a second click.

The gear on a card also changes the tenant's client id. If the tenant has accounts, Save first turns into "Remove N accounts and save" and asks for a second click:
the accounts signed in through the old App Registration are removed, and you add them again after
the restart. Changing an App Registration takes effect after a restart, and the app says so when it
does; until then, **+ Add account…** on that card stays disabled. Tenants that come from your
organization's policy cannot be changed here.

## Updates

The app does not update itself and never contacts GitHub. New versions arrive through your
organization's software rollout; your settings, accounts and favourites survive every update.

## When something does not work

**No roles listed, but you know you have some.** Check the account, top of the panel — it is easy to
be signed in with your everyday account instead of the admin one. If the account is right, the
eligibility genuinely is not there, and that is a question for whoever administers the tenant.

**Azure roles are missing while the others are there.** The panel shows a line that starts *Azure
resource roles unavailable* — what follows it is the cause. A missing consent resolves itself once an
admin grants it. A Conditional Access policy that wants a managed device is one for IT.

The usual causes:

- *could not be reached* — a proxy or firewall blocks Azure Resource Manager. Run **Settings →
  Diagnostics → Network check** and send the report to your network team.
- *the proxy asks for authentication* — the proxy wants its own sign-in for Azure Resource Manager;
  the network team allows the endpoint for the device.
- *did not get through this time* — the network or the Windows sign-in was not ready yet, typically
  right after logon. The app reloads by itself when the network comes back and every 20 seconds
  while the note is shown.
- *background sign-in for Azure was refused* or a consent text — an admin grants the Azure Service
  Management consent; see below.

**Refresh** asks Azure again right away.

**A tenant says "Sign-in required".** Your session for that account has expired, MFA is due again
or your password changed. The app never opens a sign-in window on its own; click **Refresh** (↻)
and sign in in the window that opens — one per account that needs it.

**Sign-in window opens and stays blank.** Usually a network that blocks a Microsoft endpoint. Run
**Settings → Diagnostics → Network check**, hit **Copy report**, and send that to IT — it names the
endpoint that is blocked instead of leaving them guessing.

**A tenant shows "PIM is not available in this tenant" or "PIM is not set up in this tenant".** The
tenant has no Microsoft Entra ID P2 or Governance license, which PIM needs. There is nothing to
activate there until it has one.

**Sign-in fails with a consent or permission message.** The App Registration has not been consented
to in that tenant. An admin does it once; see
[app-registration-setup.md](app-registration-setup.md). If Azure resource roles stay unavailable
afterwards, an admin grants admin consent under Enterprise applications → (your app) → Permissions.

**Activation fails and mentions authentication context or Conditional Access.** The role demands a
stronger sign-in than the session has. Activate again and complete the Windows sign-in prompt; if it
keeps failing, the device does not meet the policy — that one is for IT.

**Activation says it needs approval and nothing happens.** It is waiting for a person. The app
cannot speed that up.

**The stop button is grey.** The first five minutes; see above.

**The tray icon is gone.** Windows hid it. Click the **^** arrow next to the clock and drag it back
onto the bar.

**A notification says "Unexpected error".** Something failed that the app did not expect. It keeps
running; what you clicked may not have taken effect, so check it and try again. The cause is in
the log.

**Anything else** — **Settings → Diagnostics → Open log folder**. The log contains no passwords, no
tokens and no justification text; it is safe to attach to a ticket.

## What lands on your machine

In your own profile, readable by you (and the system) only:

- `%APPDATA%\ModernWorkElevate` — your settings, pinned roles, saved justifications and scope sets,
  which accounts you connected, the App Registration entries you added, and your organization's
  logo if there is one
- `%LOCALAPPDATA%\ModernWorkElevate\logs` — the log files, kept for seven days
- `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` — the autostart value, while autostart is on

**No password and no token is ever written to disk.** Windows' token broker keeps your sign-ins;
the app holds access tokens in memory only while it runs.

To remove your data and sign the app's accounts out of Windows' token broker, IT runs
`Remove-ModernWorkElevateUserData.ps1` (see [deployment.md](deployment.md)). An account you added to
Windows itself, under Settings → Accounts → Access work or school, stays there.
