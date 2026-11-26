// RHF S3.1.1 — native construction prototype, fail-closed extraction.
// This is a PARTIAL implementation of S3, not a confirmed successful pod flight.
// State and physical stock refs live on RHFOperation (Scribe in RHFOperations.cs).
using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace RaidersHaveFamilies
{
    public sealed partial class RHFOperationController
    {
        private const int NativeVipWorkTimeoutS311 = 15000;
        private const int NativeVipBoardTimeoutS311 = 5400;

        // Only a successfully landed RESCUE drop party owns a construction kit.
        // Supply is issued at arrival, not conjured next to a freed VIP later.
        private RHFStrikeParty NativeRescueOwnerS311(RHFOperation op)
        {
            if (op?.Parties == null) return null;
            foreach (RHFStrikeParty party in op.Parties)
                if (party != null && party.Succeeded && party.Drop &&
                    party.Role == (int)RHFStrikeRole.Rescue && party.Faction != null)
                    return party;
            return null;
        }

        private static bool NativeAliveS311(Pawn pawn, Map location)
        {
            return pawn != null && pawn.Spawned && pawn.Map == location &&
                !pawn.Dead && !pawn.Downed && pawn.jobs != null;
        }

        private Pawn NativeEngineerS311(RHFOperation op, IntVec3 cell, Pawn excluded=null, Pawn excluded2=null,bool prepare=true,HashSet<IntVec3> pocket=null)
        {
            RHFStrikeParty party = NativeRescueOwnerS311(op);
            if (party == null || op.Squad == null) return null;
            Pawn best = null;
            int bestDistance = int.MaxValue;
            for (int i = 0; i < op.Squad.Count; i++)
            {
                Pawn p = op.Squad[i];
                if (p==excluded || p==excluded2 || !NativeAliveS311(p, map) || (pocket!=null && !pocket.Contains(p.Position)) || p.Faction != party.Faction ||
                    p.InMentalState || RHFCombatPlannerV125.IsCombatEmergencyJob(p) ||
                    p.health?.capacities == null ||
                    !p.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation) ||
                    !p.CanReach(cell, PathEndMode.Touch, Danger.Deadly)) continue;
                if (p.WorkTypeIsDisabled(WorkTypeDefOf.Construction) || p.skills?.GetSkill(SkillDefOf.Construction)==null) continue;
                // Preserve one actual builder while supply carriers unload.
                // A carrier may still build if no free qualified soldier exists.
                int dist = (p.Position - cell).LengthHorizontalSquared +
                    (RHFPodSupplyS41.HasCargo(op,p) ? 100000 : 0) -
                    (p.skills?.GetSkill(SkillDefOf.Construction)?.Level??0)*5000;
                if(p.jobs?.curJob?.targetA.Thing is Thing target && target.Position==cell &&
                   (p.jobs.curJob.def?.defName??"").Contains("Construct"))dist-=1000000;
                if (dist >= bestDistance) continue;
                bestDistance = dist;
                best = p;
            }
            if (best == null || (prepare && !NativePrepareEngineerS41(best))) return null;
            return best;
        }

        private Pawn NativeRefuelerR041(RHFOperation op,Pawn excluded,Pawn excluded2=null)
        {
            if(!(op.PodLauncherThing is ThingWithComps launcher) || !launcher.Spawned ||
               launcher.GetComp<CompRefuelable>()==null)return null;
            Pawn best=null;int score=int.MaxValue;
            foreach(Pawn p in op.Squad)
            {
                if(p==excluded || p==excluded2 || p?.jobs?.curJob?.def?.defName=="NJR_MoveSealCargoR044" || !NativeAliveS311(p,map) || p.InMentalState ||
                   p.Faction!=op.PodCargoOwnerFactionS311 || p.WorkTypeIsDisabled(WorkTypeDefOf.Hauling) ||
                   RHFPodSupplyS41.HasCargo(op,p) || RHFCombatPlannerV125.IsCombatEmergencyJob(p) ||
                   p.health?.capacities?.CapableOf(PawnCapacityDefOf.Manipulation)!=true ||
                   !p.CanReach(launcher,PathEndMode.Touch,Danger.Some))continue;
                bool canFuel=p.jobs?.curJob?.def?.defName=="Refuel" && p.jobs.curJob.targetA.Thing==launcher;
                if(!canFuel && op.PodFuelStacksS311!=null)
                    foreach(Thing stack in op.PodFuelStacksS311)
                        if(stack!=null && stack.Spawned && !stack.Destroyed && stack.Map==map &&
                           p.CanReach(stack,PathEndMode.Touch,Danger.Some)){canFuel=true;break;}
                if(!canFuel)continue;
                RHFOrderS41 prior=op.BoardS41?.For(p,false);
                int n=(p.Position-launcher.Position).LengthHorizontalSquared +
                    (p.skills?.GetSkill(SkillDefOf.Construction)?.Level??0)*40;
                if(prior?.Role==(int)RHFRoleS41.Engineer && prior.Objective==launcher)n-=100000;
                if(n>=score)continue;
                best=p;score=n;
            }
            if(best==null)return null;
            best.workSettings?.EnableAndInitializeIfNotAlreadyInitialized();
            if(best.workSettings==null)return null;
            best.workSettings.SetPriority(WorkTypeDefOf.Hauling,3);
            return best;
        }

        private static bool NativePrepareEngineerS41(Pawn p)
        {
            try
            {
                p.workSettings?.EnableAndInitializeIfNotAlreadyInitialized();
                if(p.workSettings==null)return false;
                p.workSettings.SetPriority(WorkTypeDefOf.Construction,3);
                // Vanilla LordToil_Siege similarly trains its assigned builders
                // up to the actual construction prerequisite. This only
                // applies to the mission engineer, not the entire raid.
                ThingDef launcher=DefDatabase<ThingDef>.GetNamedSilentFail("PodLauncher");
                ThingDef pod=DefDatabase<ThingDef>.GetNamedSilentFail("TransportPod");
                int required=Math.Max(launcher?.constructionSkillPrerequisite ?? 0,
                    pod?.constructionSkillPrerequisite ?? 0);
                p.skills?.GetSkill(SkillDefOf.Construction)?.EnsureMinLevelWithMargin(required);
                if(!p.WorkTypeIsDisabled(WorkTypeDefOf.Hauling))
                    p.workSettings.SetPriority(WorkTypeDefOf.Hauling,3);
                return true;
            }
            catch(Exception e)
            {Log.Warning("[RHF][AirS41] Engineer rejected: "+e.Message);return false;}
        }

        private static void NativeAddStacksS311(List<Thing> destination, ThingDef def,
            int total, int maxPerStack = 12)
        {
            if (total < 0 || def == null || def.stackLimit <= 0)
                throw new InvalidOperationException("Invalid supply quantity or stack definition");
            while (total > 0)
            {
                Thing thing = ThingMaker.MakeThing(def);
                int count = Math.Min(total, Math.Min(def.stackLimit, maxPerStack));
                thing.stackCount = count;
                destination.Add(thing);
                total -= count;
                // Number of supply stacks is limited by actual issued cargo
                // and carrier capacity, never by an arbitrary 48-stack cap.
            }
        }

        private static void NativeCancelAirBeforeExtractionS311(RHFOperation op, string why)
        {
            // The captive is usually still imprisoned when supplies arrive.
            // Cancel only the *air option*; do not abandon the rescue objective.
            op.TryPod = false;
            op.FortifyPrison = false;
            Log.Warning("[RHF][S311] Air plan canceled before VIP extraction: " + why);
        }

        // Called from the strategic decision loop, after actual drop arrivals.
        // A one-way latch prevents save/load replenishment and duplicates.
        private void NativeIssueCargoOnArrivalS311(RHFOperation op, int now)
        {
            if (op == null || !op.TryPod || !op.PodBudgetPaid || op.PodSuppliesIssued)
                return;
            RHFStrikeParty party = NativeRescueOwnerS311(op);
            if (party == null || op.Squad == null) return;
            Pawn owner = null;
            foreach (Pawn pawn in op.Squad)
                if (NativeAliveS311(pawn, map) && pawn.Faction == party.Faction)
                { owner = pawn; break; }
            if (owner == null) return; // Droppods can arrive several ticks later.
            ThingDef launcher = DefDatabase<ThingDef>.GetNamedSilentFail("PodLauncher");
            ThingDef pod = DefDatabase<ThingDef>.GetNamedSilentFail("TransportPod");
            CompProperties_Refuelable fuelProps =
                launcher?.GetCompProperties<CompProperties_Refuelable>();
            int fuel = fuelProps == null ? 0 : Mathf.CeilToInt(fuelProps.fuelCapacity);
            int steel = FieldCost(launcher, ThingDefOf.Steel) +
                FieldCost(pod, ThingDefOf.Steel) + (op.FortifyPrison && op.SealCountR052==0 ? 20 : 0);
            int components = FieldCost(launcher, ThingDefOf.ComponentIndustrial) +
                FieldCost(pod, ThingDefOf.ComponentIndustrial);
            if (launcher == null || pod == null || steel <= 0 || components <= 0 || fuel <= 0)
            { NativeCancelAirBeforeExtractionS311(op, "native defs/fuel missing"); return; }
            if (steel > 2 * ThingDefOf.Steel.stackLimit ||
                components > ThingDefOf.ComponentIndustrial.stackLimit ||
                fuel > 16 * ThingDefOf.Chemfuel.stackLimit)
            { NativeCancelAirBeforeExtractionS311(op, "modded supply exceeds tracked stack capacity"); return; }

            // Make real small stacks; their actual mass must fit into the
            // existing raiders' inventories. Nothing lands as an extra drop.
            var supplied = new List<Thing>();
            var carried = new List<Thing>();
            try
            {
                if(op.FortifyPrison && op.SealCountR052>0)
                {
                    if(op.SealStuffR052==null || op.SealUnitsR052<=0)
                        throw new InvalidOperationException("paid seal material unavailable");
                    float issuedCost=0f;int issuedUnits=0;
                    foreach(ThingDefCountClass item in ThingDefOf.Wall.CostListAdjusted(op.SealStuffR052,errorOnNullStuff:false))
                    {
                        if(item?.thingDef==null || item.count<=0)continue;
                        issuedCost+=item.count*op.SealCountR052*item.thingDef.BaseMarketValue;
                        if(item.thingDef==op.SealStuffR052)issuedUnits+=item.count;
                        NativeAddStacksS311(supplied,item.thingDef,item.count*op.SealCountR052,Math.Min(24,item.count));
                    }
                    if(issuedUnits!=op.SealUnitsR052 || Math.Abs(issuedCost-op.SealCostR052)>0.1f)
                        throw new InvalidOperationException("wall recipe changed after raid funding");
                }
                NativeAddStacksS311(supplied, ThingDefOf.Steel, steel, 8);
                NativeAddStacksS311(supplied, ThingDefOf.ComponentIndustrial, components, 6);
                NativeAddStacksS311(supplied, ThingDefOf.Chemfuel, fuel, 24);
                var candidates = new List<Pawn>();
                foreach (Pawn soldier in op.Squad)
                    if (NativeAliveS311(soldier, map) && soldier.Faction == party.Faction &&
                        soldier.inventory?.innerContainer != null &&
                        soldier.health?.capacities?.CapableOf(PawnCapacityDefOf.Manipulation) == true)
                        candidates.Add(soldier);
                if (candidates.Count == 0)
                    throw new InvalidOperationException("no living mission cargo carriers");
                // Order by remaining real carrying capacity. A soldier who
                // can't carry a stack never receives an overweight phantom kit.
                foreach (Thing stack in supplied)
                {
                    Pawn carrier = null;
                    float mostFree = -1f;
                    foreach (Pawn p in candidates)
                    {
                        float available = MassUtility.FreeSpace(p);
                        if (available <= mostFree ||
                            MassUtility.WillBeOverEncumberedAfterPickingUp(
                                p, stack, stack.stackCount))continue;
                        carrier = p; mostFree = available;
                    }
                    if (carrier == null ||
                        !carrier.inventory.innerContainer.TryAdd(stack,
                            canMergeWithExistingStacks: false))
                        throw new InvalidOperationException("no physical inventory room for " + stack);
                    carried.Add(stack);
                }
                // One-way latch after the entire real payload has been placed
                // in existing pawn inventories. Subsequent phases only move it.
                op.PodSuppliesIssued = true;
                op.PodNativeS311 = true;
                op.PodCargoOwnerFactionS311 = party.Faction;
                op.PodFuelStacksS311.Clear();
                op.PodCargoStacksS41.Clear();
                int steelIndex = 0;
                foreach (Thing stack in carried)
                {
                    op.PodCargoStacksS41.Add(stack);
                    if (stack.def == ThingDefOf.Steel)
                    {
                        if (steelIndex++ == 0) op.PodSteel = stack;
                        else if (steelIndex == 2) op.PodSteelExtra = stack;
                    }
                    else if (stack.def == ThingDefOf.ComponentIndustrial)
                        op.PodComponents = stack;
                    else if (stack.def == ThingDefOf.Chemfuel)
                        op.PodFuelStacksS311.Add(stack);
                }
                op.PodStageStartedTickS311 = now;
                Log.Message("[RHF][AirS41] Cargo carried by " + candidates.Count +
                    " mission soldiers, owner=" + party.Faction +
                    "; steel=" + steel + ", components=" + components + ", fuel=" + fuel+
                    "; seals="+op.SealLimitR052+" stuff="+(op.SealStuffR052??ThingDefOf.Steel).defName);
            }
            catch (Exception e)
            {
                // No replenishment. Real already-carried stacks stay on
                // those soldiers and can be looted if they are defeated.
                foreach (Thing stack in supplied)
                    if (stack.holdingOwner == null && !stack.Destroyed) stack.Destroy();
                op.PodSuppliesIssued = true;
                op.PodNativeS311 = true;
                Log.Warning("[RHF][AirS41] Insufficient physical inventory cargo: " + e);
                NativeCancelAirBeforeExtractionS311(op, "air cargo could not be carried by actual raid");
            }
        }

        private static Thing NativeAtSiteS311(Map map, IntVec3 cell, ThingDef def, Faction faction)
        {
            if (!cell.IsValid || !cell.InBounds(map)) return null;
            foreach (Thing thing in map.thingGrid.ThingsListAt(cell))
                if (thing != null && !thing.Destroyed && thing.def == def &&
                    thing.Faction == faction) return thing;
            return null;
        }

        private static Thing NativeWorkSiteS311(Map map, IntVec3 cell, Faction faction)
        {
            if (!cell.IsValid || !cell.InBounds(map)) return null;
            foreach (Thing thing in map.thingGrid.ThingsListAt(cell))
                if (!thing.Destroyed && thing.Faction == faction &&
                    (thing is Blueprint_Build || thing is Frame)) return thing;
            return null;
        }

        private static bool NativeJobFromWorkGiverS311(Pawn pawn, Thing target, string defName)
        {
            WorkGiverDef def = DefDatabase<WorkGiverDef>.GetNamedSilentFail(defName);
            if (!(def?.Worker is WorkGiver_Scanner giver)) return false;
            try
            {
                Job job = giver.JobOnThing(pawn, target, true);
                if (job == null) return false;
                // Never interrupt suppression, rescue wounds or a real job to
                // restart the same task on every strategic pulse.
                if (RHFCombatPlannerV125.IsCombatEmergencyJob(pawn)) return false;
                if (pawn.jobs?.curJob?.def == job.def &&
                    pawn.jobs.curJob.targetA == job.targetA) return true;
                return pawn.jobs != null && pawn.jobs.TryTakeOrderedJob(job);
            }
            catch (Exception e)
            {
                Log.Warning("[RHF][S311] Native workgiver " + defName + " rejected: " + e.Message);
                return false;
            }
        }

        private bool NativeConstructionOrderS311(Pawn engineer, Thing site)
        {
            // S4.1: strategy writes a role, only ThinkTree issues the
            // native physical construction job. Never issue both.
            if(RHFRescuePlannerS41.Enabled)return true;
            // Vanilla WorkGivers, vanilla JobDrivers, vanilla Frame resource
            // consumption and pawn work ticks. No ThingMaker completed building.
            if (site is Blueprint_Build)
                return NativeJobFromWorkGiverS311(engineer, site,
                    "ConstructDeliverResourcesToBlueprints");
            if (site is Frame)
                return NativeJobFromWorkGiverS311(engineer, site,
                    "ConstructDeliverResourcesToFrames") ||
                    NativeJobFromWorkGiverS311(engineer, site, "ConstructFinishFrames");
            return false;
        }

        private void NativeDefendSiteS311(RHFOperation op, Pawn engineer, int now)
        {
            // Screen roles now belong to S4.1 Blackboard/ThinkTree.
            if(RHFRescuePlannerS41.Enabled)return;
            if (!op.PodLauncherCell.IsValid || now % 360 > 90 || op.Squad == null) return;
            int issued = 0;
            int[] dx = {-5, 5, -5, 5, 0, 0};
            int[] dz = {-4, -4, 4, 4, -6, 6};
            foreach (Pawn guard in op.Squad)
            {
                if (issued >= 4) break;
                if (!NativeAliveS311(guard, map) || guard == engineer ||
                    guard.Faction != op.PodCargoOwnerFactionS311 ||
                    RHFCombatPlannerV125.IsCombatEmergencyJob(guard)) continue;
                int slot = issued;
                IntVec3 guardCell = op.PodLauncherCell + new IntVec3(dx[slot], 0, dz[slot]);
                if (!guardCell.InBounds(map) || !guardCell.Standable(map) ||
                    !guard.CanReach(guardCell, PathEndMode.OnCell, Danger.Deadly)) continue;
                if ((guard.Position - guardCell).LengthHorizontalSquared > 16 &&
                    guard.jobs?.curJob?.def != JobDefOf.Goto)
                    OrderGotoIfNeeded(guard, guardCell, 9);
                issued++;
            }
        }

        private void NativeFallBackS311(RHFOperation op, int now, string reason)
        {
            if (op.Vip != null && !op.Vip.Spawned && op.PodVehicleThing != null)
            {
                CompTransporter transporter =
                    (op.PodVehicleThing as ThingWithComps)?.GetComp<CompTransporter>();
                if (transporter != null && transporter.innerContainer.Contains(op.Vip))
                {
                    Thing returned;
                    if (!transporter.innerContainer.TryDrop(op.Vip, op.PodCell,
                        map, ThingPlaceMode.Near, out returned))
                    {
                        Log.Error("[RHF][S311] CRITICAL: loaded VIP cannot be returned on fallback");
                        return; // Never strand a VIP by silently completing the op.
                    }
                }
                if (!op.Vip.Spawned)
                {
                    Log.Error("[RHF][S311] CRITICAL: VIP still unspawned; blocking ground retreat");
                    return;
                }
            }
            if(RHFRescuePlannerS41.Enabled)
                RHFPodPlannerS41.ResetAfterFallback(op);
            FallBackToLand(op, now, reason);
        }

        // Never depend on guessed private fueling-port fields. The engine's
        // own CompLaunchable.CanLaunch checks actual transporter group, roof,
        // mass, minimum fuel and cooldown. The launcher must be the actual
        // enemy-owned built structure; we never borrow a colonist launcher.
        private static bool NativeLaunchReadyS41(CompLaunchable launch,
            Thing expectedLauncher, Faction faction)
        {
            if (launch == null || expectedLauncher == null ||
                faction == null || faction.IsPlayer ||
                !expectedLauncher.Spawned || expectedLauncher.Faction != faction)
                return false;
            try { return launch.CanLaunch(); }
            catch(Exception e)
            {
                Log.Warning("[RHF][AirS41] Native CanLaunch rejected: " +
                    e.GetBaseException().Message);
                return false;
            }
        }

        // Do not seal the LAST route out of the launch room. The VIP may
        // fly, but guards still need a physically usable retreat if the
        // pod is damaged or after the VIP takes off.
        private bool NativeEscapeLaneS41(IntVec3 protectedCell,IntVec3 proposedWall)
        {
            int[] ox={1,-1,0,0,1,1,-1,-1};
            int[] oz={0,0,1,-1,1,-1,1,-1};
            for(int radius=6;radius<=22;radius+=4)
            for(int i=0;i<ox.Length;i++)
            {
                IntVec3 outside=protectedCell+new IntVec3(
                    ox[i]*radius,0,oz[i]*radius);
                if(!outside.InBounds(map) || !outside.Standable(map) ||
                   outside.Roofed(map) || outside.GetEdifice(map)!=null)continue;
                if(OpenRouteExcept(protectedCell,outside,proposedWall))return true;
            }
            return false;
        }

        // Native S4.1 fortification: enemy-owned BLUEPRINT -> FRAME -> WALL.
        // Exactly the construction pipeline used by LordToil_Siege. Never
        // spawn a completed wall, never spend steel by editing stack counts.
        private Room ProtectedPodRoomR041(RHFOperation op)
        {
            Room room=op.PodCell.GetRoom(map)??op.PodLauncherCell.GetRoom(map);
            if(room!=null)return room;
            foreach(IntVec3 dir in GenAdj.CardinalDirections)
            {
                IntVec3 c=op.PodCell+dir;
                if(c.InBounds(map) && c.Standable(map) && !NJRPodSiteClearS41.ReservedFootprint(op,c))
                {room=c.GetRoom(map);if(room!=null)return room;}
            }
            return null;
        }
        // Do not use the impassable built launcher/pod as path destinations.
        // A valid assembly square and supplied work pocket must stay connected.
        // External retreat may be temporarily sealed; reopen the own wall in phase 2.
        // R044: geometry and logistics are separate. A stack outside the
        // future pocket generates transport work, not a permanent rejection.
        private bool VipBoardSideSafeR051(RHFOperation op,Pawn vip,IntVec3 wall)
        {
            if(vip==null || !vip.Spawned || vip.Map!=map || vip.Position==wall)return false;
            // Conservative proof: already on a free boarding square, or a
            // short physically reachable adjacent square without crossing the wall.
            for(int d=0;d<4;d++)
            {
                IntVec3 c=RHFObjectiveRouteV128.CardinalR044(op.PodCell,d);
                if(c==wall || !c.InBounds(map) || !c.Standable(map) || NJRPodSiteClearS41.ReservedFootprint(op,c))continue;
                if(vip.Position==c)return true;
            }
            return false;
        }
        private bool CanSealR041(RHFOperation op,Pawn vip,IntVec3 wall,Room room,
            out HashSet<IntVec3> pocket)
        {
            pocket=null;
            var reserved=new HashSet<IntVec3>();
            ThingDef launcherDef=DefDatabase<ThingDef>.GetNamedSilentFail("PodLauncher");
            ThingDef podDef=DefDatabase<ThingDef>.GetNamedSilentFail("TransportPod");
            if(launcherDef==null || podDef==null || wall==vip.Position)return false;
            foreach(IntVec3 c in GenAdj.OccupiedRect(op.PodLauncherCell,NJRPodSiteClearS41.RotationFor(op.PodLauncherCell,op.PodCell),launcherDef.size))reserved.Add(c);
            foreach(IntVec3 c in GenAdj.OccupiedRect(op.PodCell,Rot4.North,podDef.size))reserved.Add(c);
            var anchors=new List<IntVec3>();
            for(int d=0;d<4;d++)anchors.Add(RHFObjectiveRouteV128.CardinalR044(op.PodCell,d));
            if(op.Phase==3 && VipBoardSideSafeR051(op,vip,wall))
                foreach(IntVec3 tile in GenAdj.OccupiedRect(op.PodLauncherCell,
                    NJRPodSiteClearS41.RotationFor(op.PodLauncherCell,op.PodCell),launcherDef.size))
                for(int d=0;d<4;d++)anchors.Add(RHFObjectiveRouteV128.CardinalR044(tile,d));
            foreach(IntVec3 stand in anchors)
            {
                if(!stand.InBounds(map) || stand==wall || !stand.Standable(map) ||
                   reserved.Contains(stand))continue;
                var seen=new HashSet<IntVec3>();var queue=new Queue<IntVec3>();
                seen.Add(stand);queue.Enqueue(stand);
                while(queue.Count>0)
                {
                    IntVec3 at=queue.Dequeue();
                    for(int direction=0;direction<4;direction++)
                    {
                        IntVec3 c=RHFObjectiveRouteV128.CardinalR044(at,direction);
                        if(!c.InBounds(map) || c==wall || reserved.Contains(c) || seen.Contains(c) ||
                           Math.Abs(c.x-stand.x)>24 || Math.Abs(c.z-stand.z)>24)continue;
                        if((!c.Walkable(map) && !NJRVipDebrisR045.ClearableCell(op,map,c)) || (c.GetEdifice(map) is Building_Door door && !door.Open))continue;
                        seen.Add(c);queue.Enqueue(c);
                    }
                }
                // Prospective geometry: VIP may still be in the original
                // prison on the other side of the corridor. Stage him FIRST.
                if(!seen.Contains(vip.Position) && !vip.CanReach(stand,PathEndMode.OnCell,Danger.Some) &&
                   !(op.Phase==1 && EarlySealSafeR051(op,vip,wall,stand)) && !VipBoardSideSafeR051(op,vip,wall))continue;
                if(op.Phase==1 && !EarlySealSafeR051(op,vip,wall,stand))continue;
                bool adjacentInside=false;
                for(int d2=0;d2<4;d2++)
                    if(seen.Contains(RHFObjectiveRouteV128.CardinalR044(wall,d2)))adjacentInside=true;
                if(!adjacentInside)continue;
                bool launcherWork=false;
                foreach(IntVec3 tile in GenAdj.OccupiedRect(op.PodLauncherCell,
                    NJRPodSiteClearS41.RotationFor(op.PodLauncherCell,op.PodCell),launcherDef.size))
                for(int d3=0;d3<4;d3++)
                    if(seen.Contains(RHFObjectiveRouteV128.CardinalR044(tile,d3)) && RHFObjectiveRouteV128.CardinalR044(tile,d3).Standable(map))launcherWork=true;
                if(!launcherWork)continue;
                int crew=0,builders=0;
                foreach(Pawn member in op.Squad)
                {
                    if(!NativeAliveS311(member,map))continue;
                    if(seen.Contains(member.Position) || member.CanReach(stand,PathEndMode.OnCell,Danger.Some))
                    {
                        crew++;
                        if(!member.WorkTypeIsDisabled(WorkTypeDefOf.Construction) &&
                           (member.skills?.GetSkill(SkillDefOf.Construction)?.Level??0)>=6)builders++;
                    }
                }
                if(crew<2 || builders<1)continue;
                // No dependence on Room identity of an impassable finished pad.
                // Require that the current primary engineer remains on this side.
                Pawn primary=op.BoardS41?.PodEngineerS41;
                if(NativeAliveS311(primary,map) && !seen.Contains(primary.Position) &&
                   !primary.CanReach(stand,PathEndMode.OnCell,Danger.Some))continue;
                pocket=seen;return true;
            }
            return false;
        }
        private bool SealLogisticsR044(RHFOperation op,IntVec3 wall,HashSet<IntVec3> pocket,
            Pawn engineer,Pawn refueler,out Pawn mover,out Thing cargo,out IntVec3 destination,bool early=false)
        {
            mover=null;cargo=null;destination=IntVec3.Invalid;
            // Hold the same hauling assignment across planning pulses, including
            // the interval when the stack is carried rather than spawned.
            foreach(Pawn running in op.Squad)
            {
                if(!NativeAliveS311(running,map) || running.jobs?.curJob?.def?.defName!="NJR_MoveSealCargoR044")continue;
                RHFOrderS41 previous=op.BoardS41?.For(running,false);
                mover=running;cargo=previous?.Objective??running.jobs.curJob.targetA.Thing;
                destination=running.jobs.curJob.targetB.Cell;
                return false;
            }
            // R046: a chunk on the planned wall is clearance work even if
            // it was never part of the mission cargo. Do this before testing
            // native standability or creating a wall blueprint.
            Thing debris=NJRVipDebrisR045.DebrisAt(map,wall);
            if(debris!=null)
            {
                cargo=debris;
                NJRVipDebrisR045.TryMove(op,map,debris,pocket,engineer,refueler,out mover,out destination);
                return false;
            }
            if(op.PodCargoStacksS41==null)return true;
            foreach(Pawn carrier in op.Squad)
                if(!early && NativeAliveS311(carrier,map) && !pocket.Contains(carrier.Position) &&
                   (RHFPodSupplyS41.HasCargo(op,carrier) || (carrier.carryTracker?.CarriedThing!=null &&
                    op.PodCargoStacksS41.Contains(carrier.carryTracker.CarriedThing))))return false;
            foreach(Thing stack in op.PodCargoStacksS41)
            {
                if(stack==null || stack.Destroyed || !stack.Spawned || stack.Map!=map ||
                   (pocket.Contains(stack.Position) && stack.Position!=wall))continue;
                int best=int.MaxValue;
                foreach(Pawn p in op.Squad)
                {
                    if(p==engineer || p==refueler || (early && (p==op.BoardS41?.Breacher || op.BoardS41?.For(p,false)?.Role==(int)RHFRoleS41.Breach || op.BoardS41?.For(p,false)?.Role==(int)RHFRoleS41.Escort)) || !NativeAliveS311(p,map) || p.InMentalState ||
                       p.Faction!=op.PodCargoOwnerFactionS311 || RHFPodSupplyS41.HasCargo(op,p) ||
                       p.WorkTypeIsDisabled(WorkTypeDefOf.Hauling) || RHFCombatPlannerV125.IsCombatEmergencyJob(p) ||
                       p.health?.capacities?.CapableOf(PawnCapacityDefOf.Manipulation)!=true ||
                       !p.CanReach(stack,PathEndMode.ClosestTouch,Danger.Some))continue;
                    foreach(IntVec3 cell in pocket)
                    {
                        if(cell==wall || !cell.Roofed(map) || cell.GetEdifice(map)!=null ||
                           cell.GetFirstPawn(map)!=null || NJRPodSiteClearS41.ReservedFootprint(op,cell) ||
                           (cell-op.PodLauncherCell).LengthHorizontalSquared>64 ||
                           (cell-wall).LengthHorizontalSquared<=1)continue;
                        bool occupied=false;
                        foreach(Thing item in map.thingGrid.ThingsListAt(cell))
                            if(item.def?.category==ThingCategory.Item){occupied=true;break;}
                        if(occupied)continue;
                        int score=(p.Position-stack.Position).LengthHorizontalSquared+
                            (stack.Position-cell).LengthHorizontalSquared*2+(cell-op.PodLauncherCell).LengthHorizontalSquared*4;
                        if(score>=best || !p.CanReach(cell,PathEndMode.OnCell,Danger.Some))continue;
                        best=score;mover=p;cargo=stack;destination=cell;
                    }
                }
                return false; // pending transfer, or safely wait if none executable
            }
            return true;
        }
        private Thing NativeFortificationWorkS41(RHFOperation op,
            Pawn vip,Faction owner,int now,Pawn engineer,Pawn refueler,
            out Pawn mover,out Thing cargo,out IntVec3 destination,bool early=false)
        {
            mover=null;cargo=null;destination=IntVec3.Invalid;
            if(op==null || vip==null || !vip.Spawned || owner==null || owner.IsPlayer ||
               !op.FortifyPrison || op.BarricadesBuilt>=op.SealLimitR052)return null;
            ThingDef requested=ThingDefOf.Wall;
            Room protectedRoom=ProtectedPodRoomR041(op);
            // A launch roof clearance must not revoke the previously chosen
            // enclosed site. The geometry below verifies structural shelter.
            if(!op.BarricadeCell.IsValid)
            {
                // Retry after cargo unloading, a door breach or room topology change.
                if(op.FortifyStartedTick>0 && now-op.FortifyStartedTick<150)return null;
                IntVec3 best=IntVec3.Invalid;ThingDef bestDef=null;int bestScore=int.MaxValue;int candidates=0,viable=0,noBenefit=0,noPocket=0,noWorker=0;
                var geometry=new SealGeometryR049(op,map);
                if(!geometry.HasShelteredSeed)
                {
                    op.FortifyStartedTick=now;
                    Log.Message("[NJR][R050][Fortify] no sheltered work anchor; seeds="+geometry.SeedCount+" exterior="+geometry.ExteriorCount);
                    return null;
                }
                for(int radius=1;radius<=12;radius++)
                for(int dx=-radius;dx<=radius;dx++)
                for(int dz=-radius;dz<=radius;dz++)
                {
                    if(Math.Max(Math.Abs(dx),Math.Abs(dz))!=radius)continue;
                    IntVec3 cell=op.PodLauncherCell+new IntVec3(dx,0,dz);
                    if(!NJRVipDebrisR045.ClearableCell(op,map,cell) ||
                       op.CompletedBarricades.Contains(cell) ||
                       NJRPodSiteClearS41.ReservedFootprint(op,cell))continue;
                    Pawn occupant=cell.GetFirstPawn(map);
                    if(occupant!=null && (occupant==vip || !op.Squad.Contains(occupant)))continue;
                    bool n=SolidFlankS41(cell+IntVec3.North),s=SolidFlankS41(cell+IntVec3.South);
                    bool e=SolidFlankS41(cell+IntVec3.East),w=SolidFlankS41(cell+IntVec3.West);
                    bool DoorAt(IntVec3 c)=>SealGeometryR049.DoorAt(map,c);
                    bool throat=(n&&(s||DoorAt(cell+IntVec3.South))) || (s&&DoorAt(cell+IntVec3.North)) ||
                                (e&&(w||DoorAt(cell+IntVec3.West))) || (w&&DoorAt(cell+IntVec3.East));
                    bool adjacentDoor=false;
                    for(int direction=0;direction<4;direction++)
                    {
                        IntVec3 near=RHFObjectiveRouteV128.CardinalR044(cell,direction);
                        if(DoorAt(near))
                        {
                            adjacentDoor=true; // R049: real gate adjacency is sufficient for geometric evaluation
                        }
                    }
                    if(!throat && !adjacentDoor)continue;
                    // The mission calls for an IMPASSABLE wall, not a shooting barricade.
                    // An adjacent door is a valid throat if the wall closes its approach.
                    candidates++;
                    int benefit=geometry.Benefit(cell);
                    if(benefit<=0){noBenefit++;continue;}
                    if(!CanSealR041(op,vip,cell,protectedRoom,out HashSet<IntVec3> candidatePocket))
                    {noPocket++;Log.Message("[NJR][R050][Fortify] useful but unsafe cell="+cell+" benefit="+benefit);continue;}
                    ThingDef material=ThingDefOf.Wall;
                    Pawn worker=early?engineer:NativeEngineerS311(op,cell,engineer,refueler,prepare:false);
                    if(worker==null || !worker.CanReach(cell,PathEndMode.Touch,Danger.Deadly))
                    {noWorker++;continue;}
                    viable++;
                    bool realGap=(n&&s)||(e&&w);
                    int score=-Math.Min(1000,benefit)*1000+(adjacentDoor?0:2000)+(realGap?0:throat?500:1000)+radius*20+
                        (vip.Position-cell).LengthHorizontalSquared;
                    if(score<bestScore){best=cell;bestDef=material;bestScore=score;}
                }
                op.FortifyStartedTick=now;
                if(!best.IsValid)
                {
                    Log.Message("[NJR][R050][Fortify] no geometric seal yet; candidates="+candidates+" viable="+viable+" noBenefit="+noBenefit+" noPocket="+noPocket+" noWorker="+noWorker+" seeds="+geometry.SeedCount+" exterior="+geometry.ExteriorCount);
                    return null;
                }
                op.BarricadeCell=best;requested=bestDef;
                Log.Message("[NJR][R050][Fortify] selected="+best+" score="+bestScore+" viable="+viable+" noBenefit="+noBenefit+" noPocket="+noPocket+" noWorker="+noWorker);
            }
            IntVec3 target=op.BarricadeCell;
            foreach(Thing item in map.thingGrid.ThingsListAt(target))
            {
                if(item==null || item.Destroyed)continue;
                if(item.def==ThingDefOf.Wall && item.Faction==owner)
                {
                    op.BarricadesBuilt++;op.CompletedBarricades.Add(target);
                    op.BarricadeCell=IntVec3.Invalid;op.FortifyStartedTick=0;
                    Log.Message("[NJR][R041][Fortify] completed "+item.def.defName+" "+target);
                    return NativeFortificationWorkS41(op,vip,owner,now,engineer,refueler,out mover,out cargo,out destination,early);
                }
                if((item is Blueprint_Build || item is Frame) && item.Faction==owner)
                {
                    if(!CanSealR041(op,vip,target,protectedRoom,out HashSet<IntVec3> readyPocket))return null;
                    RHFPodSupplyS41.RememberSealPocketR044(op,target,readyPocket);
                    if(!SealLogisticsR044(op,target,readyPocket,engineer,refueler,out mover,out cargo,out destination,early))return null;
                    if((!early && !readyPocket.Contains(vip.Position) && !VipBoardSideSafeR051(op,vip,target)) || (NativeAliveS311(engineer,map) && !readyPocket.Contains(engineer.Position)) ||
                       (NativeAliveS311(refueler,map) && !readyPocket.Contains(refueler.Position)))return null;
                    return item;
                }
            }
            if(!NJRVipDebrisR045.ClearableCell(op,map,target) || NJRPodSiteClearS41.ReservedFootprint(op,target))
            {op.BarricadeCell=IntVec3.Invalid;op.FortifyStartedTick=now;return null;}
            if(!CanSealR041(op,vip,target,protectedRoom,out HashSet<IntVec3> pocket) || new SealGeometryR049(op,map).Benefit(target)<=0)
            {
                Log.Message("[NJR][R050][Fortify] selected cell invalidated by pocket/topology: "+target);
                op.BarricadeCell=IntVec3.Invalid;op.FortifyStartedTick=now;return null;
            }
            RHFPodSupplyS41.RememberSealPocketR044(op,target,pocket);
            if(!SealLogisticsR044(op,target,pocket,engineer,refueler,out mover,out cargo,out destination,early))
            {
                if(now-op.FortifyStartedTick>=600 || op.FortifyStartedTick==now)
                {
                    Log.Message("[NJR][R044][Fortify] pending="+target+" cargo="+cargo+" mover="+mover+" destination="+destination);
                    op.FortifyStartedTick=now;
                }
                return null;
            }
            // R047: a prospective seal is not a permit to trap a VIP or an
            // active engineer. The published pocket drives their movement.
            if((!early && !pocket.Contains(vip.Position) && !VipBoardSideSafeR051(op,vip,target)) || (NativeAliveS311(engineer,map) && !pocket.Contains(engineer.Position)) ||
               (NativeAliveS311(refueler,map) && !pocket.Contains(refueler.Position)))return null;
            if(target.GetFirstPawn(map)!=null || (early ? !NativeAliveS311(engineer,map) || !engineer.CanReach(target,PathEndMode.Touch,Danger.Deadly) : NativeEngineerS311(op,target,engineer,refueler,prepare:false,pocket:pocket)==null))
            {
                if(now-op.FortifyStartedTick>=600){Log.Message("[NJR][R050][Fortify] awaiting empty target/inside builder: "+target);op.FortifyStartedTick=now;}
                return null;
            }
            try
            {
                Blueprint_Build plan=GenConstruct.PlaceBlueprintForBuild(
                    requested,target,map,Rot4.North,owner,op.SealStuffR052??ThingDefOf.Steel);
                if(plan!=null && plan.Faction==owner)
                {
                    Log.Message("[RHF S41][Fortify] Real enemy wall blueprint "+target+" def="+requested.defName+" stuff="+(op.SealStuffR052??ThingDefOf.Steel).defName+" phase="+op.Phase+" / op="+op.CreatedTick);
                    return plan;
                }
            }
            catch(Exception ex){Log.Warning("[RHF S41][Fortify] Blueprint failed: "+ex.Message);}
            op.BarricadeCell=IntVec3.Invalid;op.FortifyStartedTick=now;
            return null;
        }

        // A protected narrow corridor has solid opposite sides. Doors are
        // deliberately not counted as walls: a door can be opened in combat.
        private bool SolidFlankS41(IntVec3 cell)
        {
            if(!cell.InBounds(map))return false;
            Thing wall=cell.GetEdifice(map);
            return wall!=null && !(wall is Building_Door) && !cell.Standable(map);
        }

        // A freed captive does NOT normally own RHFRescueLordJobV124. The
        // mission ThinkTree is exclusive to rescuers; treating it as the VIP's
        // job source left the captive standing beside a ready pod indefinitely.
        // Only the liberated VIP is commanded here; soldiers remain ThinkTree-
        // owned. Check the ACTUAL transporter container before flight success.
        private void NativeVipMovementS41(RHFOperation op,Pawn vip,int now)
        {
            if(vip==null || !vip.Spawned || vip.Map!=map || vip.Downed ||
               vip.jobs==null || op?.Freed==null || !op.Freed.Contains(vip) ||
               vip.GetLord()?.LordJob is RHFRescueLordJobV124)return;
            if(op.PodConstructionStage==4)
            {
                ThingWithComps vehicle=op.PodVehicleThing as ThingWithComps;
                CompTransporter transport=vehicle?.GetComp<CompTransporter>();
                if(vehicle==null || !vehicle.Spawned || transport==null ||
                   transport.innerContainer.Contains(vip))return;
                if(!transport.LoadingInProgressOrReadyToLaunch)return;
                if(vip.jobs.curJob?.def!=JobDefOf.EnterTransporter)
                {
                    Job board=JobMaker.MakeJob(JobDefOf.EnterTransporter,vehicle);
                    vip.jobs.TryTakeOrderedJob(board);
                }
                return;
            }
            if(now%180>=90 || op.PodConstructionStage>3)return;
            if(op.FortifyPrison && op.BoardS41?.For(vip,false) is RHFOrderS41 planned &&
               planned.Role==(int)RHFRoleS41.PodAssembly && planned.Cell.IsValid && planned.Cell!=vip.Position &&
               vip.CanReach(planned.Cell,PathEndMode.OnCell,Danger.Some))
            {
                if(vip.jobs.curJob?.def!=JobDefOf.Goto || vip.jobs.curJob.targetA.Cell!=planned.Cell)
                    vip.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Goto,planned.Cell));
                return;
            }
            IntVec3 target=op.PodLauncherCell;
            if(!target.IsValid || !target.InBounds(map) ||
               (vip.Position-target).LengthHorizontalSquared<36)return;
            IntVec3 best=IntVec3.Invalid;int score=int.MaxValue;
            for(int dx=-4;dx<=4;dx++)for(int dz=-4;dz<=4;dz++)
            {
                IntVec3 at=target+new IntVec3(dx,0,dz);
                if(!at.InBounds(map) || !at.Standable(map) ||
                   at.GetEdifice(map)!=null ||
                   !vip.CanReach(at,PathEndMode.OnCell,Danger.Some))continue;
                int d=(at-vip.Position).LengthHorizontalSquared;
                if(d<score){best=at;score=d;}
            }
            if(best.IsValid && vip.jobs.curJob?.def!=JobDefOf.Goto)
                vip.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Goto,best));
        }

        // Bounded door-aware prospective route proof: the seal must not cut
        // off the captive or the currently assigned assault operator.
        private bool EarlySealSafeR051(RHFOperation op,Pawn vip,IntVec3 wall,IntVec3 stand)
        {
            var seen=new HashSet<IntVec3>{stand};var queue=new Queue<IntVec3>();queue.Enqueue(stand);
            while(queue.Count>0)
            {
                IntVec3 at=queue.Dequeue();
                for(int d=0;d<4;d++)
                {
                    IntVec3 c=RHFObjectiveRouteV128.CardinalR044(at,d);
                    if(c==wall || !c.InBounds(map) || Math.Abs(c.x-stand.x)>24 || Math.Abs(c.z-stand.z)>24 ||
                       NJRPodSiteClearS41.ReservedFootprint(op,c) || seen.Contains(c) ||
                       (!c.Walkable(map) && !SealGeometryR049.DoorAt(map,c) && !NJRVipDebrisR045.ClearableCell(op,map,c)))continue;
                    seen.Add(c);queue.Enqueue(c);
                }
            }
            if(!seen.Contains(vip.Position))return false;
            foreach(Pawn carrier in op.Squad)
                if(NativeAliveS311(carrier,map) && RHFPodSupplyS41.HasCargo(op,carrier) && !seen.Contains(carrier.Position))return false;
            Pawn breacher=op.BoardS41?.Breacher;
            return breacher!=null ? seen.Contains(breacher.Position) :
                op.Squad.Exists(p=>NativeAliveS311(p,map) && seen.Contains(p.Position));
        }

        private static bool NJRSealCargoPriorityR052(RHFOperation op,Pawn pawn)
        {
            if(pawn?.inventory?.innerContainer==null)return false;
            foreach(Thing stack in op.PodCargoStacksS41)
                if(stack!=null && !stack.Destroyed && stack.def==(op.SealStuffR052??ThingDefOf.Steel) && pawn.inventory.innerContainer.Contains(stack))return true;
            return false;
        }

        private static void AssignEarlyR052(RHFTacticalBoardS41 board,Pawn pawn,RHFRoleS41 role,Thing objective,IntVec3 cell,int now)
        {
            RHFOrderS41 old=board.For(pawn,false);
            bool changed=old==null || old.Role!=(int)role || old.Objective!=objective || old.Cell!=cell;
            board.Assign(pawn,role,objective,cell,now);
            Job current=pawn?.jobs?.curJob;
            // The ThinkTree remains the job writer. Release stale transit/wait
            // on a real role change, never reload/suppression or live combat.
            if(changed && current!=null && !RHFCombatPlannerV125.IsCombatEmergencyJob(pawn) &&
                (current.def==JobDefOf.Goto || current.def==JobDefOf.Wait || current.def.defName=="Wait_Wander"))
                pawn.jobs.EndCurrentJob(JobCondition.InterruptForced,false);
        }

        private void NativePrepareBeforeReleaseR051(RHFOperation op,int now)
        {
            if(op.Vip==null || !op.Vip.Spawned || op.Vip.Map!=map || !op.PodNativeS311 ||
               op.Squad==null || op.BoardS41==null || op.PodConstructionStage>=3)return;
            var board=op.BoardS41;
            if(op.Squad.FindAll(p=>NativeAliveS311(p,map)).Count<4)
            {board.PodEngineerS41=null;op.EarlyCarrierR052=null;return;}
            Pawn engineer=null;int best=int.MinValue;
            foreach(Pawn member in op.Squad)
            {
                var order=board.For(member,false);
                if(!NativeAliveS311(member,map) || member==board.Breacher || order==null ||
                   order.Role==(int)RHFRoleS41.Breach || order.Role==(int)RHFRoleS41.Escort ||
                   member.Faction!=op.PodCargoOwnerFactionS311 || member.WorkTypeIsDisabled(WorkTypeDefOf.Construction) ||
                   member.health?.capacities?.CapableOf(PawnCapacityDefOf.Manipulation)!=true ||
                   RHFCombatPlannerV125.IsCombatEmergencyJob(member))continue;
                if(op.PodLauncherCell.IsValid && !member.CanReach(op.PodLauncherCell,PathEndMode.Touch,Danger.Deadly))continue;
                int score=(member.skills?.GetSkill(SkillDefOf.Construction)?.Level??0)*100+(member==board.PodEngineerS41?10000:0);
                if(score>best){best=score;engineer=member;}
            }
            if(engineer==null || !NativePrepareEngineerS41(engineer)){board.PodEngineerS41=null;op.EarlyCarrierR052=null;return;}
            ThingDef launcher=DefDatabase<ThingDef>.GetNamedSilentFail("PodLauncher"),pod=DefDatabase<ThingDef>.GetNamedSilentFail("TransportPod");
            if(launcher==null || pod==null)return;
            if(op.PodConstructionStage==0)
            {
                if(op.EarlyPadProbeTickR052>=0 && now-op.EarlyPadProbeTickR052<150)return;
                op.EarlyPadProbeTickR052=now;
                if(!op.PodPrisonAnchorR043.IsValid)op.PodPrisonAnchorR043=op.Vip.Position;
                if(!NJRRescueAccessR040.TryEarlyPadR051(op,map,op.Vip,engineer,launcher,pod,out IntVec3 lc,out IntVec3 pc))
                {Log.Message("[NJR][R052][Early] no reachable separate sheltered pad; retry after assault advances");return;}
                op.PodLauncherCell=lc;op.PodCell=pc;op.PodConstructionStage=1;op.PodStageStartedTickS311=now;
                Log.Message("[NJR][R051][Early] pad reserved during assault "+lc+" "+pc);
            }
            board.PodEngineerS41=engineer;
            // Reserve at most engineer plus one carrier; the assault board's
            // breach operator and at least two other soldiers remain untouched.
            Pawn carrier=null;
            foreach(Pawn member in op.Squad)
            {
                var order=board.For(member,false);
                if(!NativeAliveS311(member,map) || member==board.Breacher || order==null ||
                   order.Role==(int)RHFRoleS41.Breach || order.Role==(int)RHFRoleS41.Escort ||
                   !RHFPodSupplyS41.HasCargo(op,member))continue;
                if(member==engineer)continue;
                if(carrier==null || (NJRSealCargoPriorityR052(op,member) && !NJRSealCargoPriorityR052(op,carrier)) ||
                   (NJRSealCargoPriorityR052(op,member)==NJRSealCargoPriorityR052(op,carrier) && member==op.EarlyCarrierR052))carrier=member;
            }
            op.EarlyCarrierR052=carrier;
            Thing site=null;Pawn mover=null;Thing cargo=null;IntVec3 destination=IntVec3.Invalid;
            if(op.PodConstructionStage==1 && op.BarricadesBuilt<op.SealLimitR052 && NativeWorkSiteS311(map,op.PodLauncherCell,op.PodCargoOwnerFactionS311)==null &&
               NativeAtSiteS311(map,op.PodLauncherCell,launcher,op.PodCargoOwnerFactionS311)==null)
            {
                site=NativeFortificationWorkS41(op,op.Vip,op.PodCargoOwnerFactionS311,now,engineer,null,out mover,out cargo,out destination,true);
                if(site!=null || (op.BarricadeCell.IsValid && now-op.PodStageStartedTickS311<Math.Min(6000,1200*op.SealLimitR052)) || now-op.PodStageStartedTickS311<150)
                {
                    if(mover!=null && mover!=board.Breacher && board.For(mover,false)?.Role!=(int)RHFRoleS41.Breach && cargo!=null && destination.IsValid)
                        {op.EarlyCarrierR052=mover;AssignEarlyR052(board,mover,RHFRoleS41.PodSupply,cargo,destination,now);}
                    if(RHFPodSupplyS41.HasCargo(op,engineer))
                    {IntVec3 unload=RHFPodSupplyS41.UnloadCell(engineer,op,map);if(unload.IsValid)AssignEarlyR052(board,engineer,RHFRoleS41.PodSupply,null,unload,now);}
                    else if(site!=null)AssignEarlyR052(board,engineer,RHFRoleS41.Engineer,site,site.Position,now);
                    else
                    {
                        var pocket=RHFPodSupplyS41.SealPocketR044(op);
                        IntVec3 stage=IntVec3.Invalid;int distance=int.MaxValue;
                        if(pocket!=null)foreach(IntVec3 c in pocket)
                        {
                            int cost=(c-op.PodLauncherCell).LengthHorizontalSquared;
                            if(cost>=distance || !c.Standable(map) || c==op.BarricadeCell ||
                               NJRPodSiteClearS41.ReservedFootprint(op,c) || !engineer.CanReach(c,PathEndMode.OnCell,Danger.Some))continue;
                            stage=c;distance=cost;
                        }
                        AssignEarlyR052(board,engineer,stage.IsValid?RHFRoleS41.Screen:RHFRoleS41.Hold,null,stage.IsValid?stage:engineer.Position,now);
                    }
                    if(carrier!=null && mover==null)
                    {IntVec3 unload=RHFPodSupplyS41.UnloadCell(carrier,op,map);if(unload.IsValid)AssignEarlyR052(board,carrier,RHFRoleS41.PodSupply,null,unload,now);}
                    return;
                }
            }
            bool first=op.PodConstructionStage==1;IntVec3 cell=first?op.PodLauncherCell:op.PodCell;
            ThingDef requested=first?launcher:pod;
            site=NativeAtSiteS311(map,cell,requested,op.PodCargoOwnerFactionS311);
            if(site!=null)
            {
                if(first)op.PodLauncherThing=site;else op.PodVehicleThing=site;
                op.PodConstructionStage++;op.PodStageStartedTickS311=now;
                Log.Message("[NJR][R052][Early] completed "+requested.defName+" while phase="+op.Phase);
                if(op.PodConstructionStage>=3){board.PodEngineerS41=null;op.EarlyCarrierR052=null;board.NextReplanTick=0;}
                return;
            }
            site=NativeWorkSiteS311(map,cell,op.PodCargoOwnerFactionS311);
            if(site==null)
            {
                Rot4 rotation=first?NJRPodSiteClearS41.RotationFor(op.PodLauncherCell,op.PodCell):Rot4.North;
                if(!FieldFootprintFree(cell,requested,op.Vip,rotation))return;
                try{site=GenConstruct.PlaceBlueprintForBuild(requested,cell,map,rotation,op.PodCargoOwnerFactionS311,null);}
                catch(Exception e){Log.Warning("[NJR][R051][Early] blueprint rejected: "+e.Message);return;}
            }
            if(RHFPodSupplyS41.HasCargo(op,engineer))
            {IntVec3 unload=RHFPodSupplyS41.UnloadCell(engineer,op,map);if(unload.IsValid)AssignEarlyR052(board,engineer,RHFRoleS41.PodSupply,null,unload,now);}
            else if(site!=null)AssignEarlyR052(board,engineer,RHFRoleS41.Engineer,site,cell,now);
            if(carrier!=null)
            {IntVec3 unload=RHFPodSupplyS41.UnloadCell(carrier,op,map);if(unload.IsValid)AssignEarlyR052(board,carrier,RHFRoleS41.PodSupply,null,unload,now);}
        }

        private void BuildAndLaunchNativeS311(RHFOperation op, int now)
        {
            RHFStrikeParty owner = NativeRescueOwnerS311(op);
            Pawn vip = op?.Vip;
            if(owner!=null && (owner.Faction==null || owner.Faction==Faction.OfPlayer))
            { NativeFallBackS311(op,now,"INVALID: player-owned rescue construction faction");return; }
            if (owner == null || !op.TryPod || !op.PodBudgetPaid ||
                vip == null || vip.Dead || vip.Downed ||
                (vip.Map != map && vip.Spawned))
            { NativeFallBackS311(op, now, "drop owner, funded kit or viable VIP missing"); return; }
            if (!op.PodSuppliesIssued)
            {
                NativeIssueCargoOnArrivalS311(op, now);
                if (!op.PodSuppliesIssued && now - op.PhaseStartTick > 4800)
                    NativeFallBackS311(op, now, "drop cargo never arrived");
                return;
            }
            if (!op.PodNativeS311 || op.PodCargoOwnerFactionS311 != owner.Faction)
            { NativeFallBackS311(op, now, "legacy or foreign cargo ownership"); return; }
            ThingDef launcherDef = DefDatabase<ThingDef>.GetNamedSilentFail("PodLauncher");
            ThingDef podDef = DefDatabase<ThingDef>.GetNamedSilentFail("TransportPod");
            if (launcherDef == null || podDef == null)
            { NativeFallBackS311(op, now, "native definitions vanished"); return; }
            if (op.PodConstructionStage == 0)
            {
                if (vip.Spawned && op.Freed != null && op.Freed.Contains(vip))
                    RHFRescueCommandV124.StabilizeFreed(op, vip);
                if (!vip.Spawned || vip.Map != map)
                { NativeFallBackS311(op, now, "VIP absent at site search"); return; }
                if(!op.PodPrisonAnchorR043.IsValid)op.PodPrisonAnchorR043=vip.Position;
                if(NJRRescueAccessR040.ContinuePodAccess(op,map,vip,now))return;
                if(op.FortifyPrison && NJRVipDebrisR045.Continue(op,map,now))return;
                if(op.PodStageStartedTickS311>=op.PhaseStartTick && now-op.PodStageStartedTickS311<450 && now-op.PhaseStartTick>90)
                {RHFPodPlannerS41.Plan(op,map,null,null,now);return;}
                op.PodStageStartedTickS311=now;
                if(NJRRescueAccessR040.TryPodAccess(op,map,vip,launcherDef,podDef,now,
                    out IntVec3 launchCell,out IntVec3 podCell))return;
                if(op.FortifyPrison && (!launchCell.IsValid || !podCell.IsValid ||
                    !NJRRescueInteriorS41.ShelteredPadR043(map,launchCell,podCell)))
                {
                    // Even when the strict parallel-work search finds a lawn, try the
                    // original native direct search INDOORS before accepting it.
                    if(FindFieldConstructionSite(vip,launcherDef,podDef,out IntVec3 indoorLaunch,
                        out IntVec3 indoorPod,true,op.PodPrisonAnchorR043))
                    {launchCell=indoorLaunch;podCell=indoorPod;}
                    else if(op.FortifyPrison && now-op.PhaseStartTick<6000 &&
                        NJRVipDebrisR045.TryStart(op,map,vip,now))return;
                    else if(now-op.PhaseStartTick<1800)
                    {
                        RHFPodPlannerS41.Plan(op,map,null,null,now);
                        Log.Message("[NJR][R043][PodSite] no executable sheltered pad; retry before outdoor fallback");
                        return;
                    }
                }
                if(!launchCell.IsValid || !podCell.IsValid)
                {
                    // R040 completed a real launch using the independent native
                    // direct-site search. Never discard it because the stricter
                    // R041 parallel-work layout search found zero candidates.
                    bool direct=FindFieldConstructionSite(vip,launcherDef,podDef,
                        out launchCell,out podCell);
                    if(!direct || launchCell.GetRoom(map)?.PsychologicallyOutdoors==true)
                    {
                        if(NJRRescueAccessR040.TryPodFallbackR042(op,map,vip,launcherDef,podDef,now,direct))return;
                    }
                    if(!direct)
                    {
                        if(now-op.PhaseStartTick<1800)
                        {
                            RHFPodPlannerS41.Plan(op,map,null,null,now);
                            Log.Message("[NJR][R042][PodSite] retry before land fallback");
                            return;
                        }
                        NativeFallBackS311(op, now, "no physical build site or recoverable access route after retry");return;
                    }
                    Log.Message("[NJR][R042][PodSite] verified R040 native direct fallback "+launchCell+" "+podCell);
                }
                NJRRescueAccessR040.Clear(op);
                op.PodLauncherCell = launchCell;
                op.PodCell = podCell;
                op.PodConstructionStage = 1;
                op.PodStageStartedTickS311 = now;
                op.PhaseStartTick = now;
                Log.Message("[RHF][S311] Native blueprint site=" + launchCell + ", pod=" + podCell+
                    "; R043 sheltered="+NJRRescueInteriorS41.ShelteredPadR043(map,launchCell,podCell)+
                    " tier="+NJRRescueInteriorS41.PodTierR043(map,op.PodPrisonAnchorR043,launchCell,podCell));
                return;
            }
            if (!op.PodLauncherCell.IsValid || !op.PodCell.IsValid ||
                !op.PodLauncherCell.InBounds(map) || !op.PodCell.InBounds(map))
            { NativeFallBackS311(op, now, "site invalidated"); return; }
            IntVec3 siteCell = op.PodConstructionStage == 2 ? op.PodCell : op.PodLauncherCell;
            NativeVipMovementS41(op,vip,now);
            // S4.1: stable engineer assignment avoids 90-tick ownership thrash.
            Pawn engineer = RHFRescuePlannerS41.Enabled &&
                RHFPodPlannerS41.PinnedEngineerValid(op,map,siteCell) ?
                    op.BoardS41.PodEngineerS41 : NativeEngineerS311(op,siteCell);
            if(RHFRescuePlannerS41.Enabled)
            {
                Thing currentSite=NativeWorkSiteS311(map,siteCell,owner.Faction) ??
                    (op.PodConstructionStage==3 ? op.PodLauncherThing : null);
                if(op.PodConstructionStage<=3 && currentSite!=null &&
                   now%360<90 &&
                   !(engineer?.jobs?.curJob?.targetA.Thing==currentSite &&
                     (engineer.jobs.curJob.def?.defName??"").Contains("Construct")) &&
                   RHFPodPlannerS41.WorkJob(engineer,currentSite)==null)
                {
                    // A pawn may be physically reachable yet unable to get
                    // a construction/hauling job. Try another qualified real
                    // soldier instead of waiting 15000 ticks with an idle blueprint.
                    foreach(Pawn available in op.Squad)
                    {
                        if(available==null || available==engineer ||
                           available.Faction!=owner.Faction ||
                           !NativeAliveS311(available,map) ||
                           available.WorkTypeIsDisabled(WorkTypeDefOf.Construction) ||
                           !available.CanReach(siteCell,PathEndMode.Touch,Danger.Deadly))continue;
                        if(!NativePrepareEngineerS41(available))continue;
                        if(RHFPodPlannerS41.WorkJob(available,currentSite)==null)continue;
                        engineer=available;
                        break;
                    }
                }
                RHFPodPlannerS41.RememberEngineer(op,engineer);
            }
            NativeDefendSiteS311(op, engineer, now);
            Thing parallelWall=null;
            Pawn fortifier=null;
            Pawn refueler=null;
            Pawn sealMover=null;Thing sealCargo=null;IntVec3 sealDestination=IntVec3.Invalid;
            // The moment the launcher exists, give a DISTINCT hauler the fuel
            // job while the primary engineer constructs the pod. Stage 3 is
            // a readiness check, not the first opportunity to refuel.
            if(op.PodConstructionStage==2 && op.PodLauncherThing!=null &&
               (op.PodLauncherThing as ThingWithComps)?.GetComp<CompRefuelable>()?.IsFull==false)
                refueler=NativeRefuelerR041(op,engineer);
            if(op.PodConstructionStage==3)
            {
                Pawn dedicated=NativeRefuelerR041(op,null);
                if(dedicated!=null)engineer=dedicated;
            }
            int active=0;
            foreach(Pawn candidate in op.Squad)if(NativeAliveS311(candidate,map))active++;
            // Keep a guard, a builder, and if needed the distinct fuel carrier.
            Thing footprintDebris=NJRVipDebrisR045.FootprintDebris(op,map);
            // Preserve a live physical move even after picking up the last
            // blocker (it is no longer present in the source thing grid).
            if(op.FortifyPrison && op.PodConstructionStage>=1 && op.PodConstructionStage<=4)
            foreach(Pawn running in op.Squad)
            {
                if(!NativeAliveS311(running,map) || running.jobs?.curJob?.def?.defName!="NJR_MoveSealCargoR044")continue;
                sealMover=running;sealCargo=op.BoardS41?.For(running,false)?.Objective??running.jobs.curJob.targetA.Thing;
                sealDestination=running.jobs.curJob.targetB.Cell;break;
            }
            if(footprintDebris!=null && sealMover==null && active>=(refueler==null?3:4))
            {
                sealCargo=footprintDebris;
                NJRVipDebrisR045.TryMove(op,map,footprintDebris,RHFPodSupplyS41.SealPocketR044(op),engineer,refueler,out sealMover,out sealDestination);
            }
            if(footprintDebris==null && sealMover==null && RHFRescuePlannerS41.Enabled && op.PodConstructionStage>=1 &&
               op.PodConstructionStage<=4 && op.FortifyPrison && op.BarricadesBuilt<op.SealLimitR052 &&
               engineer!=null && active>=(refueler==null?3:4))
            {
                parallelWall=NativeFortificationWorkS41(op,vip,owner.Faction,now,engineer,refueler,out sealMover,out sealCargo,out sealDestination);
                if(parallelWall!=null)
                {
                    fortifier=NativeEngineerS311(op,parallelWall.Position,engineer,refueler,pocket:RHFPodSupplyS41.SealPocketR044(op));
                    if(fortifier!=null && RHFPodSupplyS41.HasCargo(op,fortifier))fortifier=null;
                }
            }
            if(op.FortifyPrison && op.PodConstructionStage==1 && op.BarricadesBuilt<op.SealLimitR052 &&
               engineer!=null && NativeWorkSiteS311(map,siteCell,owner.Faction)==null &&
               (parallelWall!=null || op.BarricadeCell.IsValid) && now-op.PodStageStartedTickS311<Math.Min(6000,1200*op.SealLimitR052))
            {
                // First finish the seal, then construct the launcher. Cargo
                // and safe staging remain active, not a blanket Wait order.
                RHFPodPlannerS41.Plan(op,map,engineer,null,now,fortifier,parallelWall,
                    refueler,op.PodLauncherThing,sealMover,sealCargo,sealDestination);
                return;
            }
            if(RHFRescuePlannerS41.Enabled)
                RHFPodPlannerS41.Plan(op,map,engineer,
                    op.PodConstructionStage==4 ? op.PodVehicleThing :
                    NativeWorkSiteS311(map,siteCell,owner.Faction) ??
                    (op.PodConstructionStage==3 ? op.PodLauncherThing : null),
                    now,fortifier,parallelWall,refueler,op.PodLauncherThing,sealMover,sealCargo,sealDestination);
            // Unfinished optional protection can NEVER delay fueling or boarding.
            if (op.PodConstructionStage <= 3 && engineer == null)
            {
                if (now - op.PodStageStartedTickS311 > NativeVipWorkTimeoutS311)
                    NativeFallBackS311(op, now, "no qualified reachable enemy engineer");
                return;
            }
            if (op.PodConstructionStage == 1 || op.PodConstructionStage == 2)
            {
                bool first = op.PodConstructionStage == 1;
                ThingDef requested = first ? launcherDef : podDef;
                IntVec3 cell = first ? op.PodLauncherCell : op.PodCell;
                Thing done = NativeAtSiteS311(map, cell, requested, owner.Faction);
                if (done != null)
                {
                    if (first) op.PodLauncherThing = done;
                    else op.PodVehicleThing = done;
                    op.PodConstructionStage++;
                    op.PodStageStartedTickS311 = now;
                    return;
                }
                Thing site = NativeWorkSiteS311(map, cell, owner.Faction);
                if(footprintDebris!=null && now-op.PodStageStartedTickS311<=4500)
                {
                    if(now%900<90)Log.Message("[NJR][R046] footprint clearance pending: "+footprintDebris+" mover="+sealMover);
                    return;
                }
                if(footprintDebris!=null)
                {NativeFallBackS311(op,now,"footprint debris could not be physically relocated");return;}
                if (site == null)
                {
                    if (!FieldFootprintFree(cell, requested, vip,first ?
                        NJRPodSiteClearS41.RotationFor(op.PodLauncherCell,op.PodCell) : Rot4.North))
                    {
                        // The chosen site must remain clear. Never destroy
                        // unrelated colony walls/furniture because our
                        // landing/engineering planner picked a bad footprint.
                        NativeFallBackS311(op, now, "building footprint obstructed");
                        return;
                    }
                    try
                    {
                        site = GenConstruct.PlaceBlueprintForBuild(requested, cell,
                            map, first ? NJRPodSiteClearS41.RotationFor(op.PodLauncherCell,op.PodCell) : Rot4.North, owner.Faction, null);
                    }
                    catch (Exception e)
                    { NativeFallBackS311(op, now, "native blueprint failed: " + e.Message); return; }
                    if (site == null)
                    { NativeFallBackS311(op, now, "native blueprint unavailable"); return; }
                    Log.Message("[RHF][AirS41] Blueprint generated: faction=" +
                        site.Faction + ", intended=" + owner.Faction +
                        ", engineer=" + (engineer?.Faction?.Name ?? "none"));
                }
                if(site.Faction!=owner.Faction || engineer.Faction!=owner.Faction)
                {
                    Log.Error("[RHF][AirS41] Construction faction mismatch: site=" +
                        site.Faction + ", engineer=" + engineer.Faction + ", operator=" +
                        owner.Faction + "; never commandeering player blueprints");
                    NativeFallBackS311(op,now,"construction site belongs to another faction");
                    return;
                }
                if (now - op.PodStageStartedTickS311 > NativeVipWorkTimeoutS311)
                { NativeFallBackS311(op, now, "native construction could not finish"); return; }
                NativeConstructionOrderS311(engineer, site);
                if(RHFRescuePlannerS41.Enabled && now%900<90 &&
                   RHFPodPlannerS41.WorkJob(engineer,site)==null)
                {
                    bool stillCarrying=false;
                    foreach(Pawn soldier in op.Squad)
                        if(soldier!=null && RHFPodSupplyS41.HasCargo(op,soldier))
                        {stillCarrying=true;break;}
                    string job=engineer.jobs?.curJob?.def?.defName ?? "none";
                    Log.Warning("[NJR][VIP construction] WorkGiver unavailable: stage="+
                        op.PodConstructionStage+" site="+site.def.defName+
                        " worker="+engineer.LabelShortCap+" job="+job+
                        " cargoStillCarried="+stillCarrying);
                    // Missing construction jobs are not an excuse to trap
                    // the freed VIP forever. Try normal ground extraction
                    // after bounded recovery, without pretending the pod flew.
                    if(!stillCarrying && !job.Contains("Construct") &&
                       now-op.PodStageStartedTickS311>4500)
                    {
                        NativeFallBackS311(op,now,"no physical blueprint/frame job after 4500 ticks");
                        return;
                    }
                }
                return;
            }
            if (op.PodConstructionStage == 3)
            {
                ThingWithComps launchThing = op.PodLauncherThing as ThingWithComps;
                CompRefuelable fuel = launchThing?.GetComp<CompRefuelable>();
                if (fuel == null || !launchThing.Spawned || launchThing.Faction != owner.Faction)
                { NativeFallBackS311(op, now, "launcher absent or lacks native fuel comp"); return; }
                if (fuel.HasFuel)
                {
                    op.PodConstructionStage = 4;
                    op.PodStageStartedTickS311 = now;
                    return;
                }
                if (now - op.PodStageStartedTickS311 > NativeVipWorkTimeoutS311)
                { NativeFallBackS311(op, now, "native refueling did not complete"); return; }
                if(!RHFRescuePlannerS41.Enabled)
                    NativeJobFromWorkGiverS311(engineer, launchThing, "Refuel");
                return;
            }
            if (op.PodConstructionStage == 4)
            {
                ThingWithComps vehicle = op.PodVehicleThing as ThingWithComps;
                CompTransporter comp = vehicle?.GetComp<CompTransporter>();
                CompLaunchable launch = vehicle?.GetComp<CompLaunchable>();
                if (comp == null || launch == null || !vehicle.Spawned ||
                    vehicle.Faction != owner.Faction)
                { NativeFallBackS311(op, now, "vanilla transporter/launchable missing"); return; }
                // Vanilla needs a real transporter group before CanLaunch()
                // is meaningful. Verify AFTER grouping rather than checking
                // an undocumented reflection-only fuel link.
                if (!comp.LoadingInProgressOrReadyToLaunch)
                    comp.groupID = Find.UniqueIDsManager.GetNextTransporterGroupID();
                if (!NativeLaunchReadyS41(launch,op.PodLauncherThing,owner.Faction))
                {
                    if(now-op.PodStageStartedTickS311>NativeVipBoardTimeoutS311)
                        NativeFallBackS311(op,now,"native launcher cannot launch after ready timeout");
                    return;
                }
                if (vip.Spawned)
                {
                    if (now - op.PodStageStartedTickS311 > NativeVipBoardTimeoutS311)
                    { NativeFallBackS311(op, now, "VIP failed to board real pod"); return; }
                    if(!RHFRescuePlannerS41.Enabled &&
                       vip.jobs?.curJob?.def != JobDefOf.EnterTransporter)
                        vip.jobs?.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.EnterTransporter, vehicle));
                    return;
                }
                if (comp.innerContainer.Contains(vip))
                {
                    op.PodConstructionStage = 5;
                    op.PodReady = true;
                    op.PodStageStartedTickS311 = now;
                    return;
                }
                NativeFallBackS311(op, now, "VIP not in transporter container");
                return;
            }
            if (op.PodConstructionStage == 6)
            {
                // Successful departure is signaled ONLY by the actual
                // FlyShipLeaving world handoff, never by stage number.
                if(op.PodLaunchCompleted)return;
                if(RHFEnemyPodLaunchS41.Reconcile(op,map))return;
                if(now-op.PodStageStartedTickS311>2400)
                    Log.ErrorOnce("[RHF S41] Flight transfer did not complete: " +
                        op.CreatedTick,op.CreatedTick ^ 417611);
                return;
            }
            if (op.PodConstructionStage == 5)
            {
                // S4.1-A/F: physically fire the built and fueled native transporter.
                // The vanilla skyfaller MUST become a real world traveler
                // carrying THIS VIP before PodLaunchCompleted is credited.
                if (RHFRescuePlannerS41.Enabled)
                {
                    if(RHFEnemyPodLaunchS41.TryLaunch(op,map,now) ||
                       RHFEnemyPodLaunchS41.DepartureInFlight(op,map))
                        op.PodConstructionStage=6;
                    else if(op.PodVehicleThing != null && op.PodVehicleThing.Spawned)
                        NativeFallBackS311(op,now,"hostile flight validation rejected launch");
                    return;
                }
                NativeFallBackS311(op, now, "native hostile pod launch not yet supported (S3.2)");
            }
        }
    }
}
