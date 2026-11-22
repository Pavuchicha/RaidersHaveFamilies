"""Source-level regression checks for v0.12.5.
NOT a substitute for DLL compilation or real gunfire/pathfinding gameplay tests.
"""
from pathlib import Path
root=Path(__file__).resolve().parents[1]
source=root/'RaidersHaveFamilies/Source'
tactics=(source/'RHFCombatPlannerV125.cs').read_text('utf8')
rescue=(source/'RHFRescueCommandV124.cs').read_text('utf8')
workflow=(root/'.github/workflows/build.yml').read_text('utf8')
for key in (
    'MaxPawnScan = 160', 'MaxStructureScan = 240', 'MaxThreats = 24',
    'ThreatRefreshTicks = 180', 'MaxTacticalOrders = 8',
    'evaluationBudget=Math.Min(32',
    'IsTurret(building)', 'FreeColonistsSpawned' if False else 'AllPawnsSpawned',
    'RHFCombatPlannerV125', 'JobDefOf.AttackStatic', 'JobDefOf.AttackMelee',
    'GenSight.LineOfSight', 'TryTakeOrderedJob', 'MaxFlankCandidates',
    'FirstBlockingBuilding(out before, rescuer)',
    'TraverseMode.PassAllDestroyableThings',
    'private static IntVec3 FlankCell(', 'SafeApproach(',
    'LastApproachTick', 'SafelyEngage(',
    'RHFCombatVengeancePatchV125', 'RHFTactical.PressureAssault',
    '30 — Угрозы, прикрытие и обход',
):
    assert key in tactics, f'planner missing {key}'
for key in (
    'RHFCombatPlannerV125.Response(',
    'if (covering.Contains(rescuer)) continue;',
    'if(paired || covering.Contains(rescuer))continue;',
    'RHFCombatPlannerV125.SafeApproach(',
    'RHFCombatPlannerV125.FirstObjectiveObstacle(',
    'RHFCombatPlannerV125.IsBreachableStructure(',
    'RHFCombatPlannerV125.TryPaidBreach(',
    'if(assignedBreaches>=breachLimit)continue;',
    'RHFCombatPlannerV125.ImproveAttack(',
):
    assert key in rescue, f'rescue integration missing {key}'
for disallowed in ('HarmonyPatch(typeof(Pawn_JobTracker)', 'HarmonyPatch(typeof(TickManager)',
                   'HarmonyPatch(typeof(Pawn), nameof(Pawn.Tick))'):
    assert disallowed not in tactics, f'global per-tick patch: {disallowed}'
assert 'int score = (prisonEdge ? 0 : 8000)' in rescue
assert 'generators' in tactics.lower() and 'machinery' in tactics.lower()
assert 'RHFCombatPlannerV125.IsBreachableStructure(edifice)' in rescue
assert 'AssignedEscort(op,fighter,map)' in tactics
assert 'if(extraction && AssignedEscort(op,fighter,map))continue' in tactics
assert 'RaidersHaveFamilies_v0.13.3_RimWorld_1.7_INSTALL.zip' in workflow
assert 'tests/rhf_125_combat_planner.py' in workflow
assert tactics.count('{') == tactics.count('}')
assert rescue.count('{') == rescue.count('}')
print('PASS: v0.12.5 bounded threat assessment, live covering fire, deliberate route/breach, anti-furniture and coordinated exit')
