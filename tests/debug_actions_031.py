"""RHF 0.6 dev command contracts. Actual game behavior needs a GitHub compile + play test."""
from pathlib import Path
P=Path(__file__).resolve().parents[1]/'RaidersHaveFamilies/Source'
patch=(P/'RHFPatches.cs').read_text('utf-8')
ops=(P/'RHFOperations.cs').read_text('utf-8')
host=(P/'RHFHostageSystems.cs').read_text('utf-8')
debug=(P/'RHFDebugActions.cs').read_text('utf-8')
assert 'TraitDefOf.Cannibal' not in host
assert 'GetNamedSilentFail("Cannibal")' in host
assert 'SetGuestStatus(null, false)' not in ops
assert 'SetGuestStatus(null);' in ops
assert 'bool prisoner)' not in patch
assert 'p.IsPrisonerOfColony' in patch
assert 'DebugForced' in ops and 'CancelDebugOperation' in ops
assert 'using LudeonTK;' in debug
assert debug.count('[DebugAction(')>=30
for name in ['PreparePrisoner','Rescue','MassRescue','VipRescue','FieldPod','Mercenaries',
    'Coalition','Vengeance','TribalVengeance','HighTechDrop','HighTechSiege',
    'CombinedSingle','ThreeFactions','HiredDistraction','MixedCombined',
    'CoalitionDiagnostics','CancelTestOperations','OtherRank8']:
    assert 'void '+name+'()' in debug,name
assert 'IncidentDefOf.RaidEnemy.Worker.TryExecute(parms)' in ops
assert 'Find.Storyteller.incidentQueue.Add' not in debug
assert 'DelayTicks = 150' not in debug
assert 'controller.ForceDeployNow(op)' in debug
assert 'MaxDebugTargets = 24' in debug
assert 'RHFMod.Settings?.Retaliation != true' in ops
assert 'RHFStrikeRole.Diversion' in debug
assert 'RHF - ТЕСТЫ | ПЛЕННИКИ' in debug
assert 'DebugActionType.ToolMap' in debug
print('PASS: immediate test raids, rescue/diversion roles, multi-sponsor coalition, hostage rank toolmap.')
