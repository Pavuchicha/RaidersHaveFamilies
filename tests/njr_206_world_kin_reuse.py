"""NJR V15 independent WorldPawn kin reuse: static constraints, not game proof."""
from pathlib import Path
p=Path(__file__).resolve().parents[1]/'RaidersHaveFamilies/Source/RaidersHaveFamilies.cs'
s=p.read_text('utf-8')
start=s.index('private static int ReuseIndependentWorldKin(')
end=s.index('// Reuse a bounded selection of stored world kin.',start)
f=s[start:end]
order=s.index('links += ReuseIndependentWorldKin(newcomers, ledger, cfg);')
assert order < s.index('links += EnsureMinimumFamily(newcomers, ledger, cfg')
assert 'IndependentWorldKinWindow = 256' in s
assert 'IndependentKinProbesPerPawn = 48' in s
assert 'foreach(Pawn kin in world)' in f and 'if(inspected>skip+IndependentWorldKinWindow)break' in f
assert 'IEnumerable<Pawn> world=Find.WorldPawns.AllPawnsAlive' in f
assert '.ToList(' not in f and '.ToArray(' not in f
for guard in ('kin.Spawned','kin.Dead','kin.Faction.IsPlayer','kin==kin.Faction.leader',
              'WorldPawnSituation.Free','kin.def!=ThingDefOf.Human','ageGap<19f || ageGap>65f',
              'pawn.GetFather()==null','pawn.GetMother()==null','CurrentRelations(kin)>=6'):
    assert guard in f,guard
assert 'ledger.RegisterGeneratedKin(kin)' not in f
assert 'ledger.RegisterProvenance(pawn,kin' in f
print('PASS V15 bounded existing world-pawn reuse contracts (STATIC)')
