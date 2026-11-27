// R040: shared structural access for pod construction and trapped rescue convoys.
// No extra damage, new serialized data, teleportation or independent job writer.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace RaidersHaveFamilies
{
    internal static class NJRRescueAccessR040
    {
        private sealed class State
        {
            internal Thing Barrier;
            internal int Phase,NextSearch,Started,LastHP,LastProgress;
            internal int ExitDoorFailureR044=-1;
            internal IntVec3 Goal=IntVec3.Invalid;
            internal int NavigatorId=-1;
            internal bool AllowExposedExitR056;
            internal readonly HashSet<Pawn> Failed=new HashSet<Pawn>();
        }
        private static readonly ConditionalWeakTable<RHFOperation,State> States=new ConditionalWeakTable<RHFOperation,State>();
        internal static void Clear(RHFOperation op)
        {
            if(op==null)return;
            if(States.TryGetValue(op,out State previous) && op.BoardS41!=null &&
               op.BoardS41.Barrier==previous.Barrier)
            {
                op.BoardS41.Barrier=null;op.BoardS41.Breacher=null;
                op.BoardS41.NextReplanTick=0;
            }
            States.Remove(op);
        }
        internal static bool IsAssignedAccess(RHFOperation op,Thing barrier)
        {
            return op!=null && barrier!=null && States.TryGetValue(op,out State state) &&
                state.Phase==op.Phase && state.Barrier==barrier && op.BoardS41?.Barrier==barrier &&
                RHFObjectiveRouteV128.IsRescueStructuralBarrier(barrier);
        }
        internal static bool PermitsExit(RHFOperation op,Thing barrier)
        {return op!=null && barrier!=null && (IsAssignedAccess(op,barrier) || (!op.FortifyPrison && barrier is Building_Door));}
        private static bool Active(Pawn pawn,Map map)=>RHFRescuePlannerS41.ActiveForRoleS41(pawn,map);
        private static bool Live(Thing barrier,Map map)=>barrier!=null && barrier.Spawned &&
            barrier.Map==map && !barrier.Destroyed && RHFObjectiveRouteV128.IsRescueStructuralBarrier(barrier);
        private static bool CanWork(RHFOperation op,Map map,Thing barrier,bool allowExposed=false)
        {
            if(!Live(barrier,map))return false;
            var excluded=new HashSet<Pawn>();var used=new HashSet<IntVec3>();
            if(op.Freed!=null)foreach(Pawn freed in op.Freed)
                if(freed!=null && freed.Spawned && freed.Map==map)used.Add(freed.Position);
            for(int i=0;i<op.Squad.Count;i++)
            {
                Pawn actor=SelectAccessOperatorR047(op,map,barrier,excluded,used,allowExposed);
                if(actor==null)break;
                if(AccessSlotR047(op,actor,barrier,map,used,IntVec3.Invalid,allowExposed).IsValid)return true;
                excluded.Add(actor);
            }
            return false;
        }
        private static Pawn SelectAccessOperatorR047(RHFOperation op,Map map,Thing barrier,HashSet<Pawn> excluded,HashSet<IntVec3> used,bool allowExposed=false)
        {
            if(op.FortifyPrison)return SelectVipPairR048(op,map,barrier,excluded,used,null,out _,allowExposed);
            Pawn normal=RHFCombatPlannerV125.SelectBreachOperator(op,map,barrier,excluded);
            if(normal!=null || !op.FortifyPrison || op.Phase!=2)return normal;
            Pawn best=null;int score=int.MinValue;
            foreach(Pawn member in op.Squad)
            {
                if(!Active(member,map) || excluded.Contains(member) || RHFCombatPlannerV125.IsCombatEmergencyJob(member) ||
                   member.WorkTagIsDisabled(WorkTags.Violent) || member.health?.capacities?.CapableOf(PawnCapacityDefOf.Manipulation)!=true ||
                   !AccessSlotR047(op,member,barrier,map,used,IntVec3.Invalid).IsValid)continue;
                int rank=(RHFTactical.IsBreachAxe(member)?10000:0)+(member.skills?.GetSkill(SkillDefOf.Melee)?.Level??0)*15-
                    (member.Position-barrier.Position).LengthHorizontalSquared;
                if(rank>score){best=member;score=rank;}
            }
            return best;
        }
        internal static IntVec3 AccessSlotR047(RHFOperation op,Pawn actor,Thing barrier,Map map,HashSet<IntVec3> used,IntVec3 previous,bool allowExposed=false)
        {
            IntVec3 slot=IntVec3.Invalid;
            if(!op.FortifyPrison || !(barrier is Building_Door))
                slot=NJRRescueFormationR032.BreachSlot(actor,barrier,map,used,op.Squad,true,previous,true);
            if(slot.IsValid || !op.FortifyPrison || (op.Phase!=1 && op.Phase!=2 && op.Phase!=3))return slot;
            // A native reachable cardinal strike square is enough: a turn
            // around a wall must not be rejected as a blocked straight ray.
            int best=int.MaxValue;
            for(int d=0;d<4;d++)
            {
                IntVec3 cell=RHFObjectiveRouteV128.CardinalR044(barrier.Position,d);
                if(used.Contains(cell) || !NJRRescueFormationR032.StrikeCell(map,barrier,cell,actor) ||
                   RHFObjectiveRouteV128.StagingExposedToTurret(map,cell) ||
                   !actor.CanReach(cell,PathEndMode.OnCell,Danger.Deadly))continue;
                Pawn occupant=cell.GetFirstPawn(map);
                if(occupant!=null && occupant!=actor && !op.Squad.Contains(occupant))continue;
                int cost=(actor.Position-cell).LengthHorizontalSquared-(cell==previous?10000:0);
                if(cost<best){best=cost;slot=cell;}
            }
            // Only a proved post-launch route may use this last resort. A turret
            // risk is a combat problem, not a permanent impassable wall.
            if(!slot.IsValid && allowExposed && op.Phase==2 && op.PodLaunchCompleted)
                for(int d=0;d<4;d++)
                {
                    IntVec3 c=RHFObjectiveRouteV128.CardinalR044(barrier.Position,d);
                    if(used.Contains(c) || !NJRRescueFormationR032.StrikeCell(map,barrier,c,actor) ||
                       !actor.CanReach(c,PathEndMode.OnCell,Danger.Deadly))continue;
                    Pawn occupant=c.GetFirstPawn(map);
                    if(occupant!=null && occupant!=actor && !op.Squad.Contains(occupant))continue;
                    int cost=(actor.Position-c).LengthHorizontalSquared;
                    if(cost<best){best=cost;slot=c;}
                }
            return slot;
        }
        // R048: choose a pawn AND a working strike cell, not a nominal axe
        // holder who cannot reach any of the actual squares beside the door.
        internal static Pawn SelectVipPairR048(RHFOperation op,Map map,Thing barrier,
            HashSet<Pawn> excluded,HashSet<IntVec3> used,Pawn preferred,out IntVec3 slot,bool allowExposed=false)
        {
            slot=IntVec3.Invalid;Pawn best=null;int score=int.MinValue;
            foreach(Pawn p in op.Squad)
            {
                if(!Active(p,map) || excluded.Contains(p) || RHFCombatPlannerV125.IsCombatEmergencyJob(p) ||
                   p.WorkTagIsDisabled(WorkTags.Violent) || p.health?.capacities?.CapableOf(PawnCapacityDefOf.Manipulation)!=true)continue;
                RHFOrderS41 previous=op.BoardS41?.For(p,false);
                IntVec3 c=AccessSlotR047(op,p,barrier,map,used,
                    previous?.Objective==barrier?previous.Cell:IntVec3.Invalid,allowExposed);
                if(!c.IsValid)continue;
                int rank=(RHFTactical.IsBreachAxe(p)?100000:0)+(p.equipment?.Primary?.def?.IsMeleeWeapon==true?1200:0)+
                    (p.skills?.GetSkill(SkillDefOf.Melee)?.Level??0)*15-Math.Min(2000,(p.Position-c).LengthHorizontalSquared*4)+
                    (p==preferred?500:0);
                if(rank>score){score=rank;best=p;slot=c;}
            }
            return best;
        }
        internal static bool TryEntryDebrisR048(RHFOperation op,Map map,RHFRoutePlanV128 route,int now)
        {
            if(!op.FortifyPrison || op.Phase!=1 || !route.Valid || route.FirstBarrierIndex<0 ||
               route.Cells==null || route.FirstBarrierIndex>=route.Cells.Count)return false;
            var board=op.BoardS41;
            Pawn mover=null;Thing chunk=null;IntVec3 dest=IntVec3.Invalid;
            foreach(Pawn p in op.Squad)
            {
                if(!Active(p,map) || NJREarlyWingR053.IsBuilderWing(op,p) || p.jobs?.curJob?.def?.defName!="NJR_MoveSealCargoR044")continue;
                RHFOrderS41 prior=board.For(p,false);
                if(prior?.Objective==null || !NJRVipDebrisR045.IsDebris(prior.Objective))continue;
                mover=p;chunk=prior.Objective;dest=prior.Cell;break;
            }
            if(mover==null)
            {
                var forbidden=new HashSet<IntVec3>(route.Cells);
                foreach(IntVec3 offset in NJRRescueFormationR032.Adjacent)forbidden.Add(route.FirstBarrier.Position+offset);
                for(int i=Math.Max(0,route.FirstBarrierIndex-12);i<route.FirstBarrierIndex;i++)
                {
                    Thing obstruction=NJRVipDebrisR045.DebrisAt(map,route.Cells[i]);
                    if(obstruction==null || !NJRVipDebrisR045.TryMove(op,map,obstruction,null,null,null,out mover,out dest,forbidden))continue;
                    chunk=obstruction;break;
                }
                if(mover==null)return false;
                Log.Message("[NJR][R048][Entry] clear route debris "+chunk.Position+" -> "+dest);
            }
            board.Breacher=null;board.Barrier=null;
            var used=new HashSet<IntVec3>{dest};
            foreach(Pawn p in op.Squad)
            {
                if(!Active(p,map) || NJREarlyWingR053.IsBuilderWing(op,p) && p!=mover)continue;
                if(p==mover){board.Assign(p,RHFRoleS41.PodSupply,chunk,dest,now);continue;}
                IntVec3 c=NJRRescueFormationR032.SpreadNear(p,p.Position,map,used,0,4,true);
                if(c.IsValid){used.Add(c);board.Assign(p,RHFRoleS41.Screen,null,c,now);}
                else board.Assign(p,RHFRoleS41.Hold,null,p.Position,now);
            }
            board.Decision="R048 clear actual entry corridor";board.NextReplanTick=now+90;board.LastPlanTick=now;
            return true;
        }
        // A safe local interior bend is legal even if the two endpoints do not
        // have straight LOS. Check actual native reachability in BreachSlot too.
        internal static bool SafeInteriorReach(Pawn pawn,Map map,IntVec3 target)
        {
            if(pawn==null || map==null || !target.InBounds(map) ||
               (pawn.Position-target).LengthHorizontalSquared>576)return false;
            Room room=pawn.Position.GetRoom(map);
            if(room==null || room.PsychologicallyOutdoors || target.GetRoom(map)!=room)return false;
            var danger=RHFObjectiveRouteV128.AccessExposureR040(map,pawn.Position);
            var seen=new HashSet<IntVec3>();var queue=new Queue<IntVec3>();
            queue.Enqueue(pawn.Position);seen.Add(pawn.Position);
            int expanded=0;
            while(queue.Count>0 && expanded++<800)
            {
                IntVec3 at=queue.Dequeue();if(at==target)return true;
                foreach(IntVec3 offset in GenAdj.CardinalDirections)
                {
                    IntVec3 next=at+offset;
                    if(!next.InBounds(map) || seen.Contains(next) || !next.Standable(map) ||
                       next.GetRoom(map)!=room || danger(next)>0)continue;
                    if(RHFObjectiveRouteV128.Step(map,next,out Thing barrier)<0 || barrier!=null)continue;
                    seen.Add(next);queue.Enqueue(next);
                }
            }
            return false;
        }
        // One forward search over real cells. Reuse the assault planner's exact
        // structural classification and costs, including cheap doors and expensive
        // constructed walls. The returned first barrier belongs to a COMPLETE path.
        private sealed class Field
        {
            internal readonly int Width,Height;
            internal readonly int[] Cost,Parent;
            internal readonly Thing[] First;
            internal IntVec3 Exit=IntVec3.Invalid;
            internal Field(Map map)
            {Width=map.Size.x;Height=map.Size.z;Cost=new int[Width*Height];First=new Thing[Cost.Length];Parent=new int[Cost.Length];Array.Fill(Parent,-1);Array.Fill(Cost,int.MaxValue);}
            internal int Id(IntVec3 c)=>c.z*Width+c.x;
            internal int At(IntVec3 c)=>Cost[Id(c)];
            internal Thing BarrierAt(IntVec3 c)=>First[Id(c)];
            internal HashSet<IntVec3> Path()
            {
                var result=new HashSet<IntVec3>();if(!Exit.IsValid)return result;
                for(int i=Id(Exit);i>=0;i=Parent[i])result.Add(new IntVec3(i%Width,0,i/Width));
                return result;
            }
        }
        private static int Edge(Map map,IntVec3 c)=>Math.Min(Math.Min(c.x,map.Size.x-1-c.x),Math.Min(c.z,map.Size.z-1-c.z));
        private static Field Search(Map map,Pawn navigator,int radius,bool toEdge,bool preferVipDoors=false,bool doorsOnly=false,RHFOperation ownExit=null,bool clearDebris=false,HashSet<Thing> excludedRepair=null)
        {
            int count=map.Size.x*map.Size.z;
            if(count<=0 || count>120000)return null;
            var f=new Field(map);
            var heap=new PriorityQueue<int,int>();
            var closed=new bool[count];
            var hazards=new int[count];Array.Fill(hazards,-1);
            var danger=RHFObjectiveRouteV128.AccessExposureR040(map,navigator.Position,preferVipDoors);
            int start=f.Id(navigator.Position);f.Cost[start]=0;
            heap.Enqueue(start,toEdge?Edge(map,navigator.Position)*10:0);
            int expanded=0;
            while(heap.TryDequeue(out int id,out int priority) && expanded<count)
            {
                if(closed[id])continue;
                IntVec3 at=new IntVec3(id%f.Width,0,id/f.Width);
                if(priority!=f.Cost[id]+(toEdge?Edge(map,at)*10:0))continue;
                closed[id]=true;expanded++;
                if(toEdge && Edge(map,at)==0 && at.Standable(map) && at.GetEdifice(map)==null && !at.Roofed(map))
                {
                    // A zero-action geometric route is not a repair plan. In
                    // particular it must not hide a real route via another door.
                    if(!clearDebris || f.First[id]!=null || NJRRescueExitS41.Eligible(map,ownExit,navigator,at,Danger.Deadly))
                    {f.Exit=at;break;}
                }
                for(int direction=0;direction<4;direction++)
                {
                    IntVec3 next=RHFObjectiveRouteV128.CardinalR044(at,direction);
                    if(!next.InBounds(map) || (!toEdge &&
                        (Math.Abs(next.x-navigator.Position.x)>radius || Math.Abs(next.z-navigator.Position.z)>radius)))continue;
                    int nid=f.Id(next);if(closed[nid])continue;
                    Thing obstacle;
                    int step=doorsOnly ? RHFObjectiveRouteV128.DoorStepR043(map,next,out obstacle) :
                        preferVipDoors ? RHFObjectiveRouteV128.StepVipR041(map,next,out obstacle) :
                        RHFObjectiveRouteV128.Step(map,next,out obstacle);
                    if(ownExit!=null && IsOwnSealR043(ownExit,next.GetEdifice(map)))
                    {obstacle=next.GetEdifice(map);step=10+obstacle.HitPoints*2;}
                    if(clearDebris && ownExit?.PodLaunchCompleted==true && ExitOwnedAtR054(ownExit,map,next) is Thing owned)
                    {obstacle=owned;step=owned==ownExit.PodLauncherThing?1800:10+owned.HitPoints*2;}
                    if(clearDebris && ownExit?.PodLaunchCompleted==true && next.GetEdifice(map) is Building gun &&
                       RHFCombatPlannerV125.IsTurret(gun) && gun.def.passability==Traversability.Impassable)
                    {obstacle=gun;step=3200;}
                    if(clearDebris && obstacle==null && ExitChunkAtR054(map,next) is Thing debris)
                    {obstacle=debris;step=100+Math.Max(0,step);}
                    // Before the first repair, only admit cells that the native pawn can reach.
                    // Otherwise an abstract walkable path with no actionable barrier wins forever.
                    if(obstacle!=null && excludedRepair!=null && excludedRepair.Contains(obstacle))continue;
                    if(clearDebris && ownExit?.PodLaunchCompleted==true && f.First[id]==null && obstacle==null &&
                       next.Standable(map) && !navigator.CanReach(next,PathEndMode.OnCell,Danger.Deadly))continue;
                    if(clearDebris && ownExit?.PodLaunchCompleted==true && f.First[id]==null && obstacle!=null &&
                       !navigator.CanReach(obstacle,PathEndMode.Touch,Danger.Deadly))continue;
                    if(step<0)continue;
                    if(hazards[nid]<0)hazards[nid]=danger(next);
                    step+=hazards[nid];
                    if(f.Cost[id]>int.MaxValue-step || f.Cost[id]+step>=f.Cost[nid])continue;
                    f.Cost[nid]=f.Cost[id]+step;f.First[nid]=f.First[id]??obstacle;f.Parent[nid]=id;
                    heap.Enqueue(nid,f.Cost[nid]+(toEdge?Edge(map,next)*10:0));
                }
            }
            return f;
        }
        // Compare ALL local pads, including those behind a verified door,
        // in one cost field. No outdoor-first short circuit or arbitrary rotation.
        private static Thing FindPodBarrier(RHFOperation op,Map map,Pawn vip,ThingDef launcher,ThingDef pod,
            out IntVec3 goal,out IntVec3 podGoal,Pawn earlyEngineer=null)
        {
            goal=podGoal=IntVec3.Invalid;
            Field f=Search(map,vip,24,false,true,op.FortifyPrison);if(f==null)return null;
            IntVec3 anchor=op.PodPrisonAnchorR043.IsValid?op.PodPrisonAnchorR043:vip.Position;
            Room prison=anchor.GetRoom(map);
            Thing best=null;long scoreBest=long.MaxValue;int tierBest=int.MaxValue;
            var canWork=new Dictionary<Thing,bool>();
            var roomTiers=new Dictionary<Room,int>();
            var exposure=RHFObjectiveRouteV128.AccessExposureR040(map,vip.Position);
            var riskCache=new Dictionary<IntVec3,int>();
            int checkedPads=0,validPads=0,blockedFootprint=0,roomMismatch=0,noSlots=0,noWorker=0,noLane=0;
            int Risk(IntVec3 c)
            {
                if(!riskCache.TryGetValue(c,out int risk))riskCache[c]=risk=exposure(c);
                return risk;
            }
            for(int x=Math.Max(0,vip.Position.x-24);x<=Math.Min(map.Size.x-1,vip.Position.x+24);x++)
            for(int z=Math.Max(0,vip.Position.z-24);z<=Math.Min(map.Size.z-1,vip.Position.z+24);z++)
            foreach(Rot4 rotation in NJRPodSiteClearS41.Rotations)
            {
                if(rotation!=Rot4.North && !launcher.rotatable)continue;
                IntVec3 cell=new IntVec3(x,0,z),vehicle=FuelingPortUtility.GetFuelingPortCell(cell,rotation);
                if(!vehicle.InBounds(map) || f.At(cell)==int.MaxValue || f.At(vehicle)==int.MaxValue)continue;
                Room room=cell.GetRoom(map);
                if(room==null || vehicle.GetRoom(map)!=room)continue;
                // Exclude the occupied prison BEFORE comparing early candidates.
                // Otherwise a tier-0 prison winner hides all tier-1/2 alternatives.
                if(earlyEngineer!=null && room==vip.Position.GetRoom(map))continue;
                checkedPads++;
                if(NJRPodSiteClearS41.Needed(map,cell,vehicle,launcher,pod,vip,rotation)!=0)
                {blockedFootprint++;continue;}
                var footprint=new HashSet<IntVec3>();
                foreach(IntVec3 tile in GenAdj.OccupiedRect(cell,rotation,launcher.size))footprint.Add(tile);
                foreach(IntVec3 tile in GenAdj.OccupiedRect(vehicle,Rot4.North,pod.size))footprint.Add(tile);
                bool sameRoom=true;
                foreach(IntVec3 tile in footprint)
                    if(tile.GetRoom(map)!=room){sameRoom=false;break;}
                if(!sameRoom){roomMismatch++;continue;}
                // Two distinct free work positions, one adjacent to each
                // structure. Do not let a footprint consume the sole access.
                var launchSlots=new HashSet<IntVec3>();var podSlots=new HashSet<IntVec3>();
                foreach(IntVec3 tile in footprint)
                foreach(IntVec3 offset in GenAdj.CardinalDirections)
                {
                    IntVec3 slot=tile+offset;
                    if(!slot.InBounds(map) || footprint.Contains(slot) || !slot.Standable(map) ||
                       slot.GetEdifice(map)!=null || slot.GetFirstBuilding(map)!=null ||
                       slot.GetRoom(map)!=room || f.At(slot)==int.MaxValue)continue;
                    if(NJRPodSiteClearS41.Contains(tile,cell,launcher,rotation))launchSlots.Add(slot);
                    if(NJRPodSiteClearS41.Contains(tile,vehicle,pod,Rot4.North))podSlots.Add(slot);
                }
                if(launchSlots.Count==0 || podSlots.Count==0){noSlots++;continue;}
                var slots=new HashSet<IntVec3>(launchSlots);slots.UnionWith(podSlots);
                if(slots.Count<2){noSlots++;continue;}
                Thing barrier=f.BarrierAt(cell)??f.BarrierAt(vehicle);
                if(op.FortifyPrison && barrier!=null && !(barrier is Building_Door))continue;
                if(barrier!=null)
                {
                    if(!canWork.TryGetValue(barrier,out bool possible))
                        canWork[barrier]=possible=CanWork(op,map,barrier);
                    if(!possible){noWorker++;continue;}
                }
                if(!roomTiers.TryGetValue(room,out int tier))
                {
                    tier=room==prison && !room.PsychologicallyOutdoors?0:
                        !room.PsychologicallyOutdoors && NJRRescueInteriorS41.DirectDoor(map,prison,room,vip.Position)?1:
                        !room.PsychologicallyOutdoors?2:3;
                    roomTiers[room]=tier;
                }
                if(op.FortifyPrison)
                {
                    bool sheltered=NJRRescueInteriorS41.ShelteredPadR043(map,cell,vehicle);
                    if(sheltered && room.PsychologicallyOutdoors)
                        foreach(IntVec3 tile in footprint)if(!tile.Roofed(map)){sheltered=false;break;}
                    if(!sheltered)tier=3;
                    else if(room==prison && (!room.PsychologicallyOutdoors || (cell-anchor).LengthHorizontalSquared<=36))tier=0;
                    else if(tier!=1)tier=2;
                }
                if(op.FortifyPrison && tier==3 && (barrier!=null || earlyEngineer!=null))continue;
                // Absolute priority: a viable indoor pad cannot lose to an outdoor score.
                if(op.FortifyPrison && tier>tierBest)continue;
                int risk=Risk(cell)+Risk(vehicle);
                // Prefer a sheltered site; never accept a stronger exposure
                // just to gain a slightly closer room. Work slots matter too.
                long score=tier*100000L+risk*200L+Math.Max(f.At(cell),f.At(vehicle))+
                    (vehicle-vip.Position).LengthHorizontalSquared*20L+
                    (vehicle.Roofed(map)?150:0)-Math.Min(8,slots.Count)*80L;
                if((!op.FortifyPrison || tier==tierBest) && score>=scoreBest)continue;
                // Validate access with BOTH future footprints excluded. The
                // earlier point-only test could choose a pad that sealed its own
                // supply/boarding path. Native reach is also required for a
                // direct site; a planned breach may be traversed in preview.
                if(!WorkLaneR041(map,vip,footprint,launchSlots,podSlots,barrier,earlyEngineer!=null)){noLane++;continue;}
                if(earlyEngineer!=null && (!earlyEngineer.CanReach(cell,PathEndMode.Touch,Danger.Deadly) ||
                    !earlyEngineer.CanReach(vehicle,PathEndMode.Touch,Danger.Deadly)))continue;
                validPads++;
                scoreBest=score;tierBest=tier;best=barrier;goal=cell;podGoal=vehicle;
            }
            Log.Message("[NJR][R041][PodSite] checked="+checkedPads+" valid="+validPads+
                " launcher="+goal+" pod="+podGoal+" barrier="+(best?.LabelShortCap.ToString()??"none")+
                " tier="+tierBest+" score="+scoreBest+" footprintRejected="+blockedFootprint+" roomRejected="+roomMismatch+
                " slotsRejected="+noSlots+" workerRejected="+noWorker+" laneRejected="+noLane);
            return best;
        }
        internal static bool TryEarlyPadR051(RHFOperation op,Map map,Pawn vip,Pawn engineer,
            ThingDef launcher,ThingDef pod,out IntVec3 cell,out IntVec3 vehicle)
        {
            FindPodBarrier(op,map,vip,launcher,pod,out cell,out vehicle,engineer);
            return cell.IsValid && vehicle.IsValid &&
                NJRRescueInteriorS41.ShelteredPadR043(map,cell,vehicle) &&
                cell.GetRoom(map)!=vip.Position.GetRoom(map);
        }
        internal static bool DirectWorkLaneR043(Map map,Pawn vip,ThingDef launcher,ThingDef pod,
            IntVec3 cell,IntVec3 vehicle)
        {
            var footprint=new HashSet<IntVec3>();var launchSlots=new HashSet<IntVec3>();var podSlots=new HashSet<IntVec3>();
            var rotation=NJRPodSiteClearS41.RotationFor(cell,vehicle);
            foreach(IntVec3 t in GenAdj.OccupiedRect(cell,rotation,launcher.size))footprint.Add(t);
            foreach(IntVec3 t in GenAdj.OccupiedRect(vehicle,Rot4.North,pod.size))footprint.Add(t);
            if(cell.GetRoom(map)?.PsychologicallyOutdoors!=false)
                foreach(IntVec3 tile in footprint)if(!tile.Roofed(map))return false;
            foreach(IntVec3 tile in footprint)
            foreach(IntVec3 offset in GenAdj.CardinalDirections)
            {
                IntVec3 slot=tile+offset;
                if(!slot.InBounds(map) || footprint.Contains(slot) || !slot.Standable(map) ||
                    slot.GetEdifice(map)!=null || slot.GetFirstBuilding(map)!=null)continue;
                if(NJRPodSiteClearS41.Contains(tile,cell,launcher,rotation))launchSlots.Add(slot);
                if(NJRPodSiteClearS41.Contains(tile,vehicle,pod,Rot4.North))podSlots.Add(slot);
            }
            return launchSlots.Count>0 && podSlots.Count>0 &&
                WorkLaneR041(map,vip,footprint,launchSlots,podSlots,null);
        }
        private static bool WorkLaneR041(Map map,Pawn vip,HashSet<IntVec3> footprint,
            HashSet<IntVec3> launchSlots,HashSet<IntVec3> podSlots,Thing barrier,bool prospective=false)
        {
            var seen=new HashSet<IntVec3>();var queue=new Queue<IntVec3>();
            seen.Add(vip.Position);queue.Enqueue(vip.Position);
            IntVec3 launch=IntVec3.Invalid,pod=IntVec3.Invalid;
            while(queue.Count>0)
            {
                IntVec3 at=queue.Dequeue();
                bool isLaunch=launchSlots.Contains(at),isPod=podSlots.Contains(at);
                if((isLaunch || isPod) && (prospective || barrier!=null || vip.CanReach(at,PathEndMode.OnCell,Danger.Deadly)))
                {
                    if(isLaunch && pod.IsValid && at!=pod || isPod && launch.IsValid && at!=launch)return true;
                    if(isLaunch && !launch.IsValid)launch=at;
                    if(isPod && !pod.IsValid)pod=at;
                }
                foreach(IntVec3 dir in GenAdj.CardinalDirections)
                {
                    IntVec3 c=at+dir;
                    if(!c.InBounds(map) || footprint.Contains(c) || seen.Contains(c) ||
                       Math.Abs(c.x-vip.Position.x)>26 || Math.Abs(c.z-vip.Position.z)>26)continue;
                    Building edifice=c.GetEdifice(map);
                    if(!c.Walkable(map) && !((barrier!=null && edifice==barrier) || ((prospective || barrier!=null) && edifice is Building_Door)))continue;
                    if(edifice is Building_Door door && !door.Open && barrier==null && !prospective && RHFObjectiveRouteV128.IsRescueStructuralBarrier(door))continue;
                    seen.Add(c);queue.Enqueue(c);
                }
            }
            return false;
        }
        private static Thing FindPodBarrierFallbackR042(RHFOperation op,Map map,Pawn vip,ThingDef launcher,ThingDef pod,bool indoorOnly,out IntVec3 goal)
        {
            goal=IntVec3.Invalid;
            Field f=Search(map,vip,32,false,true);if(f==null)return null;
            Room prison=vip.Position.GetRoom(map);
            Thing best=null;long scoreBest=long.MaxValue;
            var canWork=new Dictionary<Thing,bool>();
            var roomTiers=new Dictionary<Room,int>();
            var guns=new List<Building>();
            foreach(Building gun in map.listerBuildings.allBuildingsColonist)
                if(RHFCombatPlannerV125.IsTurret(gun))guns.Add(gun);
            for(int x=Math.Max(0,vip.Position.x-32);x<=Math.Min(map.Size.x-1,vip.Position.x+32);x++)
            for(int z=Math.Max(0,vip.Position.z-32);z<=Math.Min(map.Size.z-1,vip.Position.z+32);z++)
            {
                IntVec3 cell=new IntVec3(x,0,z),vehicle=FuelingPortUtility.GetFuelingPortCell(cell,Rot4.North);
                if(!vehicle.InBounds(map) || f.At(cell)==int.MaxValue || f.At(vehicle)==int.MaxValue)continue;
                Room room=cell.GetRoom(map);
                if(room==null || vehicle.GetRoom(map)!=room || (indoorOnly && room.PsychologicallyOutdoors))continue;
                // Do not enlarge the footprint by smashing furniture or walls.
                if(NJRPodSiteClearS41.Needed(map,cell,vehicle,launcher,pod,vip)!=0)continue;
                Thing barrier=f.BarrierAt(cell)??f.BarrierAt(vehicle);
                if(barrier==null)continue; // direct native site search owns these candidates
                if(!canWork.TryGetValue(barrier,out bool possible))
                    canWork[barrier]=possible=CanWork(op,map,barrier);
                if(!possible)continue;
                bool exposed=false;
                foreach(Building gun in guns)
                    if(RHFObjectiveRouteV128.TurretCoversR037(gun,map,cell) ||
                       RHFObjectiveRouteV128.TurretCoversR037(gun,map,vehicle)){exposed=true;break;}
                if(exposed)continue;
                if(!roomTiers.TryGetValue(room,out int tier))
                {
                    tier=room==prison?0:!room.PsychologicallyOutdoors &&
                        NJRRescueInteriorS41.DirectDoor(map,prison,room,vip.Position)?1:
                        !room.PsychologicallyOutdoors?2:3;
                    roomTiers[room]=tier;
                }
                long score=tier*100000L+Math.Max(f.At(cell),f.At(vehicle))+
                    (vehicle-vip.Position).LengthHorizontalSquared*6L;
                if(score<scoreBest){scoreBest=score;best=barrier;goal=cell;}
            }
            return best;
        }
        private static State Set(RHFOperation op,Thing barrier,IntVec3 goal,int now)
        {
            var state=States.GetValue(op,_=>new State());
            state.Barrier=barrier;state.Phase=op.Phase;state.Goal=goal;state.Started=now;
            state.LastHP=barrier?.HitPoints??0;state.LastProgress=now;
            state.NextSearch=now+600;state.Failed.Clear();state.AllowExposedExitR056=false;
            if(barrier!=null)Log.Message("[NJR][R040][Access] phase="+op.Phase+" barrier="+barrier.Position+" goal="+goal);
            return state;
        }
        // Plan only. The same mission ThinkTree, legal strike-cell checks and
        // native melee jobs used in Rescue-1 execute all structural work.
        private static bool Assign(RHFOperation op,Map map,Pawn navigator,State state,int now)
        {
            Thing barrier=state.Barrier;
            if(!Live(barrier,map))return false;
            RHFTacticalBoardS41 board=op.BoardS41??=new RHFTacticalBoardS41();
            if(board.ObservedPhase!=op.Phase)
            {board.Orders.Clear();board.ObservedPhase=op.Phase;board.PodWorkStageS41=-1;board.PodWorkTargetS41=null;}
            board.Barrier=barrier;
            if(state.LastHP!=barrier.HitPoints)
            {state.LastHP=barrier.HitPoints;state.LastProgress=now;state.Failed.Clear();op.LastProgressTick=now;}
            Pawn prior=board.Breacher;
            RHFOrderS41 priorOrder=prior!=null?board.For(prior,false):null;
            if(priorOrder!=null && priorOrder.Objective==barrier && prior.Position==priorOrder.Cell &&
               Active(prior,map) && !RHFCombatPlannerV125.IsCombatEmergencyJob(prior) && now-state.LastProgress>=900)
            {
                state.Failed.Add(prior);state.LastProgress=now;
                if(prior.jobs?.curJob?.targetA.Thing==barrier)
                    prior.jobs.EndCurrentJob(JobCondition.InterruptForced,false);
            }
            var used=new HashSet<IntVec3>();var actors=new HashSet<Pawn>();
            if(op.Freed!=null)foreach(Pawn freed in op.Freed)
                if(freed!=null && freed.Spawned && freed.Map==map && !freed.Dead)
                {used.Add(freed.Position);board.Assign(freed,RHFRoleS41.Hold,null,freed.Position,now);}
            var excluded=new HashSet<Pawn>(state.Failed);
            board.Breacher=null;
            int mobile=0;foreach(Pawn member in op.Squad)if(Active(member,map))mobile++;
            int limit=Math.Min(3,Math.Max(1,mobile-2));
            for(int tries=0;tries<op.Squad.Count && actors.Count<limit;tries++)
            {
                Pawn actor=op.FortifyPrison && op.Phase==2 && tries==0 ?
                    SelectVipPairR048(op,map,barrier,excluded,used,priorOrder?.Objective==barrier?prior:null,out _,state.AllowExposedExitR056) :
                    SelectAccessOperatorR047(op,map,barrier,excluded,used,state.AllowExposedExitR056);
                if(actor==null)break;
                excluded.Add(actor);
                if(actors.Count>0 && actor.equipment?.Primary?.def?.IsMeleeWeapon!=true)continue;
                RHFOrderS41 old=board.For(actor,false);
                IntVec3 slot=AccessSlotR047(op,actor,barrier,map,used,
                    old!=null && old.Objective==barrier?old.Cell:IntVec3.Invalid,state.AllowExposedExitR056);
                if(!slot.IsValid)continue;
                used.Add(slot);actors.Add(actor);
                if(board.Breacher==null)board.Breacher=actor;
                board.Assign(actor,RHFRoleS41.Breach,barrier,slot,now);
            }
            if(actors.Count==0)
            {
                state.Failed.Clear();board.Barrier=null;board.Breacher=null;
                board.NextReplanTick=0;return false;
            }
            foreach(IntVec3 offset in NJRRescueFormationR032.Adjacent)
            {IntVec3 c=barrier.Position+offset;if(c.InBounds(map))used.Add(c);}
            foreach(Pawn member in op.Squad)
            {
                if(!Active(member,map) || actors.Contains(member))continue;
                IntVec3 slot=NJRRescueFormationR032.SpreadNear(member,barrier.Position,map,used,4,9,false);
                if(!slot.IsValid)slot=NJRRescueFormationR032.SpreadNear(member,barrier.Position,map,used,4,9,true);
                if(slot.IsValid){used.Add(slot);board.Assign(member,RHFRoleS41.Screen,null,slot,now);}
                else board.Assign(member,RHFRoleS41.Hold,null,member.Position,now);
            }
            board.Decision="R040 verified "+(op.Phase==3?"pod site":"exit")+" access: "+barrier.Position+" -> "+state.Goal;
            board.LastPlanTick=now;board.NextReplanTick=now+90;
            return true;
        }
        internal static bool ContinuePodAccess(RHFOperation op,Map map,Pawn vip,int now)
        {
            if(now-op.PhaseStartTick>9000 || !States.TryGetValue(op,out State state) || state.Phase!=3 ||
               !Live(state.Barrier,map) || now-state.Started>4500)return false;
            return Assign(op,map,vip,state,now);
        }
        internal static bool TryPodAccess(RHFOperation op,Map map,Pawn vip,ThingDef launcher,ThingDef pod,int now,
            out IntVec3 goal,out IntVec3 podGoal)
        {
            goal=podGoal=IntVec3.Invalid;
            if(now-op.PhaseStartTick>9000)return false;
            if(ContinuePodAccess(op,map,vip,now))return true;
            Thing barrier=FindPodBarrier(op,map,vip,launcher,pod,out goal,out podGoal);
            if(barrier==null)return false; // valid coordinates mean direct pad
            if(Assign(op,map,vip,Set(op,barrier,goal,now),now))return true;
            goal=podGoal=IntVec3.Invalid;return false;
        }
        internal static bool TryPodFallbackR042(RHFOperation op,Map map,Pawn vip,ThingDef launcher,ThingDef pod,int now,bool indoorOnly)
        {
            if(op.FortifyPrison)return false; // R043: do not demolish rooms merely to move the pad.
            Thing barrier=FindPodBarrierFallbackR042(op,map,vip,launcher,pod,indoorOnly,out IntVec3 goal);
            if(barrier==null)return false;
            Log.Message("[NJR][R042][PodSite] R040 accessible alternative via "+barrier.Position+" to "+goal);
            return Assign(op,map,vip,Set(op,barrier,goal,now),now);
        }
        internal static bool IsOwnSealR043(RHFOperation op,Thing wall)
        {
            return op!=null && op.Phase==2 && op.FortifyPrison && wall!=null && wall.Spawned &&
                !wall.Destroyed && wall.def==ThingDefOf.Wall && op.PodCargoOwnerFactionS311!=null && wall.Faction==op.PodCargoOwnerFactionS311 &&
                (op.CompletedBarricades.Contains(wall.Position) || op.BarricadeCell==wall.Position || op.PlannedBarricadesR055.Contains(wall.Position));
        }
        internal static bool IsOwnExitStructureR054(RHFOperation op,Thing structure)
        {
            return IsOwnSealR043(op,structure) || (op!=null && op.Phase==2 && op.FortifyPrison &&
                op.PodLaunchCompleted && structure!=null && structure==op.PodLauncherThing &&
                structure.Spawned && !structure.Destroyed && structure.def.defName=="PodLauncher" &&
                structure.Faction==op.PodCargoOwnerFactionS311 && structure.Faction!=Faction.OfPlayer);
        }
        private static Thing ExitOwnedAtR054(RHFOperation op,Map map,IntVec3 cell)
        {
            foreach(Thing item in map.thingGrid.ThingsListAt(cell))
                if(IsOwnExitStructureR054(op,item))return item;
            return null;
        }
        // Chunks may share a cell with a chair or other traversable furniture.
        // For evacuation ONLY, moving the chunk is legal without destroying furniture.
        private static Thing ExitChunkAtR054(Map map,IntVec3 cell)
        {
            Thing chunk=NJRVipDebrisR045.DebrisAt(map,cell);
            if(chunk==null || map.terrainGrid.TerrainAt(cell)?.passability==Traversability.Impassable)return null;
            foreach(Thing item in map.thingGrid.ThingsListAt(cell))
                if(item!=chunk && item.def?.passability==Traversability.Impassable && !NJRVipDebrisR045.IsDebris(item))return null;
            return chunk;
        }
        private static bool AssignOwnExitR043(RHFOperation op,Map map,Pawn navigator,Thing wall,int now)
        {
            if(!IsOwnExitStructureR054(op,wall))return false;
            Pawn worker=null;int best=int.MaxValue;
            foreach(Pawn member in op.Squad)
            {
                if(!Active(member,map) || member.Faction!=wall.Faction || member.WorkTypeIsDisabled(WorkTypeDefOf.Construction) ||
                    !member.CanReach(wall,PathEndMode.Touch,Danger.Deadly) ||
                    member.health?.capacities?.CapableOf(PawnCapacityDefOf.Manipulation)!=true)continue;
                int score=(member.Position-wall.Position).LengthHorizontalSquared -
                    (member.skills?.GetSkill(SkillDefOf.Construction)?.Level??0)*10;
                if(member.jobs?.curJob?.def?.defName=="NJR_ReopenOwnSealR051" &&
                   member.jobs.curJob.targetA.Thing==wall)score-=100000;
                if(score<best){best=score;worker=member;}
            }
            if(worker==null)return false;
            Clear(op);
            RHFTacticalBoardS41 board=op.BoardS41??=new RHFTacticalBoardS41();
            board.Barrier=null;board.Breacher=null;
            foreach(Pawn member in op.Squad)
                if(Active(member,map))board.Assign(member,member==worker?RHFRoleS41.Engineer:RHFRoleS41.Hold,
                    member==worker?wall:null,member.Position,now);
            foreach(Pawn freed in op.Freed)
                if(Active(freed,map))board.Assign(freed,RHFRoleS41.Hold,null,freed.Position,now);
            board.Decision="R054 reopen own exit obstruction "+wall.def.defName+" "+wall.Position;
            board.LastPlanTick=now;board.NextReplanTick=now+90;
            return true;
        }
        internal static bool TryExitDebrisR047(RHFOperation op,Map map,Pawn navigator,int now)
        {
            if(!op.FortifyPrison || !op.PodLaunchCompleted || op.Phase!=2)return false;
            RHFTacticalBoardS41 board=op.BoardS41??=new RHFTacticalBoardS41();
            Pawn mover=null;Thing chunk=null;IntVec3 destination=IntVec3.Invalid;
            foreach(Pawn member in op.Squad)
            {
                if(!Active(member,map) || member.jobs?.curJob?.def?.defName!="NJR_MoveSealCargoR044")continue;
                RHFOrderS41 order=board.For(member,false);
                if(order?.Objective==null || !NJRVipDebrisR045.IsDebris(order.Objective))continue;
                mover=member;chunk=order.Objective;destination=order.Cell;break;
            }
            if(mover==null)
            {
                
                Field corridor=Search(map,navigator,0,true,true,true,op,true);
                if(corridor==null || !corridor.Exit.IsValid)return false;
                chunk=corridor.BarrierAt(corridor.Exit);
                if(!NJRVipDebrisR045.IsDebris(chunk) || !NJRVipDebrisR045.TryMove(op,map,chunk,null,null,null,out mover,out destination,corridor.Path()))return false;
                Log.Message("[NJR][R047][Exit] physically clear corridor debris "+chunk.Position+" -> "+destination);
            }
            return AssignExitDebrisR053(op,map,mover,chunk,destination,now);
        }
        private static bool AssignExitDebrisR053(RHFOperation op,Map map,Pawn mover,Thing chunk,IntVec3 destination,int now)
        {
            var board=op.BoardS41;
            Clear(op);board.Barrier=null;board.Breacher=null;
            var used=new HashSet<IntVec3>{destination};
            foreach(Pawn member in op.Squad)
            {
                if(!Active(member,map))continue;
                if(member==mover){board.Assign(member,RHFRoleS41.PodSupply,chunk,destination,now);continue;}
                IntVec3 slot=NJRRescueFormationR032.SpreadNear(member,member.Position,map,used,0,5,true);
                if(slot.IsValid){used.Add(slot);board.Assign(member,RHFRoleS41.Screen,null,slot,now);}
                else board.Assign(member,RHFRoleS41.Hold,null,member.Position,now);
            }
            board.Decision="R047 clear actual exit corridor";board.NextReplanTick=now+90;board.LastPlanTick=now;
            return true;
        }
        private sealed class ExitRetryR054
        {
            internal readonly Dictionary<Pawn,int> Until=new Dictionary<Pawn,int>();
        }
        private static readonly ConditionalWeakTable<RHFOperation,ExitRetryR054> ExitRetriesR054=new ConditionalWeakTable<RHFOperation,ExitRetryR054>();
        // R056: assign an actual reachable firing/strike position on a turret
        // covering the proved exit corridor, not an arbitrary map-wide target.
        private static bool TryExitCombatR056(RHFOperation op,Map map,Pawn navigator,Field corridor,Thing first,int now)
        {
            var path=corridor.Path();var local=new List<IntVec3>();
            foreach(IntVec3 cell in path)
                if((cell-navigator.Position).LengthHorizontalSquared<=196 && cell.Standable(map) &&
                   navigator.CanReach(cell,PathEndMode.OnCell,Danger.Deadly))local.Add(cell);
            Building target=null;Pawn fighter=null;IntVec3 fire=IntVec3.Invalid;int best=int.MaxValue;
            foreach(Building gun in map.listerBuildings.allBuildingsColonist)
            {
                if(!RHFCombatPlannerV125.IsTurret(gun) || (gun.Position-navigator.Position).LengthHorizontalSquared>900)continue;
                bool relevant=gun==first;
                foreach(IntVec3 cell in local)if(!relevant && RHFObjectiveRouteV128.TurretCoversR037(gun,map,cell)){relevant=true;break;}
                if(!relevant)continue;
                foreach(Pawn member in op.Squad)
                {
                    if(!Active(member,map) || member.WorkTagIsDisabled(WorkTags.Violent) ||
                       RHFCombatPlannerV125.IsCombatEmergencyJob(member))continue;
                    float range=RHFCombatPlannerV125.WeaponRange(member);
                    IntVec3 origin=member.Position;
                    // Small physical search around the soldier, including both
                    // sides of an opened doorway. CE still controls every shot.
                    for(int dx=-8;dx<=8;dx++)for(int dz=-8;dz<=8;dz++)
                    {
                        IntVec3 cell=origin+new IntVec3(dx,0,dz);
                        int dist=(cell-gun.Position).LengthHorizontalSquared;
                        bool melee=dist==1;
                        bool ranged=range>2f && dist<=range*range;
                        if(!melee && !ranged || !cell.InBounds(map) || !cell.Standable(map))continue;
                        Pawn occupant=cell.GetFirstPawn(map);
                        if(occupant!=null && occupant!=member)continue;
                        bool shoot=ranged && GenSight.LineOfSight(cell,gun.Position,map) &&
                            !RHFRescueFireDisciplineV136.UnsafeLineFrom(op,member,gun,cell);
                        if(!shoot && !melee)continue;
                        int rank=dx*dx+dz*dz+(shoot?0:1000)+(gun.Position-navigator.Position).LengthHorizontalSquared;
                        if(rank>=best || !member.CanReach(cell,PathEndMode.OnCell,Danger.Deadly))continue;
                        target=gun;fighter=member;fire=cell;best=rank;
                    }
                }
            }
            if(fighter==null)return false;
            Clear(op);var board=op.BoardS41;board.Barrier=null;board.Breacher=null;board.Threat=target;
            var assigned=new HashSet<IntVec3>{fire};int count=1;
            board.Assign(fighter,RHFRoleS41.SuppressTurret,target,fire,now);
            foreach(Pawn member in op.Squad)
            {
                if(!Active(member,map) || member==fighter)continue;
                IntVec3 slot=IntVec3.Invalid;int cost=int.MaxValue;
                if(count<3 && !member.WorkTagIsDisabled(WorkTags.Violent) && !RHFCombatPlannerV125.IsCombatEmergencyJob(member))
                {
                    float range=RHFCombatPlannerV125.WeaponRange(member);
                    if(range>2f)for(int dx=-6;dx<=6;dx++)for(int dz=-6;dz<=6;dz++)
                    {
                        IntVec3 c=member.Position+new IntVec3(dx,0,dz);int d=dx*dx+dz*dz;
                        if(d>=cost || assigned.Contains(c) || !c.InBounds(map) || !c.Standable(map) ||
                           (c-target.Position).LengthHorizontalSquared>range*range || !GenSight.LineOfSight(c,target.Position,map) ||
                           RHFRescueFireDisciplineV136.UnsafeLineFrom(op,member,target,c) ||
                           !member.CanReach(c,PathEndMode.OnCell,Danger.Deadly))continue;
                        Pawn occupant=c.GetFirstPawn(map);if(occupant!=null && occupant!=member)continue;
                        slot=c;cost=d;
                    }
                }
                if(slot.IsValid){assigned.Add(slot);count++;board.Assign(member,RHFRoleS41.SuppressTurret,target,slot,now);}
                else board.Assign(member,RHFRoleS41.Hold,null,member.Position,now);
            }
            Log.Message("[NJR][R056][Exit] corridor turret="+target.Position+" actor="+fighter.LabelShortCap+" cell="+fire+" fighters="+count);
            return true;
        }
        internal static bool TryExitRepairR054(RHFOperation op,Map map,Pawn navigator,int now)
        {
            if(!Active(navigator,map) || !op.PodLaunchCompleted)return false;
            var board=op.BoardS41;
            // Preserve any genuine recovery job through its physical completion.
            foreach(Pawn member in op.Squad)
            {
                if(!Active(member,map))continue;
                RHFOrderS41 order=board.For(member,false);Job job=member.jobs?.curJob;
                if(order==null || job==null)continue;
                if(job.def?.defName=="NJR_ReopenOwnSealR051" && IsOwnExitStructureR054(op,order.Objective))return true;
                if(job.def?.defName=="NJR_MoveSealCargoR044" && NJRVipDebrisR045.IsDebris(order.Objective) &&
                   (order.Objective.Spawned && order.Objective.Position!=order.Cell || member.carryTracker?.CarriedThing!=null))return true;
            }
            if(States.TryGetValue(op,out State state) && state.Phase==2 && Live(state.Barrier,map) &&
               now-state.Started<9000 && Assign(op,map,navigator,state,now))return true;
            var retry=ExitRetriesR054.GetValue(op,_=>new ExitRetryR054());
            if(retry.Until.TryGetValue(navigator,out int until) && now<until)return false;
            var excluded=new HashSet<Thing>();
            var contested=new List<Field>();
            // Try multiple distinct first actions, door routes before structural
            // alternatives. Do not let an exposed strike square veto all exits.
            for(int attempt=0;attempt<5;attempt++)
            {
                Field corridor=Search(map,navigator,0,true,true,attempt<3,op,true,excluded);
                if(corridor==null || !corridor.Exit.IsValid)
                    corridor=Search(map,navigator,0,true,true,false,op,true,excluded);
                if(corridor==null || !corridor.Exit.IsValid)break;
                Thing first=corridor.BarrierAt(corridor.Exit);
                if(first==null)break; // only native Eligible may authorize an open exit
                if(IsOwnExitStructureR054(op,first))
                {if(AssignOwnExitR043(op,map,navigator,first,now))return true;}
                else if(NJRVipDebrisR045.IsDebris(first))
                {
                    if(NJRVipDebrisR045.TryMove(op,map,first,null,null,null,out Pawn mover,out IntVec3 dst,corridor.Path()))
                        return AssignExitDebrisR053(op,map,mover,first,dst,now);
                }
                else if(CanWork(op,map,first) && Assign(op,map,navigator,Set(op,first,corridor.Exit,now),now))return true;
                // A real door corridor under fire should not be replaced by a
                // gratuitous wall breach while an executable gun job exists.
                if((first is Building_Door || first is Building blockingGun && RHFCombatPlannerV125.IsTurret(blockingGun)) &&
                   TryExitCombatR056(op,map,navigator,corridor,first,now))return true;
                contested.Add(corridor);
                excluded.Add(first);
            }
            // If all safe actions failed, suppress the relevant defenders, then
            // retry the original route on the next strategic pulse.
            foreach(Field corridor in contested)
                if(TryExitCombatR056(op,map,navigator,corridor,corridor.BarrierAt(corridor.Exit),now))return true;
            // Combat sometimes has no available firing lane. A real reachable
            // strike position on a proved route is still preferable to HOLD forever.
            foreach(Field corridor in contested)
            {
                Thing first=corridor.BarrierAt(corridor.Exit);
                if(!CanWork(op,map,first,true))continue;
                State exposed=Set(op,first,corridor.Exit,now);exposed.AllowExposedExitR056=true;
                if(Assign(op,map,navigator,exposed,now))
                {Log.Message("[NJR][R056][Exit] contested physical breach "+first.Position);return true;}
            }
            // Independent established door-only edge proof as a final fallback.
            Building_Door proved=NJRRescueExitS41.FirstBlockedExitDoor(map,op,navigator);
            if(proved!=null && CanWork(op,map,proved,true))
            {
                State independent=Set(op,proved,IntVec3.Invalid,now);independent.AllowExposedExitR056=true;
                if(Assign(op,map,navigator,independent,now))return true;
            }
            retry.Until[navigator]=now+150;
            NJRRescueRallyR033.NoteOnce(op,"[NJR][R056][Exit] no executable action from "+navigator.Position+
                " rejected="+excluded.Count,navigator.thingIDNumber^560056);
            return false;
        }
        // Bounded local split proof: remove only loose debris (or a mission
        // seal) that directly joins the trapped pocket to a native reachable ally.
        internal static bool TryLocalPocketR053(RHFOperation op,Map map,Pawn isolated,int now)
        {
            if(!op.ExitCell.IsValid || !Active(isolated,map))return false;
            var outside=new List<Pawn>();foreach(Pawn p in op.Squad)
                if(Active(p,map) && p!=isolated && p.CanReach(op.ExitCell,PathEndMode.OnCell,Danger.Deadly))outside.Add(p);
            if(outside.Count==0)return false;
            var pocket=new HashSet<IntVec3>{isolated.Position};var queue=new Queue<IntVec3>();queue.Enqueue(isolated.Position);
            var boundary=new HashSet<IntVec3>();
            while(queue.Count>0 && pocket.Count<=512)
            {
                IntVec3 at=queue.Dequeue();
                foreach(IntVec3 dir in GenAdj.CardinalDirections)
                {
                    IntVec3 c=at+dir;
                    if(!c.InBounds(map) || (c-isolated.Position).LengthHorizontalSquared>144 || pocket.Contains(c))continue;
                    if(NJRVipDebrisR045.DebrisAt(map,c)!=null || IsOwnSealR043(op,c.GetEdifice(map)))
                    {boundary.Add(c);continue;}
                    if(c.Standable(map) && isolated.CanReach(c,PathEndMode.OnCell,Danger.Deadly) && pocket.Add(c))queue.Enqueue(c);
                }
            }
            foreach(IntVec3 c in boundary)
            {
                bool joins=false;foreach(IntVec3 dir in GenAdj.CardinalDirections)
                {
                    IntVec3 n=c+dir;if(!n.InBounds(map) || pocket.Contains(n) || !n.Standable(map))continue;
                    foreach(Pawn ally in outside)if(ally.CanReach(n,PathEndMode.OnCell,Danger.Deadly)){joins=true;break;}
                    if(joins)break;
                }
                if(!joins)continue;
                Thing chunk=NJRVipDebrisR045.DebrisAt(map,c);
                if(chunk!=null)
                {
                    var forbidden=new HashSet<IntVec3>(pocket){c};foreach(IntVec3 dir in GenAdj.CardinalDirections)forbidden.Add(c+dir);
                    if(NJRVipDebrisR045.TryMove(op,map,chunk,null,null,null,out Pawn mover,out IntVec3 dst,forbidden))
                    {Log.Message("[NJR][R053][Exit] reconnect isolated pocket via debris "+c);return AssignExitDebrisR053(op,map,mover,chunk,dst,now);}
                }
                Thing wall=c.GetEdifice(map);if(IsOwnSealR043(op,wall) && AssignOwnExitR043(op,map,isolated,wall,now))return true;
            }
            return false;
        }
        internal static bool TryExitAccess(RHFOperation op,Map map,Pawn navigator,int now)
        {
            if(States.TryGetValue(op,out State state) && state.Phase==2)
            {
                if(Live(state.Barrier,map) && now-state.Started<=9000 &&
                   (!op.FortifyPrison || state.Barrier is Building_Door ||
                    state.Barrier.HitPoints<state.Barrier.MaxHitPoints || now<state.NextSearch) &&
                   Assign(op,map,navigator,state,now))return true;
                if(state.Barrier==null && now<state.NextSearch && (!op.FortifyPrison || state.NavigatorId==navigator.thingIDNumber))return false;
            }
            // The same complete door-only access search precedes destructive fallback
            // after VIP launch or an unavoidable land extraction.
            Field f=op.FortifyPrison?Search(map,navigator,0,true,true,true,op):null;
            if(op.FortifyPrison && (f==null || !f.Exit.IsValid))
            {
                // Independent established Rescue-1 solver: door-only full edge proof.
                Building_Door door=NJRRescueExitS41.FirstBlockedExitDoor(map,op,navigator);
                if(door!=null)
                {
                    if(CanWork(op,map,door))
                    {
                        Log.Message("[NJR][R044][Exit] native door corridor fallback "+door.Position);
                        State proved=Set(op,door,IntVec3.Invalid,now);proved.ExitDoorFailureR044=-1;
                        return Assign(op,map,navigator,proved,now);
                    }
                    // A proved door corridor without an available strike cell is
                    // a movement problem, not permission to destroy a nearby corner.
                    State waiting=Set(op,null,IntVec3.Invalid,now);waiting.ExitDoorFailureR044=-1;waiting.NavigatorId=navigator.thingIDNumber;
                    return false;
                }
                RHFObjectiveRouteV128.DoorGridR044(map,navigator.Position,navigator.Position,"exit door proof failed");
                State retry=States.GetValue(op,_=>new State());
                if(retry.Phase!=2 || retry.ExitDoorFailureR044<0)
                {
                    retry=Set(op,null,IntVec3.Invalid,now);retry.ExitDoorFailureR044=now;retry.NavigatorId=navigator.thingIDNumber;
                    retry.NextSearch=now+180;return false;
                }
                if(now-retry.ExitDoorFailureR044<180)return false;
                retry.ExitDoorFailureR044=-1;
            }
            if(f==null || !f.Exit.IsValid)
                f=Search(map,navigator,0,true,op.FortifyPrison,false,op.FortifyPrison?op:null);
            Thing barrier=f!=null && f.Exit.IsValid?f.BarrierAt(f.Exit):null;
            if(IsOwnSealR043(op,barrier))return AssignOwnExitR043(op,map,navigator,barrier,now);
            if(!CanWork(op,map,barrier)){Set(op,null,IntVec3.Invalid,now).NavigatorId=navigator.thingIDNumber;return false;}
            return Assign(op,map,navigator,Set(op,barrier,f.Exit,now),now);
        }
    }

    // Shared immediate authorization, including delayed drop arrivals before
    // their Lord is claimed on the next operation pulse. No unrelated pawn is
    // changed, and no actual turret/defender or emergency job is suppressed.
    [HarmonyPatch(typeof(Pawn_JobTracker),"StartJob")]
    internal static class NJRRescueStructureGateR040
    {
        private static readonly FieldInfo PawnField=AccessTools.Field(typeof(Pawn_JobTracker),"pawn");
        private static IntVec3 DisengageR042(Pawn pawn,Pawn other)
        {
            if(pawn?.Map==null || other?.Map!=pawn.Map || pawn.Downed)return IntVec3.Invalid;
            Map map=pawn.Map;
            IntVec3 best=IntVec3.Invalid;int score=int.MaxValue;
            for(int dx=-5;dx<=5;dx++)for(int dz=-5;dz<=5;dz++)
            {
                IntVec3 cell=pawn.Position+new IntVec3(dx,0,dz);
                if(!cell.InBounds(map) || !cell.Standable(map) ||
                   cell.GetEdifice(map)!=null || cell.GetFirstPawn(map)!=null ||
                   (cell-other.Position).LengthHorizontalSquared<9 ||
                   cell.GetRoom(map)!=pawn.Position.GetRoom(map) ||
                   RHFObjectiveRouteV128.StagingExposedToTurret(map,cell) ||
                   !pawn.CanReach(cell,PathEndMode.OnCell,Danger.Some))continue;
                int cost=dx*dx+dz*dz;
                if(cost<score){score=cost;best=cell;}
            }
            return best;
        }
        private static void Prefix(Pawn_JobTracker __instance,ref Job __0)
        {
            if(__0==null || PawnField==null)return;
            Thing target=__0.targetA.Thing;
            Building structure=target as Building;
            Pawn other=target as Pawn;
            if(structure==null && !(other?.IsPrisonerOfColony==true))return;
            if(structure!=null && RHFCombatPlannerV125.IsTurret(structure))return;
            string def=__0.def?.defName??"";
            if(def!="AttackMelee" && def!="AttackStatic" && def!="Ignite" && def!="UseVerbOnThing")return;
            Pawn pawn=PawnField.GetValue(__instance) as Pawn;
            if(pawn?.Spawned!=true || pawn.Faction==Faction.OfPlayer || pawn.Map==null)return;
            var operations=RHFMissionIntegrityV130.Operations(pawn.Map);
            if(operations==null)return;
            foreach(RHFOperation op in operations)
            {
                if(op==null || op.Finished || !op.Deployed || op.Type!=(int)RHFOperationType.Rescue ||
                   op.Phase>3)continue;
                bool soldier=op.Squad?.Contains(pawn)==true;
                bool beast=op.FortifyPrison && op.NativeAnimalsS41?.Contains(pawn)==true;
                bool freed=op.FortifyPrison && op.Freed?.Contains(pawn)==true;
                if(!soldier && !beast && !freed)continue;
                if(other!=null)
                {
                    if(!op.FortifyPrison)return;
                    // Actual recent damage authorizes a bounded melee response, even
                    // when the prisoner immediately goes back to a Wander job.
                    if(soldier && !freed && NJRPrisonerDefenseR047.MayDefend(op,pawn,other))
                    {__0=NJRPrisonerDefenseR047.Counter(op,pawn);return;}
                    // The recorded prisoner initiated a melee fight. Break contact
                    // when possible rather than repeatedly counterattacking.
                    IntVec3 away=DisengageR042(pawn,other);
                    if(away.IsValid)
                    {
                        __0=JobMaker.MakeJob(JobDefOf.Goto,away);__0.expiryInterval=180;
                        return;
                    }
                    // No verified incoming contact: never initiate an attack
                    // on another captive, even if vanilla considers them hostile.
                    __0=JobMaker.MakeJob(JobDefOf.Wait,pawn.Position);__0.expiryInterval=30;
                    return;
                }
                RHFOrderS41 order=op.BoardS41?.For(pawn,false);
                bool permitted=soldier && def!="Ignite" && RHFObjectiveRouteV128.IsRescueStructuralBarrier(structure) &&
                    (op.Phase==1 && op.BoardS41?.PermitsAssaultBreach(pawn,structure)==true ||
                     op.Phase==2 && op.BoardS41?.PermitsAssaultBreach(pawn,structure)==true &&
                         NJRRescueAccessR040.PermitsExit(op,structure) ||
                     op.Phase==3 && order?.Role==(int)RHFRoleS41.Breach && order.Objective==structure &&
                         (op.PodConstructionStage!=0 || op.BoardS41.PermitsAssaultBreach(pawn,structure)));
                if(!permitted)
                {
                    NJRRescueRallyR033.NoteOnce(op,"[NJR][R040] prevented unauthorized structure job: "+pawn.LabelShortCap+" / "+structure.LabelShortCap,
                        pawn.thingIDNumber^structure.thingIDNumber^400040);
                    __0=JobMaker.MakeJob(JobDefOf.Wait,pawn.Position);__0.expiryInterval=30;
                }
                return;
            }
        }
    }
}
