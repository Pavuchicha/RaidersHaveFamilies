"""NJR S4.1 P0 follow-up safety contracts (STATIC ONLY, NOT gameplay)."""
from pathlib import Path
R=Path(__file__).resolve().parents[1]
S=R/'RaidersHaveFamilies/Source'
ops=(S/'RHFOperations.cs').read_text()
entry=(S/'RHFRescueGroundEntryS41.cs').read_text()
plan=(S/'RHFRescuePlannerS41.cs').read_text()
pod=(S/'RHFPodPlannerS41.cs').read_text()
air=(S/'RHFVipEvacS311.cs').read_text()
clear=(S/'NJRPodSiteClearS41.cs').read_text()
supply=(S/'JobDriver_RHFPodCargoS41.cs').read_text()
# A rescue cannot be rerouted to an arbitrary edge if safe placement was refused.
assert 'if(!parms.spawnCenter.IsValid)' in ops
assert 'refusing a random cave/remote map spawn' in ops
assert 'Native retry with safe rescue' in ops
assert 'parms.spawnCenter=plannedRescueEntry;' in ops
retry=ops[ops.index('if (!accepted && !party.Drop'):ops.index('catch (Exception e)',ops.index('if (!accepted && !party.Drop'))]
assert 'parms.spawnCenter=IntVec3.Invalid;' in retry  # only non-rescue retry
assert retry.index('else {parms.raidArrivalMode=null;') < retry.index('parms.spawnCenter=IntVec3.Invalid;')
assert 't++' in entry and 'GroundEntryClusterSafe(map,center)' in entry
# Candidate can't choose a destructive launch pad while a free footprint exists.
assert 'if(clearance!=0)continue;' in ops
assert '!vip.CanReach(cell,PathEndMode.OnCell,Danger.Deadly)' in ops
assert '!vip.CanReach(vehicle,PathEndMode.OnCell,Danger.Deadly)' in ops
assert 'vehicle.GetRoom(map)!=candidate' in ops
assert 'roof!=null && roof.isThickRoof' in ops  # thin roofs can be worked physically
assert 'c==vip.Position' in clear  # never overlap the VIP's actual tile
assert 'occupant.GetLord()?.LordJob is RHFRescueLordJobV124' in clear
# R021: route planning uses every eligible assault member as a source, not
# an elected leader. A fallback, when allowed, is only a physical approach
# from a sample rescuer and NEVER a guessed independent demolition target.
assert 'RHFObjectiveRouteV128.ResolveShared(' in plan
assert 'op,map,op.Squad,escortedDuringAssault,captive,now)' in plan
assert 'if(sample==null)sample=member;' in plan
assert 'ChooseReachableApproach(sample,captive,map)' in plan
# An exhausted strategic search is unknown, NOT proof that a valid
# vanilla walkable approach is forbidden. Never turn it into a wall permit.
assert 'IntVec3 fallback=route.Valid ? IntVec3.Invalid :' in plan
assert 'ChooseReachableApproach(sample,captive,map)' in plan
assert 'if(!route.Valid)obstacle=null;' in plan
assert 'if(!route.Valid)obstacle=null;' in plan
assert 'soldier.CanReach(fallback,PathEndMode.OnCell,Danger.Some)' in plan
assert 'if(way.IsValid)' in plan
assert 'board.Assign(soldier,RHFRoleS41.Advance,null,way,now);' in plan
assert 'board.Assign(soldier,RHFRoleS41.Hold,null,soldier.Position,now);' in plan
assert 'ChooseReachableApproach(lead,captive,map)' not in plan
assert 'leadCost' not in plan
# Guard/haul assignments remain stable and avoid the native footprint.
assert 'ValidGuardCell(previous.Cell,op,map,positions)' in pod
assert 'InFootprint(cell,op.PodCell,' in pod
assert 'bool launcherFloor=' in supply and 'bool podFloor=' in supply
assert 'if(launcherFloor || podFloor ||' in supply
# Narrow actual passages, not decorative lawn walls, with bounded job time.
assert 'if(!(n&&s) && !(e&&w))continue;' in air
assert 'if(seenDoors==0 ||' in air
assert air.index('if(!(n&&s) && !(e&&w))continue;') < air.index('!OpenRouteExcept(vip.Position,op.PodCell,cell)')
assert 'op.PodStageStartedTickS311=now;' in air
print('PASS NJR S4.1 P0: safe entry, active rally, non-destructive pod, stable guards and real corridor checks (STATIC ONLY).')
