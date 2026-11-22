// S4.1-A/D: native physical breaching job. Consumables are spent only on
// completion after the pawn reaches the real obstacle and works for 750 ticks.
using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace RaidersHaveFamilies
{
    public sealed class JobDriver_RHFPlantBreachChargeS41 : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.GetTarget(TargetIndex.A),job,1,-1,null,errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A,PathEndMode.Touch);
            // A real planting delay; re-evaluation still respects job interrupts.
            Toil plant=new Toil();
            plant.defaultCompleteMode=ToilCompleteMode.Delay;
            plant.defaultDuration=750;
            yield return plant;
            Toil activate=new Toil();
            activate.defaultCompleteMode=ToilCompleteMode.Instant;
            activate.initAction=()=>
            {
                Thing obstacle=job.targetA.Thing;
                Map map=pawn.Map;
                if(map==null || obstacle==null || obstacle.Destroyed ||
                   obstacle.Map!=map || !RHFCombatPlannerV125.IsBreachableStructure(obstacle))
                    return;
                RHFOperation operation=RHFRescuePlannerS41.MissionFor(map,pawn);
                bool rescue=operation!=null && operation.Phase==1 &&
                    operation.BoardS41?.Breacher==pawn &&
                    operation.BoardS41.Barrier==obstacle;
                if(!rescue)operation=RHFOffenseDirectorS41.MissionFor(pawn);
                RHFTacticalBoardS41 board=rescue ? operation.BoardS41 : operation?.OffenseBoardS41;
                RHFOrderS41 order=board?.For(pawn,false);
                bool assigned=rescue ? board?.Breacher==pawn && board.Barrier==obstacle :
                    order!=null && order.Role==(int)RHFRoleS41.Breach &&
                    order.Objective==obstacle;
                if(operation==null || !assigned || operation.BreachChargesUsed>=4 ||
                   !RHFTactical.HasBreachKit(pawn))return;
                // No area damage near a prisoner or coalition member. This is
                // a controlled STRUCTURAL charge, not a free explosion/AOE.
                if(!RHFTactical.ConsumeBreachKit(pawn))return;
                obstacle.TakeDamage(new DamageInfo(DamageDefOf.Bomb,150f,0f,-1f,pawn));
                operation.BreachChargesUsed++;
                operation.LastProgressTick=Find.TickManager.TicksGame;
                board.NextReplanTick=0;
                board.LastPlanTick=0;
            };
            yield return activate;
        }
    }
}
