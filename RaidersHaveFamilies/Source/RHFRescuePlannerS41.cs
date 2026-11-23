// RHF S4.1-B: rescue strategy writes saveable role orders, never regular Pawn jobs.
// The only direct jobs in this class are one-shot native physical carry/ExitMap actions.
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using RimWorld;
using Verse;
using Verse.AI;

namespace RaidersHaveFamilies
{
    internal static class RHFRescuePlannerS41
    {
        internal static bool Enabled => true;
        internal static bool ActiveForRoleS41(Pawn pawn,Map map) => Active(pawn,map);

        private const int PlanCadence = 180;
        // Pursuit memory is map/operation-scoped, rebuilt after loading. It
        // detects PLAYER pawns genuinely moving closer to the exiting convoy;
        // a static colonist or turret does not force a permanent rearguard.
        private sealed class PursuitMemory
        {
            internal readonly Dictionary<int,IntVec3> Previous =
                new Dictionary<int,IntVec3>();
            // Relative movement requires BOTH snapshots. Comparing an old
            // enemy position with the new convoy position can label an enemy
            // as "catching up" even when the convoy is outrunning them.
            internal IntVec3 PreviousConvoy = IntVec3.Invalid;
        }
        private static readonly ConditionalWeakTable<RHFOperation,PursuitMemory>
            Pursuits = new ConditionalWeakTable<RHFOperation,PursuitMemory>();
        private sealed class BarrierEventCache
        {
            internal readonly Dictionary<int,int> Seen = new Dictionary<int,int>();
        }
        private static readonly ConditionalWeakTable<RHFOperation,BarrierEventCache>
            BarrierPulses = new ConditionalWeakTable<RHFOperation,BarrierEventCache>();
        // R020: a rescue is commanded by a stable leader for each hostage.
        // Electing the geometrically nearest soldier on EVERY strategy pulse
        // moved the A* origin between different flanks and changed demolition
        // targets while a breacher was still working on the first one.
        private sealed class RescueCommandR020
        {
            internal Pawn Target;
            internal Pawn Leader;
        }
        private static readonly ConditionalWeakTable<RHFOperation,RescueCommandR020>
            RescueLeadsR020 = new ConditionalWeakTable<RHFOperation,RescueCommandR020>();

        internal static RHFOperation MissionFor(Map map, Pawn member)
        {
            if (map == null || member == null) return null;
            List<RHFOperation> operations = RHFMissionIntegrityV130.Operations(map);
            if (operations == null) return null;
            for (int i = 0; i < operations.Count; i++)
            {
                RHFOperation op = operations[i];
                if (op == null || op.Finished || !op.Deployed ||
                    op.Type != (int)RHFOperationType.Rescue) continue;
                if (op.Squad != null && op.Squad.Contains(member)) return op;
                if (op.Freed != null && op.Freed.Contains(member)) return op;
            }
            return null;
        }

        private static bool Active(Pawn p, Map map) => p != null && p.Spawned &&
            p.Map == map && !p.Dead && !p.Downed && !p.InMentalState &&
            p.jobs != null;

        private static Pawn CurrentCaptive(RHFOperation op, Map map)
        {
            if (op?.Targets == null) return null;
            if (Captive(op, op.Vip, map)) return op.Vip;
            for (int i = 0; i < op.Targets.Count; i++)
                if (Captive(op, op.Targets[i], map)) return op.Targets[i];
            return null;
        }

        private static bool Captive(RHFOperation op, Pawn p, Map map) =>
            p != null && p.Spawned && p.Map == map && !p.Dead &&
            (p.IsPrisonerOfColony || p.IsSlaveOfColony) &&
            (op.Freed == null || !op.Freed.Contains(p));

        private static bool InTouch(RHFOperation op, Pawn captive, Map map)
        {
            if(captive==null || op?.Squad==null)return false;
            for(int i=0;i<op.Squad.Count;i++)
            {
                Pawn rescuer=op.Squad[i];
                if(Active(rescuer,map) &&
                   (rescuer.Position-captive.Position).LengthHorizontalSquared<=5 &&
                   SameAccessibleRoom(rescuer,captive,map) &&
                   rescuer.CanReach(captive,PathEndMode.Touch,Danger.Some))return true;
            }
            return false;
        }

        // Physical contact is not equivalent to a pathfinder's Destroyable/Deadly
        // route through a still-closed hostile door. Both sides must be in the
        // same actual room; otherwise an unbreached cell is NOT liberated.
        private static bool SameAccessibleRoom(Pawn rescuer,Pawn captive,Map map)
        {
            if (rescuer?.Map!=map || captive?.Map!=map)return false;
            Room a=rescuer.Position.GetRoom(map), b=captive.Position.GetRoom(map);
            if(a!=b)return false;
            // Both null: no room graph (unroofed/open area). Demand physical LoS.
            if(a==null && !GenSight.LineOfSight(rescuer.Position,captive.Position,map))
                return false;
            return true;
        }

        internal static bool PhysicalContact(RHFOperation op, Pawn captive, Map map)
        {
            if (captive == null || !captive.Spawned || captive.Map != map ||
                op?.Squad == null) return false;
            for (int i = 0; i < op.Squad.Count; i++)
            {
                Pawn rescuer = op.Squad[i];
                if (!Active(rescuer, map) ||
                    (rescuer.Position-captive.Position).LengthHorizontalSquared>36)continue;
                if (SameAccessibleRoom(rescuer,captive,map) &&
                    rescuer.CanReach(captive,PathEndMode.Touch,Danger.Some))return true;
            }
            return false;
        }

        // Event-scoped adoption: not every released prisoner, only an operation's
        // named target physically reached by its own rescuers. Never grant success
        // to player-initiated releases on the far side of the map.
        internal static bool TryAdoptReleased(Pawn captive)
        {
            Map map = captive?.Map;
            // Vanilla GenGuest.PrisonerRelease may set Released=true while
            // keeping IsPrisonerOfColony until a later transition.
            bool vanillaReleased=captive?.guest?.Released==true;
            if (map == null || captive.Dead || captive.IsSlaveOfColony ||
                (captive.IsPrisonerOfColony && !vanillaReleased)) return false;
            RHFOperationController controller=map.GetComponent<RHFOperationController>();
            RHFOperation op=controller?.FindRescueFor(captive.ThingID);
            if (op == null || !op.Deployed || op.Finished || op.Phase != 1 ||
                op.Targets == null || !op.Targets.Contains(captive) ||
                !PhysicalContact(op,captive,map))return false;
            if (op.Freed == null) op.Freed = new List<Pawn>();
            if(op.Freed.Contains(captive))
            {
                RHFRescueCommandV124.StabilizeFreed(op,captive);
                return true; // event hook re-entry; restore lord/duty if needed
            }
            op.Freed.Add(captive);
            // Clear native Release-to-exit state only after real rescue contact.
            // The status hook can re-enter; Freed membership makes it idempotent.
            if(captive.IsPrisonerOfColony && vanillaReleased)
                captive.guest.SetGuestStatus(null);
            RHFHostageRecord record=Current.Game?.GetComponent<RHFGameLedger>()?.FindHostage(captive);
            if(captive.Faction==Faction.OfPlayer && record?.OriginalFaction!=null &&
               !record.OriginalFaction.IsPlayer)
                captive.SetFaction(record.OriginalFaction);
            EnsureEscort(op,captive,map);
            RHFRescueCommandV124.StabilizeFreed(op,captive);
            // A newly freed pawn must receive mission orders on this SAME tick.
            // Without this, the vanilla release/flee job can win before the
            // next 90/180-tick strategy update.
            op.BoardS41 ??= new RHFTacticalBoardS41();
            op.BoardS41.NextReplanTick=0;
            op.BoardS41.LastPlanTick=0;
            AssignFreedEscort(op,map,Find.TickManager.TicksGame,null);
            // GenGuest.PrisonerRelease can have already issued a vanilla
            // exitMapOnArrival Goto. One single corrective job is allowed here.
            Job job=captive.jobs?.curJob;
            if (job != null && job.exitMapOnArrival)
                captive.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Wait,captive.Position));
            op.BoardS41.Decision="event rescue adoption: " + captive.LabelShortCap;
            op.BoardS41.LastProgressTick=Find.TickManager.TicksGame;
            op.LastProgressTick=Find.TickManager.TicksGame;
            return true;
        }

        internal static bool GuardExit(Pawn pawn)
        {
            Map map=pawn?.Map;
            if(map==null)return true;
            RHFOperation op=map.GetComponent<RHFOperationController>()?.FindRescueFor(pawn.ThingID);
            if(op==null || op.Finished || !op.Deployed || op.Phase<1)return true;
            if(op.Freed!=null && op.Freed.Contains(pawn))
                return RHFRescueCommandV124.PermitExit(pawn);
            // A named target physically reached by the rescue is protected even
            // when prisoner escape AI leaves IsPrisonerOfColony=true. No global
            // ban on remote vanilla prison breaks or player releases.
            if(op.Targets!=null && op.Targets.Contains(pawn) &&
               !pawn.IsSlaveOfColony && PhysicalContact(op,pawn,map))
            {
                if(!pawn.IsPrisonerOfColony || pawn.guest?.Released==true)
                    TryAdoptReleased(pawn);
                return false;
            }
            return true;
        }

        internal static RHFEscortLink EnsureEscort(RHFOperation op, Pawn captive, Map map)
        {
            if (op == null || captive == null || op.Squad == null) return null;
            if (op.Escorts == null) op.Escorts = new List<RHFEscortLink>();
            RHFEscortLink existing = null;
            foreach (RHFEscortLink link in op.Escorts)
                if (link?.Captive == captive) { existing=link; break; }
            // Never keep an escort who is already carrying ANOTHER VIP or
            // has been bound to another live captive in an old save.
            bool reserved=false;
            if(existing?.Escort!=null)
                foreach(RHFEscortLink link in op.Escorts)
                {
                    if(link==existing)break; // earliest valid allocation wins
                    if(link!=null && link.Escort==existing.Escort &&
                       link.Captive!=null && !link.Captive.Dead &&
                       link.Captive.MapHeld==map)
                    {reserved=true;break;}
                }
            if(Active(existing?.Escort,map) && !reserved &&
               (!captive.Downed ||
                (existing.Escort.health?.capacities?.CapableOf(PawnCapacityDefOf.Manipulation)==true &&
                 existing.Escort.carryTracker?.CarriedThing==null)))return existing;
            Pawn previous=existing?.Escort;
            Pawn selected=null;
            int best=int.MaxValue;
            for (int i=0;i<op.Squad.Count;i++)
            {
                Pawn candidate=op.Squad[i];
                if (!Active(candidate,map) || candidate==op.BoardS41?.Breacher ||
                    (op.Phase==3 && candidate==op.BoardS41?.PodEngineerS41) ||
                    !RHFMissionIntegrityV130.MayEscort(op,candidate,captive))continue;
                // Exclusivity is a HARD constraint. A 100000-point penalty
                // still selected a busy escort when all other troops were down.
                // Holding the second VIP is safer than losing both to one pawn.
                bool occupied=false;
                foreach(RHFEscortLink link in op.Escorts)
                    if(link?.Escort==candidate && link.Captive!=captive &&
                       link.Captive!=null && link.Captive.MapHeld==map && !link.Captive.Dead)
                    {occupied=true;break;}
                if(occupied)continue;
                if(captive.Downed &&
                   (candidate.health?.capacities?.CapableOf(PawnCapacityDefOf.Manipulation)!=true ||
                    candidate.carryTracker?.CarriedThing!=null ||
                    !candidate.CanReach(captive,PathEndMode.ClosestTouch,Danger.Some)))
                    continue;
                int score=(candidate.Position-captive.Position).LengthHorizontalSquared;
                if(score<best){best=score;selected=candidate;}
            }
            if(selected==null)
            {
                if(existing!=null)
                {
                    existing.Escort=null; // invalidate stale double allocation
                    if(op.BoardS41!=null)op.BoardS41.NextReplanTick=0;
                }
                return null;
            }
            if(existing==null){existing=new RHFEscortLink(captive,selected);op.Escorts.Add(existing);}
            else existing.Escort=selected;
            if(previous!=null && previous!=selected)
            {
                RHFOrderS41 stale=op.BoardS41?.For(previous,false);
                if(stale!=null && stale.Role==(int)RHFRoleS41.Escort &&
                   stale.Objective==captive)
                    op.BoardS41.Assign(previous,RHFRoleS41.Hold,null,previous.Position,
                        Find.TickManager.TicksGame);
            }
            if(op.BoardS41!=null)
            {
                int now=Find.TickManager.TicksGame;
                op.BoardS41.Assign(selected,RHFRoleS41.Escort,captive,captive.Position,now);
            }
            return existing;
        }

        // Protect released prisoners throughout Phase 1, not only after all
        // other cells are breached. One-to-one escort reserves assigned soldiers
        // from the later assault loop; no second Job writer is introduced.
        private static void AssignFreedEscort(RHFOperation op,Map map,int now,
            HashSet<Pawn> escortsReserved)
        {
            if(op?.Freed==null || map==null)return;
            RHFTacticalBoardS41 board=op.BoardS41 ??= new RHFTacticalBoardS41();
            for(int i=0;i<op.Freed.Count;i++)
            {
                Pawn freed=op.Freed[i];
                if(freed==null || freed.Dead || freed.MapHeld!=map || !freed.Spawned)continue;
                RHFRescueCommandV124.StabilizeFreed(op,freed);
                RHFEscortLink link=EnsureEscort(op,freed,map);
                Pawn escort=link?.Escort;
                if(!Active(escort,map))
                {board.Assign(freed,RHFRoleS41.Hold,null,freed.Position,now);continue;}
                escortsReserved?.Add(escort);
                // Keep the early-liberated hostage sheltered near their escort
                // until the operation is ready for a coordinated departure.
                board.Assign(escort,RHFRoleS41.Escort,freed,freed.Position,now);
                board.Assign(freed,RHFRoleS41.FollowEscort,escort,escort.Position,now);
            }
        }

        // Event hook for actual structural destruction. Never turn destruction
        // of an unrelated door into an automatic prisoner release; physical
        // contact remains mandatory and ordinary prison breaks are untouched.
        internal static void OnBarrierDestroyed(Map map,Thing barrier)
        {
            if(map==null || barrier==null)return;
            var operations=RHFMissionIntegrityV130.Operations(map);
            if(operations==null)return;
            int now=Find.TickManager.TicksGame;
            for(int i=0;i<operations.Count;i++)
            {
                RHFOperation op=operations[i];
                if(op==null || !op.Deployed || op.Finished || op.Phase!=1 ||
                   op.Type!=(int)RHFOperationType.Rescue || op.BoardS41==null)continue;
                if(op.BoardS41.Barrier==barrier)
                {
                    op.BoardS41.Barrier=null;
                    op.BoardS41.Breacher=null;
                    op.BoardS41.NextReplanTick=0;
                    op.BoardS41.LastPlanTick=0;
                    op.BoardS41.Decision="breach destroyed; replan next pulse";
                }
                if(op.Targets==null)continue;
                BarrierEventCache cache=BarrierPulses.GetValue(op,_=>new BarrierEventCache());
                for(int j=0;j<op.Targets.Count;j++)
                {
                    Pawn captive=op.Targets[j];
                    if(captive==null || captive.Dead || captive.Map!=map ||
                       (captive.Position-barrier.Position).LengthHorizontalSquared>225)
                        continue;
                    // Multiple walls can disappear in the same explosion. We
                    // only query pathing for this prisoner once per operation/tick.
                    int key=captive.thingIDNumber;
                    if(cache.Seen.TryGetValue(key,out int seen) && seen==now)continue;
                    cache.Seen[key]=now;
                    // First attempt event adoption for a genuinely released
                    // prisoner; for a still imprisoned captive, the mission
                    // must walk in and free them physically, never by geometry.
                    if(!captive.IsPrisonerOfColony || captive.guest?.Released==true)
                        TryAdoptReleased(captive);
                    else if(InTouch(op,captive,map))
                    {
                        // Only a genuine rescuer already inside the breached
                        // detention area may liberate a still-held prisoner.
                        // The wall's destruction itself grants no success.
                        map.GetComponent<RHFOperationController>()?.S41FreeTarget(captive,op);
                    }
                }
            }
        }

        internal static void Infiltrate(RHFOperationController controller,RHFOperation op,int now)
        {
            if(controller==null || op==null || op.Finished)return;
            Map map=MapFor(op);
            if(map==null)return;
            RHFTactical.EnsureBreachTeam(op,map);
            RHFTacticalBoardS41 board=op.BoardS41 ??= new RHFTacticalBoardS41();
            board.Prune(map);
            if(board.ObservedPhase!=op.Phase)
            {board.ObservedPhase=op.Phase;board.NextReplanTick=0;board.LastPlanTick=0;}

            // Sync with actual release state even when a mod altered guest status
            // without going through standard GenGuest/SetGuestStatus APIs.
            if (op.Targets != null)
                for(int i=0;i<op.Targets.Count;i++)
                    if(op.Targets[i]!=null &&
                       (!op.Targets[i].IsPrisonerOfColony || op.Targets[i].guest?.Released==true) &&
                       !op.Targets[i].IsSlaveOfColony)
                        TryAdoptReleased(op.Targets[i]);
            var escortedDuringAssault=new HashSet<Pawn>();
            AssignFreedEscort(op,map,now,escortedDuringAssault);
            Pawn captive=CurrentCaptive(op,map);
            if(captive==null)
            {
                op.Phase=op.Freed!=null && op.Freed.Contains(op.Vip) && op.TryPod &&
                    op.Vip!=null && op.Vip.Map==map ? 3 : 2;
                op.PhaseStartTick=now;
                board.NextReplanTick=0;
                board.LastPlanTick=0;
                board.Decision="no captive remaining; switching to extraction";
                if(op.Phase==2)Extract(controller,op,now);
                return;
            }
            Pawn previousTarget=board.RescueTarget;
            board.RescueTarget=captive;
            board.LastKnownCell=captive.Position; // declared hostage location, not enemy omniscience
            // Select the nearest available leader ONCE per captive, then keep
            // that leader until incapacitated or assigned to an earlier VIP.
            // It is not valid to switch the squad's breach corridor every 180t.
            RescueCommandR020 command=RescueLeadsR020.GetValue(op,
                _=>new RescueCommandR020());
            if(command.Target!=captive)
            { command.Target=captive;command.Leader=null; }
            Pawn lead=command.Leader;
            if(!Active(lead,map) || escortedDuringAssault.Contains(lead) ||
               !op.Squad.Contains(lead))
            {
                lead=null;
                int leadCost=int.MaxValue;
                for(int i=0;i<op.Squad.Count;i++)
                {
                    Pawn candidate=op.Squad[i];
                    if(!Active(candidate,map) || escortedDuringAssault.Contains(candidate))continue;
                    int cost=(candidate.Position-captive.Position).LengthHorizontalSquared;
                    if(cost<leadCost){lead=candidate;leadCost=cost;}
                }
                command.Leader=lead;
                if(lead!=null && RHFMod.Settings?.VerboseLogging==true)
                    Log.Message("[NJR][R020] Rescue leader assigned: "+lead.LabelShortCap+
                        ", target="+captive.LabelShortCap);
            }
            if(lead==null)
            {
                // An entire squad protecting first-liberated hostages is not
                // a valid leader for the next room's breach. Do not build an
                // A* route from the escort and then strand both subgroups.
                bool anyone=false;
                foreach(Pawn member in op.Squad)
                    if(Active(member,map)){anyone=true;break;}
                if(!anyone){op.Finished=true;board.Orders.Clear();}
                else if(now-Math.Max(op.LastProgressTick,op.PhaseStartTick)>27000)
                {
                    // Protect the survivors and the already rescued; this is
                    // a documented failed multi-captive assault, never success.
                    board.Decision="no free assault team; abort remaining captives";
                    op.Phase=2;
                    op.PhaseStartTick=now;
                    board.NextReplanTick=0;
                    board.LastPlanTick=0;
                    Extract(controller,op,now);
                    return;
                }
                board.Decision=anyone ?
                    "all fighters reserved for earlier captives; no assault lead" :
                    "rescue force destroyed";
                return;
            }
            if(previousTarget!=captive)
            {
                board.BestLeadDistance=int.MaxValue;
                board.LeadProgressCell=IntVec3.Invalid;
            }
            int leadDistance=(lead.Position-captive.Position).LengthHorizontalSquared;
            if(leadDistance<board.BestLeadDistance)
            {
                board.BestLeadDistance=leadDistance;
                board.LeadProgressCell=lead.Position;
                board.LastProgressTick=now;
                op.LastProgressTick=now;
            }
            if (InTouch(op,captive,map))
            {
                controller.S41FreeTarget(captive,op);
                board.Decision="contact established and prisoner released";
                board.LastProgressTick=now;
                board.LastPlanTick=0;
                board.NextReplanTick=0;
                // Rebind the just-freed target and allocate its escort now;
                // do not leave 90-180 ticks of stale Breach/Advance jobs.
                AssignFreedEscort(op,map,now,escortedDuringAssault);
                if(op.Phase==2)Extract(controller,op,now);
                else if(op.Phase==1)
                {
                    // If an incompatible guest state rejected FreeTarget,
                    // don't recurse on the same prisoner forever.
                    Pawn next=CurrentCaptive(op,map);
                    if(next!=captive)Infiltrate(controller,op,now);
                    else board.Decision="physical contact but release rejected; inspect guest state";
                }
                return;
            }
            if(now<board.NextReplanTick && board.LastPlanTick>0)return;
            board.NextReplanTick=now+PlanCadence;
            board.LastPlanTick=now;
            RHFRoutePlanV128 route=RHFObjectiveRouteV128.Resolve(op,map,lead,captive,now);
            Thing obstacle=route.Valid ? route.FirstBarrier : null;
            // If the expensive route solver cannot establish a corridor,
            // rally toward an actually reachable objective approach instead
            // of giving every soldier Hold for 27,000 ticks.
            IntVec3 fallback=route.Valid ? IntVec3.Invalid :
                ChooseReachableApproach(lead,captive,map);
            // A failed A* route is not permission to pick a random wall
            // approximately toward the captive. Only the validated shared
            // objective corridor may authorize demolition.
            if(!route.Valid)obstacle=null;
            if(obstacle!=null && (obstacle.Destroyed || !obstacle.Spawned))obstacle=null;
            if (board.Barrier != obstacle)
            {
                // Record each REAL change of entry rather than printing one
                // line per pawn/tick. Needed to diagnose unnecessary breaches.
                if(RHFMod.Settings?.VerboseLogging==true)
                    Log.Message("[NJR][R020][EntryChoice] captive="+
                        captive.LabelShortCap+", leader="+lead.LabelShortCap+
                        ", previous="+(board.Barrier?.LabelShortCap??"none")+
                        ", next="+(obstacle?.LabelShortCap??"none")+
                        ", doors="+route.Doors+", walls="+route.Walls+
                        ", nodes="+route.Expanded+", reason="+route.Explanation);
                board.Barrier=obstacle;
                board.Breacher=null;
                board.LastObstacleHP=obstacle?.HitPoints ?? 0;
                board.LastProgressTick=now;
            }
            else if(obstacle!=null && obstacle.HitPoints<board.LastObstacleHP)
            {
                board.LastObstacleHP=obstacle.HitPoints;
                board.LastProgressTick=now;
                op.LastProgressTick=now;
            }
            bool firebreakBeforeBreach=route.Valid &&
                route.FirstTurretExposureIndex>=0 &&
                (route.FirstBarrierIndex<0 ||
                 route.FirstTurretExposureIndex<=route.FirstBarrierIndex);
            if (obstacle != null && !firebreakBeforeBreach)
            {
                if(!Active(board.Breacher,map) ||
                   escortedDuringAssault.Contains(board.Breacher) ||
                   board.Breacher.health?.capacities==null ||
                   !board.Breacher.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation) ||
                   !board.Breacher.CanReach(obstacle.Position,PathEndMode.Touch,Danger.Deadly))
                    board.Breacher=RHFCombatPlannerV125.SelectBreachOperator(op,map,
                        obstacle,escortedDuringAssault);
            }
            else board.Breacher=null;
            // A long stalemate is a failed mission, never an endless "Wait".
            if(now-Math.Max(op.LastProgressTick,op.PhaseStartTick)>27000)
            {
                board.Decision="no objective progress; organized withdrawal";
                op.Phase=2;
                op.PhaseStartTick=now;
                return;
            }
            board.Threat=FirstRouteTurret(route,map);
            if(board.Threat==null && (!route.Valid ||
                route.FirstTurretExposureIndex<0))
                board.Threat=DefensiveTurretFront(op,map,lead);
            if(board.Threat!=null && board.Threat.HitPoints<board.LastThreatHP)
            {
                op.LastProgressTick=now;
                board.LastProgressTick=now;
            }
            board.LastThreatHP=board.Threat?.HitPoints ?? 0;
            int shooters=0;
            int supportBudget=board.Threat==null?0:Math.Max(2,op.Squad.Count/3);
            var allocated=new HashSet<IntVec3>();
            int screened=0;
            for(int i=0;i<op.Squad.Count;i++)
            {
                Pawn soldier=op.Squad[i];
                if(!Active(soldier,map) || escortedDuringAssault.Contains(soldier))continue;
                if(soldier==board.Breacher && obstacle!=null)
                {
                    board.Assign(soldier,RHFRoleS41.Breach,obstacle,obstacle.Position,now);
                    continue;
                }
                if(board.Threat!=null && shooters<supportBudget)
                {
                    IntVec3 fire=TurretFirePosition(soldier,board.Threat,map);
                    if(!fire.IsValid)
                        fire=TurretStagingCell(soldier,board.Threat,map,allocated);
                    if(fire.IsValid && !allocated.Contains(fire))
                    {
                        board.Assign(soldier,RHFRoleS41.SuppressTurret,board.Threat,fire,now);
                        allocated.Add(fire);shooters++;
                        continue;
                    }
                }
                IntVec3 way=route.Valid ? RHFObjectiveRouteV128.Waypoint(route,soldier,map) : IntVec3.Invalid;
                // Do not strand a dispersed member merely because the one
                // leader-owned path starts in a different reachable region.
                // This is a per-pawn PHYSICAL route, not a teleport or free shot.
                if(route.Valid && !way.IsValid && soldier!=lead)
                {
                    RHFRoutePlanV128 own=RHFObjectiveRouteV128.PlanIndividual(
                        map,soldier,captive,now);
                    if(own.Valid)
                    {
                        way=RHFObjectiveRouteV128.Waypoint(own,soldier,map);
                        // A soldier's local path is ONLY a movement aid.
                        // It cannot independently authorize a second demolition
                        // target while the operation already has a shared
                        // objective corridor and a single breacher.
                        if(!way.IsValid && own.FirstBarrier!=null &&
                           own.FirstBarrier!=obstacle)
                            way=IntVec3.Invalid;
                    }
                }
                if(obstacle!=null && !firebreakBeforeBreach &&
                   screened<Math.Max(2,op.Squad.Count/4))
                {
                    IntVec3 screen=ScreenCell(soldier,obstacle.Position,map,allocated);
                    if(screen.IsValid)
                    {
                        board.Assign(soldier,RHFRoleS41.Screen,null,screen,now);
                        screened++;
                        continue;
                    }
                }
                if(way.IsValid)
                    board.Assign(soldier,RHFRoleS41.Advance,null,way,now);
                else if(!route.Valid && obstacle==null && fallback.IsValid &&
                    !CrossesTurretLane(map,soldier.Position,fallback) &&
                    soldier.CanReach(fallback,PathEndMode.OnCell,Danger.Some) &&
                    (soldier.Position-fallback).LengthHorizontalSquared>4)
                    board.Assign(soldier,RHFRoleS41.Advance,null,fallback,now);
                else if(obstacle==null && !route.Valid &&
                    !CrossesTurretLane(map,soldier.Position,captive.Position) &&
                    soldier.CanReach(captive,PathEndMode.Touch,Danger.Deadly))
                    board.Assign(soldier,RHFRoleS41.Advance,captive,IntVec3.Invalid,now);
                else board.Assign(soldier,RHFRoleS41.Hold,null,soldier.Position,now);
            }
            board.Decision=obstacle!=null ?
                "breach="+obstacle.LabelShortCap+" owner="+(board.Breacher?.LabelShortCap??"MISSING")+
                "; screen="+screened+" suppress="+shooters+
                (board.Threat!=null && shooters==0?" / NO VIABLE TURRET FIRE POSITION":"") :
                route.Valid?"advance via cached corridor; suppress="+shooters:
                "no valid route: "+route.Explanation;
        }

        // A direct fallback Goto is NOT a tactical route. Do not allow the
        // vanilla pathfinder to drag stranded troops through a visible
        // turret firing lane simply because a waypoint could not be found.
        private static bool CrossesTurretLane(Map map,IntVec3 a,IntVec3 b)
        {
            if(map==null)return true;
            int steps=Math.Max(Math.Abs(b.x-a.x),Math.Abs(b.z-a.z));
            for(int i=1;i<=steps;i++)
            {
                IntVec3 sample=new IntVec3(a.x+(b.x-a.x)*i/steps,0,
                    a.z+(b.z-a.z)*i/steps);
                if(RHFObjectiveRouteV128.StagingExposedToTurret(map,sample))
                    return true;
            }
            return false;
        }

        // Bounded fallthrough for a failed route: choose a physically reachable
        // cell nearer the prisoner without teleporting through solid rock.
        // One lookup per strategic plan, never one A* scan per combat tick.
        private static IntVec3 ChooseReachableApproach(Pawn lead,Pawn captive,Map map)
        {
            if(lead==null || captive==null || map==null)return IntVec3.Invalid;
            int start=(lead.Position-captive.Position).LengthHorizontalSquared;
            IntVec3 best=IntVec3.Invalid;int bestDistance=start;
            // Probe a few points along the direct advance first.
            for(int step=9;step>=1;step--)
            {
                IntVec3 cell=new IntVec3(
                    lead.Position.x+(captive.Position.x-lead.Position.x)*step/10,
                    0,
                    lead.Position.z+(captive.Position.z-lead.Position.z)*step/10);
                if(!cell.InBounds(map) || !cell.Standable(map) ||
                   cell.GetEdifice(map)!=null)continue;
                int distance=(cell-captive.Position).LengthHorizontalSquared;
                if(distance>=bestDistance ||
                   !lead.CanReach(cell,PathEndMode.OnCell,Danger.Some))continue;
                best=cell;bestDistance=distance;
            }
            // If the direct line intersects a mountain, probe real reachable
            // positions around the cell. Caps keep the incident TPS bounded.
            if(!best.IsValid)
            {
                for(int radius=3;radius<=Math.Min(16,Math.Max(map.Size.x,map.Size.z));radius++)
                for(int dx=-radius;dx<=radius;dx++)
                for(int dz=-radius;dz<=radius;dz++)
                {
                    if(Math.Max(Math.Abs(dx),Math.Abs(dz))!=radius)continue;
                    IntVec3 cell=captive.Position+new IntVec3(dx,0,dz);
                    if(!cell.InBounds(map) || !cell.Standable(map) ||
                       cell.GetEdifice(map)!=null)continue;
                    int distance=(cell-captive.Position).LengthHorizontalSquared;
                    if(distance>=bestDistance)continue;
                    if(lead.CanReach(cell,PathEndMode.OnCell,Danger.Some))
                    {best=cell;bestDistance=distance;}
                }
            }
            return best;
        }

        // When every approach crosses a turret ring the safe objective route
        // may be invalid: never turn the entire assault into HOLD. Choose a
        // truly active turret visible from an achievable front; the ordinary
        // attack jobs retain real line-of-fire and weapon-range requirements.
        private static Building DefensiveTurretFront(RHFOperation op,Map map,Pawn lead)
        {
            if(map==null || lead==null || op?.Squad==null)return null;
            Building best=null, nearest=null;
            int cost=int.MaxValue, nearDist=int.MaxValue;
            foreach(Building turret in map.listerBuildings.allBuildingsColonist)
            {
                // All registered player turrets, not only the first 250 buildings.
                if(!RHFCombatPlannerV125.IsTurret(turret) ||
                   !turret.Spawned || turret.Destroyed)continue;
                // Fallback suppression needs an actual sighting by a living
                // rescuer, not omniscient knowledge of every hidden gun.
                bool spotted=false;
                foreach(Pawn observer in op.Squad)
                    if(Active(observer,map) &&
                       GenSight.LineOfSight(observer.Position,turret.Position,map))
                    {spotted=true;break;}
                if(!spotted)continue;
                int dist=(lead.Position-turret.Position).LengthHorizontalSquared;
                if(dist>7000)continue;
                if(dist<nearDist){nearest=turret;nearDist=dist;}
                bool feasible=false;
                foreach(Pawn soldier in op.Squad)
                {
                    if(!Active(soldier,map))continue;
                    if(TurretFirePosition(soldier,turret,map).IsValid)
                    {feasible=true;break;}
                }
                if(feasible && dist<cost){best=turret;cost=dist;}
            }
            // A turret ring outside current weapon range must not make the
            // entire squad HOLD. Advance physically until a firing lane is
            // available; never fabricate a shot or teleport past the guns.
            return best??nearest;
        }

        // Threat MUST be a real active player turret with a verified firing
        // line to the first exposed corridor cell. Bounded combat-lister scan.
        private static Thing FirstRouteTurret(RHFRoutePlanV128 route,Map map)
        {
            if(route==null || !route.Valid || route.FirstTurretExposureIndex<0 ||
               route.FirstTurretExposureIndex>=route.Cells.Count)return null;
            IntVec3 exposure=route.Cells[route.FirstTurretExposureIndex];
            Thing chosen=null;
            int score=int.MaxValue;
            foreach(Building building in map.listerBuildings.allBuildingsColonist)
            {
                // Keep the observed route exposure tied to an actual turret.
                if(building==null || building.Destroyed || !building.Spawned ||
                   !RHFCombatPlannerV125.IsTurret(building))continue;
                int distance=(building.Position-exposure).LengthHorizontalSquared;
                if(distance>4900 || distance>=score ||
                   !GenSight.LineOfSight(building.Position,exposure,map))continue;
                score=distance;chosen=building;
            }
            return chosen;
        }

        // A shooter needs an actual accessible line of fire, not a static
        // offset relative to a random prison door. Called only on strategy pulses.
        private static IntVec3 TurretFirePosition(Pawn soldier,Thing turret,Map map)
        {
            if(soldier==null || turret==null || map==null)return IntVec3.Invalid;
            float range=RHFCombatPlannerV125.WeaponRange(soldier);
            if(range<=2f)return IntVec3.Invalid;
            IntVec3 origin=soldier.Position;
            IntVec3 best=IntVec3.Invalid;
            int cost=int.MaxValue;
            // R019: check reachable fire positions within one tactical move,
            // rather than the whole map for every shooter every plan pulse.
            for(int x=Math.Max(0,origin.x-20);x<=Math.Min(map.Size.x-1,origin.x+20);x++)
            for(int z=Math.Max(0,origin.z-20);z<=Math.Min(map.Size.z-1,origin.z+20);z++)
            {
                IntVec3 cell=new IntVec3(x,0,z);
                if(!cell.InBounds(map) || !cell.Standable(map) ||
                   cell.GetRoom(map)!=origin.GetRoom(map))continue;
                int targetDist=(cell-turret.Position).LengthHorizontalSquared;
                if(targetDist>range*range ||
                   !GenSight.LineOfSight(cell,turret.Position,map))continue;
                int pathDist=(cell-origin).LengthHorizontalSquared;
                if(pathDist>=cost ||
                   !soldier.CanReach(cell,PathEndMode.OnCell,Danger.Some))continue;
                cost=pathDist;best=cell;
            }
            return best;
        }

        // Bounded physical bounding move for riflemen unable to fire on the
        // distant ring of turrets. Scores actual reachable cells by progress,
        // cover and nearby turret sight-lines. Does not create artificial fire.
        private static IntVec3 TurretStagingCell(Pawn soldier,Thing turret,Map map,
            HashSet<IntVec3> occupied)
        {
            if(soldier==null || turret==null || map==null ||
               RHFCombatPlannerV125.WeaponRange(soldier)<=2f)
                return IntVec3.Invalid;
            IntVec3 origin=soldier.Position;
            int starting=(origin-turret.Position).LengthHorizontalSquared;
            int dx=turret.Position.x-origin.x;
            int dz=turret.Position.z-origin.z;
            int scale=Math.Max(1,Math.Max(Math.Abs(dx),Math.Abs(dz)));
            IntVec3 best=IntVec3.Invalid;
            int bestCost=int.MaxValue;
            // Plan a short tactical leg; on the next pulse another leg may
            // advance. Full-map CanReach queries created severe TPS spikes.
            for(int x=Math.Max(0,origin.x-14);x<=Math.Min(map.Size.x-1,origin.x+14);x++)
            for(int z=Math.Max(0,origin.z-14);z<=Math.Min(map.Size.z-1,origin.z+14);z++)
            {
                IntVec3 cell=new IntVec3(x,0,z);
                if(!cell.InBounds(map) || !cell.Standable(map) ||
                   cell.GetEdifice(map)!=null || occupied.Contains(cell))continue;
                int distance=(cell-turret.Position).LengthHorizontalSquared;
                if(distance>=starting-9 ||
                   !soldier.CanReach(cell,PathEndMode.OnCell,Danger.Deadly))continue;
                int exposures=0;
                foreach(Building gun in map.listerBuildings.allBuildingsColonist)
                {
                    // Prevent later turrets from disappearing behind list caps.
                    if(!RHFCombatPlannerV125.IsTurret(gun))continue;
                    if((cell-gun.Position).LengthHorizontalSquared<=1600 &&
                       GenSight.LineOfSight(gun.Position,cell,map))exposures++;
                }
                // Progress must be real. Prefer cover, but do not turn an
                // unavoidable turret perimeter into an infinite HOLD order.
                int move=(cell-origin).LengthHorizontalSquared;
                int cost=distance+exposures*240+move;
                if(cost<bestCost){best=cell;bestCost=cost;}
            }
            return best;
        }

        private static IntVec3 ScreenCell(Pawn soldier,IntVec3 barrier,Map map,
            HashSet<IntVec3> occupied)
        {
            // Disperse instead of sending the entire wing to a single door cell.
            IntVec3 best=IntVec3.Invalid;int score=int.MaxValue;
            // A screen occupies nearby physical cover, not any cell on map.
            for(int x=Math.Max(0,soldier.Position.x-16);x<=Math.Min(map.Size.x-1,soldier.Position.x+16);x++)
            for(int z=Math.Max(0,soldier.Position.z-16);z<=Math.Min(map.Size.z-1,soldier.Position.z+16);z++)
            {
                IntVec3 cell=new IntVec3(x,0,z);
                if(!cell.InBounds(map)||!cell.Standable(map)||
                   cell.GetEdifice(map)!=null || occupied.Contains(cell) ||
                   (cell-barrier).LengthHorizontalSquared<9 ||
                   cell.GetRoom(map)!=soldier.Position.GetRoom(map) ||
                   !soldier.CanReach(cell,PathEndMode.OnCell,Danger.Some))continue;
                int value=(soldier.Position-cell).LengthHorizontalSquared+
                    (cell-barrier).LengthHorizontalSquared;
                if(value<score){score=value;best=cell;}
            }
            if(best.IsValid)occupied.Add(best);
            return best;
        }

        private static Map MapFor(RHFOperation op)
        {
            if(op?.Squad!=null)
                foreach(Pawn p in op.Squad)
                    if(p?.Map!=null)return p.Map;
            if(op?.Vip?.Map!=null)return op.Vip.Map;
            if(op?.Targets!=null)
                foreach(Pawn p in op.Targets)
                    if(p?.Map!=null)return p.Map;
            return null;
        }

        private static List<Pawn> ActivePursuers(RHFOperation op,Map map,
            IntVec3 convoy,IntVec3 exit)
        {
            var memory=Pursuits.GetValue(op,_=>new PursuitMemory());
            var result=new List<Pawn>();
            var stillHere=new HashSet<int>();
            IntVec3 heading=exit.IsValid?exit-convoy:IntVec3.Invalid;
            foreach(Pawn enemy in map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer))
            {
                if(enemy==null || !enemy.Spawned || enemy.Dead || enemy.Downed ||
                   enemy.Map!=map || op.Targets?.Contains(enemy)==true ||
                   op.Freed?.Contains(enemy)==true)continue;
                // Never infer a pursuer's position through impassable rocks.
                // Faction-scoped contact must be physically observed.
                bool observed=false;
                if(op.Squad!=null)
                    foreach(Pawn observer in op.Squad)
                        if(Active(observer,map) &&
                           GenSight.LineOfSight(observer.Position,enemy.Position,map))
                        {observed=true;break;}
                if(!observed)continue;
                int id=enemy.thingIDNumber;
                stillHere.Add(id);
                int distance=(enemy.Position-convoy).LengthHorizontalSquared;
                // An enemy ahead of the fleeing column is a blocking contact,
                // not a chaser: the escort's ordinary combat resolves it.
                bool behind=!heading.IsValid ||
                    (enemy.Position.x-convoy.x)*heading.x+
                    (enemy.Position.z-convoy.z)*heading.z<=40;
                if(behind && distance<=900 &&
                   memory.PreviousConvoy.IsValid &&
                   memory.Previous.TryGetValue(id,out IntVec3 before) &&
                   before!=enemy.Position &&
                   distance < (before-memory.PreviousConvoy).LengthHorizontalSquared)
                    result.Add(enemy);
                memory.Previous[id]=enemy.Position;
            }
            // Avoid retaining dead/despawned defenders from previous battles.
            var stale=new List<int>();
            foreach(int id in memory.Previous.Keys)
                if(!stillHere.Contains(id))stale.Add(id);
            foreach(int id in stale)memory.Previous.Remove(id);
            memory.PreviousConvoy=convoy;
            return result;
        }

        private static IntVec3 RearguardCell(Pawn soldier,Pawn captive,Pawn pursuer,
            Map map,HashSet<IntVec3> occupied)
        {
            IntVec3 best=IntVec3.Invalid;
            int bestScore=int.MaxValue;
            // Place the screen BETWEEN the advancing pursuer and convoy,
            // never on the destination edge ahead of the prisoner.
            for(int x=Math.Max(0,captive.Position.x-15);x<=Math.Min(map.Size.x-1,captive.Position.x+15);x++)
            for(int z=Math.Max(0,captive.Position.z-15);z<=Math.Min(map.Size.z-1,captive.Position.z+15);z++)
            {
                IntVec3 cell=new IntVec3(x,0,z);
                if(!cell.Standable(map) || cell.GetEdifice(map)!=null ||
                   occupied.Contains(cell) ||
                   (cell-captive.Position).LengthHorizontalSquared>225 ||
                   !soldier.CanReach(cell,PathEndMode.OnCell,Danger.Deadly))continue;
                int pursuit=(cell-pursuer.Position).LengthHorizontalSquared;
                int movement=(cell-soldier.Position).LengthHorizontalSquared;
                int score=pursuit*2+movement;
                if(RHFObjectiveRouteV128.StagingExposedToTurret(map,cell))
                    score+=15000;
                if(score<bestScore){best=cell;bestScore=score;}
            }
            if(best.IsValid)occupied.Add(best);
            return best;
        }

        // If no edge is passable, at least clear an actual obstructing wall
        // and maintain an active convoy, rather than returning from Extract()
        // with stale combat roles and idle rescued pawns forever.
        private static void RecoverBlockedExit(RHFOperation op,Map map,
            RHFTacticalBoardS41 board,Pawn anchor,Pawn navigator,int now)
        {
            board.Decision="no open exit: engage or breach toward nearest map edge";
            IntVec3[] edges={new IntVec3(0,0,navigator.Position.z),
                new IntVec3(map.Size.x-1,0,navigator.Position.z),
                new IntVec3(navigator.Position.x,0,0),
                new IntVec3(navigator.Position.x,0,map.Size.z-1)};
            Building barrier=null;
            int least=int.MaxValue;
            foreach(IntVec3 edge in edges)
            {
                Building candidate=NJRCombatContactS44.BlockingStructure(navigator,edge);
                // Do not destroy a random perimeter wall to escape. Only a
                // real reachable door is eligible for this emergency fallback.
                if(!(candidate is Building_Door))continue;
                int score=(navigator.Position-candidate.Position).LengthHorizontalSquared+
                    RHFCombatPlannerV125.BreachWorkCost(candidate);
                if(score<least){least=score;barrier=candidate;}
            }
            IntVec3 progress=NJRRescueExitS41.ReachableProgress(map,op,navigator);
            var reserved=new HashSet<Pawn>();
            // If a reachable progress cell exists, keep the liberated pair
            // together. If a sealed wall must be breached, shelter the VIP
            // until a physical access route has opened.
            if(op.Freed!=null)
                foreach(Pawn captive in op.Freed)
                {
                    if(captive==null || !captive.Spawned || captive.Map!=map)continue;
                    if(barrier==null && progress.IsValid && !captive.Downed)
                    {
                        RHFEscortLink pair=EnsureEscort(op,captive,map);
                        if(Active(pair?.Escort,map))
                        {
                            board.Assign(pair.Escort,RHFRoleS41.Escort,captive,progress,now);
                            board.Assign(captive,RHFRoleS41.FollowEscort,
                                pair.Escort,progress,now);
                            reserved.Add(pair.Escort);
                            continue;
                        }
                    }
                    board.Assign(captive,RHFRoleS41.Hold,null,captive.Position,now);
                }
            bool assigned=false;
            foreach(Pawn soldier in op.Squad)
            {
                if(!Active(soldier,map) || reserved.Contains(soldier))continue;
                if(!assigned && barrier!=null &&
                   soldier.CanReach(barrier.Position,PathEndMode.Touch,Danger.Deadly))
                {
                    board.Assign(soldier,RHFRoleS41.Breach,barrier,barrier.Position,now);
                    assigned=true;
                }
                else board.Assign(soldier,RHFRoleS41.Withdraw,null,
                    progress.IsValid?progress:anchor.Position,now);
            }
            board.NextReplanTick=now+PlanCadence;
        }

        internal static bool Extract(RHFOperationController controller,RHFOperation op,int now)
        {
            if(controller==null || op==null || op.Finished)return true;
            Map map=MapFor(op);
            if(map==null)return true;
            RHFTacticalBoardS41 board=op.BoardS41 ??= new RHFTacticalBoardS41();
            board.Prune(map);
            bool enteringWithdrawal=board.ObservedPhase!=op.Phase;
            if(enteringWithdrawal)
            {board.ObservedPhase=op.Phase;board.NextReplanTick=0;board.LastPlanTick=0;}

            board.Breacher=null; // breaching is over: every survivor may escort
            if (now<board.NextReplanTick && board.LastPlanTick>0)return true;
            board.NextReplanTick=now+PlanCadence;
            board.LastPlanTick=now;
            // A downed VIP is the *defence anchor*, not a pathfinding actor.
            // Always choose an actual mobile rescuer for exit routing. Asking
            // RCellFinder to find an exit for a downed pawn can deadlock all
            // carriers even when a reachable ground evacuation exists.
            Pawn anchor=null;
            Pawn navigator=null;
            if(op.Freed!=null)
                for(int i=0;i<op.Freed.Count;i++)
                {
                    Pawn freed=op.Freed[i];
                    if(freed==null || freed.Dead || freed.MapHeld!=map)continue;
                    if(anchor==null && freed.Spawned)anchor=freed;
                    if(navigator==null && Active(freed,map))navigator=freed;
                }
            if(op.Squad!=null)
                for(int i=0;i<op.Squad.Count;i++)
                {
                    Pawn member=op.Squad[i];
                    if(!Active(member,map))continue;
                    if(anchor==null)anchor=member;
                    if(navigator==null)navigator=member;
                }
            if(anchor==null)
            {
                // All soldiers may have left while the final patient is still
                // physically carried. Never record a false completed mission.
                if(op.Freed!=null && op.Freed.Exists(p=>p!=null && !p.Dead && p.MapHeld==map))
                {board.Decision="evacuation pending: captive is still held on map";return true;}
                op.Finished=true;op.Phase=4;board.Orders.Clear();return true;
            }
            if(navigator==null)
            {board.Decision="evacuation stalled: no mobile rescuer to carry VIP";return true;}
            if(enteringWithdrawal ||
               !NJRRescueExitS41.Eligible(map,op,navigator,op.ExitCell,Danger.Deadly))
            {
                // Freeze a single exit for all rescued pawns. A vanilla
                // TryFindBestExitSpot per moving pawn causes back-and-forth
                // between unrelated edges and can strand the VIP in a cave.
                op.ExitCell=NJRRescueExitS41.Choose(map,op,navigator);
            }
            board.WithdrawalCell=op.ExitCell;
            if(!board.WithdrawalCell.IsValid)
            {
                RecoverBlockedExit(op,map,board,anchor,navigator,now);
                return true;
            }
            int remaining=0;
            var paired=new HashSet<Pawn>();
            if(op.Freed!=null)
            for(int i=0;i<op.Freed.Count;i++)
            {
                Pawn rescued=op.Freed[i];
                if(rescued==null || rescued.Dead || rescued.MapHeld!=map ||
                   (op.PodLaunchCompleted && rescued==op.Vip))continue;
                remaining++;
                if(!rescued.Spawned)continue; // carried by a physical JobDriver
                RHFRescueCommandV124.StabilizeFreed(op,rescued);
                RHFEscortLink pair=EnsureEscort(op,rescued,map);
                if(rescued.Downed)
                {
                    // The mission's ThinkTree is the ONLY carrier job writer.
                    // Old carrier dispatch injected ordered jobs and could
                    // steal a different VIP's escort or the pod engineer.
                    Pawn carrier=pair?.Escort;
                    if(Active(carrier,map))
                    {
                        paired.Add(carrier);
                        board.Assign(carrier,RHFRoleS41.Escort,rescued,
                            board.WithdrawalCell,now);
                    }
                    board.Assign(rescued,RHFRoleS41.Hold,null,rescued.Position,now);
                    continue;
                }
                Pawn escort=pair?.Escort;
                if(!Active(escort,map))
                {board.Assign(rescued,RHFRoleS41.Hold,null,rescued.Position,now);continue;}
                paired.Add(escort);
                board.Assign(rescued,RHFRoleS41.FollowEscort,escort,board.WithdrawalCell,now);
                board.Assign(escort,RHFRoleS41.Escort,rescued,board.WithdrawalCell,now);
                if((rescued.Position-board.WithdrawalCell).LengthHorizontalSquared<=10 &&
                   (escort.Position-board.WithdrawalCell).LengthHorizontalSquared<=25 &&
                   (rescued.Position-escort.Position).LengthHorizontalSquared<=36)
                {
                    // RimWorld ExitMap is not an atomic pair API. Fail closed:
                    // never remove the VIP first, leaving them unescorted.
                    // Preflight both pawns, then exit the escort first.
                    if(Active(escort,map) && rescued.Spawned &&
                       !rescued.InMentalState &&
                       escort.CanReach(board.WithdrawalCell,PathEndMode.OnCell,Danger.Some) &&
                       rescued.CanReach(board.WithdrawalCell,PathEndMode.OnCell,Danger.Some))
                    {
                        RHFRescueCommandV124.MissionExit(escort);
                        if(escort.MapHeld!=map)
                        {
                            RHFRescueCommandV124.MissionExit(rescued);
                            if(rescued.MapHeld!=map)op.EscapedWithEscort++;
                            else
                            {
                                // Real escort left but the VIP did not: select
                                // another one instead of declaring success.
                                pair.Escort=null;
                                board.NextReplanTick=0;
                                board.Decision="pair exit incomplete: VIP stays on map";
                            }
                        }
                    }
                }
            }
            // A rearguard is NOT automatic. It exists only while player
            // defenders really move to catch the already escaping convoy.
            // Other mobile rescuers withdraw WITH the prisoners, not to the
            // map edge alone and then back to the prisoner in a loop.
            Pawn convoy=anchor;
            if(op.Escorts!=null)
                foreach(RHFEscortLink link in op.Escorts)
                    if(link?.Captive!=null && op.Freed?.Contains(link.Captive)==true &&
                       Active(link.Escort,map))
                    {convoy=link.Escort;break;}
            List<Pawn> chasers=remaining>0 ?
                ActivePursuers(op,map,convoy.Position,board.WithdrawalCell) :
                new List<Pawn>();
            var available=new List<Pawn>();
            foreach(Pawn soldier in op.Squad)
                if(Active(soldier,map) && !paired.Contains(soldier))
                    available.Add(soldier);
            available.Sort((a,b)=>
            {
                if(chasers.Count==0)return a.thingIDNumber.CompareTo(b.thingIDNumber);
                int ad=int.MaxValue,bd=int.MaxValue;
                foreach(Pawn enemy in chasers)
                {ad=Math.Min(ad,(a.Position-enemy.Position).LengthHorizontalSquared);
                 bd=Math.Min(bd,(b.Position-enemy.Position).LengthHorizontalSquared);}
                return ad!=bd?ad.CompareTo(bd):a.thingIDNumber.CompareTo(b.thingIDNumber);
            });
            int rearCount=remaining>0 && paired.Count>0 ?
                Math.Min(chasers.Count,Math.Max(1,available.Count/2)) : 0;
            var coverSlots=new HashSet<IntVec3>();
            for(int i=0;i<available.Count;i++)
            {
                Pawn soldier=available[i];
                if(remaining>0 && i<rearCount)
                {
                    Pawn chaser=chasers[0];int nearest=int.MaxValue;
                    foreach(Pawn enemy in chasers)
                    {
                        int d=(soldier.Position-enemy.Position).LengthHorizontalSquared;
                        if(d<nearest){nearest=d;chaser=enemy;}
                    }
                    IntVec3 cover=RearguardCell(soldier,convoy,chaser,map,coverSlots);
                    board.Assign(soldier,RHFRoleS41.Screen,chaser,
                        cover.IsValid?cover:soldier.Position,now);
                }
                else if(remaining>0)
                {
                    board.Assign(soldier,RHFRoleS41.Withdraw,null,
                        convoy.Position,now);
                }
                else
                {
                    board.Assign(soldier,RHFRoleS41.Withdraw,null,board.WithdrawalCell,now);
                    if((soldier.Position-board.WithdrawalCell).LengthHorizontalSquared<=10)
                        RHFRescueCommandV124.MissionExit(soldier);
                }
            }
            if(remaining==0)
            {
                if(op.AssaultAfterObjective && (op.EscapedWithEscort>0 || op.PodLaunchCompleted))
                { op.Phase=5;op.PhaseStartTick=now; }
                else
                {
                    bool anySoldier=false;
                    foreach(Pawn s in op.Squad)if(Active(s,map)){anySoldier=true;break;}
                    if(!anySoldier){op.Phase=4;op.Finished=true;board.Orders.Clear();}
                }
            }
            board.Decision="extraction; remaining="+remaining+"; escorted="+
                op.EscapedWithEscort+"; pursuers="+chasers.Count+
                "; rearguard="+rearCount;
            return true;
        }
    }

    public sealed partial class RHFOperationController
    {
        internal void S41FreeTarget(Pawn captive,RHFOperation operation)
        {
            // Only the controller owns the original guest/faction side-effects.
            FreeTarget(operation,captive);
        }
    }
}
