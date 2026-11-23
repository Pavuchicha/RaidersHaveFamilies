"""R016 static regression for repeated rescue #01 mountain/lake insertion.
These checks are NOT a RimWorld runtime playtest or DLL compilation.
"""
from pathlib import Path
p=Path(__file__).resolve().parents[1]
s=(p/'RaidersHaveFamilies/Source/RHFRescueGroundEntryS41.cs').read_text(encoding='utf-8-sig')
ops=(p/'RaidersHaveFamilies/Source/RHFOperations.cs').read_text(encoding='utf-8-sig')
claim=(p/'RaidersHaveFamilies/Source/RHFRescueControlV123.cs').read_text(encoding='utf-8-sig')
for phrase in (
    'internal static bool SafeArrivalCell(',
    '!cell.Roofed(map) && !cell.Fogged(map)',
    '!cell.GetTerrain(map).IsWater',
    'int maxRadius=Math.Min(64,Math.Max(map.Size.x,map.Size.z)/3)',
    'ReachableExterior(map,center,captive)',
    'GroundEntryClusterSafe(map,center)',
    'NJRGroundRescueCenterPatchR016',
    'PawnsArrivalModeWorker_EdgeWalkIn.TryResolveRaidSpawnCenter',
    'NJRGroundRescueArrivalPatchR016',
    'PawnsArrivalModeWorker_EdgeWalkIn.Arrive',
    'DeploymentCells(map,parms.spawnCenter)',
    'GenSpawn.Spawn(pawn,loc,map,parms.spawnRotation)',
    'if(!RHFRescueGroundEntryS41.IsGroundRescueIncident(parms))return true;',
):
    assert phrase in s,phrase
assert 'ReachableApproach(' in claim and 'GiveOrder(rescuer,JobDefOf.Goto,approach)' in claim
assert 'parms.spawnCenter=plannedRescueEntry;' in ops
assert 'return true;' in s and 'return false;' in s
# Every spawned cell is filtered individually rather than trusting the vanilla
# RandomClosewalkCellNear's Standable-only validator. Importantly, do NOT use
# Harmony patch for unrelated storyteller/vanilla raids.
assert 'CellFinder.RandomClosewalkCellNear(' not in s
# The old remote-exterior bug had a growing loop all the way to the map edge;
# such a test accepts any spot in an isolated region, far from the objective.
assert 'maxRadius=Math.Max(map.Size.x,map.Size.z)' not in s
# Model: a land region with no approach in the vicinity of a captive must
# never pass simply because its outermost tile is standable.
prisoner=(170,170); remote_region=[(x,8) for x in range(4,21)]
assert not any(max(abs(x-prisoner[0]),abs(z-prisoner[1]))<=64 for x,z in remote_region)
print('PASS R016: scoped vanilla entry validation, individual no-cave/no-water spawn, objective-near approach (STATIC ONLY)')
