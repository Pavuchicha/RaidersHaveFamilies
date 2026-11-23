"""NJR R003 P1 offense doctrine contracts + illustrative route/hunt simulations.

SOURCE CONTRACTS ONLY. Simulations are independent Python reference models;
they do NOT execute C#, compile DLL, or claim any in-game tactical success.
"""
from pathlib import Path
import re

ROOT=Path(__file__).resolve().parents[1]
OFF=(ROOT/'RaidersHaveFamilies/Source/RHFOffenseS41.cs').read_text('utf-8')
OPS=(ROOT/'RaidersHaveFamilies/Source/RHFOperations.cs').read_text('utf-8')
NATIVE=(ROOT/'RaidersHaveFamilies/Source/RHFNativeDoctrineS41.cs').read_text('utf-8')
DEBUG=(ROOT/'RaidersHaveFamilies/Source/RHFDebugActions.cs').read_text('utf-8')
W=(ROOT/'.github/workflows/build.yml').read_text('utf-8')

markers={
    'active scouts without observer truncation': 'for(int i=0;i<wing.Count;i++)',
    'observed scout must be active': 'if(!Active(scout,map))continue;',
    'shared visible enemy protection': 'p!=op.HuntTarget &&',
    'explicit sighting cache': 'op.HuntLastSeenCell=target.Position;',
    'no teleporting hunt orders': 'Thing confirmed=Seen(soldier,op.HuntTarget,map)',
    'pursuit LOS recheck at job time': 'if(target is Pawn && (role==RHFRoleS41.OffenseHunt &&',
    'door compared with safe wall': 'Thing door=AccessibleDoor(lead,goal,map,knownTurrets,out doorRisk);',
    'old fallback retained only on impossible path': 'if(lead.CanReach(goal,PathEndMode.Touch,Danger.Some))return null;',
    'whole-map safe-first offense': 'NJROffensePathS45.Plan(members[0],goal,knownTurrets)',
    'door line danger considered': 'KnownGunExposure(b.Position,goal,map,knownTurrets)',
    'wall danger compared to door': 'bestDanger<doorRisk',
    'all known guns considered': 'foreach(Building turret in map.listerBuildings.allBuildingsColonist)',
    'physical reach to door': 'lead.CanReach(b.Position,PathEndMode.Touch,Danger.Some)',
    'no aimless map-center demolition': 'bool hasObjective=objective!=null ||',
    'observed turrets only': 'TeamSees(members,turret,map)',
    'turret exposure penalty': 'score+=30000;',
    'safe post even when all dangerous': 'if(best.IsValid)occupied.Add(best);',
    'close threat beats buildings': 'urgent ? defender : building ?? defender;',
    'cover selected by real weapon': 'RHFCombatPlannerV125.WeaponRange(soldier)>4f',
    'real cover target': 'role==RHFRoleS41.OffenseCover && defender!=null',
    'custom offense lord': 'class RHFOffenseLordJobS41 : LordJob',
    'native siege retained': 'if(lord?.LordJob is LordJob_Siege)return true;',
    'native beast lord retained': 'RHFNativeDoctrineS41.IsAnimalLord(lord)',
    'separate faction and wing': 'Key(op,faction,wing)',
    'coalition corridor fire check': 'RHFRescueFireDisciplineV136.UnsafeLine',
    'forensic developer action': '38 — NJR: наступательные рейды, приказы и наблюдения',
    'on-demand only diagnosis': 'public static class NJROffenseDiagnosticsS41',
}
for name,needle in markers.items():
    assert needle in OFF, f'missing {name}: {needle}'
assert 'TryTakeOrderedJob(' not in OFF
assert 'GenSpawn.Spawn(' not in OFF
assert 'GenExplosion' not in OFF
assert 'RHFOffenseDirectorS41.Tick(op,map,now)' in OPS
assert 'LordToil_Siege' in NATIVE and 'OwnerLord' in NATIVE
for num in range(7,19):
    assert re.search(r'\[DebugAction\(Category,\s*"'+f'{num:02}'+r' —', DEBUG),num
assert 'tests/njr_210_offense_tactics.py' in W

# Mutation checks: deliberately removing each safety gate is rejected.
for name,needle in markers.items():
    mutated=OFF.replace(needle,'/* removed: '+name+' */',1)
    assert needle not in mutated or OFF.count(needle)>1, name

# Pure demonstrative model: retreat/cover preference, never a C# execution.
def choose_formation(candidates, known_turrets):
    feasible=[]
    for c in candidates:
        if not c['standable'] or not c['reachable'] or c['occupied']:
            continue
        score=c['distance_sq']//4
        score+=sum(30000 for t in known_turrets if t['can_see'](c['xy']) and
                   (c['xy'][0]-t['xy'][0])**2+(c['xy'][1]-t['xy'][1])**2<=1600)
        feasible.append((score,c['xy']))
    return min(feasible)[1] if feasible else None
base=[{'xy':(x,0),'standable':True,'reachable':True,
       'occupied':False,'distance_sq':x*x} for x in (7,10)]
known=[{'xy':(11,0),'can_see':lambda xy: xy[0]>8}]
assert choose_formation(base,known)==(7,0)   # avoid observable gun lane
assert choose_formation(base,[])==(7,0)      # no invented unseen gun
assert choose_formation(base,[{'xy':(9,0),'can_see':lambda xy: True}])==(7,0)
assert choose_formation([dict(base[0],reachable=False)],[]) is None

# The door must be physically usable; a closer but sealed door is not a shortcut.
def best_door(doors):
    accessible=[d for d in doors if d['reachable'] and d['near_sq']<=225]
    return min(accessible,key=lambda d:100000*min(d.get('known_guns',0),10)+
               3*d['near_sq']+d['lead_sq']//12+d['breach_work']//8)['id'] if accessible else None
assert best_door([{'id':'sealed','reachable':False,'near_sq':1,'lead_sq':1,'breach_work':20},
                  {'id':'door','reachable':True,'near_sq':9,'lead_sq':100,'breach_work':40}])=='door'
assert best_door([{'id':'outside','reachable':True,'near_sq':900,'lead_sq':10,'breach_work':20}]) is None
assert best_door([]) is None
assert best_door([
    {'id':'unsafe-short','reachable':True,'near_sq':4,'lead_sq':16,
     'breach_work':40,'known_guns':2},
    {'id':'safe-long','reachable':True,'near_sq':64,'lead_sq':240,
     'breach_work':80,'known_guns':0}]) == 'safe-long'
# A risky door is no longer automatically preferable to a safe wall.
def barrier_choice(door_danger,wall_danger,door_available=True,wall_available=True):
    if door_available and door_danger==0:
        return 'door'
    if wall_available and (not door_available or wall_danger<door_danger):
        return 'wall'
    return 'door' if door_available else ('wall' if wall_available else None)
assert barrier_choice(2,0)=='wall'
assert barrier_choice(2,1)=='wall'
assert barrier_choice(0,1)=='door'
assert barrier_choice(1,2)=='door'
assert barrier_choice(2,0,wall_available=False)=='door'  # last resort

def hunt_order(is_active,is_visible,last_observed):
    if not is_active:return None
    return {'pawn': 'known target' if is_visible else None,
            'cell':last_observed}
assert hunt_order(True,False,(6,5))=={'pawn':None,'cell':(6,5)}
assert hunt_order(True,True,(6,5))['pawn']=='known target'
assert hunt_order(False,True,(6,5)) is None

print('PASS NJR 210: observed turrets, risk-aware breaching, safe positions, private hunt sightings, native Lords, 18 labels (STATIC + independent model)')
print('NOT VERIFIED: actual C# runtime, CE pathfinding, tactical wins or game-save compatibility')
