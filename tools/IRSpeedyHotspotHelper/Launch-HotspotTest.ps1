[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$HelperPath)
$ErrorActionPreference = 'Stop'
$resultCode = 0
try {
    $helper = (Resolve-Path -LiteralPath $HelperPath).Path
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    try {
        $admin = ([Security.Principal.WindowsPrincipal]::new($identity)).IsInRole(
            [Security.Principal.WindowsBuiltInRole]::Administrator)
    }
    finally { $identity.Dispose() }
    if (-not $admin) {
        # Pass paths as quoted native arguments; no command text or passwords.
        $arguments = '-NoProfile -File "' + $PSCommandPath + '" -HelperPath "' + $helper + '"'
        $elevated = Start-Process -FilePath "$PSHOME\powershell.exe" -Verb RunAs `
            -ArgumentList $arguments -PassThru -Wait
        exit $elevated.ExitCode
    }

    Write-Host 'Experimental IRSPEEDY hotspot test - not a verified kill switch.'
    Write-Host 'Connect IRSPEEDY in full TUN mode before continuing.'
    $adapters = @(Get-NetAdapter -IncludeHidden | Where-Object {
        $_.Name -eq 'irspeedy-tun' -and $_.Status -eq 'Up'
    })
    if ($adapters.Count -ne 1) { throw 'Exactly one active irspeedy-tun adapter is required. Enable TUN and connect VPN first.' }
    $tunId = [Guid]$adapters[0].InterfaceGuid
    Write-Host ('TUN found: ' + $tunId)

    # Windows does not expose reliable TUN ownership via Get-NetAdapter. Do NOT
    # guess ownership from a name or pick the first Xray/core process automatically.
    $candidates = @(Get-Process | Where-Object { $_.ProcessName -match '^(SGuard(?:7)?(?:32|64)|sing-box|xray|throne|IRSpeedyVPN)$' })
    if ($candidates.Count -gt 0) {
        $candidates | Select-Object Id, ProcessName | Format-Table -AutoSize | Out-Host
    }
    Write-Host 'IRSPEEDY starts SGuard64.exe on current x64 Windows (SGuard32 on x86).'
    Write-Host 'Enter its PID. SGuard can remain running after VPN disconnect because the app stops the tunnel over RPC.'
    Write-Host 'Do not select only the UI or Xray process if another core owns the TUN.'
    $coreProcessId = 0
    if (-not [int]::TryParse((Read-Host 'TUN core PID'), [ref]$coreProcessId) -or $coreProcessId -le 0) {
        throw 'A positive process ID is required.'
    }
    $selected = Get-Process -Id $coreProcessId -ErrorAction Stop
    Write-Host ('Selected: ' + $selected.ProcessName + ' / PID ' + $selected.Id)
    if ((Read-Host 'Confirm this process owns the active TUN [Y/N]') -notmatch '^(?i:y)$') {
        throw 'Test cancelled before any sharing changes.'
    }
    & (Join-Path $PSScriptRoot 'Test-Hotspot.ps1') -HelperPath $helper -TunId $tunId `
        -CorePid $coreProcessId -Experimental -Seconds 120
    Write-Host 'Test driver finished. Verify Mobile Hotspot is off.'
}
catch {
    $resultCode = 1
    # Do not print the full ErrorRecord/InvocationInfo: a driver invocation can
    # include a serialized request containing the temporary password.
    Write-Host ('Test failed: ' + $_.Exception.Message) -ForegroundColor Red
}
finally {
    if ($admin) { [void](Read-Host 'Press Enter to close') }
}
exit $resultCode
