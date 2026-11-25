"""Real R034 trace facts + revised source contracts, not gameplay tests."""
import json, math
from pathlib import Path
r=Path(__file__).resolve().parents[1];s=r/'RaidersHaveFamilies/Source'
a=json.loads((r/'tests/fixtures/r035_runtime_extract.json').read_text())
assert a['revision']=='R034' and a['events']==8709
assert len(a['roster'])==7 and sum(w.startswith('Gun_') for w in a['roster'].values())==3
assert sum(w=='MeleeWeapon_BreachAxe' for w in a['roster'].values())==2
assert len(a['casts'])==2
wave=[x for x in a['console'] if 'commit members=' in x['msg']]
regroup=next(x for x in a['console'] if 'regroup before second attempt' in x['msg'])
assert len(wave)==2 and wave[1]['tick']-regroup['tick']==90
assert sum(x['job']=='RunForCover' for x in a['jobs'])==9
force=(s/'NJRForceStrengthR017.cs').read_text();combat=(s/'NJRRescueFirebreakR034.cs').read_text();tactics=(s/'RHFTactical.cs').read_text();emergency=(s/'RHFCombatPlannerV125.cs').read_text()
assert 'if(humans>=minimum && (!requiresEngineer || hasEngineer))\n            {BalanceGroundRescue(op,party,pawns);PreparePodRosterR041(op,party,pawns);return;}' in force
assert 'kind.combatPower<=cap+remaining' in force and 'remaining+cap-candidate.kindDef.combatPower' in force
assert 'using RimWorld.Planet;' in force
assert 'original.IsWorldPawn()' in force and 'PawnDiscardDecideMode.KeepForever' in force
assert 'if(!NJRRescueFirebreakR034.DirectGun(candidate))' in force
assert 'party.Role!=(int)RHFStrikeRole.Rescue' in force
assert 'op.Squad.Count>=12?2:1' in tactics
assert '!NJRRescueFirebreakR034.DirectGun(pawn)' in tactics
assert 'Position(p,map,op,e.Gun,e.Shelter,occupied,false,true,true)' in combat
assert 'regroup before second attempt' not in combat and 'e.Recovering.Contains(p)' in combat
assert 'localReady>=2 && localReady*2>=localGuns' in combat and 'e.Recovering.Remove(p)' in combat
assert 'MaintainPosture' in emergency
# replay actual roster and native cover sequence under the policy; no automatic retry
shooters={int(p) for p,w in a['roster'].items() if w.startswith('Gun_')}
recovering=set(); broken=False
for job in a['jobs']:
 if job['tick']<wave[0]['tick'] or job['tick']>=wave[1]['tick']:continue
 if job['job']=='RunForCover':
  recovering.add(job['pawn']);broken |= len(recovering&shooters)*2>=len(shooters) or len(recovering)*2>=len(a['roster'])
assert broken
assert len(shooters)<math.ceil(len(a['roster'])*.75)==6
print('PASS R035: real roster, suppression and premature retry trace; source policy checks; no runtime proof')
