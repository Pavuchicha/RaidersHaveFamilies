"""S4.1-A/E native Phase3 bounded role dispatch - static checks only."""
from pathlib import Path
R=Path(__file__).resolve().parents[1]
SRC=R/'RaidersHaveFamilies/Source'
B=(SRC/'RHFTacticalBoardS41.cs').read_text('utf-8')
P=(SRC/'RHFPodPlannerS41.cs').read_text('utf-8')
G=(SRC/'RHFRescueRoleJobGiverS41.cs').read_text('utf-8')
N=(SRC/'RHFVipEvacS311.cs').read_text('utf-8')
W=(R/'.github/workflows/build.yml').read_text('utf-8')

def verify(b,p,g,n,w):
    assert 'Engineer = 9, BoardPod = 10, PodAssembly = 11' in b
    assert '            Scribe_References.Look(ref PodWorkTargetS41' in b
    assert 'board.PodWorkStageS41==op.PodConstructionStage' in p
    assert 'ChooseAssemblyCell(op,map)' in p and 'vip.CanReach(cell,PathEndMode.OnCell,Danger.Some)' in p
    assert 'PinnedEngineerValid(RHFOperation op' in p
    assert 'RememberEngineer(RHFOperation op' in p
    assert 'RHFPodPlannerS41.PinnedEngineerValid(op,map,siteCell)' in n
    assert 'occupied.Contains(soldier)' in p
    assert 'op.Freed.Contains(op.Vip)' in p
    assert '(op.Phase==3 && candidate==op.BoardS41?.PodEngineerS41)' in (SRC/'RHFRescuePlannerS41.cs').read_text('utf-8')
    assert 'RHFRoleS41.BoardPod' in p and 'RHFRoleS41.BoardPod' in g
    assert 'RHFRoleS41.Engineer' in p and 'RHFRoleS41.Engineer' in g
    assert 'giver.JobOnThing(pawn,site,true)' in p
    assert 'JobMaker.MakeJob(JobDefOf.EnterTransporter,vehicle)' in g
    assert 'RHFPodPlannerS41.Plan(op,map,engineer' in n
    assert 'if(RHFRescuePlannerS41.Enabled)return true;' in n
    assert 'if(RHFRescuePlannerS41.Enabled)return;' in n
    assert 'RHFPodPlannerS41.ResetAfterFallback(op)' in n
    assert 'if(!RHFRescuePlannerS41.Enabled)' in n
    assert 'python3 tests/rhf_154_s41_phase3_native_roles.py' in w
    assert 'Danger.Some' in p and 'cell.GetRoom(map)!=pawn.Position.GetRoom(map)' in p

verify(B,P,G,N,W)
changes=[
 ('b','Engineer = 9, BoardPod = 10, PodAssembly = 11','Engineer = 9'),
 ('b','Scribe_References.Look(ref PodWorkTargetS41','// Scribe_References.Look(ref PodWorkTargetS41'),
 ('p','op.Freed.Contains(op.Vip)','false'),
 ('p','PinnedEngineerValid(RHFOperation op','BrokenValid(RHFOperation op'),
 ('p','ChooseAssemblyCell(op,map)','op.PodCell'),
 ('n','RHFPodPlannerS41.PinnedEngineerValid(op,map,siteCell)','false'),
 ('p','occupied.Contains(soldier)','false'),
 ('p','giver.JobOnThing(pawn,site,true)','null'),
 ('p','RHFRoleS41.BoardPod','RHFRoleS41.Hold'),
 ('g','JobMaker.MakeJob(JobDefOf.EnterTransporter,vehicle)','JobDefOf.Wait,vehicle,180'),
 ('n','RHFPodPlannerS41.ResetAfterFallback(op)','// missing rollback'),
 ('n','if(RHFRescuePlannerS41.Enabled)return true;','if(false)return true;'),
 ('n','if(!RHFRescuePlannerS41.Enabled)','if(false)'),
 ('w','python3 tests/rhf_154_s41_phase3_native_roles.py',''),
]
data={'b':B,'p':P,'g':G,'n':N,'w':W}
for idx,(name,old,new) in enumerate(changes,1):
    assert old in data[name],(idx,old)
    vals=dict(data);vals[name]=vals[name].replace(old,new,1)
    try:verify(vals['b'],vals['p'],vals['g'],vals['n'],vals['w'])
    except AssertionError:pass
    else:raise AssertionError('negative mutation escaped: '+str(idx))
print(f'PASS S4.1-A/E phase3 native roles: {len(changes)} negative mutants; STATIC ONLY')
