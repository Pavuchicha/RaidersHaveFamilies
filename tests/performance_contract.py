"""Static TPS constraints, not runtime measurements."""
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
source=(ROOT/'RaidersHaveFamilies/Source/RaidersHaveFamilies.cs').read_text(encoding='utf-8')
operation=(ROOT/'RaidersHaveFamilies/Source/RHFOperations.cs').read_text(encoding='utf-8')
patch=(ROOT/'RaidersHaveFamilies/Source/RHFPatches.cs').read_text(encoding='utf-8')
for key in ['MaxBatchSize = 192','MaxPairProbes = 64','MaxRememberedIds = 8192',
            'HardMaxPerCycle = 48','HardMaxPerSave = 1024','CleanupGeneratedKin()',
            'ReuseExistingKin(', 'nextHostageCheck = tick + 30000']:
    assert key in source, key
# A one-time old-save backfill is explicitly allowed, but repeated scans are not.
scan=source[source.index('public override void MapComponentTick()',source.index('class RHFMapComponent')):
            source.index('private void Remember(int id)',source.index('class RHFMapComponent'))]
assert scan.count('AllPawnsSpawned') == 1
assert 'if (oneTimeBackfill && now > 120)' in scan and 'oneTimeBackfill = false' in scan
assert 'visited < MaxBatchSize' in scan
for key in ['DecisionInterval = 90','MaxOperationsPerMap = 4','MaxSquad = 96',
            'MaxOrdersPerDecision = 14','MaxTargets = 24','MaxOperationDuration = 180000',
            'operations.Count == 0', 'FindBarrier(', 'BuildAndLaunch(', 'ChooseExit(']:
    assert key in operation, key
for term in ['.Patch(typeof(TickManager)','Patch(typeof(Map)','WorldPawnGC']:
    assert term not in patch
assert 'TryTakeOrderedJob' in operation
print('PASS: bounded scheduled operations, no global tick Harmony hooks, bounded world pawn and batch generation.')
