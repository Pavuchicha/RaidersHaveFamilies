"""Targeted P0 regressions from gameplay, NOT a substitute for playing."""
from pathlib import Path
R=Path(__file__).resolve().parents[1]
S=R/'RaidersHaveFamilies/Source'
entry=(S/'RHFRescueGroundEntryS41.cs').read_text()
route=(S/'RHFObjectiveRouteV128.cs').read_text()
job=(S/'RHFRescueRoleJobGiverS41.cs').read_text()
fort=(S/'RHFVipEvacS311.cs').read_text()
release=(R/'.github/workflows/build.yml').read_text()
assert 'ReachableExterior(map,edge,captive)' in entry
assert 'SafeRescueDropPad(map,nearby)' in (S/'RHFOperations.cs').read_text()
assert 'TraverseMode.PassDoors,Danger.Deadly' in entry
assert 'PassAllDestroyableThings' not in entry
assert 'building is Building_Door ? work' in route
assert 'if(!route.RiskFallback && LateralExposed(map,from,to))return true;' in route
assert 'plan.RiskFallback=!forbidTurretExposure;' in route
assert 'if(route.FirstTurretExposureIndex>=0 && !route.RiskFallback)' in route
follow=job.split('case RHFRoleS41.FollowEscort:')[-1].split('case RHFRoleS41.Withdraw:')[0]
assert 'JobDefOf.Goto,order.Cell' not in follow
assert 'JobDefOf.Goto,escort,120' in follow
assert 'CrossesTurretLane(map,soldier.Position,captive.Position)' in (S/'RHFRescuePlannerS41.cs').read_text()
assert 'if(op.PodConstructionStage==3 &&' in fort
assert 'NativeEscapeLaneS41(op.PodLauncherCell,cell)' in fort
assert 'adjacentDoor' in fort and 'cell.GetRoom(map)!=protectedRoom' in fort
assert 'NJRPodSiteClearS41.Blocker(op,map,launcherDef,podDef,vip)' not in fort
assert 'NJRRescueExitS41.Choose(map,op,navigator)' in (S/'RHFRescuePlannerS41.cs').read_text()
assert '_release/NJR-NotJustRaiders' in release
print('PASS NJR 204: physical edge, turret/doors, convoy, door-adjacent walls, named install folder (STATIC)')
