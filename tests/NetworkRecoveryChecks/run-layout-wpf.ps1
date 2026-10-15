# Run on Windows with PowerShell -STA. Uses the production geometry and text;
# theme brushes/images/events are removed so the fixture needs no application.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
[xml]$xml = Get-Content (Join-Path $root 'IRSpeedyVPN/UserControls/UCUserInfo.xaml') -Raw -Encoding UTF8
$events = @('Loaded','IsVisibleChanged','Unloaded','PreviewKeyDown','Click','SizeChanged','PreviewMouseDown')
foreach ($node in $xml.SelectNodes('//*')) {
    foreach ($attribute in @($node.Attributes)) {
        if ($attribute.LocalName -eq 'Class' -or $events -contains $attribute.LocalName -or
            $attribute.Value -like '{StaticResource *' -or
            $attribute.LocalName -in @('Source','ImageSource')) {
            $node.RemoveAttributeNode($attribute) | Out-Null
        }
    }
}
$view = [Windows.Markup.XamlReader]::Parse($xml.OuterXml)
$view.Width = 376
$view.Height = 720
$banner = $view.FindName('NetworkRecoveryBanner')
$text = $view.FindName('NetworkRecoveryStatus')
$badge = $view.FindName('ConnectedCheckBadge')
$targets = @('ConnectedServerCard','ConnectedAccountCard','btnDisConnect')
function Positions {
    $view.Measure([Windows.Size]::new(376,720))
    $view.Arrange([Windows.Rect]::new(0,0,376,720))
    $view.UpdateLayout()
    foreach ($name in $targets) {
        $item = $view.FindName($name)
        $point = $item.TranslatePoint([Windows.Point]::new(0,0),$view)
        "$name=$($point.X),$($point.Y),$($item.ActualWidth),$($item.ActualHeight)"
    }
}
$baseline = (Positions) -join ';'
foreach ($label in @('در انتظار برگشت اینترنت','در حال بازیابی اتصال…')) {
    $text.Text = $label
    $banner.Visibility = 'Visible'
    $badge.Visibility = 'Collapsed'
    if (((Positions) -join ';') -ne $baseline) { throw "Recovery changed page geometry: $label" }
    if ($text.ActualWidth -gt $banner.ActualWidth -or $text.ActualHeight -gt $banner.ActualHeight) {
        throw 'Recovery text exceeds its fixed banner'
    }
}
$banner.Visibility = 'Collapsed'
$badge.Visibility = 'Visible'
if (((Positions) -join ';') -ne $baseline) { throw 'Recovered state moved cards or button' }
Write-Output 'PASS real WPF recovery/recovered states preserve card and button positions'
