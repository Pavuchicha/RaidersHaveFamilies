"""RHF v0.12.8 static integration + deterministic terrain simulations.
This cannot execute C#/RimWorld's actual pathfinder. The game playtest is mandatory.
"""
from pathlib import Path
from heapq import heappush,heappop
root=Path(__file__).resolve().parents[1]
route=(root/'RaidersHaveFamilies/Source/RHFObjectiveRouteV128.cs').read_text(encoding='utf8')
mission=(root/'RaidersHaveFamilies/Source/RHFRescueCommandV124.cs').read_text(encoding='utf8')
workflow=(root/'.github/workflows/build.yml').read_text(encoding='utf8')
for required in (
    'class RHFObjectiveRouteV128',
    'internal static RHFRoutePlanV128 Resolve(',
    'internal static bool TryObjective(',
    'internal static IntVec3 Waypoint(',
    'BreachWorkCost(building)',
    'step=MissionStepR041(map,cell,holder,out obstacle)',
    'hazard[at]=Exposure(map,cell,threats,holder.PreferVipDoors)',
    'GenSight.LineOfSight(t.Origin,cell,map)',
    'plan.FirstBarrier=obstacle',
    'RHFRouteDiagnosticsV128',
    '31 — Цена маршрута и простреливаемые участки',
    'if(finish<0)',
    'RHFCombatPlannerV125.RequiresBreach(prior.FirstBarrier)',
    'ReplanTicks = 900',
    'while(heap.Count>0 && expanded<MaxRouteExpansionsR024)',
    'if(!Cache.TryGetValue(op',
):
    assert required in route,required
for required in (
    'RHFObjectiveRouteV128.TryObjective(op,map,leader,captive,now,',
    'RHFObjectiveRouteV128.Resolve(op,map,lead,captive,now)',
    'RHFObjectiveRouteV128.Waypoint(route,rescuer,map)',
    'if (canApproach && !route.Valid',
    'RHFObjectiveRouteV128.IsAllowedWaypoint(route,current.targetA.Cell)',
    'if (approaching && now-moved<900 && now-closer<1200 &&',
    'IntVec3 staged = route.Valid ?',
    'state.CurrentBarrier = missionRoute.Valid ? missionRoute.FirstBarrier : null;',
):
    assert required in mission,required
assert mission.index('RHFObjectiveRouteV128.TryObjective') < mission.index('RHFCombatPlannerV125.FirstObjectiveObstacle('), 'legacy fallback must not win over objective route'
assert 'if (covering.Contains(rescuer)) continue;' in mission, 'cover fire must keep its assignment'
assert 'tests/rhf_128_route_control.py' in workflow
assert 'v0.16.0-beta1' in workflow
assert route.count('{')==route.count('}') and route.count('(')==route.count(')')
# Independent reference model: one weighted route through a map, not merely an
# arithmetic comparison of two obstacles. Mimics cardinal movement and the HP
# and exposure penalty of the C# mission planner, not the RimWorld API.
def choose(width,height,start,goal,obstacles,exposure):
    q=[(0,start)]; dist={start:0}; parent={}; closed=0
    while q:
        value,cell=heappop(q)
        if value!=dist[cell]: continue
        closed+=1
        if cell==goal:
            path=[goal]
            while path[-1]!=start:path.append(parent[path[-1]])
            return list(reversed(path)),value,closed
        for dx,dy in [(1,0),(-1,0),(0,1),(0,-1)]:
            n=(cell[0]+dx,cell[1]+dy)
            if not (0<=n[0]<width and 0<=n[1]<height):continue
            hp=obstacles.get(n,0)
            extra=(16+2*hp if hp else 0)+exposure.get(n,0)
            cost=value+10+extra
            if cost<dist.get(n,10**12):
                dist[n]=cost;parent[n]=cell;heappush(q,(cost,n))
    raise AssertionError('expected a route')

# Case one: three thick walls straight ahead, three low-HP door tiles up north.
w,h=45,28
b={(x,y):1000 for x in (17,18,19) for y in range(h)}
for x in (17,18,19): b[(x,3)]=100
path,cost,_=choose(w,h,(5,14),(38,14),b,{})
assert path and [(x,y) for x,y in path if (x,y) in b]==[(17,3),(18,3),(19,3)], 'route must use the corridor doors, not 3 walls'

# Case two: no structures and one fixed turret with a lethal central firing arc.
# A longer ring route must avoid crossing the clearly exposed zone.
exposure={(x,y):1100 for x in range(20,31) for y in range(8,19)}
path,cost,_=choose(52,30,(4,13),(48,13),{},exposure)
assert not any(c in exposure for c in path), 'route must avoid unnecessary turret exposure'
# Case three: modded material strength matters by actual HP and not defName.
weak={(17,y):120 for y in range(1,19)}
weak[(17,8)]=1400
path,cost,_=choose(37,24,(4,8),(30,8),weak,{})
assert (17,8) not in path, 'choose a real weak segment instead of the 1400-HP tile'
print('PASS: control integration, no nearest-wall fallback, 3-door corridor, avoid turret killfield, universal HP behavior')
