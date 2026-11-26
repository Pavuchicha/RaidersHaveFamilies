"""Recorded R040 facts, independent terrain-only Dijkstra, static R041 contracts. Not an engine test."""
from pathlib import Path
import json,heapq
r=Path(__file__).resolve().parents[1];s=r/'RaidersHaveFamilies/Source'
a=json.loads((r/'tests/fixtures/r041_vip_trace.json').read_text());assert a['events']==10036 and a['revision']=='R040'
assert a['route']['Walls']==1 and a['route']['Doors']==0 and a['route']['TotalCost']==4278
assert a['first_refuel']==42605
assert any(t==40512 and 'Blueprint generated' in m for t,m in a['logs'])
assert any(t==44543 and 'Real hostile world transport' in m for t,m in a['logs'])
assert sum('Native work] Refuel:' in m for t,m in a['logs'])==4
walk={(i,z) for z,row in enumerate(a['rows']) for x,n,w,_ in row if w for i in range(x,x+n)}
structures={tuple(pos):(kind,hp) for _,pos,kind,hp in a['structure']}
start=tuple(a['route']['from']);goal=tuple(a['route']['to']);q=[(0,start)];d={start:0};prev={}
while q:
 cost,c=heapq.heappop(q)
 if d[c]!=cost:continue
 if c==goal:break
 for dx,dz in ((1,0),(-1,0),(0,1),(0,-1)):
  n=(c[0]+dx,c[1]+dz)
  if n in structures:
   k,hp=structures[n];step=10+180+16+2*hp if 'Door' in k else 10+2400+12000+3*(16+2*hp)
  elif n in walk:step=10
  else:continue
  if cost+step<d.get(n,10**10):d[n]=cost+step;prev[n]=c;heapq.heappush(q,(cost+step,n))
assert goal in d
path=[goal]
while path[-1]!=start:path.append(prev[path[-1]])
assert any('Door' in structures.get(c,('',0))[0] for c in path)
assert not any('Wall' in structures.get(c,('',0))[0] for c in path)
print('Recorded geometry: door route',d[goal],list(reversed(path)))
p=(s/'RHFVipEvacS311.cs').read_text();f=(s/'NJRForceStrengthR017.cs').read_text();a=(s/'NJRRescueAccessR040.cs').read_text();b=(s/'RHFPodPlannerS41.cs').read_text();rt=(s/'RHFObjectiveRouteV128.cs').read_text()
assert 'if(op.PodConstructionStage==2 && op.PodLauncherThing!=null' in p and 'refueler=NativeRefuelerR041(op,engineer)' in p
assert 'refueler!=engineer && refueler!=fortifier' in b and 'board.Assign(refueler,RHFRoleS41.Engineer,launcher,assembly,now)' in b
assert 'fuel==null || fuel.IsFull' in b and 'giver.HasJobOnThing(pawn,site,true)' in b
assert 'active>=(refueler==null?3:4)' in p and 'NativeEngineerS311(op,parallelWall.Position,engineer,refueler,pocket:RHFPodSupplyS41.SealPocketR044(op))' in p
assert 'CanSealR041' in p and 'reserved.Contains(c)' in p and '!seen.Contains(vip.Position)' in p
assert 'if(fortifyWork!=null)return;' not in p
assert 'pawns.Count>=7?3:2' in f and 'EnsureMinLevelWithMargin(10)' in f and 'p.IsWorldPawn()' in f
assert 'foreach(Rot4 rotation in NJRPodSiteClearS41.Rotations)' in a and 'WorkLaneR041(map,vip,footprint,launchSlots,podSlots,barrier,earlyEngineer!=null)' in a
assert 'slots.Count<2' in a and 'room==prison' in a and 'GenAdj.OccupiedRect' in a
assert 'StepVipR041' in rt and 'cost+=12000' in rt
print('PASS R041: recorded sequence and geometry; parallel jobs, specialists, protected access and actual footprints (STATIC ONLY)')
