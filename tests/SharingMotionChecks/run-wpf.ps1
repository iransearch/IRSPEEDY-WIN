# Run against an unobfuscated Windows build; no VPN or hotspot is started.
# powershell.exe -NoProfile -STA -File tests/SharingMotionChecks/run-wpf.ps1 -AppPath <IRSpeedyVPN.exe>
param([Parameter(Mandatory=$true)][string]$AppPath)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase
$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path $AppPath).Path)
[System.Windows.Application]::ResourceAssembly = $assembly
$app = [Activator]::CreateInstance($assembly.GetType('IRSpeedyVPN.App', $true))
$app.InitializeComponent()
$managerType = $assembly.GetType('IRSpeedyVPN.Common.ThemeManager', $true)
$manager = $managerType.GetProperty('Instance').GetValue($null, $null)
$manager.Initialize()
$apply = $managerType.GetMethod('Apply', [Reflection.BindingFlags]'Instance,NonPublic')
$motionType = $assembly.GetType('IRSpeedyVPN.Controls.ProxySharingMotion', $true)
$motionField = $motionType.GetField('motion', [Reflection.BindingFlags]'Instance,NonPublic')
$faults = [System.Collections.Generic.List[System.Exception]]::new()
$app.add_DispatcherUnhandledException({ param($sender, $eventArgs)
    $faults.Add($eventArgs.Exception)
    $eventArgs.Handled = $true
})

function Check($condition, [string]$label) {
    if (!$condition) { throw $label }
    Write-Output "PASS $label"
}
function Pump {
    $frame = [System.Windows.Threading.DispatcherFrame]::new()
    $timer = [System.Windows.Threading.DispatcherTimer]::new()
    $timer.Interval = [TimeSpan]::FromMilliseconds(80)
    $timer.add_Tick({ $timer.Stop(); $frame.Continue = $false })
    $timer.Start()
    [System.Windows.Threading.Dispatcher]::PushFrame($frame)
}

$panel = [System.Windows.Controls.StackPanel]::new()
$first = [Activator]::CreateInstance($motionType)
$second = [Activator]::CreateInstance($motionType)
$first.Visibility = $second.Visibility = [System.Windows.Visibility]::Collapsed
$panel.Children.Add($first) | Out-Null
$panel.Children.Add($second) | Out-Null
$window = [System.Windows.Window]::new()
$window.Width = 420
$window.Height = 420
$window.ShowInTaskbar = $false
$window.Content = $panel
try {
    $window.Show()
    Pump
    foreach ($dark in @($false, $true, $false)) {
        $apply.Invoke($manager, @($dark, $false)) | Out-Null
        # Reproduce immutable palette resources even when this build loads them unfrozen.
        $paletteBrush = $app.FindResource('Theme.AccentLineBrush')
        if (!$paletteBrush.IsFrozen) { $paletteBrush.Freeze() }
        $paletteOpacity = $paletteBrush.Opacity
        for ($cycle = 0; $cycle -lt 3; $cycle++) {
            $first.Visibility = $second.Visibility = [System.Windows.Visibility]::Visible
            Pump
            $firstStroke = $first.FindName('Tunnel').Stroke
            $secondStroke = $second.FindName('Tunnel').Stroke
            Check ($faults.Count -eq 0) "no dispatcher animation fault (dark=$dark, cycle=$cycle)"
            Check (!$firstStroke.IsFrozen -and !$secondStroke.IsFrozen) 'animation targets remain mutable'
            Check (![object]::ReferenceEquals($firstStroke, $secondStroke) -and
                   ![object]::ReferenceEquals($firstStroke, $paletteBrush)) 'views have independent animated brushes'
            Check ($firstStroke.HasAnimatedProperties -and $secondStroke.HasAnimatedProperties -and
                   $null -ne $motionField.GetValue($first)) 'production storyboard starts on both visible views'
            Check ($firstStroke.Color -eq $app.FindResource('Theme.AccentLineColor')) 'animated stroke follows the current theme'
            Check ($paletteBrush.Opacity -eq $paletteOpacity -and !$paletteBrush.HasAnimatedProperties) 'palette brush is unchanged'
            $first.Visibility = [System.Windows.Visibility]::Collapsed
            Pump
            Check (!$firstStroke.HasAnimatedProperties -and $null -eq $motionField.GetValue($first)) 'hidden view removes its animation clocks'
            Check ($secondStroke.HasAnimatedProperties) 'hiding one view keeps the other animation running'
            $second.Visibility = [System.Windows.Visibility]::Collapsed
            Pump
        }
        # Leave motion running across the next theme switch as well.
        $first.Visibility = $second.Visibility = [System.Windows.Visibility]::Visible
        Pump
    }
    $panel.Children.Clear()
    Pump
    Check ($null -eq $motionField.GetValue($first) -and $null -eq $motionField.GetValue($second)) 'unloaded views release their storyboards'
    $replacement = [Activator]::CreateInstance($motionType)
    $panel.Children.Add($replacement) | Out-Null
    Pump
    Check ($faults.Count -eq 0 -and $replacement.FindName('Tunnel').Stroke.HasAnimatedProperties) 'recreated view starts without a crash'
} finally {
    $window.Close()
}
