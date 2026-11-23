"""v0.12.3 rescue mission guards. Structural only; actual DLL compile and in-game test required."""
from pathlib import Path
base = Path(__file__).resolve().parents[1]
src = (base / 'RaidersHaveFamilies/Source/RHFRescueControlV123.cs').read_text(encoding='utf-8')
project_path = base / 'RaidersHaveFamilies/Source/RaidersHaveFamilies.csproj'
project = project_path.read_text(encoding='utf-8') if project_path.exists() else None
for marker in (
    'RHFRescueDeploymentPatchV123', 'RHFRescueTickPatchV123',
    'RHFRescueInfiltratePatchV123', 'RHFRescueExitPatchV123',
    'RHFRescueMissionDiagnosticsV123',
    'private const int DecisionInterval = 90',
    'for (int i = 0; i < op.Squad.Count; i++)',
):
    assert marker in src, marker
assert 'previous.Contains(p.thingIDNumber)' in src, 'unrelated raid pawn hijack protection'
assert 'op.Parties.Count == 1' in src, 'single party recovery'
assert 'TakeMissionOwnership(rescuer)' in src, 'vanilla Lord separation'
assert 'FreeTargetMethod.Invoke' in src, 'real prisoner status transition'
assert 'state.PreviousDistance' in src, 'actual progress detection'
assert 'current.targetA.Thing == barrier' in src, 'preserve ongoing door attack'
assert 'state.BlockedTicks >= 1800' in src, 'bounded stall reporting'
assert 'PermitExit(__instance)' in src, 'block false raid-exit success'
assert 'pawn.health.summaryHealth.SummaryHealthPercent < 0.20f' in src, 'humanitarian retreat for dying fighter'
assert 'operation has finished' not in src.lower(), 'no fake completion'
assert 'Condition' not in src or 'Persistent' not in src
assert project is None or 'Sdk="Microsoft.NET.Sdk"' in project, 'new source auto-included by SDK-style project'
# Check all major blocks remain balanced, excluding string/comment semantics.
assert src.count('{') == src.count('}')
assert src.count('(') == src.count(')')
assert 'RHFFactionConversation' not in src, 'do not alter diplomacy'
print('PASS: v0.12.3 mission ownership, bounded rescue pursuit, safe exit & diagnostics structural contract')
