@echo off
setlocal
cd /d "%~dp0"

echo IRSpeedyVPN Windows Builder
echo.

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Build-IRSpeedyVPN.ps1" %*
set "EXITCODE=%ERRORLEVEL%"

echo.
if "%EXITCODE%"=="0" (
    echo Build finished successfully.
) else (
    echo Build failed with exit code %EXITCODE%.
)

if not "%IRSpeedy_NO_PAUSE%"=="1" pause
exit /b %EXITCODE%
