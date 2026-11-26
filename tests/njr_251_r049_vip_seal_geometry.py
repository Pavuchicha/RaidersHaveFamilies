"""Replay R048 recorded geometry, including both possible 1x2 origins. Not a native pathfinder test."""
import json
from collections import deque
from pathlib import Path
r=Path(__file__).resolve().parents[1];s=r/'RaidersHaveFamilies/Source'
a=json.loads((r/'tests/fixtures/r049_runtime.json').read_text())
assert a['revision']=='R048' and a['door_destroy']<a['freed']<a['launch']
assert len([m for _,m in a['logs'] if 'viable=0' in m])==8
assert a['launcher_size']==[1,2] and a['actual_exit_doors']==[[124,149],[127,145]]
walk,roof,doors=(set(map(tuple,a[k])) for k in ('walk','roof','doors'))
base=tuple(a['pad']);pod=tuple(a['pod'])
def nb(c):
 x,z=c;return ((x+1,z),(x-1,z),(x,z+1),(x,z-1))
class Geometry:
 def __init__(self,walk,roof,doors,pads,seeds):
  self.free=(walk|doors)-pads;self.doors=doors&self.free;self.ext=self.free-roof;self.seeds=set(seeds)&self.free&roof-self.doors
  self.gates,self.inside=self.get_gates(None);self.gates&=self.dist(self.ext,None,self.inside).keys();self.base=self.dist(self.ext,None)
 def get_gates(self,wall):
  inside=self.seeds-{wall};q=deque(inside);gates=set()
  while q:
   for n in nb(q.popleft()):
    if n==wall or n not in self.free:continue
    if n in self.doors or n in self.ext:gates.add(n);continue
    if n not in inside:inside.add(n);q.append(n)
  return gates,inside
 def dist(self,starts,wall,forbidden=frozenset()):
  d={c:0 for c in starts if c!=wall and c not in forbidden};q=deque(d)
  while q:
   c=q.popleft()
   for n in nb(c):
    if n!=wall and n in self.free and n not in forbidden and n not in d:d[n]=d[c]+1;q.append(n)
  return d
 def benefit(self,wall):
  if wall not in self.inside or wall in self.seeds:return 0
  score=sum(100 if c in self.doors else 10 for c in self.gates-self.get_gates(wall)[0])
  if score:return score
  before=min((self.base.get(c,10**9) for c in self.seeds),default=10**9)
  after=self.dist(self.ext,wall);later=min((after.get(c,10**9) for c in self.seeds),default=10**9)
  if before==10**9:return 0
  return 100 if later==10**9 else min(50,(later-before)*5) if later-before>=2 else 0
for z in (151,153):
 g=Geometry(walk,roof,doors,{base,pod,(121,z)},nb(pod))
 assert g.benefit((124,150))>=100,(z,g.gates,g.inside)
 assert g.benefit((119,150))==0 and g.benefit((121,154))==0
 assert g.benefit(pod)==0 and g.benefit(base)==0
 # actual two doors remain on exterior approach beyond the selected wall
 assert (124,149) in g.gates and (127,145) in g.base
# A door leading only to a closed closet is not a defensive entrance.
g=Geometry({(0,0),(1,0),(2,0),(3,0),(0,1)}, {(0,0),(1,0),(2,0),(3,0)}, {(2,0)},set(),{(0,0)})
assert g.benefit((1,0))==0
v=(s/'RHFVipEvacS311.cs').read_text();geom=(s/'SealGeometryR049.cs').read_text()
assert 'geometry.Benefit(cell)' in v and 'noBenefit=' in v and 'noPocket=' in v and 'noWorker=' in v
assert v.index('geometry.Benefit(cell)')<v.index('CanSealR041(op,vip,cell,protectedRoom')
assert 'NativeEngineerS311(op,cell,engineer,refueler,prepare:false)' in v
assert 'NJRPodSiteClearS41.ReservedFootprint(op,c)' in geom and 'seeds.Contains(wall)' in geom
assert 'gates.IntersectWith(sources.Keys)' in geom and 'later-before>=2' in geom
assert 'GenConstruct.PlaceBlueprintForBuild' in v and 'SealLogisticsR044(op,target,pocket' in v
print('PASS R049: fresh R048 geometry, full launcher footprint (both origins), actual exterior gate at 124,149 blocked by 124,150; no useless R047 cells; native safety remains untested.')
