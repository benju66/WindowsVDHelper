@echo off
rem Removes Windows Virtual Desktop Helper (your settings are kept)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Scripts\install.ps1" -Uninstall
echo.
pause
