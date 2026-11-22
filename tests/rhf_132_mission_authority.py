"""RHF v0.13.2 mission authority and no-arbitrary-destruction regression.

Static contract plus a tiny decision model, not a simulated RimWorld raid.
"""
from pathlib import Path
r=Path(__file__).resolve().parents[1]
src=r/'RaidersHaveFamilies'/'Source'
ops=(src/'RHFOperations.cs').read_text(encoding='utf-8')
command=(src/'RHFRescueCommandV124.cs').read_text(encoding='utf-8')
debug=(src/'RHFDebugActions.cs').read_text(encoding='utf-8')
auto=(src/'RHFHostageSystems.cs').read_text(encoding='utf-8')
mission=(src/'RHFMissionCommandAuditV132.cs').read_text(encoding='utf-8')
workflow=(r/'.github/workflows/build.yml').read_text(encoding='utf-8')
# Directly dispatch objective AI. No chain V123 -> V124 required to control raid.
assert 'RHFRescueCommandV124.OnDeploy(this, op, true);' in ops
assert 'RHFRescueCommandV124.Infiltrate(this, op, now);' in ops
assert 'RHFRescueCommandV124.Extract(this, op, now)' in ops
assert 'SearchMissingVip(op, now);' in ops
# Do not auto-create diversion by splitting the ordinary rescue squad.
section=ops.split('private void SplitRescueWing(RHFOperation op)',1)[1].split('public override void MapComponentTick',1)[0]
assert 'op.SplitApplied = true;' in section
assert 'op.Diversion.Add' not in section and 'op.Squad.RemoveAt' not in section
# Case search also has command ownership, but is not converted to a captive-freeing job.
assert 'string.IsNullOrEmpty(op.CaseKeyV10)' not in command.split('private static bool IsRescue(RHFOperation op)',1)[1].split('private static string Key',1)[0]
assert '!string.IsNullOrEmpty(op.CaseKeyV10)' in command
assert 'op.CaseSearchCellV10' in command
# No speculative nearest-door fallback after the objective route fails.
assert 'Thing sharedBarrier = route.Valid ? route.FirstBarrier : null;' in command
assert 'Thing barrier = sharedBarrier;' in command
s=command.split('Thing barrier = sharedBarrier;',1)[1].split('if (RHFCombatPlannerV125.IsBreachableStructure(barrier))',1)[0]
assert 'AccessObstacle(' not in s and 'FindBarrierMethod' not in s
# Only real movement toward prisoner or damage to route's barrier is progress.
assert 'state.ProgressBarrierHP=sharedBarrier.HitPoints' in command
assert 'if (objectiveProgress) op.LastProgressTick = now;' in command
assert 'if (orders > 0) op.LastProgressTick = now;' not in command.split('internal static void Infiltrate(',1)[1].split('private static IntVec3 ExitCell(',1)[0]
# The hostage-first doctrine must be explicit across both scripted and natural missions.
assert 'bool combined = scenario == RHFDebugScenario.CombinedSingleFaction' in debug
assert 'bool addHire = combined && mercenary != null' in debug
assert 'else if (sponsors.Count == 1 && combined)' in debug
assert 'CombinedStrike = type == RHFOperationType.Rescue && hired != null' in auto
assert 'Rand.Chance(.44f)' not in auto
# Diagnostic displays REAL Lord job + pawn job (not merely raid intent).
assert '33 — Кто командует рейдом и зачем' in mission
assert 'l?.LordJob?.GetType().Name' in mission
assert 'pawn.jobs?.curJob?.def?.defName' in mission
assert 'python3 tests/rhf_132_mission_authority.py' in workflow
assert 'RaidersHaveFamilies-RimWorld-1.7-v0.13.3-S2.1-HARMONY-RECOVERY-TEST' in workflow
# Notional branching model for the intended invariants, for any map layout.
def plan_action(rescue, intentional_diversion, objective_route, barrier, progress):
    if not rescue: return 'normal_attack'
    if objective_route and barrier: return 'objective_breach'
    if objective_route: return 'objective_approach'
    return 'hold_then_withdraw' if not progress else 'replan'
for v in (False,True):
    assert plan_action(True,v,False,True,False) == 'hold_then_withdraw'
    assert plan_action(True,v,True,True,True) == 'objective_breach'
    assert plan_action(True,v,True,False,True) == 'objective_approach'
assert plan_action(False,True,False,False,False) == 'normal_attack'
print('PASS: one direct rescue controller, real progress, no arbitrary structural targets, explicit diversion, diagnostic')
