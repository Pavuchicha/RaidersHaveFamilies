"""S4.1-A/P: direct destination keeps real range checks, distant site uses relay.
Static source contract (not runtime). The original A/M contract excluded distant
settlements and is deliberately superseded by the user's relay-hop doctrine.
"""
from pathlib import Path
R=Path(__file__).resolve().parents[1]
s=(R/'RaidersHaveFamilies/Source/RHFEnemyPodLaunchS41.cs').read_text()
for m in ('TryDestination(op,map,launch,out PlanetTile destination,',
          'out PlanetTile finalDestination,out bool preparedRelay)',
          'launch.MaxLaunchDistanceAtFuelLevel(',
          'Find.WorldGrid.TraversalDistanceBetween(',
          'if(realDistance>=0 && realDistance<=range)',
          'preparedRelay=true;',
          'world.destinationTile=action.FinalDestination;',
          'world.ContainsPawn(action.Vip)'):
    assert m in s,m
assert 'if(distance<=0 || distance>range || distance>=shortest)continue;' not in s
assert 'op.PodLaunchCompleted=true;' not in s.split('internal static bool TryLaunch',1)[1].split('internal static void WorldHandoff',1)[0]
print('PASS AP: reachable direct and remote relay, no premature VIP success (STATIC ONLY)')
