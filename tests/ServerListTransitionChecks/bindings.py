"""Source regression for WPF's Run.Text TwoWay default versus immutable snapshots.

This check complements run.py's state-machine tests. The Windows BAML companion
in ServerStarGlowChecks loads getter-only country/ping data into the real row.
"""
from pathlib import Path
import re
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[2]
source = root / 'IRSpeedyVPN/Components/ServerListControl'
w = '{http://schemas.microsoft.com/winfx/2006/xaml/presentation}'
x = '{http://schemas.microsoft.com/winfx/2006/xaml}'
xml = ET.parse(source / 'ServerCountryPicker.xaml').getroot()
row = next(e for e in xml.iter(w + 'DataTemplate') if e.get(x + 'Key') == 'ServerRowContent')
country_runs = [e for e in row.iter(w + 'Run') if 'CountryName' in e.get('Text', '')]
assert len(country_runs) == 1
one_way = re.compile(r'\bMode\s*=\s*OneWay\b')
assert one_way.search(country_runs[0].get('Text')), 'Run.Text defaults to TwoWay, which crashes with getter-only ServerItem.CountryName; declare Mode=OneWay'
assert country_runs[0].get('FlowDirection') == 'RightToLeft'
print('PASS Persian country Run explicitly reads its immutable snapshot with OneWay binding')

items = next(e for e in xml.iter(w + 'ItemsControl') if e.get(x + 'Name') == 'icCountries')
controls = [e for e in xml.iter() if e.tag.endswith('}LightSweepRowTransition')]
count = 0
for fragment in [row, items] + controls:
    for element in fragment.iter():
        for prop, value in element.attrib.items():
            if value.startswith('{Binding'):
                assert one_way.search(value), (element.tag, prop, value, 'snapshot display must never write to the source')
                count += 1
assert count >= 15, 'row bindings were not inspected'
print('PASS all %d snapshot/slot bindings explicitly use OneWay, including triggers and automation' % count)

track = next(e for e in xml.iter(w + 'Track') if e.get(x + 'Name') == 'PART_Track')
assert 'Mode=TwoWay' in track.get('Value')
print('PASS scroll thumb retains its intentional writable TwoWay control binding')
model = (source / 'ServerListViewModel.cs').read_text()
assert 'public string CountryName { get; }' in model
assert 'public string SignalText { get; }' in model
print('PASS display snapshots remain immutable; no writable-model workaround')
