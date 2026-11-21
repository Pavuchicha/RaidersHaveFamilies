"""Static bounding checks; not a substitute for RimWorld 1.7 profiling."""
from pathlib import Path
import re
ROOT = Path(__file__).resolve().parents[1]
source=(ROOT/'RaidersHaveFamilies/Source/RaidersHaveFamilies.cs').read_text(encoding='utf-8')
diplomacy=(ROOT/'RaidersHaveFamilies/Source/RHFHostageSystems.cs').read_text(encoding='utf-8')
patches=(ROOT/'RaidersHaveFamilies/Source/RHFPatches.cs').read_text(encoding='utf-8')
for key in ['MaxBatchSize = 144','MaxPairProbes = 32','MaxRememberedIds = 8192',
            'HardMaxPerCycle = 8','HardMaxPerSave = 512','CleanupGeneratedKin()',
            'ActiveGeneratedKin','nextHostageCheck = tick + 30000']:
    assert key in source, key
assert 'AllPawnsSpawned' not in source[source.index('public override void MapComponentTick()',source.index('class RHFMapComponent')):
                                       source.index('private void Remember(int id)',source.index('class RHFMapComponent'))], 'Map tick cannot scan all pawns'
assert '.Patch(typeof(TickManager)' not in patches
assert 'MapComponentTick' not in patches
assert 'WorldPawnGC' not in patches
assert 'new List<Pawn>(48)' in source
assert 'Math.Min(200' in diplomacy
assert 'Math.Min(90' in diplomacy
print('PASS: event-driven entry, bounded arrival / pair search / world kin / memory thoughts; no game tick patches.')
