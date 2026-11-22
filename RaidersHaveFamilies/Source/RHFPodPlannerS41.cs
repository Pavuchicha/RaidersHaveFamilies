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
        private const int MaxSquad=96;
        private const int MaxCaptives=24;
        private const int MaxGuards=8;

        // Stable owner for native work. The old nearest-pawn selector ran every
        // 90 ticks and could repeatedly interrupt builders as they moved.
        internal static bool PinnedEngineerValid(RHFOperation op,Map map,IntVec3 workCell)
        {
            Pawn p=op?.BoardS41?.PodEngineerS41;
            return p!=null && op.Squad!=null && op.Squad.Contains(p) &&
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

        internal static void Plan(RHFOperation op,Map map,Pawn engineer,Thing work,int now)
        {
            if(op==null || map==null || op.Phase!=3 || op.Finished)return;
            RHFTacticalBoardS41 board=op.BoardS41 ??= new RHFTacticalBoardS41();
            board.Prune(map);
            if(board.ObservedPhase!=3)
            {
                board.ObservedPhase=3;
                board.LastPlanTick=0;
                board.NextReplanTick=0;
                board.Orders.Clear();
            }
            if(now<board.NextReplanTick && board.LastPlanTick>0 &&
               board.PodWorkTargetS41==work && board.PodWorkStageS41==op.PodConstructionStage)
                return;
            board.PodWorkTargetS41=work;
            board.PodWorkStageS41=op.PodConstructionStage;
            board.LastPlanTick=now;
            board.NextReplanTick=now+120;
            board.Decision="Phase3 native build/load: stage "+op.PodConstructionStage;
            IntVec3 assembly=ChooseAssemblyCell(op,map);
            var occupied=new HashSet<Pawn>();
            if(engineer!=null && RHFRescuePlannerS41.ActiveForRoleS41(engineer,map) &&
               op.PodConstructionStage>=1 && op.PodConstructionStage<=3 && work!=null &&
               work.Spawned && work.Map==map)
            {
                board.Assign(engineer,RHFRoleS41.Engineer,work,assembly,now);
                occupied.Add(engineer);
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
            for(int i=0;i<op.Freed.Count && i<MaxCaptives;i++)
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
            int guards=0;
            for(int i=0;i<op.Squad.Count && i<MaxSquad;i++)
            {
                Pawn soldier=op.Squad[i];
                if(!RHFRescuePlannerS41.ActiveForRoleS41(soldier,map) ||
                   occupied.Contains(soldier))continue;
                IntVec3 cell=GuardCell(soldier,assembly,map,positions);
                if(cell.IsValid && guards++<MaxGuards)
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
            int[] dx={0,0,-3,3,-2,2,-2,2,0,0,-4,4};
            int[] dz={-3,3,0,0,-2,-2,2,2,-4,4,0,0};
            IntVec3 best=IntVec3.Invalid;
            int score=int.MaxValue;
            for(int i=0;i<dx.Length;i++)
            {
                IntVec3 cell=site+new IntVec3(dx[i],0,dz[i]);
                if(!cell.InBounds(map) || !cell.Standable(map) ||
                   cell.GetEdifice(map)!=null ||
                   !vip.CanReach(cell,PathEndMode.OnCell,Danger.Some))continue;
                int candidate=(vip.Position-cell).LengthHorizontalSquared;
                if(candidate<score){best=cell;score=candidate;}
            }
            return best.IsValid ? best : vip.Position;
        }

        private static IntVec3 GuardCell(Pawn pawn,IntVec3 anchor,Map map,
            HashSet<IntVec3> occupied)
        {
            if(!anchor.IsValid || !anchor.InBounds(map))return IntVec3.Invalid;
            // Stay in the existing accessible room rather than ordering a
            // guard to break into a neighboring locked store room.
            int[] dx={-5,5,0,0,-4,4,-4,4};
            int[] dz={0,0,-5,5,-4,-4,4,4};
            IntVec3 best=IntVec3.Invalid;
            int bestScore=int.MaxValue;
            for(int i=0;i<dx.Length;i++)
            {
                IntVec3 cell=anchor+new IntVec3(dx[i],0,dz[i]);
                if(!cell.InBounds(map) || !cell.Standable(map) ||
                   occupied.Contains(cell) ||
                   cell.GetRoom(map)!=pawn.Position.GetRoom(map) ||
                   !pawn.CanReach(cell,PathEndMode.OnCell,Danger.Some))continue;
                int score=(pawn.Position-cell).LengthHorizontalSquared;
                if(score<bestScore){best=cell;bestScore=score;}
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
