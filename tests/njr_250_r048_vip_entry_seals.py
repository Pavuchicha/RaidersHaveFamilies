"""Recorded R047 geometry + executable-pair and clearance source contracts. Not engine simulation."""
import json
from pathlib import Path
from collections import deque
r=Path(__file__).resolve().parents[1];s=r/'RaidersHaveFamilies/Source';a=json.loads((r/'tests/fixtures/r048_runtime.json').read_text());g=a['layout']
assert a['events']==[13303,33190] and a['manual_tick']-a['stall_tick']>23000
assert a['door']==[114,151] and a['door_hp']==160 and a['chunk']==[115,150]
walk,roof,doors,cells=(set(map(tuple,g[k])) for k in ('walk','roof','doors','cells'));pads={tuple(g['pad']),tuple(g['pod'])}
def nb(c):
 x,y=c;return ((x+1,y),(x-1,y),(x,y+1),(x,y-1))
def gates(seed,blocked):
 seen={seed};interior={seed};q=deque([seed]);out=set()
 while q:
  for c in nb(q.popleft()):
   if c not in cells or c==blocked or c in seen or c in pads:continue
   seen.add(c)
   if c in doors:out.add(c);continue
   if c not in walk:continue
   if c not in roof:out.add(c);continue
   interior.add(c);q.append(c)
 return out,interior
def exterior(interior):
 valid=(walk|doors)-interior-pads;seen=valid-roof;q=deque(seen)
 while q:
  for c in nb(q.popleft()):
   if c in valid and c not in seen:seen.add(c);q.append(c)
 return seen
for seed in [(123,152),(122,151)]:
 before,interior=gates(seed,None);ext=exterior(interior)
 assert before&ext=={(124,149)}
 def blocked(c):return (before-gates(seed,c)[0])&ext
 assert not blocked((119,150)) and not blocked((121,154))
 assert blocked((124,150))=={(124,149)}
# Pair selection: the axe preference cannot defeat actual executable slot.
actors=[('axe',10000,None),('rifle',200,(114,150))]
assert max((a for a in actors if a[2] is not None),key=lambda a:a[1])[0]=='rifle'
# If no pair is available, only a recorded prefix obstruction is eligible for move.
route=[(116,150),(115,150),(114,150),(114,151),(114,152)]
assert (115,150) in route[:3] and (119,150) not in route[:3]
ac=(s/'NJRRescueAccessR040.cs').read_text();v=(s/'RHFVipEvacS311.cs').read_text();p=(s/'RHFRescuePlannerS41.cs').read_text();d=(s/'NJRVipDebrisR045.cs').read_text();j=(s/'RHFRescueRoleJobGiverS41.cs').read_text();driver=(s/'JobDriver_RHFPodCargoS41.cs').read_text()
assert 'SelectVipPairR048' in ac and 'if(!c.IsValid)continue;' in ac and 'actor.CanReach(cell,PathEndMode.OnCell,Danger.Deadly)' in ac
assert 'breachSlots.Count==0' in p and 'TryEntryDebrisR048' in p and 'breachSlots.Add(actor,cell)' in p
assert 'route.FirstBarrierIndex-12' in ac and 'new HashSet<IntVec3>(route.Cells)' in ac and 'forbidden.Contains(c)' in d
assert 'op.Phase==1 || (op.Phase==2 && op.PodLaunchCompleted)' in j and 'op.Phase==1 && (debris || op.TryPod)' in driver and 'op.Phase==2 && op.PodLaunchCompleted' in driver
geom=(s/'SealGeometryR049.cs').read_text()
assert 'gates.RemoveWhere(g=>!doors.Contains(g)' in geom and 'if(!after.Contains(gate))' in geom
assert 'if(benefit<=0){noBenefit++;continue;}' in v and 'new SealGeometryR049(op,map).Benefit(target)<=0' in v
print('PASS R048: actual R047 geometry rejects both old seal cells, blocks exterior door at 124,150; native jobs still require playtest.')
