// NJR v0.14 beta2: a qualified hostile engineer physically removes a
// thin roof above its native transporter before the vanilla launch check.
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
namespace RaidersHaveFamilies
{
    public sealed class JobDriver_NJRPodRoofS41 : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        { return pawn.Reserve(job.targetA,this,errorOnFailed:errorOnFailed); }
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => pawn.Map==null || pawn.Dead || pawn.Downed ||
                !NJRPodRoofS41.Authorized(pawn,job.targetA.Cell));
            yield return Toils_Goto.GotoCell(TargetIndex.A,PathEndMode.Touch);
            yield return Toils_General.Wait(360,TargetIndex.A);
            yield return Toils_General.DoAtomic(() =>
                NJRPodRoofS41.Finish(pawn,job.targetA.Cell));
        }
    }
    internal static class NJRPodRoofS41
    {
        internal static bool NeedsWork(Map map,IntVec3 cell)
        {
            if(map==null || !cell.IsValid || !cell.InBounds(map))return false;
            RoofDef roof=map.roofGrid.RoofAt(cell);
            return roof!=null && !roof.isThickRoof;
        }
        internal static bool Authorized(Pawn pawn,IntVec3 cell)
        {
            RHFOperation op=pawn?.Map!=null ?
                RHFRescuePlannerS41.MissionFor(pawn.Map,pawn) : null;
            return op!=null && op.Phase==3 && op.PodConstructionStage==4 &&
                op.PodCell==cell && op.PodCargoOwnerFactionS311==pawn.Faction &&
                NeedsWork(pawn.Map,cell);
        }
        internal static void Finish(Pawn pawn,IntVec3 cell)
        {
            if(!Authorized(pawn,cell))return;
            // Thin roofs only. Thick mountain roof is NEVER a permitted site.
            pawn.Map.roofGrid.SetRoof(cell,null);
        }
    }
}
