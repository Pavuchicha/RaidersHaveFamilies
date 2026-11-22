// S4.1-A/O: no instant spawned supplies. A mission carrier walks to the
// assembly point and physically drops only the pre-issued stacks from its
// inventory. Native vanilla WorkGivers then haul them into the enemy's frame.
using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace RaidersHaveFamilies
{
    internal static class RHFPodSupplyS41
    {
        internal static bool HasCargo(RHFOperation op,Pawn pawn)
        {
            if(op?.PodCargoStacksS41==null || pawn?.inventory?.innerContainer==null)
                return false;
            for(int i=0;i<op.PodCargoStacksS41.Count && i<48;i++)
            {
                Thing item=op.PodCargoStacksS41[i];
                if(item!=null && !item.Destroyed &&
                   pawn.inventory.innerContainer.Contains(item))return true;
            }
            return false;
        }

        internal static IntVec3 UnloadCell(Pawn pawn,RHFOperation op,Map map)
        {
            if(pawn==null || op==null || map==null ||
               !op.PodLauncherCell.IsValid)return IntVec3.Invalid;
            IntVec3 center=op.PodLauncherCell;
            IntVec3 best=IntVec3.Invalid;int score=int.MaxValue;
            for(int radius=2;radius<=7;radius++)
            for(int dx=-radius;dx<=radius;dx++)
            for(int dz=-radius;dz<=radius;dz++)
            {
                if(Math.Max(Math.Abs(dx),Math.Abs(dz))!=radius)continue;
                IntVec3 c=center+new IntVec3(dx,0,dz);
                // Delivery stacks must never block the actual physical
                // launcher/pod footprints while the engineer is building.
                ThingDef launcher=DefDatabase<ThingDef>.GetNamedSilentFail("PodLauncher");
                ThingDef pod=DefDatabase<ThingDef>.GetNamedSilentFail("TransportPod");
                bool launcherFloor=op.PodLauncherCell.IsValid && launcher!=null &&
                    c.x>=op.PodLauncherCell.x && c.x<op.PodLauncherCell.x+launcher.size.x &&
                    c.z>=op.PodLauncherCell.z && c.z<op.PodLauncherCell.z+launcher.size.z;
                bool podFloor=op.PodCell.IsValid && pod!=null &&
                    c.x>=op.PodCell.x && c.x<op.PodCell.x+pod.size.x &&
                    c.z>=op.PodCell.z && c.z<op.PodCell.z+pod.size.z;
                if(launcherFloor || podFloor ||
                   !c.InBounds(map) || !c.Standable(map) ||
                   c.GetEdifice(map)!=null ||
                   !pawn.CanReach(c,PathEndMode.OnCell,Danger.Some))continue;
                int candidate=(pawn.Position-c).LengthHorizontalSquared;
                if(candidate<score){best=c;score=candidate;}
            }
            return best;
        }

        internal static void Unload(RHFOperation op,Pawn carrier)
        {
            if(op?.PodCargoStacksS41==null || carrier==null ||
               !carrier.Spawned || carrier.inventory?.innerContainer==null ||
               carrier.Faction!=op.PodCargoOwnerFactionS311 ||
               carrier.Faction==Faction.OfPlayer)return;
            ThingOwner inventory=carrier.inventory.innerContainer;
            for(int i=0;i<op.PodCargoStacksS41.Count && i<48;i++)
            {
                Thing item=op.PodCargoStacksS41[i];
                if(item==null || item.Destroyed || !inventory.Contains(item))continue;
                if(inventory.TryDrop(item,carrier.Position,carrier.Map,
                    ThingPlaceMode.Near,out Thing dropped))
                {
                    // Item refs survive a normal drop, but vanilla can merge
                    // into an existing ground stack. Keep the actual reference.
                    op.PodCargoStacksS41[i]=dropped;
                    if(op.PodSteel==item)op.PodSteel=dropped;
                    if(op.PodSteelExtra==item)op.PodSteelExtra=dropped;
                    if(op.PodComponents==item)op.PodComponents=dropped;
                    if(op.PodFuelStacksS311!=null)
                    for(int j=0;j<op.PodFuelStacksS311.Count;j++)
                        if(op.PodFuelStacksS311[j]==item)
                            op.PodFuelStacksS311[j]=dropped;
                }
            }
        }
    }

    public sealed class JobDriver_RHFPodCargoS41 : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {return true;}

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => pawn.Dead || pawn.Downed || pawn.Map==null ||
                RHFRescuePlannerS41.MissionFor(pawn.Map,pawn)==null);
            yield return Toils_Goto.GotoCell(TargetIndex.A,PathEndMode.OnCell);
            yield return Toils_General.DoAtomic(() =>
            {
                RHFOperation op=RHFRescuePlannerS41.MissionFor(pawn.Map,pawn);
                if(op!=null && op.Phase==3 &&
                   op.PodConstructionStage>=1 && op.PodConstructionStage<=3)
                    RHFPodSupplyS41.Unload(op,pawn);
            });
        }
    }
}
