"""R060: independent faction budgets across all dispatch paths.
Arithmetic model and static source checks, not a RimWorld/Harmony integration test.
"""
from pathlib import Path
from collections import defaultdict
from math import isclose
root=Path(__file__).resolve().parents[1]/'RaidersHaveFamilies'/'Source'
def read(n): return (root/n).read_text()
# Same formula as the established R059 coefficient implementation.
def budget(v,rank,n,pressure,strength=1):
    return max(1,v*(1+.1*min(8,max(0,rank))+.12*min(6,max(0,n-1))+.005*min(100,max(0,-pressure)))*min(2,max(.5,strength)))
def allocate(v, wings, faction_data, rank):
    groups=defaultdict(list)
    for index,(f,w) in enumerate(wings): groups[f].append((index,w))
    result=[0.]*len(wings)
    for f,g in groups.items():
        gross=budget(v,rank,*faction_data[f]); weights=sum(w for _,w in g); left=gross
        for i,(idx,w) in enumerate(g):
            share=left if i==len(g)-1 else gross*w/weights
            result[idx]=share; left-=share
    return result
factions={'A':(1,0),'B':(1,0),'C':(1,0),'H':(0,0)}
for vanilla in (100,350,1200,5000,15000,50000):
    # Debug drop + diversion used to divide DIFFERENT factions 58/42.
    assert allocate(vanilla,[('A',.58),('B',.42)],factions,0)==[vanilla,vanilla]
    # Same faction fallback in reprisal and traitor hunt used to double.
    assert isclose(sum(allocate(vanilla,[('A',1),('A',1)],factions,0)),vanilla)
    assert allocate(vanilla,[('A',.6),('A',.4)],factions,0)==[vanilla*.6,vanilla-vanilla*.6]
    assert isclose(sum(allocate(vanilla,[('A',1),('B',1),('C',1),('H',1)],factions,0)),4*vanilla)
    # Distinct faction data; positive pressure never buffs or nerfs.
    d={'A':(3,-100),'B':(1,100),'H':(0,-20)}
    a=allocate(vanilla,[('A',.6),('A',.4),('B',.1),('H',.1)],d,8)
    assert isclose(sum(a[:2]),budget(vanilla,8,3,-100))
    assert isclose(a[2],budget(vanilla,8,1,0)); assert isclose(a[3],budget(vanilla,8,0,-20))
    # Protect five affordable ordinary fighters before discretionary gear.
    for cp in (35,70,140,350):
        gross=budget(vanilla,4,1,0); core=5*cp
        if gross<core: continue # no artificial topup; refuse this wing
        supply=min(300,.12*gross,gross-core)
        gear=min(.23*gross,3600,max(0,gross-supply-max(core,350)))
        assert gross-supply-gear+1e-7>=core
        assert supply+gear+core<=gross+1e-7
# Example: two 150pt elite pawns plus two 70pt fighters would block 5th
# at 460 points. Replace elites with ordinary members, then supplement.
assert 2*150+2*70+70>460 and 5*70<=460
threat=read('NJRThreatBudgetS41.cs'); ops=read('RHFOperations.cs'); force=read('NJRForceStrengthR017.cs'); debug=read('RHFDebugActions.cs'); comp=read('NJRForceCompositionS42.cs')
for token in ('Dictionary<Faction,List<RHFStrikeParty>>','FactionAllocation(map,op,group.Key)','wing.Points=allocated','op.ThreatPoints=aggregate','LiveCaptives(map,faction)','p.MapHeld!=map','p.IsPrisonerOfColony || p.IsSlaveOfColony','seen.Add(p)'):
    assert token in threat,token
assert ops.index('NJRThreatBudgetS41.AllocateOperation(map,operation);') < ops.index('operations.Add(operation);')
assert ops.index('NJRThreatBudgetS41.AllocateOperation(map,op);') < ops.index('party.Attempted = true;')
assert 'NJRThreatBudgetS41.FactionAllocation(map,op,alternate)' in ops
assert 'Mathf.Max(1f,op.ThreatPoints),breach:' not in ops
assert '!budgetRejected && op.DebugForced' in ops
assert 'party.Points<rosterFloor' in ops and 'party.Points-rosterFloor' in ops
assert 'Mathf.Max(350f,NJRForceStrengthR017.HumanCoreFloor(party))' in comp
assert 'nativeSpent+(minimum-humans)*cheap>humanBudget' in force
assert 'party.PodCostR060=cost' in ops and 'party.PodCostR060' in force and 'op.PodSupplyCost' not in force
assert 'SourceImportance=debugImportance' in debug and 'traitorCase.Importance : 0' in debug
print('PASS R060: one budget per faction, independent hired wings, same-faction split, live captives, protected roster, actual kit payer; native execution UNVERIFIED')
