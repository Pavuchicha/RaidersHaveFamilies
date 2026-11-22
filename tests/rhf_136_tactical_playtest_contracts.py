"""S2 tactical rescue contracts with negative mutations; not a RimWorld gameplay test."""
from pathlib import Path
import re, xml.etree.ElementTree as ET
R=Path(__file__).resolve().parents[1]
S=R/'RaidersHaveFamilies'/'Source'
route=(S/'RHFObjectiveRouteV128.cs').read_text('utf8')
mission=(S/'RHFRescueCommandV124.cs').read_text('utf8')
combat=(S/'RHFCombatPlannerV125.cs').read_text('utf8')
ops=(S/'RHFOperations.cs').read_text('utf8')
safety=(S/'RHFRescueFireDisciplineV136.cs').read_text('utf8')
xml=(R/'RaidersHaveFamilies/Defs/DutyDefs/RHF_MissionHold.xml').read_text('utf8')
wf=(R/'.github/workflows/build.yml').read_text('utf8')

def holds_route(x):
    assert 'int maxLeg = 13 + (soldier.thingIDNumber & 3) * 2;' in x
    assert 'i<=nearest+maxLeg' in x
    assert 'if(nextStep!=step)break;' in x
    assert 'int limit=SafeAdvanceLimit(route)' in x
    assert 'CrossesKnownFire(route,soldier.Position,destination,map)' in x
    assert 'i<limit-3' in x and 'destination+side' in x

def holds_ce(m,c):
    assert 'if (RHFCombatPlannerV125.IsCombatEmergencyJob(p)) return false;' in m
    assert 'if (IsCombatEmergencyJob(pawn)) return false;' in c
    assert 'state.Engaged.Add(fighters[i])' in c
    for token in ('"Suppress"','"TakeCover"','"RunForCover"','"SeekCover"','"Flee"','"Reload"'):
        assert token in c

def holds_hostages(s,x,c):
    ET.fromstring(x)
    assert 'Class="RaidersHaveFamilies.RHFJobGiverMissionDefenseV136"' in x
    assert 'Class="JobGiver_AIFightEnemies"' not in x
    assert 'RescueFor(pawn)' in s
    assert 'if(target is Pawn attacked && IsProtected(op,attacked))return true;' in s
    for token in ('op.Squad','op.Diversion','op.Targets','op.Freed'):
        assert token in s
    assert 'RHFRescueFireDisciplineV136.UnsafeLine(op,pawn,enemy)' in c
    assert 'RHFRescueFireDisciplineV136.UnsafeLine(op,pawn,defender)' in s
    assert 'map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer)' in s
    assert 'RHFCombatPlannerV125.IsTurret(turret)' in s

def holds_drop(o):
    assert 'private void RecoverLateDropRescuers(RHFOperation op,int now)' in o
    assert 'recorded.UnionWith(presentBeforeRaid);' in o
    assert 'before.Contains(pawn.thingIDNumber)' in o
    assert 'RecoverLateDropRescuers(op,now);' in o
    assert 'other.Squad!=null && other.Squad.Contains(pawn)' in o
    assert 'other.Diversion!=null && other.Diversion.Contains(pawn)' in o

def holds_stalemate(m,c):
    assert '(needsFireSuppression ? 7200 : 24000)' in m
    assert '!needsFireSuppression && now-moved<=Step' in m
    assert 'RHFCombatPlannerV125.TurretDamageProgress(op)' in m
    assert 'enemy.HitPoints<previous' in c

holds_route(route);holds_ce(mission,combat);holds_hostages(safety,xml,combat)
holds_drop(ops);holds_stalemate(mission,combat)
assert 'python3 tests/rhf_136_tactical_playtest_contracts.py' in wf

# Each core contract must reject a one-point reversal of the corresponding bug.
for fn, args in [
    (holds_route,(route.replace('i<=nearest+maxLeg','i<=nearest+5',1),)),
    (lambda x:holds_ce(x,combat),(mission.replace('if (RHFCombatPlannerV125.IsCombatEmergencyJob(p)) return false;','',1),)),
    (lambda x:holds_hostages(safety,x,combat),(xml.replace('RaidersHaveFamilies.RHFJobGiverMissionDefenseV136','JobGiver_AIFightEnemies',1),)),
    (holds_drop,(ops.replace('RecoverLateDropRescuers(op,now);','',1),)),
    (lambda x:holds_stalemate(x,combat),(mission.replace('(needsFireSuppression ? 7200 : 24000)','24000',1),)),
]:
    try:fn(*args)
    except AssertionError:pass
    else:raise AssertionError('negative mutation incorrectly passed')
print('PASS: long bounded movement, cautious 3-lane formation, CE suppression isolation, hostage-safe mission targets, recovered drop pawns, finite turret stalemate; 5 negative mutations')
