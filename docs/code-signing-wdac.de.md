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
