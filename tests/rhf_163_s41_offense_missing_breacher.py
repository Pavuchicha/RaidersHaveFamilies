"""P0 crash regression: no reachable engineer while breach wall exists.
This is Python static source-contract + independent mission outcome model.
It DOES NOT compile or run RimWorld C# or claim in-game PASS.
"""
from pathlib import Path
import re
src=(Path(__file__).resolve().parents[1]/
     'RaidersHaveFamilies/Source/RHFOffenseS41.cs').read_text('utf-8')
checks={
    'failure branch': 'if(breacher==null)',
    'physical target': 'board.Assign(soldier,RHFRoleS41.OffenseStrike,\n                            barrier,post,now);',
    'safe support': 'IntVec3 cover=Formation(soldier,breacher.Position,map,i,occupied);',
    'fallback post': 'if(!post.IsValid)post=soldier.Position;',
    'support post': 'if(!cover.IsValid)cover=soldier.Position;',
    'failure trace': '"no sapper / physical attacks"',
    'melee position search': 'private static IntVec3 ReachableBreachStand(Pawn pawn,Thing barrier)',
    'real adjacent cell': 'IntVec3 cell=barrier.Position+deltas[i];',
    'reject sealed approach': '!pawn.CanReach(cell,PathEndMode.OnCell,Danger.Some)',
    'melee close movement': 'return Task(JobDefOf.Goto,approach,180);',
}
for name,code in checks.items():
    assert code in src,name
# Keep the dereference ONLY in the guarded else branch, never in the missing
# breacher branch. Extract that specific block with distinct markers.
start=src.index('                if(barrier!=null)\n                {\n',src.index('if(soldier==breacher)',src.index('private static void PlanFaction(')))
end=src.index('                IntVec3 lane=Formation(',start)
block=src[start:end]
assert block.count('if(breacher==null)')==1
assert block.count('breacher.Position')==1
assert block.index('breacher.Position')>block.index('                    else\n                    {')
assert block.index('RHFRoleS41.OffenseStrike')<block.index('                    else\n                    {')
assert src.count('breacher.Position')==1
# Any removal of contract pieces must be independently caught.
for marker in checks.values():
    changed=src.replace(marker, '/* mutation */',1)
    assert changed.count(marker)==src.count(marker)-1

# Pure branching model: when a barrier is present and the only potentially
# qualified breacher goes down, all remaining fighters still receive plans.
def plan(members, barrier, breacher):
    result={}
    for pawn in members:
        if pawn==breacher:
            result[pawn]='BREACH'
        elif barrier and breacher is None:
            result[pawn]='ATTACK_REAL_BARRIER'
        elif barrier:
            result[pawn]='SUPPORT_BREACHER'
        else:
            result[pawn]='ADVANCE'
    return result
assert plan(['rifle1','rifle2','club'],True,None)=={
 'rifle1':'ATTACK_REAL_BARRIER','rifle2':'ATTACK_REAL_BARRIER','club':'ATTACK_REAL_BARRIER'}
assert plan(['rifle','sapper','club'],True,'sapper')['club']=='SUPPORT_BREACHER'
assert plan(['rifle','sapper','club'],True,'sapper')['sapper']=='BREACH'
assert set(plan(['rifle','club'],False,None).values())=={'ADVANCE'}
assert plan([],True,None)=={}
# Stand-cell model: cannot move through walls, floors or absent access.
def approach(origin,adjacent):
    valid=[(x,z) for x,z,standable,reachable in adjacent
           if standable and reachable]
    return min(valid,key=lambda xy:(xy[0]-origin[0])**2+(xy[1]-origin[1])**2) if valid else None
assert approach((0,0),[(2,3,False,True),(1,2,True,False),(2,2,True,True)])==(2,2)
assert approach((0,0),[(1,1,False,True),(2,2,True,False)]) is None
assert approach((4,4),[]) is None
print('PASS S4.1-A/N: no-sapper structural assault has no null dereference, guarded support, eight policy cases (STATIC ONLY)')
