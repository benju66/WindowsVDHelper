# Builds WindowsVirtualDesktopHelper.exe into .\dist using only the .NET SDK (no Visual Studio needed).
# Usage: powershell -ExecutionPolicy Bypass -File Scripts\build-local.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$build = Join-Path $PSScriptRoot 'build-local'
$dist = Join-Path $root 'dist'

# 1. Convert the forms' .resx files to .resources (the SDK can't embed the non-string resources of .NET Framework .resx files)
dotnet build (Join-Path $build 'resgen\resgen.csproj') -c Release --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw 'building resgen failed' }
$resgen = Join-Path $build 'resgen\bin\Release\net472\resgen.exe'
$resDir = Join-Path $build 'obj\res'
New-Item -ItemType Directory -Force $resDir | Out-Null
Get-ChildItem (Join-Path $root 'Source\Forms\*.resx') | ForEach-Object {
	& $resgen $_.FullName (Join-Path $resDir ($_.BaseName + '.resources')) | Out-Null
	if ($LASTEXITCODE -ne 0) { throw "converting $($_.Name) failed" }
}

# 2. Build the app
dotnet build (Join-Path $build 'app.csproj') -c Release --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw 'build failed' }

# 3. Copy to dist (close the running app first, the exe is locked while it runs)
New-Item -ItemType Directory -Force $dist | Out-Null
Copy-Item (Join-Path $build 'bin\Release\net472\WindowsVirtualDesktopHelper.exe') $dist -Force
Copy-Item (Join-Path $root 'Source\app.config') (Join-Path $dist 'WindowsVirtualDesktopHelper.exe.config') -Force
Write-Host "Built $dist\WindowsVirtualDesktopHelper.exe"
