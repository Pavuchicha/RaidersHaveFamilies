from pathlib import Path
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
mod = root / 'RaidersHaveFamilies'
for relative in [
    'About/About.xml',
    'Languages/English/Keyed/RHF.xml',
    'Languages/Russian/Keyed/RHF.xml',
    'Source/RaidersHaveFamilies.csproj',
]:
    ET.parse(mod / relative)

code = (mod / 'Source/RaidersHaveFamilies.cs').read_text(encoding='utf-8')
for expected in ['RHFMapComponent', 'RHFGameLedger', 'CanCreate(', 'NewWorldPawn(', 'PassToWorld(', 'AddDirectRelation(']:
    assert expected in code, f'Missing required source feature: {expected}'

meta = ET.parse(mod / 'About/About.xml').getroot()
assert '1.6' in [e.text for e in meta.findall('./supportedVersions/li')]
print('Static validation passed; next step is C# compilation, not a runtime game test.')
