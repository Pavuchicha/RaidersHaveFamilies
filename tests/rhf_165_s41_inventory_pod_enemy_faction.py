"""Inventory-backed enemy-native pod siege regression. Static only, not C# or game."""
from pathlib import Path
import xml.etree.ElementTree as ET
R=Path(__file__).resolve().parents[1]
S=R/'RaidersHaveFamilies/Source'
P=(S/'RHFVipEvacS311.cs').read_text('utf8')
B=(S/'RHFPodPlannerS41.cs').read_text('utf8')
J=(S/'JobDriver_RHFPodCargoS41.cs').read_text('utf8')
ROLE=(S/'RHFRescueRoleJobGiverS41.cs').read_text('utf8')
T=(S/'RHFTacticalBoardS41.cs').read_text('utf8')
O=(S/'RHFOperations.cs').read_text('utf8')
X=(R/'RaidersHaveFamilies/Defs/JobDefs/RHF_PodCargoS41.xml').read_text('utf8')
W=(R/'.github/workflows/build.yml').read_text('utf8')

def verify(p=P,b=B,j=J,role=ROLE,t=T,o=O,x=X,w=W):
    assert 'GenSpawn.Spawn(stack' not in p, 'native supply must not fall from nowhere'
    assert 'carrier.inventory.innerContainer.TryAdd(stack,' in p
    assert 'MassUtility.WillBeOverEncumberedAfterPickingUp' in p
    assert 'op.PodCargoStacksS41.Add(stack);' in p
    assert 'op.PodSuppliesIssued = true;' in p
    assert 'Scribe_Collections.Look(ref PodCargoStacksS41' in o
    assert 'op.PodCargoStacksS41.Count>0' in o
    assert 'RHFPodSupplyS41.UnloadCell(carrier,op,map)' in b
    assert 'board.Assign(carrier,RHFRoleS41.PodSupply,null,unload,now)' in b
    assert '!occupied.Contains(engineer)' in b
    assert 'RHFPodSupplyS41.HasCargo(op,pawn)' in role
    assert 'RHF_UnloadPodCargoS41' in role
    assert 'RHFRoleS41.PodSupply' in role and 'PodSupply = 17' in t
    assert 'Toils_Goto.GotoCell(TargetIndex.A,PathEndMode.OnCell)' in j
    assert 'inventory.TryDrop(item,carrier.Position,carrier.Map,' in j
    assert 'carrier.Faction!=op.PodCargoOwnerFactionS311' in j
    assert 'RHFPodSupplyS41.Unload(op,pawn);' in j
    assert '<driverClass>RaidersHaveFamilies.JobDriver_RHFPodCargoS41</driverClass>' in x
    assert ET.fromstring(x).find('.//JobDef/defName').text=='RHF_UnloadPodCargoS41'
    assert 'owner.Faction==Faction.OfPlayer' in p
    assert 'site.Faction!=owner.Faction || engineer.Faction!=owner.Faction' in p
    assert 'GenConstruct.PlaceBlueprintForBuild(requested, cell,' in p
    assert 'map, first ? NJRPodSiteClearS41.RotationFor(op.PodLauncherCell,op.PodCell) : Rot4.North, owner.Faction, null' in p
    assert 'NativeLaunchReadyS41(launch,op.PodLauncherThing,owner.Faction)' in p
    assert 'return launch.CanLaunch();' in p
    assert 'FuelingPortSource' not in p and 'fuelingPortSource' not in p
    assert 'EnsureMinLevelWithMargin(required)' in p
    assert 'python3 tests/rhf_165_s41_inventory_pod_enemy_faction.py' in w
verify()
mutations=[
    ('p','carrier.inventory.innerContainer.TryAdd(stack,','GenSpawn.Spawn(stack,'),
    ('p','MassUtility.WillBeOverEncumberedAfterPickingUp','true /* ignoring encumbrance */'),
    ('p','op.PodCargoStacksS41.Add(stack);','// cargo not recorded'),
    ('p','site.Faction!=owner.Faction || engineer.Faction!=owner.Faction','false'),
    ('p','map, first ? NJRPodSiteClearS41.RotationFor(op.PodLauncherCell,op.PodCell) : Rot4.North, owner.Faction, null','map, first ? NJRPodSiteClearS41.RotationFor(op.PodLauncherCell,op.PodCell) : Rot4.North, Faction.OfPlayer, null'),
    ('p','return launch.CanLaunch();','return true;'),
    ('p','EnsureMinLevelWithMargin(required)','EnsureMinLevelWithMargin(0)'),
    ('b','RHFPodSupplyS41.UnloadCell(carrier,op,map)','IntVec3.Invalid'),
    ('b','!occupied.Contains(engineer)','true'),
    ('j','Toils_Goto.GotoCell(TargetIndex.A,PathEndMode.OnCell)','Toils_General.Wait(0)'),
    ('j','carrier.Faction!=op.PodCargoOwnerFactionS311','false'),
    ('x','RaidersHaveFamilies.JobDriver_RHFPodCargoS41','Verse.AI.JobDriver_Wait'),
]
orig=dict(p=P,b=B,j=J,role=ROLE,t=T,o=O,x=X,w=W)
for key,old,new in mutations:
    assert old in orig[key],(key,old)
    state=dict(orig); state[key]=state[key].replace(old,new,1)
    try:verify(**state)
    except (AssertionError,AttributeError,ET.ParseError):continue
    raise AssertionError('mutation undetected '+key+' '+old)
print('PASS: physical cargo in pawn inventories, owner-bound construction, native launch check, 12 negative mutations (STATIC ONLY)')
