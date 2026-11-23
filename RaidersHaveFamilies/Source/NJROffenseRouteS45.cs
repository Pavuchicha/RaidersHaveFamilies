// NJR R012: safe-first, whole-map route for a PHYSICALLY OBSERVED offense objective.
// This planner owns no jobs, causes no fake damage, and never patches vanilla pathfinding.
// It examines all reachable cells and all KNOWN guns; combat TPS is not artificially capped.
using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace RaidersHaveFamilies
{
    internal sealed class NJROffenseRouteS45
    {
        internal bool Found;
        internal bool Risky;
        internal Building FirstBarrier;
        internal IntVec3 Approach = IntVec3.Invalid;
        // Next A* cell. Mission jobs use this instead of long vanilla Goto
        // paths which can cross an unseen turret corridor.
        internal IntVec3 NextStep = IntVec3.Invalid;
        internal int ExposedCells;
        internal int StructuralCells;
        internal int TotalCost;
        internal string Reason = "no physical route";
    }

    internal static class NJROffensePathS45
    {
        private struct Node
        {
            internal int Id;
            internal int Priority;
            internal Node(int id,int priority){Id=id;Priority=priority;}
        }
        private static void Push(List<Node> heap,int id,int priority)
        {
            Node n=new Node(id,priority);
            heap.Add(n);
            int i=heap.Count-1;
            while(i>0)
            {
                int p=(i-1)/2;
                if(heap[p].Priority<=priority)break;
                heap[i]=heap[p];i=p;
            }
            heap[i]=n;
        }
        private static Node Pop(List<Node> heap)
        {
            Node first=heap[0],last=heap[heap.Count-1];
            heap.RemoveAt(heap.Count-1);
            if(heap.Count==0)return first;
            int i=0;
            while(true)
            {
                int c=i*2+1;
                if(c>=heap.Count)break;
                if(c+1<heap.Count && heap[c+1].Priority<heap[c].Priority)c++;
                if(heap[c].Priority>=last.Priority)break;
                heap[i]=heap[c];i=c;
            }
            heap[i]=last;
            return first;
        }
        private static int Heuristic(IntVec3 at,IntVec3 goal) =>
            (Math.Abs(at.x-goal.x)+Math.Abs(at.z-goal.z))*10;

        private static float GunRange(Building gun)
        {
            ThingDef weapon=gun?.def?.building?.turretGunDef;
            float range=0f;
            if(weapon?.Verbs!=null)
                foreach(VerbProperties verb in weapon.Verbs)
                    if(verb!=null && verb.range>range)range=verb.range;
            return range>0f ? range : 35f;
        }
        // The physical room beyond a closed door is assessed as if the door
        // were opened; present-time LoS alone hides guns behind that door.
        private static int Exposure(Map map,IntVec3 cell,List<Building> observed)
        {
            if(observed==null)return 0;
            int count=0;
            for(int i=0;i<observed.Count;i++)
            {
                Building gun=observed[i];
                if(!RHFCombatPlannerV125.IsTurret(gun) || !gun.Spawned ||
                   gun.Destroyed || gun.Map!=map)continue;
                float range=GunRange(gun);
                if((gun.Position-cell).LengthHorizontalSquared>range*range)continue;
                Room room=gun.Position.GetRoom(map);
                if((room!=null && room==cell.GetRoom(map)) ||
                   GenSight.LineOfSight(gun.Position,cell,map))count++;
            }
            return count;
        }
        private static int MovementCost(Map map,IntVec3 cell)
        {
            Building edifice=cell.GetEdifice(map);
            if(edifice!=null && !edifice.Destroyed)
            {
                // RimWorld GenGrid.Standable excludes all pass-through-only
                // edifices, even an already OPEN door. Walking through an open
                // door must not require the cell to be Standable.
                if(edifice is Building_Door door && door.Open)return 10;
                if(RHFCombatPlannerV125.RequiresBreach(edifice))
                {
                    // Doors are often preferable, but an unexposed wall route
                    // wins over a turret corridor regardless of wall count.
                    int work=RHFCombatPlannerV125.BreachWorkCost(edifice);
                    return 10+(edifice is Building_Door ? work : Math.Max(1000,work*6));
                }
                if(edifice.def?.passability==Traversability.Impassable)return -1;
            }
            if(!cell.Standable(map))return -1;
            return 10;
        }
        private static bool GoalReached(IntVec3 at,IntVec3 goal,bool touch)
        {
            return at==goal || (touch &&
                Math.Abs(at.x-goal.x)+Math.Abs(at.z-goal.z)==1);
        }
        private static NJROffenseRouteS45 Search(Pawn soldier,IntVec3 goal,
            List<Building> known,bool forbidGuns,bool touch)
        {
            Map map=soldier.Map;
            int width=map.Size.x,height=map.Size.z;
            int count=width*height;
            var result=new NJROffenseRouteS45();
            int origin=soldier.Position.z*width+soldier.Position.x;
            var cost=new int[count];
            var parents=new int[count];
            var exposures=new int[count];
            for(int i=0;i<count;i++){cost[i]=int.MaxValue;parents[i]=-1;exposures[i]=-1;}
            var heap=new List<Node>(128);
            cost[origin]=0;
            Push(heap,origin,Heuristic(soldier.Position,goal));
            int finish=-1;
            while(heap.Count>0)
            {
                Node node=Pop(heap);
                int at=node.Id;
                IntVec3 cell=new IntVec3(at%width,0,at/width);
                if(cost[at]==int.MaxValue ||
                   node.Priority!=cost[at]+Heuristic(cell,goal))continue;
                if(GoalReached(cell,goal,touch)){finish=at;break;}
                for(int side=0;side<4;side++)
                {
                    int x=cell.x+(side==0?1:side==1?-1:0);
                    int z=cell.z+(side==2?1:side==3?-1:0);
                    if(x<0 || z<0 || x>=width || z>=height)continue;
                    IntVec3 nextCell=new IntVec3(x,0,z);
                    int step=MovementCost(map,nextCell);
                    if(step<0)continue;
                    int next=z*width+x;
                    if(exposures[next]<0)
                        exposures[next]=Exposure(map,nextCell,known);
                    if(forbidGuns && exposures[next]>0)continue;
                    long penalty=(long)exposures[next]*12000L;
                    long candidate=(long)cost[at]+step+penalty;
                    if(candidate>=cost[next] || candidate>=int.MaxValue)continue;
                    int heuristic=Heuristic(nextCell,goal);
                    if(candidate>int.MaxValue-heuristic)continue;
                    cost[next]=(int)candidate;
                    parents[next]=at;
                    Push(heap,next,cost[next]+heuristic);
                }
            }
            if(finish<0)return result;
            var path=new List<IntVec3>();
            for(int i=finish;i>=0;i=parents[i])
            {
                path.Add(new IntVec3(i%width,0,i/width));
                if(i==origin)break;
                if(path.Count>count)return result;
            }
            if(path.Count==0 || path[path.Count-1]!=soldier.Position)return result;
            path.Reverse();
            result.NextStep=path.Count>1 ? path[1] : IntVec3.Invalid;
            result.Found=true;
            result.Risky=!forbidGuns;
            result.TotalCost=cost[finish];
            result.Reason=forbidGuns?"unexposed corridor":"unavoidable gunfire corridor";
            // Choose the first reachable wall and a *real* approach tile.
            // Do not send the whole team directly to the far destination:
            // vanilla Goto could shortcut a safe corridor through a kill zone.
            int barrierIndex=-1;
            for(int i=1;i<path.Count;i++)
            {
                Building wall=path[i].GetEdifice(map);
                if(RHFCombatPlannerV125.RequiresBreach(wall))
                {
                    result.StructuralCells++;
                    if(result.FirstBarrier==null)
                    {result.FirstBarrier=wall;barrierIndex=i;}
                }
                if(Exposure(map,path[i],known)>0)result.ExposedCells++;
            }
            int last=result.FirstBarrier==null ? path.Count-1 : barrierIndex-1;
            // Stage on a short, straight, physically reachable stretch. A new
            // plan can be issued on the next strategic pulse, including when
            // a barrier is destroyed or a defender changes the layout.
            int endpoint=Math.Min(last,Math.Min(path.Count-1,10));
            for(int i=endpoint;i>=1;i--)
            {
                if(!path[i].Standable(map) || path[i].GetEdifice(map)!=null ||
                   !soldier.CanReach(path[i],PathEndMode.OnCell,Danger.Deadly))continue;
                // A single Goto must never cut a corner of the costed A* path.
                // The next strategic replan will resume from the next bend.
                IntVec3 direction=path[1]-path[0];
                bool sameLeg=true;
                for(int k=2;k<=i;k++)
                    if(path[k]-path[k-1]!=direction)
                    {sameLeg=false;break;}
                if(!sameLeg)continue;
                // Only accept a direct leg if all sampled positions are safe
                // (or this was the explicit unavoidable-risk fallback).
                if(forbidGuns)
                {
                    bool unsafeLeg=false;
                    int dx=path[i].x-soldier.Position.x;
                    int dz=path[i].z-soldier.Position.z;
                    int steps=Math.Max(Math.Abs(dx),Math.Abs(dz));
                    for(int s=1;s<=steps;s++)
                    {
                        IntVec3 sample=new IntVec3(soldier.Position.x+dx*s/steps,
                            0,soldier.Position.z+dz*s/steps);
                        if(Exposure(map,sample,known)>0){unsafeLeg=true;break;}
                    }
                    if(unsafeLeg)continue;
                }
                result.Approach=path[i];
                break;
            }
            return result;
        }
        internal static NJROffenseRouteS45 Plan(Pawn soldier,IntVec3 goal,
            List<Building> known)
        {
            if(soldier?.Map==null || !soldier.Spawned ||
               !soldier.Position.InBounds(soldier.Map) ||
               !goal.IsValid || !goal.InBounds(soldier.Map))
                return new NJROffenseRouteS45();
            // An OPEN door still has an edifice object, but is passable.
            // Treat only non-standable cells or genuinely closed destructible
            // barriers as touch destinations. Otherwise reach the cell itself.
            Pawn occupant=goal.GetFirstPawn(soldier.Map);
            Building destinationEdifice=goal.GetEdifice(soldier.Map);
            bool openedDoor=destinationEdifice is Building_Door door && door.Open;
            bool touch=(!goal.Standable(soldier.Map) && !openedDoor) ||
                RHFCombatPlannerV125.RequiresBreach(destinationEdifice) ||
                (occupant!=null && occupant!=soldier);
            NJROffenseRouteS45 safe=Search(soldier,goal,known,true,touch);
            return safe.Found? safe:Search(soldier,goal,known,false,touch);
        }
    }
}
