"""S3.1.1 static contracts and negative mutations (NOT a C# or in-game test)."""
from pathlib import Path
r = Path(__file__).resolve().parents[1]
ops = (r/'RaidersHaveFamilies/Source/RHFOperations.cs').read_text('utf8')
native = (r/'RaidersHaveFamilies/Source/RHFVipEvacS311.cs').read_text('utf8')
diag = (r/'RaidersHaveFamilies/Source/RHFVipDiagnosticsS311.cs').read_text('utf8')
ci = (r/'.github/workflows/build.yml').read_text('utf8')

def contract(o,n,d,w):
    for marker in (
        'public sealed partial class RHFOperationController',
        'Scribe_Values.Look(ref PodNativeS311',
        'Scribe_References.Look(ref PodCargoOwnerFactionS311',
        'Scribe_Collections.Look(ref PodFuelStacksS311',
        'fuel*ThingDefOf.Chemfuel.BaseMarketValue',
        'NativeIssueCargoOnArrivalS311(op, now);',
        'BuildAndLaunchNativeS311(op,now);',
        'legacy untracked kit cannot be upgraded mid-save',
    ): assert marker in o, marker
    for marker in (
        'party.Succeeded && party.Drop',
        'party.Role == (int)RHFStrikeRole.Rescue',
        'pawn.Faction == party.Faction',
        'op.PodSuppliesIssued = true;',
        'carrier.inventory.innerContainer.TryAdd(stack,',
        'EnableAndInitializeIfNotAlreadyInitialized()',
        'SetPriority(WorkTypeDefOf.Construction,3)',
        'SetPriority(WorkTypeDefOf.Hauling,3)',
        'site = GenConstruct.PlaceBlueprintForBuild(requested, cell',
        'Blueprint_Build plan=GenConstruct.PlaceBlueprintForBuild(',
        'ConstructDeliverResourcesToBlueprints',
        'ConstructDeliverResourcesToFrames',
        'ConstructFinishFrames',
        'NativeJobFromWorkGiverS311(engineer, launchThing, "Refuel")',
        'comp.groupID = Find.UniqueIDsManager.GetNextTransporterGroupID()',
        'JobDefOf.EnterTransporter',
        'comp.innerContainer.Contains(vip)',
        'NativeFallBackS311(op, now, "native hostile pod launch not yet supported (S3.2)")',
        'transporter.innerContainer.TryDrop(op.Vip',
        'NativeDefendSiteS311(op, engineer, now)',
        'IsCombatEmergencyJob(guard)',
        'NativeVipWorkTimeoutS311',
    ): assert marker in n, marker
    # S4.1-A/O uses native CanLaunch; the old runtime-reflection field was
    # never a dependable RimWorld fueling-port contract.
    assert '.ConnectedToFuelingPort' not in n
    assert '.FuelingPortSourceHasAnyFuel' not in n
    assert 'FuelingPortSource' not in n
    assert 'NativeLaunchReadyS41(launch,op.PodLauncherThing,owner.Faction)' in n
    assert 'return launch.CanLaunch();' in n
    assert 'site.Faction!=owner.Faction || engineer.Faction!=owner.Faction' in n
    assert 'SkyfallerMaker' not in n
    assert 'ThingMaker.MakeThing(requested)' not in n
    assert 'GenSpawn.Spawn(completed' not in n
    assert 'op.PodLaunchCompleted = true' not in n
    assert '34 — S3.1.1' in d
    assert 'python3 tests/rhf_144_s311_vip_native.py' in w

contract(ops,native,diag,ci)
mutants = [
    ('owner drop gate', (ops,native.replace('party.Succeeded && party.Drop','party.Succeeded && true',1),diag,ci)),
    ('native launch unchecked', (ops,native.replace('return launch.CanLaunch();','return true;',1),diag,ci)),
    ('faction filter', (ops,native.replace('pawn.Faction == party.Faction','pawn.Faction != party.Faction',1),diag,ci)),
    ('budget fuel', (ops.replace('fuel*ThingDefOf.Chemfuel.BaseMarketValue','0f',1),native,diag,ci)),
    ('real blueprint', (ops,native.replace('site = GenConstruct.PlaceBlueprintForBuild(requested, cell','site = GenSpawn.Spawn(requested, cell',1),diag,ci)),
    ('inventory cargo', (ops,native.replace('carrier.inventory.innerContainer.TryAdd(stack,','GenSpawn.Spawn(stack,',1),diag,ci)),
    ('loading group', (ops,native.replace('comp.groupID = Find.UniqueIDsManager.GetNextTransporterGroupID()','comp.groupID = -1',1),diag,ci)),
    ('rollback', (ops,native.replace('transporter.innerContainer.TryDrop(op.Vip','transporter.innerContainer.TryAdd(op.Vip',1),diag,ci)),
    ('diagnostic', (ops,native,diag.replace('34 — S3.1.1','30 — S3.1.1',1),ci)),
    ('CI omitted', (ops,native,diag,ci.replace('python3 tests/rhf_144_s311_vip_native.py','python3 tests/rhf_143_s31_native_cargo.py',1))),
]
for label, inputs in mutants:
    try: contract(*inputs)
    except AssertionError: continue
    raise AssertionError('Undetected unsafe mutation: '+label)
print('PASS: S3.1.1 static native-work, cargo, faction, rollback and guard contracts; 10 mutations. NOT gameplay.')
