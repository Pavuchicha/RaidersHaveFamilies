"""R026 regression contracts and deterministic models for failures seen in-game.

These tests do not compile C# or instantiate RimWorld. Use the on-map
RHFRescueCommandV124 diagnostic to establish the real runtime cause.
"""
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
S=ROOT/'RaidersHaveFamilies/Source'
def source(n): return (S/n).read_text()
planner=source('RHFRescuePlannerS41.cs')
route=source('RHFObjectiveRouteV128.cs')
combat=source('RHFCombatPlannerV125.cs')
entry=source('RHFRescueGroundEntryS41.cs')
command=source('RHFRescueCommandV124.cs')
role=source('RHFRescueRoleJobGiverS41.cs')
assert '[NJR][R028] Resumable reverse route' in route
assert 'internal static void RejectUnworkableBarrier(' in route
assert 'rejectedBarriers.Contains(obstacle)' in route
assert 'holder.UnworkableBarriers[barrier]=now+1800;' in route
assert 'holder.Plan=null;' in route
assert 'PlanBounded(map,origins,captive,fresh,' in route
assert 'new HashSet<Thing>(holder.UnworkableBarriers.Keys)' in route
assert 'holder.UnworkableBarriers.Count>=8' in route
assert 'if(nativeAccess>0)' in planner
assert 'fighter.CanReach(captive,PathEndMode.Touch,Danger.Deadly)' in planner
assert 'board.Barrier=null;board.Breacher=null;board.Threat=null;' in planner
assert 'board.Assign(soldier,RHFRoleS41.Advance,captive,IntVec3.Invalid,now)' in planner
assert planner.index('if(nativeAccess>0)') < planner.index('RHFObjectiveRouteV128.ResolveShared(')
assert 'RHFObjectiveRouteV128.RejectUnworkableBarrier(op,obstacle,now)' in planner
assert 'SelectBreachOperator(op,map,obstacle,' in planner
assert 'RHFCombatPlannerV125.IsTurret(target)' in planner
assert 'if(!melee && !safeRifle)continue;' not in combat
assert 'return JobDefOf.AttackMelee;' in combat.split('internal static JobDef BreachAttack(Pawn pawn)')[1].split('internal static bool TryPaidBreach(')[0]
assert 'if(op.BoardS41?.Breacher!=pawn ||' in role
assert 'RHFObjectiveRouteV128.IsRescueStructuralBarrier(obstacle)' in role
assert 'bool overlaps=false;' in entry
assert 'if(existing.Side==proposed.Side &&' in entry
assert '(existing.Center-center).LengthHorizontalSquared<225' in entry
assert 'int minimumDanger=int.MaxValue;' in entry
assert 'System.Random debugRandom=op.DebugForced ?' in entry
assert 'Guid.NewGuid().GetHashCode()' in entry
assert 'Rand.Range(0,options.Count)' in entry
assert '(c.Center-history.Last).LengthHorizontalSquared>=225' in entry
assert 'R026 S41:' in command
assert 'RHFTacticalBoardS41 board=op.BoardS41;' in command
assert 'board?.Breacher?' in command
assert 'RHfIgnore' not in route

# A physically accessible captive wins over any synthetic obstruction;
# a blocked (unworkable) wall is excluded in the one permitted alternative.
def choose_action(native_path, planned_barrier, breacher_available, alternative):
    if native_path: return ('goto_captive', None)
    if not planned_barrier: return ('search', None)
    if breacher_available: return ('breach',planned_barrier)
    return ('reroute_without', planned_barrier) if alternative else ('report_unreachable', planned_barrier)
assert choose_action(True, 'wall', False, True)==('goto_captive',None)
assert choose_action(False,'wall',True,True)==('breach','wall')
assert choose_action(False,'wall',False,True)==('reroute_without','wall')
assert choose_action(False,'wall',False,False)==('report_unreachable','wall')

# Entry candidates need not share the exact nearest edge cell if multiple
# safe physical sectors exist. They never authorize water, rock or roofs.
def dispersed_spawns(candidates,dist_sq=225):
    kept=[]
    for x,z,side in sorted(candidates,key=lambda c:c[1]):
        if not any(s==side and (x-a)**2+(z-b)**2<dist_sq for a,b,s in kept):
            kept.append((x,z,side))
    return kept
c=[(0,z,0) for z in range(12,120,3)]+[(240,z,1) for z in range(12,120,3)]
result=dispersed_spawns(c)
assert len(result)>6 and len({v[2] for v in result})==2,result
print('PASS R026: actionable breach, native-access override, alternate route, dispersed debug entry, on-map diagnostics (not gameplay)')
