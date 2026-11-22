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
        private const int MaxSquad = 96;
        private const int MaxTargets = 24;
        private const int PlanCadence = 180;
        private sealed class BarrierEventCache
        {
            internal readonly Dictionary<int,int> Seen = new Dictionary<int,int>();
        }
        private static readonly ConditionalWeakTable<RHFOperation,BarrierEventCache>
            BarrierPulses = new ConditionalWeakTable<RHFOperation,BarrierEventCache>();

        internal static RHFOperation MissionFor(Map map, Pawn member)
        {
            if (map == null || member == null) return null;
            List<RHFOperation> operations = RHFMissionIntegrityV130.Operations(map);
            if (operations == null) return null;
            for (int i = 0; i < operations.Count && i < 4; i++)
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
            for (int i = 0; i < op.Targets.Count && i < MaxTargets; i++)
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
            for(int i=0;i<op.Squad.Count && i<MaxSquad;i++)
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
            for (int i = 0; i < op.Squad.Count && i < MaxSquad; i++)
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
            for (int i=0;i<op.Squad.Count && i<MaxSquad;i++)
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
            for(int i=0;i<op.Freed.Count && i<MaxTargets;i++)
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
            for(int i=0;i<operations.Count && i<4;i++)
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
                for(int j=0;j<op.Targets.Count && j<MaxTargets;j++)
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
                    if(cache.Seen.Count>MaxTargets*3)cache.Seen.Clear();
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
            RHFTacticalBoardS41 board=op.BoardS41 ??= new RHFTacticalBoardS41();
            board.Prune(map);
            if(board.ObservedPhase!=op.Phase)
            {board.ObservedPhase=op.Phase;board.NextReplanTick=0;board.LastPlanTick=0;}

            // Sync with actual release state even when a mod altered guest status
            // without going through standard GenGuest/SetGuestStatus APIs.
            if (op.Targets != null)
                for(int i=0;i<op.Targets.Count && i<MaxTargets;i++)
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
            Pawn lead=null;
            for(int i=0;i<op.Squad.Count && i<MaxSquad;i++)
                if(Active(op.Squad[i],map) &&
                   !escortedDuringAssault.Contains(op.Squad[i]))
                {lead=op.Squad[i];break;}
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
            if(obstacle!=null && (obstacle.Destroyed || !obstacle.Spawned))obstacle=null;
            if (board.Barrier != obstacle)
            {
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
            if(board.Threat!=null && board.Threat.HitPoints<board.LastThreatHP)
            {
                op.LastProgressTick=now;
                board.LastProgressTick=now;
            }
            board.LastThreatHP=board.Threat?.HitPoints ?? 0;
            int shooters=0;
            int supportBudget=board.Threat==null?0:Math.Min(8,Math.Max(2,op.Squad.Count/3));
            var allocated=new HashSet<IntVec3>();
            int screened=0;
            for(int i=0;i<op.Squad.Count && i<MaxSquad;i++)
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
                    if(fire.IsValid && !allocated.Contains(fire))
                    {
                        board.Assign(soldier,RHFRoleS41.SuppressTurret,board.Threat,fire,now);
                        allocated.Add(fire);shooters++;
                        continue;
                    }
                }
                IntVec3 way=route.Valid ? RHFObjectiveRouteV128.Waypoint(route,soldier,map) : IntVec3.Invalid;
                if(obstacle!=null && !firebreakBeforeBreach &&
                   screened<Math.Min(5,Math.Max(2,op.Squad.Count/4)))
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
                else if(obstacle==null && soldier.CanReach(captive,PathEndMode.Touch,Danger.Deadly))
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

        // Threat MUST be a real active player turret with a verified firing
        // line to the first exposed corridor cell. Bounded combat-lister scan.
        private static Thing FirstRouteTurret(RHFRoutePlanV128 route,Map map)
        {
            if(route==null || !route.Valid || route.FirstTurretExposureIndex<0 ||
               route.FirstTurretExposureIndex>=route.Cells.Count)return null;
            IntVec3 exposure=route.Cells[route.FirstTurretExposureIndex];
            Thing chosen=null;
            int score=int.MaxValue,seen=0;
            foreach(Building building in map.listerBuildings.allBuildingsColonist)
            {
                if(++seen>200)break;
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
            for(int dx=-12;dx<=12;dx+=3)
            for(int dz=-12;dz<=12;dz+=3)
            {
                IntVec3 cell=origin+new IntVec3(dx,0,dz);
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

        private static IntVec3 ScreenCell(Pawn soldier,IntVec3 barrier,Map map,
            HashSet<IntVec3> occupied)
        {
            // Disperse instead of sending the entire wing to a single door cell.
            IntVec3 best=IntVec3.Invalid;int score=int.MaxValue;
            // Hard cap: 12 candidates * <=5 screen fighters per 180 ticks.
            // Avoid 64+ expensive CanReach path probes per soldier.
            int[] dx={-4,4,0,0,-6,6,-4,4,-6,6,-2,2};
            int[] dz={0,0,-4,4,-2,2,-4,-4,4,4,6,-6};
            for(int i=0;i<dx.Length;i++)
            {
                IntVec3 cell=barrier+new IntVec3(dx[i],0,dz[i]);
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

        internal static bool Extract(RHFOperationController controller,RHFOperation op,int now)
        {
            if(controller==null || op==null || op.Finished)return true;
            Map map=MapFor(op);
            if(map==null)return true;
            RHFTacticalBoardS41 board=op.BoardS41 ??= new RHFTacticalBoardS41();
            board.Prune(map);
            if(board.ObservedPhase!=op.Phase)
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
                for(int i=0;i<op.Freed.Count && i<MaxTargets;i++)
                {
                    Pawn freed=op.Freed[i];
                    if(freed==null || freed.Dead || freed.MapHeld!=map)continue;
                    if(anchor==null && freed.Spawned)anchor=freed;
                    if(navigator==null && Active(freed,map))navigator=freed;
                }
            if(op.Squad!=null)
                for(int i=0;i<op.Squad.Count && i<MaxSquad;i++)
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
            if(!op.ExitCell.IsValid || !op.ExitCell.InBounds(map) ||
               !navigator.CanReach(op.ExitCell,PathEndMode.OnCell,Danger.Some))
            {
                // A stale exit cell must not survive a failed exit recompute:
                // otherwise escorts keep receiving impossible Goto orders.
                op.ExitCell=IntVec3.Invalid;
                if(RCellFinder.TryFindBestExitSpot(navigator,out IntVec3 cell) &&
                   cell.IsValid && cell.InBounds(map) &&
                   navigator.CanReach(cell,PathEndMode.OnCell,Danger.Some))
                    op.ExitCell=cell;
            }
            board.WithdrawalCell=op.ExitCell;
            if(!board.WithdrawalCell.IsValid)
            {board.Decision="no reachable map edge for mobile rescuer; extraction pending";return true;}
            int remaining=0;
            var paired=new HashSet<Pawn>();
            if(op.Freed!=null)
            for(int i=0;i<op.Freed.Count && i<MaxTargets;i++)
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
            var coverSlots=new HashSet<IntVec3>();
            int coverCount=0;
            foreach(Pawn soldier in op.Squad)
            {
                if(!Active(soldier,map) || paired.Contains(soldier))continue;
                if(remaining>0)
                {
                    IntVec3 cover=coverCount<8 ?
                        ScreenCell(soldier,anchor.Position,map,coverSlots):IntVec3.Invalid;
                    if(cover.IsValid)
                    { board.Assign(soldier,RHFRoleS41.Screen,null,cover,now);coverCount++; }
                    else // Can't screen, so remain available for VIP defence.
                        board.Assign(soldier,RHFRoleS41.Hold,null,soldier.Position,now);
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
            board.Decision="extraction; remaining="+remaining+"; escorted="+op.EscapedWithEscort;
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
