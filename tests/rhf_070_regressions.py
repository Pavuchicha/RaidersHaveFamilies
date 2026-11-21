"""RHF 0.7 static source regression checks. Not a substitute for a C# compile/game tests."""
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
assert 'Widgets.DrawMenuSection(area)' in r  # character tab still has full details
assert 'Widgets.DrawMenuSection(area);\n            Text.Font' not in r.split('public static class RHFPrisonerInline')[1].split('public static void Draw')[1].split('[HarmonyPatch')[0]
h=source['RHFHostageSystems']
for name in ['LastAdmittedMissing','HighDemandRejected','RefusalCount','NegotiatorSocial','NegotiatedPrice','AuditInteractionMutilation','CanAdmitMutilation','AdmitMutilation','HasNewEvidence']:
 assert name in h,name
assert 'IsWceMutilationModeEnabled' in h and 'GetNamedSilentFail("WCE2_Mutilate")' in h
assert 'QueueRetaliation(r, true)' in h
assert 'ReportCondition(r)' in h
assert 'HighDemandRejected' in source['RHFDialogue'] and 'negotiator' in source['RHFDialogue']
assert 'report.Disable(' in source['RHFDialogue']
assert 'Признать: мы сознательно искалечили пленника' in source['RHFDialogue']
patch=source['RHFPatches']
assert 'typeof(BodyPartRecord)' in patch and 'typeof(Bill)' in patch
ops=source['RHFOperations']
assert '(barrier.Position-captive.Position).LengthHorizontalSquared > 49' in ops
assert 'rescuer.CanReach(target, PathEndMode.Touch, Danger.Deadly)' in ops
assert 'RHF: спасательная операция' not in ops or 'LetterDefOf.ThreatBig' in ops
assert 'ЦЕЛЬ ОПЕРАЦИИ' in ops and 'new LookTargets(map.Center, map)' in ops
assert '01 — Спасение' in source['RHFDebugActions']
assert '09 — Месть' in source['RHFDebugActions']
assert 'RHFPreceptMoodEntry : PreceptComp_SelfTookMemoryThought' in source['RHFPreceptNotes']
assert '0.12.1' in (root/'.github/workflows/build.yml').read_text()
print('PASS: 0.7 regression guards for social visibility, rank, diplomacy, WCE2, raid reports and localized dev menu')
