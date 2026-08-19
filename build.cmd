@echo off
setlocal

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\Build-IRSpeedyVPN.ps1" -ProjectRoot "%~dp0." %*
exit /b %ERRORLEVEL%
