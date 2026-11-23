// NJR P0: one stable, terrain-reachable evacuation edge for a whole convoy.
// No vanilla per-pawn best-exit recomputation while the escort is moving.
using System;
using RimWorld;
using Verse;
using Verse.AI;

namespace RaidersHaveFamilies
{
    internal static class NJRRescueExitS41
    {
        internal static bool Eligible(Map map,RHFOperation op,Pawn navigator,IntVec3 cell,Danger allowedDanger = Danger.Some)
        {
            if(map==null || navigator==null || !cell.IsValid ||
                !cell.InBounds(map) || !cell.Standable(map) ||
                cell.GetEdifice(map)!=null || cell.Roofed(map) ||
                (cell.x>1 && cell.z>1 &&
                 cell.x<map.Size.x-2 && cell.z<map.Size.z-2) ||
                !navigator.CanReach(cell,PathEndMode.OnCell,allowedDanger))return false;
            // A map edge reachable by one fighter but not the released
            // prisoner is an impossible extraction order, not a route.
            if(op?.Freed!=null)
            for(int i=0;i<op.Freed.Count;i++)
            {
                Pawn p=op.Freed[i];
                if(p==null || p.Dead || !p.Spawned || p.Map!=map || p.Downed)continue;
                if(!p.CanReach(cell,PathEndMode.OnCell,allowedDanger))return false;
            }
            return true;
        }

        // If there is no reachable edge, move toward the nearest physically
        // reachable unroofed boundary of the present region while another
        // fighter breaches a real obstructing wall. Never command a fake exit.
        internal static IntVec3 ReachableProgress(Map map,RHFOperation op,Pawn navigator)
        {
            if(map==null || navigator?.Map!=map)return IntVec3.Invalid;
            IntVec3 best=IntVec3.Invalid;
            int bestScore=int.MaxValue;
            int currentEdge=Math.Min(Math.Min(navigator.Position.x,
                map.Size.x-1-navigator.Position.x),Math.Min(navigator.Position.z,
                map.Size.z-1-navigator.Position.z));
            for(int x=0;x<map.Size.x;x++)for(int z=0;z<map.Size.z;z++)
            {
                IntVec3 cell=new IntVec3(x,0,z);
                int edge=Math.Min(Math.Min(x,map.Size.x-1-x),
                    Math.Min(z,map.Size.z-1-z));
                if(edge>=currentEdge || !cell.Standable(map) ||
                   cell.GetEdifice(map)!=null ||
                   !navigator.CanReach(cell,PathEndMode.OnCell,Danger.Deadly))continue;
                bool convoyCanReach=true;
                if(op?.Freed!=null)
                    foreach(Pawn captive in op.Freed)
                        if(captive!=null && captive.Spawned && !captive.Dead &&
                           !captive.Downed && captive.Map==map &&
                           !captive.CanReach(cell,PathEndMode.OnCell,Danger.Deadly))
                        {convoyCanReach=false;break;}
                if(!convoyCanReach)continue;
                int score=edge*100000+
                    (cell-navigator.Position).LengthHorizontalSquared+
                    (cell.Roofed(map)?5000:0);
                if(score<bestScore){best=cell;bestScore=score;}
            }
            return best;
        }

        internal static IntVec3 Choose(Map map,RHFOperation op,Pawn navigator)
        {
            IntVec3 safe=FindEdge(map,op,navigator,Danger.Some);
            return safe.IsValid ? safe : FindEdge(map,op,navigator,Danger.Deadly);
        }

        private static IntVec3 FindEdge(Map map,RHFOperation op,Pawn navigator,
            Danger allowedDanger)
        {
            if(map==null || navigator==null)return IntVec3.Invalid;
            IntVec3 best=IntVec3.Invalid;
            int bestScore=int.MaxValue;
            for(int side=0;side<4;side++)
            {
                int span=side<2?map.Size.z:map.Size.x;
                for(int offset=3;offset<span-3;offset++)
                {
                    IntVec3 c=side==0?new IntVec3(0,0,offset):
                        side==1?new IntVec3(map.Size.x-1,0,offset):
                        side==2?new IntVec3(offset,0,0):
                                new IntVec3(offset,0,map.Size.z-1);
                    if(!Eligible(map,op,navigator,c,allowedDanger))continue;
                    int danger=0;
                    // Score the *approach*, not only the map-edge endpoint.
                    // Every cell of the projected approach corridor is checked at phase transition.
                    int steps=Math.Max(Math.Abs(c.x-navigator.Position.x),Math.Abs(c.z-navigator.Position.z));
                    for(int sample=1;sample<=steps;sample++)
                    {
                        IntVec3 at=new IntVec3(
                            navigator.Position.x+(c.x-navigator.Position.x)*sample/steps,
                            0,navigator.Position.z+(c.z-navigator.Position.z)*sample/steps);
                        if(RHFObjectiveRouteV128.StagingExposedToTurret(map,at))
                            danger+=3500;
                    }
                    int score=(navigator.Position-c).LengthHorizontalSquared+danger;
                    if(score<bestScore){best=c;bestScore=score;}
                }
            }
            return best;
        }
    }
}
