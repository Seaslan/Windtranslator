"""Check resource coverage, format placeholders, and stable storage identities.

Run with Python 3 from any directory. No third-party dependencies are required.
"""
from pathlib import Path
import hashlib
import re
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]


def read_resources(path):
    entries = ET.parse(path).getroot().findall('data')
    values = {entry.attrib['name']: entry.findtext('value') for entry in entries}
    assert len(entries) == len(values), f'Duplicate resource keys: {path}'
    return values


english = read_resources(ROOT / 'Resources/UiStrings.resx')
chinese = read_resources(ROOT / 'Resources/UiStrings.zh-CN.resx')
assert english.keys() == chinese.keys(), 'Chinese and English resource keys differ'
for key in english:
    placeholders = lambda value: sorted(re.findall(r'\{\d+(?:[^}]*)?\}', value))
    assert placeholders(english[key]) == placeholders(chinese[key]), f'Format mismatch: {key}'
    assert english[key] and chinese[key], f'Empty translation: {key}'

xaml = (ROOT / 'MainWindow.xaml').read_text(encoding='utf-8')
ET.fromstring(xaml)
for key in re.findall(r'localization:LocalizedString Key=(\w+)', xaml):
    assert key in english, f'Missing XAML resource: {key}'
assert not re.search(r'="[^"{}]*[\u4e00-\u9fff][^"{}]*"', xaml), 'Unlocalized XAML attribute'

for path in [ROOT / 'MainWindow.xaml.cs', *(ROOT / 'Services').glob('*.cs')]:
    source = path.read_text(encoding='utf-8')
    for literal in re.findall(r'Localization.Text\("((?:\\.|[^"\\])*)"', source):
        value = literal.replace('\\\\', '\\').replace('\\n', '\n').replace('\\"', '"')
        key = 'S' + hashlib.sha256(value.encode()).hexdigest()[:16]
        assert key in english, f'Missing code resource in {path.name}: {value}'

for language, name in [('zh-CN', '风译'), ('en-US', 'Windtanslator')]:
    resources = read_resources(ROOT / f'Strings/{language}/Resources.resw')
    assert resources['AppDisplayName'] == name
    app_key = 'S' + hashlib.sha256(b'Windtranslator').hexdigest()[:16]
    assert (chinese if language == 'zh-CN' else english)[app_key] == name

for filename in ['AppSettingsStore.cs', 'OfflineModelService.cs']:
    source = (ROOT / 'Services' / filename).read_text(encoding='utf-8')
    assert 'Localization.AppName' not in source, f'Localized storage path in {filename}'
    assert '"Windtranslator",' in source, f'Storage identity changed in {filename}'

print(f'Validated {len(english)} bilingual resources, XAML/code references, format placeholders, app names, and storage identities.')
