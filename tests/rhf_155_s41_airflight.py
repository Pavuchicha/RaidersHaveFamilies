"""S4.1-A/F airlift safety contracts: static, not gameplay or C# compiler."""
from pathlib import Path
R=Path(__file__).resolve().parents[1]
code=(R/'RaidersHaveFamilies/Source/RHFEnemyPodLaunchS41.cs').read_text('utf-8')
vanilla=(R/'RaidersHaveFamilies/Source/RHFVipEvacS311.cs').read_text('utf-8')
planner=(R/'RaidersHaveFamilies/Source/RHFRescuePlannerS41.cs').read_text('utf-8')
w=(R/'.github/workflows/build.yml').read_text('utf-8')
checks=[
 'class RHFEnemyPodArrivalS41 : TransportersArrivalAction',
 'public override bool GeneratesMap => false;',
 'Arrived(List<ActiveTransporterInfo> transporters,',
 'Scribe_References.Look(ref Vip',
 'Scribe_References.Look(ref Operator',
 'Scribe_Values.Look(ref OperationCreatedTick',
 'holder.Remove(p);',
 'Find.WorldPawns.PassToWorld(p,PawnDiscardDecideMode.KeepForever);',
 'CompLaunchable launch=craft.GetComp<CompLaunchable>();',
 'cargo.innerContainer.Contains(op.Vip)',
 'launch.CanLaunch()',
 'launch.TryLaunch(destination,action);',
 'ReferenceEquals(flight.arrivalAction,action)',
 'flight.Contents?.innerContainer?.Contains(op.Vip)==true',
 'Reconcile(RHFOperation op,Map origin)',
 'WorldHandoff(RHFEnemyPodArrivalS41 action,',
 'world.ContainsPawn(action.Vip)',
 'world.SetFaction(action.Operator)',
 'op.PodLaunchCompleted=true;',
 'op.Phase=2;',
 'op.BoardS41.Orders.Clear();',
 '[HarmonyPatch(typeof(FlyShipLeaving),"LeaveMap")]',
 'ReferenceEquals(travel.arrivalAction,__state.Action)',
 'TravellingTransporters travel=objects[i] as TravellingTransporters;',
]
for chk in checks:assert chk in code,chk
assert 'if(RHFEnemyPodLaunchS41.TryLaunch(op,map,now)' in vanilla
assert 'if (op.PodConstructionStage == 6)' in vanilla
assert 'RHFEnemyPodLaunchS41.Reconcile(op,map)' in vanilla
assert 'if(op.AssaultAfterObjective && (op.EscapedWithEscort>0 || op.PodLaunchCompleted))' in planner
assert 'python3 tests/rhf_155_s41_airflight.py' in w
assert 'op.PodLaunchCompleted=true;' in code and 'op.PodLaunchCompleted=true;' not in code.split('internal static bool TryLaunch',1)[1].split('internal static void WorldHandoff',1)[0]
for n,old in enumerate(checks,1):
    corrupted=code.replace(old,'/* removed */',1)
    assert old not in corrupted and not all(c in corrupted for c in checks),n
print('PASS S4.1-A/F airlift: '+str(len(checks))+' negative contract mutations; STATIC ONLY')
