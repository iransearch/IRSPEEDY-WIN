# Windows-only: real compiled BAML, production picker, timeline and overlay.
# powershell.exe -NoProfile -STA -File tests/ServerListTransitionChecks/run-wpf.ps1 -AppPath <IRSpeedyVPN.exe>
# Use an unobfuscated build. No VPN/probe constructors, network calls or registry writes.
param([Parameter(Mandatory=$true)][string]$AppPath)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase
$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path $AppPath).Path)
[System.Windows.Application]::ResourceAssembly = $assembly
$app = [Activator]::CreateInstance($assembly.GetType('IRSpeedyVPN.App', $true))
$app.ShutdownMode = [System.Windows.ShutdownMode]::OnExplicitShutdown
$app.InitializeComponent()
$assembly.GetType('IRSpeedyVPN.Common.ThemeManager', $true).GetProperty('Instance').GetValue($null, $null).Initialize()
$flags = [Reflection.BindingFlags]'Instance,NonPublic,Public'
$prefix = 'IRSpeedyVPN.Components.ServerListControl.'
$groupType = $assembly.GetType($prefix + 'GroupItem', $true)
$itemType = $assembly.GetType($prefix + 'ServerItem', $true)
$urlType = $assembly.GetType('IRSpeedyVPN.Models.NewService.Url', $true)
$serverType = $assembly.GetType('IRSpeedyVPN.Models.NewService.ServerEx', $true)
$serviceType = $assembly.GetType('IRSpeedyVPN.Services.TunnelPlusService', $true)
function Check($ok, [string]$name) { if (!$ok) { throw $name }; Write-Output "PASS $name" }
function Field($instance, [string]$name) { return $instance.GetType().GetField($name,$flags).GetValue($instance) }
function Call($instance, [string]$name, [object[]]$arguments) { return $instance.GetType().GetMethod($name,$flags).Invoke($instance,$arguments) }
function Pump([int]$milliseconds = 20) {
    $frame = New-Object System.Windows.Threading.DispatcherFrame
    $timer = New-Object System.Windows.Threading.DispatcherTimer
    $timer.Interval = [TimeSpan]::FromMilliseconds($milliseconds)
    $timer.Add_Tick({ $frame.Continue = $false }.GetNewClosure())
    $timer.Start()
    try { [System.Windows.Threading.Dispatcher]::PushFrame($frame) } finally { $timer.Stop() }
}
function Wait-For([scriptblock]$condition, [int]$timeout = 6000) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    while (!( & $condition )) { if ($watch.ElapsedMilliseconds -gt $timeout) { throw 'UI condition timed out' }; Pump }
}
function Order($slots) { return (@($slots | ForEach-Object { $_.DisplayedServer.Service.ID }) -join ',') }
function Unique($slots) {
    foreach ($part in @('SignalServer','BadgeServer','NameServer','FlagServer','SelectionServer')) {
        $owners = @{}
        foreach ($slot in $slots) {
            $value = $slot.$part
            if ($null -eq $value -or $slot.IsVacant) { throw 'Blank row during paired exchange' }
            if ($owners.ContainsKey($value.Identity)) { throw 'Duplicate identity within a displayed group' }
            $owners[$value.Identity] = $true
        }
    }
}
function Check-FlagClocks([bool]$active) {
    $running = 0
    foreach ($shine in @(Field $picker '_probeShines')) {
        $shift = $shine.RenderTransform.Children[1]
        if ($shift.HasAnimatedProperties) {
            if (!$shine.IsVisible -or !$shine.DataContext.IsChecking) { throw 'Checking sheen on an idle flag' }
            $running++
        }
    }
    Check (($active -and $running -eq 1) -or (!$active -and $running -eq 0)) 'checking flag owns its clock; idle/hidden flags have no running clock'
}
function Group([int]$id, [string]$name, [string]$code, [long]$ping) {
    $url = [Activator]::CreateInstance($urlType); $url.latency = $ping; $url.latencychkTime = [DateTime]::Now
    $urls = [Activator]::CreateInstance([Collections.Generic.List[object]].GetGenericTypeDefinition().MakeGenericType($urlType)); $urls.Add($url)
    $server = [Activator]::CreateInstance($serverType); $server.ID = $id; $server.urls = $urls; $server.Protocol = 'vless'
    # Only ID and GetServerUrls are read. Never initialize Core or create a connection.
    $service = [Runtime.Serialization.FormatterServices]::GetUninitializedObject($serviceType)
    $serviceType.GetField('server',$flags).SetValue($service,$server)
    $group = [Activator]::CreateInstance($groupType,$true)
    $group.Service = $service; $group.CountryName = $name; $group.CountryCode = $code
    $group.ResolveFlag(); $group.RefreshSignals()
    return $group
}
$picker = [Activator]::CreateInstance($assembly.GetType($prefix + 'ServerCountryPicker',$true))
$picker.Height = 430
$window = New-Object System.Windows.Window
$window.Width = 420; $window.Height = 500; $window.ShowInTaskbar = $false; $window.ShowActivated = $false
$window.Content = $picker
try {
    $window.Show(); Pump 100
    $a = Group 1 'A' 'FR' 300; $b = Group 2 'B' 'DE' 500; $c = Group 3 'C' 'NL' 700
    $a.IsChecking = $true; $a.IsSelectedCountry = $true
    $groups = [Activator]::CreateInstance([Collections.Generic.List[object]].GetGenericTypeDefinition().MakeGenericType($groupType))
    $groups.Add($a); $groups.Add($b); $groups.Add($c)
    $picker.GetType().GetField('_allGroups',$flags).SetValue($picker,$groups)
    $picker.GetType().GetField('_groups',$flags).SetValue($picker,$groups)
    $items = [Array]::CreateInstance($itemType,3)
    $ctor = $itemType.GetConstructor($flags,$null,[Type[]]@($groupType),$null)
    for ($i=0; $i -lt 3; $i++) { $items[$i] = $ctor.Invoke([object[]]@($groups[$i])) }
    $model = Field $picker '_serverList'; $animator = Field $picker '_sweepAnimator'; $overlay = Field $picker 'CometOverlay'
    Call $model 'Publish' ([object[]]@($items,$true,$false,$null)) | Out-Null
    Pump 100
    Check ((Order $model.Slots) -eq '1,2,3' -and !(Field $overlay 'ShowBand')) 'initial population has no light'
    Check-FlagClocks ([System.Windows.SystemParameters]::ClientAreaAnimation)
    $a.IsChecking = $false; $c.IsChecking = $true
    $picker.RefreshGroup($a.Service)
    $b.Service.GetServerUrls()[0].latency = 80
    $picker.RefreshGroup($b.Service)
    Check ($model.Servers[0].Service.ID -eq 2 -and $c.IsChecking) 'completed country sorts while another remains checking'
    if (![System.Windows.SystemParameters]::ClientAreaAnimation) {
        Check ((Order $model.Slots) -eq '2,1,3' -and !$model.IsTransitioning) 'OS reduced motion applies final order immediately'
    } else {
        Wait-For { Field $overlay 'ShowBand' }
        $pass = Field $animator '_pass'; $started = Field $animator '_started'
        $c.IsChecking = $false
        $c.Service.GetServerUrls()[0].latency = 200
        $picker.RefreshGroup($c.Service)
        Check ([object]::ReferenceEquals($pass,(Field $animator '_pass')) -and $started -eq (Field $animator '_started')) 'new completion coalesces without restarting current pass'
        $surfaces = @(Field $animator '_surfaces'); $bands = @(Field $overlay 'Bands')
        Check ($bands.Count -eq 2 -and $pass.Rows.Length -eq 2) 'both exchanged rows have a simultaneous light band'
        Check (![object]::ReferenceEquals((Field $bands[0] 'Shift'),(Field $bands[1] 'Shift'))) 'paired bands own independent row transforms'
        $lefts = @($bands | ForEach-Object { (Field $_ 'Shift').X })
        Pump 150
        for ($j=0; $j -lt 2; $j++) {
            Check ((Field $bands[$j] 'Shift').X -gt $lefts[$j]) 'each band advances physically left to right'
        }
        Check ($overlay.FlowDirection -eq [Windows.FlowDirection]::LeftToRight) 'RTL layout does not reverse either light'
        Unique $model.Slots
        # A group commits atomically after BOTH physical bands cross its center.
        for ($i=0; $i -lt 5; $i++) {
            $crossedAll = $true; $aheadOfOne = $false
            foreach ($surface in $surfaces) {
                $shift = Field (Field $surface 'Band') 'Shift'
                $edge = $shift.X - (Field $surface 'Left') + .85*170
                $center = (Field $surface 'Centers')[$i]
                if ($edge -le $center+1) { $crossedAll = $false }
                if ($edge -lt $center-1) { $aheadOfOne = $true }
            }
            $part = [Enum]::ToObject($assembly.GetType($prefix+'ServerRowPart',$true),$i)
            foreach ($assignment in $pass.Rows) {
                $shown = Call $assignment.Slot 'GetPart' ([object[]]@($part))
                if ($crossedAll) { Check ([object]::ReferenceEquals($shown,$assignment.Target)) 'both crossed groups show their new snapshots' }
                if ($aheadOfOne) { Check ([object]::ReferenceEquals($shown,$assignment.From)) 'both groups retain old data until their shared crossing' }
            }
        }
        Wait-For { Unique $model.Slots; !$model.IsTransitioning -and (Field $overlay 'Flashes').Count -eq 0 -and !(Field $animator '_subscribed') }
        Check ((Order $model.Slots) -eq '2,3,1' -and $model.Slots[2].SelectionServer.IsSelectedCountry) 'latest order settles with selection on its stable identity'
        Check-FlagClocks $false
        $b.Service.GetServerUrls()[0].latency = 70; $picker.RefreshGroup($b.Service); Pump 100
        Check (!$model.IsTransitioning -and !(Field $overlay 'ShowBand') -and (Field $overlay 'Flashes').Count -eq 0) 'same-position result has no band or flash'
        $a.Service.GetServerUrls()[0].latency = 50; $picker.RefreshGroup($a.Service)
        Wait-For { Field $overlay 'ShowBand' }
        $threePass = Field $animator '_pass'
        Check ($threePass.Rows.Length -eq 3 -and @(Field $overlay 'Bands').Count -eq 3) 'three changed rows receive three simultaneous bands without a pair limit'
        Check ((($threePass.Rows | ForEach-Object { $_.Target.Service.ID }) -join ',') -eq '1,2,3') 'all changed slots target their final identities directly'
        Wait-For { Unique $model.Slots; !$model.IsTransitioning -and (Field $overlay 'Flashes').Count -eq 0 -and !(Field $animator '_subscribed') }
        Check ((Order $model.Slots) -eq '1,2,3') 'three-way reorder needs no follow-up pair passes'

    }
    $picker.BeginCheckCycle(); $picker.EndCheckCycleSetup()
    $c.IsLastSuccessfulConnection = $true; $c.IsChecking = $true
    Call $picker 'ResortGroups' ([object[]]@()) | Out-Null
    Call $picker 'RefreshDisplayedRows' ([object[]]@($true,$false,$null)) | Out-Null
    Pump 40
    Check ($model.Slots[0].DisplayedServer.Service.ID -eq 3 -and $model.Slots[0].BadgeServer.IsLastSuccessfulConnection) 'checking star pins first before any current-cycle completion'
    Check (!$model.IsTransitioning -and !(Field $overlay 'ShowBand')) 'pin change applies immediately without a stale sweep'
    Unique $model.Slots
    $before = Order $model.Slots
    $picker.BeginCheckCycle()
    foreach ($g in @($a,$b,$c)) { $g.Service.GetServerUrls()[0].latency = 0 }
    $picker.ResetTestResults(); $picker.EndCheckCycleSetup(); Pump 900
    Check ((Order $model.Slots) -eq $before -and !$model.IsTransitioning -and !(Field $overlay 'ShowBand')) 'check-again reset preserves order without light'
    $a.IsChecking = $true
    $c.Service.GetServerUrls()[0].latency = 50; $picker.RefreshGroup($c.Service); Pump 40
    $window.WindowState = [Windows.WindowState]::Minimized; Pump 100
    Check ((Order $model.Slots) -eq $before) 'minimize keeps unfinished countries fixed with only one completed country'
    Check (!(Field $overlay 'ShowBand') -and !(Field $animator '_subscribed') -and (Field $overlay 'Flashes').Count -eq 0) 'minimize releases timeline and all overlays'
    Check-FlagClocks $false
    $window.WindowState = [Windows.WindowState]::Normal; Pump 100
    Check-FlagClocks ([System.Windows.SystemParameters]::ClientAreaAnimation)
    Check (!(Field $overlay 'ShowBand')) 'restore never replays obsolete work'
} finally { $window.Close() }
Check (!(Field $animator '_subscribed')) 'close/unload unsubscribes rendering'
Check-FlagClocks $false
Write-Output 'PASS native compiled picker comet lifecycle; no networking or VPN started'
