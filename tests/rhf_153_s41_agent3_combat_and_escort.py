"""S4.1-A/D agent A audit regressions. STATIC ONLY; NOT a runtime PASS."""
from pathlib import Path
import xml.etree.ElementTree as ET
R=Path(__file__).resolve().parents[1]
S=R/'RaidersHaveFamilies'/'Source'
P=(S/'RHFRescuePlannerS41.cs').read_text('utf-8')
G=(S/'RHFRescueRoleJobGiverS41.cs').read_text('utf-8')
B=(S/'RHFTacticalBoardS41.cs').read_text('utf-8')
J=(S/'JobDriver_RHFPlantBreachChargeS41.cs').read_text('utf-8')
W=(R/'.github/workflows/build.yml').read_text('utf-8')
X=(R/'RaidersHaveFamilies/Defs/JobDefs/RHF_BreachChargeS41.xml').read_text('utf-8')

def test(p,g,b,j,w,x):
    # No escort may be selected as sapper; change owner after casualty.
    assert 'escortedDuringAssault.Contains(board.Breacher)' in p
    assert 'obstacle,escortedDuringAssault);' in p
    # This mechanic was rejected after gameplay: no spawned toolkit,
    # no custom instant-damage JobDriver. The normal AttackMelee job
    # applies vanilla/CE weapon damage and swing timing.
    assert 'RHF_PlantBreachChargeS41' not in g
    assert 'return Task(strike,obstacle,900);' in g
    assert 'JobDefOf.AttackMelee' in g
    assert 'GenExplosion' not in j and 'DamageInfo' not in j
    assert len(ET.fromstring(x))==0
    # A turret firebreak must reserve a real ranged team, and the breacher
    # may not run past an active firebreak before it is cleared.
    assert 'firebreakBeforeBreach' in p
    assert 'obstacle != null && !firebreakBeforeBreach' in p
    assert 'FirstRouteTurret(route,map)' in p
    assert 'RHFRoleS41.SuppressTurret' in p and 'RHFRoleS41.SuppressTurret' in g
    assert 'Thing routeGun=FirstRouteTurret(route,map);' in p
    assert 'route.FirstTurretExposureIndex<=route.FirstBarrierIndex) ? routeGun : null;' in p
    assert 'Scribe_References.Look(ref Threat' in b
    assert 'LastThreatHP' in b and 'board.Threat.HitPoints<board.LastThreatHP' in p
    # No combat role's Hold may suppress native/local self-defense.
    assert 'return op.Freed!=null && op.Freed.Contains(pawn) ?' in g
    assert 'Task(JobDefOf.Wait,pawn.Position,90) : null' in g
    # Synchronised escort: tight range + short path job + no VIP-first ExitMap.
    assert 'if(separation>9)' in g
    assert 'if(gap>16)' not in g
    assert 'paired?10:0' in g
    assert 'pawn.CanReach(order.Cell,PathEndMode.OnCell,Danger.Deadly)' in g
    assert 'return Task(JobDefOf.Goto,order.Cell,240)' in g
    assert 'RHFRescueCommandV124.MissionExit(escort);' in p
    assert p.index('RHFRescueCommandV124.MissionExit(escort);') < p.index('RHFRescueCommandV124.MissionExit(rescued);')
    assert 'pair.Escort=null;' in p
    # No foreign-room screen position; no 90-tick wall-destruction storm.
    assert 'cell.GetRoom(map)!=soldier.Position.GetRoom(map)' in p
    assert 'soldier.CanReach(cell,PathEndMode.OnCell,Danger.Some)' in p
    assert 'cache.Seen.TryGetValue(key,out int seen) && seen==now' in p
    assert 'ConditionalWeakTable<RHFOperation,BarrierEventCache>' in p
    assert 'python3 tests/rhf_153_s41_agent3_combat_and_escort.py' in w
    ET.fromstring(x)

test(P,G,B,J,W,X)
changes=[
 ('p', 'obstacle,escortedDuringAssault);','obstacle,new HashSet<Pawn>());'),
 ('p', 'escortedDuringAssault.Contains(board.Breacher)','false'),
 ('g', 'return Task(strike,obstacle,900);','return null;'),
 ('g', 'JobDefOf.AttackMelee','JobDefOf.Wait'),
 ('j', 'namespace RaidersHaveFamilies {}','GenExplosion.DoExplosion'),
 ('p', 'obstacle != null && !firebreakBeforeBreach','obstacle != null'),
 ('p', 'route.FirstTurretExposureIndex<=route.FirstBarrierIndex) ? routeGun : null;', 'route.FirstTurretExposureIndex<=route.FirstBarrierIndex) ? null : null;'),
 ('p', 'RHFRoleS41.SuppressTurret','RHFRoleS41.Screen'),
 ('g', 'Task(JobDefOf.Wait,pawn.Position,90) : null','Task(JobDefOf.Wait,pawn.Position,90) : Task(JobDefOf.Wait,pawn.Position,90)'),
 ('g', 'if(separation>9)','if(separation>81)'),
 ('p', 'RHFRescueCommandV124.MissionExit(escort);','RHFRescueCommandV124.MissionExit(rescued);'),
 ('p', 'cell.GetRoom(map)!=soldier.Position.GetRoom(map)','false'),
 ('p', 'cache.Seen.TryGetValue(key,out int seen) && seen==now','false'),
 ('w', 'python3 tests/rhf_153_s41_agent3_combat_and_escort.py',''),
]
orig={'p':P,'g':G,'b':B,'j':J,'w':W,'x':X}
for i,(k,before,after) in enumerate(changes,1):
    assert before in orig[k],(i,before)
    data=dict(orig);data[k]=data[k].replace(before,after)
    try:test(data['p'],data['g'],data['b'],data['j'],data['w'],data['x'])
    except AssertionError:pass
    else:raise AssertionError(f'Negative mutation {i} missed')
print(f'PASS: S4.1-A/D static checks; {len(changes)} negative mutations; no gameplay claim')
