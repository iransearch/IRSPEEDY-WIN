# Requires an unobfuscated Windows build. No VPN initialization or preference writes.
# powershell.exe -NoProfile -STA -File tests/ThemeChecks/run-wpf.ps1 -AppPath <IRSpeedyVPN.exe>
param([Parameter(Mandatory=$true)][string]$AppPath)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase
$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path $AppPath).Path)
[System.Windows.Application]::ResourceAssembly = $assembly
$app = [Activator]::CreateInstance($assembly.GetType('IRSpeedyVPN.App', $true))
$app.InitializeComponent()
$type = $assembly.GetType('IRSpeedyVPN.Common.ThemeManager', $true)
$manager = $type.GetProperty('Instance').GetValue($null, $null)
$manager.Initialize()
$apply = $type.GetMethod('Apply', [Reflection.BindingFlags]'Instance,NonPublic')
function Set-Mode([bool]$dark) { $apply.Invoke($manager, @($dark, $false)) | Out-Null }
function New-View([string]$name) { [Activator]::CreateInstance($assembly.GetType($name, $true)) }
function Check($condition, $label) { if (!$condition) { throw $label }; Write-Output "PASS $label" }

# Render two consumers of each affected resource, without loading VPN views.
# One is cached before the switch; another is created after it. Comparing only
# Application.FindResource missed the mixed backgrounds/text shown in reports.
function New-PaletteProbe {
    $panel = New-Object System.Windows.Controls.StackPanel
    $panel.SetResourceReference([System.Windows.Controls.Panel]::BackgroundProperty, 'WindowBackgroundFallbackBrush')
    $row = New-Object System.Windows.Controls.TextBlock
    $row.Text = 'country / traffic row'
    $row.SetResourceReference([System.Windows.Controls.TextBlock]::BackgroundProperty, 'Theme.SurfaceBrush')
    $row.SetResourceReference([System.Windows.Controls.TextBlock]::ForegroundProperty, 'RowTitleBrush')
    $card = New-Object System.Windows.Controls.TextBlock
    $card.Text = 'connected server'
    $card.SetResourceReference([System.Windows.Controls.TextBlock]::BackgroundProperty, 'SmartLocationBackgroundBrush')
    $card.SetResourceReference([System.Windows.Controls.TextBlock]::ForegroundProperty, 'SmartLocationTitleBrush')
    $panel.Children.Add($row) | Out-Null
    $panel.Children.Add($card) | Out-Null
    return $panel
}
function Check-PaletteProbe($panel, [bool]$dark) {
    $panel.Measure([System.Windows.Size]::new(420, 100))
    $panel.Arrange([System.Windows.Rect]::new(0, 0, 420, 100))
    $panel.UpdateLayout()
    $bitmap = [System.Windows.Media.Imaging.RenderTargetBitmap]::new(420, 100, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($panel)
    $expected = if ($dark) { @('#FF181F2B', '#FF1C2535', '#FFE5EBF7', '#FF26324B', '#FFCAD8FF') } else { @('#FFFFFFFF', '#FFFFFFFF', '#FF1B2033', '#FFEFF5FF', '#FF142057') }
    $actual = @($panel.Background.GradientStops[0].Color.ToString(), $panel.Children[0].Background.Color.ToString(),
        $panel.Children[0].Foreground.Color.ToString(), $panel.Children[1].Background.GradientStops[0].Color.ToString(),
        $panel.Children[1].Foreground.Color.ToString())
    Check (($actual -join ',') -eq ($expected -join ',')) ('rendered palette is coherent (dark=' + $dark + '): ' + ($actual -join ','))
}

# Exercise cold dark resource loading before any light-mode view is created.
Set-Mode $true
$coldDark = New-PaletteProbe
Check-PaletteProbe $coldDark $true

Set-Mode $false
$cachedProbe = New-PaletteProbe
Check-PaletteProbe $coldDark $false
Check-PaletteProbe $cachedProbe $false
$count = $app.Resources.MergedDictionaries.Count
$login = New-View 'IRSpeedyVPN.UserControls.UCLogin'
$toggle = New-Object System.Windows.Controls.Primitives.ToggleButton
$toggle.Style = $app.FindResource('AppearanceToggle')
$toggle.ApplyTemplate() | Out-Null
$toggle.Measure([System.Windows.Size]::new(34, 34))
$toggle.Arrange([System.Windows.Rect]::new(0, 0, 34, 34))
$toggle.UpdateLayout()
$sun = $toggle.Template.FindName('SunIcon', $toggle)
$moon = $toggle.Template.FindName('MoonIcon', $toggle)
Check ($toggle.ActualWidth -eq 34 -and $sun.Visibility -eq 'Visible' -and $moon.Visibility -eq 'Collapsed') 'light header template lays out with sun icon'
$binding = [System.Windows.Data.BindingOperations]::GetBinding($toggle, [System.Windows.Controls.Primitives.ToggleButton]::IsCheckedProperty)
Check ($binding.Mode -eq 'TwoWay' -and $binding.Source -eq $manager) 'header toggle writes back to the application theme manager'
$connected = New-View 'IRSpeedyVPN.UserControls.UCUserInfo'
$loading = New-View 'IRSpeedyVPN.UserControls.UCLoginLoading'
$split = New-View 'IRSpeedyVPN.Windows.SettingsSplitTunnelApps'
$login.SetUserPassword('theme-test', 'retained-input', $true)
$lightText = $login.FindName('txtUsername').Foreground.Color
$lightCard = $connected.FindName('ConnectedServerCard').Background.GradientStops[0].Color
$cachedLabel = $loading.Steps[0].LabelBrush

Set-Mode $true
Check-PaletteProbe $cachedProbe $true
Check-PaletteProbe (New-PaletteProbe) $true
Check ($cachedLabel.Color.ToString() -eq '#FFA9B4CB') 'cached view-model brush follows dark mode'
Check ($login.FindName('txtUsername').Foreground.Color -ne $lightText) 'cached login foreground updates'
Check ($connected.FindName('ConnectedServerCard').Background.GradientStops[0].Color -ne $lightCard) 'cached connected gradient updates'
Check ($toggle.IsChecked -eq $true) 'header toggle binding follows dark mode'
Check ($moon.Visibility -eq 'Visible' -and $sun.Visibility -eq 'Collapsed') 'dark header template displays moon icon'
Check ($split.FindResource('Brush.CardBg').Color.ToString() -eq '#FF1C2535') 'local split dictionary follows app palette'
Check ($loading.Steps[0].LabelBrush.Color.ToString() -eq '#FFA9B4CB') 'login progress labels follow app palette'
$password = New-View 'IRSpeedyVPN.Windows.SettingsPassword'
Check ($password.FindName('CurrentPassword').Foreground.Color -eq $login.FindName('txtUsername').Foreground.Color) 'new windows inherit dark text'
for ($i=0; $i -lt 20; $i++) { Set-Mode (($i % 2) -eq 0) }
Set-Mode $false
Check-PaletteProbe $cachedProbe $false
Check ($cachedLabel.Color.ToString() -eq '#FF9CA3B4') 'cached view-model brush returns to light mode'
Check ($app.Resources.MergedDictionaries.Count -eq $count) 'repeated toggles do not accumulate dictionaries'
Check ($login.FindName('txtUsername').Foreground.Color -eq $lightText) 'light foreground is restored'
Check ($connected.FindName('ConnectedServerCard').Background.GradientStops[0].Color -eq $lightCard) 'light gradient is restored'
Check ($toggle.IsChecked -eq $false -and $sun.Visibility -eq 'Visible') 'header toggle and sun icon return to light mode'
Check ($login.FindName('txtPassword').Password -eq 'retained-input') 'theme changes preserve entered credentials'
$split.Close()
$password.Close()
