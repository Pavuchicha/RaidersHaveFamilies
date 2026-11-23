"""S4.1-A/B structural contracts with deliberate negative mutations.

This test does NOT compile RimWorld C# or validate runtime pathing/CE/escort.
It only ensures that the new rescue dispatch uses one routine JobGiver, that
liberation is event-scoped, and that the blackboard is saveable.
"""
from pathlib import Path
import xml.etree.ElementTree as ET
ROOT=Path(__file__).resolve().parents[1]
SRC=ROOT/'RaidersHaveFamilies'/'Source'
OPS=(SRC/'RHFOperations.cs').read_text('utf-8')
RES=(SRC/'RHFRescueCommandV124.cs').read_text('utf-8')
PAT=(SRC/'RHFPatches.cs').read_text('utf-8')
BOARD=(SRC/'RHFTacticalBoardS41.cs').read_text('utf-8')
PLANNER=(SRC/'RHFRescuePlannerS41.cs').read_text('utf-8')
GIVER=(SRC/'RHFRescueRoleJobGiverS41.cs').read_text('utf-8')
EVENT=(SRC/'RHFAutoLiberationS41.cs').read_text('utf-8')
WF=(ROOT/'.github/workflows/build.yml').read_text('utf-8')
V123=(SRC/'RHFRescueControlV123.cs').read_text('utf-8')
DUTY=ET.parse(ROOT/'RaidersHaveFamilies/Defs/DutyDefs/RHF_MissionHold.xml')


def contract(op,res,patch,board,planner,giver,event,wf):
    assert 'public RHFTacticalBoardS41 BoardS41' in op
    assert 'Scribe_Deep.Look(ref BoardS41, "rhfBlackboardS41")' in op
    assert '36 — S4.1 Blackboard, роли, приказы' in board
    assert 'new RHFTacticalBoardS41()' in op
    for required in ('Scribe_References.Look(ref RescueTarget',
                     'Scribe_References.Look(ref Breacher',
                     'Scribe_References.Look(ref Barrier',
                     'Scribe_Collections.Look(ref Orders, "orders", LookMode.Deep)',
                     'Scribe_Values.Look(ref BestLeadDistance'):
        assert required in board,required
    assert 'if (RHFRescuePlannerS41.Enabled)' in res
    assert 'RHFRescuePlannerS41.Infiltrate(controller,op,now); return;' in res
    assert 'return RHFRescuePlannerS41.Extract(controller,op,now);' in res
    assert 'return RHFRescuePlannerS41.GuardExit(__instance);' in patch
    assert 'public static bool Prefix(Pawn __instance, out RHFExitState __state)' in patch
    assert 'nameof(Pawn_GuestTracker.SetGuestStatus)' in event
    assert 'nameof(GenGuest.PrisonerRelease)' in event
    assert 'TryAdoptReleased(p)' in event or 'TryAdoptReleased(pawn)' in event
    assert 'bool vanillaReleased=captive?.guest?.Released==true;' in planner
    assert 'if(op.Freed.Contains(captive))' in planner and 'StabilizeFreed(op,captive)' in planner
    assert 'if (InTouch(op,captive,map))' in planner
    assert 'board.Breacher=RHFCombatPlannerV125.SelectBreachOperator' in planner
    assert 'board.Assign(soldier,RHFRoleS41.Breach' in planner
    assert 'NJRRescueExitS41.Choose(map,op,navigator)' in planner
    assert 'RHFRescueCommandV124.MissionExit(rescued)' in planner
    assert 'class RHFRescueRoleJobGiverS41 : ThinkNode_JobGiver' in giver
    assert 'RHFCombatPlannerV125.IsCombatEmergencyJob(pawn)' in giver
    assert 'RHFRescueFireDisciplineV136.UnsafeLine' in giver
    # No strategic direct job writes in the normal rescue/extraction loops.
    def between(text,start,end):return text.split(start,1)[1].split(end,1)[0]
    infiltration=between(planner,'internal static void Infiltrate(', 'private static IntVec3 ScreenCell(')
    extraction=planner.split('internal static bool Extract(',1)[1]
    assert 'TryTakeOrderedJob' not in infiltration
    assert 'TryTakeOrderedJob' not in extraction
    assert 'python3 tests/rhf_151_s41_rescue_single_writer.py' in wf

contract(OPS,RES,PAT,BOARD,PLANNER,GIVER,EVENT,WF)
assert 'if (RHFRescuePlannerS41.Enabled)' in V123
assert 'return RHFRescuePlannerS41.GuardExit(__instance);' in V123
prior=[n.attrib.get('Class') for n in DUTY.findall('.//li')]
assert prior.index('RaidersHaveFamilies.RHFRescueRoleJobGiverS41') < \
       prior.index('RaidersHaveFamilies.RHFJobGiverMissionDefenseV136')

# Each independently required P0 property must cause a contract failure if removed.
mutations=[
 (OPS.replace('Scribe_Deep.Look(ref BoardS41, "rhfBlackboardS41")', '/* removed */'),RES,PAT,BOARD,PLANNER,GIVER,EVENT,WF),
 (OPS,RES.replace('RHFRescuePlannerS41.Infiltrate(controller,op,now); return;', 'return;'),PAT,BOARD,PLANNER,GIVER,EVENT,WF),
 (OPS,RES,PAT.replace('return RHFRescuePlannerS41.GuardExit(__instance);','return true;'),BOARD,PLANNER,GIVER,EVENT,WF),
 (OPS,RES,PAT,BOARD,PLANNER.replace('if (InTouch(op,captive,map))','if (PhysicalContact(op,captive,map))'),GIVER,EVENT,WF),
 (OPS,RES,PAT,BOARD,PLANNER,GIVER.replace('RHFCombatPlannerV125.IsCombatEmergencyJob(pawn)','false'),EVENT,WF),
 (OPS,RES,PAT,BOARD,PLANNER,GIVER,EVENT.replace('nameof(GenGuest.PrisonerRelease)','"not_release"'),WF),
]
for i,case in enumerate(mutations,1):
    try:contract(*case)
    except AssertionError:continue
    raise AssertionError(f'Negative mutation {i} incorrectly passed')
try:
    assert 'return RHFRescuePlannerS41.GuardExit(__instance);' in V123.replace(
        'return RHFRescuePlannerS41.GuardExit(__instance);','return true;')
except AssertionError:pass
else:raise AssertionError('Legacy V123 exit guard mutation not caught')
print('PASS: S4.1-A/B rescue single-writer, board persistence, release/exit guard, 7 negative mutations; NOT a gameplay test')
