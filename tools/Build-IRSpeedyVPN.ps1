[CmdletBinding()]
param(
    [string]$ProjectRoot = "",

    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [switch]$Clean,
    [switch]$SkipObfuscation,
    [switch]$NoPackage,

    # Optional path to the private runtime payload:
    #   - a directory containing V-Guard, misc, etc.
    #   - or an existing Files.zip
    [string]$RuntimeSource = ""
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"
try { [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false); $OutputEncoding = [Console]::OutputEncoding } catch {}

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Root = if ($ProjectRoot) { (Resolve-Path -LiteralPath $ProjectRoot).Path } else { Split-Path -Parent $ScriptDir }
$LogDir = Join-Path $Root "build-logs"
$DistDir = Join-Path $Root "dist"
New-Item -ItemType Directory -Force -Path $LogDir | Out-Null
New-Item -ItemType Directory -Force -Path $DistDir | Out-Null

$Stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$LogFile = Join-Path $LogDir "build-$Stamp.log"

function Write-Step([string]$Message) {
    Write-Host ""
    Write-Host "============================================================"
    Write-Host $Message
    Write-Host "============================================================"
    Add-Content -Path $LogFile -Value ("`r`n=== " + $Message + " ===")
}

function Get-Sha256([string]$Path) {
    $stream = [System.IO.File]::OpenRead($Path)
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        return ([System.BitConverter]::ToString($sha256.ComputeHash($stream))).Replace("-", "")
    }
    finally {
        $sha256.Dispose()
        $stream.Dispose()
    }
}

function Find-MSBuild {
    $candidates = New-Object System.Collections.Generic.List[string]

    $vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        try {
            $found = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" 2>$null
            foreach ($item in $found) {
                if ($item) { $candidates.Add($item) }
            }
        } catch {}
    }

    $known = @(
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2019\BuildTools\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2019\Professional\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2019\Enterprise\MSBuild\Current\Bin\MSBuild.exe"
    )
    foreach ($item in $known) {
        if ($item) { $candidates.Add($item) }
    }

    try {
        $cmd = Get-Command msbuild.exe -ErrorAction SilentlyContinue
        if ($cmd) { $candidates.Add($cmd.Source) }
    } catch {}

    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path $candidate)) {
            return (Resolve-Path $candidate).Path
        }
    }

    throw @"
MSBuild was not found.

Install Visual Studio / Build Tools with:
  - .NET desktop build tools
  - .NET Framework 4.8 targeting pack/developer pack
  - a current .NET SDK

Then run build.cmd again.
"@
}

function Invoke-MSBuild {
    param(
        [Parameter(Mandatory=$true)][string]$Project,
        [string[]]$ExtraArgs = @()
    )

    $args = @(
        $Project,
        "/nologo",
        "/m",
        "/v:minimal",
        "/p:Configuration=$Configuration",
        "/p:Platform=AnyCPU"
    ) + $ExtraArgs

    Write-Host "> `"$MSBuild`" $($args -join ' ')"
    Add-Content -Path $LogFile -Value ("> `"$MSBuild`" " + ($args -join " "))

    & $MSBuild @args 2>&1 | Tee-Object -FilePath $LogFile -Append
    $code = $LASTEXITCODE
    if ($code -ne 0) {
        throw "MSBuild failed with exit code $code. See: $LogFile"
    }
}

function Copy-DirectoryContents([string]$Source, [string]$Destination) {
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    Get-ChildItem -LiteralPath $Source -Force | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $Destination -Recurse -Force
    }
}


function Backup-BuilderRepairFile {
    param([Parameter(Mandatory=$true)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return }

    $backupRoot = Join-Path $Root ".builder-backups\$Stamp"
    $relative = $Path.Substring($Root.Length).TrimStart('\')
    $target = Join-Path $backupRoot $relative
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
    Copy-Item -LiteralPath $Path -Destination $target -Force
    Write-Host "Backup before repair: $target"
}

function Repair-KnownSourceSnapshot {
    Write-Step "Checking source compatibility"

    $repairs = New-Object System.Collections.Generic.List[string]

    # This source snapshot references AppServices throughout the application,
    # but the AppServices.cs implementation is missing from the repository.
    $appServicesPath = Join-Path $Root "IRSpeedyVPN\AppServices.cs"
    if (-not (Test-Path -LiteralPath $appServicesPath)) {
        $appServices = @'
using IRSpeedyVPN.Common;
using IRSpeedyVPN.Common.Json;
using IRSpeedyVPN.Interfaces;
using IRSpeedyVPN.Models;
using IRSpeedyVPN.Resource;
using IRSpeedyVPN.Services;
using IRSpeedyVPN.UserControls;
using IRSpeedyVPN.WebServices;
using System;

namespace IRSpeedyVPN
{
    internal static class AppServices
    {
        private static readonly object Sync = new object();
        private static bool initialized;

        internal static GlobalInfo GlobalInfo { get; private set; }
        internal static ResourceManager ResourceManager { get; private set; }
        internal static ServiceFactory ServiceFactory { get; private set; }
        internal static IProxifier Proxifier { get; private set; }
        internal static NewServiceController NewServiceController { get; private set; }
        internal static PersianIsoNames PersianIsoNames { get; private set; }
        internal static JsonConverter JsonConverter { get; private set; }
        internal static UCLogin UCLogin { get; private set; }
        internal static UCServerList UCServerList { get; private set; }
        internal static UCUserInfo UCUserInfo { get; private set; }
        internal static UCUpdate UCUpdate { get; private set; }
        internal static UCChangePassword UCChangePassword { get; private set; }

        internal static void Initialize()
        {
            if (initialized) return;
            lock (Sync)
            {
                if (initialized) return;

                JsonConverter = new JsonConverter();
                PersianIsoNames = new PersianIsoNames();
                GlobalInfo = new GlobalInfo();
                ResourceManager = new ResourceManager();
                NewServiceController = new NewServiceController();
                ServiceFactory = new ServiceFactory();
                Proxifier = new Proxifier();

                UCLogin = new UCLogin();
                UCServerList = new UCServerList();
                UCUserInfo = new UCUserInfo();
                UCUpdate = new UCUpdate();
                UCChangePassword = new UCChangePassword();

                initialized = true;
            }
        }
    }
}
'@
        Set-Content -LiteralPath $appServicesPath -Value $appServices -Encoding UTF8
        $repairs.Add("Created IRSpeedyVPN\AppServices.cs")
    }

    # A newer FastestConnectionService was merged with one old DI-container line.
    $fastestPath = Join-Path $Root "IRSpeedyVPN\Services\Fastest\FastestConnectionService.cs"
    if (Test-Path -LiteralPath $fastestPath) {
        $text = Get-Content -LiteralPath $fastestPath -Raw
        $legacy = 'serviceController = (NewServiceController)Program.container.GetInstance(typeof(NewServiceController));'
        if ($text.Contains($legacy)) {
            Backup-BuilderRepairFile -Path $fastestPath
            $text = $text.Replace($legacy, 'serviceController = AppServices.NewServiceController;')
            Set-Content -LiteralPath $fastestPath -Value $text -Encoding UTF8
            $repairs.Add("Replaced legacy Program.container reference in FastestConnectionService.cs")
        }
    }

    # FastestConnectionService calls KillProcessTree, while the helper was absent.
    $shellPath = Join-Path $Root "IRSpeedyVPN\Common\ShellExecute.cs"
    $killProcessTreeCallers = Get-ChildItem -LiteralPath (Join-Path $Root "IRSpeedyVPN") -Filter "*.cs" -Recurse -File |
        Where-Object { $_.FullName -ne $shellPath } |
        Select-String -Pattern 'ShellExecute\.KillProcessTree\s*\(' -ErrorAction SilentlyContinue
    if ((Test-Path -LiteralPath $shellPath) -and $killProcessTreeCallers) {
        $text = Get-Content -LiteralPath $shellPath -Raw
        if ($text -notmatch 'KillProcessTree\s*\(\s*Process\s+process\s*\)') {
            Backup-BuilderRepairFile -Path $shellPath
            $method = @'

        /// <summary>
        /// Terminates a process and its child process tree. .NET Framework 4.8
        /// does not provide Process.Kill(entireProcessTree), so use taskkill /T.
        /// </summary>
        public static void KillProcessTree(Process process)
        {
            if (process == null)
                return;

            int pid;
            try
            {
                if (process.HasExited)
                    return;
                pid = process.Id;
            }
            catch
            {
                return;
            }

            try
            {
                using (var killer = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "taskkill.exe",
                        Arguments = "/PID " + pid + " /T /F",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    }
                })
                {
                    killer.Start();
                    killer.WaitForExit(5000);
                }
            }
            catch
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill();
                }
                catch
                {
                }
            }
        }
'@
            $pattern = '(?s)\r?\n    \}\r?\n\}\s*$'
            if (-not [regex]::IsMatch($text, $pattern)) {
                throw "Could not repair ShellExecute.cs automatically because its closing structure is unexpected."
            }
            $replacement = $method + "`r`n    }`r`n}`r`n"
            $rx = New-Object System.Text.RegularExpressions.Regex($pattern)
            $text = $rx.Replace($text, $replacement, 1)
            Set-Content -LiteralPath $shellPath -Value $text -Encoding UTF8
            $repairs.Add("Added ShellExecute.KillProcessTree(Process)")
        }
    }

    if ($repairs.Count -eq 0) {
        Write-Host "Source compatibility: OK (no repair required)."
    } else {
        Write-Host "Applied source compatibility repairs:"
        foreach ($repair in $repairs) { Write-Host "  - $repair" }
    }

    # Fail early if the known broken state still exists.
    $appServicesRefs = Get-ChildItem -LiteralPath (Join-Path $Root "IRSpeedyVPN") -Filter "*.cs" -Recurse -File |
        Select-String -SimpleMatch "AppServices." -ErrorAction SilentlyContinue
    if ($appServicesRefs -and -not (Test-Path -LiteralPath $appServicesPath)) {
        throw "Source references AppServices but IRSpeedyVPN\AppServices.cs is still missing."
    }
}

function Prepare-RuntimeArchive {
    param(
        [Parameter(Mandatory=$true)][string]$ThroneExe,
        [Parameter(Mandatory=$true)][string]$UpdateHelperExe
    )

    $runtimeZip = Join-Path $Root "IRSpeedyVPN\Resources\Files.zip"
    $runtimeFolder = Join-Path $Root "IRSpeedyVPN\Resources\Files"
    $adjacentRuntimeZip = Join-Path $Root "Files.zip"

    $source = $null
    if ($RuntimeSource) {
        $source = $RuntimeSource
        if (-not (Test-Path $source)) {
            throw "RuntimeSource does not exist: $source"
        }
    } elseif (Test-Path -LiteralPath $adjacentRuntimeZip) {
        # Preferred local workflow: put the private payload next to build.cmd.
        $source = $adjacentRuntimeZip
    } elseif (Test-Path $runtimeFolder) {
        $source = $runtimeFolder
    } elseif (Test-Path $runtimeZip) {
        $source = $runtimeZip
    } else {
        throw @"
The private runtime payload is missing.

This source archive does not contain:
  Files.zip next to build.cmd
or:
  IRSpeedyVPN\Resources\Files.zip
or:
  IRSpeedyVPN\Resources\Files\

The application embeds this payload and requires V-Guard core executables.
Restore your original runtime payload, then either:
  1) put it next to build.cmd as Files.zip
  2) put it at IRSpeedyVPN\Resources\Files.zip
  3) put the extracted files at IRSpeedyVPN\Resources\Files\
  4) run: build.cmd -RuntimeSource "D:\path\to\Files.zip"

The builder will then inject the freshly built Throne.exe and udh.exe automatically.
"@
    }

    Write-Step "Preparing embedded runtime payload"

    $sourceItem = Get-Item -LiteralPath $source
    $sourceHash = if ($sourceItem.PSIsContainer) { "directory" } else { Get-Sha256 $sourceItem.FullName }
    Write-Host "Runtime source: $($sourceItem.FullName)"
    Write-Host "Source SHA256: $sourceHash"

    $temp = Join-Path $env:TEMP ("IRSpeedy-runtime-" + [Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Force -Path $temp | Out-Null

    try {
        if ($sourceItem.PSIsContainer) {
            Copy-DirectoryContents $source $temp
        } else {
            $ext = [IO.Path]::GetExtension($source)
            if ($ext -ne ".zip") {
                throw "RuntimeSource must be a directory or a .zip file: $source"
            }
            Add-Type -AssemblyName System.IO.Compression
            Add-Type -AssemblyName System.IO.Compression.FileSystem
            [System.IO.Compression.ZipFile]::ExtractToDirectory($source, $temp)
        }

        $vguard = Join-Path $temp "V-Guard"
        $misc = Join-Path $temp "misc"
        New-Item -ItemType Directory -Force -Path $vguard | Out-Null
        New-Item -ItemType Directory -Force -Path $misc | Out-Null

        Copy-Item -LiteralPath $ThroneExe -Destination (Join-Path $vguard "Throne.exe") -Force
        Copy-Item -LiteralPath $UpdateHelperExe -Destination (Join-Path $misc "udh.exe") -Force

        # The current application resolves one of these core binaries at runtime.
        $coreFiles = @(
            "SGuard64.exe",
            "SGuard32.exe",
            "SGuard764.exe",
            "SGuard732.exe"
        )
        $present = @()
        foreach ($name in $coreFiles) {
            if (Test-Path (Join-Path $vguard $name)) { $present += $name }
        }

        if ($present.Count -eq 0) {
            throw @"
The runtime payload has no supported V-Guard core executable.
Expected at least one of:
  V-Guard\SGuard64.exe
  V-Guard\SGuard32.exe
  V-Guard\SGuard764.exe
  V-Guard\SGuard732.exe
"@
        }

        if (-not (Test-Path (Join-Path $vguard "SGuard64.exe"))) {
            Write-Warning "V-Guard\SGuard64.exe is missing; normal 64-bit Windows systems will not be able to start the core."
        }
        if (-not (Test-Path (Join-Path $vguard "SGuard32.exe"))) {
            Write-Warning "V-Guard\SGuard32.exe is missing; normal 32-bit Windows systems will not be able to start the core."
        }

        $zipTemp = Join-Path $env:TEMP ("IRSpeedy-Files-" + [Guid]::NewGuid().ToString("N") + ".zip")
        if (Test-Path $zipTemp) { Remove-Item -LiteralPath $zipTemp -Force }

        # Compress-Archive writes directory entries with Windows backslashes.
        # DotNetZip then treats entries such as "misc\runtimes\" as files and
        # aborts extraction when the real directory already exists. Add only
        # files and always use ZIP-standard forward slashes.
        Add-Type -AssemblyName System.IO.Compression
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $zipArchive = [System.IO.Compression.ZipFile]::Open(
            $zipTemp,
            [System.IO.Compression.ZipArchiveMode]::Create)
        try {
            Get-ChildItem -LiteralPath $temp -Recurse -File | ForEach-Object {
                $relativePath = $_.FullName.Substring($temp.Length).TrimStart('\', '/')
                $entryName = $relativePath.Replace('\', '/')
                [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                    $zipArchive,
                    $_.FullName,
                    $entryName,
                    [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
            }
        }
        finally {
            $zipArchive.Dispose()
        }

        $resourceDir = Split-Path -Parent $runtimeZip
        New-Item -ItemType Directory -Force -Path $resourceDir | Out-Null

        if ((Test-Path $runtimeZip) -and
            [string]::Equals($sourceItem.FullName, (Get-Item -LiteralPath $runtimeZip).FullName, [StringComparison]::OrdinalIgnoreCase)) {
            $backup = Join-Path $Root ".builder-backups\$Stamp\Files.zip"
            New-Item -ItemType Directory -Force -Path (Split-Path -Parent $backup) | Out-Null
            Copy-Item -LiteralPath $runtimeZip -Destination $backup -Force
            Write-Host "Backup: $backup"
        }

        Move-Item -LiteralPath $zipTemp -Destination $runtimeZip -Force

        $hash = Get-Sha256 $runtimeZip
        Write-Host "Runtime payload: $runtimeZip"
        Write-Host "SHA256: $hash"
        Write-Host "Injected: V-Guard\Throne.exe"
        Write-Host "Injected: misc\udh.exe"
    }
    finally {
        if (Test-Path $temp) {
            Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    return [pscustomobject]@{
        Source = $sourceItem.FullName
        SourceSHA256 = $sourceHash
        EmbeddedZipSHA256 = $hash
    }
}

function Get-AppVersion {
    $assemblyInfo = Join-Path $Root "IRSpeedyVPN\Properties\AssemblyInfo.cs"
    if (-not (Test-Path $assemblyInfo)) { return "unknown" }

    $text = Get-Content -LiteralPath $assemblyInfo -Raw
    $m = [regex]::Match($text, 'AssemblyFileVersion\("([^"]+)"\)')
    if ($m.Success) { return $m.Groups[1].Value }

    $m = [regex]::Match($text, 'AssemblyVersion\("([^"]+)"\)')
    if ($m.Success) { return $m.Groups[1].Value }

    return "unknown"
}

$ThroneProject = Join-Path $Root "Throne\Throne.csproj"
$UpdateProject = Join-Path $Root "UpdateHelper\UpdateHelper.csproj"
$AppProject = Join-Path $Root "IRSpeedyVPN\IRSpeedyVPN.csproj"

foreach ($required in @($ThroneProject, $UpdateProject, $AppProject)) {
    if (-not (Test-Path $required)) {
        throw "Required project file not found: $required"
    }
}

Write-Step "Preflight"
$MSBuild = Find-MSBuild
Write-Host "MSBuild: $MSBuild"
Write-Host "Configuration: $Configuration"
Write-Host "Log: $LogFile"

$net48Ref = Join-Path ${env:ProgramFiles(x86)} "Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8"
if (-not (Test-Path $net48Ref)) {
    Write-Warning ".NET Framework 4.8 reference assemblies were not found at the standard location. Build may fail until the 4.8 targeting/developer pack is installed."
}

Repair-KnownSourceSnapshot

if ($Clean) {
    Write-Step "Cleaning projects"
    Invoke-MSBuild $ThroneProject @("/t:Clean")
    Invoke-MSBuild $UpdateProject @("/t:Clean", "/p:SignManifests=false")

    # Clean can still be useful even if the private Files.zip is currently absent.
    try {
        Invoke-MSBuild $AppProject @("/t:Clean")
    } catch {
        Write-Warning "Application clean reported an error; continuing to the actual build. $($_.Exception.Message)"
    }
}

Write-Step "Restoring NuGet packages"
Invoke-MSBuild $AppProject @("/t:Restore")

Write-Step "Building Throne relay"
Invoke-MSBuild $ThroneProject @("/t:Build")

$throneExe = Join-Path $Root "Throne\bin\$Configuration\Throne.exe"
if (-not (Test-Path $throneExe)) {
    throw "Throne build completed but output was not found: $throneExe"
}

Write-Step "Building UpdateHelper"
# The old project contains a temporary ClickOnce key. Manifest signing is not needed
# for the local helper executable and can cause unnecessary certificate errors.
Invoke-MSBuild $UpdateProject @("/t:Build", "/p:SignManifests=false")

$updateExe = Join-Path $Root "UpdateHelper\bin\$Configuration\udh.exe"
if (-not (Test-Path $updateExe)) {
    throw "UpdateHelper build completed but output was not found: $updateExe"
}

$runtimeInfo = Prepare-RuntimeArchive -ThroneExe $throneExe -UpdateHelperExe $updateExe

Write-Step "Building IRSpeedyVPN"
$appArgs = @("/t:Build")
if ($SkipObfuscation) {
    # The csproj runs Dotfuscator only if the configured executable exists.
    $disabledDotfuscator = Join-Path $Root "__dotfuscator_disabled__.exe"
    $appArgs += "/p:DotfuscatorExe=$disabledDotfuscator"
}
Invoke-MSBuild $AppProject $appArgs

$appOut = Join-Path $Root "IRSpeedyVPN\bin\$Configuration\net48"
$appExe = Join-Path $appOut "IRSpeedyVPN.exe"
if (-not (Test-Path $appExe)) {
    throw "Application build completed but output was not found: $appExe"
}

if (-not $NoPackage) {
    Write-Step "Creating single-file release"

    $version = Get-AppVersion
    $packageName = "IRSpeedyVPN-$version-$Configuration"
    $releaseDir = Join-Path $DistDir $packageName
    $releaseExe = Join-Path $releaseDir "IRSpeedyVPN.exe"
    $legacyReleaseZip = Join-Path $DistDir ($packageName + ".zip")
    $buildInfoFile = Join-Path $LogDir "build-info-$Stamp.txt"

    if (Test-Path $releaseDir) { Remove-Item -LiteralPath $releaseDir -Recurse -Force }
    if (Test-Path $legacyReleaseZip) { Remove-Item -LiteralPath $legacyReleaseZip -Force }
    New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null

    $obfuscatedExe = Join-Path $appOut "Obfuscated\IRSpeedyVPN.exe"
    $usedObfuscated = $false
    $sourceExe = $appExe
    if (-not $SkipObfuscation -and (Test-Path $obfuscatedExe)) {
        $sourceExe = $obfuscatedExe
        $usedObfuscated = $true
    }
    Copy-Item -LiteralPath $sourceExe -Destination $releaseExe -Force

    # Every managed DLL copied by MSBuild must also exist inside the executable.
    # Program.ResolveEmbeddedAssembly loads these resources before WPF starts.
    $releaseAssembly = [Reflection.Assembly]::ReflectionOnlyLoadFrom($releaseExe)
    $manifestNames = @($releaseAssembly.GetManifestResourceNames())
    $outputDlls = @(Get-ChildItem -LiteralPath $appOut -Filter "*.dll" -File)
    $missingEmbedded = @()
    foreach ($dll in $outputDlls) {
        $expected = "EmbeddedAssemblies.$($dll.Name)"
        if ($manifestNames -notcontains $expected) {
            $missingEmbedded += $dll.Name
        }
    }
    if ($missingEmbedded.Count -gt 0) {
        throw "Single-file validation failed. Missing embedded assemblies: $($missingEmbedded -join ', ')"
    }

    $runtimeResourceFound = $false
    $resourceStream = $releaseAssembly.GetManifestResourceStream("IRSpeedyVPN.g.resources")
    if ($resourceStream) {
        $resourceReader = New-Object System.Resources.ResourceReader($resourceStream)
        try {
            $resourceEnumerator = $resourceReader.GetEnumerator()
            while ($resourceEnumerator.MoveNext()) {
                if ([string]$resourceEnumerator.Key -eq "resources/files.zip") {
                    $runtimeResourceFound = $true
                    break
                }
            }
        } finally {
            $resourceReader.Dispose()
            $resourceStream.Dispose()
        }
    }
    if (-not $runtimeResourceFound) {
        throw "Single-file validation failed. Embedded Resources/Files.zip was not found."
    }

    $exeHash = Get-Sha256 $releaseExe
    $runtimeHash = Get-Sha256 (Join-Path $Root "IRSpeedyVPN\Resources\Files.zip")

    @"
IRSpeedyVPN local build
Version: $version
Configuration: $Configuration
Built: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss zzz")
Single executable: True
Embedded managed assemblies: $($outputDlls.Count)
Embedded runtime Files.zip: True
Runtime source: $($runtimeInfo.Source)
Runtime source SHA256: $($runtimeInfo.SourceSHA256)
Obfuscated executable used: $usedObfuscated
IRSpeedyVPN.exe SHA256: $exeHash
Embedded runtime Files.zip SHA256: $runtimeHash

Note: This builder does not code-sign the final executable.
"@ | Set-Content -LiteralPath $buildInfoFile -Encoding UTF8

    Write-Host ""
    Write-Host "SUCCESS"
    Write-Host "Executable: $releaseExe"
    Write-Host "Build info: $buildInfoFile"
    Write-Host "Build log:  $LogFile"
} else {
    Write-Host ""
    Write-Host "SUCCESS"
    Write-Host "Executable: $appExe"
    Write-Host "Build log:  $LogFile"
}
