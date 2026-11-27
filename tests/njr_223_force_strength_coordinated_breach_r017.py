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
    'if(cost>unused)break;',
    'unused=Mathf.Max(0f,unused-cost);',
    'if(op==null || party==null || map==null || party.Drop ||',
    'nativeLord.AddPawn(created);',
    'RHFRescueGroundEntryS41.DeploymentCells(map,spawnCenter)',
    'op.Squad.Add(created);',
    'RHFTactical.Register(op,party,created);',
    'if(cost>spare)break;',
    'if(party.Members<RequiredMembers(party))',
    'UNDERSTRENGTH rescue wing',
    'CompleteGeneratedRoster(RHFOperation op,',
    'pawns.Add(soldier);',
    'NJRCenterDropRosterPatchR017',
    'PawnsArrivalModeWorker_CenterDrop.Arrive',
]: assert token in force,token
for token in [
    'NJRForceStrengthR017.CompleteGroundRescue(op,party,map,',
    'rescueReinforcementEntry);',
    'IntVec3 rescueReinforcementEntry = IntVec3.Invalid;',
    'rescueReinforcementEntry=safer;',
    'NJRForceStrengthR017.HumanCoreFloor(party)',
    'effectivePoints>=required',
    'points = effectivePoints',
    'NJRForceStrengthR017.RequiredMembers(party)',
    'internal static RHFStrikeParty ActiveRaidParty('
]: assert token in ops,token
assert 'CommitMissionBudget' not in ops
assert 'party.Points+=' not in force and 'op.ThreatPoints+=' not in force
assert ops.index('NJRForceStrengthR017.CompleteGroundRescue(op,party,map,') > ops.index('finally { activeOperation = null; activeParty = null; }')
assert 'board.Assign(soldier,RHFRoleS41.Breach,\n                                own.FirstBarrier' not in planner
assert 'RHFObjectiveRouteV128.PlanIndividual(' not in planner  # R021: no alternate path authority
assert 'board.Assign(soldier,RHFRoleS41.Breach,obstacle,obstacle.Position,now)' not in planner or 'board.Breacher' in planner
for token in [
    'internal static bool IsRescueStructuralBarrier(Thing thing)',
    'DoorBreachBaseR024+work',
    'WallBreachBaseR024+work*3',
    'if(building is Building_Door openDoor && openDoor.Open)return 10;',
    'PlanBounded(map,origins,captive,fresh,',
    'RHFCombatPlannerV125.RequiresBreach(prior.FirstBarrier)'
]: assert token in route,token
assert 'forbidTurretExposure' not in route
assert 'bool forbidWalls=false' not in route

# Formation target must not create extra points when a small colony
# cannot fund five soldiers; spend only the actual residual budget.
for cp in (35,60,120,250,700):
    for budget in (100,300,1200,3000,18000):
        supply=min(300,budget*.12)
        loadout=min(budget*.23,3600,max(0,budget-supply-350)) if budget>=650 else 0
        core=budget-supply-loadout
        members=min(5,int(core//cp))
        assert supply+loadout+members*cp<=budget+1e-6
        if core<5*cp: assert members<5

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
