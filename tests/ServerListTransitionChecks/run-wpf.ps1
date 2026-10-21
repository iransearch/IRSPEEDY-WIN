# Real native WPF clocks and compiled production template. No probes or VPN.
# powershell.exe -NoProfile -STA -File tests/ServerListTransitionChecks/run-wpf.ps1 -AppPath <IRSpeedyVPN.exe>
param([Parameter(Mandatory=$true)][string]$AppPath)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase
$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path $AppPath).Path)
[System.Windows.Application]::ResourceAssembly = $assembly
$app = [Activator]::CreateInstance($assembly.GetType('IRSpeedyVPN.App', $true))
$app.ShutdownMode = [System.Windows.ShutdownMode]::OnExplicitShutdown
$app.InitializeComponent()
$themeType = $assembly.GetType('IRSpeedyVPN.Common.ThemeManager', $true)
$themeType.GetProperty('Instance').GetValue($null, $null).Initialize()
$flags = [Reflection.BindingFlags]'Instance,NonPublic'
$prefix = 'IRSpeedyVPN.Components.ServerListControl.'
$rowType = $assembly.GetType($prefix + 'LightSweepRowTransition', $true)
$slotType = $assembly.GetType($prefix + 'ServerDisplaySlot', $true)
$groupType = $assembly.GetType($prefix + 'GroupItem', $true)
$itemType = $assembly.GetType($prefix + 'ServerItem', $true)
function Check($ok, [string]$name) { if (!$ok) { throw $name }; Write-Output "PASS $name" }
function Field($instance, [string]$name) { return $instance.GetType().GetField($name,$flags).GetValue($instance) }
function Pump {
    $frame = New-Object System.Windows.Threading.DispatcherFrame
    $timer = New-Object System.Windows.Threading.DispatcherTimer
    $timer.Interval = [TimeSpan]::FromMilliseconds(100)
    $timer.Add_Tick({ $frame.Continue = $false }.GetNewClosure())
    $timer.Start()
    try { [System.Windows.Threading.Dispatcher]::PushFrame($frame) } finally { $timer.Stop() }
}
$picker = [Activator]::CreateInstance($assembly.GetType($prefix + 'ServerCountryPicker', $true))
$picker.Height = 90
$layout = New-Object System.Windows.Controls.StackPanel
$layout.Children.Add($picker) | Out-Null
$window = New-Object System.Windows.Window
$window.Width = 420; $window.Height = 260
$window.ShowInTaskbar = $false; $window.ShowActivated = $false
$window.Content = $layout
try {
    $window.Show(); Pump
    $group = [Activator]::CreateInstance($groupType, $true)
    $group.CountryName = 'country'
    # Service=null is deliberate: constructing the immutable visual snapshot
    # needs no actual VPN service. Addition from null still changes identity.
    $ctor = $itemType.GetConstructor($flags, $null, [Type[]]@($groupType), $null)
    $item = $ctor.Invoke([object[]]@($group))
    $slot = [Activator]::CreateInstance($slotType)
    $row = [Activator]::CreateInstance($rowType)
    $row.Style = $picker.FindResource($rowType)
    $row.RowTemplate = $picker.FindResource('ServerRowContent')
    $row.Height = 56; $row.Padding = [System.Windows.Thickness]::new(13,0,13,0)
    $row.Duration = [TimeSpan]::FromSeconds(10)
    $ease = New-Object System.Windows.Media.Animation.QuadraticEase
    $ease.EasingMode = [System.Windows.Media.Animation.EasingMode]::EaseIn
    $row.Easing = $ease; $row.Slot = $slot
    $layout.Children.Add($row) | Out-Null
    $layout.UpdateLayout(); Pump
    $begin = $slotType.GetMethod('Begin',$flags)
    $begin.Invoke($slot,[object[]]@($item,$null)) | Out-Null
    if (![System.Windows.SystemParameters]::ClientAreaAnimation) {
        Check (!$row.IsAnimatingSurface -and $slot.DisplayedServer -eq $item) 'OS motion preference completes immediately'
    } else {
        $clock = Field $row '_storyboard'
        Check ($null -ne $clock -and $row.IsAnimatingSurface) 'production native storyboard starts without property-path exceptions'
        # Clock construction copies/freeze-snapshots its timeline. A direct
        # Freezable Target is copied too, unlike its owning FrameworkElement.
        $frozen = $clock.GetCurrentValueAsFrozen()
        for ($i=0; $i -lt $clock.Children.Count; $i++) {
            $target = [System.Windows.Media.Animation.Storyboard]::GetTarget($clock.Children[$i])
            $copiedTarget = [System.Windows.Media.Animation.Storyboard]::GetTarget($frozen.Children[$i])
            Check ($target -is [System.Windows.FrameworkElement] -and [object]::ReferenceEquals($target,$copiedTarget)) 'clock snapshot retains its real row element instead of cloning a detached transform'
        }
        $clock.Pause($row)
        $clock.SeekAlignedToLastTick($row,[TimeSpan]::FromSeconds(6),[System.Windows.Media.Animation.TimeSeekOrigin]::BeginTime)
        $band = Field $row '_band'
        $old = Field $row '_old'; $next = Field $row '_next'
        $x = $band.RenderTransform.Children[1].X
        $edge = $old.Clip.Transform.X + $row.Padding.Left
        $expectedX = -$row.BandWidth + ($row.ActualWidth + $row.BandWidth) * .36
        Check ($band.RenderTransform.Children[1].HasAnimatedProperties -and $old.Clip.Transform.HasAnimatedProperties -and $next.Clip.Transform.HasAnimatedProperties -and [Math]::Abs($x - $expectedX) -lt .01) 'real visible transforms move to the expected position, rather than merely sharing an unchanged coordinate'
        Check ([Math]::Abs($edge - ($x + $row.BandWidth/2)) -lt .01 -and [Math]::Abs($edge - ($next.Clip.Transform.X + $row.Padding.Left)) -lt .01) 'native band and complementary clip transforms share the eased clock'
        Check ($null -eq $slot.DisplayedServer -and $row.IsAnimatingSurface) 'asymmetric easing keeps old identity before the light reaches midpoint'
        $clock.SeekAlignedToLastTick($row,[TimeSpan]::FromSeconds(8),[System.Windows.Media.Animation.TimeSeekOrigin]::BeginTime)
        $expectedLater = -$row.BandWidth + ($row.ActualWidth + $row.BandWidth) * .64
        Check ([Math]::Abs($band.RenderTransform.Children[1].X - $expectedLater) -lt .01 -and $band.RenderTransform.Children[1].X -gt $x) 'light actually advances physically left to right between separate clock times'
        Check ($slot.DisplayedServer -eq $item -and $row.IsAnimatingSurface) 'discrete marker reveals identity under the native eased light before completion'
        $clock.Resume($row); $clock.SkipToFill($row); Pump
        Check ($null -eq $slot.Transition -and !$row.IsAnimatingSurface -and $band.Visibility -eq [System.Windows.Visibility]::Collapsed -and $null -eq $old.Clip -and $null -eq $old.CacheMode -and $null -eq $next.Content) 'completion releases native clocks, clips, cached surfaces and second presenter'

        # Same identity takes the native 150ms opacity path, not a light band.
        $begin.Invoke($slot,[object[]]@($item,$null)) | Out-Null
        Check ($band.Visibility -eq [System.Windows.Visibility]::Collapsed) 'data-only fade adds no second light band'
        Pump; Pump
        Check (!$row.IsAnimatingSurface -and $null -eq $slot.Transition) 'native short opacity fade completes independently'
        $begin.Invoke($slot,[object[]]@($null,$null)) | Out-Null
        $row.Visibility = [System.Windows.Visibility]::Collapsed
        Pump
        Check (!$row.IsAnimatingSurface -and $null -eq $slot.DisplayedServer -and $null -eq (Field $row '_storyboard')) 'hiding an active removal flushes identity and removes its storyboard'
    }
} finally { $window.Close() }
Write-Output 'PASS compiled row template and native WPF transition lifecycle; no backend required'
