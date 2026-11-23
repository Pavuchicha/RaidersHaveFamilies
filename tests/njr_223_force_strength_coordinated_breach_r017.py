"""R017 forced-rescue regression. Source contracts and independent math/graph
models only: no RimWorld DLL, no Harmony execution, no in-game verification.
"""
from pathlib import Path
from heapq import heappush, heappop
root=Path(__file__).resolve().parents[1]/'RaidersHaveFamilies'/'Source'
read=lambda n:(root/n).read_text(encoding='utf-8-sig')
force=read('NJRForceStrengthR017.cs')
ops=read('RHFOperations.cs')
planner=read('RHFRescuePlannerS41.cs')
entry=read('RHFRescueGroundEntryS41.cs')
assert 'NJRForceStrengthR017.CompleteGeneratedRoster(mission,wing,pawns);' in entry
route=read('RHFObjectiveRouteV128.cs')
for token in [
    'case RHFStrikeRole.Rescue: return 5;',
    'case RHFStrikeRole.Diversion: return 3;',
    'case RHFStrikeRole.Vengeance: return 5;',
    'maker?.kindDef!=PawnGroupKindDefOf.Combat',
    'kind?.race?.race?.Humanlike!=true',
    'minimum*soldierCost*1.65f+140f',
    'op.ThreatPoints+=extra;',
    'if(op==null || party==null || map==null || party.Drop ||',
    'nativeLord.AddPawn(created);',
    'RHFRescueGroundEntryS41.DeploymentCells(map,spawnCenter)',
    'op.Squad.Add(created);',
    'RHFTactical.Register(op,party,created);',
    'if(party.Points+added>15000f)break;',
    'if(party.Members<RequiredMembers(party))',
    'UNDERSTRENGTH rescue wing',
    'CompleteGeneratedRoster(RHFOperation op,',
    'pawns.Add(soldier);',
    'NJRCenterDropRosterPatchR017',
    'PawnsArrivalModeWorker_CenterDrop.Arrive',
]: assert token in force,token
for token in [
    'NJRForceStrengthR017.CommitMissionBudget(op,party);',
    'NJRForceStrengthR017.CompleteGroundRescue(op,party,map,',
    'rescueReinforcementEntry);',
    'IntVec3 rescueReinforcementEntry = IntVec3.Invalid;',
    'rescueReinforcementEntry=safer;',
    'NJRForceStrengthR017.HumanCoreFloor(party)',
    'float additional=required-effectivePoints;',
    'party.Points+=additional;',
    'points = Mathf.Min(effectivePoints, 15000f)',
    'NJRForceStrengthR017.RequiredMembers(party)',
    'internal static RHFStrikeParty ActiveRaidParty('
]: assert token in ops,token
assert ops.index('NJRForceStrengthR017.CommitMissionBudget(op,party);') < ops.index('party.Attempted = true;')
assert ops.index('NJRForceStrengthR017.CommitMissionBudget(op,party);') < ops.index('NJRForceCompositionS42.Reserve(op,party);')
assert ops.index('NJRForceStrengthR017.CompleteGroundRescue(op,party,map,') > ops.index('finally { activeOperation = null; activeParty = null; }')
assert 'board.Assign(soldier,RHFRoleS41.Breach,\n                                own.FirstBarrier' not in planner
assert 'RHFObjectiveRouteV128.PlanIndividual(' not in planner  # R021: no alternate path authority
assert 'board.Assign(soldier,RHFRoleS41.Breach,obstacle,obstacle.Position,now)' not in planner or 'board.Breacher' in planner
for token in [
    'bool forbidWalls=false',
    'if(forbidWalls && obstacle!=null &&',
    '!(obstacle is Building_Door))continue;',
    'if(building is Building_Door openDoor && openDoor.Open)return 10;',
    'Math.Max(map.Size.x,map.Size.z),true,true)',
    'Math.Max(map.Size.x,map.Size.z),true)',
    'Math.Max(map.Size.x,map.Size.z),false)',
    'RHFCombatPlannerV125.RequiresBreach(prior.FirstBarrier)'
]: assert token in route,token

# Threat points are committed up-front (not injected after calling vanilla).
for cp in (35,60,120,250,700):
    min_members=5
    wanted=min(15000,min_members*cp*1.65+140)
    supply=min(300,wanted*.12)
    loadout=min(wanted*.23,3600) if wanted>=650 else 0
    core=wanted-supply-loadout
    assert core>=min_members*cp,(cp,wanted,core)

# Door-first safe routing, then a necessary wall; a known gun lane disqualifies
# a 'convenient' door until no safer path exists. Also verifies no random
# multi-wall shortcut when a peaceful open doorway exists.
G={
    'start':[('hall','walk'),('bypass','wall'),('covered','door')],
    'hall':[('frontdoor','door')],
    'frontdoor':[('prison','walk')],
    'bypass':[('prison','walk')],
    'covered':[('prison','gun')],
}
def search(no_walls,safe):
    q=[(0,'start',())];seen={}
    costs={'walk':10,'door':120,'wall':2800,'gun':10}
    while q:
        c,pos,trail=heappop(q)
        if pos=='prison': return trail
        if pos in seen and seen[pos]<=c:continue
        seen[pos]=c
        for nxt,kind in G.get(pos,()):
            if no_walls and kind=='wall':continue
            if safe and kind=='gun':continue
            heappush(q,(c+costs[kind],nxt,trail+(kind,)))
    return None
assert search(True,True)==('walk','door','walk')
G['hall']=[]
assert search(True,True) is None
assert search(False,True)==('wall','walk')
G['bypass']=[]
assert search(True,True) is None
assert search(False,False)==('door','gun')
print('PASS R017 source contracts, squad budget arithmetic, coordinated door-first breach model; game runtime UNVERIFIED')
