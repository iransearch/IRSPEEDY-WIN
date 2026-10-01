"""Source-level theme resource/type/contrast checks; not a WPF rendering test."""
from pathlib import Path
import re
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2] / 'IRSpeedyVPN'
KEY = '{http://schemas.microsoft.com/winfx/2006/xaml}Key'
DOCS = {p: ET.parse(p) for p in ROOT.rglob('*.xaml') if 'obj' not in p.parts}

def palette(name):
    items = DOCS[ROOT / ('Themes/Palette.' + name + '.xaml')].getroot()
    values = {n.get(KEY): n.text for n in items}
    assert len(values) == len(items), 'Duplicate palette keys'
    return values

light, dark = palette('Light'), palette('Dark')
assert light.keys() == dark.keys(), 'Light/dark resource sets differ'
keys = {n.get(KEY) for doc in DOCS.values() for n in doc.iter() if n.get(KEY)}
for p, doc in DOCS.items():
    for n in doc.iter():
        for prop, value in n.attrib.items():
            ref = re.fullmatch(r'\{(?:Static|Dynamic)Resource ([\w.]+)\}', value)
            if not ref:
                continue
            key = ref.group(1)
            assert key in keys, (p, key, 'unresolved resource')
            if key in light:
                assert prop == 'Color', (p, prop, key, 'Color used where a Brush is needed')
            if prop == 'Color' and key.endswith('Brush'):
                raise AssertionError((p, prop, key, 'Brush used where a Color is needed'))
print('PASS all XAML parses, palette parity, resource references and Color/Brush types')

def luminance(value):
    value = value.lstrip('#')[-6:]
    rgb = [int(value[i:i+2], 16) / 255 for i in (0, 2, 4)]
    rgb = [x / 12.92 if x <= .04045 else ((x + .055) / 1.055) ** 2.4 for x in rgb]
    return sum(x * w for x, w in zip(rgb, (.2126, .7152, .0722)))

# Small text, labels, placeholders and status values remain readable on all
# of the main dark surfaces; a bright screenshot alone would not catch this.
minimum = 100
for fg in ('TextPrimaryColor', 'TextBodyColor', 'TextSecondaryColor', 'TextMutedColor',
           'FieldLabelColor', 'FieldPlaceholderIconColor', 'SmartLocationSubtitleColor',
           'ConnectedGreenColor', 'Theme.DangerColor'):
    for bg in ('WindowBgTopColor', 'Theme.SurfaceColor', 'SmartLocationBgTopColor'):
        a, b = sorted((luminance(dark[fg]), luminance(dark[bg])))
        contrast = (b + .05) / (a + .05)
        assert contrast >= 4.5, (fg, bg, contrast)
        minimum = min(minimum, contrast)
print('PASS dark text contrast >= 4.5:1 (minimum %.2f:1)' % minimum)

# No page-local palette may override the selected app palette. This guards
# the separately merged connection-method and split-tunnel resource trees.
for p, doc in DOCS.items():
    if p.name.startswith('Palette.'):
        continue
    assert not any(n.get(KEY) in light for n in doc.iter()), (p, 'local palette shadowing')
print('PASS application palette is not shadowed by page-local resources')
