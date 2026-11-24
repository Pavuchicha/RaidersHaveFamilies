"""R028: regressions seen in Player.log 24 Nov. Model, not RimWorld runtime."""
from pathlib import Path
from heapq import heappush, heappop
from random import Random
import re
P=Path(__file__).resolve().parents[1]; S=P/'RaidersHaveFamilies/Source'
r=(S/'RHFObjectiveRouteV128.cs').read_text(); j=(S/'RHFRescueRoleJobGiverS41.cs').read_text(); o=(S/'RHFOperations.cs').read_text(); c=(S/'RHFRescueCommandV124.cs').read_text(); p=(S/'RHFRescuePlannerS41.cs').read_text()
assert 'Searches.TryGetValue(holder,out search)' in r and 'Searches.Add(holder,search)' in r
assert 'while(heap.Count>0 && expanded<MaxRouteExpansionsR024)' in r
assert 'plan.SearchPending=heap.Count>0;' in r and '!prior.SearchPending' in r
assert 'if(!plan.SearchPending)Searches.Remove(holder);' in r
assert 'search.Roots.Contains(cell)' in r and 'step+=hazard[at]' in r
assert 'if(cell==search.Goal)break;' in r and 'plan.From=reverse[0]' in r
assert 'R028 route invalidated by changed terrain; retry' in r
assert 'if(owner!=op)return;' in c and 'if(captive.GetLord()!=null)return;' in c
free=o.split('private void FreeTarget(',1)[1].split('private Thing FindBarrier',1)[0]
assert free.index('op.Freed.Add(p)')<free.index('p.guest.SetGuestStatus(null)')
assert 'FreedOwnerR028(p)' in free
assert 'RHFRescueCommandV124.Key(op)!=lordKey' in p
assert 'TryAdoptReleased(op.Targets[i],op)' in p
assert 'op.Freed.RemoveAt(i)' in c
assert 'pulse.LastTick==now' in j and 'job.targetA.Thing!=null' in j
assert 'return ApproachPawn(pawn,order.Objective as Pawn,90);' in j
assert 'return ApproachPawn(pawn,escorted,180);' in j
assert 'Task(JobDefOf.Goto,order.Objective,' not in j
assert 'Task(JobDefOf.Goto,escorted,' not in j
assert 'pawn.CanReach(cell,PathEndMode.OnCell,Danger.Deadly)' in j
# Reverse search oracle using the actual source tariffs; preserve frontier and compare
# with independent forward Dijkstra on random blocked grids and an actual map-sized case.
W=int(re.search(r'WallBreachBaseR024 = (\d+);',r)[1]);D=int(re.search(r'DoorBreachBaseR024 = (\d+);',r)[1]);SLICE=int(re.search(r'MaxRouteExpansionsR024 = (\d+);',r)[1])
def neighbors(at,w,h):
 x,y=at
 for a,b in ((x+1,y),(x-1,y),(x,y+1),(x,y-1)):
  if 0<=a<w and 0<=b<h:yield a,b
def reverse(w,h,roots,goal,price,budget):
 def heuristic(a):return 10*min(abs(a[0]-x)+abs(a[1]-y) for x,y in roots)
 g={goal:0};par={};q=[(heuristic(goal),goal)];slices=total=0
 while q:
  slices+=1;expanded=0
  while q and expanded<budget:
   f,a=heappop(q)
   if f!=g[a]+heuristic(a):continue
   total+=1;expanded+=1
   if a in roots:
    path=[a]
    while path[-1]!=goal:path.append(par[path[-1]])
    assert sum(price(v) for v in path[1:])==g[a]
    return g[a],path,slices,total
   step=price(a)
   if step is None:continue
   for b in neighbors(a,w,h):
    if price(b) is None:continue
    cost=g[a]+step
    if cost<g.get(b,10**15):g[b]=cost;par[b]=a;heappush(q,(cost+heuristic(b),b))
 return None
def forward(w,h,roots,goal,price):
 g={a:0 for a in roots};q=[(0,a) for a in roots]
 import heapq;heapq.heapify(q)
 while q:
  d,a=heappop(q)
  if d!=g[a]:continue
  if a==goal:return d
  for b in neighbors(a,w,h):
   v=price(b)
   if v is not None and d+v<g.get(b,10**15):g[b]=d+v;heappush(q,(d+v,b))
 return None
rng=Random(28)
for trial in range(40):
 w,h=20,19;roots=[(0,0),(19,18)];goal=(10,9)
 costs={(x,y):rng.choice([10,10,10,None,10+D+416,10+W+3*816,610]) for x in range(w) for y in range(h)}
 for a in roots+[goal]:costs[a]=10
 a=reverse(w,h,roots,goal,costs.get,7);b=forward(w,h,roots,goal,costs.get)
 assert (a[0] if a else None)==b,(trial,a,b)
# Scenario sized and positioned like the report (synthetic geometry, not save reconstruction).
roots=[(86,6),(83,243),(243,182),(6,156)];goal=(118,152)
wall={(x,y) for x in range(110,127) for y in range(145,160) if x in (110,126) or y in (145,159)};door=(118,159);wall.remove(door)
water={(x,y) for x in range(60,105) for y in range(60,126)}
def mapprice(a):
 if a in water:return None
 if a==door:return 10+D+416
 if a in wall:return 10+W+3*816
 return 10
for origin in [[v] for v in roots]+[roots]:
 res=reverse(250,250,origin,goal,mapprice,SLICE)
 assert res is not None and door in res[1] and not any(a in wall for a in res[1]); assert res[0]==forward(250,250,origin,goal,mapprice)
# A deliberately expensive maze forces continuation across slices, not restarting.
a=reverse(80,80,[(0,0)],(79,79),lambda v:10,13)
assert a and a[2]>1 and a[3]>13
print('PASS R028: 40 reverse-vs-forward oracle grids, 250x250 synthetic case, resumable search, unique prisoner ownership and Goto job guard. NOT gameplay.')
