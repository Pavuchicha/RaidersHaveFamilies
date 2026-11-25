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
        private static bool CanWork(RHFOperation op,Map map,Thing barrier)
        {
            if(!Live(barrier,map))return false;
            var excluded=new HashSet<Pawn>();var used=new HashSet<IntVec3>();
            if(op.Freed!=null)foreach(Pawn freed in op.Freed)
                if(freed!=null && freed.Spawned && freed.Map==map)used.Add(freed.Position);
            for(int i=0;i<op.Squad.Count;i++)
            {
                Pawn actor=RHFCombatPlannerV125.SelectBreachOperator(op,map,barrier,excluded);
                if(actor==null)break;
                if(NJRRescueFormationR032.BreachSlot(actor,barrier,map,used,op.Squad,true,
                    IntVec3.Invalid,true).IsValid)return true;
                excluded.Add(actor);
            }
            return false;
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
            internal readonly int[] Cost;
            internal readonly Thing[] First;
            internal IntVec3 Exit=IntVec3.Invalid;
            internal Field(Map map)
            {Width=map.Size.x;Height=map.Size.z;Cost=new int[Width*Height];First=new Thing[Cost.Length];Array.Fill(Cost,int.MaxValue);}
            internal int Id(IntVec3 c)=>c.z*Width+c.x;
            internal int At(IntVec3 c)=>Cost[Id(c)];
            internal Thing BarrierAt(IntVec3 c)=>First[Id(c)];
        }
        private static int Edge(Map map,IntVec3 c)=>Math.Min(Math.Min(c.x,map.Size.x-1-c.x),Math.Min(c.z,map.Size.z-1-c.z));
        private static Field Search(Map map,Pawn navigator,int radius,bool toEdge,bool preferVipDoors=false,bool doorsOnly=false,RHFOperation ownExit=null)
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
                if(toEdge && Edge(map,at)<=(preferVipDoors?1:0) && at.Standable(map) && at.GetEdifice(map)==null && !at.Roofed(map))
                {f.Exit=at;break;}
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
                    if(step<0)continue;
                    if(hazards[nid]<0)hazards[nid]=danger(next);
                    step+=hazards[nid];
                    if(f.Cost[id]>int.MaxValue-step || f.Cost[id]+step>=f.Cost[nid])continue;
                    f.Cost[nid]=f.Cost[id]+step;f.First[nid]=f.First[id]??obstacle;
                    heap.Enqueue(nid,f.Cost[nid]+(toEdge?Edge(map,next)*10:0));
                }
            }
            return f;
        }
        // Compare ALL local pads, including those behind a verified door,
        // in one cost field. No outdoor-first short circuit or arbitrary rotation.
        private static Thing FindPodBarrier(RHFOperation op,Map map,Pawn vip,ThingDef launcher,ThingDef pod,
            out IntVec3 goal,out IntVec3 podGoal)
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
                if(op.FortifyPrison && tier==3 && barrier!=null)continue;
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
                if(!WorkLaneR041(map,vip,footprint,launchSlots,podSlots,barrier)){noLane++;continue;}
                validPads++;
                scoreBest=score;tierBest=tier;best=barrier;goal=cell;podGoal=vehicle;
            }
            Log.Message("[NJR][R041][PodSite] checked="+checkedPads+" valid="+validPads+
                " launcher="+goal+" pod="+podGoal+" barrier="+(best?.LabelShortCap.ToString()??"none")+
                " tier="+tierBest+" score="+scoreBest+" footprintRejected="+blockedFootprint+" roomRejected="+roomMismatch+
                " slotsRejected="+noSlots+" workerRejected="+noWorker+" laneRejected="+noLane);
            return best;
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
            HashSet<IntVec3> launchSlots,HashSet<IntVec3> podSlots,Thing barrier)
        {
            var seen=new HashSet<IntVec3>();var queue=new Queue<IntVec3>();
            seen.Add(vip.Position);queue.Enqueue(vip.Position);
            IntVec3 launch=IntVec3.Invalid,pod=IntVec3.Invalid;
            while(queue.Count>0)
            {
                IntVec3 at=queue.Dequeue();
                bool isLaunch=launchSlots.Contains(at),isPod=podSlots.Contains(at);
                if((isLaunch || isPod) && (barrier!=null || vip.CanReach(at,PathEndMode.OnCell,Danger.Deadly)))
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
                    if(!c.Standable(map) && !(barrier!=null && (edifice==barrier || edifice is Building_Door)))continue;
                    if(edifice is Building_Door door && !door.Open && barrier==null && RHFObjectiveRouteV128.IsRescueStructuralBarrier(door))continue;
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
            state.NextSearch=now+600;state.Failed.Clear();
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
                Pawn actor=RHFCombatPlannerV125.SelectBreachOperator(op,map,barrier,excluded);
                if(actor==null)break;
                excluded.Add(actor);
                if(actors.Count>0 && actor.equipment?.Primary?.def?.IsMeleeWeapon!=true)continue;
                RHFOrderS41 old=board.For(actor,false);
                IntVec3 slot=NJRRescueFormationR032.BreachSlot(actor,barrier,map,used,op.Squad,true,
                    old!=null && old.Objective==barrier?old.Cell:IntVec3.Invalid,true);
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
                (op.CompletedBarricades.Contains(wall.Position) || op.BarricadeCell==wall.Position);
        }
        private static bool AssignOwnExitR043(RHFOperation op,Map map,Pawn navigator,Thing wall,int now)
        {
            if(!IsOwnSealR043(op,wall))return false;
            Pawn worker=null;int best=int.MaxValue;
            foreach(Pawn member in op.Squad)
            {
                if(!Active(member,map) || member.Faction!=wall.Faction || member.WorkTypeIsDisabled(WorkTypeDefOf.Construction) ||
                    !member.CanReach(wall,PathEndMode.Touch,Danger.Deadly) ||
                    member.health?.capacities?.CapableOf(PawnCapacityDefOf.Manipulation)!=true)continue;
                int score=(member.Position-wall.Position).LengthHorizontalSquared;
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
            board.Decision="R043 reopen own sealed wall on exit corridor "+wall.Position;
            board.LastPlanTick=now;board.NextReplanTick=now+90;
            return true;
        }
        internal static bool TryExitAccess(RHFOperation op,Map map,Pawn navigator,int now)
        {
            if(States.TryGetValue(op,out State state) && state.Phase==2)
            {
                if(Live(state.Barrier,map) && now-state.Started<=9000 &&
                   (!op.FortifyPrison || state.Barrier is Building_Door ||
                    state.Barrier.HitPoints<state.Barrier.MaxHitPoints || now<state.NextSearch) &&
                   Assign(op,map,navigator,state,now))return true;
                if(state.Barrier==null && now<state.NextSearch)return false;
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
                    State waiting=Set(op,null,IntVec3.Invalid,now);waiting.ExitDoorFailureR044=-1;
                    return false;
                }
                RHFObjectiveRouteV128.DoorGridR044(map,navigator.Position,navigator.Position,"exit door proof failed");
                State retry=States.GetValue(op,_=>new State());
                if(retry.Phase!=2 || retry.ExitDoorFailureR044<0)
                {
                    retry=Set(op,null,IntVec3.Invalid,now);retry.ExitDoorFailureR044=now;
                    retry.NextSearch=now+180;return false;
                }
                if(now-retry.ExitDoorFailureR044<180)return false;
                retry.ExitDoorFailureR044=-1;
            }
            if(f==null || !f.Exit.IsValid)
                f=Search(map,navigator,0,true,op.FortifyPrison,false,op.FortifyPrison?op:null);
            Thing barrier=f!=null && f.Exit.IsValid?f.BarrierAt(f.Exit):null;
            if(IsOwnSealR043(op,barrier))return AssignOwnExitR043(op,map,navigator,barrier,now);
            if(!CanWork(op,map,barrier)){Set(op,null,IntVec3.Invalid,now);return false;}
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
                if(!soldier && !beast)continue;
                if(other!=null)
                {
                    if(!op.FortifyPrison || op.Targets?.Contains(other)==true)return;
                    // The recorded prisoner initiated a melee fight. Break contact
                    // when possible rather than repeatedly counterattacking.
                    IntVec3 away=DisengageR042(pawn,other);
                    if(away.IsValid)
                    {
                        __0=JobMaker.MakeJob(JobDefOf.Goto,away);__0.expiryInterval=180;
                        return;
                    }
                    bool activeThreat=other.jobs?.curJob?.targetA.Thing==pawn &&
                        (other.jobs.curJob.def?.defName??"").StartsWith("Attack");
                    if(!activeThreat)
                    {__0=JobMaker.MakeJob(JobDefOf.Wait,pawn.Position);__0.expiryInterval=30;}
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
