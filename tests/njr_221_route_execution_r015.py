"""NJR R015 regression: source-level invariants and independent route reference.
Does NOT compile .NET or execute RimWorld's JobDriver/Pathfinder.
"""
from pathlib import Path
from heapq import heappop, heappush

src = Path(__file__).resolve().parents[1] / 'RaidersHaveFamilies' / 'Source'
def read(name): return (src/name).read_text(encoding='utf-8-sig')
offense = read('RHFOffenseS41.cs')
path = read('NJROffenseRouteS45.cs')
contact = read('NJRCombatContactS44.cs')
combat = read('RHFCombatPlannerV125.cs')
assert 'internal IntVec3 NextStep = IntVec3.Invalid;' in path
assert 'result.NextStep=path.Count>1 ? path[1] : IntVec3.Invalid;' in path
assert 'NJROffensePathS45.Plan(pawn,destination,' in offense
assert 'RememberedGuns(pawn,op)' in offense
assert 'record.FactionKey==key' in offense
assert 'return Task(JobDefOf.Goto,next,90);' in offense
assert 'return Task(JobDefOf.AttackMelee,blocking,600);' in offense
assert 'FollowAssessedRoute(pawn,op,cell)' in offense
assert 'return Task(JobDefOf.Goto,cell,90);' not in offense
assert 'if(!cover.IsValid)cover=breacher.Position;' in offense
assert 'if(!lane.IsValid)lane=staging;' in offense
assert 'soldier.WorkTagIsDisabled(WorkTags.Violent)' in contact
assert 'pawn.WorkTagIsDisabled(WorkTags.Violent)' in offense
assert 'RHFCombatPlannerV125.RequiresBreach(destinationEdifice)' in path
assert 'goal.GetEdifice(soldier.Map)!=null' not in path
assert '(occupant!=null && occupant!=soldier)' in path
assert 'if(edifice is Building_Door door && door.Open)return 10;' in path
assert '(!goal.Standable(soldier.Map) && !openedDoor)' in path
assert '(!next.Standable(map) && !openDoor)' in offense
assert 'RHFCombatPlannerV125.RequiresBreach(barrier)' in contact
assert 'RHFCombatPlannerV125.IsBreachableStructure(barrier)' not in contact
# The old report incorrectly suspected missing ownership checks; the
# underlying IsTurret routine ALREADY rejects non-player guns.
assert 'building.Faction != Faction.OfPlayer' in combat

# Independent 4-neighbour risk-first search (algorithm reference, NOT C#).
# The central direct corridor is under a gun; a longer route must win.
start=(0,2);goal=(6,2);w,h=7,5; exposed={(3,2)}
def route(avoid_guns):
    frontier=[(0,start)]; cost={start:0}; parent={}
    while frontier:
        c,at=heappop(frontier)
        if c!=cost[at]: continue
        if at==goal:
            out=[at]
            while at!=start: at=parent[at];out.append(at)
            return list(reversed(out))
        x,z=at
        for nx,nz in [(x+1,z),(x-1,z),(x,z+1),(x,z-1)]:
            if not 0<=nx<w or not 0<=nz<h or (avoid_guns and (nx,nz) in exposed):continue
            nxt=(nx,nz); nc=c+10+(12000 if nxt in exposed else 0)
            if nc<cost.get(nxt,10**30):cost[nxt]=nc;parent[nxt]=at;heappush(frontier,(nc,nxt))
    return None
safe=route(True)
assert safe is not None and len(safe)>7
assert not exposed.intersection(safe)
assert safe[1] in {(0,1),(0,3)}
assert all(abs(x2-x1)+abs(z2-z1)==1 for (x1,z1),(x2,z2) in zip(safe,safe[1:]))
print('PASS R015 source contracts and separate safe-route model; DLL/JobDriver still unverified')
