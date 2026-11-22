"""S4.1-A/G source contracts and mutation guards; NOT a gameplay or C# compiler."""
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
OFF=(ROOT/'RaidersHaveFamilies/Source/RHFOffenseS41.cs').read_text('utf-8')
BOARD=(ROOT/'RaidersHaveFamilies/Source/RHFTacticalBoardS41.cs').read_text('utf-8')
DUTY=(ROOT/'RaidersHaveFamilies/Defs/DutyDefs/RHF_OffenseS41.xml').read_text('utf-8')
CHARGE=(ROOT/'RaidersHaveFamilies/Source/JobDriver_RHFPlantBreachChargeS41.cs').read_text('utf-8')
OPS=(ROOT/'RaidersHaveFamilies/Source/RHFOperations.cs').read_text('utf-8')
WORKFLOW=(ROOT/'.github/workflows/build.yml').read_text('utf-8')


def verify(off=OFF,board=BOARD,duty=DUTY,charge=CHARGE,ops=OPS,workflow=WORKFLOW):
    # No two independent job writers for a pawn owned by RHFOffenseLord.
    assert 'TryTakeOrderedJob' not in '\n'.join(line for line in off.splitlines() if not line.lstrip().startswith('//'))
    assert 'class RHFOffenseLordJobS41 : LordJob' in off
    assert 'class RHFOffenseToilS41 : LordToil' in off
    assert 'RHF_OffenseMissionS41' in off and 'RHF_OffenseMissionS41' in duty
    assert '<li Class="RaidersHaveFamilies.RHFOffenseRoleJobGiverS41" />' in duty
    assert '<li Class="RaidersHaveFamilies.RHFOffenseDefenseJobGiverS41" />' in duty
    assert 'MissionFor(Pawn soldier)' in off
    assert 'op.OffenseBoardS41?.For(soldier,false)' in off
    # Only bound operations can commandeer; native siege / fauna remain unchanged.
    assert 'if(lord?.LordJob is LordJob_Siege)return true;' in off
    assert 'RHFNativeDoctrineS41.IsAnimalLord(lord)' in off
    assert 'var visited=new HashSet<Faction>()' in off
    assert 'Key(op,faction,wing)' in off
    assert 'LordMaker.MakeNewLord(faction,' in off
    # All wings plan the SAME pulse, not just the first faction.
    assert 'PlanWing(op,op.Diversion,map,now,2,replan)' in off
    assert 'PlanWing(op,op.Squad,map,now,3,replan)' in off
    assert 'if(replan)\n            {' in off
    assert 'board.NextReplanTick=now+ReplanTicks;' in off
    # Per-pawn breach authority; a shared Breacher pointer would race factions.
    assert 'board.Assign(soldier,RHFRoleS41.Breach,barrier' in off
    assert 'board.Breacher=breacher' not in off
    assert 'order.Role==(int)RHFRoleS41.Breach' in charge
    assert 'operation?.OffenseBoardS41' in charge
    # Player's faction threats only, no omniscient target tracking.
    assert 'op.HuntLastSeenCell=target.Position;' in off
    assert 'GenSight.LineOfSight(observer.Position,target.Position,map)' in off
    assert 'now-op.HuntLastSeenTick<9000' in off
    assert 'SpawnedPawnsInFaction(Faction.OfPlayer)' in off
    assert 'RHFRescueFireDisciplineV136.UnsafeLine' in off
    # Director preserves Lord jobs and faction duty on timeout, not orphan enemies.
    assert 'job.Retreat=true' in off
    assert 'new PawnDuty(DutyDefOf.ExitMapBest)' in off
    assert 'RHFOffenseDirectorS41.End(op,map)' in ops
    assert 'RHFOffenseDirectorS41.Tick(op,map,now)' in ops
    assert 'RHFOffenseDirectorS41.End(op,map); op.Finished = true;' in ops
    assert 'Scribe_Deep.Look(ref OffenseBoardS41, "rhfOffenseBoardS41AG")' in ops
    assert 'if (OffenseBoardS41 == null) OffenseBoardS41' in ops
    assert 'python3 tests/rhf_156_s41_offense_single_writer.py' in workflow

verify()
changes=[
 (OFF,'if(lord?.LordJob is LordJob_Siege)return true;','if(false)return true;'),
 (OFF,'new PawnDuty(DutyDefOf.ExitMapBest)','new PawnDuty(DutyDefOf.Defend)'),
 (OFF,'Key(op,faction,wing)','Key(op,null,wing)'),
 (OFF,'op.OffenseBoardS41?.For(soldier,false)','op.BoardS41?.For(soldier,false)'),
 (OFF,'RHFRescueFireDisciplineV136.UnsafeLine','UnsafeLegacyLine'),
 (OFF,'now-op.HuntLastSeenTick<9000','true'),
 (OFF,'board.Assign(soldier,RHFRoleS41.Breach,barrier','board.Assign(soldier,RHFRoleS41.Hold,barrier'),
 (OFF,'GenSight.LineOfSight(observer.Position,target.Position,map)','true'),
 (OFF,'PlanWing(op,op.Squad,map,now,3,replan)','PlanWing(op,op.Squad,map,now,3,false)'),
 (OFF,'LordMaker.MakeNewLord(faction,','LordMaker.MakeNewLord(Faction.OfPlayer,'),
 (CHARGE,'operation?.OffenseBoardS41','operation?.BoardS41'),
 (OPS,'RHFOffenseDirectorS41.Tick(op,map,now)','RHFTactical.PressureAssault(map,op,now)'),
 (OPS,'Scribe_Deep.Look(ref OffenseBoardS41, "rhfOffenseBoardS41AG")','// state dropped'),
 (DUTY,'RHFOffenseDefenseJobGiverS41','JobGiver_AIFightEnemy'),
]
for source,old,new in changes:
    assert old in source,old
    modified=source.replace(old,new)
    try:
        if source is OFF: verify(off=modified)
        elif source is CHARGE: verify(charge=modified)
        elif source is OPS: verify(ops=modified)
        else: verify(duty=modified)
    except AssertionError:
        continue
    raise AssertionError('Mutation escaped source contract '+old[:70])
print('PASS: S4.1-A/G offense tactical state, native siege/animal preservation, role jobs, save/load; 14 negative mutations')
print('NOT CHECKED: C# compilation, CE runtime, tactical quality or full 18-scenario behavior')
