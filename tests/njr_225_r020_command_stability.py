"""R020 source regression assertions and tiny deterministic decision model.
NOT a C# compiler and NOT an in-game RimWorld run.
"""
from pathlib import Path
root=Path(__file__).resolve().parents[1]/'RaidersHaveFamilies'/'Source'
def src(name): return (root/name).read_text(encoding='utf-8-sig')
planner=src('RHFRescuePlannerS41.cs')
route=src('RHFObjectiveRouteV128.cs')
command=src('RHFRescueCommandV124.cs')
assert 'RescueLeadsR020.GetValue(op' in planner
assert 'command.Target!=captive' in planner
assert 'if(!Active(lead,map) || escortedDuringAssault.Contains(lead) ||' in planner
assert 'command.Leader=lead;' in planner
assert 'internal bool BudgetExceeded;' in route
assert 'if(!safe && !fresh.BudgetExceeded)' in route
assert 'if(!safe && !plan.BudgetExceeded)' in route
assert 'prior.FirstBarrier.Spawned' in route
assert 'RHfIgnore' not in route
assert 'committedBarrier?2700:ReplanTicks' in route
assert 'running.def!=JobDefOf.AttackMelee' in command
assert 'running.def!=JobDefOf.AttackStatic' in command
assert 'RHFCombatPlannerV125.IsTurret(structure)' in command
assert 'issued.Objective==structure' in command
assert 'op.BoardS41?.Breacher==p' in command
assert 'p.jobs.EndCurrentJob(JobCondition.InterruptForced);' in command

# The leader must not change as dispersed soldiers draw closer to the target.
def lead_selection(stable, available, positions, target, reserved):
    if stable in available and stable not in reserved:
        return stable
    eligible=[x for x in available if x not in reserved]
    return min(eligible,key=lambda x:(positions[x][0]-target[0])**2+(positions[x][1]-target[1])**2) if eligible else None
units={'A':(0,0),'B':(4,0),'C':(6,0)}
leader=lead_selection(None,list(units),units,(10,0),set())
assert leader=='C'
units['A']=(9,0)
assert lead_selection(leader,list(units),units,(10,0),set())=='C'
assert lead_selection(leader,['A','B'],units,(10,0),set())=='A'

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
print('PASS R020 stable leader, CPU-budget no-demolition, native demolition abort (STATIC+ORACLE)')
