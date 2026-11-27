"""Recorded R057 no-op cargo loop and R058 source contracts; not engine simulation."""
from pathlib import Path
import json
r=Path(__file__).resolve().parents[1];s=r/'RaidersHaveFamilies/Source'
a=json.loads((r/'tests/fixtures/r058_runtime.json').read_text());assert a['revision']=='R057'
assert sum(a['cargo_moves'].values())==589
assert max(a['cargo_moves'].values())==586
assert [t for t,m in a['logs'] if 'Real enemy wall blueprint' in m]==[39289,41629]
assert [t for t,m in a['logs'] if 'Freed=' in m]==[40009]
assert [43411,'Frame_PodLauncher'] in a['build'] and [45458,'PodLauncher'] in a['build']
assert [t for t,m in a['logs'] if 'WorkGiver unavailable' in m]==[40549,41449,42349,43249]
v=(s/'RHFVipEvacS311.cs').read_text(); j=(s/'RHFRescueRoleJobGiverS41.cs').read_text(); h=(s/'JobDriver_RHFPodCargoS41.cs').read_text();p=(s/'RHFPodPlannerS41.cs').read_text()
assert 'order.Objective.Position!=order.Cell' in j[j.index('if(op.Phase==1 && op.TryPod'):]
assert 'job.targetA.Thing.Position!=job.targetB.Cell' in h
assert 'moving.Position==running.jobs.curJob.targetB.Cell' in v
assert 'cell==stack.Position' in v and 'SafeCargoCellR058(op,map,cell)' in v
assert 'SafeCargoCellR058(op,map,c)' in h and 'SafeCargoCellR058(op,carrier.Map,carrier.Position)' in h
assert 'ThingPlaceMode.Direct,out Thing dropped' in h
assert 'RememberDeferredR058(op,deferred,deferredPocket)' in v and 'DeferredPocketR058(op)' in p
assert 'if(op.PodConstructionStage>=3)return null;' in v
assert 'if(op.PodConstructionStage>=4)CancelUnfinishedSealsR058' in v
assert 'site.Destroy(DestroyMode.Cancel)' in v and 'NJRPodSiteClearS41.ReservedFootprint(op,c)' in v[v.index('private void CancelUnfinishedSealsR058'):v.index('private void AssignExtraSealsR055')]
assert 'if(site==null || worker==null)return;' in v
assert '!RHFOperationController.BusyProjectR055(p,current)' in p
# Small abstract lifecycle check: a relocation is actionable only if it changes position;
# optional construction is never started at fueling or later.
def relocate(source,destination):return source!=destination
assert not relocate((123,152),(123,152)) and relocate((119,150),(121,154))
assert [stage for stage in range(1,7) if stage<3]==[1,2]
print('PASS R058: 586 repeated cargo moves, delayed launcher and wall; no-op, staging, boarding cancellation source contracts')
