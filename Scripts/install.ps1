<#
.SYNOPSIS
  Installs (or updates, or uninstalls) Windows Virtual Desktop Helper for the current user.

.DESCRIPTION
  Double-click Install.cmd / Uninstall.cmd instead of running this directly. No admin rights needed.
    - copies the app to %LOCALAPPDATA%\Programs\WindowsVDHelper
    - adds a Start menu shortcut and starts the app with Windows
    - starts the app
    - optionally builds and registers the Command Palette extension (from the source code; needs the
      .NET SDK and Windows Developer Mode)
  Your settings (%APPDATA%\WindowsVirtualDesktopHelper) are kept when updating or uninstalling.

.PARAMETER Uninstall
  Removes the app, the shortcut, the startup entry and the Command Palette extension.

.PARAMETER SkipExtension
  Don't offer to install the Command Palette extension.
#>
param([switch] $Uninstall, [switch] $SkipExtension)

$ErrorActionPreference = 'Stop'
$appName = 'Windows Virtual Desktop Helper' # also the name of the startup entry the app itself uses
$exeName = 'WindowsVirtualDesktopHelper.exe'
$installDir = Join-Path $env:LOCALAPPDATA 'Programs\WindowsVDHelper'
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'Windows VD Helper.lnk'
$runKey = 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run'

function Stop-App {
    Get-Process 'WindowsVirtualDesktopHelper' -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 500
}

if ($Uninstall) {
    Write-Host "Uninstalling $appName..."
    Stop-App
    Remove-ItemProperty -Path $runKey -Name $appName -ErrorAction SilentlyContinue
    Remove-Item $shortcut -ErrorAction SilentlyContinue
    Remove-Item $installDir -Recurse -Force -ErrorAction SilentlyContinue
    $extension = Get-AppxPackage -Name 'CmdPalVirtualDesktops' -ErrorAction SilentlyContinue
    if ($extension) {
        Get-Process 'CmdPalVirtualDesktops' -ErrorAction SilentlyContinue | Stop-Process -Force
        Remove-AppxPackage $extension.PackageFullName
        Write-Host 'Removed the Command Palette extension.'
    }
    Write-Host "Done. Your settings are still in $env:APPDATA\WindowsVirtualDesktopHelper (delete that folder to remove them too)." -ForegroundColor Green
    return
}

# Where are the app files? Next to this script (downloaded release), or in the source code's dist folder
$root = $PSScriptRoot
$sourceRoot = Split-Path $root -Parent
$isSource = Test-Path (Join-Path $sourceRoot 'Source\WindowsVirtualDesktopHelper.csproj')
if ($isSource) {
    if (Get-Command dotnet -ErrorAction SilentlyContinue) {
        Write-Host 'Building the app from the source code...'
        Stop-App # the build copies over dist\, which is locked while the app runs from there
        & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'build-local.ps1')
        if ($LASTEXITCODE -ne 0) { throw 'The build failed.' }
    }
    $appDir = Join-Path $sourceRoot 'dist'
} else {
    $appDir = $root
}
$exe = Join-Path $appDir $exeName
if (-not (Test-Path $exe)) { throw "Can't find $exeName in $appDir." }

Write-Host "Installing $appName to $installDir..."
Stop-App
New-Item -ItemType Directory -Force $installDir | Out-Null
Copy-Item $exe $installDir -Force
Copy-Item (Join-Path $appDir "$exeName.config") $installDir -Force -ErrorAction SilentlyContinue
$installedExe = Join-Path $installDir $exeName
Unblock-File $installedExe -ErrorAction SilentlyContinue # downloaded zips are marked as "from the internet"

# Start menu shortcut
$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut($shortcut)
$link.TargetPath = $installedExe
$link.WorkingDirectory = $installDir
$link.Description = 'Virtual desktop number in the tray, switching, moving and pinning windows'
$link.Save()

# Start with Windows (the same entry as the app's own "Startup with Windows" setting)
New-Item -Path $runKey -Force | Out-Null
Set-ItemProperty -Path $runKey -Name $appName -Value "`"$installedExe`""

Start-Process $installedExe
Write-Host "Installed and started. It starts with Windows and is in the Start menu as 'Windows VD Helper'." -ForegroundColor Green

# Optional: the Command Palette extension (built from the source code)
$extensionScript = Join-Path $sourceRoot 'CommandPalette\build.ps1'
if ($isSource -and -not $SkipExtension -and (Test-Path $extensionScript)) {
    $hasCmdPal = [bool](Get-AppxPackage -Name 'Microsoft.CommandPalette*' -ErrorAction SilentlyContinue) -or (Test-Path (Join-Path $env:LOCALAPPDATA 'PowerToys\PowerToys.exe')) -or (Test-Path "$env:ProgramFiles\PowerToys\PowerToys.exe")
    $devMode = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock' -ErrorAction SilentlyContinue).AllowDevelopmentWithoutDevLicense -eq 1
    $hasSdk = [bool](Get-Command dotnet -ErrorAction SilentlyContinue)
    if ($hasCmdPal -and $devMode -and $hasSdk) {
        $answer = Read-Host 'Also install the PowerToys Command Palette extension? (Y/n)'
        if ($answer -notmatch '^[nN]') {
            & powershell -NoProfile -ExecutionPolicy Bypass -File $extensionScript
            if ($LASTEXITCODE -eq 0) { Write-Host 'In Command Palette, run "Reload" once to load it.' -ForegroundColor Green }
        }
    } elseif ($hasCmdPal) {
        Write-Host 'Skipped the Command Palette extension (it needs the .NET SDK and Windows Developer Mode).'
    }
}
