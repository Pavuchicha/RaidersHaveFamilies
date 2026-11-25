// R045: bounded physical clearance of drop slag, not demolition of furnishings.
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
        internal static bool IsDebris(Thing thing)=>thing?.def?.defName=="ChunkSlagSteel";
        internal static bool IsAssigned(RHFOperation op,Pawn worker,Thing thing)=>
            op!=null && op.FortifyPrison && op.Phase==3 && op.PodConstructionStage==0 && IsDebris(thing) &&
            States.TryGetValue(op,out State state) && state.Worker==worker && state.Debris==thing;
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
        private static bool LocalBypass(Map map,IntVec3 blocked)
        {
            var neighbors=new List<IntVec3>();
            for(int i=0;i<4;i++)
            {
                IntVec3 n=RHFObjectiveRouteV128.CardinalR044(blocked,i);
                if(n.InBounds(map) && n.Walkable(map))neighbors.Add(n);
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
                       !n.Walkable(map) || n.GetEdifice(map) is Building_Door || !seen.Add(n))continue;
                    queue.Enqueue(n);
                }
            }
            foreach(IntVec3 n in neighbors)if(!seen.Contains(n))return false;
            return true;
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
                        if(dx*dx+dz*dz<9 || !cell.InBounds(map) || !cell.Standable(map) ||
                           cell.GetFirstBuilding(map)!=null || cell.GetFirstPawn(map)!=null ||
                           (cell-vip.Position).LengthHorizontalSquared<16 ||
                           RHFObjectiveRouteV128.StagingExposedToTurret(map,cell))continue;
                        bool occupied=false;
                        foreach(Thing item in map.thingGrid.ThingsListAt(cell))
                            if(item.def.category==ThingCategory.Item){occupied=true;break;}
                        if(occupied)continue;
                        int free=0;bool door=false;
                        for(int i=0;i<4;i++)
                        {
                            IntVec3 near=RHFObjectiveRouteV128.CardinalR044(cell,i);
                            if(!near.InBounds(map))continue;
                            if(near.GetEdifice(map) is Building_Door)door=true;
                            if(near.Walkable(map))free++;
                        }
                        // Never drop debris in a one-cell through corridor or next to a door.
                        if(door || free==0 || (free>1 && !LocalBypass(map,cell)) ||
                           !candidate.CanReach(cell,PathEndMode.OnCell,Danger.Deadly))continue;
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
