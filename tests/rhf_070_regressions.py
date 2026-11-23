"""RHF 0.7+ static source regressions, updated for 0.12.2 dialog and nonduplicated raid letters.
Not a substitute for a C# compile or in-game test.
"""
from pathlib import Path
root=Path(__file__).resolve().parents[1]
src=root/'RaidersHaveFamilies/Source'
source={p.stem:p.read_text('utf8') for p in src.glob('*.cs')}
f=source['RaidersHaveFamilies']
assert 'edge.B.relations.everSeenByPlayer = true' in f, 'offmap kin hidden in normal Social tab'
assert 'b.relations.everSeenByPlayer = true' in f
assert 'RHFProvenanceLink' in f and 'ProvenanceLinks' in f
r=source['RHFRankUI']
assert '"get_ValueString"' in r and 'RHFRankResolver.OfficeStory' in r
assert 'RHFRankResolver.OfficeFor' in r and 'RHFPrisonerInline.Draw' in r
assert 'override string GetStatDrawEntryLabel' not in r
assert 'Widgets.DrawMenuSection(area)' in r
assert 'Widgets.DrawMenuSection(area);\n            Text.Font' not in r.split('public static class RHFPrisonerInline')[1].split('public static void Draw')[1].split('[HarmonyPatch')[0]
h=source['RHFHostageSystems']
for name in ['LastAdmittedMissing','HighDemandRejected','RefusalCount','NegotiatorSocial','NegotiatedPrice','AuditInteractionMutilation','CanAdmitMutilation','AdmitMutilation','HasNewEvidence']:
    assert name in h,name
assert 'IsWceMutilationModeEnabled' in h and 'GetNamedSilentFail("WCE2_Mutilate")' in h
assert 'QueueRetaliation(r, true)' in h
assert 'ReportCondition(r)' in h
dialog=source['RHFDialogue']
assert 'HighDemandRejected' in dialog and 'negotiator' in dialog
assert 'report.Disable(' in dialog
# There is now a single admission path and a separate factual medical report.
assert 'Признать умышленное увечье и ответственность колонии' in dialog
assert 'Передать фактический медицинский отчёт (без признания умысла)' in dialog
assert 'bool viaFamily=r.Importance<5 && family!=null' in dialog
patch=source['RHFPatches']
assert 'typeof(BodyPartRecord)' in patch and 'typeof(Bill)' in patch
ops=source['RHFOperations']
assert 'if (!touchesPrison) continue;' in ops and 'IsBreachableStructure(edifice)' in ops
assert 'rescuer.CanReach(target, PathEndMode.Touch, Danger.Deadly)' in ops
# Normal raid letter comes from RimWorld; RHF must not add a duplicate centred on empty map.
assert 'Find.LetterStack.ReceiveLetter("RHF: "' not in ops
assert 'ЦЕЛЬ ОПЕРАЦИИ' in ops and 'new LookTargets(map.Center, map)' not in ops
assert '01 — Спасение' in source['RHFDebugActions']
assert '09 — Месть' in source['RHFDebugActions']
assert 'RHFPreceptMoodEntry : PreceptComp_SelfTookMemoryThought' in source['RHFPreceptNotes']
assert '0.16.0-beta1' in (root/'.github/workflows/build.yml').read_text()
print('PASS: rank, family, medical, WCE2, single raid notification, and updated dialogs')
