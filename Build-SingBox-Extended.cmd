@echo off
setlocal EnableExtensions DisableDelayedExpansion
pushd "%~dp0"
if errorlevel 1 exit /b 1
powershell.exe -NoProfile -File tools\Build-ExtendedCore.ps1 %*
if errorlevel 1 goto failed
:done
echo Extended build finished. Test TUN and proxy modes on Windows 7 SP1 x86/x64 before release.
popd
exit /b 0
:failed
echo Extended build failed. The previous stable branch is unchanged.
popd
exit /b 1
