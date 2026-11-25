"""R043 real trace + geometric regression. No claim of running RimWorld C#."""
import json,heapq,collections
from pathlib import Path
r=Path(__file__).resolve().parents[1];s=r/'RaidersHaveFamilies/Source';a=json.loads((r/'tests/fixtures/r044_vip_trace.json').read_text())
assert a['revision']=='R043' and a['events']==18019
assert a['breaches'][0][1:]==[[119,152],'Wall',300]
assert a['breaches'][1][1:]==[[123,149],'Wall',300] and a['breaches'][2][1:]==[[124,149],'Door',160]
assert sum('no safe seal yet' in m for _,m in a['logs'])==10
assert any('sheltered=True tier=0' in m for _,m in a['logs']) and a['refuelFirst']==44811
walk=set();roof=set()
for z,rows in a['rows']:
 for x,n,w,rf in rows:
  for i in range(x,x+n):
   if w:walk.add((i,z))
   if rf:roof.add((i,z))
struct={tuple(p):(k,h) for p,k,h in a['structures']}
card=((1,0),(-1,0),(0,1),(0,-1))
def adj(c):return [(c[0]+dx,c[1]+dz) for dx,dz in card]
def path(start,goal,blocked=frozenset(),opened=frozenset()):
 q=[(0,start)];dist={start:0};prev={}
 while q:
  d,c=heapq.heappop(q)
  if d!=dist[c]:continue
  if goal(c):
   p=[c]
   while p[-1]!=start:p.append(prev[p[-1]])
   return d,p[::-1]
  for n in adj(c):
   if n in blocked:continue
   if n in opened:st=10
   elif n in struct:
    k,h=struct[n];st=10+180+16+h*2 if k=='Door' else None
   else:st=10 if n in walk else None
   if st is not None and d+st<dist.get(n,10**12):dist[n]=d+st;prev[n]=c;heapq.heappush(q,(d+st,n))
 return None
start=tuple(a['route']['from']);goal=tuple(a['route']['to']);d,p=path(start,lambda c:c==goal)
assert d==606 and (114,151) in p and all(struct.get(c,('',''))[0]!='Wall' for c in p)
# Reuse same recorded grid after the ingress breach, preserving the launched pod's launcher footprint.
opened={(119,152)};launcher={(121,152),(121,153)};pod={(122,152)}
d2,p2=path(tuple(a['exitStart']),lambda c:min(c[0],c[1],249-c[0],249-c[1])<=1 and c not in roof,launcher,opened)
assert any(struct.get(c,('',''))[0]=='Door' for c in p2)
assert not any(struct.get(c,('',''))[0]=='Wall' and c not in opened for c in p2)
# Geometry only: seal an actual gap, preserve VIP and work cells, relocate resources instead of vetoing forever.
legal=(walk|opened)-set(struct)-launcher-pod|opened;wall=(119,152);legal.discard(wall)
# The snapshot uses standability, not engine reachability / actual footprint reservation.
seen={tuple(a['vipAtBuild'])};q=collections.deque(seen)
while q:
 c=q.popleft()
 for n in adj(c):
  if n in legal and n not in seen:seen.add(n);q.append(n)
assert any(n in seen for n in adj(wall)) and any(n in seen for c in launcher for n in adj(c)) and any(n in seen for c in pod for n in adj(c))
outside={tuple(c) for c in a['cargo'] if tuple(c) not in seen or tuple(c)==wall}
assert wall in outside and (118,152) in outside
available={c for c in seen&roof if c not in {tuple(p) for p in a['cargo']} and c not in launcher|pod and c!=wall and (c[0]-121)**2+(c[1]-152)**2<=64 and (c[0]-wall[0])**2+(c[1]-wall[1])**2>1}
assert len(available)>=len(outside),(available,outside)
# One physical move per stack; reject closure until all are deposited.
model=set(outside)
for c,target in zip(sorted(outside),sorted(available)):
 assert c in model and target in seen;model.remove(c)
assert not model
rt=(s/'RHFObjectiveRouteV128.cs').read_text();ac=(s/'NJRRescueAccessR040.cs').read_text();vp=(s/'RHFVipEvacS311.cs').read_text();pp=(s/'RHFPodPlannerS41.cs').read_text();job=(s/'RHFRescueRoleJobGiverS41.cs').read_text();cargo=(s/'JobDriver_RHFPodCargoS41.cs').read_text()
assert 'DoorProofR044' in rt and 'DoorOnlyR042=true' in rt and 'NativeDoorProofR044' in rt and '"PassDoors"' in rt and 'DoorGridR044' in rt
assert 'alternative.SearchPending' in rt and 'holder.WallProofStartedR044<180' in rt
assert 'FirstBlockedExitDoor(map,op,navigator)' in ac and 'retry.ExitDoorFailureR044' in ac
assert 'SealLogisticsR044' in vp and 'RememberSealPocketR044' in vp and vp.index('if(!SealLogisticsR044(op,target,pocket')<vp.index('Blueprint_Build plan=GenConstruct.PlaceBlueprintForBuild',vp.index('if(!SealLogisticsR044(op,target,pocket'))
assert 'sealMover!=engineer && sealMover!=refueler' in pp and 'NJR_MoveSealCargoR044' in job and 'Toils_Haul.StartCarryThing(TargetIndex.A)' in cargo and 'TryDropCarriedThing(job.targetB.Cell,ThingPlaceMode.Direct' in cargo
assert 'pocket!=null && !pocket.Contains(c)' in cargo and 'pocket!=null && !pocket.Contains(cell)' in pp
print('PASS R044 geometry: entry',d,p,'exit',d2,'doors',[c for c in p2 if struct.get(c,('',''))[0]=='Door'],'cargo to relocate',sorted(outside),'candidate cells',len(available))
