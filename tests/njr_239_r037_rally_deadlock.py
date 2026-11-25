"""R036 observed deadlock; replay recorded terrain with an independent, resumable
cardinal flood model. Source contracts are not C# execution or CE LOS tests."""
from pathlib import Path
from collections import deque
import json, math
R=Path(__file__).resolve().parents[1];S=R/'RaidersHaveFamilies/Source'
a=json.loads((R/'tests/fixtures/r037_runtime_extract.json').read_text())
assert a['revision']=='R036' and a['events']==10561
assert len(a['roster'])==7 and sum(w.startswith('Gun_') for w in a['roster'].values())==6
assert len(a['members'])==6 and all(p['job']=='Wait' for p in a['members'])
assert a['final_tick']-a['last_progress_tick']>9000
anchor=tuple(a['rally']); far=[tuple(p['pos']) for p in a['members'] if math.dist(p['pos'],anchor)>6]
assert len(far)==3 and 'ready=3/6' in a['decision']
assert len(a['routes'][0]['cells'])>len(a['routes'][1]['cells'])*3
assert all(tuple(p) not in {tuple(c) for c in a['routes'][1]['cells']} for p in far)
assert len(a['casts'])==20 and a['raider_casts']==0
assert 43<max(math.dist(c['origin'],c['cell']) for c in a['casts'])<44
assert len(a['windows'])==1 and 'UIRootOnGUI' in a['windows'][0]['stack']
assert a['console_levels']=={'Message':7}
# Replay actual recorded walkability; LOS is deliberately not inferred from walkability.
# Test hazard exclusion separately with a synthetic firing-zone mask.
b=a['map_crop']['bounds'];walk={(x+b[0],z+b[1]) for z,row in enumerate(a['map_crop']['walk_rows']) for x,c in enumerate(row) if c=='.'}
D=((1,0),(-1,0),(0,1),(0,-1))
def field(start,walk,unsafe,budget,targets):
    dist={start:0};q=deque([start]);pulses=0
    while q and not all(c in dist for c in targets):
        pulses+=1
        for _ in range(min(budget,len(q)+budget)):
            if not q:break
            x,y=q.popleft()
            for dx,dy in D:
                c=(x+dx,y+dy)
                if c in walk and c not in unsafe and c not in dist:
                    dist[c]=dist[x,y]+1;q.append(c)
    return dist,pulses
f,pulses=field(anchor,walk,set(),200,far)
assert pulses>1 and all(c in f for c in far)
assert all(f[c]>=math.dist(c,anchor) for c in far)
for c in far:
    original=f[c]
    for _ in range(original):
        c=next((c[0]+dx,c[1]+dy) for dx,dy in D if f.get((c[0]+dx,c[1]+dy))==f[c]-1)
    assert c==anchor
# U-shaped wall / synthetic hazard: progress requires moving geometrically away
# before coming back around the corner; no unsafe tile is admitted.
box={(x,y) for x in range(15) for y in range(15)};danger={(7,y) for y in range(1,15)};goal=(10,10);origin=(5,10)
f,p=field(goal,box,danger,13,[origin]);assert p>1 and origin in f and f[origin]>math.dist(origin,goal)
assert not set(f)&danger
assert any(f.get((origin[0]+dx,origin[1]+dy),999)<f[origin] for dx,dy in D)
# No quorum cannot wait forever, even when no route exists; quorum still required
# for a coordinated timed release. Incoming fire shortens stagnant wait.
def outcome(ready,total,stalled,pending,progress,incoming,first_ready_elapsed):
    quorum=ready>=2 and ready*3>=total*2
    if ready==total or (first_ready_elapsed>=1800 and not progress and quorum):return 'release'
    if (stalled>=900 and incoming and not progress and not quorum) or (stalled>=1800 and not progress and not pending) or stalled>=3600:return 'withdraw'
    return 'gather'
assert outcome(3,6,900,True,False,True,1800)=='withdraw'
assert outcome(3,6,1800,False,False,False,1800)=='withdraw'
assert outcome(3,6,1800,True,False,False,1800)=='gather'
assert outcome(3,6,3600,True,False,False,3600)=='withdraw'
assert outcome(4,6,1800,False,False,False,1800)=='release'
assert outcome(3,6,700,True,True,True,1200)=='gather'
g=(S/'NJRRescueRallyR033.cs').read_text();r=(S/'NJRRallyFieldR037.cs').read_text();t=(S/'RHFObjectiveRouteV128.cs').read_text();o=(S/'NJRObservedTurretCastR037.cs').read_text();d=(S/'Diagnostics/NJRTestRecorderR030.cs').read_text()
assert 's.Field=new NJRRallyFieldR037(map,s.Anchor,now)' in g and 's.Field.Waypoint(p,used)' in g
assert 'n<1600 && frontier.Count>0' in r and 'frontier.Enqueue(next)' in r and 'exposed(next)' in r
assert 'distance.TryGetValue(c,out int remaining)' in r and 'i<8' in r and 'p.CanReach(candidates[i]' in r
assert 'stalled>=900 && incoming>0 && !laggardProgress && !quorum' in g and 'stalled>=3600' in g
assert 'Math.Max(range,cached.ObservedRange)' in t and 'TurretRangeR037(turret)' in t and 'ObserveTurretRangeR037(turret,pawn.Position)' in o
assert 'if(!__result' in o and 'ref bool' not in o
assert 'engine_log_snapshot' in d and 'raw_log_snapshot' in d and 'CopyPlayerLog("console")' in d and 'CopyPlayerLog("export")' in d and 'baseRevision="R046"' in d
print('PASS R037: R036 trace (actual roster/stall/20 incoming casts/console), recorded terrain approach, synthetic hazard and timeout policy, source contracts. Not C# or RimWorld/CE execution.')
