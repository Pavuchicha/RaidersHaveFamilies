"""R044 real trace; independent local graph model, not RimWorld runtime."""
import json,collections
from pathlib import Path
r=Path(__file__).resolve().parents[1];s=r/'RaidersHaveFamilies/Source'
a=json.loads((r/'tests/fixtures/r045_vip_trace.json').read_text())
assert a['revision']=='R044' and a['events']==9320
assert a['routes'][0]['Walls']==0 and a['routes'][0]['barrier']['pos']==[114,151] and a['routes'][0]['barrier']['hp']==160
assert len(a["chunks"])==5
assert sum('checked=58 valid=0' in m for _,m in a['logs'])==5
assert any('fallback to ground exit' in m for _,m in a['logs'])
assert sum(v==19434 and i==36725 for _,v,i,_ in a['damage'])==15
assert a['last']=='no open exit: engage or breach toward nearest map edge'
# A pass-through-only tile remains traversable, but cannot serve as a work/strike slot.
def connected(cells,start,goal,blocked=None):
 q=collections.deque([start]);seen={start}
 while q:
  x,y=q.popleft()
  if (x,y)==goal:return True
  for c in ((x+1,y),(x-1,y),(x,y+1),(x,y-1)):
   if c in cells and c not in seen and c!=blocked:seen.add(c);q.append(c)
 return False
standable={(0,0),(2,0),(3,0)};walkable=standable|{(1,0)}
assert not connected(standable,(0,0),(3,0)) and connected(walkable,(0,0),(3,0))
assert (1,0) not in standable
# Moving the recorded slag may make a corridor accessible; the model explicitly
# distinguishes an obstructing chunk from a walkable pass-through tile.
walk=set()
for z,runs in a['rows'].items():
 for x,n,w in runs:
  if w:walk.update((i,int(z)) for i in range(x,x+n))
chunks=set(map(tuple,a['chunks']));assert chunks<=walk
assert connected(walk,(121,152),(124,149))
assert not connected(walk-chunks,(121,152),(124,149))
assert not connected({(0,0),(1,0),(2,0)},(0,0),(2,0),(1,0))
assert connected({(0,0),(1,0),(2,0),(0,1),(1,1),(2,1)},(0,0),(2,0),(1,0))
route=(s/'RHFObjectiveRouteV128.cs').read_text();access=(s/'NJRRescueAccessR040.cs').read_text();vip=(s/'RHFVipEvacS311.cs').read_text();debris=(s/'NJRVipDebrisR045.cs').read_text();driver=(s/'JobDriver_RHFPodCargoS41.cs').read_text();jobs=(s/'RHFRescueRoleJobGiverS41.cs').read_text()
assert 'cell.Walkable(map)' in route and 'cost<0 && obstacle==null' in route
assert 'if(!c.Walkable(map)' in access and 'slot.Standable(map)' in access
assert 'op.Freed?.Contains(pawn)==true' in access and 'if(freed || !activeThreat)' in access
assert vip.index('NJRVipDebrisR045.TryStart')<vip.index('no physical build site or recoverable access route after retry')
assert 'state.Tried.Count>=6' in debris and 'LocalBypass(map,cell)' in debris and 'now-state.Started<1200' in debris
assert 'Toils_Haul.StartCarryThing' in driver and 'if(op==null || carried==null || debris)return;' in driver and 'NJRVipDebrisR045.Completed' in driver
assert 'NJRVipDebrisR045.IsAssigned(op,pawn,cargo)' in jobs
print('PASS R045 trace, traversal/placement split, bounded physical debris and VIP combat gate')
