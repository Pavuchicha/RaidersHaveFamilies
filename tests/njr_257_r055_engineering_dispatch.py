"""R055: recorded R054 failure and prospective safety/source contracts; no engine emulation."""
import json
from pathlib import Path
from collections import deque
r=Path(__file__).resolve().parents[1]; s=r/'RaidersHaveFamilies/Source'; a=json.loads((r/'tests/fixtures/r055_runtime.json').read_text())
assert a['revision']=='R054'
logs=a['logs']; jobs=a['jobs']
assert [t for t,m in logs if 'Real enemy wall blueprint' in m]==[39306,46146]
assert next(t for t,m in logs if 'Freed=' in m)==40116
assert next(t for t,_,_,j,_,_ in jobs if j=='FinishFrame')==42328
assert any(t==41810 and j=='RHF_UnloadPodCargoS41' and p==36776 for t,p,_,j,_,_ in jobs)
assert len({p for _,p in a['exits']})==5 and a['last_operations']==[]
assert len([1 for _,m in logs if 'physically dismantled own exit obstruction' in m])==2
# Independent jobs: preserve actual native finish/delivery ownership, allow a second safe wall
# without waiting for the first to complete. A combined cut cannot seal the VIP route.
free={(x,z) for x in range(5) for z in (0,2)}|{(0,1),(4,1)}
def reachable(blocked):
 q=deque([(0,1)]); seen=set(q)
 while q:
  x,z=q.popleft()
  for n in ((x-1,z),(x+1,z),(x,z-1),(x,z+1)):
   if n in free and n not in blocked and n not in seen:seen.add(n);q.append(n)
 return (4,1) in seen
assert reachable({(2,0)}) and reachable({(2,2)}) and not reachable({(2,0),(2,2)})
read=lambda n:(s/n).read_text(); v=read('RHFVipEvacS311.cs'); o=read('RHFOperations.cs'); w=read('NJREarlyWingR053.cs'); g=read('SealGeometryR049.cs'); ac=read('NJRRescueAccessR040.cs')
assert 'n=="FinishFrame"' in v and 'n=="HaulToContainer"' in v and 'BusyProjectR055(candidate,site)' in v
assert 'now-op.EarlyPadProbeTickR052<=240' in v and 'RHFPodSupplyS41.Unload(op,p)' in v and 'now-op.EarlyPadProbeTickR052<=240' in w
assert 'op.BarricadesBuilt+op.PlannedBarricadesR055.Count>=op.SealLimitR052' in v and 'Scribe_Collections.Look(ref PlannedBarricadesR055' in o
assert 'op.PlannedBarricadesR055.Contains(c)' in g and 'c==wall || op.PlannedBarricadesR055.Contains(c)' in v
assert 'op.PlannedBarricadesR055.Contains(wall.Position)' in ac and 'ReconcileSealsR055' in v
assert 'AssignExtraSealsR055(op,op.Vip,builders,used,now,true)' in v and 'AssignExtraSealsR055(op,vip,spare,assigned,now,false)' in v
assert 'KeepProjectStandbyR055(op,builders,used,now)' in v and 'op.PodConstructionStage>=4 || op.BarricadesBuilt+op.PlannedBarricadesR055.Count' in v
print('PASS R055: recorded delayed cargo, actual job names, five exits, combined-cut model, parallel wall source contracts')
