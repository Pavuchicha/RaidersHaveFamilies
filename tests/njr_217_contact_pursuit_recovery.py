"""R011 tactical-contract regression; complements manual source audit.
NOT a RimWorld simulation, API check, or evidence that combat works in-game.
"""
from pathlib import Path
s=Path(__file__).resolve().parents[1] / 'RaidersHaveFamilies' / 'Source'
contact=(s/'NJRCombatContactS44.cs').read_text()
rescue=(s/'RHFRescueRoleJobGiverS41.cs').read_text()
planner=(s/'RHFRescuePlannerS41.cs').read_text()
exitpath=(s/'NJRRescueExitS41.cs').read_text()
offense=(s/'RHFOffenseS41.cs').read_text()
pod=(s/'RHFPodPlannerS41.cs').read_text()
vip=(s/'RHFVipEvacS311.cs').read_text()
orb=(s/'NJROrbitalSupportS42.cs').read_text()
operation=(s/'RHFOperations.cs').read_text()
combat=(s/'RHFCombatPlannerV125.cs').read_text()
workflow=(s.parents[1]/'.github/workflows/build.yml').read_text()

assert 'NJRCombatContactS44.Respond' in rescue and 'NJRCombatContactS44.Respond' in offense
assert 'SpawnedPawnsInFaction(Faction.OfPlayer)' in contact
assert 'RHFCombatPlannerV125.IsTurret(gun)' in contact
assert 'GenSight.LineOfSight' in contact
assert 'RHFRescueFireDisciplineV136.UnsafeLine' in contact
assert 'JobDefOf.AttackStatic' in contact and 'JobDefOf.AttackMelee' in contact
assert 'soldier.CanReach(cells[i],PathEndMode.OnCell,Danger.Deadly)' in contact
assert 'if(cells.Count>20)' in contact  # source is a bounded list, not a full-map pathing scan
assert 'NJRCombatContactS44.BreachToward' in rescue and 'NJRCombatContactS44.BreachToward' in offense

# Phase3: defenders intercept routinely; the pinned engineer builds while
# someone can defend, but a SOLO remaining builder may fight for survival.
section=rescue.split('if(op.Phase==3)',1)[1].split('if(op.Phase!=1',1)[0]
assert 'phase3Role==RHFRoleS41.Screen' in section
assert 'phase3Role==RHFRoleS41.Hold' in section
assert 'else if(phase3Role==RHFRoleS41.Engineer)' in section
assert 'if(!HasActivePodGuard(op,pawn))' in section
assert 'AdjacentObservedThreat(op,pawn)' in section
assert 'else if(phase3Role==RHFRoleS41.PodSupply)' in section
assert 'case RHFRoleS41.Engineer:' in section
assert 'return RHFPodPlannerS41.WorkJob(pawn,phase3.Objective);' in section
assert 'board.Assign(engineer,RHFRoleS41.Engineer,work,assembly,now)' in pod
assert 'RHFPodSupplyS41.HasCargo(op,p) ? 100000 : 0' in vip
assert 'if(++checkedPawns>16)break;' not in vip

# A rearguard exists ONLY after a PHYSICALLY OBSERVED chaser has moved
# closer to the retreating column. Static guards/turrets do not qualify.
assert 'before!=enemy.Position' in planner
assert '(before-memory.PreviousConvoy).LengthHorizontalSquared' in planner
assert 'memory.PreviousConvoy=convoy;' in planner
assert 'GenSight.LineOfSight(observer.Position,enemy.Position,map)' in planner
assert 'ActivePursuers(op,map,convoy.Position,board.WithdrawalCell)' in planner
assert 'Math.Min(chasers.Count,Math.Max(1,available.Count/2))' in planner
assert 'i<rearCount' in planner
assert 'RHFRoleS41.Withdraw,null' in planner
assert 'RHFRoleS41.Screen,chaser' in planner

# No safe exit is different from no possible exit. Last resort: genuine
# Deadly traverse, or destructible wall/progress cell; no fake ExitMap.
assert 'FindEdge(map,op,navigator,Danger.Some)' in exitpath
assert 'FindEdge(map,op,navigator,Danger.Deadly)' in exitpath
assert 'ReachableProgress(Map map,RHFOperation op,Pawn navigator)' in exitpath
assert 'RecoverBlockedExit(op,map,board,anchor,navigator,now)' in planner
assert 'NJRRescueExitS41.FirstBlockedExitDoor(map,op,navigator)' in planner
assert 'board.Breacher=soldier;' in planner
assert 'if(op.Phase==2 && !NJRRescueAccessR040.PermitsExit(op,obstacle))return null;' in rescue
assert 'cache.NextSearch=now+900;' in exitpath
assert 'captive.CanReach(cell,PathEndMode.OnCell,Danger.Deadly)' in exitpath
assert 'if(separation>9)' in rescue and 'return ApproachPawn(pawn,escorted,180);' in rescue
assert 'pawn.CanReach(order.Cell,PathEndMode.OnCell,Danger.Deadly)' in rescue

# Full real weapon range instead of a fake 55-tile CE/mod weapon cap.
assert 'return Math.Max(0f,range);' in combat
assert 'Mathf.Clamp(range, 4f, 55f)' not in combat
# Paid orbital beam can only fire via native verb and is disallowed during
# escorted ground retreat, including allied/hostage blast-radius avoidance.
assert 'Verb_PowerBeam' in orb and 'orbital.TryStartCastOn' in orb
assert '(op.Phase==2 || op.Phase==4)' in orb
assert 'op.NativeAnimalsS41' in orb and 'op.Freed' in orb
assert 'NJROrbitalSupportS42.Tick(op,map,now)' in operation
assert 'python3 tests/njr_217_contact_pursuit_recovery.py' in workflow

# Minimal independent test table for the intended *classification* only;
# physical movement/visibility is separately checked by the live game.
def chasing(previous_enemy,now_enemy,previous_convoy,now_convoy,visible,behind):
    d0=(previous_enemy[0]-previous_convoy[0])**2+(previous_enemy[1]-previous_convoy[1])**2
    d1=(now_enemy[0]-now_convoy[0])**2+(now_enemy[1]-now_convoy[1])**2
    return visible and behind and previous_enemy!=now_enemy and d1<d0 and d1<=900
assert chasing((0,5),(0,7),(0,20),(0,21),True,True)     # faster pursuer
assert not chasing((0,5),(0,6),(0,20),(0,23),True,True) # slower pursuer
assert not chasing((0,5),(0,5),(0,20),(0,21),True,True) # stationary defender
assert not chasing((0,5),(0,7),(0,20),(0,21),False,True) # no sighting
assert not chasing((0,5),(0,7),(0,20),(0,21),True,False)# front blocker
print('PASS R011: source contracts, classification model; NOT game/compile proof')
