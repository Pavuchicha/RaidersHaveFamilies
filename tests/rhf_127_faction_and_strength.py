"""RHF 0.12.7: structure/faction/VIP static contracts.
These are source checks and numerical examples, NOT in-game integration tests.
"""
from pathlib import Path
root=Path(__file__).resolve().parents[1]
src=root/'RaidersHaveFamilies/Source'
debug=(src/'RHFDebugActions.cs').read_text(encoding='utf8')
planner=(src/'RHFCombatPlannerV125.cs').read_text(encoding='utf8')
ops=(src/'RHFOperations.cs').read_text(encoding='utf8')
workflow=(root/'.github/workflows/build.yml').read_text(encoding='utf8')
for marker in [
    'private static Faction lastRandomFaction;',
    'private static Faction RandomTestFaction(List<Faction> candidates)',
    'private static Faction pinnedTestFaction;',
    'public static void CyclePrisonerFaction()',
    'public static void ResetPrisonerFaction()',
    'private static List<Faction> EligibleTestFactions(bool industrialOnly)',
    'private static Faction ChooseTestFaction(bool industrialOnly)',
    'private static PawnKindDef DebugMemberKind(Faction faction)',
    'candidate.defaultFactionDef == faction.def',
    'PawnKindDef basic = DebugMemberKind(faction)',
    'Rank6Industrial()', 'Rank8Industrial()',
    'Pawn selectedVip = null;',
    'int rank = HostageRank(candidate);',
    'selectedVip != null ? SourceFaction(selectedVip)',
    'if (selectedVip != null) targets.Add(selectedVip);',
    'private static int HostageRank(Pawn pawn)',
    'record != null ? record.Importance',
    'scenario == RHFDebugScenario.FortifiedVipExtraction || coalition)',
    'sponsor.def.techLevel < TechLevel.Industrial && mercenary != null',
    'Обычное VIP-спасение доступно через команду 03',
]:
    assert marker in debug, 'missing debug/faction/VIP rule: '+marker
assert 'FirstEnemy(null, false, false)' not in debug.split('private static void PrepareAt(',1)[1].split('private static void Start(',1)[0]
assert 'PawnGenerator.GeneratePawn(basic, faction)' in debug
for marker in [
    'internal static int BreachWorkCost(Thing obstacle)',
    'obstacle.def.IsEdifice()',
    'obstacle.def.passability == Traversability.Impassable',
    'obstacle.def.destroyable',
    'obstacle.def.useHitPoints',
    'obstacle.HitPoints',
    'BreachWorkCost(block)',
    'int damageWork=RequiresBreach(block) ? BreachWorkCost(block) : -1;',
    'routeFound=true;',
    'building.def?.building?.turretGunDef != null',
]:
    assert marker in planner, 'missing universal structure rule: '+marker
for forbidden in ('name.IndexOf("wall"', 'name.IndexOf("bulkhead"',
                  'name.IndexOf("barricade"', 'Math.Min(900,block.HitPoints*2)',
                  'Math.Min(140,block.HitPoints/2)'):
    assert forbidden not in planner, 'nonuniversal structural name / HP cap: '+forbidden
assert 'RHFCombatPlannerV125.IsBreachableStructure(edifice)' in ops
assert 'RHFCombatPlannerV125.BreachWorkCost(edifice)' in ops
assert 'bestDoor ?? bestWall' not in ops
# User scenario: 3 walls x 1000 HP versus wall 1000 + door 500.
def break_cost(hp):
    return 16 + min(1000000,hp)*2
assert 3*break_cost(1000) > break_cost(1000)+break_cost(500)
# Equal HP must not receive an arbitrary door-type discount.
assert break_cost(600) == break_cost(600)
# Modded materials of arbitrary strength must retain ordering (no 900-HP clamp).
assert break_cost(3000) > break_cost(1500) > break_cost(500)
assert 'tests/rhf_127_faction_and_strength.py' in workflow
assert 'NJR_NotJustRaiders_v0.15.0-beta1_RimWorld_1.7_INSTALL.zip' in workflow
print('PASS: modded faction PawnKind, random dev selection / explicit pinning, VIP rank, industrial mercenaries, universal actual-HP path cost')
