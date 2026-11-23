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
assert 't++' in entry and 'GroundEntryClusterSafe(map,edge)' in entry
# Candidate can't choose a destructive launch pad while a free footprint exists.
assert 'if(clearance!=0 ||' in ops
assert 'vehicle.GetRoom(map)!=candidate' in ops
assert 'LengthHorizontalSquared<9' in ops
assert 'occupant.GetLord()?.LordJob is RHFRescueLordJobV124' in clear
# Every fighter receives a mission assignment after a failed route solution.
assert 'ChooseReachableApproach(lead,captive,map)' in plan
assert 'soldier.CanReach(fallback,PathEndMode.OnCell,Danger.Some)' in plan
assert 'if(cost<leadCost){lead=candidate;leadCost=cost;}' in plan
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
