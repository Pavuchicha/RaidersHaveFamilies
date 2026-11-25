// S4.1-A/E: Phase 3 single-writer dispatch. Strategic orders ONLY;
// actual construction, refueling and boarding jobs come from ThinkTree.
using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace RaidersHaveFamilies
{
    internal static class RHFPodPlannerS41
    {

        // Stable owner for native work. The old nearest-pawn selector ran every
        // 90 ticks and could repeatedly interrupt builders as they moved.
        internal static bool PinnedEngineerValid(RHFOperation op,Map map,IntVec3 workCell)
        {
            Pawn p=op?.BoardS41?.PodEngineerS41;
            return p!=null && op.Squad!=null && op.Squad.Contains(p) &&
                p.jobs?.curJob?.def?.defName!="NJR_MoveSealCargoR044" &&
                RHFRescuePlannerS41.ActiveForRoleS41(p,map) &&
                p.health?.capacities!=null &&
                p.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation) &&
                !p.WorkTypeIsDisabled(WorkTypeDefOf.Construction) &&
                workCell.IsValid && workCell.InBounds(map) &&
                p.CanReach(workCell,PathEndMode.Touch,Danger.Some);
        }

        internal static void RememberEngineer(RHFOperation op,Pawn engineer)
        {
            if(op?.BoardS41!=null)
                op.BoardS41.PodEngineerS41=engineer;
        }

        private static bool HasParallelOrder(RHFTacticalBoardS41 board,Pawn primary)
        {
            foreach(RHFOrderS41 order in board.Orders)
                if(order!=null && order.Member!=primary && order.Role==(int)RHFRoleS41.Engineer)return true;
            return false;
        }

        internal static void Plan(RHFOperation op,Map map,Pawn engineer,Thing work,int now,
            Pawn fortifier=null,Thing parallelWall=null,Pawn refueler=null,Thing launcher=null,
            Pawn sealMover=null,Thing sealCargo=null,IntVec3 sealDestination=default)
        {
            if(op==null || map==null || op.Phase!=3 || op.Finished)return;
            RHFTacticalBoardS41 board=op.BoardS41 ??= new RHFTacticalBoardS41();
            board.Prune(map);
            if(board.ObservedPhase!=3 || (board.PodWorkStageS41<1 && op.PodConstructionStage>=1))
            {
                board.ObservedPhase=3;
                board.LastPlanTick=0;
                board.NextReplanTick=0;
                board.Orders.Clear();
            }
            if(now<board.NextReplanTick && board.LastPlanTick>0 &&
               board.PodWorkTargetS41==work && board.PodWorkStageS41==op.PodConstructionStage &&
               fortifier==null && refueler==null && sealMover==null && !HasParallelOrder(board,engineer))
                return;
            board.PodWorkTargetS41=work;
            board.PodWorkStageS41=op.PodConstructionStage;
            board.LastPlanTick=now;
            board.NextReplanTick=now+120;
            board.Decision="Phase3 native build/load: stage "+op.PodConstructionStage;
            IntVec3 assembly=ChooseAssemblyCell(op,map);
            // Do not move the VIP's assembly point after every 120-tick plan.
            // The old nearest-cell heuristic chased its own moving target.
            RHFOrderS41 vipOrder=board.For(op.Vip,false);
            if(vipOrder!=null && vipOrder.Role==(int)RHFRoleS41.PodAssembly &&
               vipOrder.Cell.IsValid && vipOrder.Cell.InBounds(map) &&
               vipOrder.Cell.Standable(map) && op.Vip!=null &&
               !NJRPodSiteClearS41.ReservedFootprint(op,vipOrder.Cell) &&
               (!assembly.IsValid || assembly.GetRoom(map)==null || vipOrder.Cell.GetRoom(map)==assembly.GetRoom(map)) &&
               op.Vip.CanReach(vipOrder.Cell,PathEndMode.OnCell,Danger.Some))
                assembly=vipOrder.Cell;
            var occupied=new HashSet<Pawn>();
            // Supplies arrived in soldier inventories, NOT as spawned loot.
            // Carriers must physically travel to the build site to unpack.
            if(op.PodConstructionStage>=1 && op.PodConstructionStage<=3 && op.Squad!=null)
            for(int i=0;i<op.Squad.Count;i++)
            {
                Pawn carrier=op.Squad[i];
                if(!RHFRescuePlannerS41.ActiveForRoleS41(carrier,map) ||
                   !RHFPodSupplyS41.HasCargo(op,carrier))continue;
                IntVec3 unload=RHFPodSupplyS41.UnloadCell(carrier,op,map);
                if(!unload.IsValid)continue;
                board.Assign(carrier,RHFRoleS41.PodSupply,null,unload,now);
                occupied.Add(carrier);
            }
            if(sealMover!=null && sealMover!=engineer && sealMover!=refueler &&
               !occupied.Contains(sealMover) && sealCargo!=null && sealDestination.IsValid)
            {
                board.Assign(sealMover,RHFRoleS41.PodSupply,sealCargo,sealDestination,now);
                occupied.Add(sealMover);
            }
            if(work is Building wall &&
               RHFCombatPlannerV125.RequiresBreach(wall))
            {
                Pawn breacher=null;
                foreach(Pawn fighter in op.Squad)
                    if(RHFRescuePlannerS41.ActiveForRoleS41(fighter,map) &&
                       fighter.health?.capacities?.CapableOf(PawnCapacityDefOf.Manipulation)==true &&
                       fighter.CanReach(wall.Position,PathEndMode.Touch,Danger.Deadly) &&
                       (breacher==null || RHFTactical.IsBreachAxe(fighter)))
                    {breacher=fighter;if(RHFTactical.IsBreachAxe(fighter))break;}
                if(breacher!=null)
                {
                    board.Assign(breacher,RHFRoleS41.Breach,wall,wall.Position,now);
                    occupied.Add(breacher);
                }
            }
            if(engineer!=null && !occupied.Contains(engineer) &&
               RHFRescuePlannerS41.ActiveForRoleS41(engineer,map) &&
               op.PodConstructionStage>=1 && op.PodConstructionStage<=4 && work!=null &&
               !(work is Building hostileWall && hostileWall.Faction==Faction.OfPlayer) &&
               work.Spawned && work.Map==map)
            {
                board.Assign(engineer,RHFRoleS41.Engineer,work,assembly,now);
                occupied.Add(engineer);
            }
            if(fortifier!=null && fortifier!=engineer && !occupied.Contains(fortifier) &&
               parallelWall!=null && parallelWall.Spawned && parallelWall.Map==map &&
               RHFRescuePlannerS41.ActiveForRoleS41(fortifier,map))
            {
                board.Assign(fortifier,RHFRoleS41.Engineer,parallelWall,assembly,now);
                occupied.Add(fortifier);
            }
            if(refueler!=null && refueler!=engineer && refueler!=fortifier && !occupied.Contains(refueler) &&
               launcher!=null && launcher.Spawned && launcher.Map==map &&
               RHFRescuePlannerS41.ActiveForRoleS41(refueler,map))
            {
                board.Assign(refueler,RHFRoleS41.Engineer,launcher,assembly,now);
                occupied.Add(refueler);
            }
            // The VIP may board only in the real native CompTransporter stage.
            // No external TryTakeOrderedJob or artificial removal from the map.
            if(op.Vip!=null && op.Freed!=null && op.Freed.Contains(op.Vip) &&
               op.Vip.Spawned && op.Vip.Map==map)
            {
                if(op.PodConstructionStage==4 && work!=null && work.Spawned &&
                   work.Map==map && (work as ThingWithComps)?.GetComp<CompTransporter>()!=null)
                    board.Assign(op.Vip,RHFRoleS41.BoardPod,work,assembly,now);
                else
                    board.Assign(op.Vip,RHFRoleS41.PodAssembly,null,assembly,now);
            }
            // Every OTHER liberated prisoner remains with a living escort at
            // the protected assembly point. A VIP pod cannot silently abandon
            // another rescued pawn or make that pawn take a vanilla flee job.
            if(op.Freed!=null)
            for(int i=0;i<op.Freed.Count;i++)
            {
                Pawn freed=op.Freed[i];
                if(freed==null || freed==op.Vip || freed.Dead || !freed.Spawned ||
                   freed.Map!=map)continue;
                RHFRescueCommandV124.StabilizeFreed(op,freed);
                RHFEscortLink link=RHFRescuePlannerS41.EnsureEscort(op,freed,map);
                Pawn escort=link?.Escort;
                if(!RHFRescuePlannerS41.ActiveForRoleS41(escort,map))
                {board.Assign(freed,RHFRoleS41.Hold,null,freed.Position,now);continue;}
                occupied.Add(escort);
                board.Assign(escort,RHFRoleS41.Escort,freed,assembly,now);
                board.Assign(freed,RHFRoleS41.FollowEscort,escort,assembly,now);
            }
            if(op.Squad==null)return;
            // Guard slots are distinct accessible cells; do not overwrite an
            // engineer, escort or existing physical rescue job.
            var positions=new HashSet<IntVec3>();
            if(assembly.IsValid)positions.Add(assembly);
            if(sealDestination.IsValid)positions.Add(sealDestination);
            ReserveWorkCellR041(engineer,work,map,positions,op);
            ReserveWorkCellR041(fortifier,parallelWall,map,positions,op);
            ReserveWorkCellR041(refueler,launcher,map,positions,op);
            for(int i=0;i<op.Squad.Count;i++)
            {
                Pawn soldier=op.Squad[i];
                if(!RHFRescuePlannerS41.ActiveForRoleS41(soldier,map) ||
                   occupied.Contains(soldier))continue;
                RHFOrderS41 previous=board.For(soldier,false);
                IntVec3 cell=IntVec3.Invalid;
                // A stable interior guard slot is a tactical position, not
                // a continuously moving waypoint calculated from current feet.
                if(previous!=null && previous.Role==(int)RHFRoleS41.Screen &&
                   ValidGuardCell(previous.Cell,op,map,positions) &&
                   assembly.IsValid && (previous.Cell-assembly).LengthHorizontalSquared<=144 &&
                   previous.Cell.GetRoom(map)==assembly.GetRoom(map) &&
                   soldier.CanReach(previous.Cell,PathEndMode.OnCell,Danger.Some))
                {cell=previous.Cell;positions.Add(cell);}
                else cell=GuardCell(soldier,assembly,map,positions,op);
                if(cell.IsValid)
                    board.Assign(soldier,RHFRoleS41.Screen,null,cell,now);
                else board.Assign(soldier,RHFRoleS41.Hold,null,soldier.Position,now);
            }
        }

        // The VIP must never be ordered INTO the pod blueprint/launcher footprint.
        // Wait near the assembly point in a real, standable, reachable cell.
        private static IntVec3 ChooseAssemblyCell(RHFOperation op,Map map)
        {
            Pawn vip=op?.Vip;
            IntVec3 site=op!=null && op.PodCell.IsValid ? op.PodCell :
                op!=null ? op.PodLauncherCell : IntVec3.Invalid;
            if(!site.IsValid || !site.InBounds(map))
                return vip!=null && vip.Spawned ? vip.Position : IntVec3.Invalid;
            if(vip==null || !vip.Spawned || vip.Map!=map)return site;
            // Pod assembly is LOCAL. Searching all map cells and calling
            // VIP.CanReach for most of them every 120 ticks caused TPS loss.
            IntVec3 best=IntVec3.Invalid;
            Room siteRoom=site.GetRoom(map)??op.PodLauncherCell.GetRoom(map);
            int score=int.MaxValue;
            for(int x=Math.Max(0,site.x-10);x<=Math.Min(map.Size.x-1,site.x+10);x++)
            for(int z=Math.Max(0,site.z-10);z<=Math.Min(map.Size.z-1,site.z+10);z++)
            {
                IntVec3 cell=new IntVec3(x,0,z);
                if(!cell.Standable(map) || cell.GetEdifice(map)!=null ||
                   InFootprint(cell,op.PodLauncherCell,
                       DefDatabase<ThingDef>.GetNamedSilentFail("PodLauncher"),
                       NJRPodSiteClearS41.RotationFor(op.PodLauncherCell,op.PodCell)) ||
                   InFootprint(cell,op.PodCell,
                       DefDatabase<ThingDef>.GetNamedSilentFail("TransportPod")) ||
                   !vip.CanReach(cell,PathEndMode.OnCell,Danger.Some))continue;
                int candidate=(site-cell).LengthHorizontalSquared*4+
                    (vip.Position-cell).LengthHorizontalSquared+
                    (siteRoom!=null && cell.GetRoom(map)!=siteRoom?10000:0);
                if(candidate<score){best=cell;score=candidate;}
            }
            return best.IsValid ? best : vip.Position;
        }

        private static bool InFootprint(IntVec3 cell,IntVec3 origin,ThingDef def,Rot4 rotation=default)
        {
            return origin.IsValid && def!=null &&
                NJRPodSiteClearS41.Contains(cell,origin,def,rotation);
        }

        private static void ReserveWorkCellR041(Pawn pawn,Thing target,Map map,HashSet<IntVec3> occupied,RHFOperation op)
        {
            if(pawn==null || target==null || !target.Spawned)return;
            IntVec3 best=IntVec3.Invalid;int score=int.MaxValue;
            foreach(IntVec3 tile in GenAdj.OccupiedRect(target.Position,target.Rotation,target.def.size))
            foreach(IntVec3 dir in GenAdj.CardinalDirections)
            {
                IntVec3 c=tile+dir;
                if(!ValidGuardCell(c,op,map,occupied) || !pawn.CanReach(c,PathEndMode.OnCell,Danger.Some))continue;
                int n=(pawn.Position-c).LengthHorizontalSquared;
                if(n<score){best=c;score=n;}
            }
            if(best.IsValid)occupied.Add(best);
        }
        private static bool ValidGuardCell(IntVec3 cell,RHFOperation op,Map map,
            HashSet<IntVec3> occupied)
        {
            if(op.FortifyPrison && cell.IsValid && cell.InBounds(map))
                for(int dx=-1;dx<=1;dx++)for(int dz=-1;dz<=1;dz++)
                {
                    IntVec3 near=cell+new IntVec3(dx,0,dz);
                    if(!near.InBounds(map))continue;
                    Pawn captive=near.GetFirstPawn(map);
                    if(captive!=null && captive.IsPrisonerOfColony &&
                       op.Targets?.Contains(captive)!=true)return false;
                }
            HashSet<IntVec3> pocket=RHFPodSupplyS41.SealPocketR044(op);
            if(pocket!=null && !pocket.Contains(cell))return false;
            return cell.IsValid && cell.InBounds(map) && cell.Standable(map) &&
                cell.GetEdifice(map)==null && !occupied.Contains(cell) && cell!=op.BarricadeCell &&
                !InFootprint(cell,op.PodLauncherCell,
                    DefDatabase<ThingDef>.GetNamedSilentFail("PodLauncher"),
                    NJRPodSiteClearS41.RotationFor(op.PodLauncherCell,op.PodCell)) &&
                !InFootprint(cell,op.PodCell,
                    DefDatabase<ThingDef>.GetNamedSilentFail("TransportPod"));
        }

        private static IntVec3 GuardCell(Pawn pawn,IntVec3 anchor,Map map,
            HashSet<IntVec3> occupied,RHFOperation op)
        {
            if(!anchor.IsValid || !anchor.InBounds(map) || pawn==null)
                return IntVec3.Invalid;
            Room siteRoom=anchor.GetRoom(map);
            Room startRoom=pawn.Position.GetRoom(map);
            IntVec3 best=IntVec3.Invalid;
            int bestScore=int.MaxValue;
            // Stable guard positions inside a tactical perimeter. Searching
            // an entire map in nested rings multiplied the problem by squad size.
            for(int radius=1;radius<=12;radius++)
            for(int dx=-radius;dx<=radius;dx++)
            for(int dz=-radius;dz<=radius;dz++)
            {
                if(Math.Max(Math.Abs(dx),Math.Abs(dz))!=radius)continue;
                IntVec3 cell=anchor+new IntVec3(dx,0,dz);
                if(!ValidGuardCell(cell,op,map,occupied))continue;
                Room room=cell.GetRoom(map);
                bool atSite=siteRoom!=null && room==siteRoom;
                if(!atSite && room!=startRoom)continue;
                int dist=(pawn.Position-cell).LengthHorizontalSquared;
                int score=(atSite?0:12000)+dist*3+radius*5;
                if(score>=bestScore)continue;
                if(!pawn.CanReach(cell,PathEndMode.OnCell,Danger.Some))continue;
                best=cell;bestScore=score;
            }
            if(best.IsValid)occupied.Add(best);
            return best;
        }

        internal static Job WorkJob(Pawn pawn,Thing site)
        {
            if(pawn==null || site==null || !site.Spawned || pawn.Map!=site.Map ||
               pawn.Downed || pawn.InMentalState ||
               pawn.health?.capacities==null ||
               !pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation))return null;
            RHFOperation mission=RHFRescuePlannerS41.MissionFor(pawn.Map,pawn);
            if(mission!=null && mission.Phase==3 && mission.PodConstructionStage==4 &&
               mission.PodVehicleThing==site &&
               NJRPodRoofS41.NeedsWork(pawn.Map,site.Position))
            {
                JobDef roof=DefDatabase<JobDef>.GetNamedSilentFail("NJR_RemovePodRoofS41");
                return roof!=null ? JobMaker.MakeJob(roof,site.Position) : null;
            }
            if(!(site is Blueprint_Build) && !(site is Frame))
            {
                CompRefuelable fuel=(site as ThingWithComps)?.GetComp<CompRefuelable>();
                if(fuel==null || fuel.IsFull)return null;
            }
            string[] names=site is Blueprint_Build ?
                new[]{"ConstructDeliverResourcesToBlueprints"} :
                site is Frame ?
                new[]{"ConstructDeliverResourcesToFrames","ConstructFinishFrames"} :
                new[]{"Refuel"};
            for(int i=0;i<names.Length;i++)
            {
                WorkGiverDef def=DefDatabase<WorkGiverDef>.GetNamedSilentFail(names[i]);
                if(!(def?.Worker is WorkGiver_Scanner giver))continue;
                try
                {
                    if(names[i]=="Refuel" && !giver.HasJobOnThing(pawn,site,true))continue;
                    Job result=giver.JobOnThing(pawn,site,true);
                    if(result!=null)return result;
                }
                catch(Exception e)
                { Log.Warning("[RHF S41][Native work] "+names[i]+": "+e.Message); }
            }
            return null;
        }

        internal static void ResetAfterFallback(RHFOperation op)
        {
            RHFTacticalBoardS41 board=op?.BoardS41;
            if(board==null)return;
            board.Orders.Clear();
            board.PodWorkTargetS41=null;
            board.PodWorkStageS41=-1;
            board.PodEngineerS41=null;
            board.LastPlanTick=0;
            board.NextReplanTick=0;
            board.ObservedPhase=-1;
            board.Decision="Native pod aborted: ground escort replan required";
        }
    }
}
