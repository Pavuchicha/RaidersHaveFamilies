"""R007 source regression for mod compatibility / passive event caches.

Not a RimWorld assembly or simulated game. Includes a small independent model
for cache invalidation and equipment invariants; source assertions make sure
those protections actually appear in the appropriate live code paths.
"""
from pathlib import Path
import re

ROOT=Path(__file__).resolve().parents[1]
SRC=ROOT/'RaidersHaveFamilies'/'Source'
def source(name):return (SRC/name).read_text(encoding='utf-8')
ledger=source('RaidersHaveFamilies.cs')
hostages=source('RHFHostageSystems.cs')
group=source('NJRGroupRansomV15.cs')
animal=source('NJRForceCompositionS42.cs')
gear=source('NJRAdaptiveLoadoutsS42.cs')
orbital=source('NJROrbitalSupportS42.cs')
apparel=source('NJRApparelCompatibilityS43.cs')

# Dormant populations and short-lived pawns are tracked via event-time maps,
# not by a full hostage scan on every unrelated Pawn.ExitMap.
assert 'private Dictionary<Pawn,RHFHostageRecord> hostageByPawn' in ledger
assert 'hostageByPawn.TryGetValue(pawn,out RHFHostageRecord record)' in ledger
assert 'indexedHostageCount!=Hostages.Count' in ledger
assert 'record?.Pawn!=pawn || !Hostages.Contains(record)' in ledger
assert 'hostageByPawn?.Clear();' in ledger
assert 'LoadSaveMode.PostLoadInit' in ledger
assert 'InvalidateHostageIndex();' in ledger
assert 'Hostages.Add(record);\n            InvalidateHostageIndex();' in ledger
assert 'ledger.InvalidateHostageIndex();' in hostages
assert 'ledger.InvalidateHostageIndex();' in group
assert 'r.Pawn=null;\n                    ledger.InvalidateHostageIndex();' in hostages
assert re.search(r'if\(!r\.Recruited && !IsDeadCase\(r\) && ledger!=null\)\s*\{\s*ledger\.Hostages\.Remove\(r\);\s*ledger\.InvalidateHostageIndex\(\);',hostages)
# Pre-existing R006 save fields remain unchanged.
for key in ('"rhfHostagesV2"','"rhfWorldPawnsCreated"','"njrNextGroupRansomIdV15"'):
    assert key in ledger
assert 'hostageByPawn, "' not in ledger

# Hot arrivals must not repeatedly linearly probe the same 192-pawn queue.
assert 'private HashSet<Pawn> pendingSet' in ledger
assert 'if (!pendingSet.Add(pawn)) return;' in ledger
assert 'pendingSet.Clear();' in ledger
assert 'pendingSet = new HashSet<Pawn>();' in ledger
assert 'if (pending.Contains(pawn))' not in ledger
assert 'if (pending.Count == 0) return;' in ledger

# Upgrade armour and orbital support may NEVER eject existing modded outfit.
assert 'ApparelUtility.CanWearTogether(worn.def,apparel,pawn.RaceProps.body)' in apparel
assert 'NJRApparelCompatibilityS43.CanAdd(pawn,armor)' in gear
assert 'NJRApparelCompatibilityS43.CanAdd(pawn,def)' in orbital
assert 'return pawn.apparel.WornApparel.Contains(apparel)?armor.BaseMarketValue:0f' not in gear
assert 'if(pawn.apparel.WornApparel.Contains(apparel))return actual;' in gear
assert 'if(actual>party.LoadoutBudgetS42-party.LoadoutSpentS42)' in gear
assert 'if(actualCost>party.LoadoutBudgetS42-party.LoadoutSpentS42)' in orbital
assert 'party.LoadoutSpentS42+=actualCost;' in orbital
assert 'if(CombatExtended && selected.IsRangedWeapon)return;' in gear
assert 'if(CombatExtended && ranged)return;' in gear

# Animal option discovery is per immutable faction DEF, not every active raid.
assert 'Dictionary<FactionDef,List<PawnKindDef>> animalCandidates' in animal
assert 'animalCandidates.TryGetValue(faction.def,out List<PawnKindDef> cached)' in animal
assert 'animalCandidates[faction.def]=kinds;' in animal
assert 'var affordable=new List<PawnKindDef>(kinds.Count);' in animal
assert 'affordable.Clear();' in animal

# Independent state transition checks: when a contract participant departs,
# the index must stop owning the pawn even if its contract record is retained.
class IndexModel:
    def __init__(self):
        self.records=[]
        self.lookup={}
        self.length=-1
    def invalidate(self):
        self.length=-1
        self.lookup.clear()
    def find(self,p):
        if self.length!=len(self.records):
            self.lookup={name:obj for name,obj in self.records if name is not None}
            self.length=len(self.records)
        return self.lookup.get(p)
    def add(self,p,record):
        self.records.append((p,record));self.invalidate()
    def release(self,p):
        self.records=[(None if name==p else name, rec) for name,rec in self.records]
        self.invalidate()
m=IndexModel();m.add('A','contract A');m.add('B','contract B')
assert m.find('C') is None
assert m.find('A')=='contract A'
m.release('A')
assert m.find('A') is None and m.find('B')=='contract B'
# Equivalent of removing B and adding C at unchanged list length.
m.records.pop();m.invalidate();m.add('C','contract C')
assert m.find('B') is None and m.find('C')=='contract C'

# The no-strip doctrine prioritizes keeping a native CE/HAR outfit over
# issuing an optional upgrade, even when budget permits.
def accept(existing_compat, cost, budget):
    return all(existing_compat) and cost <= budget
assert accept([True,True], 150,200)
assert not accept([True,False],150,200)
assert not accept([True],250,200)

print('PASS R007 compatibility and passive audit: transient event index, '
      'no leaking released pawns, hash arrivals, native outfit protection, '
      'actual-value billing, mod faction animal cache (STATIC + MODEL ONLY)')
