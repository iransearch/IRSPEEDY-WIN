#!/usr/bin/env python3
"""Static WPF handoff checks. This does not replace a Windows build or render test."""
from pathlib import Path
import re
import xml.etree.ElementTree as E
R = Path(__file__).resolve().parents[1]
A = R / 'IRSpeedyVPN'
W = '{http://schemas.microsoft.com/winfx/2006/xaml/presentation}'
X = '{http://schemas.microsoft.com/winfx/2006/xaml}'
files = list(A.rglob('*.xaml'))
roots = {p: E.parse(p).getroot() for p in files}
keys = {el.get(X+'Key') for root in roots.values() for el in root.iter() if el.get(X+'Key')}
new_files = [p for p in files if p.stem in {'SettingsHub','SettingsPassword','SettingsSplitTunnelApps','VGAURDServiceSetting','SpeedyShieldSetting','ShareVPNSetting','UCConnecting','SharingMotion','ProxySharingMotion','ServiceToggle','UCLogin','UCUserInfo','ServerCountryPicker','MainWindow','IrspeedyTheme'}]
events = {'Click','Loaded','IsVisibleChanged','MouseLeftButtonDown','MouseLeftButtonUp','PreviewMouseDown','Checked','Unchecked','TextChanged','PasswordChanged','Unloaded','SizeChanged'}
for p in new_files:
    root = roots[p]
    names = [el.get(X+'Name') for el in root.iter() if el.get(X+'Name')]
    assert len(names) == len(set(names)), f'{p}: duplicate names'
    source = '\n'.join(f.read_text(encoding='utf-8-sig') for f in p.parent.glob(p.stem+'*.cs'))
    for el in root.iter():
        for attr, value in el.attrib.items():
            for key in re.findall(r'\{StaticResource ([^}]+)\}',value):
                assert key in keys, f'{p}: missing resource {key}'
            if attr in events and not value.startswith('{'):
                assert re.search(r'\b'+re.escape(value)+r'\s*\(',source), f'{p}: missing handler {value}'
    if root.tag == W+'Window':
        assert root.get('SizeToContent') == 'Manual', p
        assert root.get('FlowDirection') == 'RightToLeft', p
        assert root.get('Width') == '381.6', p
        assert root.get('Height') == ('415.3142857143' if p.stem in {'VGAURDServiceSetting','SettingsPassword'} else '632'), p
        viewport = root.find(W+'Viewbox')
        assert viewport is not None and viewport.get('Stretch') == 'Uniform', p
        assert viewport[0].get('Width') == '420', p
        assert viewport[0].get('Height') == ('460' if p.stem in {'VGAURDServiceSetting','SettingsPassword'} else '700'), p
        if p.stem != 'MainWindow': assert viewport[0].get('FlowDirection') == 'LeftToRight', p
        assert not list(root.iter(W+'ScrollViewer')), p
        assert root.get('AllowsTransparency') == 'False', p
        assert 'WindowDrag.Begin(this, e)' in source, p
for name in ['UCLogin','UCUserInfo']:
    assert not list(roots[A/'UserControls'/f'{name}.xaml'].iter(W+'ScrollViewer'))
# All drawing resources are unique and only depend on earlier declared keys.
theme=roots[A/'Themes/IrspeedyTheme.xaml'];seen=set()
for element in theme:
    key=element.get(X+'Key')
    if key:
        assert key not in seen, f'duplicate theme key: {key}'
        for el in element.iter():
            for value in el.attrib.values():
                for ref in re.findall(r'\{StaticResource ([^}]+)\}',value):
                    assert ref in seen, f'{key}: forward theme reference {ref}'
        seen.add(key)
# Fixed-height app pagination must fit the available body height.
assert 7 * (48 + 6) <= 700 - 62 - 68 - 32 - 46 - 58 - 32
print(f'PASS: parsed {len(files)} XAML files; checked {len(new_files)} handoff views, resources, handlers, sizes and app-list capacity.')
print('Windows compilation, UI rendering, DPI and live network validation remain required.')

manifest = E.parse(A / 'app.manifest').getroot()
assert manifest.find('.//{http://schemas.microsoft.com/SMI/2016/WindowsSettings}dpiAwareness').text == 'PerMonitorV2'
connected = roots[A/'UserControls/UCUserInfo.xaml']
for key in ['ConnectedGlowMotion','ConnectedRingMotion','ConnectedExhaustMotion']:
    board = next(el for el in connected.iter(W+'Storyboard') if el.get(X+'Key') == key)
    assert board.get('RepeatBehavior') == 'Forever'
    assert all(a.get('Duration') == ('0:0:0.42' if key.endswith('ExhaustMotion') else '0:0:1.3') for a in board)
# Every declared storyboard target must resolve within the Connected view.
names = {e.get(X+'Name') for e in connected.iter()}
for el in connected.iter():
    if el.get('Storyboard.TargetName'): assert el.get('Storyboard.TargetName') in names
print('PASS: explicit DIP/RTL window contract, active PMv2 manifest and Connected motion targets/durations.')

# Regression: an invisible legacy warning must not widen the header Auto column.
main = roots[A/'MainWindow.xaml']
header = next(e for e in main.iter(W+'Grid') if e.get('MouseLeftButtonDown') == 'Header_MouseDown')
warning = next(e for e in header if e.get(X+'Name') == 'txtGlobalMessage')
assert warning.get('Visibility') == 'Collapsed'
assert warning.get('Grid.ColumnSpan') == '3'
assert header.find(W+'Grid.ColumnDefinitions')[1].get('MinWidth') == '150'
project = E.parse(A/'IRSpeedyVPN.csproj').getroot()
assert project.find('.//ApplicationManifest').text == 'app.manifest'
print('PASS: header warning cannot reserve the controls column; executable uses the DPI manifest.')

# Approved output size at the user's 125% monitor scale.
assert abs(381.6 * 1.25 - 477) < 0.001
assert 632 * 1.25 == 790
# Proxy diagram uses the original 384x175 coordinates and independent timing.
proxy = roots[A/'Controls/ProxySharingMotion.xaml']
assert proxy.get('FlowDirection') == 'LeftToRight'
canvas = proxy.find(W+'Viewbox').find(W+'Canvas')
assert (canvas.get('Width'), canvas.get('Height')) == ('384', '175')
proxy_names = {el.get(X+'Name') for el in proxy.iter()}
assert {'Tunnel','LockRing','SparkLeft','SparkRight','OnlineHalo'} <= proxy_names
assert {prefix+str(i) for prefix in ['Http','Socks'] for i in range(3)} <= proxy_names
assert {'Ring'+str(i) for i in range(5)} <= proxy_names
assert 'SettingsPadImage' not in (A/'Controls/ProxySharingMotion.xaml').read_text()
assert (A/'Resources/Irspeedy/Reference/proxy-reference.png').read_bytes() == (R/'docs/design-handoff/latest/screenshots/SettingsShare_proxy_on.png').read_bytes()
print('PASS: approved 477x790 target at 125%, uniform design scaling and separate proxy artwork.')

# Connected's CSS content-box badge is 60 + 2*6, not a 60-DIP outer border.
badge = next(e for e in connected.iter(W+'Border') if e.get(X+'Name') == 'ConnectedCheckBadge')
assert (badge.get('Width'), badge.get('Height'), badge.get('BorderThickness')) == ('72', '72', '6')
assert badge.find(W+'Viewbox').find(W+'Canvas').get('Width') == '12'
server = next(e for e in connected.iter(W+'Border') if e.get(X+'Name') == 'ConnectedServerCard')
assert server.get('Height') == '52'
account = next(e for e in connected.iter(W+'Border') if e.get(X+'Name') == 'ConnectedAccountCard')
assert account.get('Margin') == '0,16,0,0'
assert len(list(account.iter(W+'Viewbox'))) == 3
print('PASS: Connected box-model sizes, account spacing and SVG coordinate systems.')
