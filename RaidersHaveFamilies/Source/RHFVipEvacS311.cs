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

        private Pawn NativeEngineerS311(RHFOperation op, IntVec3 cell, Pawn excluded=null, Pawn excluded2=null,bool prepare=true)
        {
            RHFStrikeParty party = NativeRescueOwnerS311(op);
            if (party == null || op.Squad == null) return null;
            Pawn best = null;
            int bestDistance = int.MaxValue;
            for (int i = 0; i < op.Squad.Count; i++)
            {
                Pawn p = op.Squad[i];
                if (p==excluded || p==excluded2 || !NativeAliveS311(p, map) || p.Faction != party.Faction ||
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
                if(p==excluded || p==excluded2 || !NativeAliveS311(p,map) || p.InMentalState ||
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
                FieldCost(pod, ThingDefOf.Steel) + (op.FortifyPrison ? 20 : 0);
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
                    "; steel=" + steel + ", components=" + components + ", fuel=" + fuel);
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
        // A valid assembly square must remain reachable and have an escape lane.
        private bool CanSealR041(RHFOperation op,Pawn vip,IntVec3 wall,Room room)
        {
            var reserved=new HashSet<IntVec3>();
            ThingDef launcherDef=DefDatabase<ThingDef>.GetNamedSilentFail("PodLauncher");
            ThingDef podDef=DefDatabase<ThingDef>.GetNamedSilentFail("TransportPod");
            if(launcherDef==null || podDef==null)return false;
            foreach(IntVec3 c in GenAdj.OccupiedRect(op.PodLauncherCell,NJRPodSiteClearS41.RotationFor(op.PodLauncherCell,op.PodCell),launcherDef.size))reserved.Add(c);
            foreach(IntVec3 c in GenAdj.OccupiedRect(op.PodCell,Rot4.North,podDef.size))reserved.Add(c);
            foreach(IntVec3 dir in GenAdj.CardinalDirections)
            {
                IntVec3 stand=op.PodCell+dir;
                if(!stand.InBounds(map) || stand==wall || !stand.Standable(map) ||
                   NJRPodSiteClearS41.ReservedFootprint(op,stand) || stand.GetRoom(map)!=room)continue;
                // One local flood, not a separate path search for every
                // soldier and every small cargo stack. Count the future pad
                // and proposed wall as solid, retain at least one escape lane.
                var seen=new HashSet<IntVec3>();var queue=new Queue<IntVec3>();
                seen.Add(stand);queue.Enqueue(stand);bool outside=false;
                while(queue.Count>0)
                {
                    IntVec3 at=queue.Dequeue();
                    if((at-stand).LengthHorizontalSquared>=36 && !at.Roofed(map) &&
                       at.GetEdifice(map)==null && at.GetRoom(map)?.PsychologicallyOutdoors==true)outside=true;
                    foreach(IntVec3 offset in GenAdj.CardinalDirections)
                    {
                        IntVec3 c=at+offset;
                        if(!c.InBounds(map) || c==wall || reserved.Contains(c) || seen.Contains(c) ||
                           Math.Abs(c.x-stand.x)>24 || Math.Abs(c.z-stand.z)>24)continue;
                        if(!c.Standable(map) && !(c.GetEdifice(map) is Building_Door))continue;
                        seen.Add(c);queue.Enqueue(c);
                    }
                }
                if(!outside || !seen.Contains(vip.Position))continue;
                bool access=true;
                foreach(Pawn member in op.Squad)
                    if(NativeAliveS311(member,map) && !seen.Contains(member.Position) && !reserved.Contains(member.Position))
                    {access=false;break;}
                if(access && op.PodCargoStacksS41!=null)
                    foreach(Thing stack in op.PodCargoStacksS41)
                        if(stack!=null && !stack.Destroyed && stack.Spawned && stack.Map==map && !seen.Contains(stack.Position))
                        {access=false;break;}
                if(access)return true;
            }
            return false;
        }
        private Thing NativeFortificationWorkS41(RHFOperation op,
            Pawn vip,Faction owner,int now)
        {
            if(op==null || vip==null || !vip.Spawned || owner==null || owner.IsPlayer ||
               !op.FortifyPrison || op.BarricadesBuilt>=2)return null;
            ThingDef cover=DefDatabase<ThingDef>.GetNamedSilentFail("Barricade");
            ThingDef requested=ThingDefOf.Wall;
            Room protectedRoom=ProtectedPodRoomR041(op);
            if(protectedRoom==null || protectedRoom.PsychologicallyOutdoors)return null;
            if(!op.BarricadeCell.IsValid)
            {
                // Retry after cargo unloading, a door breach or room topology change.
                if(op.FortifyStartedTick>0 && now-op.FortifyStartedTick<600)return null;
                IntVec3 best=IntVec3.Invalid;ThingDef bestDef=null;int bestScore=int.MaxValue;
                for(int radius=1;radius<=12;radius++)
                for(int dx=-radius;dx<=radius;dx++)
                for(int dz=-radius;dz<=radius;dz++)
                {
                    if(Math.Max(Math.Abs(dx),Math.Abs(dz))!=radius)continue;
                    IntVec3 cell=op.PodLauncherCell+new IntVec3(dx,0,dz);
                    if(!cell.InBounds(map) || !cell.Standable(map) ||
                       cell.GetEdifice(map)!=null || cell.GetFirstBuilding(map)!=null ||
                       cell.GetRoom(map)!=protectedRoom ||
                       op.CompletedBarricades.Contains(cell) ||
                       NJRPodSiteClearS41.ReservedFootprint(op,cell))continue;
                    Pawn occupant=cell.GetFirstPawn(map);
                    if(occupant!=null && (occupant==vip || !op.Squad.Contains(occupant)))continue;
                    bool n=SolidFlankS41(cell+IntVec3.North),s=SolidFlankS41(cell+IntVec3.South);
                    bool e=SolidFlankS41(cell+IntVec3.East),w=SolidFlankS41(cell+IntVec3.West);
                    bool DoorAt(IntVec3 c)=>c.InBounds(map) && c.GetEdifice(map) is Building_Door;
                    bool throat=(n&&(s||DoorAt(cell+IntVec3.South))) || (s&&DoorAt(cell+IntVec3.North)) ||
                                (e&&(w||DoorAt(cell+IntVec3.West))) || (w&&DoorAt(cell+IntVec3.East));
                    bool adjacentDoor=false,adjacentBreach=false;
                    foreach(IntVec3 side in GenAdj.CardinalDirections)
                    {
                        IntVec3 near=cell+side;
                        if(near.InBounds(map) && near.GetEdifice(map) is Building_Door)adjacentDoor=true;
                        // A missing wall between real wall flanks is itself
                        // the entrance opened by the assault; no door remains.
                        if(near.InBounds(map) && near.Standable(map) && near.GetRoom(map)!=protectedRoom)
                            adjacentBreach=true;
                    }
                    if(!throat && !adjacentDoor)continue;
                    // Walls at actual entrance throats; a passable native
                    // barricade provides cover when sealing would trap guards.
                    bool seal=throat && CanSealR041(op,vip,cell,protectedRoom);
                    ThingDef material=seal?ThingDefOf.Wall:cover;
                    if(material==null || (!seal && !adjacentDoor && !adjacentBreach && !throat))continue;
                    Pawn worker=NativeEngineerS311(op,cell,prepare:false);
                    if(worker==null || !worker.CanReach(cell,PathEndMode.Touch,Danger.Some))continue;
                    int score=(seal?0:10000)+(adjacentDoor?0:300)+radius*20+
                        (vip.Position-cell).LengthHorizontalSquared;
                    if(score<bestScore){best=cell;bestDef=material;bestScore=score;}
                }
                op.FortifyStartedTick=now;
                if(!best.IsValid)
                {
                    Log.Message("[NJR][R041][Fortify] no legal throat/cover yet; retry after topology or cargo changes");
                    return null;
                }
                op.BarricadeCell=best;requested=bestDef;
            }
            IntVec3 target=op.BarricadeCell;
            foreach(Thing item in map.thingGrid.ThingsListAt(target))
            {
                if(item==null || item.Destroyed)continue;
                if((item.def==ThingDefOf.Wall || item.def==cover) && item.Faction==owner)
                {
                    op.BarricadesBuilt++;op.CompletedBarricades.Add(target);
                    op.BarricadeCell=IntVec3.Invalid;op.FortifyStartedTick=0;
                    Log.Message("[NJR][R041][Fortify] completed "+item.def.defName+" "+target);
                    return NativeFortificationWorkS41(op,vip,owner,now);
                }
                if((item is Blueprint_Build || item is Frame) && item.Faction==owner)return item;
            }
            if(!target.Standable(map) || NJRPodSiteClearS41.ReservedFootprint(op,target))
            {op.BarricadeCell=IntVec3.Invalid;op.FortifyStartedTick=now;return null;}
            if(requested==ThingDefOf.Wall && !CanSealR041(op,vip,target,protectedRoom))requested=cover;
            if(requested==null)return null;
            try
            {
                Blueprint_Build plan=GenConstruct.PlaceBlueprintForBuild(
                    requested,target,map,Rot4.North,owner,ThingDefOf.Steel);
                if(plan!=null && plan.Faction==owner)
                {
                    Log.Message("[RHF S41][Fortify] Real enemy wall blueprint "+target+" def="+requested.defName+" / op="+op.CreatedTick);
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
                if(NJRRescueAccessR040.ContinuePodAccess(op,map,vip,now))return;
                if(NJRRescueAccessR040.TryPodAccess(op,map,vip,launcherDef,podDef,now,
                    out IntVec3 launchCell,out IntVec3 podCell))return;
                if(!launchCell.IsValid || !podCell.IsValid)
                { NativeFallBackS311(op, now, "no physical build site or recoverable access route"); return; }
                NJRRescueAccessR040.Clear(op);
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
            if(RHFRescuePlannerS41.Enabled && op.PodConstructionStage>=1 &&
               op.PodConstructionStage<=4 && op.FortifyPrison && op.BarricadesBuilt<2 &&
               engineer!=null && active>=(refueler==null?3:4))
            {
                parallelWall=NativeFortificationWorkS41(op,vip,owner.Faction,now);
                if(parallelWall!=null)
                {
                    fortifier=NativeEngineerS311(op,parallelWall.Position,engineer,refueler);
                    if(fortifier!=null && RHFPodSupplyS41.HasCargo(op,fortifier))fortifier=null;
                }
            }
            if(RHFRescuePlannerS41.Enabled)
                RHFPodPlannerS41.Plan(op,map,engineer,
                    op.PodConstructionStage==4 ? op.PodVehicleThing :
                    NativeWorkSiteS311(map,siteCell,owner.Faction) ??
                    (op.PodConstructionStage==3 ? op.PodLauncherThing : null),
                    now,fortifier,parallelWall,refueler,op.PodLauncherThing);
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
