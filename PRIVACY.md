# Privacy

ModernWork Elevate is a desktop app that runs on your Windows device. It has no backend
and no telemetry.

- **Network destinations:** Microsoft Entra sign-in, Microsoft Graph and Azure Resource Manager.
  All calls run as the signed-in user, with the delegated permissions an admin consented to.
- **GitHub, only if turned on.** The optional check for new versions (Settings → Updates, **off by
  default**, or set by your organization's policy) asks `api.github.com` once a day for this
  project's latest release; "Check for updates" asks once when you press it. The request carries no
  user, device or tenant data — GitHub, a third party in the USA, sees your IP address and a
  User-Agent naming the app and its version. Nothing is downloaded or installed. With the policy
  *Check GitHub for new versions* disabled, the app never contacts GitHub.
- **No other third parties.** The app never contacts the maintainers of this repository or any
  analytics service. Links to this repository open in your browser only when you click them.
- **On your device:** settings, the list of connected accounts and logs, in your Windows profile.
  No passwords and no tokens are written to disk. Details:
  [What lands on your machine](docs/user-guide.md#what-lands-on-your-machine).
- **Logs** contain object ids, tenant ids and ticket numbers, but no tokens, no justification text
  and no sign-in names (UPN) or mail addresses — not even where a Microsoft error message names
  the user; those are masked before they are written.
- **Terms:** the MIT [LICENSE](LICENSE).
