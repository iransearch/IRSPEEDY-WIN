# Run from an elevated Windows PowerShell console. No execution-policy changes.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$HelperPath,
    [Parameter(Mandatory=$true)][Guid]$TunId,
    [Parameter(Mandatory=$true)][int]$CorePid,
    [ValidateRange(10, 600)][int]$Seconds = 120,
    [switch]$Experimental
)
$ErrorActionPreference = 'Stop'
if (-not $Experimental) { throw 'Experimental opt-in required. This PoC does not guarantee leak protection.' }
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Open PowerShell as Administrator first.'
}
$helperFullPath = (Resolve-Path -LiteralPath $HelperPath).Path
$start = [Diagnostics.ProcessStartInfo]::new($helperFullPath)
$start.UseShellExecute = $false
$start.RedirectStandardInput = $true
$start.RedirectStandardOutput = $true
$start.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
$start.CreateNoWindow = $true
$process = [Diagnostics.Process]::new()
$process.StartInfo = $start
$started = $false
$inputWriter = $null

function Read-Reply {
    $read = $process.StandardOutput.ReadLineAsync()
    if (-not $read.Wait(60000)) { throw 'Helper timed out; retain recovery journal and check Windows Mobile Hotspot manually.' }
    $line = $read.Result
    if ($null -eq $line) { throw 'Helper exited. Check Mobile Hotspot state and run recover if needed.' }
    $reply = $line | ConvertFrom-Json
    if ($null -ne $reply.backendState) {
        Write-Host ('Wi-Fi backend: ' + ($reply.backendState | ConvertTo-Json -Compress))
    }
    if ($null -ne $reply.observations) {
        Write-Host ('ICS observations: ' + ($reply.observations | ConvertTo-Json -Depth 6 -Compress))
    }
    if (-not $reply.ok) {
        try {
            Get-Service -Name SharedAccess,Netman,EventSystem -ErrorAction Stop |
                Select-Object Name,Status | Format-Table -AutoSize | Out-Host
        } catch { Write-Host 'Service status could not be read.' }
        throw ("Helper: {0}; Stage: {1}; Type: {2}; HRESULT: {3}; Calls: {4}; PrimaryType: {5}; PrimaryHRESULT: {6}; CleanupHRESULT: {7}" -f
            $reply.code, $reply.stage, $reply.exceptionType, $reply.hresult, ($reply.callSites -join ' -> '),
            $reply.primaryType, $reply.primaryHresult, $reply.cleanupHresult)
    }
    return $reply
}
function Send-Request($request) {
    $inputWriter.WriteLine(($request | ConvertTo-Json -Compress))
    $inputWriter.Flush()
    return Read-Reply
}

try {
    Write-Host 'Starting Wi-Fi Direct Legacy AP, then binding ICS to irspeedy-tun. Credentials appear only after verification.'
    $started = $process.Start()
    $inputWriter = [IO.StreamWriter]::new($process.StandardInput.BaseStream, [Text.UTF8Encoding]::new($false))
    $ready = Read-Reply
    if ($ready.recoveryRequired) { throw 'Run recover before starting a new test.' }
    $request = @{ command='start'; tunId=$TunId.ToString(); corePid=$CorePid;
        experimental=$true }
    $active = Send-Request $request
    Write-Host ('Startup mode: ' + $active.startupMode)
    Write-Host ('Test Wi-Fi: ' + $active.ssid)
    Write-Host ('Temporary Wi-Fi password: ' + $active.password)
    $active.password = ''
    Write-Host 'Connect a test device with proxy disabled. Press Q to stop. Do not use sensitive traffic.'
    $clock = [Diagnostics.Stopwatch]::StartNew()
    while ($clock.Elapsed.TotalSeconds -lt $Seconds -and -not $process.HasExited) {
        if ([Console]::KeyAvailable -and [Console]::ReadKey($true).Key -eq 'Q') { break }
        [void](Send-Request @{ command='heartbeat' })
        Send-Request @{ command='status' } | Out-Host
        Start-Sleep -Seconds 2
    }
    Send-Request @{ command='stop' } | Out-Host
}
finally {
    if ($started -and -not $process.HasExited) {
        # EOF requests orderly cleanup. Never force-kill an elevated network helper
        # here: a Windows API call could still be completing in the background.
        if ($null -ne $inputWriter) { $inputWriter.Close() }
        else { $process.StandardInput.Close() }
        if (-not $process.WaitForExit(30000)) {
            Write-Warning 'Cleanup not confirmed. Turn Mobile Hotspot off manually and retain the recovery journal.'
        }
    }
    $process.Dispose()
    $identity.Dispose()
}
