"""S4.1-A/P: physical first-leg / prepared relay route contracts (STATIC ONLY).

The first leg consumes real native pod fuel. A real TravellingTransporters
object carries the VIP and is retargeted to an actual settlement after its
physical map->world handoff. No relay maps/pads are generated.
"""
from pathlib import Path
R=Path(__file__).resolve().parents[1]
s=(R/'RaidersHaveFamilies/Source/RHFEnemyPodLaunchS41.cs').read_text('utf-8')
n=(R/'RaidersHaveFamilies/Source/RHFMissionIntegrityV130.cs').read_text('utf-8')
workflow=(R/'.github/workflows/build.yml').read_text('utf-8')
markers=[
    'CompLaunchable launch,out PlanetTile destination,',
    'out PlanetTile finalDestination,out bool preparedRelay)',
    'if(!finalDestination.Valid)return false;',
    'launch.MaxLaunchDistanceAtFuelLevel(',
    'Find.WorldGrid.TraversalDistanceBetween(',
    'Find.WorldGrid.GetTileNeighborCount(map.Tile)',
    'Find.WorldGrid.GetTileNeighbor(map.Tile,j)',
    'FirstLegDestination=destination',
    'FinalDestination=finalDestination',
    'UsesPreparedRelay=preparedRelay',
    'Scribe_Values.Look(ref UsesPreparedRelay',
    'Scribe_Values.Look(ref FinalDestination',
    'Scribe_Values.Look(ref FirstLegDestination',
    'launch.TryLaunch(destination,action);',
    'ReferenceEquals(flight.arrivalAction,action)',
    'flight.Contents?.innerContainer?.Contains(op.Vip)==true',
    'world.destinationTile==action.FirstLegDestination',
    'world.ContainsPawn(action.Vip)',
    'world.destinationTile=action.FinalDestination;',
    'op.PodLaunchCompleted=true;',
]
for mark in markers: assert mark in s, mark
assert 'промежуточные площадки' in n
assert 'python3 tests/rhf_167_s41_prepared_relay.py' in workflow
for marker in markers:
    corrupted=s.replace(marker,'/* CONTRACT MUTATED */')
    assert marker not in corrupted, marker
    assert not all(m in corrupted for m in markers)

def choose(first_faction,second_faction,sites,range_tiles):
    for faction in [first_faction,second_faction]:
        dest=sorted((d,name) for owner,name,d in sites if owner==faction and d>0)
        if dest:
            d,name=dest[0]
            return name,'direct' if d<=range_tiles else 'prepared_relay'
    return None
assert choose('sponsor','operator',[('sponsor','far',50),('operator','near',2)],10)==('far','prepared_relay')
assert choose('sponsor','operator',[('sponsor','near',7)],10)==('near','direct')
assert choose('sponsor','operator',[('operator','far',90)],10)==('far','prepared_relay')
assert choose('sponsor','operator',[],10)==None
print('PASS AP: physical flight to first tile, real serialized world VIP, prepared relay routing and briefing; STATIC ONLY')
