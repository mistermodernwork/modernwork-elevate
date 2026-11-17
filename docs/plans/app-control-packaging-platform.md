# Bauplan: App Control Packaging & Signing Platform

> Arbeitstitel: **ModernWork AppForge**
> Status: Plan / Übergabedokument zum Bauen · Stand: 2026-11-17
> Zielgruppe: Entwickler bzw. Coding-Agent, der die Plattform umsetzt

---

## 1. Ziel und Kontext

Mit **App Control for Business (WDAC)** darf auf verwalteten Geräten nur Code laufen, dem die
Richtlinie vertraut. In der Praxis scheitert das an drei Dingen:

1. **Skripte** (PSADT, Intune-Remediations, Platform Scripts, Detection-Skripte) sind unsigniert und
   laufen dann nur im Constrained Language Mode oder gar nicht.
2. **Pakete** werden von Hand gebaut, jedes anders, ohne Standard, ohne Nachvollziehbarkeit.
3. **Neue Versionen** kommen unkontrolliert oder gar nicht – niemand weiß, welche Version wo liegt.

Die Plattform definiert den **Standardprozess für App Control for Business** und setzt ihn um:

```
Quelle erkennen → Paket bauen (PSADT) → Skripte signieren → Prüfen/Testen → Freigabe (Approval)
→ Veröffentlichen (Share / GitHub / Azure DevOps / Intune) → WDAC-Regeln ableiten → Tracken
```

Der Admin soll **möglichst nichts** ausfüllen müssen: Metadaten, Installer-Typ, Silent-Parameter,
Detection-Regel, Uninstall-String und Version werden automatisch ermittelt. Was er doch ausfüllt,
wird als **Software-Profil** gespeichert und beim nächsten Mal wiederverwendet.

### 1.1 Kernfunktionen (Scope)

| # | Funktion | Kurzbeschreibung |
|---|---|---|
| F1 | **Code-Signing-Service** | Engineers laden Skripte/Binaries hoch (UI, API, CLI, Pipeline) und bekommen sie signiert zurück. |
| F2 | **Remediation-Signing** | Intune-Remediations (Detect + Remediate) separat signieren, optional mit eigenem Zertifikat. |
| F3 | **Auswählbares Zertifikat** | Zertifikate sind konfigurierbar (Key Vault, Artifact Signing, HSM, Zertifikatsspeicher, PFX) und je Profil/Job wählbar. |
| F4 | **Automatische Paketierung mit PSADT** | Pakete auf Basis PSAppDeployToolkit 4.1 automatisch erzeugen. |
| F5 | **Versionssteuerung** | „Immer neueste Version" oder gezielt eine/mehrere Versionen auswählen und bauen. |
| F6 | **GUI-Builder** | Visueller Builder für Pre-/Post-Install, Pre-/Post-Uninstall, Repair, Dialoge, Prozesse schließen, Registry, Dateien, … |
| F7 | **Software-Profile** | Alle Einstellungen eines Produkts speicher-, versionier-, kopier- und exportierbar. |
| F8 | **Baukasten / Module** | Jede Funktion ist ein an-/abwählbarer Baustein; eigene Bausteine hinzufügbar. |
| F9 | **Approval & Notification** | Freigaben über E-Mail, Teams (Workflows-Webhook / Adaptive Card), Logic App. |
| F10 | **Automation** | REST-API, ausgehende Webhooks, PowerShell-Modul, CLI, GitHub Actions / Azure DevOps Tasks. |
| F11 | **Standardisierung & Tracking** | Software-Katalog, Namenskonventionen, Versionshistorie, Audit-Log, wer hat was wann gebaut/signiert/freigegeben. |
| F12 | **Ablageziele** | SMB-Share, GitHub (Repo/Release), Azure DevOps (Repos/Artifacts), Azure Blob, Intune (Win32-App-Upload). |
| F13 | **WDAC-Integration** | Signer-/Hash-Regeln und Supplemental Policies automatisch aus den gebauten Paketen ableiten. |

### 1.2 Nicht im Scope (v1)

- MSIX-Paketierung/Repackaging per Capture (später als Modul möglich)
- macOS/Linux-Pakete
- Eigene Gerätesteuerung/Agent auf Clients (Verteilung bleibt bei Intune/ConfigMgr)
- Verwaltung der Zertifizierungsstelle selbst (Zertifikate werden nur referenziert)

---

## 2. Rollen

| Rolle | Darf |
|---|---|
| **Engineer** | Skripte zum Signieren einreichen, Pakete anstoßen, eigene Profile bearbeiten |
| **Packager** | Profile/Bausteine pflegen, Builds ausführen, Testergebnisse erfassen |
| **Approver** | Freigaben erteilen/ablehnen (Signing und Veröffentlichung) |
| **Signing Admin** | Zertifikate anbinden, Signing-Richtlinien festlegen (wer darf mit welchem Zertifikat signieren) |
| **Platform Admin** | Ablageziele, Integrationen, Benachrichtigungen, Rollen, globale Standards |
| **Auditor** | Nur lesen: Audit-Log, Signaturhistorie, Reports |
| **Service Principal** | Automation (Pipelines, Logic Apps) mit eng begrenztem Scope |

Authentifizierung ausschließlich über **Microsoft Entra ID** (OIDC), Rollen als App-Rollen in der
App-Registrierung, Gruppenzuweisung über Entra-Gruppen. Workload Identity Federation für GitHub
Actions / Azure DevOps (keine Secrets).

---

## 3. Architektur

### 3.1 Überblick

```
                 ┌──────────────────────────────────────────────────────────┐
  Browser ──────►│  Web-UI (Blazor WebAssembly / Fluent UI)                 │
  CLI / PS-Modul │                                                          │
  Pipelines ────►│  API (ASP.NET Core, .NET 10)   ◄── Entra ID (OIDC/RBAC)  │
  Logic Apps ───►│   ├─ Catalog / Profiles / Builder                         │
                 │   ├─ Signing Orchestrator                                 │
                 │   ├─ Approval Engine (State Machine)                      │
                 │   ├─ Notification Hub (Mail / Teams / Logic App / Hooks)  │
                 │   └─ Publisher (Share / GitHub / DevOps / Blob / Intune)  │
                 └───────┬───────────────────────┬──────────────────────────┘
                         │ Jobs (Queue)          │ Daten
                         ▼                       ▼
            ┌───────────────────────┐   ┌────────────────────────────┐
            │ Windows Build Worker  │   │ Azure SQL / PostgreSQL     │
            │ (1..n, Container oder │   │ Blob Storage (Artefakte)   │
            │  VM, Windows Server)  │   │ Key Vault (Secrets/Keys)   │
            │ PSADT, IntuneWinApp-  │   │ App Insights (Telemetrie)  │
            │ Util, signtool, WDAC- │   └────────────────────────────┘
            │ Cmdlets, Sandbox-Test │
            └───────────┬───────────┘
                        ▼
            Signing Provider (Key Vault / Artifact Signing / HSM / Cert Store / PFX)
```

### 3.2 Technologieentscheidungen

| Bereich | Entscheidung | Begründung |
|---|---|---|
| Backend | **ASP.NET Core (.NET 10 LTS)**, Minimal APIs + MediatR-artige Handler | gleiche Plattform wie bestehende ModernWork-Projekte |
| Frontend | **Blazor WebAssembly** + **Fluent UI Blazor** | ein Stack (C#), Microsoft-Look, gute Formular-/Drag&Drop-Unterstützung |
| Jobs | **Azure Service Bus** (oder Storage Queue) + Worker-Service | entkoppelte, wiederholbare Builds |
| Worker | **.NET Worker Service auf Windows Server 2025** (VM/VMSS oder Windows-Container) | PSADT, signtool, IntuneWinAppUtil und ConfigCI-Cmdlets brauchen Windows |
| Test | **Windows Sandbox / Hyper-V-Wegwerf-VM** pro Testlauf | Install/Uninstall/Repair automatisch prüfen |
| Datenbank | **Azure SQL** (EF Core); alternativ PostgreSQL für On-Prem | relational: Profile, Versionen, Audit |
| Artefakte | **Azure Blob Storage** (immutable, versioniert) | Original-Installer, Pakete, Signaturberichte |
| Secrets | **Azure Key Vault** | Tokens für GitHub/DevOps, Webhook-URLs, PFX-Passwörter |
| Hosting | **On-Prem (Windows-Dienst/IIS) oder Azure (Container Apps + Windows-Worker)**, umschaltbar – siehe 3.2a | ein Codestand für beide Betriebsmodi |
| Logging | Serilog → Application Insights; Audit separat in DB (append-only) | Audit muss unveränderbar sein |

### 3.2a Betriebsmodus: On-Prem ↔ Cloud umschaltbar

Start mit **einem Tenant**. Die Plattform läuft **on-prem oder in Azure – umschaltbar per
Konfiguration** (`Hosting:Mode = OnPrem | Azure | Hybrid`), ohne Codeänderung. Jede Infrastruktur-
Abhängigkeit steckt hinter einer Schnittstelle mit zwei Implementierungen:

| Schnittstelle | On-Prem | Azure |
|---|---|---|
| `IDatabase` (EF Core) | SQL Server (Express reicht für den Start) | Azure SQL |
| `IArtifactStore` | Dateisystem / SMB-Share (schreibgeschützt nach Abschluss, Hash-geprüft) | Blob Storage (immutable) |
| `IJobQueue` | Datenbank-Queue (Tabelle mit Leasing) | Azure Service Bus |
| `ISecretStore` | DPAPI-geschützt (Machine-Scope) / Windows-Zertifikatsspeicher | Azure Key Vault |
| `ISigningProvider` | ADCS-Zertifikat im Speicher / HSM | Key Vault / Artifact Signing |
| `ITelemetry` | Serilog → Datei + Windows-Ereignisprotokoll | Application Insights |
| Hosting API/UI | Windows-Dienst (Kestrel) oder IIS | Azure Container Apps / App Service |
| Worker | Windows-Dienst auf demselben oder eigenem Server | Windows-VM/VMSS |

- **Minimal-Installation on-prem:** ein Windows Server 2025 mit API, UI, Worker und SQL Express, installiert per MSI/Setup-Skript.
- **Cloud-Integration immer möglich:** Entra ID (Anmeldung), Microsoft Graph (Mail, Intune, Teams),
  Logic Apps, GitHub und Azure DevOps funktionieren in beiden Modi; on-prem nur ausgehend über HTTPS (Proxy-fähig).
- **Hybrid:** API/UI in Azure, Worker on-prem (z. B. wegen HSM oder ADCS-Zertifikat) – Worker holt Jobs ausgehend ab, kein eingehender Port nötig.
- **Umzug** on-prem → Azure über Export/Import-Werkzeug (DB-Migration + Artefakte kopieren + Hash-Prüfung).
- Mehrere Tenants: Datenmodell bekommt ab v1 eine `TenantId`-Spalte (Standardwert = einziger Tenant), damit Multi-Tenant später ohne Migration der Daten möglich ist.

### 3.2b Standard-Bereitstellung: Reine Cloud (`Hosting:Mode = Azure`)

**Reine Cloud ist der Standard** für die Bicep-Vorlagen und die Schnellstart-Anleitung. On-Prem und
Hybrid bleiben per Konfiguration verfügbar, sind aber Zusatzvarianten.

| Baustein | Reine-Cloud-Umsetzung |
|---|---|
| API + Web-UI | Azure Container Apps (Linux), Managed Identity, Entra-ID-Anmeldung |
| Datenbank | Azure SQL (Serverless, Auto-Pause) |
| Warteschlange | Azure Service Bus (Standard) |
| Artefakte | Blob Storage, immutable (zeitbasierte Aufbewahrung) + Versionierung |
| Secrets | Azure Key Vault (RBAC-Modus) |
| Monitoring | Application Insights + Log Analytics; Audit-Export Richtung Sentinel |
| **Build-Worker** | **Windows Server 2025 VM Scale Set**, Autoscale nach Länge der Service-Bus-Queue, **0 Instanzen im Leerlauf**; Image mit signtool, IntuneWinAppUtil, PowerShell 7, ConfigCI, PSADT-Cache (Azure Compute Gallery, per Image-Builder-Vorlage erzeugt). Container Apps unterstützen keine Windows-Container, deshalb VMSS |
| **Testinstallation** | Standard: **frische Azure-VM pro Testlauf** aus Gallery-Image, wird danach gelöscht (sauberste Isolation). Option: Windows Sandbox im Worker, nur bei VM-Größen mit verschachtelter Virtualisierung (z. B. Dv5/Ev5) |
| **Signing** | Standard: **Azure Artifact Signing, Private Trust** (kein Schlüssel zu verwalten; WDAC-Regeln auf Signing-CA + EKU, nicht auf das kurzlebige Blattzertifikat). Alternativ Key Vault (Premium/HSM) mit externer CA oder mit einmalig von der ADCS signiertem CSR |
| Ablageziele | Blob, GitHub, Azure DevOps, Intune direkt; **Azure Files** (SMB mit Entra-Kerberos) als Ersatz für On-Prem-Shares |
| ConfigMgr / On-Prem-Shares | nur mit optionalem **On-Prem-Relay-Agent** (Windows-Dienst, holt Aufträge ausgehend über HTTPS ab, kein eingehender Port) oder VPN/ExpressRoute |
| Netzwerk | Private Endpoints für SQL, Storage, Key Vault, Service Bus; Worker in eigenem Subnetz, nur ausgehend (Hersteller-Downloads über NAT Gateway mit fester IP, falls Hersteller IP-Freigaben verlangen) |
| Identitäten | Managed Identity für API und Worker; Graph-Berechtigungen per App-Rolle, `Mail.Send` per Application Access Policy auf ein Postfach begrenzt |

Bicep (`appforge/infra/`): `main.bicep` mit Parameter `deploymentMode = 'cloud' | 'hybrid'`
(Standard `cloud`), Modulen je Baustein und einer `README`-Schrittfolge: App-Registrierung → Bicep
deployen → Artifact-Signing-Konto + Zertifikatsprofil → erstes Profil anlegen.

### 3.3 Projektstruktur (Vorschlag, eigenes Repo `modernwork-appforge`)

```
src/
  AppForge.Api/                 REST-API, AuthZ, OpenAPI
  AppForge.Web/                 Blazor WASM UI inkl. GUI-Builder
  AppForge.Core/                Domänenmodell, Profile, Bausteine, State Machines
  AppForge.Infrastructure/      EF Core, Blob, Key Vault, Service Bus
  AppForge.Signing/             ISigningProvider + Implementierungen
  AppForge.Sources/             ISourceProvider (winget, Evergreen, URL, GitHub Releases, Share …)
  AppForge.Packaging.Psadt/     PSADT-Generator (Template → Invoke-AppDeployToolkit.ps1)
  AppForge.Publishing/          IPublishTarget (Share, GitHub, DevOps, Blob, Intune)
  AppForge.Notifications/       Mail (Graph), Teams Workflows, Logic App, generische Webhooks
  AppForge.AppControl/          WDAC-Regel- und Policy-Erzeugung
  AppForge.Worker/              Windows-Build-Worker
  AppForge.Cli/                 `appforge` CLI (dotnet tool)
modules/
  AppForge.PowerShell/          PowerShell-Modul (Submit-AfSigningJob, New-AfPackage, …)
integrations/
  github-action/                `appforge/sign@v1`, `appforge/package@v1`
  azure-devops-task/            AppForgeSign@1, AppForgePackage@1
  logic-app-templates/          Approval-Workflow, Notification-Workflow
building-blocks/                Standard-Bausteine (YAML + PowerShell-Snippet)
infra/                          Bicep
tests/
```

---

## 4. Funktionsdetails

### 4.1 Code-Signing-Service (F1, F2, F3)

**Ablauf**

1. Engineer reicht Datei(en) ein: Upload in der UI, `appforge sign ./script.ps1`, PowerShell
   `Submit-AfSigningJob`, REST oder Pipeline-Task.
2. **Vorprüfung** (automatisch):
   - Dateityp erlaubt? (`.ps1 .psm1 .psd1 .ps1xml .exe .dll .msi .msix .cat .cab .vbs .js`)
   - PSScriptAnalyzer (konfigurierbare Regeln, z. B. kein `Invoke-Expression`, keine Klartext-Passwörter)
   - Secret-Scan (Muster für Tokens/Passwörter)
   - Malware-Scan (Defender `MpCmdRun` auf dem Worker)
   - Bereits signiert? → Hinweis, optional neu signieren
   - Encoding: PowerShell-Skripte als UTF-8 mit BOM normalisieren (sonst bricht die Signatur bei Umlauten)
3. **Signing-Richtlinie** entscheidet: automatisch signieren oder Approval nötig
   (z. B. Remediations immer mit Approval, Skripte aus vertrauenswürdiger Pipeline automatisch).
4. Signatur: SHA-256, RFC-3161-Zeitstempel, anschließend **Verifikation** (`Get-AuthenticodeSignature`
   bzw. `signtool verify /pa`).
5. Rückgabe: signierte Datei + Signaturbericht (Thumbprint, Zeitstempel, Hash vorher/nachher,
   Prüfergebnisse) + Eintrag im Audit-Log.

**Intune-Remediations (F2)** – eigener Assistent:
- Paar aus *Detection* und *Remediation* hochladen (oder aus Vorlage erzeugen)
- Getrennt wählbares Remediation-Zertifikat
- Prüfung auf Intune-Anforderungen (Exit-Codes, Ausgabe nach STDOUT, Größe, Encoding)
- Optional direkt per Graph als Remediation in Intune anlegen/aktualisieren mit
  „Enforce script signature check" = Ja
- Gleiches für **Platform Scripts** und **Custom-Compliance-Skripte**

**Zertifikate auswählbar (F3)** – Abstraktion `ISigningProvider`:

| Provider | Einsatz | Priorität |
|---|---|---|
| `CertStoreProvider` | **On-Prem-PKI (ADCS) – Ist-Zustand.** Zertifikat aus Vorlage „Code Signing“ im Speicher `LocalMachine\My` des Workers (Thumbprint), auch Smartcard/Netzwerk-HSM über KSP/CSP. Optional automatische Registrierung/Verlängerung über `certreq`/Auto-Enrollment | v1 (Pflicht) |
| `AzureKeyVaultProvider` | **Hybrid, empfohlener Zielweg:** Key Vault (Premium/Managed HSM) erzeugt den Schlüssel und einen CSR, die **On-Prem-ADCS signiert den CSR** – der private Schlüssel verlässt das HSM nie, die Vertrauenskette bleibt eure PKI. Signieren remote (Digest-Signing wie AzureSignTool) | v1 |
| `ArtifactSigningProvider` | **Cloud:** Azure Artifact Signing (vormals Trusted Signing). Profil *Private Trust* für interne Skripte/Pakete und App Control, *Public Trust* für extern verteilte Software. Achtung: Blattzertifikate sind kurzlebig (Tage) – WDAC-Regeln deshalb auf Ebene Aussteller-CA + EKU/Profil, nie auf das Blattzertifikat | v1 |
| `ExternalCaProvider` | **Externe/öffentliche CA:** Zertifikat einer öffentlichen CA (z. B. DigiCert, GlobalSign, Sectigo) entweder über die Key-Vault-CA-Integration ausgestellt (dann `AzureKeyVaultProvider`) oder über den Cloud-Signing-Dienst des Anbieters (KSP/PKCS#11 bzw. Remote-Signing-API) als Plug-in | v2 |
| `PfxProvider` | PFX aus dem Secret-Speicher – nur Test/Lab, in Produktion per Richtlinie gesperrt | v1 (Lab) |

> **Hinweis Microsoft Cloud PKI (Intune):** Cloud PKI stellt nur Geräte-/Benutzerzertifikate per SCEP
> an Intune-verwaltete Geräte aus; der private Schlüssel entsteht auf dem Gerät. Für einen zentralen
> Code-Signing-Dienst ist es **nicht geeignet**. „Cloud-PKI für Code Signing“ bedeutet in diesem Plan
> deshalb Azure Artifact Signing oder Key Vault (mit eigener oder externer CA).

**Migrationspfad Zertifikate** (ohne Neubau der Plattform, nur Konfiguration):
1. Start: ADCS-Zertifikat im Worker-Zertifikatsspeicher (`CertStoreProvider`)
2. Härtung: Schlüssel nach Key Vault/HSM, CSR von ADCS signiert (`AzureKeyVaultProvider`)
3. Cloud: Artifact Signing (Private Trust) für neue Pakete; WDAC-Basisrichtlinie vertraut beiden Ketten parallel, bis die alte ausläuft
4. Extern: öffentliches Zertifikat nur dort, wo Geräte außerhalb der eigenen Richtlinie vertrauen müssen

Die WDAC-Signer-Regeln für jedes angebundene Zertifikat erzeugt die Plattform selbst (Abschnitt 4.9), damit ein Zertifikatswechsel nicht zum Ausfall auf den Geräten führt.

Pro Zertifikat konfigurierbar: Anzeigename, Verwendungszweck (Pakete / Skripte / Remediations /
Test), erlaubte Rollen/Gruppen, Approval-Pflicht, Zeitstempel-URL, Ablaufwarnung (z. B. 60/30/7 Tage
per Notification). Auswahl in Profil, Job oder API-Parameter `certificateId`; Default je Zweck.

### 4.2 Automatische Paketierung mit PSADT (F4, F5)

**Quellen (`ISourceProvider`)** – liefern Versionen + Download-URL + Hash + Metadaten:

| Provider | Beschreibung |
|---|---|
| `WingetSource` | winget-pkgs-Manifeste (Version, Installer-URL, SHA-256, Installer-Typ, Silent-Switches, ProductCode) |
| `EvergreenSource` | PowerShell-Modul *Evergreen* für Hersteller-APIs (neueste Version) |
| `GitHubReleaseSource` | Releases eines GitHub-Repos per Asset-Muster |
| `UrlSource` | Feste URL / URL-Muster mit Versionsregex |
| `FileShareSource` / `UploadSource` | Interne Software, manueller Upload |

**Versionsmodus pro Profil**
- `Latest` – Plattform prüft zeitgesteuert (z. B. täglich) und baut neue Versionen automatisch
- `Pinned` – genau eine Version
- `Multiple` – Liste wählbarer Versionen (z. B. N und N-1, oder LTS + Current); UI zeigt alle verfügbaren Versionen der Quelle zum Ankreuzen
- `Ring` – Pilot bekommt Latest, Produktion Latest-1 (Ringe konfigurierbar)

**Automatische Erkennung (damit der Admin nichts ausfüllen muss)**
1. Download → Hash prüfen gegen Quelle → Defender-Scan → Signatur des Herstellers prüfen
2. Installer-Typ erkennen: MSI, MSIX, EXE (Inno, NSIS, InstallShield, WiX Burn, Squirrel, Advanced Installer …)
3. Silent-Parameter aus Wissensbasis (winget-Manifest → Installer-Typ-Defaults → Profil)
4. MSI: ProductCode, UpgradeCode, Version, Hersteller, Name aus der MSI-Tabelle lesen
5. Icon aus Installer extrahieren, Beschreibung/Hersteller-URL aus Manifest
6. **Testinstallation in Sandbox**: Installieren → Unterschiede erfassen (Uninstall-Registry-Key,
   Dateien, Dienste, Verknüpfungen) → daraus Detection-Regel und Uninstall-Befehl ableiten → Uninstall
   testen → Repair testen
7. Ergebnis wird als Vorschlag in das Profil geschrieben; Admin bestätigt nur noch

**Paketerzeugung**
- PSADT 4.1 Template (Version im Profil pinnbar, Standard = freigegebene Toolkit-Version der Plattform)
- Generator erzeugt `Invoke-AppDeployToolkit.ps1` aus Profil + Bausteinen (Abschnitte
  *Pre-Install, Install, Post-Install, Pre-Uninstall, Uninstall, Post-Uninstall, Pre-Repair, Repair, Post-Repair*)
- Branding/Sprache/Logo aus globalen Standards (`Config/config.psd1`, Strings)
- **Alle Skripte im Paket signieren** (Toolkit-Module, `Invoke-AppDeployToolkit.ps1`, eigene Erweiterungen) –
  Pflicht, damit das Paket unter App Control / Constrained Language Mode läuft
- Optional `.intunewin` erzeugen (IntuneWinAppUtil) inkl. Install-/Uninstall-Kommando, Detection-Regel,
  Requirements, Rückgabecodes
- Paket-Manifest `appforge.json` (Profil-Version, Quelle, Hashes, Signaturen, Build-ID) wird beigelegt
- Paket-Name nach Konvention, z. B. `<Hersteller>_<Produkt>_<Version>_<Arch>_<Sprache>_PSADT_R<Revision>`

### 4.3 GUI-Builder (F6, F8)

Visueller Editor für den Ablauf eines Pakets – ohne PowerShell schreiben zu müssen, aber mit
Code-Ansicht für Profis.

**Aufbau der Oberfläche**
- Links: **Baustein-Bibliothek** (durchsuchbar, Kategorien)
- Mitte: **Phasen-Spalten** (Pre-Install · Install · Post-Install · Pre-Uninstall · Uninstall ·
  Post-Uninstall · Pre-Repair · Repair · Post-Repair) – Bausteine per Drag & Drop
- Rechts: **Eigenschaften** des gewählten Bausteins (Formular, automatisch aus Schema generiert)
- Unten: **Live-Vorschau** des generierten Skripts (Monaco-Editor, read-only oder „Expertenmodus")
- Validierung in Echtzeit (Pflichtfelder, Konflikte, z. B. „Repair ohne Install-Schritt")

**Standard-Bausteine (Auswahl, jeweils an-/abwählbar)**

| Kategorie | Bausteine |
|---|---|
| Benutzerdialog | Willkommensdialog, Prozesse schließen (mit Countdown/Defer), Fortschritt, Neustart-Prompt, Balloon/Toast |
| Installation | MSI installieren/patchen (MST/MSP), EXE ausführen, MSIX hinzufügen, winget installieren |
| Deinstallation | Per Name/ProductCode deinstallieren, alte Versionen entfernen, Reste löschen |
| Dateien | Kopieren, Löschen, Verknüpfungen anlegen/entfernen, Ordnerrechte |
| Registry | Schlüssel/Werte setzen/löschen (HKLM, alle Benutzer via HKCU-Loop / Active Setup) |
| System | Dienst starten/stoppen/konfigurieren, Geplante Aufgabe, Umgebungsvariable, Firewall-Regel |
| Lizenz/Config | Lizenzdatei kopieren, INI/JSON/XML-Wert setzen, Server-URL eintragen |
| Abhängigkeiten | VC++ Runtime, .NET Desktop Runtime, WebView2 prüfen/installieren |
| Prüfung | Freier Speicher, Akkubetrieb, OS-Version, Architektur, laufende Prozesse |
| Benutzerkontext | Pro-Benutzer-Aktionen (Active Setup, `Invoke-ADTAllUsersRegistryAction`) |
| Benutzerdefiniert | Eigenes PowerShell-Snippet (wird mitsigniert und analysiert) |

**Bausteine sind erweiterbar**: Ein Baustein ist eine YAML-Definition (Name, Parameter-Schema,
Phase-Kompatibilität) + PowerShell-Template. Packager können eigene Bausteine anlegen, versionieren
und freigeben (Freigabe nötig, da der Code in jedes Paket wandert).

```yaml
id: registry.set-value
version: 1.2.0
name: Registry-Wert setzen
phases: [PreInstall, PostInstall, PostRepair]
parameters:
  - { name: Key,   type: string, required: true, example: 'HKLM\SOFTWARE\Contoso\App' }
  - { name: Name,  type: string, required: true }
  - { name: Value, type: string, required: true }
  - { name: Type,  type: enum, values: [String, DWord, QWord, ExpandString, MultiString], default: String }
template: |
  Set-ADTRegistryKey -LiteralPath '{{Key}}' -Name '{{Name}}' -Value '{{Value}}' -Type {{Type}}
```

### 4.4 Software-Profile (F7)

Ein **Software-Profil** beschreibt ein Produkt vollständig und reproduzierbar:

- Stammdaten: Hersteller, Produkt, Kategorie, Owner, Lizenzmodell, Kritikalität, Tags
- Quelle + Versionsmodus + Filter (Architektur, Sprache, Kanal)
- Installer-Einstellungen (Silent-Parameter, MST, Abhängigkeiten)
- Ablauf aus dem GUI-Builder (Bausteine pro Phase)
- Detection-Regel, Requirements, Rückgabecodes
- Signing: Zertifikat je Zweck
- Approval-Workflow, Benachrichtigungen
- Ablageziele und Intune-Einstellungen (Zuweisungen, Supersedence, Abhängigkeiten)
- WDAC-Einstellungen (Regeltyp: Publisher / FilePublisher / Hash)

Eigenschaften:
- **Versioniert** (jede Änderung = neue Profil-Revision, Diff-Ansicht)
- **Vorlagen** („MSI-Standard", „EXE mit Prozessschließen", „Interne Fachanwendung") – neue Profile erben davon
- **Vererbung**: globale Standards → Vorlage → Profil (nur Abweichungen speichern)
- **Export/Import** als YAML/JSON (GitOps: Profile können im Git-Repo liegen und per PR geändert werden)
- **Klonen** für ähnliche Produkte

### 4.5 Approval & Notification (F9)

**Approval Engine** – konfigurierbare State Machine je Vorgangstyp:

```
Draft → Submitted → [Automatische Prüfungen] → PendingApproval → Approved → Signed/Built → Published
                                   │                    │
                                   └── Failed           └── Rejected (mit Begründung)
```

- Ein- oder mehrstufig (z. B. Packager → Security), N-von-M-Approver, Vier-Augen-Prinzip
  (Einreicher darf nicht selbst freigeben)
- Eskalation/Timeout (z. B. nach 48 h Erinnerung, nach 5 Tagen Ablehnung)
- Freigabe enthält immer: Hashes, Diff zum Vorgänger, Prüfergebnisse, Testprotokoll

#### Freigabestufen (Standard, festgelegt)

Herleitung aus den bisherigen Projekten: In den Repos gibt es **keine ausdrücklich definierten
Freigabestufen**. Übernommen werden die gelebten Regeln:
- *ModernWork Elevate* → Signer-Regeln vor Hash-Regeln, Signaturbericht vor jeder Regel prüfen,
  WDAC-Änderungen **zuerst im Audit-Modus** (CodeIntegrity-Events 3076/3077), dann erzwingen;
  Prinzip „Just-in-Time + Freigabe für privilegierte Aktionen“ (PIM)
- *M365 Apps MEC Packager* → automatischer Build bekannter Produkte, Supersedence, Abschluss erst
  nach erfolgreichem Kopieren, Bericht per Mail
- *Contributing-Regeln* → Review durch eine zweite Person vor dem Merge

Daraus folgt ein **risikobasiertes Stufenmodell**. Die Stufe wird von der Plattform **automatisch
ermittelt** (höchste zutreffende Regel gewinnt); Stufen sind pro Mandant konfigurierbar, das hier
ist der Auslieferungsstandard.

| Stufe | Name | Wer gibt frei | Gilt für (automatische Einstufung) |
|---|---|---|---|
| **0** | Automatisch | niemand (nur Benachrichtigung) | Signieren mit **Test-/Lab-Zertifikat**; Build und Verteilung **in den Test-Ring**; neue Herstellerversion eines bereits freigegebenen Profils, wenn Herausgeber-Signatur gültig **und unverändert**, Profil-Revision unverändert, alle Tests grün; Skripte aus einer Pipeline mit geschütztem Branch und Pflicht-Review (das Review ist die Freigabe) |
| **1** | Fachliche Freigabe | App-Owner **oder** Packager (≠ Einreicher) | Neue Version → **Pilot-/Produktions-Ring**; Änderung an einem bestehenden Profil (Bausteine, Parameter); Signieren eigener Skripte (Engineer-Tools, PSADT-Erweiterungen) mit Produktivzertifikat |
| **2** | Vier-Augen + Security | Packager **und** App-Control-/Security-Verantwortlicher (beide ≠ Einreicher) | **Neues Produkt** im Katalog; **neuer oder geänderter Herausgeber/Signer** beim Hersteller; Paket braucht **Hash-Regeln** (unsignierte Dateien); **eigener Code** im GUI-Builder (Expertenmodus) oder neuer/geänderter Baustein; **Intune-Remediations, Platform- und Compliance-Skripte** (laufen als SYSTEM); Neusignieren fremder Dateien |
| **3** | Change / CAB | Security **und** Change-Board (bzw. Plattform-Owner) | Änderung der **WDAC-Basisrichtlinie**; Supplemental Policy von **Audit → Enforced**; **Anbinden, Tauschen oder Widerrufen eines Signing-Zertifikats**; Änderung an Freigaberegeln oder Rollen; Notfall-Sperre eines Produkts aufheben |

Feste Regeln (nicht abschaltbar):
- Einreicher darf **nie** selbst freigeben; Service Principals dürfen einreichen, aber nicht freigeben.
- Jede Freigabe zeigt: Hashes, Diff zum Vorgänger (Profil, Skript, Signer), Prüf- und Testergebnisse, betroffene WDAC-Regeln, Zielringe.
- WDAC-Policies gehen **immer zuerst im Audit-Modus** raus; der Wechsel auf Enforced ist Stufe 3 und frühestens nach einer konfigurierbaren Beobachtungszeit (Standard 7 Tage ohne neue 3076/3077-Events für das Produkt) möglich.
- Ablauf: Stufe 1 = 2 Werktage, Stufe 2 = 3 Werktage, Stufe 3 = nächster CAB-Termin; danach Eskalation an Vertretung bzw. Plattform-Admin.
- **Notfall-Weg** (z. B. kritisches Sicherheitsupdate): Stufe 2 darf durch einen Security-Verantwortlichen allein freigegeben werden, wird als `Emergency` markiert und muss innerhalb von 2 Werktagen nachträglich bestätigt werden.
- Ringe: `Test` (Stufe 0) → `Pilot` (Stufe 1, automatisch nach X Tagen ohne Fehler vorgeschlagen) → `Produktion` (Stufe 1).

**Kanäle** (`INotificationChannel`, frei kombinierbar je Profil/Ereignis):

| Kanal | Umsetzung | Approval möglich |
|---|---|---|
| **E-Mail** | Microsoft Graph `sendMail` (Managed Identity / App-Berechtigung `Mail.Send` eingeschränkt per Application Access Policy) | Ja – Link in die UI, alternativ Actionable Message |
| **Microsoft Teams** | **Teams Workflows**-Webhook (Power Automate) mit **Adaptive Card**; die alten Office-365-Connectoren werden nicht verwendet | Ja – Buttons „Freigeben/Ablehnen" rufen die API zurück (signierter Callback-Token) |
| **Logic App** | HTTP-Trigger der Logic App; Plattform sendet Ereignis + Callback-URL, Logic App führt eigenen Approval-Prozess (z. B. Outlook-Approval, ServiceNow) und meldet Ergebnis zurück | Ja – Logic App ist dann der Approver |
| **Generischer Webhook** | JSON-Payload, HMAC-signiert | Nein (nur Info), außer Callback wird genutzt |

Ereignisse: `SigningRequested`, `SigningCompleted`, `ApprovalRequired`, `ApprovalGranted`,
`ApprovalRejected`, `NewVersionDetected`, `BuildSucceeded`, `BuildFailed`, `TestFailed`,
`Published`, `CertificateExpiring`, `PolicyUpdated`.

Vorlagen für Logic Apps (Bicep/ARM) werden mitgeliefert.

### 4.6 Automation (F10)

- **REST-API** (OpenAPI 3, versioniert `/api/v1`), alle UI-Funktionen auch per API
- **Ausgehende Webhooks / Event Grid** für alle Ereignisse oben
- **PowerShell-Modul** `AppForge` (`Connect-AppForge`, `Submit-AfSigningJob`, `Get-AfProfile`,
  `New-AfPackageBuild`, `Approve-AfRequest`, `Publish-AfPackage`)
- **CLI** `appforge` (dotnet tool), geeignet für Pipelines
- **GitHub Action** und **Azure DevOps Task** (Signieren + Paket bauen), Auth via Workload Identity Federation
- **Zeitpläne**: Versionsprüfung, Re-Builds, Zertifikatsablauf-Checks
- **Idempotenz**: jeder Job mit `Idempotency-Key`; gleiche Eingabe → gleiches Ergebnis, kein Doppelbau

### 4.7 Standardisierung & Tracking (F11)

- **Software-Katalog**: alle Produkte, Versionen, Status (Neu / In Test / Freigegeben / Veröffentlicht /
  Abgekündigt / Gesperrt), Owner, nächste geplante Version
- **Automatische Aufnahme**: neue Version erkannt → Build → Test → Approval-Anfrage, ohne manuellen Schritt
- Optional: **Inventarabgleich** mit Intune (Discovered Apps / Win32-Apps per Graph) – zeigt
  unpaketierte oder veraltete Software und schlägt neue Profile vor
- **Namenskonventionen** und Pflichtfelder global erzwingbar (Linter für Profile)
- **Audit-Log** (append-only): wer hat was wann eingereicht, signiert, freigegeben, veröffentlicht;
  inkl. Hashes; exportierbar (CSV/JSON) und an Log Analytics/Sentinel streambar
- **Reports/Dashboard**: offene Freigaben, Build-Erfolgsquote, Zeit von Release bis Veröffentlichung,
  veraltete Versionen, ablaufende Zertifikate, Signaturen pro Zertifikat

### 4.8 Ablageziele (F12)

`IPublishTarget` – mehrere Ziele je Profil gleichzeitig:

| Ziel | Details |
|---|---|
| **SMB-Share** | Pfadmuster `\\server\pkg\{Vendor}\{Product}\{Version}`, Zugriff über gMSA/Dienstkonto des Workers |
| **GitHub** | Profil + Quellskripte als Commit/PR in ein Repo; Binärpaket als Release-Asset (große Dateien nicht ins Git) |
| **Azure DevOps** | Repos (Commit/PR) und **Azure Artifacts – Universal Packages** für Binärpakete |
| **Azure Blob** | Immutable Container mit Versionierung |
| **Intune** | Win32-App per Graph anlegen/aktualisieren (Upload `.intunewin`, Detection, Requirements, Supersedence, Zuweisungen pro Ring) |
| **ConfigMgr** (optional, später) | Application + Deployment Type per AdminService |

Jede Veröffentlichung wird im Katalog mit Ziel, Pfad/URL und Hash protokolliert.

### 4.9 App Control for Business Integration (F13)

- Aus jedem Build: Signer-Informationen aller enthaltenen Binaries und Skripte sammeln
- Regelvorschlag je Profil: **Publisher** (bevorzugt) → **FilePublisher** → **Hash** (Fallback für unsignierte Hersteller-Dateien)
- Erzeugung/Aktualisierung einer **Supplemental Policy** pro Produkt oder Produktgruppe
  (ConfigCI-Cmdlets: `New-CIPolicyRule`, `Merge-CIPolicy`, `ConvertFrom-CIPolicy`), Policy-Binary signiert
- Eigene Signing-Zertifikate als Signer-Regel in der Basis-Richtlinie dokumentiert (inkl. Hinweis
  auf Skriptsignatur für Constrained Language Mode)
- Optionaler Export als Intune-Konfiguration (App Control for Business Policy via Graph)
- Hinweis auf **Managed Installer** (Intune Management Extension): Plattform dokumentiert, welche
  Pakete darüber abgedeckt sind und wo zusätzliche Regeln nötig sind
- Audit-Mode-Auswertung (optional, später): CodeIntegrity-Events aus Log Analytics einlesen und
  fehlende Regeln vorschlagen

---

## 5. Standardprozess (Soll-Prozess App Control for Business)

| Schritt | Automatisch | Manuell (Admin) |
|---|---|---|
| 1. Bedarf / neue Software | Inventarabgleich schlägt Profil vor | Antrag / Profil aus Vorlage anlegen |
| 2. Quelle & Version | Neueste Version erkannt, Download, Hash, Scan | ggf. Versionen auswählen |
| 3. Analyse | Installer-Typ, Silent-Switches, Metadaten, Detection | bestätigen |
| 4. Paket bauen | PSADT-Paket aus Profil + Bausteinen | optional Bausteine im GUI-Builder ergänzen |
| 5. Signieren | alle Skripte/eigene Binaries, Verifikation | – |
| 6. Testen | Sandbox: Install / Detection / Uninstall / Repair | optional manueller Test, Ergebnis erfassen |
| 7. Freigabe | Benachrichtigung per Mail/Teams/Logic App | Approver gibt frei |
| 8. Veröffentlichen | Share / GitHub / DevOps / Intune | – |
| 9. App-Control-Regeln | Supplemental Policy aktualisieren | Policy-Freigabe |
| 10. Tracking | Katalog, Audit, Reports | – |

Für reine **Skript-Signierung** (Remediations, Platform Scripts): Schritte 5 → 7 → 8 (optional
Intune-Upload) mit Vorprüfung aus 4.1.

---

## 6. Datenmodell (Kern)

```
SoftwareProfile (Id, Vendor, Product, Category, OwnerGroup, TemplateId, CurrentRevision, Status)
 └─ ProfileRevision (Id, ProfileId, Revision, Json/Yaml, CreatedBy, CreatedAt, ChangeNote)
SourceDefinition (ProfileId, ProviderType, Settings, VersionMode, Filter)
SoftwareVersion (Id, ProfileId, Version, SourceUrl, Sha256, DetectedAt, Status)
BuildingBlock (Id, Key, Version, Schema, Template, Status, ApprovedBy)
Build (Id, ProfileRevisionId, SoftwareVersionId, WorkerId, Status, Log, StartedAt, FinishedAt)
 └─ Artifact (Id, BuildId, Kind[Package|IntuneWin|Manifest|Report], BlobUri, Sha256)
TestRun (Id, BuildId, Scenario[Install|Detect|Uninstall|Repair], Result, Log)
SigningCertificate (Id, DisplayName, Provider, ProviderRef, Thumbprint, NotAfter, Purposes, AllowedGroups, RequiresApproval)
SigningJob (Id, Type[Script|Remediation|Package|Binary], CertificateId, SubmittedBy, Status, InputHashes, OutputHashes)
ApprovalRequest (Id, SubjectType, SubjectId, WorkflowId, Stage, Status, DueAt)
 └─ ApprovalDecision (RequestId, Approver, Decision, Comment, Channel, At)
PublishTarget (Id, Type, Settings, SecretRef)
Publication (Id, ArtifactId, TargetId, Location, Status, At)
NotificationRule (Id, Event, ChannelType, Settings, Scope)
AppControlRule (Id, ProfileId, Level, SignerData, PolicyId)
AuditEntry (Id, At, Actor, Action, SubjectType, SubjectId, Data, PrevHash)   -- Hash-Kette
```

---

## 7. Sicherheit

- Private Schlüssel verlassen **nie** HSM/Key Vault/Artifact Signing; PFX nur für Labor, per Richtlinie sperrbar
- Signier-Berechtigung pro Zertifikat über Entra-Gruppen; Vier-Augen-Prinzip für produktive Zertifikate
- Nur signieren, was vorher geprüft wurde (Analyzer, Secret-Scan, Malware-Scan); Bericht unveränderlich gespeichert
- Worker isoliert (eigenes Subnetz, nur ausgehend zu Quellen/Signing), Sandbox pro Testlauf, keine Admin-Anmeldung interaktiv
- Managed Identities statt Secrets; restliche Secrets in Key Vault
- Webhook-Callbacks mit kurzlebigen, signierten Tokens; ausgehende Webhooks HMAC-signiert
- Audit-Log als Hash-Kette, Export nach Sentinel
- Rate Limiting und Größenlimits für Uploads
- Downloads nur über HTTPS mit Hash-Abgleich; Hersteller-Signatur wird geprüft und protokolliert

---

## 8. Umsetzungsphasen (Übergabe zum Bauen)

Jede Phase ist eigenständig lieferbar. Akzeptanzkriterien sind die Definition of Done.

### Phase 0 – Fundament (≈ 2 Wochen)
- Repo, Solution-Struktur, CI (Build, Tests, Lint), Bicep-Grundgerüst
- Entra-App-Registrierung, App-Rollen, Login in UI/API
- Datenbank + Migrationen, Blob, Key Vault, Service Bus, Audit-Log-Grundgerüst
- Windows-Worker, der Jobs aus der Queue abholt und Logs zurückmeldet

**DoD:** Anmeldung funktioniert; Dummy-Job läuft API → Queue → Worker → Ergebnis in UI; Audit-Eintrag vorhanden.

### Phase 1 – Signing-Service (≈ 3 Wochen) – *MVP-Kern*
- `ISigningProvider` mit Key Vault, Artifact Signing, CertStore, PFX
- Zertifikatsverwaltung in der UI (anlegen, Zweck, Gruppen, Ablaufwarnung)
- Upload & Signieren (Skripte + Binaries), Vorprüfungen, Verifikation, Signaturbericht
- Remediation-Assistent (Detection + Remediation, eigenes Zertifikat)
- CLI + PowerShell-Modul: `sign`

**DoD:** Ein `.ps1` mit Umlauten wird über UI, CLI und PowerShell signiert, `Get-AuthenticodeSignature`
liefert `Valid`, Zeitstempel vorhanden, Bericht und Audit-Eintrag existieren; Remediation-Paar läuft
in Intune mit erzwungener Signaturprüfung.

### Phase 2 – Approval & Notification (≈ 2 Wochen)
- State Machine, mehrstufige Workflows, Vier-Augen-Prinzip
- Kanäle: E-Mail (Graph), Teams Workflows (Adaptive Card mit Buttons), Logic App (Callback), generischer Webhook
- Logic-App-Vorlagen

**DoD:** Signing-Job mit Approval-Pflicht wird per Teams-Karte freigegeben und danach automatisch signiert;
Ablehnung per Mail-Link funktioniert; Logic App kann freigeben.

### Phase 3 – Profile & PSADT-Paketierung (≈ 4 Wochen)
- Software-Profile (CRUD, Revisionen, Vorlagen, Vererbung, Export/Import YAML)
- Quellen: winget, Evergreen, URL, GitHub Releases, Upload
- Versionsmodi Latest / Pinned / Multiple / Ring
- Automatische Analyse (Installer-Typ, Silent-Switches, MSI-Eigenschaften, Icon)
- PSADT-4.1-Generator mit Standard-Bausteinen, Signieren aller Skripte, `.intunewin`-Erzeugung

**DoD:** Für 10 Referenzprodukte (z. B. 7-Zip, Notepad++, VLC, Google Chrome Enterprise MSI,
Adobe Acrobat Reader, Microsoft VC++ Redist, Git, VS Code, Zoom, eine interne MSI) entsteht aus dem
Profil ohne manuelle Eingabe ein signiertes PSADT-Paket inkl. `.intunewin`; „Multiple" baut zwei Versionen parallel.

### Phase 4 – GUI-Builder (≈ 4 Wochen)
- Phasen-Spalten, Drag & Drop, Eigenschaftenformulare aus Schema, Live-Vorschau
- Baustein-Bibliothek (≥ 30 Standard-Bausteine), eigene Bausteine mit Freigabe
- Validierung, Expertenmodus (eigener Code, wird analysiert und signiert)

**DoD:** Paket mit Prozessschließen-Dialog, Registry-Werten (HKLM + alle Benutzer), Repair-Phase und
eigenem Snippet wird nur per GUI gebaut; generiertes Skript besteht PSScriptAnalyzer ohne Fehler.

### Phase 5 – Test-Automation (≈ 2 Wochen)
- Sandbox/Wegwerf-VM, Szenarien Install → Detect → Uninstall → Detect(negativ) → Repair
- Automatische Ableitung von Detection und Uninstall aus der Testinstallation
- Testprotokoll + Screenshots im Approval

**DoD:** Fehlerhafte Detection-Regel wird erkannt und das Paket nicht zur Freigabe gestellt.

### Phase 6 – Veröffentlichung (≈ 3 Wochen)
- Ziele: SMB-Share, GitHub, Azure DevOps (Repos + Universal Packages), Blob, Intune Win32-Upload
- Supersedence und Ringe in Intune

**DoD:** Ein freigegebenes Paket landet gleichzeitig auf Share, in Azure Artifacts und als Win32-App in
Intune (Pilot-Ring zugewiesen); alle Orte im Katalog verlinkt.

### Phase 7 – App Control Integration (≈ 3 Wochen)
- Signer-Erfassung, Regelvorschläge, Supplemental Policy je Produkt, signierte Policy, Intune-Export

**DoD:** Für ein Referenzprodukt entsteht eine Supplemental Policy, mit der das Paket auf einem Gerät
im Enforced-Mode installiert und gestartet werden kann.

### Phase 8 – Tracking, Automation, Betrieb (≈ 3 Wochen)
- Katalog-Dashboard, Reports, Inventarabgleich Intune, Zeitpläne
- GitHub Action, Azure-DevOps-Task, Event-Webhooks vollständig
- Betriebsdoku, Runbooks, Backup/Restore, Monitoring-Alerts

**DoD:** Neue Herstellerversion wird erkannt, vollautomatisch gebaut, getestet, per Teams freigegeben
und in Intune Pilot veröffentlicht – ohne einen manuellen Schritt außer „Freigeben".

**Gesamt:** ca. 26 Wochen für ein Team aus 2–3 Entwicklern; MVP (Phase 0–2) nach ca. 7 Wochen nutzbar.

---

## 9. Teststrategie

- Unit-Tests für Generator (Snapshot-Tests des erzeugten PSADT-Skripts), State Machines, Versionsauflösung
- Integrationstests gegen echte Signing-Provider im Testtenant (Testzertifikat)
- Pester-Tests für generierte PowerShell und Standard-Bausteine
- End-to-End-Tests der Referenzprodukte auf dem Worker (nightly)
- Contract-Tests für API (OpenAPI), CLI und PowerShell-Modul
- Sicherheitstests: Upload-Missbrauch, Callback-Fälschung, Rechteausweitung zwischen Rollen

---

## 10. Annahmen und offene Entscheidungen

Entschieden (Stand 2026-11-17):

1. **Zertifikate:** heute On-Prem-PKI (ADCS) → v1 `CertStoreProvider`; Cloud (Key Vault mit ADCS-CSR,
   Artifact Signing) und externe CA als gleichwertige, umschaltbare Provider (Abschnitt 4.1).
2. **Ein Tenant** zum Start; `TenantId` im Datenmodell vorbereitet.
3. **Betrieb on-prem oder Azure umschaltbar**, Cloud-Integration in beiden Modi (Abschnitt 3.2a).
   **Standard ist reine Cloud** mit Artifact Signing (Private Trust) und Windows-VMSS-Workern (Abschnitt 3.2b).
4. **Freigabestufen 0–3** wie in Abschnitt 4.5 festgelegt.
5. **PSADT 4.1** (wie im MEC Packager) als Paketformat in v1; MSI/MSIX werden in PSADT gekapselt.
6. **Intune** ist das primäre Verteilsystem; **ConfigMgr** als zweites Ziel (Erfahrung aus dem MEC
   Packager vorhanden) in Phase 6 optional.
7. **Teams** über Workflows-Webhooks (Office-365-Connectoren sind abgekündigt).
8. Code liegt vorerst im Ordner `appforge/` dieses Repos; späterer Umzug in ein eigenes Repo `modernwork-appforge` ist vorgesehen.

Noch offen (blockiert v1 nicht):

- Lizenz/Distribution: internes Tool oder Produkt für Kunden?
- Name des Change-Boards und Vertretungsregeln für Stufe 3

---

## 11. Übergabe-Prompt für den Bau

> Baue die Plattform „ModernWork AppForge" gemäß `docs/plans/app-control-packaging-platform.md`
> im Ordner `appforge/`. Beginne mit Phase 0 und Phase 1. Halte dich an die Projektstruktur aus
> Abschnitt 3.3 und die Technologieentscheidungen aus 3.2. Jede Phase endet erst, wenn ihre
> Definition of Done nachweislich erfüllt ist (Tests + kurze Demo-Anleitung im README).
> Offene Entscheidungen aus Abschnitt 10 nicht selbst festlegen, sondern als Konfiguration
> umsetzen oder nachfragen.
