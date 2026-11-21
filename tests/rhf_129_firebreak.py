"""v0.12.9 contract + deterministic scenario tests; not a RimWorld runtime test."""
from pathlib import Path
from heapq import heappush, heappop
root=Path(__file__).resolve().parents[1]
route=(root/'RaidersHaveFamilies/Source/RHFObjectiveRouteV128.cs').read_text('utf8')
mission=(root/'RaidersHaveFamilies/Source/RHFRescueCommandV124.cs').read_text('utf8')
combat=(root/'RaidersHaveFamilies/Source/RHFCombatPlannerV125.cs').read_text('utf8')
workflow=(root/'.github/workflows/build.yml').read_text('utf8')

for token in (
    'FirstTurretExposureIndex', 'ExposedTurretCells', 'SafeAdvanceLimit(',
    'ExposedToTurret(', 'private const int ExposedCost = 4000;',
    'internal static bool IsAllowedWaypoint(',
    'if(nextStep!=step)break;', 'CrossesKnownFire(',
    'route.FirstTurretExposureIndex-1',
    'Клеток под прямым огнём турелей=',
):
    assert token in route, f'no fire-safe route control: {token}'
for token in (
    'bool needsFireSuppression = route.Valid',
    'now,false,needsFireSuppression',
    'RHFObjectiveRouteV128.IsAllowedWaypoint(route,current.targetA.Cell)',
    'route.FirstBarrierIndex>=route.FirstTurretExposureIndex',
    'state.LastDecision="огневой рубеж: сначала подавить турели',
    '(route.Valid ? 0 : 16)',
    'now-Math.Max(op.LastProgressTick,op.PhaseStartTick)>24000',
):
    assert token in mission, f'unsafe mission override: {token}'
assert mission.index('RHFObjectiveRouteV128.Resolve(op,map,lead,captive,now)') < mission.index('now,false,needsFireSuppression')
assert 'RHFObjectiveRouteV128.IsOnCorridor(route,current.targetA.Cell)' not in mission
for token in (
    'bool extraction, bool blockedByTurrets=false',
    'if(blockedByTurrets && alive>3)',
    'Math.Min(alive-2,(alive*2)/3)',
    'UnsafeCombatApproach(',
    'if(overlapping>=2)return true;',
    'return exposedSamples>4 && steps>12;',
    'if(range>0f && (c-enemy.Position).LengthHorizontalSquared>range*range)',
):
    assert token in combat, f'cover team lost fire discipline: {token}'
assert 'tests/rhf_129_firebreak.py' in workflow
assert 'v0.13.1-VIP-ESCORT-RECOVERY-TEST' in workflow

# Exact-cost reference implementation: a small deterministic grid, for checking
# relative path choice under HP and weapon exposure. Does NOT execute Verse pathfinder.
def route_choice(width,height,src,dst,walls,exposed):
    queue=[(0,src)]
    distance={src:0}; parent={}
    while queue:
        cost,node=heappop(queue)
        if cost!=distance[node]:continue
        if node==dst:
            path=[node]
            while path[-1]!=src:path.append(parent[path[-1]])
            path.reverse();return path,cost
        x,y=node
        for dx,dy in ((0,1),(0,-1),(1,0),(-1,0)):
            nxt=x+dx,y+dy
            if not(0<=nxt[0]<width and 0<=nxt[1]<height):continue
            hp=walls.get(nxt,0)
            price=10+(16+2*hp if hp else 0)+(4000 if nxt in exposed else 0)
            next_cost=cost+price
            if next_cost<distance.get(nxt,10**14):
                distance[nxt]=next_cost;parent[nxt]=node
                heappush(queue,(next_cost,nxt))
    raise AssertionError('route should exist')

# A shooting lane occupies only the central width of the map. A longer northern
# loop exists and the planner must choose it rather than 'sneaking' through it.
exposed={(x,y) for x in range(15,28) for y in range(7,14)}
path,_=route_choice(42,20,(4,10),(37,10),{},exposed)
assert not any(cell in exposed for cell in path), 'crossfire preferred over clear detour'

# When avoidance is physically impossible, the selected path can still be found,
# but the assault wing must stop before the first dangerous cell rather than push.
exposed={(14,y) for y in range(0,9)}
path,_=route_choice(32,9,(3,4),(28,4),{},exposed)
first=next(i for i,cell in enumerate(path) if cell in exposed)
assert first>0 and path[first-1] not in exposed
assert path[first] in exposed and first-1< len(path)-1

# Two doors made of low-HP materials beat three high-HP walls on the short line.
wall={(12,y):1100 for y in range(15)}
wall.update({(13,y):1100 for y in range(15)})
wall.update({(14,y):1100 for y in range(15)})
for x in (12,13,14):wall[(x,1)]=100
path,_=route_choice(32,15,(3,8),(29,8),wall,set())
assert [cell for cell in path if cell in wall] == [(12,1),(13,1),(14,1)]

# A cardinal corner is *not* a long Goto past the bend. The chosen leg must
# stop exactly before the first direction change, so Vanilla cannot cut it.
turn=[(3,3),(4,3),(5,3),(5,4),(5,5),(6,5)]
direction=lambda a,b:(b[0]-a[0],b[1]-a[1])
end=1
for i in range(2,min(len(turn),6)):
    if direction(turn[i-1],turn[i])!=direction(turn[0],turn[1]):break
    end=i
assert turn[end]==(5,3) and turn[end+1]==(5,4)
print('PASS: v0.12.9 fire gates, crossfire-safe squad split, corner-safe waypoints, HP routing')
