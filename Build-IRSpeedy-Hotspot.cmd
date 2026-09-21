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
dotnet run --project tests\HotspotPayloadChecks\HotspotPayloadChecks.csproj -c Release
if errorlevel 1 goto failed
dotnet build IRSpeedyVPN\IRSpeedyVPN.csproj -c Release -p:IncludeDirectHotspot=true -p:UseCosturaSingleFile=true
if errorlevel 1 goto failed
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Test-HotspotPayload.ps1 -AssemblyPath IRSpeedyVPN\bin\Release\net48\IRSpeedyVPN.exe
if errorlevel 1 goto failed
echo Build succeeded: IRSpeedyVPN\bin\Release\net48
echo Single-file EXE: managed dependencies and both hotspot payloads are embedded.
echo Use your existing final packaging workflow. No external Hotspot folder is required.
echo If using SmartAssembly, run tools\Test-HotspotPayload.ps1 against its final EXE too.
pause
popd
exit /b 0
:failed
echo Build failed. Install .NET 8 SDK and .NET Framework 4.8 developer pack, and check errors above.
pause
popd
exit /b 1
