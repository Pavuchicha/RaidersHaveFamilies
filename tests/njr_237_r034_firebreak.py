"""Two real R033 diagnostic extracts, source contracts, independent policy model.
This does not execute C#, Harmony, CE or native pathfinding."""
import json,collections
from pathlib import Path
R=Path(__file__).resolve().parents[1];S=R/'RaidersHaveFamilies/Source'
a,b=json.loads((R/'tests/fixtures/r034_runtime_extract.json').read_text())
assert a['revision']==b['revision']=='R033' and (a['events'],b['events'])==(9009,4666)
assert len(a['roster'])==len(b['roster'])==6
assert len(a['exits'])==7 and not b['exits']
assert all(x['level']=='Message' for r in (a,b) for x in r['console'])
assert any('release ready=6/6' in x['msg'] for x in b['console'])
assert any(r['ExposedTurretCells']==17 for r in b['routes'])
assert sum(x['job']=='RunForCover' for x in b['jobs'])==17
assert len(b['casts'])==1 and len(b['killed'])==1 and sum(p['downed'] for p in b['final'])==3
assert sum(x['weapon']=='MeleeWeapon_BreachAxe' for x in b['roster'].values())==2
assert sum('Concussion' in x['weapon'] for x in b['roster'].values())==2
# Reconstruct observed native cover -> role-giver Goto loop on the real trace.
loops=[]
for pawn in b['roster']:
 jobs=[x for x in b['jobs'] if str(x['pawn'])==pawn]
 for i,x in enumerate(jobs):
  if x['job']!='RunForCover':continue
  later=next((y for y in jobs[i+1:] if y['job'] not in ('Wait_MaintainPosture',)),None)
  if later and later['job']=='Goto' and later['source']=='RaidersHaveFamilies.RHFRescueRoleJobGiverS41':loops.append((x,later))
assert len(loops)>=8, len(loops)
p=(S/'RHFRescuePlannerS41.cs').read_text();g=(S/'NJRRescueFirebreakR034.cs').read_text();j=(S/'RHFRescueRoleJobGiverS41.cs').read_text();d=(S/'RHFRescueFireDisciplineV136.cs').read_text();r=(S/'NJRRescueRallyR033.cs').read_text();t=(S/'RHFTactical.cs').read_text();diag=(S/'Diagnostics/NJRTestRecorderR030.cs').read_text()
assert p.index('NJRRescueFirebreakR034.TryControl(op,map,board,route,routeGun')<p.index('if(board.Threat!=null && shooters<supportBudget)')
assert 'NJRRescueFirebreakR034.TryControl(op,map,board,accessRoute' in p
assert 'NJRRescueFirebreakR034.IsActive(op)' in r
assert 'NJRRescueFirebreakR034.Controls(op,pawn)' in j and 'NJRRescueFirebreakR034.Controls(op,pawn)' in d
assert 'return NJRRescueFirebreakR034.CombatJob(op,pawn,order)' in j
assert '[HarmonyPatch(typeof(Pawn_JobTracker),"StartJob")]' in g and 'ObserveCover(PawnField.GetValue(__instance) as Pawn,__0)' in g
assert 'e.Recovering.Add(p)' in g and 'e.FailedSlots.Add(e.Slots[p])' in g
assert 'if(e.Recovering.Contains(p) || NativeSuppression(p)==true)' in g and 'return JobFor(JobDefOf.Wait_Combat,p.Position,60)' in g
assert 'e.Recovering.Remove(p)' in g and 'NativeSuppression(p)' in g
assert 'e.PushBroken' not in g and 'e.Regrouping' not in g and 'e.Slots[p]=slot' in g
assert 'regroup before second attempt' not in g and 'op.Phase=2;op.PhaseStartTick=now' in g
assert 'RHFCombatPlannerV125.IsCombatEmergencyJob(p)' in g
assert 'NJRRescueFirebreakR034.DirectGun(candidate)' in t
assert 'Write("debug_window_open"' in diag and 'baseRevision="R060"' in diag
# Historical individual retries remain disallowed while suppressed; R038 permits a new coordinated wave after native recovery.
for cover,retry in loops:
 assert retry['tick']>=cover['tick']
 assert cover['pawn']==retry['pawn']
print(f'PASS R034: {len(loops)} observed cover/retry sequences; normal exit, rally, CE recovery and window-trace contracts. No game execution.')
