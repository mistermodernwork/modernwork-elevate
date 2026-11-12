<#
.SYNOPSIS
    Signiert Dateien mit signtool (SHA-256, RFC-3161-Zeitstempel) und prüft die Signatur.

.DESCRIPTION
    Signiert werden nur eigene Dateien (ModernWorkElevate.exe, ModernWorkElevate.Core.dll, MSI).
    Bereits signierte Fremdbibliotheken (Microsoft: MSAL, msalruntime.dll, SkiaSharp/HarfBuzzSharp u. a.)
    werden NICHT neu signiert. Unsignierte Fremd-DLLs (z. B. Avalonia, Serilog) signiert build.ps1 mit
    -SignUnsignedDependencies zusätzlich mit dem eigenen Zertifikat (siehe docs/code-signing-wdac.md).

    signtool.exe wird gesucht in: PATH, Windows SDK (Windows Kits\10\bin), NuGet-Paket
    Microsoft.Windows.SDK.BuildTools im Paket-Cache. Ohne signtool wird mit dem PowerShell-Cmdlet
    Set-AuthenticodeSignature signiert (SHA-256, gleiches Zertifikat; Ergebnis für WDAC/AppLocker gleichwertig).

.PARAMETER TimestampUrl
    Zeitstempeldienst. Leer = ohne Zeitstempel (Signatur wird mit Ablauf des Zertifikats ungültig –
    nur für Tests, z. B. wenn der Proxy den Dienst blockiert).

.PARAMETER CertificateThumbprint
    SHA1-Fingerabdruck des Code-Signing-Zertifikats (CurrentUser\My oder LocalMachine\My,
    auch HSM/Token-Zertifikate mit CSP/KSP).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string[]] $Files,
    [Parameter(Mandatory)] [string] $CertificateThumbprint,
    [string] $TimestampUrl = 'http://timestamp.digicert.com',
    [string] $SignToolPath
)

$ErrorActionPreference = 'Stop'

function Find-SignTool {
    if ($SignToolPath) { return $SignToolPath }

    $cmd = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    $arch = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'arm64' } else { 'x64' }
    $roots = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'),
        (Join-Path $env:USERPROFILE '.nuget\packages\microsoft.windows.sdk.buildtools')
    )
    foreach ($root in $roots) {
        if (-not (Test-Path $root)) { continue }
        $candidate = Get-ChildItem $root -Filter signtool.exe -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match "\\$arch\\" } |
            Sort-Object FullName -Descending |
            Select-Object -First 1
        if ($candidate) { return $candidate.FullName }
    }
    return $null
}

function Get-SigningCertificate {
    foreach ($store in 'Cert:\CurrentUser\My', 'Cert:\LocalMachine\My') {
        $cert = Get-Item -LiteralPath (Join-Path $store $CertificateThumbprint) -ErrorAction SilentlyContinue
        if ($cert) {
            if (-not $cert.HasPrivateKey) { throw "Zertifikat $CertificateThumbprint in $store hat keinen privaten Schlüssel." }
            return $cert
        }
    }
    throw "Zertifikat $CertificateThumbprint weder in CurrentUser\My noch in LocalMachine\My gefunden."
}

$signtool = Find-SignTool
if ($signtool) {
    # Ein Aufruf für alle Dateien: ein Zeitstempel-Durchlauf statt einer Verbindung pro Datei.
    Write-Host "Signiere $($Files.Count) Datei(en) mit $signtool"
    $timestampArgs = if ($TimestampUrl) { @('/tr', $TimestampUrl, '/td', 'SHA256') } else { @() }
    & $signtool sign /sha1 $CertificateThumbprint /fd SHA256 @timestampArgs /d 'ModernWork Elevate' @Files
    if ($LASTEXITCODE -ne 0) { throw 'Signatur fehlgeschlagen (signtool).' }

    & $signtool verify /pa /q @Files
    if ($LASTEXITCODE -ne 0) { throw 'Signaturprüfung fehlgeschlagen (signtool).' }
    return
}

Write-Warning 'signtool.exe nicht gefunden – signiere mit Set-AuthenticodeSignature. (Optional: Windows SDK "Signing Tools" installieren.)'
# Explizit aus dem eigenen $PSHOME laden (PSModulePath kann auf die andere PowerShell-Edition zeigen).
try { Import-Module Microsoft.PowerShell.Security -ErrorAction Stop }
catch { Import-Module (Join-Path $PSHOME 'Modules\Microsoft.PowerShell.Security\Microsoft.PowerShell.Security.psd1') -ErrorAction Stop }
$cert = Get-SigningCertificate
foreach ($file in $Files) {
    $params = @{ FilePath = $file; Certificate = $cert; HashAlgorithm = 'SHA256'; IncludeChain = 'NotRoot' }
    if ($TimestampUrl) { $params.TimestampServer = $TimestampUrl }
    $result = Set-AuthenticodeSignature @params
    if ($result.Status -ne 'Valid') {
        throw "Signatur fehlgeschlagen: $file ($($result.Status): $($result.StatusMessage))"
    }
    Write-Host "  signiert: $(Split-Path $file -Leaf)"
}
