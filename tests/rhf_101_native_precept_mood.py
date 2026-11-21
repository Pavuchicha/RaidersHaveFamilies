"""RHF v0.10.1: native Precept.GetTip mood grouping, not a tooltip-only workaround.
The API contract was cross-checked against RimWorld's decompiled Precept.GetTip().
This source contract is NOT proof of compilation/gameplay by itself.
"""
from pathlib import Path
import xml.etree.ElementTree as ET
root=Path(__file__).resolve().parents[1]
source=(root/'RaidersHaveFamilies/Source/RHFPreceptNotes.cs').read_text(encoding='utf-8')
host=(root/'RaidersHaveFamilies/Source/RHFHostageSystems.cs').read_text(encoding='utf-8')
memory=ET.parse(root/'RaidersHaveFamilies/Defs/ThoughtDefs/RHF_Thoughts.xml').getroot()
thoughts={p.findtext('defName'): p for p in memory.findall('ThoughtDef')}
assert 'public sealed class RHFPreceptMoodEntry : PreceptComp_SelfTookMemoryThought' in source
assert 'public override IEnumerable<string> GetDescriptions()' in source
assert 'public override void Notify_MemberTookAction' in source
assert 'def.comps.Add(new RHFPreceptMoodEntry' in source
assert 'preceptDef = def' in source and 'thought = thought' in source
assert 'def.comps == null' in source
assert 'existing is RHFPreceptMoodEntry' in source
assert 'def.description = ' in source
assert 'Настроение (RHF):' not in source
assert 'ЕЩЁ НЕ РЕАЛИЗОВАНЫ' not in source
for precept in ['WCE2_Torture_AlwaysRespected', 'WCE2_Torture_RespectedGuilty',
                'Cannibalism_', 'Charity_', 'Mercy_']:
    assert precept in source
for memory_name in ['RHF_PublicCruelty1', 'RHF_PublicCruelty6',
                    'RHF_MercyFreedom', 'RHF_MercyTrade']:
    assert memory_name in source
for level in range(1,7):
    name=f'RHF_PublicCruelty{level}'
    assert name in thoughts
    assert float(thoughts[name].findtext('./stages/li/baseMoodEffect')) == [2,4,8,16,32,48][level-1]
for level in range(1,4):
    for prefix, effects in [('RHF_MercyFreedom',[3,6,9]),('RHF_MercyTrade',[1,2,3])]:
        name=prefix+str(level)
        assert name in thoughts
        assert float(thoughts[name].findtext('./stages/li/baseMoodEffect')) == effects[level-1]
assert 'GivePublicCrueltyThoughts' in host and 'TryGainMemory(memory)' in host
assert 'if (newCruelty <= 0' in host
assert 'r.DisclosedCrueltyScore = r.CrueltyScore' in host
assert 'строк RHF в разделе Настроение' in (root/'RaidersHaveFamilies/Source/RHFDebugActions.cs').read_text(encoding='utf-8')
print('PASS: RHF precept components are native Mood entries backed by real one-off memories')
