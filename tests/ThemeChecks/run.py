"""Source-level theme resource/type/contrast checks; not a WPF rendering test."""
from pathlib import Path
import re
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2] / 'IRSpeedyVPN'
KEY = '{http://schemas.microsoft.com/winfx/2006/xaml}Key'
DOCS = {p: ET.parse(p) for p in ROOT.rglob('*.xaml') if 'obj' not in p.parts}

def palette(name):
    items = DOCS[ROOT / ('Themes/Palette.' + name + '.xaml')].getroot()
    values = {n.get(KEY): n for n in items}
    assert len(values) == len(items), 'Duplicate palette keys'
    return values

light, dark = palette('Light'), palette('Dark')
assert light.keys() == dark.keys(), 'Light/dark resource sets differ'
color_keys = {key for key, value in light.items() if value.tag.endswith('}Color')}
brush_keys = set(light) - color_keys
for name, resources in (('Light', light), ('Dark', dark)):
    seen = set()
    for key, node in resources.items():
        assert node.tag == light[key].tag, (name, key, 'resource type differs')
        for child in node.iter():
            for value in child.attrib.values():
                assert not value.startswith('{DynamicResource '), (name, key, 'nested live palette lookup')
                ref = re.fullmatch(r'\{StaticResource ([\w.]+)\}', value)
                if ref:
                    assert ref.group(1) in seen, (name, key, 'palette must resolve its own earlier color')
        seen.add(key)
print('PASS complete palettes contain %d matching brushes with self-contained color references' % len(brush_keys))
keys = {n.get(KEY) for doc in DOCS.values() for n in doc.iter() if n.get(KEY)}
for p, doc in DOCS.items():
    for n in doc.iter():
        for prop, value in n.attrib.items():
            ref = re.fullmatch(r'\{(?:Static|Dynamic)Resource ([\w.]+)\}', value)
            if not ref:
                continue
            key = ref.group(1)
            assert key in keys, (p, key, 'unresolved resource')
            if key in color_keys:
                assert prop == 'Color', (p, prop, key, 'Color used where a Brush is needed')
            if key in brush_keys and not p.name.startswith('Palette.'):
                assert value.startswith('{DynamicResource '), (p, key, 'theme brush captured statically')
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
        a, b = sorted((luminance(dark[fg].text), luminance(dark[bg].text)))
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

# The screenshots exposed dark row text on light surfaces. Verify the exact
# roles used by country rows, traffic rows and connected values in both modes.
for name, resources in (('Light', light), ('Dark', dark)):
    for fg, bg in (('RowTitleColor', 'Theme.SurfaceColor'),
                   ('TextBodyColor', 'WindowBgTopColor'),
                   ('SmartLocationTitleColor', 'SmartLocationBgTopColor')):
        a, b = sorted((luminance(resources[fg].text), luminance(resources[bg].text)))
        assert (b + .05) / (a + .05) >= 4.5, (name, fg, bg, 'unreadable paired roles')
print('PASS reported row/card/text roles have matching readable light and dark surfaces')
