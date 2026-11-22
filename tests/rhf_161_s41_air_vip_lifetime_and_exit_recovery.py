"""S4.1 A/L safety contracts; these are NOT a real C# build or RimWorld gameplay test.

Regression goals: keep the *same* VIP after world arrival; don't time out
EnterTransporter; route downed VIP evacuation using a *mobile* pawn.
"""
from pathlib import Path
root=Path(__file__).resolve().parents[1]
source=root/'RaidersHaveFamilies'/'Source'
flight=(source/'RHFEnemyPodLaunchS41.cs').read_text('utf-8')
job=(source/'RHFRescueRoleJobGiverS41.cs').read_text('utf-8')
route=(source/'RHFRescuePlannerS41.cs').read_text('utf-8')
workflow=(root/'.github/workflows/build.yml').read_text('utf-8')


def verify(a,b,c,d):
    assert 'if(p==Vip)' in a
    assert 'if(p.Discarded)' in a
    assert 'Find.WorldPawns.PassToWorld(p,PawnDiscardDecideMode.KeepForever);' in a
    assert 'Find.WorldPawns.ForcefullyKeptPawns.Add(p);' in a
    assert 'p.IsWorldPawn() && Find.WorldPawns.ForcefullyKeptPawns.Contains(p)' in a
    assert 'else if(!p.IsWorldPawn())Find.WorldPawns.PassToWorld(p);' in a
    assert 'return JobMaker.MakeJob(JobDefOf.EnterTransporter,vehicle);' in b
    assert 'Task(JobDefOf.EnterTransporter,vehicle,180)' not in b
    assert 'Pawn navigator=null;' in c
    assert 'if(navigator==null && Active(member,map))' not in c # intentionally explicit loop
    assert 'if(navigator==null)navigator=member;' in c
    assert 'if(navigator==null && Active(freed,map))navigator=freed;' in c
    assert 'RCellFinder.TryFindBestExitSpot(navigator,out IntVec3 cell)' in c
    assert 'navigator.CanReach(op.ExitCell,PathEndMode.OnCell,Danger.Some)' in c
    assert 'op.ExitCell=IntVec3.Invalid;' in c
    assert 'if(navigator==null)' in c
    assert 'evacuation stalled: no mobile rescuer' in c
    assert 'rhf_161_s41_air_vip_lifetime_and_exit_recovery.py' in d

verify(flight,job,route,workflow)
mutations=[
 ('a','PawnDiscardDecideMode.KeepForever','PawnDiscardDecideMode.Decide'),
 ('a','Find.WorldPawns.ForcefullyKeptPawns.Add(p);','/* removed */'),
 ('a','if(p.Discarded)','if(false)'),
 ('a','p.IsWorldPawn() && Find.WorldPawns.ForcefullyKeptPawns.Contains(p)','true'),
 ('b','return JobMaker.MakeJob(JobDefOf.EnterTransporter,vehicle);','return Task(JobDefOf.EnterTransporter,vehicle,180);'),
 ('c','Pawn navigator=null;','Pawn navigator=anchor;'),
 ('c','RCellFinder.TryFindBestExitSpot(navigator,out IntVec3 cell)','RCellFinder.TryFindBestExitSpot(anchor,out IntVec3 cell)'),
 ('c','navigator.CanReach(op.ExitCell,PathEndMode.OnCell,Danger.Some)','anchor.CanReach(op.ExitCell,PathEndMode.OnCell,Danger.Deadly)'),
 ('c','if(navigator==null)','if(false)'),
 ('d','rhf_161_s41_air_vip_lifetime_and_exit_recovery.py','removed'),
]
ss=dict(a=flight,b=job,c=route,d=workflow)
for which,old,replacement in mutations:
    assert old in ss[which],(which,old)
    m=dict(ss);m[which]=m[which].replace(old,replacement,1)
    try:verify(m['a'],m['b'],m['c'],m['d'])
    except AssertionError:pass
    else:raise AssertionError('Mutation passed: '+old)

# Tiny independent state illustrations. These do not simulate the RimWorld
# engine, only record intended control outcomes for future gameplay tests.
def choose_anchor_and_navigator(freed,team):
    anchor=next((name for name,spawned,mobile in freed if spawned),None)
    navigator=next((name for name,spawned,mobile in freed if spawned and mobile),None)
    for name,spawned,mobile in team:
        if not spawned or not mobile:continue
        if anchor is None:anchor=name
        if navigator is None:navigator=name
    return anchor,navigator
assert choose_anchor_and_navigator([('downedVIP',True,False)], [('escort',True,True)]) == ('downedVIP','escort')
assert choose_anchor_and_navigator([('downedVIP',True,False)], []) == ('downedVIP',None)
print('PASS S4.1-A/L: world-pawn lifetime, boarding jobs, reachable mobile extraction anchor,')
print(f'{len(mutations)} static negative mutants; C# compiler/game not tested')
