"""Static verification only; actual DLL compilation is done by GitHub Actions."""
from pathlib import Path
import xml.etree.ElementTree as ET
ROOT = Path(__file__).resolve().parents[1]
MOD = ROOT/'RaidersHaveFamilies'
xmls = list(MOD.rglob('*.xml'))
assert len(xmls) >= 4
for f in xmls:
    ET.parse(f)
meta = ET.parse(MOD/'About/About.xml').getroot()
assert [x.text for x in meta.findall('./supportedVersions/li')] == ['1.7']
assert meta.findtext('packageId') == 'pavuk.raidshavefamilies'
project = ET.parse(MOD/'Source/RaidersHaveFamilies.csproj').getroot()
assert project.findtext('./PropertyGroup/TargetFramework') == 'net10.0'
packages = {x.attrib['Include']: x.attrib['Version'] for x in project.findall('.//PackageReference')}
assert packages['Krafs.Rimworld.Ref'].startswith('1.7.')
assert packages['Lib.Harmony'].startswith('3.')
files = {p.name:p.read_text(encoding='utf-8') for p in (MOD/'Source').glob('*.cs')}
assert {'RaidersHaveFamilies.cs','RHFPatches.cs','RHFHostageSystems.cs','RHFOperations.cs','RHFRanks.cs','RHFWCERegistry.cs'} <= set(files)
patch = files['RHFPatches.cs']
for needle in ['Pawn.SpawnSetup', 'Pawn.ExitMap', 'Pawn.Kill', 'SetGuestStatus',
    'FactionDialogMaker', 'FactionDialogFor', 'CreateClassProcessor', 'HarmonyPostfix',
    'RHFWarCrimesIntegration', 'RHFWCERegistry.SeverityOf', 'ApplyOnPawn']:
    assert needle in patch, f'Missing {needle}'
diplomacy = files['RHFHostageSystems.cs']
for needle in ['SilverPrice(', 'OnPrisonerExit(', 'UpdateOffers(', 'QueueRetaliation(',
    'RHFPoliticalReputation.Change(', 'RecordWceOperation(', 'CrueltyScore', 'InitialIdeology',
    'ImprovedProsthetics(', 'RankOf(', 'HostageEligible(']:
    assert needle in diplomacy, f'Missing {needle}'
assert len(list((MOD/'Defs/ThoughtDefs').glob('*.xml'))) > 0
assert '0.13.3' in (ROOT/'.github/workflows/build.yml').read_text()
assert 'dotnet build' in (ROOT/'.github/workflows/build.yml').read_text()
print('PASS: mod metadata, XML, RimWorld 1.7, Harmony 3, source interfaces, CI contracts.')
