"""R041 runtime evidence + independently replayed standable grid.
No actual C#/CE execution: the model intentionally excludes ballistic risk."""
import json,heapq
from pathlib import Path
root=Path(__file__).resolve().parents[1];s=root/'RaidersHaveFamilies/Source'
a=json.loads((root/'tests/fixtures/r042_vip_trace.json').read_text())
assert a['revision']=='R041' and a['events']==18457
assert a['route']['Walls']==1 and a['route']['TotalCost']==16308
assert any(t==39936 and 'checked=6914 valid=0' in msg for t,msg in a['logs'])
assert any(t==39936 and 'fallback to ground exit' in msg for t,msg in a['logs'])
hits=[x for x in a['damage'] if x[3]>0]
assert hits[0][:3]==[39942,a['engineer'],a['other_captive']],hits[:3]
walk={(x,z) for z,row in enumerate(a['rows']) for i,n,w,_ in row if w for x in range(i,i+n)}
structures={tuple(pos):(kind,hp) for pos,kind,hp in a['structures']}
start=tuple(a['route']['from']);goal=tuple(a['route']['to'])
q=[(0,start)];costs={start:0};parents={}
while q:
 cost,c=heapq.heappop(q)
 if costs[c]!=cost:continue
 if c==goal:break
 for dx,dz in [(1,0),(-1,0),(0,1),(0,-1)]:
  n=(c[0]+dx,c[1]+dz)
  if n in structures:
   kind,hp=structures[n]
   if 'Door' not in kind:continue
   step=10+180+16+hp*2
  elif n in walk:step=10
  else:continue
  if cost+step<costs.get(n,10**12):
   costs[n]=cost+step;parents[n]=c;heapq.heappush(q,(cost+step,n))
assert costs[goal]==656,costs.get(goal)
path=[goal]
while path[-1]!=start:path.append(parents[path[-1]])
assert (114,151) in path and structures[(114,151)]==('Door',160)
assert structures[(119,152)]==('Wall',300)
rt=(s/'RHFObjectiveRouteV128.cs').read_text();ac=(s/'NJRRescueAccessR040.cs').read_text()
vp=(s/'RHFVipEvacS311.cs').read_text();pp=(s/'RHFPodPlannerS41.cs').read_text();rp=(s/'RHFRescuePlannerS41.cs').read_text()
assert 'holder.DoorOnlyR042 && obstacle!=null && !(obstacle is Building_Door)' in rt
assert 'alternative.TotalStructuralHP<fresh.TotalStructuralHP' in rt and 'alternative.ExposedTurretCells<=fresh.ExposedTurretCells' in rt
assert 'if(choose)fresh=alternative' in rt and 'DoorRoute]' in rt
assert 'bool direct=FindFieldConstructionSite(vip,launcherDef,podDef,' in vp
assert 'TryPodFallbackR042(op,map,vip,launcherDef,podDef,now,direct)' in vp
assert 'now-op.PhaseStartTick<1800' in vp and 'now-op.PodStageStartedTickS311<450' in vp
assert vp.index('bool direct=FindFieldConstructionSite(vip,launcherDef,podDef,')<vp.index('no physical build site or recoverable access route after retry')
assert 'footprintRejected=' in ac and 'laneRejected=' in ac
assert 'op.NativeAnimalsS41?.Contains(pawn)==true' in ac and 'DisengageR042(pawn,other)' in ac
assert 'other.jobs?.curJob?.targetA.Thing==pawn' in ac
assert 'op.FortifyPrison?Search(map,navigator,0,true,true,true)' in ac
assert 'op.FortifyPrison && NJRRescueAccessR040.TryExitAccess' in rp
assert 'refueler=NativeRefuelerR041(op,engineer)' in vp and 'fortifier!=engineer' in pp
print('R042 recorded geometry: door-only path cost',costs[goal],list(reversed(path)))
print('PASS R042 evidence/model and source contracts, NOT runtime')
