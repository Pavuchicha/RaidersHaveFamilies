"""R013 independent edge-case model and code-consistency checks.

Static contract only. Not a compilation, execution of C#, simulation of
RimWorld pathfinding, or evidence that the AI works in the game.
"""
from pathlib import Path
s=Path(__file__).resolve().parents[1]/'RaidersHaveFamilies'/'Source'
load=lambda f:(s/f).read_text(encoding='utf-8-sig')
combat=load('RHFCombatPlannerV125.cs')
rescuepath=load('RHFObjectiveRouteV128.cs')
offensepath=load('NJROffenseRouteS45.cs')
offense=load('RHFOffenseS41.cs')
role=load('RHFRescueRoleJobGiverS41.cs')
planner=load('RHFRescuePlannerS41.cs')
pod=load('RHFPodPlannerS41.cs')

assert 'internal static bool RequiresBreach(Thing obstacle)' in combat
assert 'door.Open' in combat
assert 'RequiresBreach(block) ? BreachWorkCost(block)' in combat
assert 'RHFCombatPlannerV125.RequiresBreach(building)' in rescuepath
assert 'RHFCombatPlannerV125.RequiresBreach(edifice)' in offensepath
assert 'RHFCombatPlannerV125.RequiresBreach(wall)' in offensepath
assert '!RHFCombatPlannerV125.RequiresBreach(b)' in offense
assert 'NearestObservedTurret(members,knownTurrets,map)' in offense
assert 'NearestObservedTurret(List<Pawn> members,' in offense
assert 'if(dist<score){score=dist;best=p;}\n                    break;' not in offense
assert 'if(!HasActivePodGuard(op,pawn) || AdjacentObservedThreat(op,pawn))' in role
assert 'if(!HasActivePodGuard(op,pawn))' in role
assert 'guard.WorkTagIsDisabled(WorkTags.Violent)' in role
assert 'duty==RHFRoleS41.Screen || duty==RHFRoleS41.Hold' in role
assert 'RHFCombatPlannerV125.RequiresBreach(wall)' in pod
assert 'memory.PreviousConvoy=convoy;' in planner
assert 'memory.PreviousConvoy.IsValid' in planner
assert '(before-memory.PreviousConvoy).LengthHorizontalSquared' in planner

# A model of the two-snapshot pursuit criterion. No RimWorld code executed.
def catching(previous_enemy,now_enemy,previous_convoy,now_convoy):
    old=(previous_enemy-previous_convoy)**2
    new=(now_enemy-now_convoy)**2
    return previous_enemy!=now_enemy and new<old
assert catching(7,11,20,21)    # chaser closes by 3 tiles
assert not catching(7,8,20,24) # convoy moves faster
assert not catching(7,7,20,20) # stationary defender

# Preserve physical open-door semantics without confusing it with ownership:
# player-owned closed doors remain valid breach targets, open doors don't.
def requires_breach(hostile,destroyable,open_door):
    return hostile and destroyable and not open_door
assert requires_breach(True,True,False)
assert not requires_breach(True,True,True)
assert not requires_breach(False,True,False)
print('PASS R013 source review contracts and independent pursuit/door models; NOT GAME PASS')
