# Code signing, App Control for Business (WDAC) and AppLocker

*Deutsch: [code-signing-wdac.de.md](code-signing-wdac.de.md)*

## Signing

```powershell
pwsh -NoProfile -File .\build\build.ps1 -CertificateThumbprint <SHA1> [-SignUnsignedDependencies]
```

- Signs `ModernWorkElevate.exe`, `ModernWorkElevate.dll`, `ModernWorkElevate.Core.dll` and the MSI
  (SHA-256, RFC 3161 timestamp), then verifies with `signtool verify /pa`.
- `-SignUnsignedDependencies` additionally signs every DLL/EXE in the publish folder that has **no
  valid** Authenticode signature — typically Avalonia, CommunityToolkit, Serilog and the ANGLE
  library. Files already signed by Microsoft (MSAL, `msalruntime.dll`, `libSkiaSharp.dll`,
  `libHarfBuzzSharp.dll`, `WinRT.Runtime.dll`, …) are left untouched.
- `artifacts\signature-report-<rid>.csv` lists every binary with status and publisher. Check it
  before writing rules — the signature situation can change with package versions.
- Set `Company`/`Authors` in `Directory.Build.props` to match your certificate's subject, so
  publisher-based rules and inventory tools stay consistent.
- **signtool.exe** is looked up in `PATH`, the Windows SDK and the NuGet package
  `Microsoft.Windows.SDK.BuildTools` (or pass `-SignToolPath`). Without it, `sign.ps1` falls back to
  `Set-AuthenticodeSignature` with the same certificate. To install signtool:
  `winget install Microsoft.WindowsSDK.10.0.26100` (the "Signing Tools for Desktop Apps" feature is
  enough).
- If a proxy blocks the timestamp service, pass an internally reachable RFC 3161 service with
  `-TimestampUrl`. `-TimestampUrl ''` signs without a timestamp — test builds only, the signature
  then expires with the certificate.

## Native components

Besides .NET assemblies the app loads these native DLLs from its program folder:

| File | Origin | Purpose |
| --- | --- | --- |
| `libSkiaSharp.dll` | SkiaSharp (Microsoft) | Rendering (Avalonia) |
| `libHarfBuzzSharp.dll` | HarfBuzzSharp (Microsoft) | Text shaping (Avalonia) |
| `av_libglesv2.dll` | Avalonia (ANGLE) | GPU rendering via Direct3D |
| `msalruntime.dll` | Microsoft.Identity.Client.NativeInterop | WAM broker |

If `av_libglesv2.dll` is blocked, Avalonia falls back to software rendering — the app keeps working,
just draws slower. The other three are required.

Avalonia also ships assemblies for Linux and macOS (`Avalonia.X11`, `Avalonia.Native`,
`Tmds.DBus.Protocol`, …). They are never loaded on Windows but are part of the package; sign or allow
them like the rest.

## App Control for Business (WDAC) — recommended rules

1. **Publisher rule** for your own certificate (level *Publisher* or *FilePublisher* with product
   name "ModernWork Elevate"). With `-SignUnsignedDependencies` this covers every formerly unsigned
   file.
2. **Microsoft publisher rule** (already part of most base policies) for MSAL, SkiaSharp,
   HarfBuzzSharp and .NET.
3. Without signing third-party files yourself: **hash rules** for the files the signature report
   lists as `NotSigned` — regenerate them for every release.
4. Optionally a **path rule** `%PROGRAMFILES%\ModernWork Elevate\*` — sensible only because standard
   users cannot write there (WDAC checks this at runtime).

Supplemental policy from a reference installation (ConfigCI module):

```powershell
$scan = 'C:\Program Files\ModernWork Elevate'
New-CIPolicy -FilePath .\ModernWorkElevate.xml -ScanPath $scan -Level FilePublisher -Fallback Hash -UserPEs -MultiplePolicyFormat
Set-CIPolicyIdInfo -FilePath .\ModernWorkElevate.xml -PolicyName 'ModernWork Elevate' -SupplementsBasePolicyID <BasePolicyGuid>
ConvertFrom-CIPolicy -XmlFilePath .\ModernWorkElevate.xml -BinaryFilePath .\ModernWorkElevate.cip
```

Test in audit mode first (CodeIntegrity events 3076/3077).

## AppLocker

- **Executable rules:** publisher rule on your certificate (product "ModernWork Elevate") or path rule
  `%PROGRAMFILES%\ModernWork Elevate\*`.
- **DLL rules** (if enabled): the same publisher rule plus the Microsoft publisher; without signing
  third-party DLLs, path or hash rules.
- **Windows Installer rules:** publisher rule for the signed MSI.

## Why no single-file publish and no Native AOT

A single-file build would extract `msalruntime.dll` and the Skia libraries to `%TEMP%` — a
user-writable location that defeats WDAC/AppLocker rules anchored on Program Files. Avalonia and
MSAL with the broker are not AOT-compatible either.
