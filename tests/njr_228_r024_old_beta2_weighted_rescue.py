"""R024 regression from user-supplied NJR v0.14.0-beta2 and R023 failure.

Source assertions plus deterministic graph oracle. The oracle is NOT RimWorld's
native pathfinder or the actual C# A* execution; real validation requires play.
"""
from pathlib import Path
from heapq import heappush, heappop
import re
P=Path(__file__).resolve().parents[1]
S=P/'RaidersHaveFamilies/Source'
route=(S/'RHFObjectiveRouteV128.cs').read_text()
planner=(S/'RHFRescuePlannerS41.cs').read_text()
role=(S/'RHFRescueRoleJobGiverS41.cs').read_text()
contact=(S/'NJRCombatContactS44.cs').read_text()
command=(S/'RHFRescueCommandV124.cs').read_text()
offense=(S/'RHFOffenseS41.cs').read_text()
workflow=(P/'.github/workflows/build.yml').read_text()
for token in ('WEIGHTED-R024',
              'DoorBreachBaseR024+work',
              'WallBreachBaseR024+work*6',
              'if(building is Building_Door openDoor && openDoor.Open)return 10;',
              'if(IsRescueStructuralBarrier(building))',
              'if(building.def?.passability==Traversability.Impassable)return -1;',
              'plan.RiskFallback=plan.FirstTurretExposureIndex>=0;',
              'MaxRouteExpansionsR024',
              'IntVec3 source=origins[i];',
              'return name.IndexOf("Wall",StringComparison.OrdinalIgnoreCase)>=0'):
    assert token in route,token
assert route.count('PlanBounded(map,origins,captive,fresh,') == 1
assert 'if(forbidTurretExposure' not in route and 'forbidWalls' not in route
assert 'private const int ExposedCost = 300;' in route
assert 'private const int WallBreachBaseR024 = 7000;' in route
assert 'private const int DoorBreachBaseR024 = 350;' in route
assert 'board.Threat==null && !route.Valid' in planner
assert 'op.Phase!=1 || op.BoardS41?.Breacher!=pawn' in role
assert '!RHFObjectiveRouteV128.IsRescueStructuralBarrier(obstacle)' in role
assert 'if(op?.Type==(int)RHFOperationType.Rescue)return null;' in contact
assert 'permitted && !RHFObjectiveRouteV128.IsRescueStructuralBarrier(structure)' in command
assert 'bool raze=(op.Type==(int)RHFOperationType.Vengeance || op.Phase==5)' in offense
assert 'op.Type==(int)RHFOperationType.Rescue && op.Phase!=5' in offense
assert 'tests/njr_228_r024_old_beta2_weighted_rescue.py' in workflow

# Same numbers as Step()/Exposure(), with a simplified count of firing cells.
def cost(kind,hp=0,visible=0):
    assert kind in ('walk','door','wall','generator')
    if kind=='generator':return None
    work=16+2*hp
    return 10+(350+work if kind=='door' else
               7000+6*work if kind=='wall' else 0)+600*visible

def plan(graph):
    queue=[(0,'start',())];best={}
    while queue:
        spent,node,history=heappop(queue)
        if node in best and best[node]<=spent:continue
        best[node]=spent
        if node=='vip':return spent,history
        for dest,kind,hp,exposed in graph.get(node,()):
            price=cost(kind,hp,exposed)
            if price is not None:
                heappush(queue,(spent+price,dest,history+((dest,kind),)))
    return None

# Reported gameplay: three walls directly, versus ONE wall + ONE door.
# The better route is not the shortest geometric straight line.
graph={
 'start':[('a','wall',400,0),('b','walk',0,0)],
 'a':[('c','wall',400,0)], 'c':[('vip','wall',400,0)],
 'b':[('d','wall',400,0)],'d':[('vip','door',300,0)]}
assert [kind for _,kind in plan(graph)[1]]==['walk','wall','door']
# The good route retains priority under brief exposure, without treating a
# turret as an absolute prohibition to use doors.
graph['d']=[('vip','door',300,2)]
assert [kind for _,kind in plan(graph)[1]]==['walk','wall','door']
# Under total turret coverage, a physically real objective route still exists.
graph['a']=[('c','wall',400,4)]
graph['c']=[('vip','wall',400,4)]
graph['b']=[('d','wall',400,4)]
graph['d']=[('vip','door',300,4)]
assert plan(graph) is not None
# Machinery must never be treated as a 'wall' which can be opened to rescue.
graph['b']=[('d','generator',100,0)]
graph['start']=[('a','generator',100,0),('b','walk',0,0)]
assert plan(graph) is None
# A visible defensive gun need not divert assault troops when the planned
# corridor is not exposed (verified as a source-level order-issuing gate).
assert 'if(board.Threat==null && (!route.Valid ||' not in planner
print('PASS R024: one wall + door beats three walls; turret-covered attack proceeds;'
      ' generators are impassable, unrelated turret targets are suppressed.'
      ' Source+graph test; not game runtime.')
