"""Static regression checks. The definitive C# compile runs on GitHub Actions."""
from pathlib import Path
P = Path(__file__).resolve().parents[1] / 'RaidersHaveFamilies' / 'Source'
patch = (P/'RHFPatches.cs').read_text('utf-8')
ops = (P/'RHFOperations.cs').read_text('utf-8')
hostage = (P/'RHFHostageSystems.cs').read_text('utf-8')
debug = (P/'RHFDebugActions.cs').read_text('utf-8')
assert 'TraitDefOf.Cannibal' not in hostage, '1.7 compile failure CS0117 reintroduced'
assert 'GetNamedSilentFail("Cannibal")' in hostage
assert 'SetGuestStatus(null, false)' not in ops, '1.7 compile failure CS1503 reintroduced'
assert 'SetGuestStatus(null);' in ops
assert 'bool prisoner)' not in patch, 'old Harmony patch argument breaks in 1.7'
assert 'p.IsPrisonerOfColony' in patch
assert 'DebugForced' in ops and 'CancelDebugOperation' in ops
assert 'using LudeonTK;' in debug
assert debug.count('[DebugAction(') >= 25
names = ['PreparePrisoner', 'Rescue', 'MassRescue', 'VipRescue', 'FieldPod',
    'Mercenaries', 'Coalition', 'Vengeance', 'TribalVengeance',
    'HighTechDrop', 'HighTechSiege', 'Explain', 'InspectSelectedPawn', 'EnrichSelectedPawn']
for name in names: assert 'void '+name+'()' in debug, name
for name in ['RHFDebugScenario.Rescue', 'RHFDebugScenario.MassRescue',
    'RHFDebugScenario.VipRescue', 'RHFDebugScenario.FieldPod',
    'RHFDebugScenario.MercenaryRescue', 'RHFDebugScenario.CoalitionRescue',
    'RHFDebugScenario.Vengeance', 'RHFDebugScenario.TribalVengeance',
    'RHFDebugScenario.HighTechDrop', 'RHFDebugScenario.HighTechSiege']:
    assert name in debug
assert 'IncidentDefOf.RaidEnemy' in debug
assert 'incidentQueue.Add' in debug
assert 'MaxDebugTargets = 24' in debug
assert 'DelayTicks = 150' in debug
assert 'MapComponentTick' not in debug
assert 'RHFMod.Settings?.Retaliation != true' in ops
print('PASS: 1.7 compile-error fixes, Harmony capture tracking, 14 developer actions, direct scenario scheduling and tick safeguards')
