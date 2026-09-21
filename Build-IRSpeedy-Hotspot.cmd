@echo off
setlocal EnableExtensions DisableDelayedExpansion
pushd "%~dp0"
if errorlevel 1 exit /b 1
if not exist "IRSpeedyVPN\IRSpeedyVPN.csproj" goto failed
where dotnet >nul 2>&1
if errorlevel 1 goto failed
dotnet run --project tests\HotspotChecks\HotspotChecks.csproj -c Release
if errorlevel 1 goto failed
dotnet run --project tests\HotspotIntegrationChecks\HotspotIntegrationChecks.csproj -c Release
if errorlevel 1 goto failed
dotnet build IRSpeedyVPN\IRSpeedyVPN.csproj -c Release -p:IncludeDirectHotspot=true
if errorlevel 1 goto failed
echo Build succeeded: IRSpeedyVPN\bin\Release\net48
echo Deploy the complete output, including Hotspot\win-x64 and Hotspot\win-x86.
echo Do not merge the helper with SmartAssembly. Keep its entire folder next to the final EXE.
pause
popd
exit /b 0
:failed
echo Build failed. Install .NET 8 SDK and .NET Framework 4.8 developer pack, and check errors above.
pause
popd
exit /b 1
