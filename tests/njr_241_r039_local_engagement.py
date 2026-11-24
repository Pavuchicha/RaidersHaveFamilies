"""R038 observed trace + R039 local coordination policy, not C# execution."""
from pathlib import Path
import json
R=Path(__file__).resolve().parents[1];S=R/'RaidersHaveFamilies/Source'
a=json.loads((R/'tests/fixtures/r039_runtime_extract.json').read_text());assert a['revision']=='R038' and a['events']==8986
assert len(a['roster'])==6 and sum(p['weapon'].startswith('Gun_') for p in a['roster'].values())==5
m=a['messages'];assert any(x['tick']==45328 and 'ready=5/6' in x['msg'] for x in m)
assert any(x['tick']==45598 and 'regroup; suppressed=3/5' in x['msg'] for x in m)
assert sum(x['tick']==45868 and 'recovered:' in x['msg'] for x in m)==3
assert any(x['tick']==48028 and 'abort; mobile=6 guns=5' in x['msg'] for x in m)
assert len([d for d in a['damage'] if d['tick']>48028 and d['instigator']==36739])==7
s=next(x for x in a['samples'] if x['tick']==46148)
assert sum(p['job']=='Wait_Combat' for p in s['pawns'])==5
assert next(p for p in s['pawns'] if p['id']==36739)['pos']==[148,110]
# Exercise local group decisions under recorded distribution, not unknown
# historical CE firing feasibility. Recovery does not wait for distant member.
def move(local_ready,local_guns,proposed,covering,elapsed):
    return local_ready>=2 and local_ready*2>=local_guns and elapsed>=600 and (proposed>=2 or (proposed>0 and covering>0))
assert move(4,4,3,0,600)
assert move(3,4,1,1,600)
assert not move(1,4,1,0,600)
assert not move(4,4,1,0,600)
assert not move(4,4,3,0,599)
assert not move(1,1,1,1,600)
def order(recovering,can_fire,local,step):
    if recovering:return 'cover'
    if can_fire:return 'fire'
    if not local:return 'safe approach' if step else 'hold'
    return 'local move' if step else 'hold'
assert order(False,True,False,False)=='fire'
assert order(False,True,True,False)=='fire'
assert order(True,True,True,True)=='cover'
assert order(False,False,False,True)=='safe approach'
g=(S/'NJRRescueFirebreakR034.cs').read_text();r=(S/'NJRRescueRallyR033.cs').read_text();o=(S/'NJRObservedTurretCastR037.cs').read_text();f=(S/'RHFRescueFireDisciplineV136.cs').read_text()
assert 'ready==fighters.Count' not in g and 'Regrouping' not in g and 'PushBroken' not in g
assert 'localReady>=2 && localReady*2>=localGuns' in g and 'proposed.Count>=2 || (proposed.Count>0 && localFire>0)' in g
assert 'e.Approach.Waypoint(p,occupied)' in g and 'if(!Local(p,e))continue;' in g
assert 'NJRRescueRallyR033.Anchor(op)' in g and 'internal static IntVec3 Anchor' in r
assert 'ObserveIncoming(pawn,turret' in o and 'LastIncoming(p)' in g
assert 'UnsafeLineFrom(op,p,gun,c)' in g and 'NativeCanFire(p,gun)' in g
assert 'e.LastMove.Remove(p);e.LastSearch.Remove(p);e.FireTargets.Remove(p)' in g
assert 'if(shooting && (c==p.Position ||' in g
assert 'e.TargetHP[target]=hp' in g and 'now-e.Progress>=7200' in g
assert 'Thing target=FireTarget(p,op,e);' in g and 'return JobFor(JobDefOf.AttackStatic,target,360)' in g
assert 'UnsafeLineFrom(op,shooter,target,shooter.Position)' in f
print('PASS R039: 8986 recorded events, 5/6 rally, 3 recovered, 5 idle, 7 late hits; local coordinated movement and fire policy model. No game execution.')
