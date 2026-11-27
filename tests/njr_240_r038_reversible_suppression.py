"""R037 actual event regression + independently executed recovery-policy model.
This is not C# execution, CE ballistics, or a guarantee of victory."""
from pathlib import Path
from collections import Counter
import json
R=Path(__file__).resolve().parents[1];S=R/'RaidersHaveFamilies/Source'
a=json.loads((R/'tests/fixtures/r038_runtime_extract.json').read_text())
assert a['revision']=='R037' and a['events']==6338
assert 'ready=8/8' in a['release']['msg'] and len(a['cover'])==7
assert len(a['raider_casts'])==8 and len(a['turret_casts'])==4 and a['target_hp']==100
assert 'mobile=7 available=1 noDamageTicks=1260' in a['abort']['msg']
assert len(a['samples'][0]['pawns'])==8
roster=Counter(p['weapon'] for p in a['samples'][0]['pawns'])
assert roster['Gun_Revolver']==4 and roster['MeleeWeapon_BreachAxe']==1
frozen=[s for s in a['samples'] if sum(p['pos']==[101,141] and p['job']=='Wait_Combat' for p in s['pawns'])==6]
assert len(frozen)==9 and frozen[-1]['tick']-frozen[0]['tick']==960
assert not a['errors'] and a['old_think_warnings']==18 and len(a['warnings'])==8
# Native CE flag is reversible; a past RunForCover does not indefinitely
# suppress our order issuance. Model inputs are hypothetical CE state, not
# measured suppression fields (R037 didn't record those).
def recover(native_flag,cover_job,last_cover,now):
    return native_flag is not True and not cover_job and now-last_cover>=(180 if native_flag is False else 480)
assert not recover(True,False,0,3000)
assert not recover(False,True,0,3000)
assert not recover(False,False,0,179) and recover(False,False,0,180)
assert not recover(None,False,0,479) and recover(None,False,0,480)
# Replay the recorded time of the old failure with seven mobile fighters.
# Unlike the old latch, hypothetical cleared native flags admit regroup;
# don't claim the old recorded flags are known.
def decision(mobile,initial,guns,regroup,waves,ready,regroup_elapsed,wave_idle,total_idle,second_line):
    if mobile*2<=initial or guns==0 or (regroup and regroup_elapsed>=2400) or (not regroup and wave_idle>=3000) or total_idle>=7200 or (waves>1 and not regroup and not second_line):return 'withdraw'
    if regroup and waves==1 and ready==mobile and ready>=2 and regroup_elapsed>=600:return 'retry'
    return 'regroup' if regroup else 'fire'
assert decision(7,8,6,True,1,0,1260,1260,1260,True)=='regroup'
assert decision(7,8,6,True,1,7,1260,1260,1260,True)=='retry'
assert decision(7,8,6,True,1,6,1260,1260,1260,True)=='regroup'
assert decision(7,8,6,True,2,7,1260,1260,1260,True)=='regroup'
assert decision(7,8,6,True,2,7,2400,2400,5000,True)=='withdraw'
assert decision(7,8,6,False,2,7,0,0,1500,False)=='withdraw'
assert decision(4,8,3,False,1,0,0,100,100,True)=='withdraw'
# No two members counted ready on the same allocated cell.
assert len(set(tuple(p['pos']) for p in frozen[-1]['pawns'] if not p['downed']))==2
# Source integration guards: CE state read only; unique recovery, safe regroup,
# no automatic solo exposed re-entry, bounded retry, weapon quality budget.
g=(S/'NJRRescueFirebreakR034.cs').read_text();c=(S/'RHFCombatPlannerV125.cs').read_text();f=(S/'NJRForceStrengthR017.cs').read_text();d=(S/'Diagnostics/NJRTestRecorderR030.cs').read_text()
assert 'CombatExtended.CompSuppressable' in g and 'AccessTools.Field(comp.GetType(),"isSuppressed")' in g and '.SetValue(' not in g
assert 'e.Recovering.Remove(p);e.LastCover.Remove(p);' in g
assert 'now-tick<(suppressed==false?180:480)' in g
assert '!occupied.Contains(slot)' in g and 'occupied.Add(slot)' in g
assert 'localReady>=2 && localReady*2>=localGuns && now-e.LastGroupMove>=600' in g
assert 'e.FailedSlots' in g and '(c-failed).LengthHorizontalSquared<=4' in g
assert 'now-e.RegroupStart' not in g and 'now-e.Progress>=7200' in g
assert 'e.PushBroken' not in g and 'e.FireTargets[p]=target;TrackTarget(e,target);e.Slots[p]=p.Position' in g
assert 'e.FailedSlots.Add(e.Slots[p]);' in g and 'RecoveredPosture(pawn)' in c
assert 'if(d<16)score+=' in g and 'cross*cross<=4L*len' in g and 'range*0.8f-distance' in g
assert 'desiredSupport=(desired+1)/2' in f and 'pass<2 && attempts<24' in f and 'kind.combatPower<=cap+remaining' in f
assert 'WeaponRange(p)<24f' in f and 'name.IndexOf("Revolver"' in f and 'if(pass==1 && !longGun)' in f
assert 'baseRevision="R057"' in d
print('PASS R038: real 6338-event trace, 8 distinct raider cast starts, 7 cover entries, nine frozen snapshots; reversible recovery and bounded retry policy model. No C#/CE execution.')
