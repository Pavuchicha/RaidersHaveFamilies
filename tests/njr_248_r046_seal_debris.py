"""R046 source contracts + clearance state model; not a game/CE test."""
from pathlib import Path
r=Path(__file__).resolve().parents[1];s=r/'RaidersHaveFamilies/Source'
d=(s/'NJRVipDebrisR045.cs').read_text();v=(s/'RHFVipEvacS311.cs').read_text();j=(s/'RHFRescueRoleJobGiverS41.cs').read_text();p=(s/'RHFPodPlannerS41.cs').read_text();driver=(s/'JobDriver_RHFPodCargoS41.cs').read_text()
logistics=v[v.index('private bool SealLogisticsR044'):v.index('private Thing NativeFortificationWorkS41')]
fort=v[v.index('private Thing NativeFortificationWorkS41'):v.index('private bool SolidFlankS41')]
assert logistics.index('DebrisAt(map,wall)')<logistics.index('if(op.PodCargoStacksS41==null)return true;')
assert 'TryMove(op,map,debris,pocket,engineer,refueler,out mover,out destination)' in logistics
assert 'ClearableCell(op,map,cell)' in fort and 'ClearableCell(op,map,target)' in fort
assert fort.index('if(!SealLogisticsR044(op,target,pocket')<fort.index('Blueprint_Build plan=GenConstruct.PlaceBlueprintForBuild')
assert 'op.PodConstructionStage<1 || op.PodConstructionStage>4' in d and 'order.Objective==thing' in d
assert 'candidate==engineer || candidate==refueler' in d and 'SafeDrop(op,map,c,candidate)' in d
assert 'cell==op.BarricadeCell || NJRPodSiteClearS41.ReservedFootprint(op,cell)' in d
assert 'pocket!=null && !pocket.Contains(cell)' in d and 'LocalBypass(op,map,cell)' in d
assert 'NJRVipDebrisR045.IsAssigned(op,pawn,cargo)' in j and 'Toils_Haul.StartCarryThing' in driver
assert 'if(debris && !NJRVipDebrisR045.SafeDrop' in driver and 'if(op==null || carried==null || debris)return;' in driver
assert 'sealMover!=engineer && sealMover!=refueler' in p
assert 'footprintDebris==null && sealMover==null' in v and 'footprint debris could not be physically relocated' in v
# State model: at all construction stages, untracked slag blocks seal until
# actual pickup + delivery; moving must not take the builder/refueler.
for stage in range(1,5):
    wall=(1,1);pad={(2,2),(2,3)};pocket={(1,2),(1,3),(0,3),(0,2)}
    chunks={wall};cargo=set();roles={'A':'builder','B':'fuel','C':'guard','D':'free'}
    def choose():
        return next((n for n,role in roles.items() if role=='free'),None)
    def ready(in_flight):return wall not in chunks and not in_flight
    assert not ready(False) and not cargo
    worker=choose();assert worker=='D';roles[worker]='haul'
    chunks.remove(wall);assert not ready(True)
    target=(0,3);assert target in pocket and target not in pad and target!=wall
    chunks.add(target);assert ready(False)
    assert roles['A']=='builder' and roles['B']=='fuel' and roles['C']=='guard'
print('PASS R046: stage 1-4 physical seal clearance, protected roles, footprint recovery (source/model only)')
