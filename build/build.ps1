<#
.SYNOPSIS
    Baut ModernWork Elevate: Restore, Build, Tests, Publish, optional Signatur, MSI.

.EXAMPLE
    .\build\build.ps1
    .\build\build.ps1 -NuGetSource https://artifactory.example.com/api/nuget/v3/nuget-remote
    .\build\build.ps1 -Runtime win-arm64
    .\build\build.ps1 -CertificateThumbprint 0123456789ABCDEF0123456789ABCDEF01234567 -SignUnsignedDependencies

.NOTES
    Voraussetzungen: Windows, .NET SDK 10 (global.json), Internetzugang zu nuget.org.
    Für die Signatur: Windows SDK (signtool.exe) und Code-Signing-Zertifikat im Zertifikatsspeicher.
    Pfade bewusst OHNE abschließenden Backslash: Bei Leerzeichen im Pfad setzt PowerShell
    Anführungszeichen, und ein "\" vor dem schließenden Anführungszeichen würde es maskieren.
#>
[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')]
    [string] $Configuration = 'Release',

    [ValidateSet('win-x64', 'win-arm64')]
    [string] $Runtime = 'win-x64',

    [switch] $SkipTests,
    [switch] $SkipInstaller,

    [string] $CertificateThumbprint,
    [string] $TimestampUrl = 'http://timestamp.digicert.com',
    [string] $SignToolPath,

    # Signiert zusätzlich alle Fremd-DLLs/EXEs im Publish-Ordner, die keine gültige Authenticode-Signatur haben.
    [switch] $SignUnsignedDependencies,

    # Eigene Paketquelle(n) statt nuget.org, z. B. ein internes Artifactory/ProGet/Azure-Artifacts-Feed
    # (Spiegel von nuget.org). Ohne Angabe gelten die Quellen aus NuGet.Config.
    [string[]] $NuGetSource
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    [xml] $props = Get-Content (Join-Path $root 'Directory.Build.props')
    $version = ($props.Project.PropertyGroup | Where-Object { $_['Version'] } | Select-Object -First 1)['Version'].InnerText
    $platform = $Runtime.Substring(4)                      # x64 | arm64
    $publishDir = Join-Path $root "artifacts\publish\$Runtime"
    $msiDir = Join-Path $root 'artifacts\msi'
    $appProject = 'src\ModernWorkElevate.App.Avalonia\ModernWorkElevate.App.Avalonia.csproj'
    $testProject = 'src\ModernWorkElevate.Tests\ModernWorkElevate.Tests.csproj'

    Write-Host "== ModernWork Elevate $version ($Configuration, $Runtime)" -ForegroundColor Cyan

    function Invoke-Dotnet {
        & dotnet @args
        if ($LASTEXITCODE -ne 0) { throw "dotnet $($args -join ' ') fehlgeschlagen (Exit $LASTEXITCODE)." }
    }

    # Get-/Set-AuthenticodeSignature stammen aus Microsoft.PowerShell.Security. Wird Windows
    # PowerShell 5.1 aus pwsh 7 gestartet (oder umgekehrt), zeigt PSModulePath auf die Module der
    # anderen Edition und das Autoload scheitert ("module could not be loaded"). Darum explizit
    # die Fassung aus dem eigenen $PSHOME laden.
    function Import-SecurityModule {
        if (Get-Command Get-AuthenticodeSignature -ErrorAction SilentlyContinue) {
            try { Import-Module Microsoft.PowerShell.Security -ErrorAction Stop; return $true } catch { }
        }
        try {
            Import-Module (Join-Path $PSHOME 'Modules\Microsoft.PowerShell.Security\Microsoft.PowerShell.Security.psd1') -ErrorAction Stop
            return $true
        }
        catch {
            Write-Warning "Microsoft.PowerShell.Security nicht ladbar ($($_.Exception.Message)). Tipp: build.ps1 mit pwsh 7 in einem frischen Fenster starten."
            return $false
        }
    }

    # --source für jeden Befehl, der implizit restored (restore, publish mit RID, WiX-Build).
    $sourceArgs = @()
    foreach ($source in @($NuGetSource | Where-Object { $_ })) { $sourceArgs += @('--source', $source) }

    Write-Host '== Restore' -ForegroundColor Cyan
    try {
        Invoke-Dotnet restore ModernWorkElevate.slnx @sourceArgs
    }
    catch {
        Write-Host ''
        Write-Host 'Restore fehlgeschlagen. Bei "NU1301 ... status code 407" verlangt der Proxy eine Anmeldung,' -ForegroundColor Yellow
        Write-Host 'die NuGet nicht liefert. Wege (Details: docs/deployment.md, Abschnitt "Build and sign"):' -ForegroundColor Yellow
        Write-Host '  1. internen NuGet-Feed verwenden:   .\build\build.ps1 -NuGetSource <Feed-URL>' -ForegroundColor Yellow
        Write-Host '  2. einmal ohne Proxy restoren (anderes Netz); danach baut es offline aus dem Paket-Cache' -ForegroundColor Yellow
        Write-Host '  3. Proxy-Ausnahme für *.nuget.org beantragen' -ForegroundColor Yellow
        throw
    }

    Write-Host '== Build (Warnungen = Fehler)' -ForegroundColor Cyan
    Invoke-Dotnet build ModernWorkElevate.slnx -c $Configuration --no-restore

    if (-not $SkipTests) {
        Write-Host '== Tests' -ForegroundColor Cyan
        Invoke-Dotnet test $testProject -c $Configuration --no-build
    }

    Write-Host '== Publish (framework-dependent, RollForward=LatestMajor)' -ForegroundColor Cyan
    if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
    Invoke-Dotnet publish $appProject -c $Configuration -r $Runtime --self-contained false -o $publishDir @sourceArgs

    # Lokale Overrides nie ausliefern (Tenant-/Client-IDs eines Entwicklers).
    Remove-Item (Join-Path $publishDir 'appsettings.local.json') -ErrorAction SilentlyContinue

    if ($CertificateThumbprint) {
        Write-Host '== Signieren' -ForegroundColor Cyan
        $own = Get-ChildItem $publishDir -File | Where-Object { $_.Name -in @('ModernWorkElevate.exe', 'ModernWorkElevate.dll', 'ModernWorkElevate.Core.dll') }
        $toSign = @($own.FullName)

        if ($SignUnsignedDependencies) {
            if (-not (Import-SecurityModule)) { throw '-SignUnsignedDependencies braucht Get-AuthenticodeSignature (Modul Microsoft.PowerShell.Security).' }
            $unsigned = @(Get-ChildItem $publishDir -Recurse -File -Include *.dll, *.exe |
                Where-Object { $_.FullName -notin $toSign -and (Get-AuthenticodeSignature $_.FullName).Status -ne 'Valid' })
            foreach ($file in $unsigned) {
                Write-Host "  unsigniert: $($file.FullName.Substring($publishDir.Length + 1))"
                $toSign += $file.FullName
            }
        }

        & (Join-Path $PSScriptRoot 'sign.ps1') -Files $toSign -CertificateThumbprint $CertificateThumbprint -TimestampUrl $TimestampUrl -SignToolPath $SignToolPath
    }

    # Übersicht für WDAC/AppLocker: welche Binärdateien sind wie signiert. Nur informativ –
    # scheitert sie, läuft der Build weiter.
    $report = Join-Path $root "artifacts\signature-report-$Runtime.csv"
    if (Import-SecurityModule) {
        try {
            Get-ChildItem $publishDir -Recurse -File -Include *.dll, *.exe | ForEach-Object {
                $sig = Get-AuthenticodeSignature $_.FullName
                [pscustomobject]@{
                    File      = $_.FullName.Substring($publishDir.Length + 1)
                    Status    = $sig.Status
                    Publisher = if ($sig.SignerCertificate) { $sig.SignerCertificate.Subject } else { '' }
                }
            } | Export-Csv -Path $report -NoTypeInformation -Encoding utf8
            Write-Host "Signaturbericht: $report"
        }
        catch {
            Write-Warning "Signaturbericht übersprungen: $($_.Exception.Message)"
        }
    }

    if (-not $SkipInstaller) {
        Write-Host '== MSI' -ForegroundColor Cyan

        # WiX-Zwischenstand immer frisch: x64 und arm64 nutzen denselben obj-Ordner, und WiX hielt
        # den zweiten Lauf sonst für aktuell, linkte nicht und fand danach sein MSI nicht
        # ("Could not copy … arm64.msi because it was not found"). Das MSI selbst liegt in artifacts\msi.
        Remove-Item (Join-Path $root 'packaging\msi\obj'), (Join-Path $root 'packaging\msi\bin') -Recurse -Force -ErrorAction SilentlyContinue

        Invoke-Dotnet build packaging\msi\ModernWorkElevate.Installer.wixproj -c Release @sourceArgs `
            "-p:ProductVersion=$version" "-p:PublishDir=$publishDir" "-p:InstallerPlatform=$platform" "-p:OutputPath=$msiDir"

        $msi = Join-Path $msiDir "ModernWorkElevate-$version-$platform.msi"
        if ($CertificateThumbprint) {
            & (Join-Path $PSScriptRoot 'sign.ps1') -Files $msi -CertificateThumbprint $CertificateThumbprint -TimestampUrl $TimestampUrl -SignToolPath $SignToolPath
        }

        # Erkennungsskript mit passender Version, Bereinigungsskript und Richtlinienvorlagen neben das MSI legen.
        $detect = Get-Content (Join-Path $root 'packaging\scripts\Detect-ModernWorkElevate.ps1') -Raw
        $detect = $detect -replace "\[version\]'[0-9.]+'", "[version]'$version'"
        Set-Content -Path (Join-Path $msiDir 'Detect-ModernWorkElevate.ps1') -Value $detect -Encoding utf8
        Copy-Item (Join-Path $root 'packaging\scripts\Remove-ModernWorkElevateUserData.ps1') $msiDir -Force
        Copy-Item (Join-Path $root 'packaging\policy') (Join-Path $msiDir 'policy') -Recurse -Force

        Write-Host "MSI: $msi" -ForegroundColor Green
    }
}
finally {
    Pop-Location
}
