"""R012 tactical edge-case invariants plus independent reference models.

Not a RimWorld runtime, C# compiler, TPS benchmark or gameplay certification.
The crucial changes have also been manually reviewed against their call sites.
"""
from pathlib import Path
import heapq
ROOT=Path(__file__).resolve().parents[1]
S=ROOT/'RaidersHaveFamilies'/'Source'
read=lambda name:(S/name).read_text(encoding='utf-8')
off=read('RHFOffenseS41.cs')
route=read('NJROffenseRouteS45.cs')
rescue=read('RHFObjectiveRouteV128.cs')
plan=read('RHFRescuePlannerS41.cs')
role=read('RHFRescueRoleJobGiverS41.cs')
fire=read('RHFRescueFireDisciplineV136.cs')

assert 'NJRRoute' not in off # avoid accidentally referencing another, undefined class
assert 'NJROffensePathS45.Plan(members[0],goal,knownTurrets)' in off
assert 'Thing barrier=attackRoute?.Found==true ? attackRoute.FirstBarrier' in off
assert 'NJR' in route and 'private static NJROffenseRouteS45 Search' in route
assert 'forbidGuns && exposures[next]>0' in route
assert 'NJROffenseRouteS45 safe=Search(soldier,goal,known,true,touch);' in route
assert 'Search(soldier,goal,known,false,touch)' in route
assert 'barrierIndex=i;' in route and 'result.Approach=path[i];' in route
assert 'path[k]-path[k-1]!=direction' in route  # never cut a planned path bend
assert 'bool urgent=false;' in off and 'foreach(Pawn member in members)' in off
assert 'goal.GetFirstPawn(map)==null' in off  # no goto an occupied enemy tile
assert 'route.FirstTurretExposureIndex>=0 && !route.RiskFallback' in rescue
assert 'if(!route.RiskFallback && LateralExposed(map,from,to))return true;' in rescue
assert 'PlanIndividual(Map map,Pawn member,' not in rescue
assert 'RHFObjectiveRouteV128.ResolveShared(' in plan
assert 'if(!spotted)continue;' in plan
assert 'else if(phase3Role==RHFRoleS41.PodSupply)' in role
assert 'else if(phase3Role==RHFRoleS41.Engineer)' in role
assert 'if(!HasActivePodGuard(op,pawn))' in role
assert 'KnownBlastRadius(shooter)' in fire
assert 'MemberNearImpact(op.Targets,shooter,target,blast)' in fire
assert 'MemberNearImpact(op.NativeAnimalsS41,shooter,target,blast)' in fire
assert 'explosionRadius' in fire
# Verified R011 compile-breaker: diagnostic used an undeclared 'shown'.
assert 'int shown=0;' in off and 'shown++;' in off
# A solo rifleman assigned as breacher must not Wait when gunfire is unsafe.
assert 'IntVec3 meleeStand=ReachableBreachStand(pawn,target);' in off
assert 'Task(JobDefOf.AttackMelee,target,600) : null;' in off
# Modded, player-owned, physically breachable doors and walls must be accepted.
assert '!RHFCombatPlannerV125.RequiresBreach(wall))return null;' in role

# Independent single-pass cost model: doors can beat excessive wall
# demolition even with partial turret exposure; full turret coverage still
# yields a route rather than failing the mission by policy.
def choose(graph, source, target):
    queue=[(0,source,())]
    seen={}
    while queue:
        cost,node,path=heapq.heappop(queue)
        if node in seen and seen[node]<=cost:continue
        seen[node]=cost
        if node==target:return cost,path+(node,)
        for nxt,work,guns in graph.get(node,[]):
            heapq.heappush(queue,(cost+work+guns*600,nxt,path+(node,)))
    return None

three_walls={'start':[('wall1',8500,0),('door1',800,0)],
    'wall1':[('wall2',8500,0)],'wall2':[('wall3',8500,0)],
    'wall3':[('goal',10,0)],'door1':[('door2',800,1)],
    'door2':[('goal',10,2)]}
assert choose(three_walls,'start','goal')[1] == ('start','door1','door2','goal')
no_safe={'start':[('short',20,3),('long',3000,1)],
         'short':[('goal',20,2)], 'long':[('goal',2000,0)]}
assert choose(no_safe,'start','goal')[1] == ('start','short','goal')
block={'start':[], 'goal':[]}
assert choose(block,'start','goal') is None
unknown={'start':[('door',500,0),('walls',4000,0)],'door':[('goal',500,0)],
         'walls':[('goal',4000,0)]}
assert choose(unknown,'start','goal')[1] == ('start','door','goal')

# Contact-aware convoy: no automatic rearguard for a stationary observer or
# someone whose separation grows. Only visible and behind + closing matters.
def chase(previous,current,convoy,visible,behind):
    dist=lambda p:(p[0]-convoy[0])**2+(p[1]-convoy[1])**2
    return visible and behind and previous!=current and dist(current)<dist(previous)
assert chase((30,0),(29,0),(0,0),True,True)
assert not chase((30,0),(30,0),(0,0),True,True)
assert not chase((30,0),(29,0),(0,0),False,True)
assert not chase((30,0),(31,0),(0,0),True,True)
assert not chase((30,0),(29,0),(0,0),True,False)

# Last builder self-defense is an explicit exception; when there are active
# defenders, construction remains the overriding duty.
def builder_may_fight(active_other_defenders):
    return active_other_defenders==0
assert builder_may_fight(0)
assert not builder_may_fight(1)
assert not builder_may_fight(10)

# Explosive weapons need blast safety even when the line itself is clear.
def protected_blast(shot,friends,radius):
    return any((x-shot[0])**2+(z-shot[1])**2 <= (radius+2)**2
               for x,z in friends)
assert protected_blast((10,10),[(13,10)],3)
assert not protected_blast((10,10),[(30,30)],3)
print('PASS R012: source contracts, independent safe-first/risk-fallback, pursuit, engineer and blast models')
print('NOT VERIFIED: actual C# build, RimWorld pathing/Lords, Combat Extended ammo or gameplay')
