<#
.SYNOPSIS
    Erkennungsregel für SCCM (Script Detection) und Intune (Win32-App, benutzerdefiniertes Erkennungsskript).

.DESCRIPTION
    "Installiert", wenn
      - HKLM\SOFTWARE\ModernWorkElevate\Version (vom MSI geschrieben) >= $RequiredVersion ist und
      - ModernWorkElevate.exe im Installationsordner existiert.
    Liest explizit die 64-Bit-Registry, funktioniert also auch in einem 32-Bit-Prozess (Intune-Agent).

    SCCM:   Ausgabe auf STDOUT + Exit 0  = installiert; keine Ausgabe = nicht installiert.
    Intune: Ausgabe auf STDOUT + Exit 0  = erkannt.

    build\build.ps1 legt eine Kopie mit der passenden Version neben das MSI.
    Alternativ ohne Skript: MSI-Produktcode (Intune "MSI"-Erkennung) oder
    Registry HKLM\SOFTWARE\ModernWorkElevate, Wert Version, Versionsvergleich >= Release.
#>
$RequiredVersion = [version]'1.3.6'

try {
    $hklm = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, [Microsoft.Win32.RegistryView]::Registry64)
    $key = $hklm.OpenSubKey('SOFTWARE\ModernWorkElevate')
    if ($null -eq $key) { exit 0 }

    $installed = [version]$key.GetValue('Version')
    $installPath = [string]$key.GetValue('InstallPath')
    $key.Dispose()
    $hklm.Dispose()

    if ([string]::IsNullOrWhiteSpace($installPath)) {
        $installPath = Join-Path $env:ProgramW6432 'ModernWork Elevate'
    }

    $exe = Join-Path $installPath 'ModernWorkElevate.exe'
    if ($installed -ge $RequiredVersion -and (Test-Path -LiteralPath $exe -PathType Leaf)) {
        Write-Output "ModernWork Elevate $installed installed"
    }
}
catch {
    # Ungültige Werte gelten als "nicht installiert".
}
exit 0
