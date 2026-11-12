# Privacy

ModernWork Elevate is a desktop app that runs on your Windows device. It has no backend
and no telemetry.

- **Network destinations:** Microsoft Entra sign-in, Microsoft Graph and Azure Resource Manager —
  nothing else. All calls run as the signed-in user, with the delegated permissions an admin
  consented to.
- **No third parties.** The app never contacts the maintainers of this repository, GitHub or any
  analytics service. Links to this repository open in your browser only when you click them.
- **On your device:** settings, the list of connected accounts and logs, in your Windows profile.
  No passwords and no tokens are written to disk. Details:
  [What lands on your machine](docs/user-guide.md#what-lands-on-your-machine).
- **Logs** contain object ids, tenant ids and ticket numbers, but no tokens, no justification text
  and no user principal names.
- **Terms:** the MIT [LICENSE](LICENSE).
