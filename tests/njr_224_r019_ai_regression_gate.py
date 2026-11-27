"""R019 guardrails for specific reported ground/air rescue regressions.
STATIC source checks + small deterministic policy models only: no .NET compile,
no real RimWorld pathfinder/JobDriver, and no TPS benchmark.
"""
from pathlib import Path
S=Path(__file__).resolve().parents[1]/'RaidersHaveFamilies'/'Source'
def file(n): return (S/n).read_text(encoding='utf-8-sig')
contact=file('NJRCombatContactS44.cs')
planner=file('RHFRescuePlannerS41.cs')
route=file('RHFObjectiveRouteV128.cs')
job=file('RHFRescueRoleJobGiverS41.cs')
pod=file('RHFPodPlannerS41.cs')
ops=file('RHFOperations.cs')
roster=file('NJRForceStrengthR017.cs')
assert 'MaxRouteExpansionsR024 = 3000' in route
assert 'plan.SearchPending=heap.Count>0;' in route
assert 'IndividualRoutesR019.GetValue(member' not in route  # obsolete per-unit A* removed R021
assert route.count('PlanBounded(map,origins,captive,fresh,') == 1
assert 'for(int x=0;x<map.Size.x;x++)for(int z=0;z<map.Size.z;z++)' not in contact
assert 'if(cells.Count>20)' in contact
assert 'if(op.Type==(int)RHFOperationType.Rescue &&' in contact
assert 'op.BoardS41?.Barrier!=best' in contact
assert 'op.BoardS41?.Breacher!=soldier' in contact
assert 'if(!route.Valid)obstacle=null;' in planner
assert 'FirstObjectiveObstacle(' not in planner
assert 'for(int x=Math.Max(0,origin.x-20)' in planner
assert 'for(int x=Math.Max(0,origin.x-14)' in planner
assert 'for(int radius=1;radius<=12;radius++)' in pod
assert 'site.x-10' in pod
assert 'const int siteRadius=32;' in ops
assert 'roof!=null && roof.isThickRoof' in ops
assert 'QualifiedPodEngineer(Pawn pawn,Faction faction)' in roster
assert 'if(requiresEngineer && !hasEngineer)' in roster
assert 'pawns.Add(candidate);' in roster
assert 'if(kind.combatPower>unused)' in roster
assert 'party.Points+=' not in roster and 'op.ThreatPoints+=' not in roster
assert 'if(separation>9)' in job
assert 'if(gap>16)return Task(JobDefOf.Wait,pawn.Position,30);' not in job
assert 'return Task(JobDefOf.Goto,order.Cell,360);' in job
assert 'return Task(JobDefOf.Goto,escort,120);' not in job

# Independent convoy policy oracle: a mobile VIP advances even if its escort is delayed;
# they move toward the same frozen exit, never toward one another's stale tile.
def vip_action(phase,gap,remaining,reachable):
    if phase!=2 or remaining<=10 or not reachable:return 'wait'
    return 'exit'
assert vip_action(2,4,400,True)=='exit'
assert vip_action(2,25,400,True)=='exit'
assert vip_action(2,4,5,True)=='wait'
assert vip_action(1,4,400,True)=='wait'
# Single demolition permission oracle.
def may_breach(rescue,phase,same_wall,assigned_breacher):
    return not rescue or (phase==1 and same_wall and assigned_breacher)
assert not may_breach(True,1,False,True)
assert not may_breach(True,2,True,True)
assert not may_breach(True,1,True,False)
assert may_breach(True,1,True,True)
assert may_breach(False,1,False,False)
print('PASS R019 source gate and deterministic policy oracles; NOT a live-game pass')
