"""R010 source contract: no artificial TPS/roster/route cutoffs in active combat.
This is NOT C# compilation nor RimWorld runtime or proof of correct tactics.
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
 'route': (route,['MaxExpanded','MaxCells','expanded++<85000','PlanBounded(map,leader,captive,fresh,44,false)']),
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
 'route': (route,['while(heap.Count>0)','Math.Max(map.Size.x,map.Size.z),false)']),
 'rescuer': (rescue,['for(int i=0;i<operations.Count;i++)','for(int radius=3;radius<=Math.Max(map.Size.x,map.Size.z);radius++)']),
 'pod': (pod,['private static bool InFootprint','private static bool ValidGuardCell','private static IntVec3 GuardCell','radius<=Math.Max(map.Size.x,map.Size.z)']),
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
# A mutation of the path's heap loop must fail the contract.
def route_unbounded(text):return 'while(heap.Count>0)' in text and 'MaxExpanded' not in text
assert route_unbounded(route)
assert not route_unbounded(route.replace('while(heap.Count>0)','while(heap.Count>0 && expanded++<85000)',1))
print('PASS R010: active combat caps removed and key helper methods preserved; STATIC ONLY')
