"""Targeted P0 regressions from gameplay, NOT a substitute for playing."""
from pathlib import Path
R=Path(__file__).resolve().parents[1]
S=R/'RaidersHaveFamilies/Source'
entry=(S/'RHFRescueGroundEntryS41.cs').read_text()
route=(S/'RHFObjectiveRouteV128.cs').read_text()
job=(S/'RHFRescueRoleJobGiverS41.cs').read_text()
fort=(S/'RHFVipEvacS311.cs').read_text()
release=(R/'.github/workflows/build.yml').read_text()
assert 'ReachableExterior(map,center,captive)' in entry
assert 'SafeRescueDropPad(map,nearby)' in (S/'RHFOperations.cs').read_text()
assert 'TraverseMode.PassDoors,Danger.Deadly' in entry
assert 'PassAllDestroyableThings' not in entry
assert 'building is Building_Door ?' in route and 'DoorBreachBaseR024+work' in route
assert 'if(!route.RiskFallback && LateralExposed(map,from,to))return true;' in route
assert 'plan.RiskFallback=plan.FirstTurretExposureIndex>=0;' in route
assert 'if(route.FirstTurretExposureIndex>=0 && !route.RiskFallback)' in route
follow=job.split('case RHFRoleS41.FollowEscort:')[-1].split('case RHFRoleS41.Withdraw:')[0]
assert 'return Task(JobDefOf.Goto,order.Cell,360);' in follow
assert 'JobDefOf.Goto,escort,120' not in follow
assert 'if(gap>16)return Task(JobDefOf.Wait,pawn.Position,30);' not in follow
# A direct LOS-to-VIP line is NOT the same as the actual native walking
# path. A failed strategic search must not mask confirmed prisoner access.
plan=(S/'RHFRescuePlannerS41.cs').read_text()
assert 'else if(obstacle==null && !route.Valid &&' in plan
assert 'soldier.CanReach(captive,PathEndMode.Touch,Danger.Deadly)' in plan
assert 'board.Assign(soldier,RHFRoleS41.Advance,captive,IntVec3.Invalid,now);' in plan
assert 'if(!route.Valid)obstacle=null;' in plan
assert 'op.PodConstructionStage>=1 &&' in fort
assert 'CanSealR041(op,vip,cell,protectedRoom)' in fort
assert 'adjacentDoor' in fort and 'cell.GetRoom(map)!=protectedRoom' in fort
assert 'NJRPodSiteClearS41.Blocker(op,map,launcherDef,podDef,vip)' not in fort
assert 'NJRRescueExitS41.Choose(map,op,navigator)' in (S/'RHFRescuePlannerS41.cs').read_text()
assert '_release/NJR-NotJustRaiders' in release
print('PASS NJR 204: physical edge, turret/doors, convoy, door-adjacent walls, named install folder (STATIC)')
