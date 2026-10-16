@echo off
pwsh.exe -NoLogo -NoProfile -File "%~dp0Build.ps1"
if errorlevel 1 pause
