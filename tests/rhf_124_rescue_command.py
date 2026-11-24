"""RHF 0.12.4 source contracts. Green checks are NOT gameplay verification."""
from pathlib import Path
import re
root=Path(__file__).resolve().parents[1]
source=(root/'RaidersHaveFamilies/Source/RHFRescueCommandV124.cs').read_text('utf8')
workflow=(root/'.github/workflows/build.yml').read_text('utf8')
old=(root/'RaidersHaveFamilies/Source/RHFRescueControlV123.cs').read_text('utf8')
for marker in (
    'RHFRescueLordJobV124 : LordJob', 'RHFRescueHoldToilV124 : LordToil',
    'graph.StartingToil = hold', 'Scribe_Values.Look(ref MissionKey',
    'Scribe_Values.Look(ref Focus', 'LordMaker.MakeNewLord',
    'RHFRescueDeploymentV124', 'RHFRescueOwnershipV124',
    'RHFRescueInfiltrationV124', 'RHFRescueExtractionV124',
    'RHFRescueExitV124', 'RHFRescueDiagnosticsV124',
    '29 — Диагностика спасения S41 и эвакуации',
    'AuthorizedExits.Contains', 'MissionExit(captive)',
    'MissionExit(escort)', 'MissionExit(rescuer)',
    'op.EscapedWithEscort++',
    'new RHFEscortLink(captive,chosen)',
    'if (link?.Escort == p && link.Captive != captive',
    'state.LastDecision',
    'LastCloserAt', 'now-closer<1200',
    'if (captive.MapHeld == map)',
    'may search every physical map obstacle on demand',
    'PathEndMode.Touch, Danger.Deadly',
    'Target(op, map)', 'AccessObstacle(',
    'RHFCombatRole.Escort',
):
    assert marker in source, marker
assert 'EnsureEscortMethod' not in source, 'legacy escort routine detaches a soldier from its rescue Lord'
assert 'public override void UpdateAllDuties' in source
assert 'RHF_HoldForMissionOrders' in source
assert 'new PawnDuty(missionHold, pawn.Position)' in source
assert 'LordJob is RHFRescueLordJobV124' in source
assert 'RHFRescueControlV123' in source, '0.12.3 controller integration missing'
assert 'private const int MaxOrders = 14' not in source
assert 'private const int Step = 90' in source
assert 'private const int MaxSquad = 96' not in source
assert 'private const int MaxTargets = 24' not in source
assert 'now - last < 180' in source, 'job restart throttling'
assert 'at most 80 path probes per 600 ticks' not in source
assert not re.search(r'HarmonyPatch\(typeof\(TickManager\)',source)
assert not re.search(r'HarmonyPatch\(typeof\(Pawn_JobTracker\)',source)
assert 'if(op.PodLaunchCompleted && pawn==op.Vip)return true;' in source
assert 'RHFRescueMissionDiagnosticsV123' in old, 'v0.12.3 diagnostics retained'
assert 'tests/rhf_124_rescue_command.py' in workflow
assert 'NJR-NotJustRaiders-RimWorld-1.7-v0.16.0-beta1' in workflow
assert 'NJR_NotJustRaiders_v0.16.0-beta1_RimWorld_1.7_INSTALL.zip' in workflow
assert source.count('{') == source.count('}')
assert source.count('(') == source.count(')')
print('PASS: v0.12.4 persistent Lord, group extraction, safe escort, job stability, bounded access scans & CI packaging')
