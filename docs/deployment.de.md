# Verteilung mit MSI, SCCM und Intune

## Voraussetzungen

- Windows 11 25H2 (Build 26200) oder neuer, x64 oder ARM64.
- .NET Runtime 10 (oder neuer; die App startet dank `RollForward=LatestMajor` auch mit einer neueren
  Hauptversion). Eine Windows Desktop Runtime ist nicht nötig. Das MSI bricht ohne Runtime ab;
  `SKIPRUNTIMECHECK=1` überspringt die Prüfung, wenn die Runtime im selben Rollout folgt.
- Eine eigene App-Registrierung pro Tenant (oder eine Multi-Tenant-Registrierung):
  `scripts/create-app-registration.ps1` bzw. `docs/app-registration-setup.md`. Redirect-URI
  `ms-appx-web://microsoft.aad.brokerplugin/<client-id>`. „Öffentliche Clientflows zulassen" auf
  **Nein** lassen: Die App meldet nur über WAM an (Gerätecode ist fest deaktiviert), und so lehnt
  Entra Gerätecode- und Passwort-Anfragen für die App ab.

## Installation

```cmd
msiexec /i ModernWorkElevate-<Version>-x64.msi /qn /norestart /l*v "%TEMP%\ModernWorkElevate-install.log"
```

| Eigenschaft | Wirkung |
| --- | --- |
| `AUTOSTART=1` | Autostart für alle Benutzer (`HKLM\...\Run`). Ohne: Die App schaltet beim ersten Start pro Benutzer den HKCU-Autostart ein, abschaltbar im Tray-Menü. Wird bei Upgrades übernommen. |
| `SKIPRUNTIMECHECK=1` | Prüfung auf .NET Runtime 10 überspringen. |

Installiert wird nach `C:\Program Files\ModernWork Elevate` mit Startmenü-Verknüpfung für alle Benutzer.
Laufende Instanzen werden bei Upgrade und Deinstallation beendet.

**Upgrade:** das neuere MSI genauso installieren (Major Upgrade). Benutzereinstellungen und Anmeldungen bleiben.
**Downgrade** wird verhindert.

**Deinstallation:**

```cmd
msiexec /x {ProductCode} /qn /norestart
```

Benutzerdaten bleiben stehen. Entfernen:

- vor der Deinstallation im Benutzerkontext: `Remove-ModernWorkElevateUserData.ps1` – meldet die Konten
  der App im Windows-Tokenbroker ab und löscht beide Datenordner und den Autostart;
- nach der Deinstallation als SYSTEM: `Remove-ModernWorkElevateUserData.ps1 -AllProfiles` – löscht die
  Datenordner aller Profile (ohne Broker-Abmeldung).

## Erkennungsregeln

| Werkzeug | Regel |
| --- | --- |
| Intune (Win32-App) | MSI-Produktcode, **oder** Registry `HKLM\SOFTWARE\ModernWorkElevate`, Wert `Version`, Typ Version, *größer oder gleich* der ausgerollten Version, **oder** Skript `Detect-ModernWorkElevate.ps1` |
| SCCM | Windows Installer-Produktcode, **oder** Skript `Detect-ModernWorkElevate.ps1` (PowerShell) |

`build\build.ps1` legt das Erkennungsskript mit der passenden Version neben das MSI.
Intune: Installationsverhalten **System**, Gerätezuweisung; Rückgabecodes Standard (0, 1707, 3010, 1641, 1618).

## Zentrale Konfiguration (Richtlinie)

Alle Werte liegen unter `HKLM\SOFTWARE\Policies\ModernWorkElevate` und gelten für alle Benutzer.
Die App liest nur HKLM; ein Benutzer kann die Vorgaben nicht ändern oder abschwächen.

### Gruppenrichtlinie

`packaging/policy/ModernWorkElevate.admx` nach `\\<domain>\SYSVOL\<domain>\Policies\PolicyDefinitions`,
die ADML-Dateien in die Unterordner `en-US` und `de-DE`. Pfad im Editor:
*Computerkonfiguration > Administrative Vorlagen > ModernWork Elevate*.

### Intune

*Geräte > Konfiguration > Importieren von ADMX-Dateien*: `ModernWorkElevate.admx` und die
`en-US`-ADML hochladen. Danach ein Profil *Vorlagen > Importierte administrative Vorlagen* anlegen.
Alternativ ohne ADMX: Plattformskript oder Remediation mit dem Inhalt von
`packaging/scripts/Example-Policy.reg`.

### Einstellungen

| Richtlinie | Registry | Standard |
| --- | --- | --- |
| Vorkonfigurierte Tenants und App-Registrierungen | `Tenants\<Tenant-ID>` = `ClientId[;Bezeichnung[;Cloud]]` | keine |
| Tenant-Liste sperren | `LockTenants` = 1 | aus |

Änderungen wirken beim nächsten Start der App. Ungültige Tenant-Einträge werden übersprungen und im
Log (`%LOCALAPPDATA%\ModernWorkElevate\logs`) vermerkt – ein Tippfehler legt die App nicht lahm.

Ohne `LockTenants` können Benutzer weitere Tenants in der App hinzufügen (Einstellungen > Tenants).
Diese landen in `%APPDATA%\ModernWorkElevate\appsettings.local.json`. Die Kommandozeilen-Parameter
aus [unattended-deployment.md](unattended-deployment.md) funktionieren weiterhin für per-User-Vorbelegungen, die Richtlinie
ist aber der empfohlene Weg.

## Netzwerk, Proxy und Tenant Restrictions

Die App hat **keine eigene Proxy-Einstellung**. Sie folgt der Proxy-Konfiguration von Windows
(fester Proxy, PAC-Datei oder WPAD) wie Edge und der Windows-Kontobroker (WAM). Die übliche
Konstellation funktioniert deshalb ohne Anpassung: Microsoft-365- und Azure-Endpunkte gehen
**direkt**, nur die Anmelde-Hosts laufen über den Proxy, damit er den Header für **Tenant
Restrictions v2** einfügen kann.

| Verkehr | Hosts (Global Cloud) | Wer sendet | Weg im üblichen Setup |
| --- | --- | --- | --- |
| Interaktive und stille Anmeldung | `login.microsoftonline.com`, `login.microsoft.com`, `login.windows.net`, `login.live.com` | Windows-Kontobroker (WAM) | Proxy – Header-Injection mit TLS-Inspection |
| Ressourcen der Anmeldeseite | `aadcdn.msauth.net`, `aadcdn.msftauth.net` | WAM | Direkt oder Proxy |
| MSAL-Metadaten (Instance Discovery) | `login.microsoftonline.com` | die App | Proxy |
| PIM für Verzeichnisrollen und Gruppen | `graph.microsoft.com` | die App | Direkt |
| PIM für Azure-Ressourcen | `management.azure.com` | die App | Direkt |

China-Cloud: `login.chinacloudapi.cn`, `microsoftgraph.chinacloudapi.cn`, `management.chinacloudapi.cn`.

**WPAD und PAC.** Mit *Einstellungen automatisch erkennen* (WPAD über DHCP-Option 252 oder DNS
`wpad.<domain>`) oder einer PAC-URL in den Windows-Proxy-Einstellungen wertet Windows das Skript pro
Host aus – für WAM und die App gleich; die App liest die Einstellungen des angemeldeten Benutzers,
nicht `netsh winhttp`. WPAD und PAC brauchen den Dienst **WinHTTP Web Proxy Auto-Discovery Service**
(`WinHttpAutoProxySvc`) – Härtungs-Baselines wie CIS L2 deaktivieren ihn; dann greift beides nicht
und die Anfragen gehen direkt oder scheitern. Was die App für einen Host tut, zeigt in PowerShell 7
als Benutzer:
`[System.Net.Http.HttpClient]::DefaultProxy.GetProxy([uri]'https://login.microsoftonline.com/')`
(kommt derselbe Host zurück, heisst das direkt).

**Proxy-Authentifizierung.** Schickt die PAC eine Anfrage über einen Proxy mit Anmeldung, beantwortet
die App die Rückfrage mit der Windows-Anmeldung des Benutzers (Kerberos/NTLM über SSPI) – wie Edge.
Die App sieht, verlangt oder speichert nie ein Passwort. Ein Proxy, der nur Basic-Authentifizierung
oder ein eigenes Portal-Login akzeptiert, lässt sich nicht still passieren; der Netzwerktest zeigt
dann `Proxy requires authentication (HTTP 407)`. Die Hosts oben von der Proxy-Anmeldung ausnehmen
oder direkt routen.

**TLS-Inspection.** Für die Anmelde-Hosts in Ordnung – Tenant Restrictions v2 braucht sie dort. Das
Gerät muss der inspizierenden CA im Computerspeicher vertrauen; WAM und die App nutzen beide den
Windows-Zertifikatspeicher. `graph.microsoft.com` und `management.azure.com` möglichst nicht
inspizieren: Microsoft empfiehlt, Microsoft-365-Verkehr direkt zu senden.

**Tenant Restrictions v2 und Admin-Konten in anderen Tenants.** Der Header lässt Anmeldungen nur in
Tenants zu, die eure Cross-Tenant-Access-Richtlinie erlaubt – meist nur den eigenen. Ein Admin-Konto
in einem Kunden- oder Zweit-Tenant lehnt Entra dann mit **AADSTS5000211** ab (v1-Header:
AADSTS500021), und die App meldet *„Your network's tenant restrictions block sign-in to this
tenant"*. Ein Admin **eures eigenen** Tenants erlaubt das unter **Entra ID → External Identities →
Cross-tenant access settings → Organizational settings → Add organization** (der andere Tenant) →
**Tenant restrictions**: Benutzer oder Gruppen zulassen und unter *External applications* die
**App Registration von ModernWork Elevate in jenem Tenant** sowie **Microsoft Graph**
(`00000003-0000-0000-c000-000000000000`) und **Windows Azure Service Management API**
(`797f4846-ba00-4fd7-ba43-dac1f8f63013`) zulassen. Beim Wechsel auf v2 die v1-Header
(`Restrict-Access-To-Tenants`, `Restrict-Access-Context`, `sec-Restrict-Tenant-Access-Policy:
restrict-msa`) am Proxy entfernen – sie vertragen sich nicht. WAM verbirgt den AADSTS-Code manchmal; dann nennt das Anmeldeprotokoll des Ziel-Tenants
die Sperre.

**Prüfen.** **Settings → Diagnostics → Network check** testet jeden Host, den App und Anmeldung
brauchen, zeigt den Proxy pro Gruppe und markiert einen Host, der anders geroutet wird („direct" oder
„via …") – so siehst du, dass die PAC die Anmeldung über den Proxy und Graph direkt schickt.
**Copy report** liefert dem Netzwerkteam einen Text ohne Benutzerdaten.

## Dateien und Registry im Überblick

| Ort | Inhalt |
| --- | --- |
| `C:\Program Files\ModernWork Elevate` | Programm (nur lesend für Benutzer) |
| `HKLM\SOFTWARE\ModernWorkElevate` | `Version`, `InstallPath`, `Autostart` (für Erkennung/Inventar) |
| `HKLM\SOFTWARE\Policies\ModernWorkElevate` | Richtlinie |
| `%APPDATA%\ModernWorkElevate` | `settings.json`, `accounts.json`, `appsettings.local.json`, Favoriten, Logo – keine Secrets, ACL nur Benutzer + SYSTEM |
| `%LOCALAPPDATA%\ModernWorkElevate` | `logs\` (7 Tage) – keine Tokens |
| `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` | Wert `ModernWork Elevate` (Autostart pro Benutzer) |
