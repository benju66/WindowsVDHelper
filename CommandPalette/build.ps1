<#
.SYNOPSIS
  Builds the Virtual Desktops Command Palette extension and registers it with Windows.

.DESCRIPTION
  Works without Visual Studio or an installed Windows SDK (same approach as the Bookmark Tree extension):
    1. builds the project,
    2. finalizes Package.appxmanifest into AppxManifest.xml next to the build output,
    3. registers that folder as a development package (needs Windows Developer Mode).
  Afterwards run "Reload" (Reload Command Palette extensions) in Command Palette.
  The extension needs the Windows Virtual Desktop Helper app to be running.

.PARAMETER Configuration
  Debug (default) or Release.

.PARAMETER NoRegister
  Build and lay out only.
#>
param(
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Debug',
    [switch] $NoRegister
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$name = 'CmdPalVirtualDesktops'
$proj = Join-Path $root $name

if (-not (Test-Path (Join-Path $proj 'Assets\Icon.png'))) {
    & (Join-Path $root 'make-icons.ps1')
}

# The running extension holds the exe open
Get-Process $name -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 300

dotnet build (Join-Path $proj "$name.csproj") -c $Configuration -p:Platform=x64 --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

$out = Get-ChildItem (Join-Path $proj "bin\x64\$Configuration") -Directory |
    ForEach-Object { $_.FullName; Join-Path $_.FullName 'win-x64' } |
    Where-Object { Test-Path (Join-Path $_ "$name.exe") } |
    Select-Object -First 1
if (-not $out) { throw "Couldn't find the build output under bin\x64\$Configuration." }

$assets = Join-Path $out 'Assets'
New-Item -ItemType Directory -Force $assets | Out-Null
Copy-Item (Join-Path $proj 'Assets\*.png') $assets -Force
New-Item -ItemType Directory -Force (Join-Path $out 'Public') | Out-Null

$manifest = Get-Content (Join-Path $proj 'Package.appxmanifest') -Raw
$manifest = $manifest.Replace('$targetnametoken$', $name)
$manifest = $manifest.Replace('$targetentrypoint$', 'Windows.FullTrustApplication')
$manifest = $manifest.Replace('<Resource Language="x-generate"/>', '<Resource Language="en-US"/>')
if ($manifest -notmatch 'ProcessorArchitecture=') {
    $manifest = $manifest -replace '(<Identity\s+Name="[^"]+")', '$1 ProcessorArchitecture="x64"'
}
$manifestPath = Join-Path $out 'AppxManifest.xml'
[IO.File]::WriteAllText($manifestPath, $manifest, (New-Object Text.UTF8Encoding($false)))

Write-Host "Layout: $out"
if ($NoRegister) { return }

# Windows refuses to re-register the same version when files (e.g. the icons) changed: remove the old
# development registration first (the files stay where they are)
$existing = Get-AppxPackage -Name $name -ErrorAction SilentlyContinue
if ($existing) { Remove-AppxPackage $existing.PackageFullName }
Add-AppxPackage -Register $manifestPath -ForceApplicationShutdown
Get-AppxPackage -Name $name | Select-Object Name, Version, InstallLocation | Format-List
Write-Host 'Registered. Now run "Reload" (Reload Command Palette extensions) in Command Palette.' -ForegroundColor Green
