"""R019 active combat invariant: no squad/target truncation, but expensive
per-decision pathfinding MUST have finite computational cost. STATIC ONLY.
"""
from pathlib import Path
import re
root=Path(__file__).resolve().parents[1]
src=root/'RaidersHaveFamilies/Source'
read=lambda name:(src/name).read_text(encoding='utf8')
combat=read('RHFCombatPlannerV125.cs')
route=read('RHFObjectiveRouteV128.cs')
ops=read('RHFOperations.cs')
rescue=read('RHFRescuePlannerS41.cs')
pod=read('RHFPodPlannerS41.cs')
off=read('RHFOffenseS41.cs')
entry=read('RHFRescueGroundEntryS41.cs')
beasts=read('NJRForceCompositionS42.cs')
legacy=read('RHFRescueControlV123.cs')
blocks={
 'ops': (ops,['MaxOperationsPerMap','MaxSquad','MaxTargets','MaxOrdersPerDecision','MaxStrikeParties']),
 'combat': (combat,['MaxThreats','MaxPawnScan','MaxStructureScan','MaxTacticalOrders','MaxFlankCandidates','visited++<12000']),
 'route': (route,['MaxCells','expanded++<85000','PlanBounded(map,leader,captive,fresh,44,false)']),
 'rescue': (rescue,['MaxTargets','queries<48','checkedFighters>20','coverCount<8','operations.Count && i<4']),
 'pod': (pod,['MaxSquad','MaxCaptives','MaxGuards','costlyPaths>24','radius<=8']),
 'offense': (off,['MaxObserve','MaxMembers','MaxStructures','MaxCandidateCells','MaxOperations']),
 'ground': (entry,['checkedTurrets>200','checkedDefenders>96','t+=7']),
 'legacy': (legacy,['MaxSquad','MaxTargets','MaxOrdersPerDecision']),
 'beasts': (beasts,['n<48','attempt<24','NativeAnimalsS41.Count<96'])
}
for name,(text,forbidden) in blocks.items():
 for token in forbidden:assert token not in text,(name,token)
expected={
 'combat': (combat,['while(queue.Count>0)','for(int x=0;x<map.Size.x;x++)','exposureCache','remaining>=initialRemaining']),
 'route': (route,['while(heap.Count>0)','new HashSet<Thing>(holder.UnworkableBarriers.Keys));']),
 'rescuer': (rescue,['for(int i=0;i<operations.Count;i++)','int[] radii={2,4,6,9,12,16};']),
 'pod': (pod,['private static bool InFootprint','private static bool ValidGuardCell','private static IntVec3 GuardCell','radius<=12']),
 'entry': (entry,['for(int t=5;t<span-5;t++)','ReachableExterior(map,center,captive)']),
 'animals':(beasts,['while(available>=30f)','available-=selected.combatPower']),
 'ops':(ops,['now - op.CreatedTick > MaxOperationDuration','capable < Math.Max(1, op.Squad.Count / 4)'])
}
for name,(text,required) in expected.items():
 for token in required:assert token in text,(name,token)
# Geometric and ethical constraints stay intentional, not performance cutoffs.
assert 'Math.Min(12,Math.Max(6' not in ops
assert 'int required=Math.Max(6' in ops
assert 'if(++checkedFighters>20)break;' not in rescue
assert 'if (op.RerouteCount >= 3)' not in ops
assert 'MapComponentTick()' in ops
assert 'MaxBatchSize' in read('RaidersHaveFamilies.cs')  # passive work is STILL bounded.
# The CPU budget is a safety limit for an A* invocation, NOT a raid
# size limit, an inability to see distant turrets, or a free demolition.
assert 'MaxRouteExpansionsR024 = 30000' in route
assert 'if(expanded>MaxRouteExpansionsR024)' in route
assert 'IndividualRoutesR019.GetValue(member' not in route  # R021 unified source route
assert 'for(int radius=1;radius<=12;radius++)' in pod
assert 'for(int x=Math.Max(0,origin.x-20)' in rescue
assert 'for(int x=Math.Max(0,origin.x-14)' in rescue
assert 'site.x-10' in pod
assert 'siteRadius=32' in ops
print('PASS R019: full squad semantics retained, bounded expensive search; STATIC ONLY')
