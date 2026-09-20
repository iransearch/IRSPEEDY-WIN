@echo off
setlocal EnableExtensions DisableDelayedExpansion
title IRSPEEDY - Experimental Hotspot Build
pushd "%~dp0"
if errorlevel 1 exit /b 1
set "HOTSPOT_ROOT=%CD%"
set "HOTSPOT_PROJECT=%HOTSPOT_ROOT%\tools\IRSpeedyHotspotHelper\IRSpeedyHotspotHelper.csproj"
if not exist "%HOTSPOT_PROJECT%" goto missing_source
where dotnet >nul 2>&1
if errorlevel 1 goto missing_sdk
dotnet --list-sdks | findstr /B /C:"8." >nul
if errorlevel 1 goto missing_sdk

set "HOTSPOT_RUN=%HOTSPOT_ROOT%\artifacts\hotspot-poc\build-%RANDOM%-%RANDOM%"
if exist "%HOTSPOT_RUN%" goto collision
mkdir "%HOTSPOT_RUN%"
if errorlevel 1 goto folder_error
set "HOTSPOT_OUTPUT=%HOTSPOT_RUN%\publish"
set "HOTSPOT_LOG=%HOTSPOT_RUN%\build.log"
echo IRSPEEDY experimental hotspot build >"%HOTSPOT_LOG%"
echo Started: %DATE% %TIME% >>"%HOTSPOT_LOG%"
dotnet --info >>"%HOTSPOT_LOG%" 2>&1

echo [1/3] Running safety checks. Please wait...
dotnet run --project "%HOTSPOT_ROOT%\tests\HotspotChecks\HotspotChecks.csproj" -c Release >>"%HOTSPOT_LOG%" 2>&1
if errorlevel 1 goto build_failed
echo [2/3] Building Windows helper. Please wait...
dotnet build "%HOTSPOT_PROJECT%" -c Release >>"%HOTSPOT_LOG%" 2>&1
if errorlevel 1 goto build_failed
echo [3/3] Publishing self-contained Windows x64 output. Please wait...
dotnet publish "%HOTSPOT_PROJECT%" -c Release -r win-x64 --self-contained true -o "%HOTSPOT_OUTPUT%" >>"%HOTSPOT_LOG%" 2>&1
if errorlevel 1 goto build_failed
if not exist "%HOTSPOT_OUTPUT%\IRSpeedyHotspotHelper.exe" goto build_failed

echo.
echo BUILD SUCCEEDED
echo Output: "%HOTSPOT_OUTPUT%"
echo Log:    "%HOTSPOT_LOG%"
echo Copy the WHOLE publish folder to another Windows x64 test PC.
echo This is experimental. Windows hardware and leak protection are not verified.
echo First connect IRSPEEDY in full TUN mode. Turn other hotspot/ICS sessions off.
echo.
choice /C YN /N /M "Run a 120-second phone test now? [Y/N]: "
if errorlevel 2 goto success
if errorlevel 1 goto launch_test
goto success

:launch_test
rem No execution-policy bypass. UAC elevation is requested by the test launcher.
powershell.exe -NoProfile -File "%HOTSPOT_ROOT%\tools\IRSpeedyHotspotHelper\Launch-HotspotTest.ps1" -HelperPath "%HOTSPOT_OUTPUT%\IRSpeedyHotspotHelper.exe"
if errorlevel 1 goto test_failed
goto success

:missing_source
echo ERROR: Download the complete agent/windows-hotspot-poc branch first.
echo Place this CMD at the repository root, beside IRSpeedyVPN.sln.
goto failed
:missing_sdk
echo ERROR: Install the .NET 8 SDK x64, then reopen this CMD.
echo The runtime alone is not sufficient. NuGet access is needed for first build.
goto failed
:collision
echo ERROR: Build folder already exists. Run this CMD again.
goto failed
:folder_error
echo ERROR: Cannot create output folder. Move the source to a writable folder.
goto failed
:build_failed
echo.
echo BUILD FAILED. The test will not run.
type "%HOTSPOT_LOG%"
echo.
echo Send this log for diagnosis: "%HOTSPOT_LOG%"
goto failed
:test_failed
echo Test launch or execution failed. Build output is still available.
echo Read the test window message. If scripts are blocked, use your approved signing policy.
goto failed
:success
echo.
pause
popd
exit /b 0
:failed
echo.
pause
popd
exit /b 1
