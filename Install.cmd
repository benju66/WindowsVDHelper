@echo off
rem Installs or updates Windows Virtual Desktop Helper for the current user (no admin rights needed)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Scripts\install.ps1"
echo.
pause
