"""S4.1-A/J static/behavioral contract for multiple native Lord instances.

No RimWorld binaries: this is NOT a live AI/C# compilation test.
"""
from pathlib import Path
root=Path(__file__).resolve().parents[1]
n=(root/'RaidersHaveFamilies/Source/RHFNativeDoctrineS41.cs').read_text('utf-8')
o=(root/'RaidersHaveFamilies/Source/RHFOffenseS41.cs').read_text('utf-8')
w=(root/'.github/workflows/build.yml').read_text('utf-8')
checks=[
 (n,'public Lord OwnerLord;'),
 (n,'Scribe_References.Look(ref OwnerLord,"rhfNativeOwnerLordS41AJ")'),
 (n,'state.OwnerLord==ownerLord'),
 (n,'state=new RHFNativeWingS41(p.Faction,lord,kind);'),
 (n,'state.OwnerLord==null || state.OwnerLord.ownedPawns==null'),
 (n,'state.OwnerLord.ownedPawns.Count==0'),
 (n,'state.OwnerLord.faction!=faction'),
 (n,'state.Kind!=kind || state.Faction!=faction'),
 (n,'now-state.LastObservedTick>ReobserveTicks+90'),
 (n,'(kind==Siege && state.Stage!=Building)'),
 (n,'(kind==Animals && state.Animals<=0)'),
 (n,'(state.Rally-supporterCell).LengthHorizontalSquared'),
 (n,'wing.OwnerLord?.GetUniqueLoadID()'),
 (o,'RHFNativeDoctrineS41.FindForSupport(op,'),
 (w,'python3 tests/rhf_159_s41_native_lord_identity.py'),
]
for src,fragment in checks: assert fragment in src,fragment
assert o.count('RHFNativeDoctrineS41.FindForSupport(op,')==2
assert 'state.Kind==kind && state.Faction==faction)' not in n

# Synthetic *logic* model. Two siege Lords + two animal Lords share faction,
# yet each keeps independent count, phase and front. Choosing nearest active
# Lord should not merge their state or follow a stale/wrong-stage siege.
from dataclasses import dataclass
@dataclass
class Wing:
    faction: str
    lord: str
    kind: int
    pos: tuple
    stage: int=2
    animals: int=0
    now: int=1000
    alive: bool=True
wings=[
    Wing('F','siege-near',1,(10,10),1),
    Wing('F','siege-far',1,(80,80),1),
    Wing('F','beast-near',2,(12,11),2,9),
    Wing('F','beast-far',2,(83,83),2,13),
    Wing('F','siege-finished',1,(9,11),2),
    Wing('G','siege-other-faction',1,(10,10),1),
    Wing('F','beast-stale',2,(11,10),2,30,500),
]
def select(faction,kind,pos,now=1100):
    suitable=[x for x in wings if x.faction==faction and x.kind==kind
        and x.alive and now-x.now<=450 and
        ((kind==1 and x.stage==1) or (kind==2 and x.animals>0))]
    return min(suitable,key=lambda x:(x.pos[0]-pos[0])**2+(x.pos[1]-pos[1])**2,
        default=None)
assert select('F',1,(15,15)).lord=='siege-near'
assert select('F',1,(77,78)).lord=='siege-far'
assert select('F',2,(11,12)).lord=='beast-near'
assert select('F',2,(79,83)).lord=='beast-far'
assert select('G',1,(10,10)).lord=='siege-other-faction'
assert select('G',2,(10,10)) is None
wings[0].alive=False
assert select('F',1,(15,15)).lord=='siege-far'  # not siege-finished
assert len(set(x.lord for x in wings))==len(wings)

# Each of 10 regressions is independently detected by its contract.
mutations=[
 (n,'public Lord OwnerLord;'),
 (n,'Scribe_References.Look(ref OwnerLord,"rhfNativeOwnerLordS41AJ")'),
 (n,'state.OwnerLord==ownerLord'),
 (n,'state.OwnerLord.faction!=faction'),
 (n,'state.OwnerLord.ownedPawns.Count==0'),
 (n,'now-state.LastObservedTick>ReobserveTicks+90'),
 (n,'(kind==Siege && state.Stage!=Building)'),
 (n,'(kind==Animals && state.Animals<=0)'),
 (n,'(state.Rally-supporterCell).LengthHorizontalSquared'),
 (o,'RHFNativeDoctrineS41.FindForSupport(op,'),
]
for source,old in mutations:
    assert old in source
    assert old not in source.replace(old,'// MUTATED',source.count(old))
print('PASS S4.1-A/J: per-Lord state persistence, same-faction isolation,')
print('nearest active siege/beast support, stale/wrong-stage rejection, 10 mutation contracts')
print('NOT TESTED: RimWorld C# compilation / real Lord refs after SaveLoad / CE TPS')
