// R046: common physical debris clearance for site search, footprints and seals.
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using RimWorld;
using Verse;
using Verse.AI;
namespace RaidersHaveFamilies
{
    internal static class NJRVipDebrisR045
    {
        private sealed class State
        {
            internal Pawn Worker;
            internal Thing Debris;
            internal IntVec3 Destination=IntVec3.Invalid;
            internal int Started;
            internal readonly HashSet<Thing> Tried=new HashSet<Thing>();
        }
        private static readonly ConditionalWeakTable<RHFOperation,State> States=new ConditionalWeakTable<RHFOperation,State>();
        internal static bool IsDebris(Thing thing)=>thing?.def?.category==ThingCategory.Item &&
            (thing.def.defName=="ChunkSlagSteel" || thing.def.defName.StartsWith("Chunk",StringComparison.Ordinal));
        internal static bool IsAssigned(RHFOperation op,Pawn worker,Thing thing)
        {
            if(op==null || !op.FortifyPrison || (op.Phase!=3 && !(op.Phase==1 && op.TryPod && op.PodConstructionStage>=1)) || !IsDebris(thing))return false;
            if(op.PodConstructionStage==0)
                return States.TryGetValue(op,out State state) && state.Worker==worker && state.Debris==thing;
            if(op.PodConstructionStage<1 || op.PodConstructionStage>4)return false;
            RHFOrderS41 order=op.BoardS41?.For(worker,false);
            return order!=null && order.Role==(int)RHFRoleS41.PodSupply && order.Objective==thing &&
                order.Cell.IsValid && (thing.Position==op.BarricadeCell || thing.Position==op.DeferredSealClearR057 || NJRPodSiteClearS41.ReservedFootprint(op,thing.Position));
        }
        // A loose chunk is a recoverable obstruction, not a permanent wall.
        // Reject actual terrain/buildings and all non-debris impassable things.
        internal static bool ClearableCell(RHFOperation op,Map map,IntVec3 cell)
        {
            if(!cell.InBounds(map) || map.terrainGrid.TerrainAt(cell)?.passability==Traversability.Impassable)return false;
            if(cell.GetEdifice(map)!=null || cell.GetFirstBuilding(map)!=null)return false;
            bool debris=false;
            foreach(Thing item in map.thingGrid.ThingsListAt(cell))
            {
                if(IsDebris(item)){debris=true;continue;}
                if(item.def?.passability==Traversability.Impassable)return false;
            }
            return cell.Standable(map) || debris;
        }
        internal static Thing DebrisAt(Map map,IntVec3 cell)
        {
            if(!cell.IsValid || !cell.InBounds(map))return null;
            foreach(Thing item in map.thingGrid.ThingsListAt(cell))
                if(IsDebris(item) && item.Spawned && !item.Destroyed)return item;
            return null;
        }
        internal static Thing FootprintDebris(RHFOperation op,Map map)
        {
            if(op==null || !op.FortifyPrison || op.Phase!=3 || op.PodConstructionStage<1 || op.PodConstructionStage>2)return null;
            ThingDef def=DefDatabase<ThingDef>.GetNamedSilentFail(op.PodConstructionStage==1?"PodLauncher":"TransportPod");
            if(def==null)return null;
            IntVec3 origin=op.PodConstructionStage==1?op.PodLauncherCell:op.PodCell;
            if(!origin.IsValid)return null;
            Rot4 rotation=op.PodConstructionStage==1?NJRPodSiteClearS41.RotationFor(op.PodLauncherCell,op.PodCell):Rot4.North;
            foreach(IntVec3 c in GenAdj.OccupiedRect(origin,rotation,def.size))
            {Thing chunk=DebrisAt(map,c);if(chunk!=null)return chunk;}
            return null;
        }
        internal static void Completed(RHFOperation op,Thing original,Thing dropped)
        {
            if(op==null || !States.TryGetValue(op,out State state))return;
            state.Tried.Add(original);state.Tried.Add(dropped);state.Debris=null;state.Worker=null;
        }
        internal static bool Continue(RHFOperation op,Map map,int now)
        {
            if(!States.TryGetValue(op,out State state) || state.Debris==null)return false;
            if(RHFRescuePlannerS41.ActiveForRoleS41(state.Worker,map) && now-state.Started<1200 &&
               (state.Debris.Spawned && state.Debris.Map==map || state.Worker.carryTracker?.CarriedThing==state.Debris))
            {
                op.BoardS41.Assign(state.Worker,RHFRoleS41.PodSupply,state.Debris,state.Destination,now);
                return true;
            }
            state.Tried.Add(state.Debris);state.Debris=null;state.Worker=null;return false;
        }
        // A corner with several exits is safe only if they stay connected
        // after depositing the chunk. No new choke point is introduced.
        private static bool LocalBypass(RHFOperation op,Map map,IntVec3 blocked)
        {
            var neighbors=new List<IntVec3>();
            for(int i=0;i<4;i++)
            {
                IntVec3 n=RHFObjectiveRouteV128.CardinalR044(blocked,i);
                if(n.InBounds(map) && n.Walkable(map) && n!=op.BarricadeCell && !NJRPodSiteClearS41.ReservedFootprint(op,n))neighbors.Add(n);
            }
            if(neighbors.Count<2)return true;
            var seen=new HashSet<IntVec3>{neighbors[0]};var queue=new Queue<IntVec3>();queue.Enqueue(neighbors[0]);
            while(queue.Count>0)
            {
                IntVec3 c=queue.Dequeue();
                for(int i=0;i<4;i++)
                {
                    IntVec3 n=RHFObjectiveRouteV128.CardinalR044(c,i);
                    if(n==blocked || !n.InBounds(map) || Math.Abs(n.x-blocked.x)>3 || Math.Abs(n.z-blocked.z)>3 ||
                       !n.Walkable(map) || n==op.BarricadeCell || NJRPodSiteClearS41.ReservedFootprint(op,n) || n.GetEdifice(map) is Building_Door || !seen.Add(n))continue;
                    queue.Enqueue(n);
                }
            }
            foreach(IntVec3 n in neighbors)if(!seen.Contains(n))return false;
            return true;
        }
        // Only a dedicated free soldier may move chunks; never steal the
        // primary builder or the concurrent refueler. No demolition, no teleport.
        internal static bool TryMove(RHFOperation op,Map map,Thing chunk,HashSet<IntVec3> pocket,
            Pawn engineer,Pawn refueler,out Pawn worker,out IntVec3 destination,HashSet<IntVec3> forbidden=null,HashSet<Pawn> unavailable=null)
        {
            worker=null;destination=IntVec3.Invalid;
            if(!IsDebris(chunk) || !chunk.Spawned || chunk.Map!=map)return false;
            int best=int.MaxValue;
            foreach(Pawn candidate in op.Squad)
            {
                if(candidate==engineer || candidate==refueler || (unavailable!=null && unavailable.Contains(candidate)) || NJREarlyWingR053.IsAssault(op,candidate) || !RHFRescuePlannerS41.ActiveForRoleS41(candidate,map) ||
                   candidate.Faction!=op.PodCargoOwnerFactionS311 || candidate.InMentalState || candidate.carryTracker==null ||
                   candidate.carryTracker.CarriedThing!=null || (op.Phase==3 && RHFPodSupplyS41.HasCargo(op,candidate)) ||
                   RHFCombatPlannerV125.IsCombatEmergencyJob(candidate) || candidate.WorkTypeIsDisabled(WorkTypeDefOf.Hauling) ||
                   candidate.health?.capacities?.CapableOf(PawnCapacityDefOf.Manipulation)!=true ||
                   !candidate.CanReach(chunk,PathEndMode.ClosestTouch,Danger.Deadly))continue;
                for(int dx=-7;dx<=7;dx++)for(int dz=-7;dz<=7;dz++)
                {
                    IntVec3 c=chunk.Position+new IntVec3(dx,0,dz);
                    if(dx*dx+dz*dz<4 || (forbidden!=null && forbidden.Contains(c)) || (pocket!=null && !pocket.Contains(c)) ||
                       !SafeDrop(op,map,c,candidate) || !candidate.CanReach(c,PathEndMode.OnCell,Danger.Deadly))continue;
                    int score=(candidate.Position-chunk.Position).LengthHorizontalSquared+dx*dx+dz*dz+
                        (candidate.skills?.GetSkill(SkillDefOf.Construction)?.Level??0)*40+
                        (op.PodCell.IsValid && op.PodCell.Roofed(map) && !c.Roofed(map)?10000:0);
                    if(score>=best)continue;
                    best=score;worker=candidate;destination=c;
                }
            }
            return worker!=null;
        }
        internal static bool SafeDrop(RHFOperation op,Map map,IntVec3 cell,Pawn worker)
        {
            HashSet<IntVec3> pocket=op.Phase==3?RHFPodSupplyS41.SealPocketR044(op):null;
            if(!cell.IsValid || !cell.InBounds(map) || !cell.Standable(map) || cell.GetFirstBuilding(map)!=null ||
               (pocket!=null && !pocket.Contains(cell)) ||
               (cell==op.BarricadeCell || cell==op.DeferredSealClearR057 || op.PlannedBarricadesR055.Contains(cell)) || NJRPodSiteClearS41.ReservedFootprint(op,cell) ||
               (cell.GetFirstPawn(map)!=null && cell.GetFirstPawn(map)!=worker) ||
               (op.Vip!=null && op.Vip.Spawned && (cell-op.Vip.Position).LengthHorizontalSquared<4) ||
               RHFObjectiveRouteV128.StagingExposedToTurret(map,cell))return false;
            foreach(Thing item in map.thingGrid.ThingsListAt(cell))if(item.def.category==ThingCategory.Item)return false;
            int free=0;
            for(int i=0;i<4;i++)
            {
                IntVec3 n=RHFObjectiveRouteV128.CardinalR044(cell,i);
                if(!n.InBounds(map))continue;
                if(n.GetEdifice(map) is Building_Door)return false;
                if(n.Walkable(map) && n!=op.BarricadeCell && !NJRPodSiteClearS41.ReservedFootprint(op,n))free++;
            }
            return free>0 && (free==1 || LocalBypass(op,map,cell));
        }
        internal static bool TryStart(RHFOperation op,Map map,Pawn vip,int now)
        {
            if(op==null || !op.FortifyPrison || op.Phase!=3 || op.PodConstructionStage!=0)return false;
            if(Continue(op,map,now))return true;
            State state=States.GetValue(op,_=>new State());
            if(state.Tried.Count>=6)return false;
            ThingDef slag=DefDatabase<ThingDef>.GetNamedSilentFail("ChunkSlagSteel");
            if(slag==null)return false;
            Thing best=null;Pawn worker=null;IntVec3 destination=IntVec3.Invalid;int bestScore=int.MaxValue;
            foreach(Thing chunk in map.listerThings.ThingsOfDef(slag))
            {
                if(!chunk.Spawned || chunk.Map!=map || state.Tried.Contains(chunk) ||
                   (chunk.Position-vip.Position).LengthHorizontalSquared>144)continue;
                foreach(Pawn candidate in op.Squad)
                {
                    if(!RHFRescuePlannerS41.ActiveForRoleS41(candidate,map) || candidate.carryTracker==null ||
                       candidate.carryTracker.CarriedThing!=null || RHFCombatPlannerV125.IsCombatEmergencyJob(candidate) ||
                       candidate.health?.capacities?.CapableOf(PawnCapacityDefOf.Manipulation)!=true ||
                       !candidate.CanReach(chunk,PathEndMode.ClosestTouch,Danger.Deadly))continue;
                    for(int dx=-7;dx<=7;dx++)for(int dz=-7;dz<=7;dz++)
                    {
                        IntVec3 cell=chunk.Position+new IntVec3(dx,0,dz);
                        if(dx*dx+dz*dz<9 || (cell-vip.Position).LengthHorizontalSquared<16 ||
                           !SafeDrop(op,map,cell,candidate) ||
                           !candidate.CanReach(cell,PathEndMode.OnCell,Danger.Deadly))continue;
                        int free=0;
                        for(int i=0;i<4;i++)
                        {IntVec3 n=RHFObjectiveRouteV128.CardinalR044(cell,i);if(n.InBounds(map) && n.Walkable(map))free++;}
                        int score=free*500+(candidate.Position-chunk.Position).LengthHorizontalSquared+dx*dx+dz*dz;
                        if(score<bestScore){bestScore=score;best=chunk;worker=candidate;destination=cell;}
                    }
                }
            }
            if(best==null)return false;
            state.Worker=worker;state.Debris=best;state.Destination=destination;state.Started=now;
            RHFPodPlannerS41.Plan(op,map,null,null,now);
            op.BoardS41.Assign(worker,RHFRoleS41.PodSupply,best,destination,now);
            op.BoardS41.Decision="R045 physical drop debris clearance";
            Log.Message("[NJR][R045] clearing "+best.Position+" -> "+destination+" by "+worker.LabelShortCap);
            return true;
        }
    }
}
