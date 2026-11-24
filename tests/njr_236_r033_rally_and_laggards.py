"""R032 runtime trace facts plus R033 source constraints and policy models.
Does not execute RimWorld's C# or prove a native diagonal attack succeeds.
"""
import json
from pathlib import Path
R=Path(__file__).resolve().parents[1]; S=R/'RaidersHaveFamilies/Source'
a=json.loads((R/'tests/fixtures/r033_runtime_extract.json').read_text())
assert [x['revision'] for x in a]==['R032']*3
assert [x['events'] for x in a]==[9918,9513,6450]
for record in a[:2]:
    assert any(m['weapon']=='MeleeWeapon_BreachAxe' and m['initial_stationary_ticks']>=2400 and m['roles'].get('Hold',0) for m in record['members'].values())
    assert all(len(set(b['cell'].__str__() for b in record['breach_assignments'] if b['tick']==tick))==1 for tick in set(b['tick'] for b in record['breach_assignments']))
assert any(r['ExposedTurretCells']==17 for r in a[2]['routes'])
assert sum(m['ever_downed'] for m in a[2]['members'].values())==3
assert a[2]['suppression_assignments'] and not a[2]['breach_assignments']
assert not a[1]['warnings']
p=(S/'RHFRescuePlannerS41.cs').read_text(); f=(S/'NJRRescueFormationR032.cs').read_text(); r=(S/'RHFObjectiveRouteV128.cs').read_text(); j=(S/'RHFRescueRoleJobGiverS41.cs').read_text(); g=(S/'NJRRescueRallyR033.cs').read_text()
assert 'actual.Role!=(int)RHFRoleS41.Breach' in p and 'actor.Position!=actual.Cell' in p
assert 'RecoveryWaypointR033(route,pawn,map,allocated,limit,nearest' in r
assert 'if(!nominal.IsValid || allocated.Contains(nominal))' in r
assert 'Verse.ReachabilityImmediate' in f and 'Verse.AI.ReachabilityImmediate' in f
assert '(!x.Standable(map) && !z.Standable(map))' in f
assert 'breachSlots.Count<maxOperators' in p and 'IsMeleeWeapon!=true)break' not in p
assert 'NJRRescueRallyR033.TryGather(op,map,board,route,routeGun' in p
assert p.index('NJRRescueRallyR033.TryGather(op,map,board,route,routeGun')<p.index('if(board.Threat!=null && shooters<supportBudget)')
assert 'NJRRescueRallyR033.IsGathering(op)' in j
assert 'FirstTurretExposureIndex-1' in r and 'RallyWaypointR033' in g
assert 'ready==fighters.Count || (timed && quorum)' in g and 'ready*3>=fighters.Count*2' in g
assert 'now-s.LastApproach[p]<600' in g and '&& !laggardProgress' in g
assert 's.Released=true' in g and 'if(s.Released)' in g
assert 'Notes.Add(key))Log.Message(message)' in g
assert 'NoteOnce(op,"[NJR][R026][STALL]' in p
# Policy models: a slow team member does not cause a premature rush, and no
# release of a single fighter when the timeout has elapsed.
def release(ready,total,elapsed,exposed=0,laggard_progress=False):
    return ready==total or (elapsed>=1800 and not laggard_progress and ready>=2 and ready*3>=total*2) or exposed==total
assert not release(1,6,0) and not release(1,6,5000)
assert not release(5,6,1799) and release(6,6,0) and release(4,6,1800)
assert not release(4,6,1800,laggard_progress=True)
assert not release(3,6,1800) and release(0,6,0,6)
# Along a flat wall face, each diagonal has one open orthogonal cell.
def diagonal_allowed(x_open,z_open):return x_open or z_open
assert diagonal_allowed(True,False) and diagonal_allowed(False,True) and not diagonal_allowed(False,False)
# Unavailable first leg must have a bounded alternate, with distinct cells.
route=[(i,0) for i in range(20)]; used={(16,0)}
assert next(c for c in reversed(route[:17]) if c not in used)==(15,0)
print('PASS R033: three real R032 trace extracts, recovery/diagonal/rally/quiet-note contracts and policy models; NOT gameplay.')
