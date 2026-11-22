// RHF S4.1-B: the ONLY routine rescue job issuer.
// It is executed by the pawn's mission ThinkTree, not by MapComponentTick.
using System;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace RaidersHaveFamilies
{
    public sealed class RHFRescueRoleJobGiverS41 : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            if(pawn==null || pawn.Dead || pawn.Downed || !pawn.Spawned ||
               pawn.Map==null || pawn.jobs==null ||
               !(pawn.GetLord()?.LordJob is RHFRescueLordJobV124) ||
               RHFCombatPlannerV125.IsCombatEmergencyJob(pawn))return null;
            RHFOperation op=RHFRescuePlannerS41.MissionFor(pawn.Map,pawn);
            if(op==null || op.BoardS41==null)return null;
            // S4.1-A/E: Phase 3 orders are authored by RHFPodPlannerS41.
            // Work and EnterTransporter are physically performed by the
            // game's own JobDrivers, selected through this ThinkTree only.
            if(op.Phase==3)
            {
                RHFOrderS41 phase3=op.BoardS41.For(pawn,false);
                if(phase3==null)return null;
                switch((RHFRoleS41)phase3.Role)
                {
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
                           (pawn.Position-phase3.Cell).LengthHorizontalSquared>20 &&
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
                            return Task(JobDefOf.Goto,other,90);
                        if(op.Freed!=null && op.Freed.Contains(pawn))
                            return Task(JobDefOf.Wait,pawn.Position,90);
                        return null; // armed guard can defend an existing VIP
                    }
                    case RHFRoleS41.Screen:
                        if(phase3.Cell.IsValid && phase3.Cell.InBounds(pawn.Map) &&
                           (pawn.Position-phase3.Cell).LengthHorizontalSquared>9)
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
            // Immediate physical contact takes priority over distant mission
            // targets. Protected former prisoners still never enter this rule.
            if((op.Freed==null || !op.Freed.Contains(pawn)) &&
               ImmediateThreat(op,pawn))return null;
            switch((RHFRoleS41)order.Role)
            {
                case RHFRoleS41.Breach:
                {
                    Thing obstacle=order.Objective;
                    if(obstacle==null || obstacle.Destroyed || !obstacle.Spawned ||
                       obstacle.Map!=pawn.Map ||
                       !RHFCombatPlannerV125.IsBreachableStructure(obstacle))return null;
                    JobDef strike=RHFCombatPlannerV125.BreachAttack(pawn);
                    // Actual construction-like time and physical paid inventory;
                    // the JobDriver owns the entire planting operation.
                    RHFTacticalMember specialist=RHFTactical.Member(op,pawn);
                    JobDef charge=DefDatabase<JobDef>.GetNamedSilentFail("RHF_PlantBreachChargeS41");
                    if(charge!=null && specialist?.Role==(int)RHFCombatRole.Breacher &&
                       op.BreachChargesUsed<4 && RHFTactical.HasBreachKit(pawn) &&
                       pawn.CanReach(obstacle.Position,PathEndMode.Touch,Danger.Some))
                        return Task(charge,obstacle,0);
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
                            if((pawn.Position-obstacle.Position).LengthHorizontalSquared>5)
                            {
                                Job approach=ApproachBarrier(pawn,obstacle);
                                if(approach!=null)return approach;
                            }
                            // Rifle butt / unarmed attack: no CE explosive fire
                            // near protected VIP, and no blind ranged shots.
                            if(pawn.CanReach(obstacle.Position,PathEndMode.Touch,Danger.Some))
                                return Task(JobDefOf.AttackMelee,obstacle,900);
                            return null;
                        }
                    }
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
                    if(order.Cell.IsValid && order.Cell.InBounds(pawn.Map) &&
                       (pawn.Position-order.Cell).LengthHorizontalSquared>2 &&
                       pawn.CanReach(order.Cell,PathEndMode.OnCell,Danger.Some))
                        return Task(JobDefOf.Goto,order.Cell,90);
                    return null; // no imaginary shots through sealed perimeter
                }
                case RHFRoleS41.Advance:
                {
                    if(order.Objective!=null && order.Objective.Spawned &&
                       order.Objective.Map==pawn.Map)
                        return Task(JobDefOf.Goto,order.Objective,90);
                    if(order.Cell.IsValid && order.Cell.InBounds(pawn.Map) &&
                       (pawn.Position-order.Cell).LengthHorizontalSquared>2)
                        return Task(JobDefOf.Goto,order.Cell,90);
                    return null; // local mission defence may engage enemies
                }
                case RHFRoleS41.Screen:
                {
                    if(order.Cell.IsValid && order.Cell.InBounds(pawn.Map) &&
                       (pawn.Position-order.Cell).LengthHorizontalSquared>9)
                        return Task(JobDefOf.Goto,order.Cell,90);
                    return null; // firing lanes handled by scoped defence giver
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
                           !pawn.CanReach(escorted,PathEndMode.ClosestTouch,Danger.Some) ||
                           !pawn.CanReach(order.Cell,PathEndMode.OnCell,Danger.Some))
                            return null;
                        JobDef carry=DefDatabase<JobDef>.GetNamedSilentFail("RHF_CarryDowned");
                        if(carry==null)return null;
                        Job job=JobMaker.MakeJob(carry,escorted,order.Cell);
                        job.count=1;
                        return job;
                    }
                    int separation=(pawn.Position-escorted.Position).LengthHorizontalSquared;
                    if(separation>16)
                        return Task(JobDefOf.Goto,escorted,90);
                    // Do not sprint to the map edge and leave slow VIPs behind.
                    // Only advance a short escort step, measured from the VIP.
                    if(order.Cell.IsValid && order.Cell.InBounds(pawn.Map) &&
                       (pawn.Position-order.Cell).LengthHorizontalSquared>10 &&
                       separation<=9)
                    {
                        IntVec3 step=EscortStep(escorted.Position,order.Cell,pawn.Map);
                        if(step.IsValid &&
                           (pawn.Position-step).LengthHorizontalSquared>2 &&
                           pawn.CanReach(step,PathEndMode.OnCell,Danger.Some))
                            return Task(JobDefOf.Goto,step,90);
                    }
                    return null; // local defence may protect the nearby VIP
                }
                case RHFRoleS41.FollowEscort:
                {
                    Pawn escort=order.Objective as Pawn;
                    if(escort==null || escort.Dead || escort.Map!=pawn.Map)
                        return Task(JobDefOf.Wait,pawn.Position,150);
                    if((pawn.Position-escort.Position).LengthHorizontalSquared>25)
                        return Task(JobDefOf.Goto,escort,90);
                    if(order.Cell.IsValid && order.Cell.InBounds(pawn.Map) &&
                       (pawn.Position-order.Cell).LengthHorizontalSquared>10)
                        return Task(JobDefOf.Goto,order.Cell,90);
                    return Task(JobDefOf.Wait,pawn.Position,90);
                }
                case RHFRoleS41.Withdraw:
                    if(order.Cell.IsValid && order.Cell.InBounds(pawn.Map) &&
                       (pawn.Position-order.Cell).LengthHorizontalSquared>10)
                        return Task(JobDefOf.Goto,order.Cell,90);
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
            int scanned=0;
            foreach(Pawn enemy in map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer))
            {
                if(++scanned>80)break;
                if(enemy==null || enemy.Dead || enemy.Downed || !enemy.Spawned ||
                   enemy.Map!=map || (op.Targets!=null && op.Targets.Contains(enemy)) ||
                   (op.Freed!=null && op.Freed.Contains(enemy)) ||
                   (soldier.Position-enemy.Position).LengthHorizontalSquared>16 ||
                   !GenSight.LineOfSight(soldier.Position,enemy.Position,map))continue;
                return true;
            }
            return false;
        }

        private static IntVec3 EscortStep(IntVec3 vip,IntVec3 exit,Map map)
        {
            int x=exit.x==vip.x?0:exit.x>vip.x?1:-1;
            int z=exit.z==vip.z?0:exit.z>vip.z?1:-1;
            IntVec3 next=vip+new IntVec3(x*2,0,z*2);
            return next.InBounds(map) && next.Standable(map) ? next : IntVec3.Invalid;
        }

        private static Job ApproachBarrier(Pawn pawn,Thing barrier)
        {
            Map map=pawn?.Map;
            if(map==null || barrier==null || barrier.Map!=map)return null;
            IntVec3 origin=barrier.Position;
            IntVec3[] offsets={IntVec3.North,IntVec3.South,IntVec3.East,
                IntVec3.West,IntVec3.North+IntVec3.East,
                IntVec3.North+IntVec3.West,IntVec3.South+IntVec3.East,
                IntVec3.South+IntVec3.West};
            IntVec3 best=IntVec3.Invalid;
            int score=int.MaxValue;
            foreach(IntVec3 offset in offsets)
            {
                IntVec3 site=origin+offset;
                if(!site.InBounds(map) || !site.Standable(map) ||
                   site.GetEdifice(map)!=null ||
                   !pawn.CanReach(site,PathEndMode.OnCell,Danger.Some))continue;
                int distance=(pawn.Position-site).LengthHorizontalSquared;
                if(distance<score){best=site;score=distance;}
            }
            return best.IsValid && best!=pawn.Position ?
                Task(JobDefOf.Goto,best,90) : null;
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
