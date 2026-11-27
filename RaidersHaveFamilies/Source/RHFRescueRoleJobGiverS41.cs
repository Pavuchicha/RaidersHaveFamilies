// RHF S4.1-B: the ONLY routine rescue job issuer.
// It is executed by the pawn's mission ThinkTree, not by MapComponentTick.
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace RaidersHaveFamilies
{
    public sealed class RHFRescueRoleJobGiverS41 : ThinkNode_JobGiver
    {
        private sealed class JobPulseR028 { internal int LastTick=int.MinValue; }
        private static readonly ConditionalWeakTable<Pawn,JobPulseR028> JobPulses =
            new ConditionalWeakTable<Pawn,JobPulseR028>();

        protected override Job TryGiveJob(Pawn pawn)
        {
            if(pawn==null)return null;
            Job job=MissionJob(pawn);
            if(job==null)
            {
                // A completed waypoint/exit order must not fall through to a
                // vanilla wander job at the old duty focus. Keep CE emergencies.
                if(pawn.Spawned && !pawn.Dead && !pawn.Downed && !pawn.InMentalState &&
                   pawn.Map!=null && pawn.GetLord()?.LordJob is RHFRescueLordJobV124 &&
                   !RHFCombatPlannerV125.IsCombatEmergencyJob(pawn) &&
                   RHFRescuePlannerS41.MissionFor(pawn.Map,pawn)!=null)
                    return Task(JobDefOf.Wait,pawn.Position,30);
                return null;
            }
            // JobDriver_Goto can finish immediately (already at target, or a
            // Thing target interpreted as a cell). Never issue another Goto in
            // the same tick: that caused the engine's 10-jobs recovery loop.
            int now=Find.TickManager.TicksGame;
            JobPulseR028 pulse=JobPulses.GetValue(pawn,_=>new JobPulseR028());
            if(pulse.LastTick==now || (job.def==JobDefOf.Goto &&
               (!job.targetA.Cell.IsValid || job.targetA.Cell==pawn.Position || job.targetA.Thing!=null)))
                return Task(JobDefOf.Wait,pawn.Position,30);
            pulse.LastTick=now;
            return job;
        }

        private Job MissionJob(Pawn pawn)
        {
            if(pawn==null || pawn.Dead || pawn.Downed || !pawn.Spawned ||
               pawn.Map==null || pawn.jobs==null ||
               !(pawn.GetLord()?.LordJob is RHFRescueLordJobV124) ||
               RHFCombatPlannerV125.IsCombatEmergencyJob(pawn))return null;
            RHFOperation op=RHFRescuePlannerS41.MissionFor(pawn.Map,pawn);
            if(op==null || op.BoardS41==null)return null;
            Job counter=NJRPrisonerDefenseR047.Counter(op,pawn);
            if(counter!=null)return counter;
            // S4.1-A/E: Phase 3 orders are authored by RHFPodPlannerS41.
            // Work and EnterTransporter are physically performed by the
            // game's own JobDrivers, selected through this ThinkTree only.
            if(op.Phase==3)
            {
                RHFOrderS41 phase3=op.BoardS41.For(pawn,false);
                if(phase3==null)return null;
                RHFRoleS41 phase3Role=(RHFRoleS41)phase3.Role;
                // The assigned engineer must finish the REAL pod, even under
                // pressure. Everyone else who is not transporting cargo or
                // boarding may stop to fight a physical approach to the site.
                if(op.Freed?.Contains(pawn)!=true && (phase3Role==RHFRoleS41.Screen ||
                   phase3Role==RHFRoleS41.Hold ||
                   phase3Role==RHFRoleS41.Breach))
                {
                    IntVec3 site=op.PodCell.IsValid?op.PodCell:op.PodLauncherCell;
                    Job contact=NJRCombatContactS44.Respond(pawn,op,site,true,22);
                    if(contact!=null)return contact;
                }
                else if(phase3Role==RHFRoleS41.PodSupply)
                {
                    // A visible turret is NOT permission to abandon every
                    // cargo delivery forever. Screen guards handle ordinary
                    // contact while carriers physically supply the blueprint.
                    // A carrier alone or facing a direct adjacent threat may
                    // defend itself before continuing its real native unload.
                    IntVec3 site=op.PodCell.IsValid?op.PodCell:op.PodLauncherCell;
                    if(!HasActivePodGuard(op,pawn) || AdjacentObservedThreat(op,pawn))
                    {
                        Job contact=NJRCombatContactS44.Respond(pawn,op,site,false,12);
                        if(contact!=null)return contact;
                    }
                }
                else if(phase3Role==RHFRoleS41.Engineer)
                {
                    // With a combat-capable guard, keep ONE real engineer
                    // building. If every other defender is dead/downed, the
                    // last builder must be allowed to protect their own life.
                    if(!HasActivePodGuard(op,pawn))
                    {
                        IntVec3 site=op.PodCell.IsValid?op.PodCell:op.PodLauncherCell;
                        Job contact=NJRCombatContactS44.Respond(pawn,op,site,false,10);
                        if(contact!=null)return contact;
                    }
                }
                switch(phase3Role)
                {
                    case RHFRoleS41.Breach:
                    {
                        Thing wall=phase3.Objective;
                        if(op.PodConstructionStage==0)
                        {
                            if(!op.BoardS41.PermitsAssaultBreach(pawn,wall) ||
                               !NJRRescueFormationR032.StrikeCell(pawn.Map,wall,phase3.Cell,pawn))return null;
                            if(pawn.Position!=phase3.Cell)
                                return pawn.CanReach(phase3.Cell,PathEndMode.OnCell,Danger.Deadly) ?
                                    Task(JobDefOf.Goto,phase3.Cell,360) : null;
                            return Task(JobDefOf.AttackMelee,wall,900);
                        }
                        if(wall==null || wall.Destroyed || !wall.Spawned ||
                           wall.Map!=pawn.Map ||
                           !RHFCombatPlannerV125.RequiresBreach(wall))return null;
                        Job approach=ApproachBarrier(pawn,wall);
                        if((pawn.Position-wall.Position).LengthHorizontalSquared>5 &&
                           approach!=null)return approach;
                        if(pawn.CanReach(wall.Position,PathEndMode.Touch,Danger.Deadly))
                            return Task(JobDefOf.AttackMelee,wall,900);
                        return null;
                    }
                    case RHFRoleS41.PodSupply:
                    {
                        if(phase3.Objective is Thing cargo && cargo.Spawned &&
                           (op.PodCargoStacksS41.Contains(cargo) || NJRVipDebrisR045.IsAssigned(op,pawn,cargo)) && phase3.Cell.IsValid && cargo.Position!=phase3.Cell)
                        {
                            JobDef move=DefDatabase<JobDef>.GetNamedSilentFail("NJR_MoveSealCargoR044");
                            if(move==null)return null;
                            Job haul=JobMaker.MakeJob(move,cargo,phase3.Cell);
                            haul.count=cargo.stackCount;
                            return haul;
                        }
                        JobDef unload=DefDatabase<JobDef>.GetNamedSilentFail(
                            "RHF_UnloadPodCargoS41");
                        if(unload==null || !phase3.Cell.IsValid ||
                           !RHFPodSupplyS41.HasCargo(op,pawn))return null;
                        return JobMaker.MakeJob(unload,phase3.Cell);
                    }
                    case RHFRoleS41.Engineer:
                        return RHFPodPlannerS41.WorkJob(pawn,phase3.Objective);
                    case RHFRoleS41.BoardPod:
                    {
                        ThingWithComps vehicle=phase3.Objective as ThingWithComps;
                        CompTransporter transporter=vehicle?.GetComp<CompTransporter>();
                        if(transporter==null || !vehicle.Spawned ||
                           transporter.innerContainer.Contains(pawn))return null;
                        if(!transporter.LoadingInProgressOrReadyToLaunch)
                            return Task(JobDefOf.Wait,pawn.Position,90);
                        // EnterTransporter performs physical pathing and loading.
                        // Expiring it after 180 ticks repeatedly cancels boarding
                        // for injured/slow VIPs on obstructed base layouts.
                        // Let the native JobDriver finish or fail naturally;
                        // the op still validates the actual innerContainer.
                        return JobMaker.MakeJob(JobDefOf.EnterTransporter,vehicle);
                    }
                    case RHFRoleS41.PodAssembly:
                    {
                        // VIP stays by the engineer's site, not at the map edge.
                        if(phase3.Cell.IsValid && phase3.Cell.InBounds(pawn.Map) &&
                           (pawn.Position-phase3.Cell).LengthHorizontalSquared>(op.FortifyPrison?0:20) &&
                           pawn.CanReach(phase3.Cell,PathEndMode.ClosestTouch,Danger.Some))
                            return Task(JobDefOf.Goto,phase3.Cell,90);
                        return Task(JobDefOf.Wait,pawn.Position,90);
                    }
                    case RHFRoleS41.Escort:
                    case RHFRoleS41.FollowEscort:
                    {
                        Pawn other=phase3.Objective as Pawn;
                        if(other!=null && other.Spawned && other.Map==pawn.Map &&
                           (pawn.Position-other.Position).LengthHorizontalSquared>16)
                            return ApproachPawn(pawn,other,90);
                        if(op.Freed!=null && op.Freed.Contains(pawn))
                            return Task(JobDefOf.Wait,pawn.Position,90);
                        return null; // armed guard can defend an existing VIP
                    }
                    case RHFRoleS41.Screen:
                        if(phase3.Cell.IsValid && phase3.Cell.InBounds(pawn.Map) &&
                           (pawn.Position-phase3.Cell).LengthHorizontalSquared>(op.FortifyPrison || op.PodConstructionStage==0 ||
                               pawn.Position==op.BarricadeCell ||
                               (RHFPodSupplyS41.SealPocketR044(op) is HashSet<IntVec3> pocket && !pocket.Contains(pawn.Position))?0:9) &&
                           pawn.CanReach(phase3.Cell,PathEndMode.OnCell,Danger.Deadly))
                            return Task(JobDefOf.Goto,phase3.Cell,90);
                        return null;
                    case RHFRoleS41.Hold:
                        return op.Freed!=null && op.Freed.Contains(pawn) ?
                            Task(JobDefOf.Wait,pawn.Position,90) : null;
                    default:
                        return null;
                }
            }
            if(op.Phase!=1 && op.Phase!=2)return null;
            RHFOrderS41 order=op.BoardS41.For(pawn,false);
            if(order==null)return null;
            // Fire on actual observed pursuers, defenders and turret threats
            // BEFORE following Goto waypoints. Escorts defend locally without
            // abandoning their VIP; convoy guards manoeuvre only if needed.
            RHFRoleS41 role=(RHFRoleS41)order.Role;
            if(op.Phase==1 && op.TryPod && op.FortifyPrison && op.PodConstructionStage>=1)
            {
                if(role==RHFRoleS41.Engineer)
                {
                    if(AdjacentObservedThreat(op,pawn))
                    {Job defence=NJRCombatContactS44.Respond(pawn,op,op.PodCell,false,10);if(defence!=null)return defence;}
                    return RHFPodPlannerS41.WorkJob(pawn,order.Objective);
                }
                if(role==RHFRoleS41.PodSupply && order.Objective!=null && order.Objective.Spawned && order.Cell.IsValid && order.Objective.Position!=order.Cell &&
                   (op.PodCargoStacksS41.Contains(order.Objective) || NJRVipDebrisR045.IsAssigned(op,pawn,order.Objective)))
                {
                    JobDef move=DefDatabase<JobDef>.GetNamedSilentFail("NJR_MoveSealCargoR044");
                    if(move!=null){Job haul=JobMaker.MakeJob(move,order.Objective,order.Cell);haul.count=order.Objective.stackCount;return haul;}
                }
                if(role==RHFRoleS41.PodSupply && RHFPodSupplyS41.HasCargo(op,pawn) && order.Cell.IsValid)
                {
                    JobDef unload=DefDatabase<JobDef>.GetNamedSilentFail("RHF_UnloadPodCargoS41");
                    if(unload!=null)return JobMaker.MakeJob(unload,order.Cell);
                }
            }
            if(op.FortifyPrison && (op.Phase==1 || (op.Phase==2 && op.PodLaunchCompleted)) && role==RHFRoleS41.PodSupply &&
               NJRVipDebrisR045.IsDebris(order.Objective) && order.Objective.Spawned && order.Cell.IsValid && order.Objective.Position!=order.Cell)
            {
                JobDef move=DefDatabase<JobDef>.GetNamedSilentFail("NJR_MoveSealCargoR044");
                if(move==null)return null;
                Job haul=JobMaker.MakeJob(move,order.Objective,order.Cell);haul.count=order.Objective.stackCount;return haul;
            }
            if(op.Phase==2 && role==RHFRoleS41.Engineer && (NJRRescueAccessR040.IsOwnSealR043(op,order.Objective) || NJRRescueAccessR040.IsOwnExitStructureR054(op,order.Objective)))
            {
                JobDef deconstruct=DefDatabase<JobDef>.GetNamedSilentFail("NJR_ReopenOwnSealR051");
                if(deconstruct!=null && pawn.CanReach(order.Objective,PathEndMode.Touch,Danger.Deadly))
                    return JobMaker.MakeJob(deconstruct,order.Objective);
                return null;
            }
            if(NJRRescueFirebreakR034.Controls(op,pawn))
                return NJRRescueFirebreakR034.CombatJob(op,pawn,order);
            if(op.Freed==null || !op.Freed.Contains(pawn))
            {
                bool maneuver=(op.Phase==1 && !NJRRescueRallyR033.IsGathering(op)) ||
                    (role==RHFRoleS41.Screen && !NJRRescueRallyR033.IsGathering(op));
                IntVec3 anchor=op.Phase==2 && role==RHFRoleS41.Screen &&
                    order.Objective is Pawn pursuer && pursuer.Spawned ?
                    pursuer.Position : order.Cell.IsValid?order.Cell:
                    order.Objective!=null?order.Objective.Position:op.ExitCell;
                Job contact=NJRCombatContactS44.Respond(pawn,op,anchor,maneuver,
                    op.Phase==2?15:26);
                if(contact!=null)return contact;
            }
            switch(role)
            {
                case RHFRoleS41.Breach:
                {
                    // A stale saved/order assignment is not a demolition permit.
                    // The CURRENT rescue blackboard owns exactly one barrier.
                    // Assault permits are explicit per-pawn; exit remains single-operator.
                    // Assault: one board-owned structural breach. Withdrawal:
                    // ONLY a physically verified CLOSED DOOR leading to an exit.
                    // Never treat arbitrary wall attacks as a valid escape job.
                    if(op.Phase==1)
                    {
                        if(op.BoardS41?.PermitsAssaultBreach(pawn,order.Objective)!=true)return null;
                    }
                    else if(op.Phase!=2 || op.BoardS41?.PermitsAssaultBreach(pawn,order.Objective)!=true)return null;
                    Thing obstacle=order.Objective;
                    if(op.Phase==2 && !NJRRescueAccessR040.PermitsExit(op,obstacle))return null;
                    if(obstacle==null || obstacle.Destroyed || !obstacle.Spawned ||
                       obstacle.Map!=pawn.Map ||
                       !RHFObjectiveRouteV128.IsRescueStructuralBarrier(obstacle))return null;
                    // R032: assault operators first occupy their separately
                    // allocated, reachable strike cells. Native melee may use
                    // a clear diagonal, but never an obstructed corner.
                    if(op.Phase==1 || NJRRescueAccessR040.IsAssignedAccess(op,obstacle))
                    {
                        if(!NJRRescueFormationR032.StrikeCell(pawn.Map,obstacle,order.Cell,pawn))return null;
                        if(pawn.Position!=order.Cell)
                            return pawn.CanReach(order.Cell,PathEndMode.OnCell,Danger.Deadly) ?
                                Task(JobDefOf.Goto,order.Cell,360) : null;
                        return Task(JobDefOf.AttackMelee,obstacle,900);
                    }
                    // Phase 2 keeps its original single door access permit.
                    JobDef strike=RHFCombatPlannerV125.BreachAttack(pawn);
                    // No instant damage/charges: only ordinary weapon attack jobs.
                    if(strike==null)return null;
                    if(strike==JobDefOf.AttackStatic)
                    {
                        bool unsafeFire=RHFRescueFireDisciplineV136.UnsafeLine(op,pawn,obstacle);
                        bool hasSight=GenSight.LineOfSight(pawn.Position,
                            obstacle.Position,pawn.Map);
                        float range=RHFCombatPlannerV125.WeaponRange(pawn);
                        bool outOfRange=range<=0f ||
                            (pawn.Position-obstacle.Position).LengthHorizontalSquared>range*range;
                        if(unsafeFire || !hasSight || outOfRange)
                        {
                            // A missing firing line must never produce a null
                            // job that leaves the only sapper standing still.
                            // Move to an actual TOUCH-accessible tile on the
                            // friendly side of the wall before attacking.
                            if((pawn.Position-obstacle.Position).LengthHorizontalSquared!=1)
                            {
                                Job approach=ApproachBarrier(pawn,obstacle);
                                return approach;
                            }
                            // Rifle butt / unarmed attack: no CE explosive fire
                            // near protected VIP, and no blind ranged shots.
                            if(pawn.CanReach(obstacle.Position,PathEndMode.Touch,Danger.Some))
                                return Task(JobDefOf.AttackMelee,obstacle,900);
                            return null;
                        }
                    }
                    if(strike==JobDefOf.AttackMelee &&
                       (pawn.Position-obstacle.Position).LengthHorizontalSquared!=1)
                        return ApproachBarrier(pawn,obstacle);
                    return Task(strike,obstacle,900);
                }
                case RHFRoleS41.SuppressTurret:
                {
                    Thing turret=order.Objective;
                    if(turret==null || turret.Destroyed || !turret.Spawned ||
                       turret.Map!=pawn.Map)return null;
                    float range=RHFCombatPlannerV125.WeaponRange(pawn);
                    bool canShoot=range>2f &&
                        (pawn.Position-turret.Position).LengthHorizontalSquared<=range*range &&
                        GenSight.LineOfSight(pawn.Position,turret.Position,pawn.Map) &&
                        !RHFRescueFireDisciplineV136.UnsafeLine(op,pawn,turret);
                    if(canShoot)return Task(JobDefOf.AttackStatic,turret,180);
                    if(op.Phase==2 && op.PodLaunchCompleted &&
                       (pawn.Position-turret.Position).LengthHorizontalSquared==1 &&
                       pawn.CanReach(turret,PathEndMode.Touch,Danger.Deadly))return Task(JobDefOf.AttackMelee,turret,180);
                    if(order.Cell.IsValid && order.Cell.InBounds(pawn.Map) &&
                       (op.Phase==2 && op.PodLaunchCompleted ? pawn.Position!=order.Cell : (pawn.Position-order.Cell).LengthHorizontalSquared>2) &&
                       pawn.CanReach(order.Cell,PathEndMode.OnCell,Danger.Deadly))
                        return Task(JobDefOf.Goto,order.Cell,240);
                    // If no firing line is open, breach the real obstruction
                    // instead of waiting in the open under turret fire.
                    Job breach=NJRCombatContactS44.BreachToward(pawn,op,turret.Position);
                    if(breach!=null)return breach;
                    return null; // a turret behind indestructible rock is inaccessible
                }
                case RHFRoleS41.Advance:
                {
                    // Do NOT guess a wall when strategic route is unknown.
                    // Only the explicit Breach role can attack a blackboard
                    // barrier; all other members must navigate or defend.
                    if(order.Objective!=null && order.Objective.Spawned &&
                       order.Objective.Map==pawn.Map)
                        return ApproachPawn(pawn,order.Objective as Pawn,90);
                    if(order.Cell.IsValid && order.Cell.InBounds(pawn.Map) &&
                       pawn.Position!=order.Cell)
                    {
                        if(pawn.CanReach(order.Cell,PathEndMode.OnCell,Danger.Deadly))
                            return Task(JobDefOf.Goto,order.Cell,480);
                        Job breach=NJRCombatContactS44.BreachToward(pawn,op,order.Cell);
                        if(breach!=null)return breach;
                    }
                    return null; // local mission defence may engage enemies
                }
                case RHFRoleS41.Screen:
                {
                    if(order.Cell.IsValid && order.Cell.InBounds(pawn.Map) &&
                       (op.Phase==1 || (op.Phase==2 && op.PodLaunchCompleted) || NJRRescueAccessR040.IsAssignedAccess(op,op.BoardS41.Barrier) ? pawn.Position!=order.Cell :
                           (pawn.Position-order.Cell).LengthHorizontalSquared>9))
                    {
                        if(pawn.CanReach(order.Cell,PathEndMode.OnCell,Danger.Deadly))
                            return Task(JobDefOf.Goto,order.Cell,240);
                        // Real rearguards do not hang on a forbidden tile.
                        // Fight a reachable blocking structure instead.
                        Job breach=NJRCombatContactS44.BreachToward(pawn,op,order.Cell);
                        if(breach!=null)return breach;
                    }
                    return null; // mission-scoped defence remains active
                }
                case RHFRoleS41.Escort:
                {
                    Pawn escorted=order.Objective as Pawn;
                    if(escorted==null || escorted.Dead || escorted.MapHeld!=pawn.Map)
                        return null;
                    if(!escorted.Spawned)return null; // carrier already holds captive
                    if(escorted.Downed)
                    {
                        if(op.Phase!=2 || !order.Cell.IsValid ||
                           !order.Cell.InBounds(pawn.Map) ||
                           pawn.health?.capacities?.CapableOf(PawnCapacityDefOf.Manipulation)!=true ||
                           pawn.carryTracker?.CarriedThing!=null ||
                           !pawn.CanReach(escorted,PathEndMode.ClosestTouch,Danger.Deadly) ||
                           !pawn.CanReach(order.Cell,PathEndMode.OnCell,Danger.Deadly))
                            return null;
                        JobDef carry=DefDatabase<JobDef>.GetNamedSilentFail("RHF_CarryDowned");
                        if(carry==null)return null;
                        Job job=JobMaker.MakeJob(carry,escorted,order.Cell);
                        job.count=1;
                        return job;
                    }
                    // R029: in withdrawal BOTH actors move to the fixed
                    // destination. No reciprocal chase or occupied-neighbour gate.
                    // If the exit is blocked, the planner gives a reachable
                    // staging cell instead; phase-1 local escort remains intact.
                    if(op.Phase==2 && order.Cell.IsValid)
                    {
                        if((pawn.Position-order.Cell).LengthHorizontalSquared>10 &&
                           pawn.CanReach(order.Cell,PathEndMode.OnCell,Danger.Deadly))
                            return Task(JobDefOf.Goto,order.Cell,360);
                        return null; // local defence while awaiting the VIP
                    }
                    int separation=(pawn.Position-escorted.Position).LengthHorizontalSquared;
                    if(separation>9)
                        return ApproachPawn(pawn,escorted,180);
                    return null; // phase-1 protection of already liberated targets
                }
                case RHFRoleS41.FollowEscort:
                {
                    if(op.Phase!=2 || !order.Cell.IsValid ||
                       !order.Cell.InBounds(pawn.Map))
                        return Task(JobDefOf.Wait,pawn.Position,120);
                    // R029: a mobile released pawn may advance to the same
                    // verified exit even if the escort is delayed or replaced.
                    // Only the actual paired exit is gated by proximity.
                    bool paired=order.Objective is Pawn escort && escort.Spawned &&
                        !escort.Dead && !escort.Downed && escort.Map==pawn.Map;
                    if((pawn.Position-order.Cell).LengthHorizontalSquared<=(paired?10:0))
                        return Task(JobDefOf.Wait,pawn.Position,120);
                    if(pawn.CanReach(order.Cell,PathEndMode.OnCell,Danger.Deadly))
                        return Task(JobDefOf.Goto,order.Cell,360);
                    return Task(JobDefOf.Wait,pawn.Position,30);
                }
                case RHFRoleS41.Withdraw:
                    if(order.Cell.IsValid && order.Cell.InBounds(pawn.Map) &&
                       (pawn.Position-order.Cell).LengthHorizontalSquared>10 &&
                       pawn.CanReach(order.Cell,PathEndMode.OnCell,Danger.Deadly))
                        return Task(JobDefOf.Goto,order.Cell,240);
                    return op.Freed!=null && op.Freed.Contains(pawn) ?
                        Task(JobDefOf.Wait,pawn.Position,90) : null;
                case RHFRoleS41.Hold:
                    // The hostage stays put, but armed mission members can
                    // respond through the lower self-defence ThinkNode.
                    return op.Freed!=null && op.Freed.Contains(pawn) ?
                        Task(JobDefOf.Wait,pawn.Position,90) : null;
                default:return null;
            }
        }

        // Bounded local contact, only when ThinkTree asks for a new job.
        // Let the lower mission-scoped combat giver react instead of continuing
        // Goto blindly after an unexpected melee-range encounter.
        private static bool ImmediateThreat(RHFOperation op,Pawn soldier)
        {
            Map map=soldier?.Map;
            if(map==null)return false;
            foreach(Pawn enemy in map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer))
            {
                if(enemy==null || enemy.Dead || enemy.Downed || !enemy.Spawned ||
                   enemy.Map!=map || (op.Targets!=null && op.Targets.Contains(enemy)) ||
                   (op.Freed!=null && op.Freed.Contains(enemy)) ||
                   (soldier.Position-enemy.Position).LengthHorizontalSquared>16 ||
                   !GenSight.LineOfSight(soldier.Position,enemy.Position,map))continue;
                return true;
            }
            return false;
        }

        // The mere existence of a live pawn somewhere in the squad does not
        // protect the engineer. A guard must be near the pod, free of a
        // critical work/escort duty, and physically able to fight.
        internal static bool HasActivePodGuard(RHFOperation op,Pawn worker)
        {
            if(op?.Squad==null || worker?.Map==null)return false;
            IntVec3 site=op.PodCell.IsValid ? op.PodCell :
                op.PodLauncherCell.IsValid ? op.PodLauncherCell : worker.Position;
            foreach(Pawn guard in op.Squad)
            {
                if(guard==null || guard==worker || !guard.Spawned ||
                   guard.Map!=worker.Map || guard.Dead || guard.Downed ||
                   guard.InMentalState || guard.GetLord()!=worker.GetLord() ||
                   guard.WorkTagIsDisabled(WorkTags.Violent) ||
                   (guard.Position-site).LengthHorizontalSquared>900)continue;
                RHFOrderS41 role=op.BoardS41?.For(guard,false);
                if(role==null)continue;
                RHFRoleS41 duty=(RHFRoleS41)role.Role;
                if(duty==RHFRoleS41.Screen || duty==RHFRoleS41.Hold ||
                   duty==RHFRoleS41.Breach)return true;
            }
            return false;
        }

        private static bool AdjacentObservedThreat(RHFOperation op,Pawn worker)
        {
            Thing threat=NJRCombatContactS44.VisibleContact(worker,op,
                worker.Position,3);
            return threat!=null &&
                (worker.Position-threat.Position).LengthHorizontalSquared<=9;
        }

        private static IntVec3 EscortStep(Pawn escort,Pawn vip,IntVec3 exit)
        {
            Map map=escort?.Map;
            if(map==null || vip==null || vip.Map!=map || !exit.IsValid)
                return IntVec3.Invalid;
            IntVec3 origin=vip.Position;
            IntVec3 best=IntVec3.Invalid;
            int score=int.MaxValue;
            bool progressing=false;
            int original=(origin-exit).LengthHorizontalSquared;
            // The previous two-cell diagonal got stuck at corners and doors.
            // Select an actual reachable short waypoint for BOTH members;
            // allow a side-step when the direct diagonal is blocked.
            for(int dx=-5;dx<=5;dx++)for(int dz=-5;dz<=5;dz++)
            {
                IntVec3 cell=origin+new IntVec3(dx,0,dz);
                if(!cell.InBounds(map) || !cell.Standable(map) ||
                   cell.GetEdifice(map)!=null || (dx==0 && dz==0) ||
                   !vip.CanReach(cell,PathEndMode.OnCell,Danger.Deadly) ||
                   !escort.CanReach(cell,PathEndMode.OnCell,Danger.Deadly))continue;
                int progress=(cell-exit).LengthHorizontalSquared;
                bool improves=progress<original;
                if(progressing && !improves)continue;
                int move=(cell-origin).LengthHorizontalSquared;
                int nextScore=progress*8+move+
                    (cell-escort.Position).LengthHorizontalSquared;
                if((improves && !progressing) || nextScore<score)
                {best=cell;score=nextScore;progressing=improves;}
            }
            return best;
        }

        private static Job ApproachBarrier(Pawn pawn,Thing barrier)
        {
            Map map=pawn?.Map;
            if(map==null || barrier==null || barrier.Map!=map)return null;
            IntVec3 origin=barrier.Position;
            // R031: Bishop's Touch path ended diagonally at (124,156),
            // but his melee job never damaged wall (125,155). Use a face tile.
            IntVec3[] offsets={IntVec3.North,IntVec3.South,IntVec3.East,IntVec3.West};
            IntVec3 best=IntVec3.Invalid;
            int score=int.MaxValue;
            foreach(IntVec3 offset in offsets)
            {
                IntVec3 site=origin+offset;
                if(!site.InBounds(map) || !site.Standable(map) ||
                   site.GetEdifice(map)!=null ||
                   (site.GetFirstPawn(map)!=null && site.GetFirstPawn(map)!=pawn) ||
                   !pawn.CanReach(site,PathEndMode.OnCell,Danger.Deadly))continue;
                int distance=(pawn.Position-site).LengthHorizontalSquared;
                if(distance<score){best=site;score=distance;}
            }
            return best.IsValid && best!=pawn.Position ?
                Task(JobDefOf.Goto,best,90) : null;
        }

        private static Job ApproachPawn(Pawn pawn,Pawn target,int ticks)
        {
            Map map=pawn?.Map;
            if(map==null || target==null || !target.Spawned || target.Map!=map)
                return null;
            if((pawn.Position-target.Position).LengthHorizontalSquared<=2 &&
               GenSight.LineOfSight(pawn.Position,target.Position,map))
                return Task(JobDefOf.Wait,pawn.Position,30);
            IntVec3 best=IntVec3.Invalid;int score=int.MaxValue;
            foreach(IntVec3 offset in new IntVec3[]{IntVec3.North,IntVec3.South,IntVec3.East,IntVec3.West,
                IntVec3.North+IntVec3.East,IntVec3.North+IntVec3.West,
                IntVec3.South+IntVec3.East,IntVec3.South+IntVec3.West})
            {
                IntVec3 cell=target.Position+offset;
                if(!cell.InBounds(map) || !cell.Standable(map) ||
                   cell.GetFirstPawn(map)!=null ||
                   !GenSight.LineOfSight(cell,target.Position,map) ||
                   !pawn.CanReach(cell,PathEndMode.OnCell,Danger.Deadly))continue;
                int d=(pawn.Position-cell).LengthHorizontalSquared;
                if(d<score){score=d;best=cell;}
            }
            return best.IsValid ? Task(JobDefOf.Goto,best,ticks) :
                Task(JobDefOf.Wait,pawn.Position,30);
        }

        private static Job Task(JobDef def, LocalTargetInfo target,int ticks)
        {
            Job job=JobMaker.MakeJob(def,target);
            if(ticks>0)
            {job.expiryInterval=ticks;job.checkOverrideOnExpire=true;}
            return job;
        }
    }
}
