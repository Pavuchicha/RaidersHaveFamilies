"""Recorded R055 deadlock, geometric replay and R056 source contracts (not native AI tests)."""
import json, heapq
from pathlib import Path
root=Path(__file__).resolve().parents[1]; src=root/'RaidersHaveFamilies/Source'
a=json.loads((root/'tests/fixtures/r056_runtime.json').read_text())
assert a['revision']=='R055' and a['launch'] and not a['vip_present'] and a['barricades']==2
assert a['wait_ticks']==[43398,57618] and a['boards']['R054 post-launch: reachable=0 isolated=5 recovery=False']==159
assert len(a['last_squad'])==5 and all(p['job']=='Wait' for p in a['last_squad']) and a['exit_events']==0
logs=a['logs']; assert [t for t,m in logs if '[R055][Fortify] completed' in m]==[39978,41508]
assert next(t for t,m in logs if 'Freed=' in m)==40158
# Real recorded map: a route via the already destroyed door exists if the
# next door is treated as a finite work action. Static graph ignores native
# faction restrictions, diagonal corner rules, dynamic reservations, CE.
w,h=a['map_size'];free={(x,z) for z,row in enumerate(a['walk_rows']) for x,c in enumerate(row) if c=='.'}
doors=set(map(tuple,a['doors']));guns=set(map(tuple,a['turrets']));start=(124,150)
def route(open_doors):
    pq=[(0,start)];dist={start:0};prev={}
    while pq:
        cost,c=heapq.heappop(pq)
        if cost!=dist[c]:continue
        x,z=c
        if min(x,z,w-1-x,h-1-z)==0:
            path=[c]
            while path[-1]!=start:path.append(prev[path[-1]])
            return list(reversed(path))
        for n in ((x+1,z),(x-1,z),(x,z+1),(x,z-1)):
            if n in doors:step=1000 if open_doors else -1
            else:step=10 if n in free else -1
            if step<0 or cost+step>=dist.get(n,10**12):continue
            dist[n]=cost+step;prev[n]=c;heapq.heappush(pq,(cost+step,n))
    return []
assert not route(False)
path=route(True);assert path==list(map(tuple,a['geometric_route']))
assert [c for c in path if c in doors]==[(127,145)] and (124,149) in path
assert {(124,146),(127,148)}<=guns
assert all(any((g[0]-x)**2+(g[1]-z)**2<=4 for x,z in path[:path.index((127,145))]) for g in [(124,146),(127,148)])
ac=(src/'NJRRescueAccessR040.cs').read_text();p=(src/'RHFRescuePlannerS41.cs').read_text();j=(src/'RHFRescueRoleJobGiverS41.cs').read_text()
assert 'f.First[id]!=null || NJRRescueExitS41.Eligible(map,ownExit,navigator,at,Danger.Deadly)' in ac
assert 'TryExitCombatR056(op,map,navigator,corridor,first,now)' in ac and 'TurretCoversR037(gun,map,cell)' in ac
assert 'UnsafeLineFrom(op,member,gun,cell)' in ac and 'member.CanReach(cell,PathEndMode.OnCell,Danger.Deadly)' in ac
assert 'state.AllowExposedExitR056' in ac and 'allowExposed && op.Phase==2 && op.PodLaunchCompleted' in ac
assert 'FirstBlockedExitDoor(map,op,navigator)' in ac and 'NJRRescueRallyR033.NoteOnce(op,"[NJR][R056][Exit]' in ac
post=p.split('private static bool PostLaunchCohortsR053',1)[1].split('private static void TryUnescortedExitR029',1)[0]
assert post.count('RHFRoleS41.SuppressTurret')==2 and 'ReachableProgress(' not in post
assert 'pawn.CanReach(turret,PathEndMode.Touch,Danger.Deadly))return Task(JobDefOf.AttackMelee,turret,180)' in j
assert 'op.Phase==2 && op.PodLaunchCompleted ? pawn.Position!=order.Cell' in j
print('PASS R056: recorded 159 deadlock plans, geometric next-door corridor, exit combat/physical access source contracts; no engine execution')
