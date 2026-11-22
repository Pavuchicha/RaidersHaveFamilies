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
        internal int Tick;
        internal Pawn Leader;
        internal Pawn Captive;
        internal IntVec3 From;
        internal IntVec3 To;
        internal List<IntVec3> Cells = new List<IntVec3>();
        internal Thing FirstBarrier;
        internal int FirstBarrierIndex = -1;
        // A physical turret fire corridor is a STOP, not a route waypoint.
        // The assault wing holds at the last genuinely unexposed map cell.
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
    }

    [StaticConstructorOnStartup]
    internal static class RHFObjectiveRouteV128
    {
        static RHFObjectiveRouteV128()
        {
            Log.Message("[RHF] v0.12.8 route mission controller loaded (HP + threat corridor).");
        }
        private const int ReplanTicks = 900;
        private const int MaxCells = 100000;
        private const int MaxExpanded = 85000;
        private const int MaxThreatEmitters = 40;
        private const int ScanStructures = 2600;
        // Only real emitted attacks affect danger, not an arbitrary 35-tile ring.
        // A few steps under concentrated direct fire must cost more than
        // breaching several ordinary walls; never trade soldiers for trivial distance.
        private const int ExposedCost = 4000;
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
            if(threats.Count<MaxThreatEmitters){threats.Add(incoming);return;}
            int worst=0;
            for(int i=1;i<threats.Count;i++)
                if(threats[i].CorridorDistance>threats[worst].CorridorDistance)worst=i;
            if(incoming.CorridorDistance<threats[worst].CorridorDistance)
                threats[worst]=incoming;
        }

        private static float WeaponRange(ThingDef weapon, float fallback)
        {
            if(weapon?.Verbs==null)return fallback;
            float range=0f;
            foreach(VerbProperties verb in weapon.Verbs)
                if(verb!=null && verb.range>range)range=verb.range;
            return range>0f ? Mathf.Clamp(range,5f,75f) : fallback;
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
            var threat=new List<Emitter>(MaxThreatEmitters);
            int checkedBuildings=0;
            foreach(Building building in map.listerBuildings.allBuildingsColonist)
            {
                if(++checkedBuildings>ScanStructures)break;
                if(!IsTurret(building))continue;
                ThingDef gun=building.def?.building?.turretGunDef;
                float range=WeaponRange(gun,30f);
                // A turret midway along a long route matters even when far
                // from BOTH endpoints; never discard it by endpoint distance.
                AddEmitter(threat,new Emitter(building.Position,range,2,
                    DistanceToCorridor(building.Position,a,b)));
            }
            // An armed defender can also create a hostile firing corridor.
            int checkedPawns=0;
            foreach(Pawn pawn in map.mapPawns.FreeColonistsSpawned)
            {
                if(++checkedPawns>100)break;
                if(pawn==null || pawn.Dead || pawn.Downed || !pawn.Spawned ||
                   pawn.equipment?.Primary?.def?.IsRangedWeapon!=true)continue;
                float range=WeaponRange(pawn.equipment.Primary.def,24f);
                AddEmitter(threat,new Emitter(pawn.Position,range,1,
                    DistanceToCorridor(pawn.Position,a,b)));
            }
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
                if(!GenSight.LineOfSight(t.Origin,cell,map))continue;
                cost+=ExposedCost*t.Importance;
                if(dist<t.RangeSquared*0.32f)cost+=180*t.Importance;
                if(cost>=60000)return 60000;
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
                if(GenSight.LineOfSight(source.Origin,cell,map))return true;
            }
            return false;
        }
        private static int Step(Map map,IntVec3 cell, out Thing obstacle)
        {
            obstacle=null;
            Building building=cell.GetEdifice(map);
            if(building!=null && !building.Destroyed)
            {
                if(RHFCombatPlannerV125.IsBreachableStructure(building))
                {
                    obstacle=building;
                    return 10+RHFCombatPlannerV125.BreachWorkCost(building);
                }
                if(building.def?.passability==Traversability.Impassable)return -1;
            }
            if(!cell.Standable(map))return -1;
            return 10;
        }
        private static bool PlanBounded(Map map,Pawn leader,Pawn captive,
            RHFRoutePlanV128 plan,int margin)
        {
            IntVec3 start=leader.Position,goal=captive.Position;
            int minX=Math.Max(0,Math.Min(start.x,goal.x)-margin);
            int minZ=Math.Max(0,Math.Min(start.z,goal.z)-margin);
            int maxX=Math.Min(map.Size.x-1,Math.Max(start.x,goal.x)+margin);
            int maxZ=Math.Min(map.Size.z-1,Math.Max(start.z,goal.z)+margin);
            int width=maxX-minX+1,height=maxZ-minZ+1;
            int count=width*height;
            if(count<=0 || count>MaxCells)return false;
            int from=(start.z-minZ)*width+start.x-minX;
            int finish=(goal.z-minZ)*width+goal.x-minX;
            var costs=new int[count];
            var parents=new int[count];
            var hazard=new int[count];
            for(int i=0;i<count;i++){costs[i]=int.MaxValue;parents[i]=-1;hazard[i]=-1;}
            List<Emitter> threats=Threats(map,start,goal);
            var heap=new List<HeapItem>(128);
            costs[from]=0;
            Push(heap,from,Heuristic(start.x,start.z,goal));
            int expanded=0;
            bool reached=false;
            while(heap.Count>0 && expanded<MaxExpanded)
            {
                HeapItem current=Pop(heap);
                int at=current.Id;
                int cx=at%width+minX,cz=at/width+minZ;
                if(current.Priority!=costs[at]+Heuristic(cx,cz,goal))continue;
                expanded++;
                if(at==finish){reached=true;break;}
                for(int direction=0;direction<4;direction++)
                {
                    int nx=cx+(direction==0?1:direction==1?-1:0);
                    int nz=cz+(direction==2?1:direction==3?-1:0);
                    if(nx<minX||nx>maxX||nz<minZ||nz>maxZ)continue;
                    int next=(nz-minZ)*width+nx-minX;
                    IntVec3 cell=new IntVec3(nx,0,nz);
                    Thing obstacle;
                    int step=Step(map,cell,out obstacle);
                    if(step<0)continue;
                    // Out-of-sight soft-cover and shooter arcs enter the ACTUAL
                    // cost of every route segment, not a straight-line estimate.
                    if(hazard[next]<0)hazard[next]=Exposure(map,cell,threats);
                    step+=hazard[next];
                    if(costs[at] > int.MaxValue-step)continue;
                    int total=costs[at]+step;
                    if(total>=costs[next])continue;
                    costs[next]=total;
                    parents[next]=at;
                    Push(heap,next,total+Heuristic(nx,nz,goal));
                }
            }
            plan.Expanded+=expanded;
            if(!reached)
            {
                plan.Explanation=expanded>=MaxExpanded ?
                    "исчерпан лимит обхода; не выбираем случайную стену" :
                    "нет достижимого маршрута в текущей области";
                return false;
            }
            var reverse=new List<IntVec3>(256);
            for(int cursor=finish;cursor>=0 && reverse.Count<count;cursor=parents[cursor])
            {
                reverse.Add(new IntVec3(cursor%width+minX,0,cursor/width+minZ));
                if(cursor==from)break;
            }
            if(reverse.Count<2 || reverse[reverse.Count-1]!=start)
            {
                plan.Explanation="ошибка восстановления полного маршрута";
                return false;
            }
            reverse.Reverse();
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
            plan.Explanation="маршрут построен: "+plan.Cells.Count+" клеток; " +
                "дверей="+plan.Doors+", стен="+plan.Walls+
                (plan.FirstTurretExposureIndex>=0 ?
                    "; обнаружен сектор огня турели — требуется прикрытие" :
                    "; сектор огня турелей не пересекается");
            return true;
        }

        internal static RHFRoutePlanV128 Resolve(RHFOperation op,Map map,
            Pawn leader,Pawn captive,int now)
        {
            if(op==null || map==null || leader==null || captive==null ||
               !leader.Spawned || !captive.Spawned || leader.Map!=map || captive.Map!=map)
                return new RHFRoutePlanV128{Explanation="неверная цель или карта"};
            RHFRouteStoreV128 holder=Cache.GetValue(op,_=>new RHFRouteStoreV128());
            RHFRoutePlanV128 prior=holder.Plan;
            // A corridor with no actual forward progress must expire sooner;
            // otherwise idle pawns can retain a bad initial approach for 900t.
            bool stalled=prior!=null && now-prior.Tick>=300 &&
                now-Math.Max(op.LastProgressTick,op.PhaseStartTick)>600;
            if(prior!=null && !stalled && prior.Captive==captive && prior.Leader!=null &&
               prior.Leader.Spawned && prior.Leader.Map==map && !prior.Leader.Dead &&
               now-prior.Tick<ReplanTicks &&
               (prior.FirstBarrier==null || !prior.FirstBarrier.Destroyed) &&
               (!prior.Valid || (prior.Cells.Count>0 &&
                                   NearPath(prior,prior.Leader.Position,144))))
                return prior;
            var fresh=new RHFRoutePlanV128 {Tick=now,Leader=leader,Captive=captive,
                From=leader.Position,To=captive.Position};
            if(!PlanBounded(map,leader,captive,fresh,44))
                PlanBounded(map,leader,captive,fresh,Math.Max(map.Size.x,map.Size.z));
            if(!fresh.Valid && RHFMod.Settings?.VerboseLogging==true)
                Log.Warning("[RHF][RouteV128] " +fresh.Explanation+
                    " source="+leader.Position+" target="+captive.Position);
            holder.Plan=fresh;
            return fresh;
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
            if(route.FirstTurretExposureIndex>=0)
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
            if(best>225)return IntVec3.Invalid;
            // Off-corridor soldiers must join at a nearby SAFE cell. A Goto
            // several tiles ahead is unsafe because the vanilla pathfinder may
            // cut a corner across a defended killing field.
            if(best>2)
            {
                IntVec3 join=route.Cells[nearest];
                if(join!=soldier.Position && soldier.CanReach(join,PathEndMode.OnCell,Danger.Deadly) &&
                   !CrossesKnownFire(route,soldier.Position,join,map))return join;
                return IntVec3.Invalid;
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
        // The route hazard cache can be stale after the world changes; this
        // geometric check rejects a direct Goto whose segment crosses a cell
        // that is absent from the selected route. Changes to visible stationary
        // defence trigger re-evaluation on the next mission planning pulse.
        private static bool CrossesKnownFire(RHFRoutePlanV128 route,IntVec3 from,
            IntVec3 to,Map map)
        {
            if(route.FirstTurretExposureIndex<0)return false;
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
                sb.AppendLine("RHF v0.12.9: маршрут "+(p.Valid?"найден":"не найден"));
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
        [DebugAction("RHF - ТЕСТЫ | ДИАГНОСТИКА", "31 — Цена маршрута и простреливаемые участки",
            actionType=DebugActionType.Action,allowedGameStates=AllowedGameStates.PlayingOnMap)]
        public static void Show()
        { Find.WindowStack.Add(new Dialog_MessageBox(RHFObjectiveRouteV128.Audit(Find.CurrentMap))); }
    }
}
