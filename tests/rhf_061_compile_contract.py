"""Prevent reintroducing the confirmed RimWorld 1.7 StatWorker CS0115 regression.

Static guard; only a real dotnet build against Krafs.Rimworld.Ref can certify compile."""
from pathlib import Path
root = Path(__file__).resolve().parents[1]
src = (root/'RaidersHaveFamilies/Source/RHFRankUI.cs').read_text('utf8')
assert 'override string GetStatDrawEntryLabel(' not in src, 'CS0115: invalid override in RimWorld 1.7'
assert src.count('public override string GetExplanationUnfinalized(') == 2
assert 'public override float GetValueUnfinalized(' in src
assert 'public static string Short(Pawn p)' in src
assert 'public static string Details(Pawn p)' in src
wf = (root/'.github/workflows/build.yml').read_text('utf8')
assert '0.14.0-beta2' in wf
assert 'dotnet build' in wf
assert 'python3 tests/rhf_061_compile_contract.py' in wf
print('PASS: RimWorld 1.7 StatWorker v0.6.1 compatibility guard')
