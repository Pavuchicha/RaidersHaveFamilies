"""R020 source regression assertions and tiny deterministic decision model.
NOT a C# compiler and NOT an in-game RimWorld run.
"""
from pathlib import Path
root=Path(__file__).resolve().parents[1]/'RaidersHaveFamilies'/'Source'
def src(name): return (root/name).read_text(encoding='utf-8-sig')
planner=src('RHFRescuePlannerS41.cs')
route=src('RHFObjectiveRouteV128.cs')
command=src('RHFRescueCommandV124.cs')
# R021 replaces the R020 commander with mission-wide multi-source search.
assert 'RescueLeadsR020.GetValue(op' not in planner
assert 'command.Leader=lead;' not in planner
assert 'ResolveShared(' in planner
assert 'ResolveShared(RHFOperation op' in route
assert 'IList<IntVec3> origins' in route
assert 'for(int i=0;i<origins.Count;i++)' in route
assert 'internal bool BudgetExceeded;' in route
assert route.count('PlanBounded(map,origins,captive,fresh,') == 1
assert 'if(!safe && !fresh.BudgetExceeded)' not in route
assert 'PlanIndividual(Map map' not in route  # removed redundant personal routing
assert 'prior.FirstBarrier.Spawned' in route
assert 'RHfIgnore' not in route
assert 'committedBarrier?2700:ReplanTicks' in route
assert 'running.def!=JobDefOf.AttackMelee' in command
assert 'running.def!=JobDefOf.AttackStatic' in command
assert 'RHFCombatPlannerV125.IsTurret(structure)' in command
assert 'issued.Objective==structure' in command
assert 'op.BoardS41?.Breacher==p' in command
assert 'p.jobs.EndCurrentJob(JobCondition.InterruptForced);' in command

# No selected unit is endowed with path authority; any eligible unit can
# supply a physical A* start position. Escorted units are excluded.
def eligible_positions(positions, reserved):
    return [cell for unit,cell in positions.items() if unit not in reserved]
units={'A':(0,0),'B':(4,0),'C':(6,0)}
assert eligible_positions(units,{'C'})==[(0,0),(4,0)]
units['A']=(9,0)
assert eligible_positions(units,{'C'})==[(9,0),(4,0)]

# Fallback must not infer 'no wall-free route' from exhausting A* nodes.
def should_try_demolition_route(safe_result, search_budget_exceeded):
    return not safe_result and not search_budget_exceeded
assert should_try_demolition_route(False,False)
assert not should_try_demolition_route(False,True)

# The old native building job survives Lord replacement without an explicit abort.
def keep_structure_attack(phase,authorized_breacher,board_barrier,job_target,issued_breach):
    return (phase==1 and authorized_breacher and board_barrier==job_target) or (phase in (2,3) and issued_breach==job_target)
assert not keep_structure_attack(1,False,'door','wall',None)
assert not keep_structure_attack(1,True,'door','wall',None)
assert keep_structure_attack(1,True,'door','door',None)
assert keep_structure_attack(3,False,None,'field-entry','field-entry')
print('PASS R021 leaderless route sources, CPU-budget no-demolition, native demolition abort (STATIC+ORACLE)')
