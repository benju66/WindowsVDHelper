# Builds the app and packs a release zip (release\WindowsVDHelper-<version>.zip) with the installer:
# unzip, double-click Install.cmd. Usage: powershell -ExecutionPolicy Bypass -File Scripts\package-release.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'build-local.ps1')
if ($LASTEXITCODE -ne 0) { throw 'The build failed.' }

$exe = Join-Path $root 'dist\WindowsVirtualDesktopHelper.exe'
$version = (Get-Item $exe).VersionInfo.FileVersion -replace '\.0$', ''
$staging = Join-Path $root "release\WindowsVDHelper-$version"
Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $staging | Out-Null

Copy-Item $exe, (Join-Path $root 'dist\WindowsVirtualDesktopHelper.exe.config') $staging
Copy-Item (Join-Path $PSScriptRoot 'install.ps1') $staging
Copy-Item (Join-Path $root 'LICENSE.md') $staging
# In the release the installer sits next to the app files
[IO.File]::WriteAllText((Join-Path $staging 'Install.cmd'), "@echo off`r`nrem Installs or updates Windows Virtual Desktop Helper for the current user (no admin rights needed)`r`npowershell -NoProfile -ExecutionPolicy Bypass -File `"%~dp0install.ps1`"`r`necho.`r`npause`r`n")
[IO.File]::WriteAllText((Join-Path $staging 'Uninstall.cmd'), "@echo off`r`nrem Removes Windows Virtual Desktop Helper (your settings are kept)`r`npowershell -NoProfile -ExecutionPolicy Bypass -File `"%~dp0install.ps1`" -Uninstall`r`necho.`r`npause`r`n")
[IO.File]::WriteAllText((Join-Path $staging 'README.txt'), @"
Windows Virtual Desktop Helper $version

Install: double-click Install.cmd (no admin rights needed). It installs to
%LOCALAPPDATA%\Programs\WindowsVDHelper, adds a Start menu shortcut, starts
with Windows and starts the app. Run it again to update.

Uninstall: double-click Uninstall.cmd.

Requires Windows 10/11 (the desktop and window features need Windows 11 24H2 or later).
Right-click the desktop number in the tray for the panel; Settings are in the panel's footer.
The PowerToys Command Palette extension is in the source code (CommandPalette folder).
"@.Replace("`n", "`r`n"))

$zip = Join-Path $root "release\WindowsVDHelper-$version.zip"
Remove-Item $zip -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zip
Write-Host "Release: $zip"
