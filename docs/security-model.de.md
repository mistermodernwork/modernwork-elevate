# Sicherheitsmodell und Designentscheidungen

*English: [security-model.md](security-model.md)*

ModernWork Elevate ist ein Werkzeug für privilegierten Zugriff: Es hält Anmeldungen von Administratorkonten
und ändert Rollenzuweisungen in Microsoft Entra. Daraus folgen alle Entscheidungen unten.

## Bedrohungsmodell in Kürze

| Bedrohung | Gegenmassnahme |
| --- | --- |
| Diebstahl von Anmeldedaten vom Datenträger | Keine Passwörter, keine Tokens auf dem Datenträger. Aktualisierungstokens bleiben im Windows-Tokenbroker (WAM), Zugriffstokens nur im Arbeitsspeicher. |
| Phishing von Admin-Anmeldungen | Kein Gerätecode-, kein Benutzername/Passwort-Flow (ROPC) – im Code abgelehnt; „Öffentliche Clientflows zulassen" bleibt aus, damit Entra sie ebenfalls ablehnt. Windows Hello und Passkeys über WAM. |
| Manipulation des Programms | Installation pro Gerät unter Program Files, für Standardbenutzer schreibgeschützt; signierte Binärdateien; tauglich für WDAC/AppLocker-Herausgeber- und Pfadregeln. |
| Manipulation der Konfiguration durch andere Benutzer | Benutzerdatenordner mit geschützter ACL (nur Benutzer + SYSTEM). Geräterichtlinie in HKLM, nur von Administratoren schreibbar, schlägt jede Benutzereinstellung. |
| Zu weitreichende Berechtigungen | Nur delegierte Berechtigungen, minimal; kein Client-Secret, keine Anwendungsberechtigungen. Graph- und ARM-Tokens werden getrennt angefordert. |
| Unbemerkte Nutzung einer alten Sitzung | Empfohlen: Authentifizierungskontext mit Anmeldehäufigkeit „jedes Mal" für privilegierte Rollen ([fresh-sign-in.md](fresh-sign-in.md)); die App markiert Rollen ohne. |
| Datenabfluss über Logs | Keine Tokens, keine Begründungstexte, keine UPNs oder Mailadressen – nur Objekt-IDs, Tenant-IDs und Ticketnummern. |
| Lieferkette | NuGet-Audit über alle transitiven Pakete; verwundbare Abhängigkeiten auf gepatchte Versionen fixiert; Dependabot; Signaturbericht bei jedem Build. |
| Datenabfluss an Dritte | Keine Telemetrie, kein Update-Server, kein Backend. Netzwerkverkehr nur zu Microsoft Entra, Microsoft Graph und Azure Resource Manager. |

## Entscheidungen

1. **Anmeldung nur über den Windows-Broker (WAM).** Kein Passwortfeld, kein Auslesen gespeicherter
   Passwörter, kein Autofill, keine Skript-Logins. Die interaktive Anmeldung zeigt immer die
   Kontoauswahl; das Windows-Sitzungskonto wird nie unbeabsichtigt verwendet.
2. **Keine Tokens auf dem Datenträger.** Kein persistenter MSAL-Cache; alte `msal*.cache`-Dateien
   werden beim Start gelöscht. Nach einem Neustart fragt eine separate, nur dafür genutzte
   Broker-Instanz WAM nach den Konten der App; zugeordnet wird über die Objekt-ID.
3. **Kein Gerätecode, kein ROPC** – als Konstante im Code, durch nichts einschaltbar.
4. **MSI pro Gerät, kein Selbst-Update.** Silent-Installation, Major Upgrade in place, Downgrade
   gesperrt; Updates über die Softwareverteilung der Organisation.
5. **Geräterichtlinie schlägt Benutzerkonfiguration.** `HKLM\SOFTWARE\Policies\ModernWorkElevate` mit
   Tenant-Liste und Sperre; ungültige Einträge werden übersprungen und protokolliert.
6. **Benutzerdaten mit Benutzer-ACL** in `%APPDATA%\ModernWorkElevate` und `%LOCALAPPDATA%\ModernWorkElevate`.
7. **Robuste Graph-Abfragen.** Eine Zeile pro Rolle, Gruppe und Bereich; lehnt ein Tenant eine
   `$expand`-Kombination ab, fällt die Abfrage auf kleinere Erweiterungen zurück.
8. **Windows-11-Design** mit nativem Fensterrahmen, Segoe UI Variable, Windows-Icons und der
   Akzentfarbe des Benutzers.
9. **System-Proxy, Windows-Anmeldung für die Proxy-Authentifizierung.** Keine eigene
   Proxy-Einstellung; die App folgt Windows (Proxy, PAC, WPAD). Verlangt ein Proxy eine Anmeldung,
   beantwortet Windows sie mit der Anmeldesitzung (Kerberos/NTLM über SSPI) – kein Passwort läuft
   durch die App. Details: [deployment.de.md](deployment.de.md#netzwerk-proxy-und-tenant-restrictions).

## Bekannte Grenzen

- **Oberfläche auf Englisch.** Deutsch gibt es für Benutzeranleitung, Richtlinienvorlagen und
  Rollout-Doku.
- **Fremd-Binärdateien ohne Authenticode-Signatur** (Avalonia, Serilog …) mit
  `build.ps1 -SignUnsignedDependencies` signieren oder per Hash zulassen.
- **Autostart pro Benutzer** wird beim ersten Start eingeschaltet; alternativ `AUTOSTART=1` am MSI.
- **Föderierte Identity Provider**, die immer das Alltagskonto anmelden: bewusst kein
  Gerätecode-Ausweg; im WAM-Picker „Anderes Konto verwenden" wählen.
