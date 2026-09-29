# The extension's icons are drawn by the logo script (Assets\make-logo.ps1), so they match the app
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path (Split-Path $PSScriptRoot -Parent) 'Assets\make-logo.ps1')
