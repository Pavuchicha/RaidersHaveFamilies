// Physical evacuation of an unconscious captive by an RHF escort.
// Uses RimWorld haul toils and releases the prisoner only at the actual map edge.
using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace RaidersHaveFamilies
{
    public sealed class JobDriver_RHFCarryDowned : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.GetTarget(TargetIndex.A), job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TargetIndex.A);
            this.FailOn(() => !((Pawn)job.targetA.Thing).Downed);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch);
            yield return Toils_Haul.StartCarryThing(TargetIndex.A);
            yield return Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.OnCell);
            Toil depart = new Toil();
            depart.initAction = () =>
            {
                Pawn carried = pawn.carryTracker?.CarriedThing as Pawn;
                if (carried == null) return;
                Thing dropped;
                if (!pawn.carryTracker.TryDropCarriedThing(pawn.Position,
                    ThingPlaceMode.Near, out dropped))
                {
                    Log.Warning("[RHF] Rescue escort cannot release carried captive at map edge.");
                    return;
                }
                Pawn rescued = dropped as Pawn;
                if (rescued != null && rescued.Spawned)
                    rescued.ExitMap(false, Rot4.Invalid);
            };
            depart.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return depart;
        }
    }
}
