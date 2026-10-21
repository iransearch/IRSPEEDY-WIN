# Run with an unobfuscated Windows build; loads real compiled row BAML.
# powershell.exe -NoProfile -STA -File tests/ServerStarGlowChecks/run-wpf.ps1 -AppPath <IRSpeedyVPN.exe>
param([Parameter(Mandatory=$true)][string]$AppPath)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase
$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path $AppPath).Path)
[System.Windows.Application]::ResourceAssembly = $assembly
$app = [Activator]::CreateInstance($assembly.GetType('IRSpeedyVPN.App', $true))
# Closing this test window must not run app exit/VPN cleanup.
$app.ShutdownMode = [System.Windows.ShutdownMode]::OnExplicitShutdown
$app.InitializeComponent()
$themeType = $assembly.GetType('IRSpeedyVPN.Common.ThemeManager', $true)
$theme = $themeType.GetProperty('Instance').GetValue($null, $null)
$theme.Initialize()
Add-Type -ReferencedAssemblies @('System', 'WindowsBase', 'PresentationCore') -TypeDefinition @'
using System.ComponentModel;
using System.Windows.Media;
public sealed class StarRowProbe : INotifyPropertyChanged {
    private bool star = true;
    public event PropertyChangedEventHandler PropertyChanged;
    public bool IsLastSuccessfulConnection {
        get { return star; }
        set { star=value;var h=PropertyChanged;if(h!=null)h(this,new PropertyChangedEventArgs("IsLastSuccessfulConnection")); }
    }
    public string CountryName { get { return "country"; } }
    public string SignalText { get { return "142 ms"; } }
    public string SignalToolTip { get { return "test"; } }
    public string Initials { get { return "F"; } }
    public ImageSource FlagSource { get { return null; } }
    public Brush SignalBrush { get { return Brushes.LimeGreen; } }
    public bool HasFlag { get { return false; } }
    public bool IsChecking { get { return false; } }
    public bool IsSelectedCountry { get { return false; } }
}
'@
function Check($ok, [string]$name) { if (!$ok) { throw $name }; Write-Output "PASS $name" }
function Drain-Layout {
    $frame = New-Object System.Windows.Threading.DispatcherFrame
    $finish = { $frame.Continue = $false }.GetNewClosure()
    [System.Windows.Threading.Dispatcher]::CurrentDispatcher.BeginInvoke(
        [System.Windows.Threading.DispatcherPriority]::ApplicationIdle, [Action]$finish) | Out-Null
    [System.Windows.Threading.Dispatcher]::PushFrame($frame)
}
function Find-Star($node) {
    if ($node -is [System.Windows.Shapes.Path] -and $node.Effect -is [System.Windows.Media.Effects.DropShadowEffect]) { return $node }
    for ($i=0; $i -lt [System.Windows.Media.VisualTreeHelper]::GetChildrenCount($node); $i++) {
        $found = Find-Star ([System.Windows.Media.VisualTreeHelper]::GetChild($node,$i))
        if ($null -ne $found) { return $found }
    }
    return $null
}
$picker = [Activator]::CreateInstance($assembly.GetType('IRSpeedyVPN.Components.ServerListControl.ServerCountryPicker', $true))
$layout = New-Object System.Windows.Controls.StackPanel
$picker.Height = 90
$layout.Children.Add($picker) | Out-Null
$window = New-Object System.Windows.Window
$window.Width = 420; $window.Height = 240
$window.ShowInTaskbar = $false; $window.ShowActivated = $false
$window.Content = $layout
try {
    $window.Show(); Drain-Layout
    $rowTemplate = $picker.FindResource('ServerRowContent')
    $a = New-Object System.Windows.Controls.ContentPresenter
    $b = New-Object System.Windows.Controls.ContentPresenter
    $dataA = New-Object StarRowProbe; $dataB = New-Object StarRowProbe
    foreach ($presenter in @($a,$b)) {
        $presenter.Height = 30; $presenter.Margin = [System.Windows.Thickness]::new(13,0,13,0)
        $presenter.ContentTemplate = $rowTemplate
    }
    $a.Content = $dataA; $b.Content = $dataB
    # Star=true BEFORE first template construction, inside an already visible window.
    # This is the 1.4.15 failure that compile-only and empty-list tests missed.
    $layout.Children.Add($a) | Out-Null; $layout.Children.Add($b) | Out-Null
    $layout.UpdateLayout(); Drain-Layout
    $starA = Find-Star $a; $starB = Find-Star $b
    Check ($null -ne $starA -and $null -ne $starB) 'cold already-starred row templates load without XamlParseException'
    $animate = [System.Windows.SystemParameters]::ClientAreaAnimation
    Check ($starA.Effect.HasAnimatedProperties -eq $animate) 'star starts after Loaded and respects Windows motion preference'
    if ($animate) {
        Check (!$starA.Effect.IsFrozen -and !$starB.Effect.IsFrozen -and ![object]::ReferenceEquals($starA.Effect,$starB.Effect)) 'both presenters own separate mutable WPF effects'
    }
    $glowA = $starA.Effect
    $dataA.IsLastSuccessfulConnection = $false; Drain-Layout
    Check (!$glowA.HasAnimatedProperties) 'moving the saved star stops its old clocks'
    $dataA.IsLastSuccessfulConnection = $true; Drain-Layout
    Check ($starA.Effect.HasAnimatedProperties -eq $animate) 'star resumes on a newly visible row'
    $a.Visibility = [System.Windows.Visibility]::Collapsed; Drain-Layout
    Check (!$starA.Effect.HasAnimatedProperties) 'hiding a sweep presenter stops glow clocks'
    $a.Visibility = [System.Windows.Visibility]::Visible; Drain-Layout
    $window.WindowState = [System.Windows.WindowState]::Minimized; Drain-Layout
    Check (!$starA.Effect.HasAnimatedProperties -and !$starB.Effect.HasAnimatedProperties) 'minimizing stops both presenters'
    $window.WindowState = [System.Windows.WindowState]::Normal; Drain-Layout
    Check ($starA.Effect.HasAnimatedProperties -eq $animate) 'restoring restarts only visible stars'
    $effectBeforeUnload = $starA.Effect
    $layout.Children.Remove($a); Drain-Layout
    Check (!$effectBeforeUnload.HasAnimatedProperties) 'unloading a reused row releases its animation clocks'
} finally {
    $window.Close()
}
Write-Output 'PASS real compiled starred-row BAML and WPF effect lifecycle; no server probes or VPN initialization'
