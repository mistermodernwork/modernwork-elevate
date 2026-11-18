# Code-Signatur, WDAC (App Control for Business) und AppLocker

## Signieren

```powershell
.\build\build.ps1 -CertificateThumbprint <SHA1> [-SignUnsignedDependencies]
```

- Signiert `ModernWorkElevate.exe`, `ModernWorkElevate.Core.dll` und das MSI (SHA-256,
  RFC-3161-Zeitstempel), danach `signtool verify /pa`.
- `-SignUnsignedDependencies` signiert zusätzlich jede DLL/EXE im Publish-Ordner, die **keine gültige**
  Authenticode-Signatur hat (typisch: Avalonia, CommunityToolkit, Serilog). Bereits signierte
  Microsoft-Dateien (MSAL, `msalruntime.dll`, `libSkiaSharp.dll`, `libHarfBuzzSharp.dll`, `WinRT.Runtime.dll`
  usw.) werden nicht angefasst.
- `artifacts\signature-report-<rid>.csv` listet für jede Binärdatei Status und Herausgeber – vor dem
  Erstellen der Regeln prüfen, weil sich die Signaturlage mit Paketversionen ändern kann.
- `Company`/`Authors` in `Directory.Build.props` an den CN des Zertifikats anpassen.
- **signtool.exe** wird im PATH, im Windows SDK und im NuGet-Paket `Microsoft.Windows.SDK.BuildTools`
  gesucht (oder `-SignToolPath`). Fehlt es, signiert `sign.ps1` mit `Set-AuthenticodeSignature`
  (gleiches Ergebnis). Installation, falls gewünscht: `winget install Microsoft.WindowsSDK.10.0.26100`
  (Komponente „Windows SDK Signing Tools for Desktop Apps" genügt).
- Blockiert der Proxy den Zeitstempeldienst, schlägt das Signieren fehl. Dann einen intern erreichbaren
  RFC-3161-Dienst mit `-TimestampUrl` angeben; `-TimestampUrl ''` signiert ohne Zeitstempel (nur Test).

## Signierte Releases auf GitHub (Azure Artifact Signing)

Der Release-Workflow (`.github/workflows/release.yml`) signiert mit
[Azure Artifact Signing](https://learn.microsoft.com/azure/artifact-signing/) (früher Trusted
Signing), sobald es eingerichtet ist: keine Zertifikatsdatei und kein langlebiges Secret – der Job
meldet sich per GitHub-OIDC an, der private Schlüssel verlässt die HSMs von Microsoft nie. Bis dahin
baut er wie bisher unsigniert. Signiert werden dieselben Dateien wie lokal mit
`-SignUnsignedDependencies`, danach das MSI.

Einmalige Einrichtung:

1. **Artifact-Signing-Konto** in einem Azure-Abonnement. Die Region bestimmt den Endpunkt für
   `ARTIFACT_SIGNING_ENDPOINT`, z. B. Westeuropa → `https://weu.codesigning.azure.net`
   ([alle Regionen](https://learn.microsoft.com/azure/artifact-signing/how-to-signing-integrations#create-a-json-file)).
2. **Identitätsprüfung** vom Typ *Public Trust*. Möglich für Organisationen in den USA, Kanada, der
   EU und Großbritannien mit mindestens drei Jahren nachprüfbarer Steuerhistorie sowie für
   Einzelpersonen in den USA und Kanada; sie dauert einige Werktage.
3. **Zertifikatprofil** vom Typ *Public Trust*.
4. **Entra-App-Registrierung für die Pipeline**, ohne Geheimnis: *Zertifikate & Geheimnisse →
   Verbundanmeldeinformationen → GitHub Actions deploying Azure resources*, Organisation
   `mistermodernwork`, Repository `modernwork-elevate`, Entität *Environment*, Name `release`.
5. **Rolle** *Artifact Signing Certificate Profile Signer* (früher *Trusted Signing Certificate
   Profile Signer*) für diese App auf dem Zertifikatprofil.
6. **GitHub-Umgebung** `release` (*Settings → Environments*) mit den Secrets `AZURE_CLIENT_ID`,
   `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `ARTIFACT_SIGNING_ENDPOINT`,
   `ARTIFACT_SIGNING_ACCOUNT`, `ARTIFACT_SIGNING_PROFILE`. **Required reviewers** eintragen, damit
   jedes signierte Release von einer Person freigegeben wird, und **Deployment branches and tags**
   auf das Tag-Muster `v*` beschränken – sonst könnte ein manueller Lauf von jedem Branch signieren.

Das nächste Tag `v*` veröffentlicht dann signierte MSIs; `signature-report-<rid>.csv` im Release
zeigt für jede Datei den Herausgeber. Lokal mit demselben Profil signieren geht nach `az login`
(das eigene Konto braucht dieselbe Rolle):

```powershell
.\build\build.ps1 -ArtifactSigningMetadata .\metadata.json -ArtifactSigningDlib <pfad>\bin\x64\Azure.CodeSigning.Dlib.dll -SignUnsignedDependencies
```

`metadata.json` enthält `Endpoint`, `CodeSigningAccountName` und `CertificateProfileName`; die dlib
stammt aus dem NuGet-Paket `Microsoft.ArtifactSigning.Client`, signtool aus
`Microsoft.Windows.SDK.BuildTools` (10.0.22621.755 oder neuer). Zeitstempel ist standardmäßig
`http://timestamp.acs.microsoft.com` – Pflicht, weil Artifact-Signing-Zertifikate nur 72 Stunden gelten.

## Native Komponenten

Die App lädt neben .NET-Assemblys diese nativen DLLs aus dem Programmordner:

| Datei | Herkunft | Zweck |
| --- | --- | --- |
| `libSkiaSharp.dll` | SkiaSharp (Microsoft) | Rendering (Avalonia) |
| `libHarfBuzzSharp.dll` | HarfBuzzSharp (Microsoft) | Textlayout (Avalonia) |
| `av_libglesv2.dll` | Avalonia (ANGLE) | GPU-Rendering über Direct3D |
| `msalruntime.dll` | Microsoft.Identity.Client.NativeInterop | WAM-Broker |

Ist `av_libglesv2.dll` nicht signiert und wird nicht zugelassen, fällt Avalonia auf Software-Rendering
zurück; die App bleibt nutzbar, zeichnet aber langsamer. Die anderen drei sind zwingend.

## WDAC-Regeln (Empfehlung)

1. **Herausgeberregel** für das eigene Zertifikat (Ebene *Publisher* bzw. *FilePublisher* mit
   Produktname „ModernWork Elevate"). Mit `-SignUnsignedDependencies` deckt sie alle bisher
   unsignierten Dateien ab.
2. **Herausgeberregel Microsoft** (in den meisten Basisrichtlinien bereits enthalten) für MSAL,
   SkiaSharp, HarfBuzzSharp und .NET.
3. Ohne Eigensignatur der Fremd-DLLs: **Hash-Regeln** für die im Signaturbericht als `NotSigned`
   gelisteten Dateien – diese müssen bei jedem Update neu erzeugt werden.
4. Optional **Pfadregel** `%PROGRAMFILES%\ModernWork Elevate\*` (nur sinnvoll, weil Standardbenutzer
   dort nicht schreiben können; WDAC prüft das zur Laufzeit).

Beispiel (PowerShell, ConfigCI-Modul) für eine ergänzende Richtlinie aus einer Referenzinstallation:

```powershell
$scan = 'C:\Program Files\ModernWork Elevate'
New-CIPolicy -FilePath .\ModernWorkElevate.xml -ScanPath $scan -Level FilePublisher -Fallback Hash -UserPEs -MultiplePolicyFormat
Set-CIPolicyIdInfo -FilePath .\ModernWorkElevate.xml -PolicyName 'ModernWork Elevate' -SupplementsBasePolicyID <BasePolicyGuid>
ConvertFrom-CIPolicy -XmlFilePath .\ModernWorkElevate.xml -BinaryFilePath .\ModernWorkElevate.cip
```

Die Policy-Datei vor der Verteilung im Audit-Modus testen (CodeIntegrity-Ereignisse 3076/3077).

**Mit Azure Artifact Signing signierte Releases:** Das Signaturzertifikat wird täglich erneuert –
Regeln daher nie an einen Fingerabdruck oder ein einzelnes Zertifikat binden. Microsoft empfiehlt,
das Vertrauen an die feste EKU des Zertifikatprofils zu knüpfen (`1.3.6.1.4.1.311.97.…`, in den
Signaturdetails der Datei unter *Erweiterte Schlüsselverwendung*), die über die gesamte Lebensdauer
des Profils gleich bleibt – siehe
[Artifact Signing certificate management](https://learn.microsoft.com/azure/artifact-signing/concept-certificate-management#certificate-profile-eku).

## AppLocker

- **Executable rules:** Herausgeberregel auf das eigene Zertifikat (Produkt „ModernWork Elevate") oder
  Pfadregel `%PROGRAMFILES%\ModernWork Elevate\*`.
- **DLL rules** (falls aktiviert): dieselbe Herausgeberregel plus Microsoft-Herausgeber; ohne Eigensignatur
  der Fremd-DLLs Pfad- oder Hash-Regeln.
- **Windows Installer rules:** Herausgeberregel für das signierte MSI.

## Warum kein Single-File und kein Native AOT

`msalruntime.dll` und die Skia-DLLs müssten bei Single-File nach `%TEMP%` entpackt werden – ein vom
Benutzer beschreibbarer Ort, der WDAC-/AppLocker-Regeln auf Program Files aushebelt. Avalonia und
MSAL mit Broker sind zudem nicht AOT-tauglich.
