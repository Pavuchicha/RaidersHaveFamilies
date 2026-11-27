"""Replay R039 recorded map geometry and verify R040 shared access contracts.
No engine, C# or CE execution; no inference of native reachability from walk grid.
"""
import json, heapq
from pathlib import Path
r=Path(__file__).resolve().parents[1];s=r/'RaidersHaveFamilies/Source'
a=json.loads((r/'tests/fixtures/r040_vip_access.json').read_text())
assert a['revision']=='R039' and a['events']==18411 and len(a['rows'])==250
walk=set();roof=set()
for z,row in enumerate(a['rows']):
 for x,n,w,q in row:
  for i in range(x,x+n):
   if w:walk.add((i,z))
   if q:roof.add((i,z))
structures={tuple(pos):(kind,hp) for _,pos,kind,hp in a['structure'] if hp}
start=tuple(a['start']);vip=tuple(a['vip'])
def search(start,goal=None,exit_only=False,allow_wall=True):
 d={start:0};parent={};heap=[(0,start)];expanded=0
 while heap:
  cost,c=heapq.heappop(heap)
  if d[c]!=cost:continue
  expanded+=1
  if c==goal or (exit_only and min(c[0],249-c[0],c[1],249-c[1])==0 and c in walk and c not in roof and c not in structures):
   path=[c]
   while path[-1]!=start:path.append(parent[path[-1]])
   return cost,list(reversed(path)),expanded
  for dx,dz in ((1,0),(-1,0),(0,1),(0,-1)):
   n=(c[0]+dx,c[1]+dz)
   if not (0<=n[0]<250 and 0<=n[1]<250):continue
   if n in structures:
    kind,hp=structures[n]
    if 'Door' in kind:step=10+180+16+2*hp
    elif allow_wall:step=10+2400+3*(16+2*hp)
    else:continue
   elif n in walk:step=10
   else:continue
   new=cost+step
   if new<d.get(n,10**10):d[n]=new;parent[n]=c;heapq.heappush(heap,(new,n))
 return None
cost,path,_=search(start,vip)
assert any('Door' in structures.get(c,('',0))[0] for c in path),path
assert not any('Wall' in structures.get(c,('',0))[0] for c in path),path
for c in a['removed']:structures.pop(tuple(c),None);walk.add(tuple(c))
cost2,path2,n=search(vip,exit_only=True,allow_wall=False)
assert any('Door' in structures.get(c,('',0))[0] for c in path2)
print('Recorded-map model: ingress',cost,path,'exit',cost2,'nodes',n,'doors',[c for c in path2 if c in structures])
access=(s/'NJRRescueAccessR040.cs').read_text();planner=(s/'RHFRescuePlannerS41.cs').read_text();pod=(s/'RHFVipEvacS311.cs').read_text();force=(s/'NJRForceStrengthR017.cs').read_text();jobs=(s/'RHFRescueRoleJobGiverS41.cs').read_text();pp=(s/'RHFPodPlannerS41.cs').read_text()
assert 'RHFObjectiveRouteV128.Step(map,next,out obstacle)' in access
assert 'f.First[nid]=f.First[id]??obstacle' in access and 'if(toEdge && Edge(map,at)==0' in access
assert 'NJRRescueAccessR040.TryExitAccess(op,map,navigator,now)' in planner
assert 'NJRRescueAccessR040.TryPodAccess(op,map,vip,launcherDef,podDef,now,' in pod
assert 'NJRPodSiteClearS41.Needed(map,cell,vehicle,launcher,pod,vip,rotation)!=0' in access
assert 'op.Squad?.Contains(pawn)==true' in access and 'if(!soldier && !beast && !freed)continue;' in access and 'def!="Ignite"' in access
assert 'party.Drop || party.Role!=(int)RHFStrikeRole.Rescue' not in force
assert 'op.PodConstructionStage==0' in jobs and 'NJRRescueAccessR040.PermitsExit(op,obstacle)' in jobs
assert 'fortifier!=engineer' in pp and 'board.Assign(fortifier,RHFRoleS41.Engineer,parallelWall' in pp
assert 'baseRevision="R054"' in (s/'Diagnostics/NJRTestRecorderR030.cs').read_text()
print('PASS R040: shared structural costs, scoped handoff, access routes and parallel native work source contracts; no runtime proof.')
