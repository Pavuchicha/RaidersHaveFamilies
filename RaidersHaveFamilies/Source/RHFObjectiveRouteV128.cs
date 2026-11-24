// RHF 0.12.8: one actual costed, threat-aware corridor for walking AND breaching.
// Unlike the legacy route estimator, the resulting chain is followed by troops.
// Calculated at most once per 900 ticks per rescue operation (or when invalidated).
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RaidersHaveFamilies
{
    internal sealed class RHFRoutePlanV128
    {
        internal bool Valid;
        // A capped search is UNKNOWN, not proof that the wall-free route fails.
        // Never escalate to demolition after a CPU limit interrupted A*.
        internal bool BudgetExceeded;
        internal bool SearchPending;
        // True when the chosen complete route crosses a real turret arc.
        // Such a route must advance while the rescue wing fights back.
        internal bool RiskFallback;
        internal int Tick;
        internal Pawn Leader;
        internal Pawn Captive;
        internal IntVec3 From;
        internal IntVec3 To;
        internal List<IntVec3> Cells = new List<IntVec3>();
        internal Thing FirstBarrier;
        internal int FirstBarrierIndex = -1;
        // Used to assign suppression around the FIRST exposed approach leg.
        // Exposure alone does not cancel an otherwise complete route.
        internal int FirstTurretExposureIndex = -1;
        internal int ExposedTurretCells;
        internal int TotalCost;
        internal int TotalStructuralHP;
        internal int DangerCost;
        internal int Walls;
        internal int Doors;
        internal int Expanded;
        internal string Explanation = "план не построен";
    }

    internal sealed class RHFRouteStoreV128
    {
        internal RHFRoutePlanV128 Plan;
        // Ephemeral per-operation layout guard, never serialized to a save.
        internal Map LayoutMap;
        internal int TurretLayoutSignature;
        internal int NextLayoutCheckTick;
        // A corridor may nominate a wall that no active soldier can touch.
        // Temporarily forbid THAT precise edifice and search a different real
        // corridor instead of assigning Hold to the entire rescue squad.
        internal readonly Dictionary<Thing,int> UnworkableBarriers =
            new Dictionary<Thing,int>();
    }

    [StaticConstructorOnStartup]
    internal static class RHFObjectiveRouteV128
    {
        static RHFObjectiveRouteV128()
        {
            Log.Message("[NJR][R028] Resumable reverse route and rescue job/lord repair loaded.");
        }
        private const int ReplanTicks = 900;
        // Finite per-decision CPU budget. A failed tactical search must
        // never lock a RimWorld frame while inspecting every wall/turret tile.
        private const int MaxRouteExpansionsR024 = 3000; // per pulse, NOT total search limit
        // R025: give a large structural penalty to destroying an intact WALL.
        // Several doors (even under modest defensive fire) are normally
        // preferable to tearing through a room boundary; danger still matters
        // on the WHOLE route, and impossible door paths are never fabricated.
        // R024: ONE weighted whole-route search, as in the user-supplied
        // 0.14.0-beta2 baseline. Never force a zero-turret search that can
        // prefer three demolished walls over one door and a short exposed lane.
        // Threat cost is paid for each ACTUALLY exposed cell, so a prolonged
        // crossfire remains much worse than a short covered approach.
        // ONE capped search replaces three expensive whole-map passes.
        private const int ExposedCost = 300;
        private const int WallBreachBaseR024 = 2400;
        private const int DoorBreachBaseR024 = 180;
        private static readonly ConditionalWeakTable<RHFOperation, RHFRouteStoreV128> Cache =
            new ConditionalWeakTable<RHFOperation, RHFRouteStoreV128>();

        private struct HeapItem
        {
            internal int Id;
            internal int Priority;
            internal HeapItem(int id, int priority) { Id=id; Priority=priority; }
        }
        private struct Emitter
        {
            internal IntVec3 Origin;
            internal float RangeSquared;
            internal int Importance;
            internal float CorridorDistance;
            internal Emitter(IntVec3 origin,float distance,int importance,float corridorDistance)
            { Origin=origin; RangeSquared=distance*distance;
              Importance=importance; CorridorDistance=corridorDistance; }
        }
        private static void Push(List<HeapItem> heap,int id,int priority)
        {
            HeapItem item=new HeapItem(id,priority);
            heap.Add(item);
            int at=heap.Count-1;
            while(at>0)
            {
                int parent=(at-1)/2;
                if(heap[parent].Priority<=priority)break;
                heap[at]=heap[parent];at=parent;
            }
            heap[at]=item;
        }
        private static HeapItem Pop(List<HeapItem> heap)
        {
            HeapItem first=heap[0],last=heap[heap.Count-1];
            heap.RemoveAt(heap.Count-1);
            if(heap.Count==0)return first;
            int at=0;
            while(true)
            {
                int child=at*2+1;
                if(child>=heap.Count)break;
                if(child+1<heap.Count && heap[child+1].Priority<heap[child].Priority)child++;
                if(heap[child].Priority>=last.Priority)break;
                heap[at]=heap[child];at=child;
            }
            heap[at]=last;
            return first;
        }
        private static int Heuristic(int x,int z,IntVec3 target)
        { return (Math.Abs(x-target.x)+Math.Abs(z-target.z))*10; }
        private static float DistanceToCorridor(IntVec3 position,IntVec3 start,IntVec3 end)
        {
            float dx=end.x-start.x,dz=end.z-start.z;
            float denominator=dx*dx+dz*dz;
            float t=denominator>0f ?
                Mathf.Clamp01(((position.x-start.x)*dx+(position.z-start.z)*dz)/denominator) : 0f;
            float xx=start.x+t*dx-position.x;
            float zz=start.z+t*dz-position.z;
            return xx*xx+zz*zz;
        }
        private static void AddEmitter(List<Emitter> threats,Emitter incoming)
        {
            // Every real mobile threat contributes. No nearest-N eviction.
            threats.Add(incoming);
        }

        private static float WeaponRange(ThingDef weapon, float fallback)
        {
            if(weapon?.Verbs==null)return fallback;
            float range=0f;
            foreach(VerbProperties verb in weapon.Verbs)
                if(verb!=null && verb.range>range)range=verb.range;
            return range>0f ? range : fallback;
        }
        private static bool IsTurret(Building b)
        {
            if(b==null || b.Destroyed || !b.Spawned || b.Faction!=Faction.OfPlayer)return false;
            // Prefer built-in combat APIs; optional modded turret class fallback.
            if(b is Building_TurretGun || b.def?.building?.turretGunDef!=null)return true;
            return b.GetType().Name.IndexOf("Turret",StringComparison.OrdinalIgnoreCase)>=0;
        }
        private static List<Emitter> Threats(Map map,IntVec3 a,IntVec3 b)
        {
            // A tactical decision may be expensive; unlike idle diplomatic
            // housekeeping, it must NOT hide the 41st turret. Include every
            // actual player-owned turret, irrespective of list order or the
            // direct-line corridor. The fallback route also needs to know
            // turrets guarding a seemingly attractive far detour.
            var threat=new List<Emitter>(96);
            foreach(Building building in map.listerBuildings.allBuildingsColonist)
            {
                if(!IsTurret(building))continue;
                ThingDef gun=building.def?.building?.turretGunDef;
                float range=WeaponRange(gun,30f);
                threat.Add(new Emitter(building.Position,range,2,
                    DistanceToCorridor(building.Position,a,b)));
            }
            // Dynamic shooters are counted in full, just like static turrets.
            var shooters=new List<Emitter>();
            foreach(Pawn pawn in map.mapPawns.FreeColonistsSpawned)
            {
                if(pawn==null || pawn.Dead || pawn.Downed || !pawn.Spawned ||
                   pawn.equipment?.Primary?.def?.IsRangedWeapon!=true)continue;
                float range=WeaponRange(pawn.equipment.Primary.def,24f);
                AddEmitter(shooters,new Emitter(pawn.Position,range,1,
                    DistanceToCorridor(pawn.Position,a,b)));
            }
            threat.AddRange(shooters);
            return threat;
        }
        private static int Exposure(Map map,IntVec3 cell,List<Emitter> threats)
        {
            int cost=0;
            for(int i=0;i<threats.Count;i++)
            {
                Emitter t=threats[i];
                int dist=(cell-t.Origin).LengthHorizontalSquared;
                if(dist>t.RangeSquared || dist<=1)continue;
                // A closed door can conceal a turret until the first soldier
                // breaks it. Its adjacent ROOM is still a dangerous corridor.
                Room gunRoom=t.Importance>=2?t.Origin.GetRoom(map):null;
                bool sameGunRoom=gunRoom!=null && !gunRoom.PsychologicallyOutdoors &&
                    gunRoom==cell.GetRoom(map);
                if(!sameGunRoom && !GenSight.LineOfSight(t.Origin,cell,map))continue;
                cost+=ExposedCost*t.Importance;
                if(dist<t.RangeSquared*0.32f)cost+=180*t.Importance;
                if(cost>=1600)return 1600;
            }
            return cost;
        }
        // This is an objective safety gate, not a synthetic movement cost.
        // Only actual, visible stationary guns cause a hard advance hold;
        // human defenders are handled by the tactical combat wing.
        private static bool ExposedToTurret(Map map,IntVec3 cell,List<Emitter> threats)
        {
            for(int i=0;i<threats.Count;i++)
            {
                Emitter source=threats[i];
                if(source.Importance<2)continue;
                int distance=(cell-source.Origin).LengthHorizontalSquared;
                if(distance>source.RangeSquared || distance<=1)continue;
                Room turretRoom=source.Origin.GetRoom(map);
                if(turretRoom!=null && !turretRoom.PsychologicallyOutdoors &&
                   turretRoom==cell.GetRoom(map))return true;
                if(GenSight.LineOfSight(source.Origin,cell,map))return true;
            }
            return false;
        }
        // R024: demolition authorization is limited to actual doors and
        // structural WALL/GATE barriers on the proven objective corridor.
        // A generator, battery, workbench or other impassable machinery is
        // NEVER made a rescue target just because it is an Edifice.
        internal static bool IsRescueStructuralBarrier(Thing thing)
        {
            if(!(thing is Building building) ||
               !RHFCombatPlannerV125.RequiresBreach(thing))return false;
            if(building is Building_Door)return true;
            if(building.def?.passability!=Traversability.Impassable ||
               !building.def.IsEdifice())return false;
            string name=building.def.defName??string.Empty;
            return name.IndexOf("Wall",StringComparison.OrdinalIgnoreCase)>=0 ||
                name.IndexOf("Gate",StringComparison.OrdinalIgnoreCase)>=0 ||
                name.IndexOf("Barricade",StringComparison.OrdinalIgnoreCase)>=0;
        }

        private static int Step(Map map,IntVec3 cell, out Thing obstacle)
        {
            obstacle=null;
            Building building=cell.GetEdifice(map);
            if(building!=null && !building.Destroyed)
            {
                if(building is Building_Door openDoor && openDoor.Open)return 10;
                if(IsRescueStructuralBarrier(building))
                {
                    obstacle=building;
                    int work=RHFCombatPlannerV125.BreachWorkCost(building);
                    // Evaluate COMPLETE demolition work, not nearest-wall
                    // distance. Doors are cheaper than physically solid walls.
                    return 10+(building is Building_Door ?
                        DoorBreachBaseR024+work : WallBreachBaseR024+work*3);
                }
                if(building.def?.passability==Traversability.Impassable)return -1;
            }
            if(!cell.Standable(map))return -1;
            return 10;
        }
        // R021: a single multi-source A* represents the WHOLE available assault
        // wing. No selected pawn owns the mission's path or demolition target.
        // All active start positions are genuine standable map cells.
        // Search backwards from the hostage. A sealed cell is a small region,
        // not an invitation to exhaust the open exterior before considering doors.
        // Keep the frontier across strategy pulses; a CPU slice is not failure.
        private sealed class ReverseSearchR028
        {
            internal Map Map;
            internal IntVec3 Goal;
            internal List<IntVec3> Origins;
            internal HashSet<IntVec3> Roots;
            internal List<Emitter> Threats;
            internal HashSet<Thing> Rejected;
            internal int Width, Height, Expanded;
            internal int[] Costs, Parents, Hazard, Steps, Heuristics;
            internal List<HeapItem> Heap = new List<HeapItem>(128);
        }
        private static readonly ConditionalWeakTable<RHFRouteStoreV128,ReverseSearchR028> Searches =
            new ConditionalWeakTable<RHFRouteStoreV128,ReverseSearchR028>();

        private static int RootHeuristic(ReverseSearchR028 search,int id,int x,int z)
        {
            int known=search.Heuristics[id];
            if(known>=0)return known;
            int h=int.MaxValue;
            foreach(IntVec3 source in search.Origins)
                h=Math.Min(h,Heuristic(x,z,source));
            return search.Heuristics[id]=h;
        }

        private static bool SameGoal(Map map,IntVec3 a,IntVec3 b)
        {
            if(a==b)return true;
            return a.InBounds(map) && b.InBounds(map) &&
                (a-b).LengthHorizontalSquared<=36 && a.GetRoom(map)!=null &&
                a.GetRoom(map)==b.GetRoom(map);
        }

        private static bool PlanBounded(Map map,IList<IntVec3> origins,Pawn captive,
            RHFRoutePlanV128 plan,int margin,HashSet<Thing> rejectedBarriers,
            RHFRouteStoreV128 holder)
        {
            if(origins==null || origins.Count==0 || map==null || captive==null)
                return false;
            ReverseSearchR028 search;
            if(!Searches.TryGetValue(holder,out search) || search.Map!=map ||
               !SameGoal(map,search.Goal,captive.Position) ||
               !search.Rejected.SetEquals(rejectedBarriers))
            {
                Searches.Remove(holder);
                search=new ReverseSearchR028 {Map=map,Goal=captive.Position,
                    Origins=new List<IntVec3>(origins),Roots=new HashSet<IntVec3>(origins),
                    Rejected=new HashSet<Thing>(rejectedBarriers),
                    Width=map.Size.x,Height=map.Size.z,
                    Threats=Threats(map,origins[0],captive.Position)};
                int size=search.Width*search.Height;
                search.Costs=new int[size];search.Parents=new int[size];
                search.Hazard=new int[size];search.Steps=new int[size];search.Heuristics=new int[size];
                for(int i=0;i<size;i++)
                {search.Costs[i]=int.MaxValue;search.Parents[i]=-1;search.Hazard[i]=-1;
                 search.Steps[i]=int.MinValue;search.Heuristics[i]=-1;}
                int goalId=search.Goal.z*search.Width+search.Goal.x;
                search.Costs[goalId]=0;
                Push(search.Heap,goalId,RootHeuristic(search,goalId,search.Goal.x,search.Goal.z));
                Searches.Add(holder,search);
            }
            int width=search.Width,height=search.Height,count=width*height;
            int minX=0,minZ=0;
            int[] costs=search.Costs,parents=search.Parents,hazard=search.Hazard;
            List<Emitter> threats=search.Threats;
            List<HeapItem> heap=search.Heap;
            int expanded=0,finish=-1;
            while(heap.Count>0 && expanded<MaxRouteExpansionsR024)
            {
                HeapItem current=Pop(heap);
                int at=current.Id,cx=at%width,cz=at/width;
                if(current.Priority!=costs[at]+RootHeuristic(search,at,cx,cz))continue;
                expanded++;search.Expanded++;
                IntVec3 cell=new IntVec3(cx,0,cz);
                if(search.Roots.Contains(cell)){finish=at;break;}
                Thing obstacle;
                // Reverse edge u<-v pays the FORWARD cost of entering u.
                int step=search.Steps[at];
                if(step==int.MinValue)
                {
                    step=Step(map,cell,out obstacle);
                    if(obstacle!=null && rejectedBarriers.Contains(obstacle))step=-1;
                    search.Steps[at]=step;
                }
                if(step<0)continue;
                if(hazard[at]<0)hazard[at]=Exposure(map,cell,threats);
                step+=hazard[at];
                for(int direction=0;direction<4;direction++)
                {
                    int nx=cx+(direction==0?1:direction==1?-1:0);
                    int nz=cz+(direction==2?1:direction==3?-1:0);
                    if(nx<0 || nx>=width || nz<0 || nz>=height)continue;
                    int next=nz*width+nx;
                    if(search.Steps[next]==int.MinValue)
                    {
                        int entry=Step(map,new IntVec3(nx,0,nz),out obstacle);
                        if(obstacle!=null && rejectedBarriers.Contains(obstacle))entry=-1;
                        search.Steps[next]=entry;
                    }
                    if(search.Steps[next]<0 || costs[at]>int.MaxValue-step)continue;
                    int total=costs[at]+step;
                    if(total>=costs[next])continue;
                    costs[next]=total;parents[next]=at;
                    Push(heap,next,total+RootHeuristic(search,next,nx,nz));
                }
            }
            plan.Expanded=search.Expanded;
            plan.To=search.Goal;
            if(finish<0)
            {
                plan.SearchPending=heap.Count>0;
                plan.BudgetExceeded=plan.SearchPending;
                plan.Explanation=plan.SearchPending ?
                    "R028 route search pending; saved frontier; demolition forbidden" :
                    "вся достижимая область проверена: маршрута нет";
                if(!plan.SearchPending)Searches.Remove(holder);
                return false;
            }
            var reverse=new List<IntVec3>(256);
            for(int cursor=finish;cursor>=0 && reverse.Count<count;cursor=parents[cursor])
            {
                IntVec3 cell=new IntVec3(cursor%width,0,cursor/width);
                reverse.Add(cell);
                if(cell==search.Goal)break;
            }
            Searches.Remove(holder);
            if(reverse.Count<2 || reverse[reverse.Count-1]!=search.Goal)
            {
                plan.Explanation="ошибка восстановления полного маршрута";
                return false;
            }
            // Revalidate against LIVE terrain after a multi-pulse search.
            // Do not grant a breach from a stale search after a door closes or a wall changes.
            for(int i=1;i<reverse.Count;i++)
            {
                Thing live;
                int liveCost=Step(map,reverse[i],out live);
                int index=reverse[i].z*width+reverse[i].x;
                if(liveCost<0 || (live!=null && rejectedBarriers.Contains(live)) ||
                   (search.Steps[index]!=int.MinValue && liveCost>search.Steps[index]))
                {
                    plan.Explanation="R028 route invalidated by changed terrain; retry";
                    return false;
                }
            }
            plan.From=reverse[0]; // geometric route root, NOT a commander
            plan.Cells=reverse;
            plan.TotalCost=costs[finish];
            plan.FirstBarrier=null;
            plan.FirstBarrierIndex=-1;
            plan.Walls=plan.Doors=plan.TotalStructuralHP=plan.DangerCost=0;
            plan.FirstTurretExposureIndex=-1;
            plan.ExposedTurretCells=0;
            for(int i=1;i<reverse.Count;i++)
            {
                IntVec3 c=reverse[i];
                Thing obstacle;
                Step(map,c,out obstacle);
                if(obstacle!=null)
                {
                    plan.TotalStructuralHP+=Math.Max(0,obstacle.HitPoints);
                    if(obstacle is Building_Door)plan.Doors++;else plan.Walls++;
                    if(plan.FirstBarrier==null)
                    {plan.FirstBarrier=obstacle;plan.FirstBarrierIndex=i;}
                }
                int index=(c.z-minZ)*width+c.x-minX;
                if(hazard[index]>=0)plan.DangerCost+=hazard[index];
                if(ExposedToTurret(map,c,threats))
                {
                    plan.ExposedTurretCells++;
                    if(plan.FirstTurretExposureIndex<0)
                        plan.FirstTurretExposureIndex=i;
                }
            }
            plan.Valid=true;
            // R024: an exposed leg is an explicitly accepted risk. Without
            // this flag old SafeAdvanceLimit() stopped ALL rescuers before
            // a fully turret-covered perimeter even after finding a route.
            plan.RiskFallback=plan.FirstTurretExposureIndex>=0;
            plan.Explanation="WEIGHTED-R024"+
                " маршрут: "+plan.Cells.Count+" клеток; " +
                "дверей="+plan.Doors+", стен="+plan.Walls+
                (plan.FirstTurretExposureIndex>=0 ?
                    "; обнаружен сектор огня турели — требуется прикрытие" :
                    "; сектор огня турелей не пересекается");
            return true;
        }

        // Inspired by map-scoped avoidance-grid invalidation in community
        // combat AI: a built/destroyed player turret invalidates any old
        // corridor. The fingerprint is sampled only once every 120 game ticks
        // per active rescue operation, never in passive colony housekeeping.
        private static int TurretLayout(Map map)
        {
            unchecked
            {
                int signature=17,count=0;
                foreach(Building turret in map.listerBuildings.allBuildingsColonist)
                {
                    if(!IsTurret(turret))continue;
                    count++;
                    signature=signature*31 + turret.Position.GetHashCode();
                    signature=signature*31 + turret.GetHashCode();
                }
                return signature*31+count;
            }
        }

        internal static RHFRoutePlanV128 ResolveShared(RHFOperation op,Map map,
            List<Pawn> squad,HashSet<Pawn> reserved,Pawn captive,int now)
        {
            var starts=new List<IntVec3>();
            if(map!=null && squad!=null)
                for(int i=0;i<squad.Count;i++)
                {
                    Pawn p=squad[i];
                    if(p==null || !p.Spawned || p.Dead || p.Downed || p.InMentalState ||
                       p.Map!=map || (reserved!=null && reserved.Contains(p)))continue;
                    if(p.Position.Standable(map) && !starts.Contains(p.Position))
                        starts.Add(p.Position);
                }
            return ResolveFromSources(op,map,starts,captive,now);
        }

        // Compatibility for legacy (inactive) callers. No pawn is given
        // command authority by being a source to the geometric solver.
        internal static RHFRoutePlanV128 Resolve(RHFOperation op,Map map,
            Pawn leader,Pawn captive,int now)
        {
            var sources=new List<IntVec3>();
            if(leader!=null && leader.Spawned)sources.Add(leader.Position);
            return ResolveFromSources(op,map,sources,captive,now);
        }

        // A breach which no member can physically execute is NOT a route.
        // Only this single obstacle is excluded for 1800 ticks; other doors
        // and walls remain costed by the normal full-route planner.
        internal static void RejectUnworkableBarrier(RHFOperation op,Thing barrier,int now)
        {
            if(op==null || barrier==null)return;
            RHFRouteStoreV128 holder=Cache.GetValue(op,_=>new RHFRouteStoreV128());
            // Several sequential obstructions can be unworkable. A single
            // rejected-wall slot oscillated between A/B every 180 ticks.
            if(!holder.UnworkableBarriers.ContainsKey(barrier) &&
               holder.UnworkableBarriers.Count>=8)
            {
                Thing oldest=null;int earliest=int.MaxValue;
                foreach(KeyValuePair<Thing,int> pair in holder.UnworkableBarriers)
                    if(pair.Value<earliest){earliest=pair.Value;oldest=pair.Key;}
                if(oldest!=null)holder.UnworkableBarriers.Remove(oldest);
            }
            holder.UnworkableBarriers[barrier]=now+1800;
            holder.Plan=null;
            Searches.Remove(holder);
            Log.WarningOnce("[NJR][R026] Unreachable breach at "+barrier.Position+
                "; retrying weighted route without this obstacle",
                barrier.thingIDNumber ^ 260026);
        }

        private static RHFRoutePlanV128 ResolveFromSources(RHFOperation op,Map map,
            List<IntVec3> origins,Pawn captive,int now)
        {
            if(op==null || map==null || origins==null || origins.Count==0 ||
               captive==null || !captive.Spawned || captive.Map!=map)
                return new RHFRoutePlanV128{Explanation="неверная цель или карта"};
            RHFRouteStoreV128 holder=Cache.GetValue(op,_=>new RHFRouteStoreV128());
            bool layoutChanged=false;
            if(holder.LayoutMap!=map || now>=holder.NextLayoutCheckTick)
            {
                int layout=TurretLayout(map);
                layoutChanged=holder.Plan!=null &&
                    (holder.LayoutMap!=map || holder.TurretLayoutSignature!=layout);
                holder.LayoutMap=map;
                holder.TurretLayoutSignature=layout;
                holder.NextLayoutCheckTick=now+120;
            }
            if(holder.UnworkableBarriers.Count>0)
            {
                var expired=new List<Thing>();
                foreach(KeyValuePair<Thing,int> pair in holder.UnworkableBarriers)
                    if(pair.Key==null || pair.Key.Destroyed ||
                       !pair.Key.Spawned || now>=pair.Value)expired.Add(pair.Key);
                foreach(Thing old in expired)holder.UnworkableBarriers.Remove(old);
                if(expired.Count>0){holder.Plan=null;Searches.Remove(holder);}
            }
            if(layoutChanged)Searches.Remove(holder);
            RHFRoutePlanV128 prior=holder.Plan;
            // A corridor with no actual forward progress must expire sooner;
            // otherwise idle pawns can retain a bad initial approach for 900t.
            bool stalled=prior!=null && now-prior.Tick>=300 &&
                now-Math.Max(op.LastProgressTick,op.PhaseStartTick)>600;
            // An intact authorized barrier is a committed entry decision.
            // Replan on destruction, target/threat change or hard timeout.
            bool committedBarrier=prior!=null && prior.FirstBarrier!=null &&
                prior.FirstBarrier.Spawned &&
                RHFCombatPlannerV125.RequiresBreach(prior.FirstBarrier) &&
                now-prior.Tick<2700;
            if(prior!=null && !prior.SearchPending && (!stalled || committedBarrier) && !layoutChanged && prior.Captive==captive &&
               // Route permission belongs to the objective, not a fighter.
               // If no member can reach the cached corridor, replan below.
               SameGoal(map,prior.To,captive.Position) &&
               now-prior.Tick<(prior.Valid ?
                   (committedBarrier?2700:ReplanTicks) : 300) &&
               (prior.FirstBarrier==null ||
                (prior.FirstBarrier.Spawned &&
                 RHFCombatPlannerV125.RequiresBreach(prior.FirstBarrier))) &&
               (!prior.Valid || (prior.Cells.Count>0 &&
                                   NearAnyOrigin(prior,origins,144))))
                return prior;
            var fresh=new RHFRoutePlanV128 {Tick=now,Captive=captive,
                From=origins[0],To=captive.Position};
            // Exactly one full-map multi-source weighted A* pass. In an
            // entirely turret-covered colony it still finds a route, pays
            // for the real exposure, and allows actual combat on approach.
            PlanBounded(map,origins,captive,fresh,
                Math.Max(map.Size.x,map.Size.z),
                new HashSet<Thing>(holder.UnworkableBarriers.Keys),holder);
            if(!fresh.Valid && RHFMod.Settings?.VerboseLogging==true)
                Log.Warning("[RHF][RouteV128] " +fresh.Explanation+
                    " sources="+origins.Count+" target="+captive.Position);
            holder.Plan=fresh;
            return fresh;
        }
        private static bool NearAnyOrigin(RHFRoutePlanV128 plan,
            List<IntVec3> origins,int tolerance)
        {
            for(int i=0;i<origins.Count;i++)
                if(NearPath(plan,origins[i],tolerance))return true;
            return false;
        }
        private static bool NearPath(RHFRoutePlanV128 plan,IntVec3 point,int tolerance)
        {
            int best=int.MaxValue;
            for(int i=0;i<plan.Cells.Count;i++)
            {
                int d=(plan.Cells[i]-point).LengthHorizontalSquared;
                if(d<best)best=d;
                if(best<=tolerance)return true;
            }
            return false;
        }
        // Allows legacy fallback only when the mission/target is not an RHF rescue.
        // A valid plan and an explicitly unsuccessful plan both disable random
        // wall-picking: a failed route must be diagnosed, not misrepresented.
        internal static bool TryObjective(RHFOperation op,Map map,Pawn leader,
            Pawn captive,int now,out RHFRoutePlanV128 route)
        {
            route=null;
            if(op==null || map==null || leader==null || captive==null ||
               op.Type!=(int)RHFOperationType.Rescue)return false;
            route=Resolve(op,map,leader,captive,now);
            return true;
        }
        internal static bool IsOnCorridor(RHFRoutePlanV128 route,IntVec3 cell)
        {
            if(route==null || !route.Valid || !cell.IsValid)return false;
            for(int i=0;i<route.Cells.Count;i++)
                if(route.Cells[i]==cell)return true;
            return false;
        }
        internal static int SafeAdvanceLimit(RHFRoutePlanV128 route)
        {
            if(route==null || !route.Valid)return -1;
            int end=route.Cells.Count-1;
            if(route.FirstBarrierIndex>=0)
                end=Math.Min(end,route.FirstBarrierIndex-1);
            // In a genuinely SAFE route the exposure guard is a hard stop.
            // In a RISK-FALLBACK route every safe possibility has already been
            // exhausted. Keep the physical barrier stop, but allow fighters to
            // traverse the selected LEAST-EXPOSED corridor while they engage guns.
            if(route.FirstTurretExposureIndex>=0 && !route.RiskFallback)
                end=Math.Min(end,route.FirstTurretExposureIndex-1);
            return end;
        }
        // This must be checked before allowing an already-active Goto to continue.
        // Otherwise old jobs can pass the new fire-exposure gate after replanning.
        internal static bool IsAllowedWaypoint(RHFRoutePlanV128 route,IntVec3 cell)
        {
            if(route==null || !route.Valid || !cell.IsValid)return false;
            int end=SafeAdvanceLimit(route);
            for(int i=0;i<=end;i++)
            {
                if(route.Cells[i]==cell)return true;
                // Up to seven dispersed open-ground lanes. Never allow this
                // lateral tolerance at a breach or across turret exposure.
                // The waypoint issuer separately checks terrain, LoS and fire.
                if(route.FirstTurretExposureIndex<0 && i>=3 && i<end-3 &&
                   (route.Cells[i]-cell).LengthHorizontalSquared<=9)
                    return true;
            }
            return false;
        }
        // The costed centerline can be safe while an offset lane is inside
        // a turret arc. Check only lateral candidates (bounded per Goto), with
        // several samples along the offset leg, before spreading the unit.
        private static bool LateralExposed(Map map,IntVec3 from,IntVec3 to)
        {
            if(map==null)return true;
            foreach(Building turret in map.listerBuildings.allBuildingsColonist)
            {
                if(!IsTurret(turret))continue;
                float range=WeaponRange(turret.def?.building?.turretGunDef,30f);
                float r2=range*range;
                for(int k=1;k<=4;k++)
                {
                    IntVec3 sample=new IntVec3(from.x+(to.x-from.x)*k/4,0,
                        from.z+(to.z-from.z)*k/4);
                    if((sample-turret.Position).LengthHorizontalSquared>r2)continue;
                    if(GenSight.LineOfSight(turret.Position,sample,map))return true;
                }
            }
            return false;
        }

        // Public to the mission planner, read-only and bounded to RHF ops.
        internal static bool StagingExposedToTurret(Map map,IntVec3 cell)
        {return LateralExposed(map,cell,cell);}

        internal static IntVec3 Waypoint(RHFRoutePlanV128 route,Pawn soldier,Map map)
        {
            if(route==null || !route.Valid || soldier==null || soldier.Map!=map ||
               route.Cells.Count<2)return IntVec3.Invalid;
            int limit=SafeAdvanceLimit(route);
            if(limit<1)return IntVec3.Invalid;
            int nearest=0,best=int.MaxValue;
            for(int i=0;i<=limit;i++)
            {
                int distance=(soldier.Position-route.Cells[i]).LengthHorizontalSquared;
                if(distance<best){best=distance;nearest=i;}
            }
            if(best>225)return JoinCommonCorridor(route,soldier,map,limit);
            // Off-corridor soldiers must join at a nearby SAFE cell. A Goto
            // several tiles ahead is unsafe because the vanilla pathfinder may
            // cut a corner across a defended killing field.
            if(best>2)
            {
                IntVec3 join=route.Cells[nearest];
                if(join!=soldier.Position && soldier.CanReach(join,PathEndMode.OnCell,Danger.Deadly) &&
                   (route.RiskFallback || !LateralExposed(map,soldier.Position,join)))return join;
                return JoinCommonCorridor(route,soldier,map,limit);
            }
            if(nearest>=limit)return IntVec3.Invalid;
            // Follow one uninterrupted CARDINAL corridor leg at a time.
            // A bend is a separate job; the vanilla pathfinder cannot replace
            // a planned right-angle detour with a diagonal shortcut into fire.
            int end=nearest+1;
            IntVec3 previous=route.Cells[nearest];
            IntVec3 step=route.Cells[end]-previous;
            // S2: 5-cell hops made the entire squad stop and restart every few
            // seconds in long open-field approaches. Extend only a completely
            // straight, already validated cardinal leg; never skip a bend,
            // planned breach or first turret-exposure cell.
            int maxLeg = 13 + (soldier.thingIDNumber & 3) * 2;
            for(int i=end+1;i<=limit && i<=nearest+maxLeg;i++)
            {
                IntVec3 nextStep=route.Cells[i]-route.Cells[i-1];
                if(nextStep!=step)break;
                end=i;
            }
            // Stagger depth across four ranks as well as lateral lanes.
            // 30 pawns must not all select the same three end-cells.
            int rank=(soldier.thingIDNumber/7)&3;
            int head=Math.Max(nearest+1,end-rank*2);
            for(int i=head;i>nearest;i--)
            {
                IntVec3 destination=route.Cells[i];
                if(!destination.InBounds(map) || !destination.Standable(map))continue;
                if(soldier.CanReach(destination,PathEndMode.OnCell,Danger.Deadly) &&
                   !CrossesKnownFire(route,soldier.Position,destination,map))
                {
                    // Seven wide lanes and four staggered ranks only on
                    // open, unexposed ground. In narrow rooms the squad must
                    // reduce to one safe passage, but not at map-entry.
                    int lateral=soldier.thingIDNumber%7-3;
                    if(route.FirstTurretExposureIndex<0 && i<limit-4 &&
                       i-nearest>=6 && lateral!=0)
                    {
                        IntVec3 side=step.x!=0?IntVec3.North:IntVec3.East;
                        IntVec3 lane=destination+side*lateral;
                        if(lane.InBounds(map) && lane.Standable(map) &&
                           lane.GetFirstPawn(map)==null &&
                           IsAllowedWaypoint(route,lane) &&
                           !LateralExposed(map,soldier.Position,lane) &&
                           !CrossesKnownFire(route,soldier.Position,lane,map) &&
                           GenSight.LineOfSight(soldier.Position,lane,map) &&
                           soldier.CanReach(lane,PathEndMode.OnCell,Danger.Deadly))
                            return lane;
                    }
                    return destination;
                }
            }
            return IntVec3.Invalid;
        }
        // Physically accessible rendezvous on the SAME shared corridor.
        // Native CanReach disallows magic wall traversal; no private breach.
        private static IntVec3 JoinCommonCorridor(RHFRoutePlanV128 route,
            Pawn pawn,Map map,int limit)
        {
            int probes=0;
            for(int radius=8;radius<=32 && probes<8;radius+=8)
            {
                int nearest=int.MaxValue;
                IntVec3 candidate=IntVec3.Invalid;
                for(int i=0;i<=limit;i+=Math.Max(1,limit/32))
                {
                    IntVec3 cell=route.Cells[i];
                    int d=(pawn.Position-cell).LengthHorizontalSquared;
                    if(d<=radius*radius && d<nearest && cell.Standable(map) &&
                       cell.GetEdifice(map)==null)
                    {candidate=cell;nearest=d;}
                }
                if(!candidate.IsValid || candidate==pawn.Position)continue;
                probes++;
                if((route.RiskFallback || !LateralExposed(map,pawn.Position,candidate)) &&
                   pawn.CanReach(candidate,PathEndMode.OnCell,Danger.Deadly))
                    return candidate;
            }
            return IntVec3.Invalid;
        }

        // Check that a Goto does not shortcut the costed protected centerline
        // through an unapproved firing lane or other unplanned passage.
        private static bool CrossesKnownFire(RHFRoutePlanV128 route,IntVec3 from,
            IntVec3 to,Map map)
        {
            // Check joining legs even if the chosen *corridor* is safe:
            // vanilla Goto otherwise cuts a turret-exposed corner.
            if(!route.RiskFallback && LateralExposed(map,from,to))return true;
            // A joining move is allowed only when its intermediate grid cells
            // occur on the safe prefix of the pre-calculated corridor.
            int dx=to.x-from.x,dz=to.z-from.z;
            int steps=Math.Max(Math.Abs(dx),Math.Abs(dz));
            if(steps<=1)return false;
            for(int k=1;k<steps;k++)
            {
                IntVec3 c=new IntVec3(from.x+dx*k/steps,0,from.z+dz*k/steps);
                if(!IsAllowedWaypoint(route,c))return true;
            }
            return false;
        }
        internal static string Audit(Map map)
        {
            if(map==null)return "Нет текущей карты";
            RHFOperationController ctrl=map.GetComponent<RHFOperationController>();
            if(ctrl==null)return "Нет контроллера операций RHF";
            // An audit is user-initiated; expose the route already in cache.
            var field=HarmonyLib.AccessTools.Field(typeof(RHFOperationController),"operations");
            List<RHFOperation> ops=field?.GetValue(ctrl) as List<RHFOperation>;
            if(ops==null || ops.Count==0)return "Нет активных операций RHF";
            var sb=new StringBuilder(700);
            foreach(RHFOperation op in ops)
            {
                if(op==null || op.Type!=(int)RHFOperationType.Rescue)continue;
                if(!Cache.TryGetValue(op,out RHFRouteStoreV128 store) || store.Plan==null)
                {sb.AppendLine("Маршрут ещё не рассчитывался");continue;}
                RHFRoutePlanV128 p=store.Plan;
                sb.AppendLine("NJR: маршрут "+(p.Valid?"найден":"не найден"));
                sb.AppendLine("От "+p.From+" к "+p.To+"; причина: "+p.Explanation);
                sb.AppendLine("Полная стоимость="+p.TotalCost+", ОЗ преград="+
                    p.TotalStructuralHP+", стен="+p.Walls+", дверей="+p.Doors);
                sb.AppendLine("Стоимость опасности="+p.DangerCost+", исследовано клеток="+p.Expanded);
                sb.AppendLine("Клеток под прямым огнём турелей="+p.ExposedTurretCells+
                    "; первая опасная="+p.FirstTurretExposureIndex+
                    "; разрешённое продвижение до="+SafeAdvanceLimit(p));
                sb.AppendLine("Первый пролом: "+(p.FirstBarrier?.LabelShortCap??"нет")+
                    "; живой объект="+(p.FirstBarrier!=null && !p.FirstBarrier.Destroyed));
            }
            return sb.ToString();
        }
    }
    public static class RHFRouteDiagnosticsV128
    {
        [DebugAction("NJR - ТЕСТЫ | ДИАГНОСТИКА", "31 — Цена маршрута и простреливаемые участки",
            actionType=DebugActionType.Action,allowedGameStates=AllowedGameStates.PlayingOnMap)]
        public static void Show()
        { Find.WindowStack.Add(new Dialog_MessageBox(RHFObjectiveRouteV128.Audit(Find.CurrentMap))); }
    }
}
