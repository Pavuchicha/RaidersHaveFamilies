"""RHF 0.9.1 CI regression: guard RimWorld 1.7 prisoner interaction API.

Static guard only; real C# compilation is performed by the next CI step.
"""
from pathlib import Path
root=Path(__file__).resolve().parents[1]
s=(root/'RaidersHaveFamilies/Source/RHFHostageSystems.cs').read_text('utf8')
assert 'guest.interactionMode' not in s.replace('Pawn_GuestTracker.interactionMode field', '')
assert 'WceModeEnabledApi' in s and 'IsInteractionEnabled' in s
assert 'GetNamedSilentFail("WCE2_Mutilate")' in s
assert 'IsWceMutilationModeEnabled(r.Pawn)' in s
assert 'return null; // Do NOT infer torture' in s
print('PASS: WCE2 mutilation evidence uses compatible guarded interaction API, not removed guest field')
