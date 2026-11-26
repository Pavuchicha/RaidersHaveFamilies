// Bounded snapshot of the actual local topology. No native pathfinder calls in this graph.
using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RaidersHaveFamilies
{
    internal sealed class SealGeometryR049
    {
        private readonly HashSet<IntVec3> passable=new HashSet<IntVec3>();
        private readonly HashSet<IntVec3> doors=new HashSet<IntVec3>();
        private readonly HashSet<IntVec3> exterior=new HashSet<IntVec3>();
        private readonly HashSet<IntVec3> unroofed=new HashSet<IntVec3>();
        private readonly HashSet<IntVec3> seeds=new HashSet<IntVec3>();
        private readonly HashSet<IntVec3> interior;
        private readonly HashSet<IntVec3> gates;
        private readonly Dictionary<IntVec3,int> baseline;
        internal int SeedCount=>seeds.Count;
        internal int ExteriorCount=>exterior.Count;
        internal bool HasShelteredSeed { get; private set; }
        internal SealGeometryR049(RHFOperation op,Map map)
        {
            // Use the true 1x2 launcher rotation and pod footprint, not only their origins.
            for(int dx=-20;dx<=20;dx++)for(int dz=-20;dz<=20;dz++)
            {
                IntVec3 c=op.PodCell+new IntVec3(dx,0,dz);
                if(!c.InBounds(map) || NJRPodSiteClearS41.ReservedFootprint(op,c))continue;
                bool door=DoorAt(map,c);
                if(!door && !c.Walkable(map) && !NJRVipDebrisR045.ClearableCell(op,map,c))continue;
                passable.Add(c);
                if(door)doors.Add(c);
                if(!c.Roofed(map))unroofed.Add(c);
            }
            // The pod crew deliberately removes the roof. A skylight in an
            // enclosed room is not an exterior ingress. Trace the genuinely
            // exposed ground from the perimeter, without walking through doors.
            var outside=new Queue<IntVec3>();
            foreach(IntVec3 c in unroofed)
            {
                if(doors.Contains(c))continue;
                if(Math.Abs(c.x-op.PodCell.x)==20 || Math.Abs(c.z-op.PodCell.z)==20 ||
                   c.x==0 || c.z==0 || c.x==map.Size.x-1 || c.z==map.Size.z-1)
                    if(exterior.Add(c))outside.Enqueue(c);
            }
            while(outside.Count>0)
            {
                IntVec3 c=outside.Dequeue();
                for(int d=0;d<4;d++)
                {
                    IntVec3 n=RHFObjectiveRouteV128.CardinalR044(c,d);
                    if(unroofed.Contains(n) && !doors.Contains(n) && exterior.Add(n))outside.Enqueue(n);
                }
            }
            for(int d=0;d<4;d++)
            {
                IntVec3 c=RHFObjectiveRouteV128.CardinalR044(op.PodCell,d);
                if(passable.Contains(c) && !doors.Contains(c) && c.Standable(map))seeds.Add(c);
            }
            // Actual boarding square is the anchor, regardless of roofing.
            // If all immediate squares have loose chunks, permit prospective
            // geometry; CanSealR041 still requires a real free work square.
            if(seeds.Count==0)
                for(int d=0;d<4;d++)
                {
                    IntVec3 c=RHFObjectiveRouteV128.CardinalR044(op.PodCell,d);
                    if(passable.Contains(c) && !doors.Contains(c) &&
                       NJRVipDebrisR045.ClearableCell(op,map,c))seeds.Add(c);
                }
            foreach(IntVec3 c in seeds)if(!exterior.Contains(c))HasShelteredSeed=true;
            exterior.ExceptWith(seeds);
            gates=Gates(IntVec3.Invalid,out interior);
            // Exterior trace may traverse hostile doors, but never the defended room.
            var sources=Distances(exterior,IntVec3.Invalid,interior);
            gates.IntersectWith(sources.Keys);
            baseline=Distances(exterior,IntVec3.Invalid,null);
        }
        internal static bool DoorAt(Map map,IntVec3 c)
        {
            if(!c.InBounds(map))return false;
            if(c.GetEdifice(map) is Building_Door)return true;
            foreach(Thing t in map.thingGrid.ThingsListAt(c))if(t is Building_Door && t.Spawned)return true;
            return false;
        }
        private HashSet<IntVec3> Gates(IntVec3 blocked,out HashSet<IntVec3> inside)
        {
            inside=new HashSet<IntVec3>();var result=new HashSet<IntVec3>();var q=new Queue<IntVec3>();
            foreach(IntVec3 c in seeds)if(c!=blocked && inside.Add(c))q.Enqueue(c);
            while(q.Count>0)
            {
                IntVec3 at=q.Dequeue();
                for(int d=0;d<4;d++)
                {
                    IntVec3 n=RHFObjectiveRouteV128.CardinalR044(at,d);
                    if(n==blocked || !passable.Contains(n))continue;
                    if(doors.Contains(n) || exterior.Contains(n)){result.Add(n);continue;}
                    if(inside.Add(n))q.Enqueue(n);
                }
            }
            return result;
        }
        private Dictionary<IntVec3,int> Distances(IEnumerable<IntVec3> starts,IntVec3 blocked,HashSet<IntVec3> forbidden)
        {
            var dist=new Dictionary<IntVec3,int>();var q=new Queue<IntVec3>();
            foreach(IntVec3 c in starts)
                if(c!=blocked && (forbidden==null || !forbidden.Contains(c)) && !dist.ContainsKey(c))
                {dist.Add(c,0);q.Enqueue(c);}
            while(q.Count>0)
            {
                IntVec3 at=q.Dequeue();int next=dist[at]+1;
                for(int d=0;d<4;d++)
                {
                    IntVec3 n=RHFObjectiveRouteV128.CardinalR044(at,d);
                    if(n==blocked || !passable.Contains(n) || (forbidden!=null && forbidden.Contains(n)) || dist.ContainsKey(n))continue;
                    dist.Add(n,next);q.Enqueue(n);
                }
            }
            return dist;
        }
        internal int Benefit(IntVec3 wall)
        {
            if(!interior.Contains(wall) || seeds.Contains(wall))return 0;
            var after=Gates(wall,out _);int score=0;
            foreach(IntVec3 gate in gates)if(!after.Contains(gate))score+=doors.Contains(gate)?100:10;
            if(score>0)return score;
            // A wall that cannot fully close an entrance can still genuinely
            // lengthen the shortest hostile approach. Demand an actual detour,
            // not mere proximity to a door or a decorative wall in a closet.
            int before=int.MaxValue;
            foreach(IntVec3 c in seeds)if(baseline.TryGetValue(c,out int n))before=Math.Min(before,n);
            if(before==int.MaxValue)return 0;
            var rerouted=Distances(exterior,wall,null);int later=int.MaxValue;
            foreach(IntVec3 c in seeds)if(rerouted.TryGetValue(c,out int distance))later=Math.Min(later,distance);
            return later==int.MaxValue?100:later-before>=2?Math.Min(50,(later-before)*5):0;
        }
    }
}
