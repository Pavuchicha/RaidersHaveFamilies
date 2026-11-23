"""R021 route-source integration contract + illustrative A* decision model.

The model is intentionally NOT a direct execution of RimWorld/C#; the only
runtime acceptance is GitHub compilation plus a live 1/16 test. The model
makes the intended no-commander, door-priority, limited-wall policy explicit.
"""
from pathlib import Path
from heapq import heappop, heappush
ROOT=Path(__file__).resolve().parents[1]
source=(ROOT/'RaidersHaveFamilies/Source/RHFObjectiveRouteV128.cs').read_text()
planner=(ROOT/'RaidersHaveFamilies/Source/RHFRescuePlannerS41.cs').read_text()
jobgiver=(ROOT/'RaidersHaveFamilies/Source/RHFRescueRoleJobGiverS41.cs').read_text()
assert 'ResolveShared(op,map,' not in planner or 'ResolveShared(' in planner
assert 'RHFObjectiveRouteV128.ResolveShared(' in planner
assert 'RHFObjectiveRouteV128.PlanIndividual(' not in planner
assert 'RescueLeadsR020' not in planner
assert 'IList<IntVec3> origins' in source
assert 'Push(heap,id,Heuristic(source.x,source.z,goal));' in source
assert 'if(parents[cursor]<0)break;' in source
assert 'plan.From=reverse[0]' in source
assert 'JoinCommonCorridor(' in source
assert 'Prior.Leader' not in source
assert 'PlanBounded(map,origins,captive,fresh,' in source and 'bool forbidTurretExposure' not in source
assert 'op.BoardS41?.Breacher==p' in (ROOT/'RaidersHaveFamilies/Source/RHFRescueCommandV124.cs').read_text()
assert 'RHFRoleS41.Breach' in jobgiver

# Pure minimal grid oracle: Dijkstra with multiple roots and physical obstacles.
def corridor(grid, starts, goal, allow_walls):
    h=len(grid);w=len(grid[0]); q=[]; g={}; parent={}
    for start in starts:
        x,y=start
        if not (0<=x<w and 0<=y<h and grid[y][x]!='#'):
            continue
        g[start]=0;parent[start]=None;heappush(q,(0,start))
    while q:
        cost,cur=heappop(q)
        if cost!=g[cur]:continue
        if cur==goal:
            path=[]
            while cur is not None:
                path.append(cur);cur=parent[cur]
            return list(reversed(path))
        x,y=cur
        for nx,ny in ((x+1,y),(x-1,y),(x,y+1),(x,y-1)):
            if not (0<=nx<w and 0<=ny<h):continue
            tile=grid[ny][nx]
            if tile=='#' and not allow_walls:continue
            step=1000 if tile=='#' else 45 if tile=='D' else 1
            new=cost+step
            if new<g.get((nx,ny),10**9):
                g[nx,ny]=new;parent[nx,ny]=cur
                heappush(q,(new,(nx,ny)))
    return None

# Captive behind western wall. The western soldiers can reach the southern door
# without demolishing stone; a farther teammate must not become the authority.
room=[
 '............',
 '...######...',
 '...#....#...',
 '...#....#...',
 '...###D##...',
 '............',
]
starts=[(1,2),(0,4),(10,0)]
route=corridor(room,starts,(6,3),False)
assert route is not None and (6,4) in route and all(room[y][x]!='#' for x,y in route)
route2=corridor(room,starts,(6,3),False)
assert route2==route
# Wall breach remains an explicit fallback for a physically sealed jail.
sealed=[row.replace('D','#') for row in room]
assert corridor(sealed,starts,(6,3),False) is None
assert corridor(sealed,starts,(6,3),True) is not None
print('PASS R021 source contracts and multi-source door-before-wall model (not in-game)')
