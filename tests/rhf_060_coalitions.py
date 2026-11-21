"""Cumulative update regression tests; the runtime raid worker still needs in-game validation."""
from pathlib import Path
R=Path(__file__).resolve().parents[1]
S=R/'RaidersHaveFamilies/Source'
ops=(S/'RHFOperations.cs').read_text('utf-8')
debug=(S/'RHFDebugActions.cs').read_text('utf-8')
natural=(S/'RHFHostageSystems.cs').read_text('utf-8')
dialogue=(S/'RHFDialogue.cs').read_text('utf-8')
ui=(S/'RHFRankUI.cs').read_text('utf-8')
notes=(S/'RHFPreceptNotes.cs').read_text('utf-8')
assert 'op.SpawnTick = Find.TickManager.TicksGame' in ops
assert 'controller.ForceDeployNow(op)' in debug
assert 'IncidentDefOf.RaidEnemy.Worker.TryExecute(parms)' in ops
assert 'Find.Storyteller.incidentQueue.Add' not in debug
assert 'Find.Storyteller.incidentQueue.Add' not in natural
assert 'MaxStrikeParties = 5' in ops
assert 'sponsors.Count<4' in natural
assert 'candidate.Importance<5' in natural
assert 'op.Parties.Add(new RHFStrikeParty' in natural
assert 'RHFStrikeRole.Rescue' in natural and 'RHFStrikeRole.Diversion' in natural
assert 'sponsors.Count > 1' in debug or 'sponsors.Count>1' in debug
assert 'FindHiredEnemy(sponsors' in debug
assert 'RHFDebugScenario.CoalitionManyFactions' in debug
assert 'RHFDebugScenario.CombinedSingleFaction' in debug
assert 'RHFDebugScenario.CoalitionHiredDiversion' in debug
assert 'RHFDebugScenario.CombinedMixedApproach' in debug
assert 'RHFDebugScenario.DropRescueWithDiversion' in debug
assert 'DebugStatus()' in ops and 'CancelDebugOperations()' in ops
assert 'RHFStrikeRole.Diversion' in debug and 'RHFStrikeRole.Rescue' in debug
assert 'Scribe_Collections.Look(ref Sponsors' in ops
assert 'Scribe_Collections.Look(ref Parties' in ops
assert 'Scribe_Collections.Look(ref Diversion' in ops
assert 'Raiders Have Families' in notes
assert 'RHFDialogue' in dialogue
assert 'RHF' in ui
print('PASS: 0.6 immediate spawn, 1..4 sponsors, hire, 5 strike parties max, save/load, all prior UI files.')
