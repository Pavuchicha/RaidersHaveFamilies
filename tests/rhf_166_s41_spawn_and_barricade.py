"""S4.1-A/P: ground-retry and physical enemy barricade (STATIC ONLY)."""
from pathlib import Path
R=Path(__file__).resolve().parents[1];S=R/'RaidersHaveFamilies/Source'
ops=(S/'RHFOperations.cs').read_text()
pod=(S/'RHFVipEvacS311.cs').read_text()
planner=(S/'RHFPodPlannerS41.cs').read_text()
entry=(S/'RHFRescueGroundEntryS41.cs').read_text()
workflow=(R/'.github/workflows/build.yml').read_text()
checks=[
 (ops,'RHFRescueGroundEntryS41.Choose(map,op)'),
 (ops,'parms.spawnCenter=IntVec3.Invalid;'),
 (ops,'RHFRescueGroundEntryS41.Choose(map,op);'),
 (ops,'Native retry with safe rescue'),
 (ops,'refusing a random cave/remote map spawn'),
 (ops,'parms.raidArrivalMode=PawnsArrivalModeDefOf.EdgeWalkIn;'),
 (entry,'ReachableExterior(map,center,captive)'),
 (pod,'private Thing NativeFortificationWorkS41('),
 (pod,'GenConstruct.PlaceBlueprintForBuild('),
 (pod,'requested,target,map,Rot4.North,owner,op.SealStuffR052??ThingDefOf.Steel'),
 (pod,'op.CompletedBarricades.Add(target);'),
 (pod,'op.BarricadesBuilt++'),
 (pod,'now,fortifier,parallelWall,refueler,op.PodLauncherThing'),
 (pod,'CanSealR041(op,vip,target,protectedRoom,out HashSet<IntVec3> pocket)'),
 (pod,'NativeFortificationWorkS41(op,vip,owner.Faction,now,engineer,refueler,out sealMover,out sealCargo,out sealDestination)'),
 (pod,'RHFPodPlannerS41.Plan(op,map,engineer,'),
 (ops,'int bestScore=int.MaxValue;'),
 (ops,'tier*100000+risk+dist*6'),
 (planner,'RHFRoleS41.Engineer'),
 (workflow,'python3 tests/rhf_166_s41_spawn_and_barricade.py'),
]
for text,m in checks: assert m in text,repr(m)
for text,m in checks:
  mutant=text.replace(m,'/* removed */')
  assert m not in mutant
assert 'GenSpawn.Spawn(wall' not in pod
assert 'wall.SetFaction(' not in pod
print('PASS AP: verified ground rescue entry and physical enemy-owned barricades (STATIC ONLY)')
