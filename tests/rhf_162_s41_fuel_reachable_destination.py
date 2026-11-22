"""S4.1-A/M: fuel-range and two-faction destination selection regression.
Source contract, negative mutations and independent decision model. NOT a C# build.
"""
from pathlib import Path
src=(Path(__file__).resolve().parents[1]/'RaidersHaveFamilies/Source/RHFEnemyPodLaunchS41.cs').read_text('utf-8')
root=Path(__file__).resolve().parents[1]
workflow=(root/'.github/workflows/build.yml').read_text('utf-8')
stage5=(root/'RaidersHaveFamilies/Source/RHFVipEvacS311.cs').read_text('utf-8')
checks=[
    'TryDestination(op,map,launch,out PlanetTile destination)',
    'CompLaunchable launch,out PlanetTile destination)',
    'launch.FuelLevel<=0f',
    'launch.MaxLaunchDistanceAtFuelLevel(',
    'launch.FuelLevel,site.Tile.Layer',
    'Find.WorldGrid.TraversalDistanceBetween(',
    'if(distance<=0 || distance>range || distance>=shortest)continue;',
    'for(int priority=0;priority<2;priority++)',
    'if(destination.Valid)return true;',
    'if(++inspected>4096)break;',
    'return false;',
    'internal static bool DepartureInFlight(RHFOperation op,Map map)',
    'if(Reconcile(op,map))return true;',
    'ship.Contents?.innerContainer?.Contains(op.Vip)!=true',
]
for marker in checks:
    assert marker in src,repr(marker)
assert workflow.count('rhf_162_s41_fuel_reachable_destination.py')==1
assert 'TryDestination(op,map,out PlanetTile destination)' not in src
assert 'RHFEnemyPodLaunchS41.DepartureInFlight(op,map)' in stage5
assert 'if(RHFEnemyPodLaunchS41.TryLaunch(op,map,now) ||' in stage5

for marker in checks[:-1]:
    mutant=src.replace(marker,'/* BREAK CONTRACT */',1)
    assert mutant.count(marker)==src.count(marker)-1,marker

# Independent policy model: reachable sponsor first, then operator; never
# score an unreachable settlement as a valid native destination.
def choose(sponsor,operator,sites,max_range):
    for faction in (sponsor,operator):
        eligible=[(distance,name) for owner,name,distance in sites
                  if owner==faction and 0<distance<=max_range]
        if eligible:
            return min(eligible)[1]
    return None
assert choose('Sponsor','Operator',[("Sponsor","Far",35),("Operator","Near",8)],10)=='Near'
assert choose('Sponsor','Operator',[("Sponsor","Reachable",9),("Operator","Closer",2)],10)=='Reachable'
assert choose('Sponsor','Operator',[("Sponsor","A",55),("Operator","B",40)],12) is None
assert choose('Sponsor','Operator',[],10) is None
print('PASS S4.1-A/M: fuel-range selection and physical takeoff reconciliation (STATIC ONLY)')
