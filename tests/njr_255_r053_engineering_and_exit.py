"""R053: actual R052 recorded map + temporal allocation/exit model; no C# engine execution."""
import json
from pathlib import Path
from collections import deque
root=Path(__file__).resolve().parents[1];s=root/'RaidersHaveFamilies/Source'
a=json.loads((root/'tests/fixtures/r053_runtime.json').read_text());assert a['revision']=='R052'
# R052's planning snapshot: three advancing fighters, only two reserved for pad.
assert any(t==39525 and phase==1 and sum(o[1]=='Advance' for o in orders)==3 for t,phase,stage,walls,launched,decision,orders in a['snapshots'])
assert any(phase==3 and walls==2 for t,phase,stage,walls,launched,decision,orders in a['snapshots'])
assert a['snapshots'][-1][1]==2 and a['snapshots'][-1][4] and a['snapshots'][-1][5].startswith('no open exit')
# Recorded terminal geometry, cardinal walkability model (not native pathfinding).
w,h=a['width'],a['height'];free={(x,z) for z,row in enumerate(a['walk_rows']) for x,c in enumerate(row) if c=='.'}
def flood(start,walk):
 seen={start};q=deque([start])
 while q:
  x,z=q.popleft()
  for n in ((x-1,z),(x+1,z),(x,z-1),(x,z+1)):
   if n in walk and n not in seen:seen.add(n);q.append(n)
 return seen
def edge(cells):return any(x in (0,w-1) or z in (0,h-1) for x,z in cells)
chunk=tuple(a['chunk']['pos']);assert chunk==(123,150) and a['launcher']['pos']==[121,152] and a['remaining_seal']['pos']==[120,150]
trapped=flood((122,150),free);assert len(trapped)==3 and not edge(trapped)
assert edge(flood((124,150),free)) and edge(flood((124,149),free))
assert edge(flood((122,150),free|{chunk})), 'moving actual recorded chunk connects isolated pocket to map edge'
# Synthetic policy tests for dynamic split, not a mock of the RimWorld job drivers.
def wings(n,contacts):
 assault=1 if not contacts else min(n,max(2,contacts+1));return assault,n-assault
assert wings(5,0)==(1,4) and wings(5,1)==(2,3) and wings(5,4)==(5,0)
assert wings(2,0)==(1,1)
read=lambda x:(s/x).read_text();wing=read('NJREarlyWingR053.cs');v=read('RHFVipEvacS311.cs');p=read('RHFRescuePlannerS41.cs');ac=read('NJRRescueAccessR040.cs');d=read('NJRVipDebrisR045.cs');o=read('RHFOperations.cs');j=read('RHFPodPlannerS41.cs')
assert 'contacts.Count==0?1' in wing and 'state.Engineering.Add(p)' in wing and 'now-state.Tick>=90' in wing
assert o.index('NativePrepareBeforeReleaseR051(op,now)')<o.index('RHFRescueCommandV124.Infiltrate(this, op, now)')
assert 'foreach(Pawn p in crew)' in v and 'wallWorker' in v and 'unloadSlots.Add(unload)' in v and 'builders)if(!used.Contains(p)' in v
assert 'PostLaunchCohortsR053' in p and 'exits[member]=exit;else trapped.Add(member)' in p and 'RHFRoleS41.Withdraw,null,destination' in p
assert 'TryLocalPocketR053' in ac and 'ally.CanReach(n,PathEndMode.OnCell,Danger.Deadly)' in ac and 'corridor.Path()' in ac
assert 'LocalBypass(op,map,cell)' in d and 'n!=op.BarricadeCell && !NJRPodSiteClearS41.ReservedFootprint(op,n)' in d
assert '!pawn.CanReserve(result.targetA)' in j and '!pawn.CanReserve(result.targetB)' in j
print('PASS R053: actual trapped 3-cell pocket reconnected by moving the recorded chunk; dynamic split and physical recovery source contracts')
