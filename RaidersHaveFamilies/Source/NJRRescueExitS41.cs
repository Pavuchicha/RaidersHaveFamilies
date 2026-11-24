// NJR P0: one stable, terrain-reachable evacuation edge for a whole convoy.
// No vanilla per-pawn best-exit recomputation while the escort is moving.
using System;
using System.Runtime.CompilerServices;
using RimWorld;
using Verse;
using Verse.AI;

namespace RaidersHaveFamilies
{
    internal static class NJRRescueExitS41
    {
        // R025: when no ordinary ground exit is reachable, find the FIRST
        // CLOSED PLAYER DOOR on a real wall-free path to the map boundary.
        // This is an independent bounded search (not a guess at the nearest
        // wall/door in the rough direction of an arbitrary edge). A closed
        // door consumes a finite physical breach cost; walls, generators and
        // furniture cannot be promoted into an "escape obstacle".
        // A sealed prison may require several seconds of physical door work.
        // Do not repeat a 15k-node edge search every 180 ticks while that
        // same door is being attacked. Actual native exit eligibility is
        // still rechecked separately on every extraction decision.
        private sealed class DoorMemory
        {
            internal Building_Door Door;
            internal int NextSearch;
            internal IntVec3 Origin = IntVec3.Invalid;
        }
        private static readonly ConditionalWeakTable<RHFOperation,DoorMemory>
            DoorSearches=new ConditionalWeakTable<RHFOperation,DoorMemory>();

        private struct ExitNode
        {
            internal int Cell,Priority;
            internal ExitNode(int cell,int priority)
            {Cell=cell;Priority=priority;}
        }
        private static void PushExit(System.Collections.Generic.List<ExitNode> heap,
            ExitNode item)
        {
            heap.Add(item);
            int at=heap.Count-1;
            while(at>0)
            {
                int parent=(at-1)/2;
                if(heap[parent].Priority<=item.Priority)break;
                heap[at]=heap[parent];at=parent;
            }
            heap[at]=item;
        }
        private static ExitNode PopExit(System.Collections.Generic.List<ExitNode> heap)
        {
            ExitNode first=heap[0],last=heap[heap.Count-1];
            heap.RemoveAt(heap.Count-1);
            if(heap.Count==0)return first;
            int at=0;
            while(true)
            {
                int child=at*2+1;
                if(child>=heap.Count)break;
                if(child+1<heap.Count &&
                   heap[child+1].Priority<heap[child].Priority)child++;
                if(heap[child].Priority>=last.Priority)break;
                heap[at]=heap[child];at=child;
            }
            heap[at]=last;
            return first;
        }
        private static int EdgeDistance(Map map,int x,int z)
        {
            return Math.Min(Math.Min(x,map.Size.x-1-x),
                Math.Min(z,map.Size.z-1-z));
        }
        internal static Building_Door FirstBlockedExitDoor(Map map,
            RHFOperation op,Pawn navigator)
        {
            if(map==null || op==null || navigator?.Map!=map || !navigator.Spawned)
                return null;
            DoorMemory cache=DoorSearches.GetValue(op,_=>new DoorMemory());
            int now=Find.TickManager.TicksGame;
            if(now<cache.NextSearch && cache.Origin.IsValid &&
               (navigator.Position-cache.Origin).LengthHorizontalSquared<144)
            {
                if(cache.Door==null)return null;
                if(cache.Door.Spawned && !cache.Door.Destroyed &&
                   RHFCombatPlannerV125.RequiresBreach(cache.Door) &&
                   navigator.CanReach(cache.Door.Position,PathEndMode.Touch,Danger.Deadly))
                    return cache.Door;
            }
            int width=map.Size.x,height=map.Size.z;
            int count=width*height;
            if(count<=0 || count>120000)return null;
            int start=navigator.Position.z*width+navigator.Position.x;
            int[] costs=new int[count],previous=new int[count];
            for(int i=0;i<count;i++){costs[i]=int.MaxValue;previous[i]=-1;}
            var queue=new System.Collections.Generic.List<ExitNode>(128);
            costs[start]=0;
            PushExit(queue,new ExitNode(start,EdgeDistance(map,
                navigator.Position.x,navigator.Position.z)*10));
            int reached=-1,expanded=0;
            while(queue.Count>0 && expanded++<15000)
            {
                ExitNode item=PopExit(queue);
                int cell=item.Cell,x=cell%width,z=cell/width;
                if(item.Priority!=costs[cell]+EdgeDistance(map,x,z)*10)continue;
                IntVec3 pos=new IntVec3(x,0,z);
                if(EdgeDistance(map,x,z)<=1 && pos.Standable(map) &&
                   pos.GetEdifice(map)==null && !pos.Roofed(map))
                {reached=cell;break;}
                for(int dir=0;dir<4;dir++)
                {
                    int nx=x+(dir==0?1:dir==1?-1:0);
                    int nz=z+(dir==2?1:dir==3?-1:0);
                    if(nx<0 || nx>=width || nz<0 || nz>=height)continue;
                    IntVec3 nextPos=new IntVec3(nx,0,nz);
                    Building block=nextPos.GetEdifice(map);
                    int step=10;
                    if(block is Building_Door door &&
                       RHFCombatPlannerV125.RequiresBreach(door))
                        step+=1000+RHFCombatPlannerV125.BreachWorkCost(door);
                    else if(block!=null &&
                        block.def?.passability==Traversability.Impassable)
                        continue;
                    else if(!nextPos.Standable(map))continue;
                    int next=nz*width+nx;
                    if(costs[cell]>int.MaxValue-step ||
                       costs[cell]+step>=costs[next])continue;
                    costs[next]=costs[cell]+step;
                    previous[next]=cell;
                    PushExit(queue,new ExitNode(next,costs[next]+
                        EdgeDistance(map,nx,nz)*10));
                }
            }
            cache.Origin=navigator.Position;
            cache.NextSearch=now+900;
            cache.Door=null;
            if(reached<0)return null; // no proof; no random demolition
            // Reconstruct backwards; the first closed door on the forward
            // path is the last eligible door encountered in reverse order.
            Building_Door first=null;
            for(int at=reached;at>=0 && at!=start;at=previous[at])
            {
                IntVec3 cell=new IntVec3(at%width,0,at/width);
                if(cell.GetEdifice(map) is Building_Door door &&
                   RHFCombatPlannerV125.RequiresBreach(door))first=door;
                if(previous[at]<0)break;
            }
            if(first!=null && !navigator.CanReach(first.Position,
                PathEndMode.Touch,Danger.Deadly))return null;
            cache.Door=first;
            return first;
        }

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
            // Only choose a near-term advance position. Re-evaluate after
            // the convoy moves, instead of probing the whole map every pulse.
            for(int x=Math.Max(0,navigator.Position.x-24);
                x<=Math.Min(map.Size.x-1,navigator.Position.x+24);x++)
            for(int z=Math.Max(0,navigator.Position.z-24);
                z<=Math.Min(map.Size.z-1,navigator.Position.z+24);z++)
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
