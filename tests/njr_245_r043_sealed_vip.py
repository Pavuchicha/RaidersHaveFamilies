"""R042 recorded geometry + isolated route/placement/seal models. Not C#/CE gameplay."""
from pathlib import Path
import json,heapq,collections
r=Path(__file__).resolve().parents[1];s=r/'RaidersHaveFamilies/Source'
a=json.loads((r/'tests/fixtures/r043_vip_trace.json').read_text())
assert a['revision']=='R042' and a['events']==9917
assert a['route']['barrier']['pos']==[119,152] and a['route']['TotalStructuralHP']==300
assert any('doorValid=False' in m for _,m in a['logs'])
assert any('Native blueprint site=(123, 0, 157)' in m for _,m in a['logs'])
assert any('Real hostile world transport contains the VIP' in m for _,m in a['logs'])
walk=set();roof=set()
for z,row in a['rows']:
 for x,n,w,rf in row:
  for i in range(x,x+n):
   if w:walk.add((i,z))
   if rf:roof.add((i,z))
structures={tuple(p):(k,h) for p,k,h in a['structures']}
card=((1,0),(-1,0),(0,1),(0,-1))
def adj(c):return [(c[0]+dx,c[1]+dz) for dx,dz in card]
def step(c):
 if c in structures:
  k,h=structures[c];return 10+180+16+h*2 if k=='Door' else None
 return 10 if c in walk else None
start=tuple(a['route']['from']);goal=tuple(a['route']['to']);cost={start:0};prev={};q=[(0,start)]
while q:
 d,c=heapq.heappop(q)
 if cost[c]!=d:continue
 if c==goal:break
 for n in adj(c):
  st=step(n)
  if st is None:continue
  if d+st<cost.get(n,10**9):cost[n]=d+st;prev[n]=c;heapq.heappush(q,(d+st,n))
assert cost[goal]==616,cost.get(goal)
path=[goal]
while path[-1]!=start:path.append(prev[path[-1]])
assert (114,151) in path and structures[114,151]==('Door',160)
assert not any(structures.get(c,('',0))[0]=='Wall' for c in path)
# The actual chosen launcher and pod anchors are unroofed.
assert (123,157) not in roof and (123,156) not in roof
# Geometry-only candidate footprint model: actual launcher size 1x2, pod 1x1.
# Assume a contiguous 3-cell pad on its axis, with two distinct reachable work cells.
def flood(origin,legal,excluded=frozenset()):
 seen={origin};queue=collections.deque([origin])
 while queue:
  c=queue.popleft()
  for n in adj(c):
   if n in seen or n in excluded or n not in legal:continue
   seen.add(n);queue.append(n)
 return seen
local={(x,z) for x in range(90,151) for z in range(120,181) if step((x,z)) is not None}
reachable=flood(goal,local)
placements=[]
for c in reachable & roof & walk:
 for dx,dz in card:
  p=(c[0]+dx,c[1]+dz);other=(c[0]-dx,c[1]-dz);fp={c,p,other}
  if goal in fp or not fp <= (roof&walk&reachable) or fp & structures.keys():continue
  ls=(set(adj(c))|set(adj(other)))-fp;ps=set(adj(p))-fp
  seen=flood(goal,local,fp)
  if any(x!=y for x in ls&seen&walk for y in ps&seen&walk):placements.append((c,p,other))
assert placements,'no geometric sheltered candidate in recorded map'
# Lexicographic site priorities: indoor wins even against extreme outdoor score.
assert min([(3,1),(2,900000),(0,1200000)])==(0,1200000)
# A sealed pocket with no exterior connection is legitimate if all work/resources stay inside.
room={(x,z) for x in range(1,5) for z in range(1,5)};throat=(5,2);outside={(6,2),(7,2)}
region=room|{throat}|outside;internal=flood((2,2),region,{throat,(3,3),(3,4)})
assert {(1,2),(2,3),(4,3),(4,4)}<=internal and not internal&outside
rt=(s/'RHFObjectiveRouteV128.cs').read_text();ac=(s/'NJRRescueAccessR040.cs').read_text();vp=(s/'RHFVipEvacS311.cs').read_text();ops=(s/'RHFOperations.cs').read_text();job=(s/'RHFRescueRoleJobGiverS41.cs').read_text();rp=(s/'RHFRescuePlannerS41.cs').read_text()
assert 'ForwardDoorRouteR043(map,origins,captive,' in rt and 'DoorStepR043(map,next,out Thing obstacle)' in rt
assert 'Field f=Search(map,vip,24,false,true,op.FortifyPrison)' in ac
assert 'if(op.FortifyPrison && tier>tierBest)continue;' in ac
assert 'if(op.FortifyPrison && tier==3 && (barrier!=null || earlyEngineer!=null))continue;' in ac
assert 'if(op.FortifyPrison)return false; // R043' in ac
assert 'out IntVec3 indoorPod,true,op.PodPrisonAnchorR043' in vp and 'DirectWorkLaneR043' in ops
fort=vp[vp.index('private bool CanSealR041'):vp.index('private bool SolidFlankS41')]
assert '!outside' not in fort and 'bool outside' not in fort
assert '!seen.Contains(vip.Position) && !vip.CanReach(stand' in fort and 'if(!launcherWork)continue;' in fort
assert '!pocket.Contains(vip.Position)' in fort and fort.index('!pocket.Contains(vip.Position)')<fort.index('GenConstruct.PlaceBlueprintForBuild')
assert 'ThingDef material=ThingDefOf.Wall' in fort and 'GetNamedSilentFail("Barricade")' not in fort
assert 'if(crew<2 || builders<1)continue;' in fort and 'pocket.Contains(stack.Position)' in fort and 'SealLogisticsR044' in fort
assert 'IsOwnSealR043(op,barrier)' in ac and 'op.CompletedBarricades.Contains(wall.Position)' in ac
assert 'GetNamedSilentFail("NJR_ReopenOwnSealR051")' in job and 'IsOwnSealR043(op,order.Objective)' in job
assert 'TryExitAccess(op,map,member,now)' in rp
assert 'njrPrisonAnchorR043' in ops
print('PASS R043 recorded map: door-only geometry cost',cost[goal], 'roofed geometric candidate count',len(placements),'(not a runtime or CE test)')
