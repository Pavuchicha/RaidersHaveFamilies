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
assert [e.text for e in meta.findall('./supportedVersions/li')] == ['1.7'], 'Supported game version must be exactly 1.7'
project = ET.parse(mod / 'Source/RaidersHaveFamilies.csproj').getroot()
assert project.findtext('./PropertyGroup/TargetFramework') == 'net10.0'
packages = {p.attrib['Include']: p.attrib['Version'] for p in project.findall('.//PackageReference')}
assert packages['Krafs.Rimworld.Ref'] == '1.7.5014-unstable'
assert 'Microsoft.NETFramework.ReferenceAssemblies.net472' not in packages
workflow = (root / '.github/workflows/build.yml').read_text(encoding='utf-8')
assert '10.0.x' in workflow and '1.7' in workflow
assert 'RimWorld 1.6' not in workflow and 'RimWorld-1.6' not in workflow
print('Static validation passed; next step is C# compilation, not a runtime game test.')
