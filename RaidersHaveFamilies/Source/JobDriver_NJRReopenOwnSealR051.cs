using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
namespace RaidersHaveFamilies
{
    // Native Deconstruct requires a player designation and failed immediately
    // for NPC walls in R050. This bounded physical work does not use a designation.
    public sealed class JobDriver_NJRReopenOwnSealR051 : JobDriver
    {
        private bool Authorized()
        {
            RHFOperation op=RHFRescuePlannerS41.MissionFor(pawn.Map,pawn);
            Thing wall=job.targetA.Thing;
            RHFOrderS41 order=op?.BoardS41?.For(pawn,false);
            return (NJRRescueAccessR040.IsOwnSealR043(op,wall) || NJRRescueAccessR040.IsOwnExitStructureR054(op,wall)) && wall.Map==pawn.Map &&
                wall.Faction==pawn.Faction && order?.Role==(int)RHFRoleS41.Engineer &&
                order.Objective==wall && !pawn.Downed && !pawn.Dead &&
                pawn.health?.capacities?.CapableOf(PawnCapacityDefOf.Manipulation)==true;
        }
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {return Authorized() && pawn.Reserve(job.targetA,job,errorOnFailed:errorOnFailed);}
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(()=>!Authorized());
            yield return Toils_Goto.GotoThing(TargetIndex.A,PathEndMode.Touch);
            yield return Toils_General.Wait(300,TargetIndex.A);
            yield return Toils_General.DoAtomic(()=>
            {
                if(!Authorized())return;
                Thing wall=job.targetA.Thing;
                Log.Message("[NJR][R051][Exit] physically dismantled own exit obstruction "+wall.Position);
                wall.Destroy(DestroyMode.Deconstruct);
            });
        }
    }
}
