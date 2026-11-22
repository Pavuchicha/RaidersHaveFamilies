"""v0.12.6 source contracts and a standalone weighted-route cost-model example.

This checks structural invariants and intended ordering, NOT a RimWorld gameplay
run. Real pathfinder/CE shooting are confirmed only by the game playtest.
"""
from pathlib import Path
from heapq import heappush, heappop

root = Path(__file__).resolve().parents[1]
planner = (root/'RaidersHaveFamilies/Source/RHFCombatPlannerV125.cs').read_text(encoding='utf8')
command = (root/'RaidersHaveFamilies/Source/RHFRescueCommandV124.cs').read_text(encoding='utf8')
workflow = (root/'.github/workflows/build.yml').read_text(encoding='utf8')

for marker in (
    'private static Thing WeightedObjectiveObstacle(',
    'routeFound=true; // skip destructive vanilla fallback on a clear corridor',
    'clearRoute=weighted==null;',
    'private static int StructuralStep(',
    'BreachWorkCost(block)',
    'obstacle.HitPoints) * 2',
    'parent[next]=at',
    'route',
    'while(queue.Count>0 && visited++<12000)',
    'if(width*height>24000)return null',
    'if(!IsBreachableStructure(barrier))continue;',
    'Thing weighted = WeightedObjectiveObstacle(rescuer,captive,out planned);',
    'if(planned)',
    'FirstBlockingBuilding(out before, rescuer)', # prior fallback retained
    'public readonly Dictionary<int, Thing> CoverTarget',
    'public readonly Dictionary<int, IntVec3> CoverWaypoint',
    'public readonly Dictionary<int, int> LastFlankPerPawn',
    'state.CoverTarget.TryGetValue(id,out Thing assigned)',
    'state.Engaged.Add(pawn);',
    'Mathf.FloorToInt(range>0f?range*0.85f:4f)',
    'AssignedEscort(op,fighter,map)',
    'now-lastFlank>=FlankRefreshTicks',
    'MaxThreats = 24',
    'MaxStructureScan = 240',
):
    assert marker in planner, f'missing weighted navigation / persistent fire: {marker}'
assert 'RHFCombatPlannerV125.FirstObjectiveObstacle(' in command
assert 'if (clearCorridor)' in command
assert 'if (covering.Contains(rescuer)) continue;' in command
assert 'NJR-NotJustRaiders-RimWorld-1.7-v0.14.0-beta1' in workflow
assert 'tests/rhf_126_smart_routes_and_cover.py' in workflow
assert 'HarmonyPatch(typeof(TickManager)' not in planner
assert 'HarmonyPatch(typeof(Pawn_JobTracker)' not in planner

# Cost model exercise: in a room separated from the spawn area by two layers,
# a corridor with two doors should beat a straight three-stone-wall breach.
# It does not execute the game implementation, but checks the numeric intent.
width, height = 20, 12
start, target = (2, 6), (17, 6)
blocks = {}
for x in (8, 9, 10):
    for y in range(height):
        blocks[(x, y)] = 'wall'
for x in (8, 9, 10):
    blocks[(x, 2)] = 'door'

def step_cost(xy):
    kind = blocks.get(xy, '')
    if kind == 'wall':
        return 10 + 16 + 300 * 2
    if kind == 'door':
        return 10 + 16 + 100 * 2
    return 10

def shortest():
    heap = [(0, start)]
    dist = {start: 0}
    prev = {}
    while heap:
        cost, here = heappop(heap)
        if cost != dist[here]:
            continue
        if here == target:
            route = [here]
            while route[-1] != start:
                route.append(prev[route[-1]])
            return list(reversed(route)), cost
        for dx,dy in ((1,0),(-1,0),(0,1),(0,-1)):
            there = (here[0]+dx,here[1]+dy)
            if not (0 <= there[0] < width and 0 <= there[1] < height):
                continue
            next_cost = cost + step_cost(there)
            if next_cost < dist.get(there, float('inf')):
                dist[there] = next_cost
                prev[there] = here
                heappush(heap, (next_cost,there))
    raise AssertionError('test layout has no weighted route')

route, total_cost = shortest()
blocked = [blocks[cell] for cell in route if cell in blocks]
assert blocked == ['door','door','door'], (blocked, total_cost)
assert total_cost < 3 * (10 + 500 + 600), total_cost
print('PASS: v0.12.6 bounded weighted wall/door route and persistent ranged cover contracts')
