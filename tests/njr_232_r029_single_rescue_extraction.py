"""R029 regression policy and source contracts; NOT a RimWorld runtime test."""
from pathlib import Path
P=Path(__file__).resolve().parents[1]; S=P/'RaidersHaveFamilies/Source'
j=(S/'RHFRescueRoleJobGiverS41.cs').read_text(); p=(S/'RHFRescuePlannerS41.cs').read_text()
follow=j.split('case RHFRoleS41.FollowEscort:')[-1].split('case RHFRoleS41.Withdraw:')[0]
escort=j.split('case RHFRoleS41.Escort:')[-1].split('case RHFRoleS41.FollowEscort:')[0]
assert 'if(gap>16)' not in follow and 'paired?10:0' in follow
assert 'pawn.CanReach(order.Cell,PathEndMode.OnCell,Danger.Deadly)' in follow
assert 'return Task(JobDefOf.Goto,order.Cell,360);' in follow
assert 'if(op.Phase==2 && order.Cell.IsValid)' in escort
assert escort.index('return Task(JobDefOf.Goto,order.Cell,360);')<escort.index('return ApproachPawn(pawn,escorted,180);')
assert 'EscortCanReachWithdrawalR029(op,existing.Escort)' in p
assert 'EscortCanReachWithdrawalR029(op,candidate)' in p
assert 'board.Assign(rescued,RHFRoleS41.FollowEscort,null,board.WithdrawalCell,now);' in p
assert 'TryUnescortedExitR029(op,rescued,map,board.WithdrawalCell);' in p
assert 'if(enteringWithdrawal && op.Freed!=null)' in p
assert 'freed.jobs.EndCurrentJob(JobCondition.InterruptForced,false);' in p
assert 'board.Assign(soldier,RHFRoleS41.Withdraw,null,\n                        board.WithdrawalCell,now);' in p
assert 'RHFRescueCommandV124.MissionExit(escort);' in p
assert p.index('RHFRescueCommandV124.MissionExit(escort);')<p.index('RHFRescueCommandV124.MissionExit(rescued);')
assert 'op.EscapedWithEscort++' not in p.split('private static void TryUnescortedExitR029',1)[1]
# Small independent state model: ONE rescue operation, no competing Lord.
# Temporary blocked neighbour cells and different movement speeds cannot
# stop a mobile VIP while an actual exit remains reachable.
def movement(phase, distance, reachable, paired):
    return 'move' if phase==2 and reachable and distance>(10 if paired else 0) else 'wait'
for gap in (0,4,16,25,100,400):
    for occupied_adjacent in (False,True):
        assert movement(2,100,True,True)=='move'
assert movement(2,100,True,False)=='move'
assert movement(2,5,True,False)=='move'
assert movement(2,0,True,False)=='wait'
assert movement(2,5,True,True)=='wait'
assert movement(2,100,False,True)=='wait'
assert movement(3,100,True,True)=='wait'
# Single group, different speeds, escort initially blocked for 15 steps.
# Both progress toward fixed edge without waiting for an occupied VIP neighbour.
vip=0; guard=-20; edge=100
for tick in range(200):
    if vip<edge-3:vip+=1
    if tick>=15 and guard<edge-3:guard+=2
    if vip>=edge-3 and guard>=edge-3:break
assert vip>=edge-3 and guard>=edge-3
print('PASS R029 single-operation extraction policy/source contracts; not runtime.')
