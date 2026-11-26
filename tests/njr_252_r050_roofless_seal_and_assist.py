"""R050: replay R049 roof-clearance topology and recorded prisoner damage.
Model and source contracts; no native engine emulation."""
import json
from pathlib import Path
from collections import deque
r=Path(__file__).resolve().parents[1];s=r/'RaidersHaveFamilies/Source'
a=json.loads((r/'tests/fixtures/r050_runtime.json').read_text())
assert a['revision']=='R049' and len(a['logs'])==7
assert all('seeds=0' in m and 'viable=0' in m for _,m in a['logs'])
assert a['hits'][0][:3]==[39962,19434,36819] and a['hits'][1][:3]==[39963,36819,19434]
pod=tuple(a['pod']);launcher=tuple(a['launcher'])
walk,stand,roof,doors,roof_before=(set(map(tuple,a[k])) for k in ('walk','stand','roof','doors','roof_before'))
assert (123,152) in roof_before-roof and (124,150) in roof_before-roof
def nb(c):
 x,y=c;return ((x-1,y),(x+1,y),(x,y-1),(x,y+1))
def dist(starts,free,blocked=None,forbidden=frozenset()):
 d={c:0 for c in starts if c in free and c!=blocked and c not in forbidden};q=deque(d)
 while q:
  c=q.popleft()
  for n in nb(c):
   if n!=blocked and n in free and n not in forbidden and n not in d:d[n]=d[c]+1;q.append(n)
 return d
for shift in (-1,1):
 free=(walk|doors)-{pod,launcher,(launcher[0],launcher[1]+shift)}
 seeds=set(nb(pod))&free&stand-doors
 assert seeds and not seeds&roof,'actual post-clearance boarding anchor is roofless'
 exposed=free-roof-doors
 exterior=set(dist({c for c in exposed if abs(c[0]-pod[0])==20 or abs(c[1]-pod[1])==20},exposed))
 assert (123,152) in seeds-exterior and (124,150) not in exterior
 exterior-=seeds
 def gates(wall=None):
  inside=set(dist(seeds,free-doors-exterior,wall))
  return {n for c in inside for n in nb(c) if n in (doors|exterior) and n!=wall},inside
 g,inside=gates()
 g&=dist(exterior,free,forbidden=inside).keys()
 assert (124,150) in inside and (124,150) not in seeds
 assert g-gates((124,150))[0],(shift,g)
 assert not g-gates((121,154))[0] # decorative dead-end
v=(s/'RHFVipEvacS311.cs').read_text();g=(s/'SealGeometryR049.cs').read_text();d=(s/'NJRPrisonerDefenseR047.cs').read_text()
ac=(s/'NJRRescueAccessR040.cs').read_text();cast=(s/'NJRPrisonerCastGuardR050.cs').read_text()
assert 'HasShelteredSeed' in v and 'unroofed.Contains(n)' in g
assert 'c.Standable(map))seeds.Add(c)' in g and 'exterior.ExceptWith(seeds)' in g
assert 'ConditionalWeakTable<RHFOperation,Contact>' in d and 'c.Responders.Count<2' in d
assert 'BusyCritical(op,p)' in d and 'LengthHorizontalSquared<=100' in d and 'c.Tick<=360' in d
assert 'op.Targets?.Contains(attacker)!=true' in d and 'JobDefOf.AttackMelee' in d
assert 'MayDefend(op,pawn,prisoner)' in cast and '__result=false;return false' in cast
assert 'if(freed || !activeThreat)' not in ac
print('PASS R050: actual R049 roofless boarding tile and meaningful seal, first damage order, bounded shared melee response contracts.')
