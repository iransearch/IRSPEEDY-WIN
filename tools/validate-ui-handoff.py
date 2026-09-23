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
# XML parsing accepts this ordering, but WPF's XAML compiler rejects a Style
# whose property element (Style.Triggers) splits its collection of Setters.
for path, root in roots.items():
    for style in root.iter(W+'Style'):
        children = list(style)
        for index, child in enumerate(children):
            if child.tag == W+'Style.Triggers':
                assert not any(later.tag == W+'Setter' for later in children[index + 1:]), (
                    f'{path}: Style.Triggers must follow all Setter children (MC3088)')
keys = {el.get(X+'Key') for root in roots.values() for el in root.iter() if el.get(X+'Key')}
new_files = [p for p in files if p.stem in {'SettingsHub','SettingsPassword','SettingsSplitTunnelApps','VGAURDServiceSetting','SpeedyShieldSetting','ShareVPNSetting','UCConnecting','SharingMotion','ProxySharingMotion','HotspotBroadcastMotion','ServiceToggle','UCLogin','UCUserInfo','ServerCountryPicker','UCServerList','MainWindow','IrspeedyTheme','ServerListFlags'}]
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
        assert root.get('Width') == ('420' if p.stem == 'MainWindow' else '381.6'), p
        assert root.get('Height') == ('700' if p.stem == 'MainWindow' else '415.3142857143' if p.stem in {'VGAURDServiceSetting','SettingsPassword'} else '632'), p
        viewport = root.find(W+'Viewbox')
        assert viewport is not None and viewport.get('Stretch') == 'Uniform', p
        assert viewport[0].get('Width') == '420', p
        assert viewport[0].get('Height') == ('460' if p.stem in {'VGAURDServiceSetting','SettingsPassword'} else '700'), p
        if p.stem != 'MainWindow': assert viewport[0].get('FlowDirection') == 'LeftToRight', p
        if p.stem != 'SettingsSplitTunnelApps': assert not list(root.iter(W+'ScrollViewer')), p
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
# Installed applications use a bounded, continuous scroller.
assert roots[A/'Windows/SettingsSplitTunnelApps.xaml'].find('.//'+W+'ScrollViewer') is not None
assert 'PageSize' not in (A/'Windows/SettingsSplitTunnelApps.xaml.cs').read_text()
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

# Latest server-list handoff specifies logical DIPs, not physical screenshot pixels.
assert float(main.get('Width')) * 1.25 == 525
assert float(main.get('Height')) * 1.25 == 875
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
print('PASS: main window is 420x700 DIP (525x875 at 125%); separate proxy artwork preserved.')

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

# Original flame alpha must remain stationary while only its light brush moves.
rocket = next(e for e in connected.iter() if e.get('{http://schemas.microsoft.com/winfx/2006/xaml}Name') == 'ConnectedRocketArtwork') if 'connected' in globals() else None
import xml.etree.ElementTree as _ET
_cr = _ET.parse('IRSpeedyVPN/UserControls/UCUserInfo.xaml').getroot()
_ns = {'w': 'http://schemas.microsoft.com/winfx/2006/xaml/presentation'}
_name = '{http://schemas.microsoft.com/winfx/2006/xaml}Name'
_art = next(e for e in _cr.iter() if e.get(_name) == 'ConnectedRocketArtwork')
assert [e.get('Source', '').split('/')[-1] for e in _art if e.tag.endswith('}Image')] == ['01-shield-heart-background.png', '02-exhaust-flame-shape.png', '03-rocket-foreground.png']
_light = next(e for e in _art if e.get(_name) == 'ExhaustLight')
assert _light.find('w:Rectangle.OpacityMask/w:ImageBrush', _ns).get('ImageSource').endswith('/02-exhaust-flame-shape.png')
assert _light.find('w:Rectangle.RenderTransform', _ns) is None
assert not _art.findall('.//w:PathGeometry', _ns)
print('PASS: original rocket layer order and stationary PNG alpha mask; no approximate flame geometry.')

# Physical scrollbar placement must be independent of inherited RTL.
picker = roots[A/'Components/ServerListControl/ServerCountryPicker.xaml']
scroll_template = next(e for e in picker.iter(W+'ControlTemplate') if e.get('TargetType') == 'ScrollViewer')
assert scroll_template.find(W+'Grid').get('FlowDirection') == 'LeftToRight'
hotspot = roots[A/'Controls/HotspotBroadcastMotion.xaml']
assert hotspot.get('Unloaded') == 'Motion_Unloaded'
assert len([e for e in hotspot.iter(W+'Path') if (e.get(X+'Name') or '').startswith('Route')]) == 3
assert (A/'Resources/Irspeedy/Reference/user-second-device.png').exists()
print('PASS: left scrollbar coordinate frame, continuous app list, direct motion lifecycle and receiver asset.')

# Both proxy instructions remain separate, with the original 20-DIP numbered circles.
share = roots[A/'Windows/ShareVPNSetting.xaml']
step_two = next(e for e in share.iter(W+'Grid') if e.get(X+'Name') == 'ProxyStepTwo')
round_badge = next(e for e in theme.iter(W+'Style') if e.get(X+'Key') == 'HandoffRoundBadge')
assert any(e.get('Property') == 'CornerRadius' and e.get('Value') == '100' for e in round_badge)
proxy_panel = next(e for e in share.iter(W+'Grid') if e.get(X+'Name') == 'ProxyPanel')
steps = [e for e in proxy_panel.iter(W+'Grid') if any(
    child.tag == W+'Border' and child.get('Width') == '20' and child.get('Height') == '20'
    for child in e)]
assert len(steps) == 2 and steps[1] is step_two
for step, number, title, detail, color in [
    (steps[0], '۱', 'فعال‌سازی اشتراک', 'اشتراک‌گذاری VPN را روی این دستگاه فعال کنید.', '#1E3A8A'),
    (steps[1], '۲', 'تنظیم پراکسی روی دستگاه دوم', 'روی دستگاه دوم پراکسی HTTP یا SOCKS5 را با آی‌پی و پورت زیر تنظیم کنید.', '#C7CCD8'),
]:
    badge = next(e for e in step if e.tag == W+'Border')
    assert badge.get('Style') == '{StaticResource HandoffRoundBadge}' and badge.get('Background') == color
    assert badge.find(W+'TextBlock').get('Text') == number
    texts = [e.get('Text') if e.get('Text') is not None else
             ''.join(run.get('Text', '') for run in e.iter(W+'Run'))
             for e in step.iter(W+'TextBlock')]
    assert title in texts and detail in texts
share_code = (A/'Windows/ShareVPNSetting.xaml.cs').read_text(encoding='utf-8')
assert 'ProxyStepTwo.Opacity = active ? 1 : 0.45' in share_code
assert 'proxyIp + " : " + Service.HttpPort' in share_code
assert 'proxyIp + " : " + Service.SocksPort' in share_code
assert '"http://" + proxyIp + ":" + Service.HttpPort' in share_code
assert '"socks5://" + proxyIp + ":" + Service.SocksPort' in share_code
print('PASS: two proxy guide steps, numbered badges, conditional fade, displayed addresses and machine-readable URIs.')

# The direct tab shares the same step badge and renders distinct credentials,
# controls and client count, rather than concatenating them into plain text.
direct = next(e for e in share.iter(W+'Grid') if e.get(X+'Name') == 'DirectPanel')
direct_badge = next(e for e in direct.iter(W+'Border') if e.get('Width') == '20')
assert direct_badge.get('Style') == '{StaticResource HandoffRoundBadge}'
assert direct_badge.find(W+'TextBlock').get('Text') == '۱'
card = next(e for e in direct.iter(W+'Border') if e.get(X+'Name') == 'hotspotCredentials')
card_texts = {e.get('Text') for e in card.iter(W+'TextBlock')}
assert {'اطلاعات هات‌اسپات', 'برای اتصال دستگاه‌ها'} <= card_texts
icon_badges = [e for e in card.iter(W+'Border') if e.get('Width') == '30']
assert {e.get('Background') for e in icon_badges} == {'#1A1E3A8A', '#FDF3E2'}
assert all(e.get('Style') == '{StaticResource HandoffRoundBadge}' for e in icon_badges)
round_buttons = [e for e in card.iter(W+'Button') if e.get('Style') == '{StaticResource HandoffCircleIconButton}']
assert {e.get('Click') for e in round_buttons} == {
    'HotspotCopyName_Click', 'HotspotCopyPassword_Click', 'HotspotEditPassword_Click'}
button_style = next(e for e in theme.iter(W+'Style') if e.get(X+'Key') == 'HandoffCircleIconButton')
assert any(e.get('Property') == 'BorderBrush' and e.get('Value') == '#ECEEF3' for e in button_style)
clients = next(e for e in direct.iter(W+'Grid') if e.get(X+'Name') == 'hotspotClientsRow')
assert any(e.get(X+'Name') == 'hotspotClientCount' for e in clients.iter(W+'TextBlock'))
hotspot_code = (A/'Windows/ShareVPNSetting.Hotspot.cs').read_text(encoding='utf-8')
assert 'hotspotClientCount.Text = IRSpeedyVPN.Common.PersianDigits.Format(view.Clients.ToString())' in hotspot_code
assert 'فعال — دستگاه را به این وای‌فای متصل کنید.' not in hotspot_code
assert 'hotspotStatus.Visibility = string.IsNullOrEmpty(hotspotStatusText.Text) ? Visibility.Collapsed : Visibility.Visible' in hotspot_code
print('PASS: direct-tab guide badge, credential icons, outlined actions, client count and clean active state.')
