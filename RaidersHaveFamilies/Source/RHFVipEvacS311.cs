// RHF S3.1.1 — native construction prototype, fail-closed extraction.
// This is a PARTIAL implementation of S3, not a confirmed successful pod flight.
// State and physical stock refs live on RHFOperation (Scribe in RHFOperations.cs).
using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

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

        private Pawn NativeEngineerS311(RHFOperation op, IntVec3 cell)
        {
            RHFStrikeParty party = NativeRescueOwnerS311(op);
            if (party == null || op.Squad == null) return null;
            Pawn best = null;
            int bestDistance = int.MaxValue;
            for (int i = 0; i < op.Squad.Count && i < 96; i++)
            {
                Pawn p = op.Squad[i];
                if (!NativeAliveS311(p, map) || p.Faction != party.Faction ||
                    p.health?.capacities == null ||
                    !p.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation) ||
                    !p.CanReach(cell, PathEndMode.Touch, Danger.Deadly)) continue;
                if (p.WorkTypeIsDisabled(WorkTypeDefOf.Construction)) continue;
                int dist = (p.Position - cell).LengthHorizontalSquared;
                if (dist >= bestDistance) continue;
                bestDistance = dist;
                best = p;
            }
            if (best == null) return null;
            // Enemy pawns generally have no enabled WorkSettings; WorkGivers
            // must also be called with forced=true and their actual Job verified.
            try
            {
                best.workSettings?.EnableAndInitializeIfNotAlreadyInitialized();
                if (best.workSettings != null)
                {
                    best.workSettings.SetPriority(WorkTypeDefOf.Construction, 3);
                    if (!best.WorkTypeIsDisabled(WorkTypeDefOf.Hauling))
                        best.workSettings.SetPriority(WorkTypeDefOf.Hauling, 3);
                }
            }
            catch (Exception e)
            {
                Log.Warning("[RHF][S311] Cannot initialize enemy engineer: " + e.Message);
                return null;
            }
            return best;
        }

        private IntVec3 NativeCargoCellS311(IntVec3 near, HashSet<IntVec3> used)
        {
            for (int radius = 1; radius <= 7; radius++)
            for (int dx = -radius; dx <= radius; dx++)
            for (int dz = -radius; dz <= radius; dz++)
            {
                if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != radius) continue;
                IntVec3 cell = near + new IntVec3(dx, 0, dz);
                if (used.Contains(cell) || !cell.InBounds(map) || !cell.Standable(map) ||
                    cell.GetEdifice(map) != null || cell.GetFirstBuilding(map) != null ||
                    cell.GetFirstPawn(map) != null) continue;
                bool occupied = false;
                foreach (Thing thing in map.thingGrid.ThingsListAt(cell))
                    if (thing.def.category == ThingCategory.Item) { occupied = true; break; }
                if (!occupied) { used.Add(cell); return cell; }
            }
            return IntVec3.Invalid;
        }

        private static void NativeAddStacksS311(List<Thing> destination, ThingDef def, int total)
        {
            if (total < 0 || def == null || def.stackLimit <= 0)
                throw new InvalidOperationException("Invalid supply quantity or stack definition");
            while (total > 0)
            {
                Thing thing = ThingMaker.MakeThing(def);
                int count = Math.Min(total, def.stackLimit);
                thing.stackCount = count;
                destination.Add(thing);
                total -= count;
                if (destination.Count > 16)
                    throw new InvalidOperationException("Unsupported modded supply stack count");
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
                FieldCost(pod, ThingDefOf.Steel) + (op.FortifyPrison ? 20 : 0);
            int components = FieldCost(launcher, ThingDefOf.ComponentIndustrial) +
                FieldCost(pod, ThingDefOf.ComponentIndustrial);
            if (launcher == null || pod == null || steel <= 0 || components <= 0 || fuel <= 0)
            { NativeCancelAirBeforeExtractionS311(op, "native defs/fuel missing"); return; }
            if (steel > 2 * ThingDefOf.Steel.stackLimit ||
                components > ThingDefOf.ComponentIndustrial.stackLimit ||
                fuel > 16 * ThingDefOf.Chemfuel.stackLimit)
            { NativeCancelAirBeforeExtractionS311(op, "modded supply exceeds tracked stack capacity"); return; }

            var supplied = new List<Thing>();
            var used = new HashSet<IntVec3>();
            var places = new List<IntVec3>();
            try
            {
                if (steel > 2 * ThingDefOf.Steel.stackLimit)
                    throw new InvalidOperationException("steel exceeds two tracked military stacks");
                NativeAddStacksS311(supplied, ThingDefOf.Steel, steel);
                NativeAddStacksS311(supplied, ThingDefOf.ComponentIndustrial, components);
                NativeAddStacksS311(supplied, ThingDefOf.Chemfuel, fuel);
                foreach (Thing stack in supplied)
                {
                    IntVec3 position = NativeCargoCellS311(owner.Position, used);
                    if (!position.IsValid)
                        throw new InvalidOperationException("no distinct physical cargo landing cells");
                    places.Add(position);
                }
                // Latch BEFORE first spawn: partial failure is a failure, not a
                // reason to conjure a second complete kit on the next tick.
                op.PodSuppliesIssued = true;
                op.PodNativeS311 = true;
                op.PodCargoOwnerFactionS311 = party.Faction;
                op.PodFuelStacksS311.Clear();
                int index = 0;
                for (int i = 0; i < supplied.Count; i++)
                {
                    Thing stack = supplied[i];
                    Thing spawned = GenSpawn.Spawn(stack, places[i], map);
                    // Prevent colonist AI hauling foreign military supplies away.
                    // The explicitly FORCED enemy WorkGiver jobs must still be
                    // verified; a forbidden stack is never assumed usable.
                    spawned.SetForbidden(true, false);
                    if (stack.def == ThingDefOf.Steel)
                    {
                        if (index++ == 0) op.PodSteel = spawned;
                        else op.PodSteelExtra = spawned;
                    }
                    else if (stack.def == ThingDefOf.ComponentIndustrial)
                        op.PodComponents = spawned;
                    else if (stack.def == ThingDefOf.Chemfuel)
                        op.PodFuelStacksS311.Add(spawned);
                }
                op.PodStageStartedTickS311 = now;
                Log.Message("[RHF][S311] Physical drop-rescue cargo: owner=" + party.Faction +
                    ", steel=" + steel + ", components=" + components + ", fuel=" + fuel);
            }
            catch (Exception e)
            {
                // Items already spawned remain real loot, not refunded or replaced.
                op.PodSuppliesIssued = true;
                op.PodNativeS311 = true;
                Log.Warning("[RHF][S311] Incomplete physical drop cargo: " + e);
                NativeCancelAirBeforeExtractionS311(op, "physical arrival kit incomplete");
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
            FallBackToLand(op, now, reason);
        }

        private void BuildAndLaunchNativeS311(RHFOperation op, int now)
        {
            RHFStrikeParty owner = NativeRescueOwnerS311(op);
            Pawn vip = op?.Vip;
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
                if (!vip.Spawned || vip.Map != map ||
                    !FindFieldConstructionSite(vip, launcherDef, podDef,
                        out IntVec3 launchCell, out IntVec3 podCell))
                { NativeFallBackS311(op, now, "no reachable safe outdoor build site"); return; }
                op.PodLauncherCell = launchCell;
                op.PodCell = podCell;
                op.PodConstructionStage = 1;
                op.PodStageStartedTickS311 = now;
                op.PhaseStartTick = now;
                Log.Message("[RHF][S311] Native blueprint site=" + launchCell + ", pod=" + podCell);
                return;
            }
            if (!op.PodLauncherCell.IsValid || !op.PodCell.IsValid ||
                !op.PodLauncherCell.InBounds(map) || !op.PodCell.InBounds(map))
            { NativeFallBackS311(op, now, "site invalidated"); return; }
            IntVec3 siteCell = op.PodConstructionStage == 2 ? op.PodCell : op.PodLauncherCell;
            Pawn engineer = NativeEngineerS311(op, siteCell);
            NativeDefendSiteS311(op, engineer, now);
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
                if (site == null)
                {
                    if (!FieldFootprintFree(cell, requested, vip))
                    { NativeFallBackS311(op, now, "building footprint obstructed"); return; }
                    try
                    {
                        site = GenConstruct.PlaceBlueprintForBuild(requested, cell,
                            map, Rot4.North, owner.Faction, null);
                    }
                    catch (Exception e)
                    { NativeFallBackS311(op, now, "native blueprint failed: " + e.Message); return; }
                    if (site == null)
                    { NativeFallBackS311(op, now, "native blueprint unavailable"); return; }
                }
                if (now - op.PodStageStartedTickS311 > NativeVipWorkTimeoutS311)
                { NativeFallBackS311(op, now, "native construction could not finish"); return; }
                NativeConstructionOrderS311(engineer, site);
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
                if (!launch.ConnectedToFuelingPort || !launch.FuelingPortSourceHasAnyFuel)
                { NativeFallBackS311(op, now, "launcher not connected or fueled"); return; }
                // No non-existent InitiateLoad API: vanilla initializes its
                // groupID through UniqueIDsManager. Other factions may board.
                if (!comp.LoadingInProgressOrReadyToLaunch)
                    comp.groupID = Find.UniqueIDsManager.GetNextTransporterGroupID();
                if (vip.Spawned)
                {
                    if (now - op.PodStageStartedTickS311 > NativeVipBoardTimeoutS311)
                    { NativeFallBackS311(op, now, "VIP failed to board real pod"); return; }
                    if (vip.jobs?.curJob?.def != JobDefOf.EnterTransporter)
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
            if (op.PodConstructionStage == 5)
            {
                // S3.1.1 has no validated hostile-faction WORLD arrival action.
                // A fake Skyfaller or disappearance is strictly prohibited.
                // Return the SAME VIP to the map before ground-exit fallback.
                NativeFallBackS311(op, now, "native hostile pod launch not yet supported (S3.2)");
            }
        }
    }
}
