"""R024 fire discipline: the complete path is weighted once.
Independent graph oracle + exact source invariants, NOT RimWorld gameplay.
"""
from pathlib import Path
from heapq import heappush, heappop
root=Path(__file__).resolve().parents[1]
route=(root/'RaidersHaveFamilies/Source/RHFObjectiveRouteV128.cs').read_text()
planner=(root/'RaidersHaveFamilies/Source/RHFRescuePlannerS41.cs').read_text()
job=(root/'RaidersHaveFamilies/Source/RHFRescueRoleJobGiverS41.cs').read_text()
workflow=(root/'.github/workflows/build.yml').read_text()
for token in ('FirstTurretExposureIndex','ExposedTurretCells',
    'private const int ExposedCost = 300;',
    'private const int WallBreachBaseR024 = 2400;',
    'private const int DoorBreachBaseR024 = 180;',
    'plan.RiskFallback=plan.FirstTurretExposureIndex>=0;',
    'MaxRouteExpansionsR024', 'IsRescueStructuralBarrier(Thing thing)',
    'CrossesKnownFire(', 'route.FirstTurretExposureIndex-1',
    'Клеток под прямым огнём турелей='):
    assert token in route,token
assert route.count('PlanBounded(map,origins,captive,fresh,')==1
assert 'forbidTurretExposure' not in route
assert 'forbidWalls' not in route
assert 'board.Threat==null && !route.Valid' in planner
assert 'RHFObjectiveRouteV128.IsRescueStructuralBarrier(obstacle)' in job
assert 'tests/rhf_129_firebreak.py' in workflow

def best(width,height,start,goal,obstacles,fire):
    q=[(0,start)];dist={start:0};prev={}
    while q:
        total,at=heappop(q)
        if total!=dist[at]:continue
        if at==goal:
            path=[at]
            while path[-1]!=start:path.append(prev[path[-1]])
            path.reverse();return path,total
        for dx,dz in ((1,0),(-1,0),(0,1),(0,-1)):
            nxt=(at[0]+dx,at[1]+dz)
            if not(0<=nxt[0]<width and 0<=nxt[1]<height):continue
            kind,hp=obstacles.get(nxt,('open',0))
            if kind in ('rock','generator'):continue
            extra=(180+(16+2*hp) if kind=='door' else
                   2400+3*(16+2*hp) if kind=='wall' else 0)
            val=total+10+extra+fire.get(nxt,0)*600
            if val<dist.get(nxt,10**15):
                dist[nxt]=val;prev[nxt]=at;heappush(q,(val,nxt))
    return None
# Three walls across the short route, versus a two-door corridor with
# one short gun lane. The complete safer cheaper route must be chosen.
obstacles={}
for z in range(12):
    for x in (10,11,12):obstacles[x,z]=('wall',400)
for x in (10,11,12):obstacles[x,1]=('door',100)
fire={(x,1):1 for x in range(9,15)}
path,cost=best(26,12,(3,7),(22,7),obstacles,fire)
assert all(not (x in (10,11,12) and z>=2) for x,z in path)
assert all((x,1) in path for x in (10,11,12))
# All routes covered by gun arcs: a valid risky path must still exist.
cover={(x,z):1 for x in range(7) for z in range(5)}
assert best(7,5,(0,2),(6,2),{},cover) is not None
# A generator is not a rescue breach target.
blocked={(3,z):('generator',300) for z in range(5)}
assert best(7,5,(0,2),(6,2),blocked,{}) is None
print('PASS R024 fire/structural path model and source contracts (NOT gameplay)')
