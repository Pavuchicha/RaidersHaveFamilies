// S4.1-A/F: real enemy-owned native transporter flight.
// Physical construction, refueling and boarding are handled by S4.1-E.
// This layer changes ONLY the world handoff and arrival of that same VIP.
// Never fake a skyfaller or remove the captive from the game to score a win.
using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RaidersHaveFamilies
{
    // A real world action. Serialized into the launched travel object so a
    // save/load during the flight does not lose the operation's victim.
    public sealed class RHFEnemyPodArrivalS41 : TransportersArrivalAction
    {
        public Pawn Vip;
        public Faction Operator;
        public int OperationCreatedTick;
        public bool ArrivedSuccessfully;
        // The first leg is fueled and physically launched. Subsequent prepared
        // logistics hops are abstract; no fictional map/pad is spawned.
        public bool UsesPreparedRelay;
        public PlanetTile FinalDestination = PlanetTile.Invalid;
        public PlanetTile FirstLegDestination = PlanetTile.Invalid;
        public override bool GeneratesMap => false;
        public RHFEnemyPodArrivalS41() { } // Scribe needs parameterless ctor
        internal RHFEnemyPodArrivalS41(RHFOperation op)
        {
            Vip=op.Vip;
            Operator=op.PodCargoOwnerFactionS311;
            OperationCreatedTick=op.CreatedTick;
        }
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref Vip,"rhfAirVipS41AF");
            Scribe_References.Look(ref Operator,"rhfAirOperatorS41AF");
            Scribe_Values.Look(ref OperationCreatedTick,"rhfAirOperationS41AF",0);
            Scribe_Values.Look(ref ArrivedSuccessfully,"rhfAirArrivedS41AF",false);
            Scribe_Values.Look(ref UsesPreparedRelay,"rhfPreparedRelayS41AP",false);
            Scribe_Values.Look(ref FinalDestination,"rhfFinalAirDestinationS41AP");
            Scribe_Values.Look(ref FirstLegDestination,"rhfFirstLegDestinationS41AP");
        }
        public override void Arrived(List<ActiveTransporterInfo> transporters,
            PlanetTile destinationTile)
        {
            // Arrival operates on the actual cargo inside TravellingTransporters.
            // Detach each pawn before the vanilla world-object cleanup. Don't
            // spawn a player caravan or overwrite any pawn's authentic faction.
            bool rescued=false;
            if(transporters!=null)
            for(int i=0;i<transporters.Count;i++)
            {
                ThingOwner holder=transporters[i]?.innerContainer;
                if(holder==null)continue;
                for(int j=holder.Count-1;j>=0;j--)
                {
                    Pawn p=holder[j] as Pawn;
                    if(p==null)continue;
                    holder.Remove(p);
                    if(p==Vip)
                    {
                        // Decide-mode world pawns may be garbage-collected after
                        // the travelling world object is destroyed. The actual
                        // captured VIP must survive subsequent world GC/save-load.
                        if(p.Discarded)
                        {
                            Log.Error("[RHF S41][Air] Arriving VIP already discarded: "+p);
                            continue; // never count an unsalvageable passenger
                        }
                        if(!p.IsWorldPawn())
                            Find.WorldPawns.PassToWorld(p,PawnDiscardDecideMode.KeepForever);
                        else
                            Find.WorldPawns.ForcefullyKeptPawns.Add(p);
                        if(p.IsWorldPawn() && Find.WorldPawns.ForcefullyKeptPawns.Contains(p))
                            rescued=true;
                        else
                            Log.Error("[RHF S41][Air] VIP was not durably registered in WorldPawns");
                    }
                    else if(!p.IsWorldPawn())Find.WorldPawns.PassToWorld(p);
                }
                // No orphaned ThingOwner: cargo has reached its destination.
                holder.ClearAndDestroyContentsOrPassToWorld(DestroyMode.Vanish);
            }
            ArrivedSuccessfully=rescued;
            if(!rescued)
            {
                Log.Error("[RHF S41][Air] Arrived without the designated VIP, op="+
                    OperationCreatedTick+"; no rescue success credited");
                return;
            }
            // Remaining physical cargo is processed by the world transporter's
            // own item holder cleanup. The VIP stays a world pawn, not a fake
            // corpse, player caravan or deleted pawn.
            if(RHFMod.Settings?.VerboseLogging==true)
                Log.Message("[RHF S41][Air] VIP reached world destination: "+
                    destinationTile+", operator="+(Operator?.Name??"unknown"));
        }
    }

    internal static class RHFEnemyPodLaunchS41
    {
        // Native launched pods require an existing nonplayer world destination.
        // No imagined off-map tile or teleportation directly from the prison.
        internal static bool TryLaunch(RHFOperation op,Map map,int now)
        {
            if(op==null || map==null || !op.TryPod || op.PodLaunchCompleted ||
                op.Vip==null || op.Vip.Dead || op.PodCargoOwnerFactionS311==null ||
                op.PodCargoOwnerFactionS311.IsPlayer ||
                op.PodVehicleThing is not ThingWithComps craft || !craft.Spawned ||
                craft.Map!=map || craft.Faction!=op.PodCargoOwnerFactionS311)
                return false;
            CompTransporter cargo=craft.GetComp<CompTransporter>();
            CompLaunchable launch=craft.GetComp<CompLaunchable>();
            if(cargo==null || launch==null ||
                !cargo.LoadingInProgressOrReadyToLaunch ||
                !cargo.innerContainer.Contains(op.Vip) ||
                !launch.CanLaunch())return false;
            // Use a true fuel-reachable first leg. For a distant home, the
            // faction's prepared intermediate pads are narrative abstraction:
            // the real world transport later follows the long route, carrying
            // the SAME VIP. No off-map teleports or fake passenger removal.
            if(!TryDestination(op,map,launch,out PlanetTile destination,
                out PlanetTile finalDestination,out bool preparedRelay))return false;
            int group=cargo.groupID;
            if(group<0)return false;
            var action=new RHFEnemyPodArrivalS41(op)
            {
                UsesPreparedRelay=preparedRelay,
                FinalDestination=finalDestination,
                FirstLegDestination=destination
            };
            try
            {
                // Vanilla actual launch: fuel is consumed, real
                // FlyShipLeaving created, the SAME VIP is transferred to the
                // active transporter's ThingOwner.
                launch.TryLaunch(destination,action);
            }
            catch(Exception e)
            {
                Log.Error("[RHF S41][Air] Native launch failed: "+e);
                return false;
            }
            // TryLaunch returns void. Verify the physical leaving skyfaller,
            // rather than assuming calling the method was a successful launch.
            var leaving=map.listerThings.ThingsInGroup(ThingRequestGroup.ActiveTransporter);
            for(int i=0;i<leaving.Count;i++)
            {
                FlyShipLeaving flight=leaving[i] as FlyShipLeaving;
                if(flight!=null && flight.groupID==group &&
                    ReferenceEquals(flight.arrivalAction,action) &&
                    flight.Contents?.innerContainer?.Contains(op.Vip)==true)
                {
                    op.PodStageStartedTickS311=now;
                    if(RHFMod.Settings?.VerboseLogging==true)
                        Log.Message("[RHF S41][Air] Physical takeoff verified: op="+
                            op.CreatedTick+" / destination="+destination);
                    return true;
                }
            }
            Log.Error("[RHF S41][Air] Launch returned without a matching physical " +
                "skyfaller carrying VIP; no success credited");
            return false;
        }
        private static bool TryDestination(RHFOperation op,Map map,
            CompLaunchable launch,out PlanetTile destination,
            out PlanetTile finalDestination,out bool preparedRelay)
        {
            destination=PlanetTile.Invalid;
            finalDestination=PlanetTile.Invalid;
            preparedRelay=false;
            if(op==null || map==null || launch==null ||
               !map.Tile.Valid || launch.FuelLevel<=0f)return false;
            // Sponsoring faction has priority, then the rescue operator. A
            // distant real settlement is still valid: fuel funds the first
            // physical hop, remaining legs use pre-established logistics.
            var sites=Find.WorldObjects.Settlements;
            Faction sponsor=op.Sponsor;
            Faction operatorFaction=op.PodCargoOwnerFactionS311;
            float bestDistance=float.MaxValue;
            for(int priority=0;priority<2;priority++)
            {
                Faction owner=priority==0 ? sponsor : operatorFaction;
                if(owner==null || owner.IsPlayer ||
                   (priority==1 && owner==sponsor))continue;
                foreach(Settlement site in sites)
                {
                    if(site==null || site.Faction!=owner || !site.Tile.Valid ||
                       site.Tile==map.Tile || site.Tile.Layer!=map.Tile.Layer)continue;
                    float distance=Find.WorldGrid.ApproxDistanceInTiles(map.Tile,site.Tile);
                    if(distance<0f || distance>=bestDistance)continue;
                    bestDistance=distance;
                    finalDestination=site.Tile;
                }
                if(finalDestination.Valid)break;
            }
            if(!finalDestination.Valid)return false;
            int range=launch.MaxLaunchDistanceAtFuelLevel(
                launch.FuelLevel,finalDestination.Layer);
            if(range<=0)return false;
            int realDistance=Find.WorldGrid.TraversalDistanceBetween(
                map.Tile,finalDestination,true,range,true);
            if(realDistance>=0 && realDistance<=range)
            {
                destination=finalDestination;
                return true;
            }
            // Real first leg: choose a world tile adjacent to the colony.
            // One-hop fuel is consumed by vanilla TryLaunch. The resulting
            // TravellingTransporters remains a real serialized world object.
            if(range<1)return false;
            int neighborCount=Find.WorldGrid.GetTileNeighborCount(map.Tile);
            float closest=float.MaxValue;
            for(int j=0;j<neighborCount && j<8;j++)
            {
                PlanetTile next=Find.WorldGrid.GetTileNeighbor(map.Tile,j);
                if(!next.Valid || next.Layer!=map.Tile.Layer)continue;
                float d=Find.WorldGrid.ApproxDistanceInTiles(next,finalDestination);
                if(d>=closest)continue;
                closest=d;
                destination=next;
            }
            if(!destination.Valid)return false;
            preparedRelay=true;
            return true;
        }
        // A launch may be valid even if a mod immediately advances the
        // FlyShipLeaving or a save/load happens before the caller records
        // PodConstructionStage=6. Recover only from PHYSICAL passengers:
        // a tracked leaving skyfaller, or a real travelling world object.
        internal static bool DepartureInFlight(RHFOperation op,Map map)
        {
            if(op==null || map==null || op.Vip==null || op.Phase!=3)
                return false;
            if(Reconcile(op,map))return true;
            var leaving=map.listerThings.ThingsInGroup(
                ThingRequestGroup.ActiveTransporter);
            for(int i=0;i<leaving.Count;i++)
            {
                FlyShipLeaving ship=leaving[i] as FlyShipLeaving;
                RHFEnemyPodArrivalS41 action=ship?.arrivalAction
                    as RHFEnemyPodArrivalS41;
                if(action==null || action.OperationCreatedTick!=op.CreatedTick ||
                   action.Vip!=op.Vip || action.Operator!=op.PodCargoOwnerFactionS311 ||
                   ship.Contents?.innerContainer?.Contains(op.Vip)!=true)continue;
                return true;
            }
            return false;
        }
        // Recovery after a save/load or a Harmony chain that suppressed our
        // LeaveMap postfix. Only a REAL travelling pod holding this VIP counts.
        // This runs at most once per 90-tick strategic pulse in Phase 3 stage 6.
        internal static bool Reconcile(RHFOperation op,Map origin)
        {
            if(op==null || origin==null || op.Vip==null || op.Phase!=3 ||
               op.PodLaunchCompleted)return op?.PodLaunchCompleted==true;
            var all=Find.WorldObjects.AllWorldObjects;
            for(int i=0;i<all.Count;i++)
            {
                TravellingTransporters travel=all[i] as TravellingTransporters;
                RHFEnemyPodArrivalS41 action=travel?.arrivalAction
                    as RHFEnemyPodArrivalS41;
                if(action==null || action.OperationCreatedTick!=op.CreatedTick ||
                   action.Vip!=op.Vip || !travel.ContainsPawn(op.Vip))continue;
                WorldHandoff(action,travel,origin);
                return op.PodLaunchCompleted;
            }
            return false;
        }

        // Called at the moment vanilla has created a real travelling world
        // object. Do not mark success during boarding or after launching only.
        internal static void WorldHandoff(RHFEnemyPodArrivalS41 action,
            TravellingTransporters world,Map origin)
        {
            if(action==null || world==null || origin==null || action.Vip==null ||
                world.arrivalAction!=action || !world.ContainsPawn(action.Vip) ||
                action.Operator==null || action.Operator.IsPlayer)return;
            if(world.Faction!=action.Operator)
                world.SetFaction(action.Operator); // fix vanilla forced-player owner
            // Only AFTER the physical passenger handoff: the first, fueled
            // native launch already created this travelling pod. Redirect its
            // route to the real home settlement; logistics hops are abstract.
            if(action.UsesPreparedRelay && action.FinalDestination.Valid &&
               world.destinationTile==action.FirstLegDestination &&
               world.ContainsPawn(action.Vip))
            {
                world.destinationTile=action.FinalDestination;
                Log.Message("[RHF S41][Air] Prepared relay chain established; " +
                    "same physical VIP remains aboard, destination=" +
                    action.FinalDestination);
            }
            List<RHFOperation> operations=RHFMissionIntegrityV130.Operations(origin);
            if(operations==null)return;
            for(int i=0;i<operations.Count;i++)
            {
                RHFOperation op=operations[i];
                if(op==null || op.CreatedTick!=action.OperationCreatedTick ||
                   op.Vip!=action.Vip || op.Phase!=3 ||
                   op.PodLaunchCompleted)continue;
                op.PodLaunchCompleted=true;
                op.TryPod=false;
                // Still rescue additional prisoners; do not silently abandon
                // those waiting at the construction site.
                op.Phase=2;
                op.PhaseStartTick=Find.TickManager.TicksGame;
                if(op.BoardS41!=null)
                {
                    op.BoardS41.Orders.Clear();
                    op.BoardS41.NextReplanTick=0;
                    op.BoardS41.LastPlanTick=0;
                    op.BoardS41.ObservedPhase=-1;
                    op.BoardS41.Decision="Physical VIP airlift confirmed; ground group withdrawing";
                }
                Log.Message("[RHF S41][Air] Real hostile world transport " +
                    "contains the VIP. Rescue op="+op.CreatedTick);
                break;
            }
        }
    }

    // Vanilla creates its TravellingTransporters as PLAYER-owned regardless of
    // the pod's faction. Scope this correction to OUR serialized arrival action.
    // No changes to any player, SRTS or other mod's transporter.
    [HarmonyPatch(typeof(FlyShipLeaving),"LeaveMap")]
    internal static class RHFEnemyPodWorldHandoffS41
    {
        private struct LaunchContext
        {
            internal Map Map;
            internal RHFEnemyPodArrivalS41 Action;
        }
        [HarmonyPrefix]
        private static void Prefix(FlyShipLeaving __instance,out LaunchContext __state)
        {
            __state=new LaunchContext();
            RHFEnemyPodArrivalS41 action=__instance?.arrivalAction
                as RHFEnemyPodArrivalS41;
            if(action==null)return;
            __state.Map=__instance.Map;
            __state.Action=action;
        }
        [HarmonyPostfix]
        private static void Postfix(LaunchContext __state)
        {
            if(__state.Action==null || __state.Map==null)return;
            // Normally one world traveler is created, then the entire group
            // leaves; find by the UNIQUE serialized arrival-action reference.
            var objects=Find.WorldObjects.AllWorldObjects;
            for(int i=0;i<objects.Count;i++)
            {
                TravellingTransporters travel=objects[i] as TravellingTransporters;
                if(travel!=null &&
                   ReferenceEquals(travel.arrivalAction,__state.Action))
                {
                    RHFEnemyPodLaunchS41.WorldHandoff(__state.Action,travel,
                        __state.Map);
                    return;
                }
            }
        }
    }
}
