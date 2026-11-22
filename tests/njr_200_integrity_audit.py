"""NJR v0.14 beta release-wide structural audit. Not a C# or gameplay test.

The audit deliberately covers complete source/defs/i18n/scenario manifest and
compatibility invariants, not merely the presence of a single implementation.
"""
from pathlib import Path
from collections import Counter
import re
import xml.etree.ElementTree as ET
R=Path(__file__).resolve().parents[1]
MOD=R/'RaidersHaveFamilies'
S=MOD/'Source'
W=(R/'.github/workflows/build.yml').read_text(encoding='utf-8')
meta=ET.parse(MOD/'About/About.xml').getroot()
assert meta.findtext('name')=='NJR — Not Just Raiders'
assert meta.findtext('packageId')=='pavuk.raidshavefamilies', 'Preserve savegame compatibility'
assert meta.findtext('author')=='Pavuchicha'
assert [x.text for x in meta.findall('./supportedVersions/li')]==['1.7']
assert 'Harmony' in ET.tostring(meta,encoding='unicode')
proj=ET.parse(S/'RaidersHaveFamilies.csproj').getroot()
assert proj.findtext('./PropertyGroup/TargetFramework')=='net10.0'
assert proj.findtext('./PropertyGroup/AssemblyName')=='RaidersHaveFamilies', 'Savegame type references must remain stable'
assert (S/'RaidersHaveFamilies.cs').exists()
assert 'namespace RaidersHaveFamilies' in (S/'RaidersHaveFamilies.cs').read_text(encoding='utf-8')
assert 'NJR — Not Just Raiders' in (S/'RaidersHaveFamilies.cs').read_text(encoding='utf-8')
assert 'NJR - ТЕСТЫ | РЕЙДЫ' in (S/'RHFDebugActions.cs').read_text(encoding='utf-8')
# All XML, including empty retirement placeholders, must parse.
xmls=sorted(MOD.rglob('*.xml'))
assert len(xmls)>=12, [str(f) for f in xmls]
for xml in xmls: ET.parse(xml)
# Every locally declared DefName is unique; otherwise mod loading can silently override.
all_defs=[]
for path in sorted((MOD/'Defs').rglob('*.xml')):
    for element in ET.parse(path).getroot().iter():
        if element.tag=='defName' and element.text:
            all_defs.append((element.text,path.as_posix()))
counts=Counter(x for x,p in all_defs)
assert all(n==1 for n in counts.values()), [k for k,v in counts.items() if v>1]
for legacy in ['Defs/ThingDefs/RHF_BreachKit.xml','Defs/JobDefs/RHF_BreachChargeS41.xml']:
    root=ET.parse(MOD/legacy).getroot()
    assert not list(root), 'Retired free-damage breaching tool must not be reintroduced'
assert 'namespace RaidersHaveFamilies {}' in (S/'JobDriver_RHFPlantBreachChargeS41.cs').read_text(encoding='utf-8')
# Every displayed locale key exists in English and Russian, without duplicates.
keysets=[]
for locale in ('English','Russian'):
    p=MOD/'Languages'/locale/'Keyed'/'RHF.xml'
    root=ET.parse(p).getroot()
    keys=[ch.tag for ch in root]
    assert len(keys)==len(set(keys)), (locale,'duplicate keys')
    assert len(keys)>8
    keysets.append(set(keys))
assert keysets[0]==keysets[1], sorted(keysets[0]^keysets[1])
# Scenario debug actions: every enum option exactly once, covering all 18 routes.
debug=(S/'RHFDebugActions.cs').read_text(encoding='utf-8')
em=re.search(r'internal enum RHFDebugScenario\s*\{([^}]+)\}',debug,re.S)
assert em, 'Missing 18-case dispatcher'
options=[x.strip() for x in em.group(1).split(',') if x.strip()]
assert len(options)==len(set(options))==18, options
entries=re.findall(r'\[DebugAction\(Category,\s*"(\d\d) —[^\n]+',debug)
assert len(entries)==18 and sorted(map(int,entries))==list(range(1,19)), entries
calls=re.findall(r'\bStart\(RHFDebugScenario\.(\w+)\);',debug)
assert Counter(calls)==Counter(options), 'One-to-one map: each debug scenario must be callable once'
# Audit compiled sources and CI coverage.
sources=list(S.glob('*.cs'))
assert len(sources)>=38, len(sources)
assert 'RHFRescueGroundEntryS41.Choose' in (S/'RHFOperations.cs').read_text(encoding='utf-8')
assert 'parms.spawnCenter=IntVec3.Invalid;' in (S/'RHFOperations.cs').read_text(encoding='utf-8')
assert 'NativeFortificationWorkS41' in (S/'RHFVipEvacS311.cs').read_text(encoding='utf-8')
assert 'RHFPodSupplyS41' in (S/'RHFPodPlannerS41.cs').read_text(encoding='utf-8')
assert 'GetTileNeighborCount' in (S/'RHFEnemyPodLaunchS41.cs').read_text(encoding='utf-8')
assert 'LastCohesionTick;' not in (S/'RHFRescueCommandV124.cs').read_text(encoding='utf-8'), 'Dead field produced CS0649'
assert 'NJR - Not Just Raiders v0.14.0-beta2' in W
assert 'NJR_NotJustRaiders_v0.14.0-beta2_RimWorld_1.7_INSTALL.zip' in W
assert 'test -s RaidersHaveFamilies/Assemblies/RaidersHaveFamilies.dll' in W
ci_tests=re.findall(r'^\s*python3\s+(tests/[\w.-]+\.py)\s*$',W,re.M)
assert len(ci_tests)==len(set(ci_tests)) >= 58
assert 'tests/njr_200_integrity_audit.py' in ci_tests
assert 'tests/njr_201_release_zip.py' in W
assert all((R/p).is_file() for p in ci_tests), [p for p in ci_tests if not (R/p).exists()]
# Keep existing historical saves from losing code classes or Def identities.
assert (S/'RHFHostageSystems.cs').exists()
assert 'class RHFGameLedger' in (S/'RaidersHaveFamilies.cs').read_text(encoding='utf-8')
print(f'PASS NJR: {len(sources)} C# sources, {len(xmls)} XML, {len(all_defs)} unique defNames, '
      f'{len(keysets[0])} keyed strings/localization, {len(options)} raid scenarios, '
      f'{len(ci_tests)} CI static checks and save-compatibility invariants (STATIC ONLY).')
