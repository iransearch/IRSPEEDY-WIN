# Run on Windows: powershell.exe -NoProfile -STA -File tests/ProbeShineChecks/run-wpf.ps1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$source = Get-Content (Join-Path $repoRoot 'IRSpeedyVPN/Components/ServerListControl/ServerCountryPicker.Motion.cs') -Raw
$start = $source.IndexOf('        private readonly HashSet<FrameworkElement> _probeShines')
$end = $source.IndexOf('        private Window _motionWindow;')
$methods = $source.Substring($start, $end - $start)
$code = @"
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
public class ProbeShineHarness : UserControl {
    private Window _motionWindow;
$methods
    public void Refresh(FrameworkElement e) { UpdateProbeShine(e); }
    public void Unload(FrameworkElement e) { ProbeShine_Unloaded(e, new RoutedEventArgs()); }
    public static TranslateTransform Shift(FrameworkElement e) { return ProbeShift(e); }
}
"@
Add-Type -TypeDefinition $code -ReferencedAssemblies @('PresentationFramework', 'PresentationCore', 'WindowsBase', 'System.Xaml')
$template = New-Object System.Windows.Media.TransformGroup
$template.Children.Add((New-Object System.Windows.Media.SkewTransform))
$template.Children.Add((New-Object System.Windows.Media.TranslateTransform -ArgumentList -20,0))
$template.Freeze()
$a = New-Object System.Windows.Shapes.Rectangle
$b = New-Object System.Windows.Shapes.Rectangle
$a.RenderTransform = $template
$b.RenderTransform = $template
$hostControl = New-Object ProbeShineHarness
# Reproduces the crash: UpdateProbeShine removes the clock before IsLoaded is true.
$hostControl.Refresh($a)
$hostControl.Refresh($b)
$x = [ProbeShineHarness]::Shift($a)
$y = [ProbeShineHarness]::Shift($b)
if ($x.IsFrozen -or $y.IsFrozen -or [object]::ReferenceEquals($x,$y)) { throw 'Shared or frozen animation target' }
$animation = New-Object System.Windows.Media.Animation.DoubleAnimation -ArgumentList 0,50,([System.Windows.Duration]::new([TimeSpan]::FromSeconds(1)))
$x.BeginAnimation([System.Windows.Media.TranslateTransform]::XProperty, $animation)
$hostControl.Unload($a)
$hostControl.Unload($b)
if ($x.HasAnimatedProperties -or $template.Children[1].HasAnimatedProperties) { throw 'Animation leaked on unload/template' }
$parent = $template.Clone()
$parent.Children[1].Freeze()
$a.RenderTransform = $parent
$hostControl.Refresh($a)
if ([ProbeShineHarness]::Shift($a).IsFrozen) { throw 'Frozen child not cloned' }
Write-Output 'PASS real WPF frozen template, isolated rows, early visibility update, animation and unload'
