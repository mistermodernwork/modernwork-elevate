<#
.SYNOPSIS
    Entfernt die Benutzerdaten und Anmeldungen von ModernWork Elevate.

.DESCRIPTION
    Die MSI-Deinstallation lässt Benutzerdaten bewusst stehen: Ein per-machine-Installer läuft als
    SYSTEM und erreicht weder die Benutzerprofile zuverlässig noch die Anmeldungen der Benutzer im
    Windows-Tokenbroker (WAM).

    Modus 1 (Standard, im Benutzerkontext ausführen – z. B. Intune "Als angemeldeter Benutzer"
    oder Anmeldeskript), VOR der MSI-Deinstallation:
      - beendet die eigene laufende Instanz,
      - startet "ModernWorkElevate.exe --remove-user-data": meldet die Konten der App im
        Tokenbroker ab, löscht %APPDATA%\ModernWorkElevate und %LOCALAPPDATA%\ModernWorkElevate
        und den Autostart-Eintrag (HKCU\...\Run).

    Modus 2 (-AllProfiles, als SYSTEM/Administrator – z. B. nach der MSI-Deinstallation):
      - löscht in allen lokalen Profilen die beiden Datenordner und, für geladene Profile, den
        Autostart-Eintrag. Die WAM-Anmeldungen bleiben in diesem Modus bestehen (sie gehören
        dem Benutzer); sie enthalten keine Daten der App und werden ohne App nicht verwendet.
      Folgt keinen Junctions/Symlinks.

.PARAMETER AllProfiles
    Modus 2 (siehe oben).

.EXAMPLE
    powershell.exe -ExecutionPolicy Bypass -File .\Remove-ModernWorkElevateUserData.ps1
.EXAMPLE
    powershell.exe -ExecutionPolicy Bypass -File .\Remove-ModernWorkElevateUserData.ps1 -AllProfiles -WhatIf
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [switch] $AllProfiles
)

$ErrorActionPreference = 'Stop'
$folderName = 'ModernWorkElevate'
$runValue = 'ModernWork Elevate'

if (-not $AllProfiles) {
    $hklm = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, [Microsoft.Win32.RegistryView]::Registry64)
    $key = $hklm.OpenSubKey('SOFTWARE\ModernWorkElevate')
    $installPath = if ($key) { [string]$key.GetValue('InstallPath') } else { $null }
    if ([string]::IsNullOrWhiteSpace($installPath)) { $installPath = Join-Path $env:ProgramW6432 'ModernWork Elevate' }
    $exe = Join-Path $installPath 'ModernWorkElevate.exe'

    # Nur die Instanz der eigenen Sitzung beenden.
    $session = [System.Diagnostics.Process]::GetCurrentProcess().SessionId
    Get-Process -Name 'ModernWorkElevate' -ErrorAction SilentlyContinue |
        Where-Object { $_.SessionId -eq $session } |
        ForEach-Object {
            if ($PSCmdlet.ShouldProcess("PID $($_.Id)", 'Beenden')) { $_ | Stop-Process -Force }
        }

    if (Test-Path -LiteralPath $exe -PathType Leaf) {
        if ($PSCmdlet.ShouldProcess($exe, '--remove-user-data')) {
            $p = Start-Process -FilePath $exe -ArgumentList '--remove-user-data' -Wait -PassThru
            Write-Output "ModernWork Elevate: Benutzerdaten entfernt (Exit $($p.ExitCode))."
        }
        exit 0
    }

    # App nicht (mehr) installiert: Ordner und Autostart direkt entfernen (ohne WAM-Abmeldung).
    foreach ($dir in @((Join-Path $env:APPDATA $folderName), (Join-Path $env:LOCALAPPDATA $folderName))) {
        if ((Test-Path -LiteralPath $dir) -and $PSCmdlet.ShouldProcess($dir, 'Löschen')) {
            Remove-Item -LiteralPath $dir -Recurse -Force
            Write-Output "Gelöscht: $dir"
        }
    }
    $run = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    if ((Get-ItemProperty -LiteralPath $run -Name $runValue -ErrorAction SilentlyContinue) -and $PSCmdlet.ShouldProcess("$run\$runValue", 'Löschen')) {
        Remove-ItemProperty -LiteralPath $run -Name $runValue
    }
    exit 0
}

$profileList = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList'
Get-ChildItem $profileList | ForEach-Object {
    $sid = $_.PSChildName
    if ($sid -notmatch '^S-1-(5-21|12-1)-') { return }   # lokale/AD- und Entra-ID-Benutzerkonten

    $profilePath = (Get-ItemProperty -LiteralPath $_.PSPath -Name ProfileImagePath -ErrorAction SilentlyContinue).ProfileImagePath
    if (-not $profilePath) { return }

    foreach ($dir in @((Join-Path $profilePath "AppData\Roaming\$folderName"), (Join-Path $profilePath "AppData\Local\$folderName"))) {
        if (-not (Test-Path -LiteralPath $dir)) { continue }
        $item = Get-Item -LiteralPath $dir -Force
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
            Write-Warning "Übersprungen (Reparse Point): $dir"
        }
        elseif ($PSCmdlet.ShouldProcess($dir, 'Löschen')) {
            Remove-Item -LiteralPath $dir -Recurse -Force
            Write-Output "Gelöscht: $dir"
        }
    }

    $runKey = "Registry::HKEY_USERS\$sid\Software\Microsoft\Windows\CurrentVersion\Run"
    if ((Test-Path -LiteralPath $runKey) -and (Get-ItemProperty -LiteralPath $runKey -Name $runValue -ErrorAction SilentlyContinue)) {
        if ($PSCmdlet.ShouldProcess("$runKey\$runValue", 'Löschen')) {
            Remove-ItemProperty -LiteralPath $runKey -Name $runValue
            Write-Output "Autostart entfernt: $sid"
        }
    }
}
