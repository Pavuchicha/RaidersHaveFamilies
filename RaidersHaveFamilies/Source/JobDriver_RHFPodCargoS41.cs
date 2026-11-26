// S4.1-A/O: no instant spawned supplies. A mission carrier walks to the
// assembly point and physically drops only the pre-issued stacks from its
// inventory. Native vanilla WorkGivers then haul them into the enemy's frame.
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using RimWorld;
using Verse;
using Verse.AI;

namespace RaidersHaveFamilies
{
    internal static class RHFPodSupplyS41
    {
        private sealed class SealStateR044
        {internal IntVec3 Wall=IntVec3.Invalid;internal HashSet<IntVec3> Pocket;}
        private static readonly ConditionalWeakTable<RHFOperation,SealStateR044> SealStatesR044=new ConditionalWeakTable<RHFOperation,SealStateR044>();
        internal static void RememberSealPocketR044(RHFOperation op,IntVec3 wall,HashSet<IntVec3> pocket)
        {var state=SealStatesR044.GetValue(op,_=>new SealStateR044());state.Wall=wall;state.Pocket=pocket;}
        internal static HashSet<IntVec3> SealPocketR044(RHFOperation op)
        {
            return op!=null && op.FortifyPrison && op.BarricadeCell.IsValid &&
                SealStatesR044.TryGetValue(op,out SealStateR044 state) && state.Wall==op.BarricadeCell?state.Pocket:null;
        }
        internal static bool HasCargo(RHFOperation op,Pawn pawn)
        {
            if(op?.PodCargoStacksS41==null || pawn?.inventory?.innerContainer==null)
                return false;
            for(int i=0;i<op.PodCargoStacksS41.Count;i++)
            {
                Thing item=op.PodCargoStacksS41[i];
                if(item!=null && !item.Destroyed &&
                   pawn.inventory.innerContainer.Contains(item))return true;
            }
            return false;
        }

        internal static IntVec3 UnloadCell(Pawn pawn,RHFOperation op,Map map,HashSet<IntVec3> allocated=null)
        {
            if(pawn==null || op==null || map==null ||
               !op.PodLauncherCell.IsValid)return IntVec3.Invalid;
            IntVec3 center=op.PodLauncherCell;
            HashSet<IntVec3> pocket=SealPocketR044(op);
            Room siteRoom=op.PodCell.GetRoom(map)??center.GetRoom(map);
            IntVec3 best=IntVec3.Invalid;int score=int.MaxValue;
            for(int radius=op.FortifyPrison?1:2;radius<=7;radius++)
            for(int dx=-radius;dx<=radius;dx++)
            for(int dz=-radius;dz<=radius;dz++)
            {
                if(Math.Max(Math.Abs(dx),Math.Abs(dz))!=radius)continue;
                IntVec3 c=center+new IntVec3(dx,0,dz);
                // Delivery stacks must never block the actual physical
                // launcher/pod footprints while the engineer is building.
                if((allocated!=null && allocated.Contains(c)) || NJRPodSiteClearS41.ReservedFootprint(op,c) ||
                   c==op.BarricadeCell || (pocket!=null && !pocket.Contains(c)) ||
                   !c.InBounds(map) || !c.Standable(map) ||
                   c.GetEdifice(map)!=null ||
                   !pawn.CanReach(c,PathEndMode.OnCell,Danger.Some))continue;
                int throat=0;
                if(op.FortifyPrison)
                {
                    bool n=SolidSupplySideR044(map,c+IntVec3.North),s=SolidSupplySideR044(map,c+IntVec3.South);
                    bool e=SolidSupplySideR044(map,c+IntVec3.East),w=SolidSupplySideR044(map,c+IntVec3.West);
                    if((n&&s)||(e&&w))throat=20000;
                }
                int candidate=throat+(pawn.Position-c).LengthHorizontalSquared+
                    (siteRoom!=null && c.GetRoom(map)!=siteRoom?10000:0)+radius*radius*2+
                    (op.FortifyPrison && center.Roofed(map) && !c.Roofed(map)?10000:0);
                if(candidate<score){best=c;score=candidate;}
            }
            return best;
        }

        private static bool SolidSupplySideR044(Map map,IntVec3 c)
        {return c.InBounds(map) && c.GetEdifice(map)!=null && !(c.GetEdifice(map) is Building_Door) && !c.Standable(map);}
        internal static void Unload(RHFOperation op,Pawn carrier)
        {
            if(op?.PodCargoStacksS41==null || carrier==null ||
               !carrier.Spawned || carrier.inventory?.innerContainer==null ||
               carrier.Faction!=op.PodCargoOwnerFactionS311 ||
               carrier.Faction==Faction.OfPlayer)return;
            ThingOwner inventory=carrier.inventory.innerContainer;
            for(int i=0;i<op.PodCargoStacksS41.Count;i++)
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
                if(op!=null && (op.Phase==3 || (op.Phase==1 && op.TryPod && op.FortifyPrison)) &&
                   op.PodConstructionStage>=1 && op.PodConstructionStage<=3)
                    RHFPodSupplyS41.Unload(op,pawn);
            });
        }
    }
    public sealed class JobDriver_NJRMoveSealCargoR044 : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {return pawn.Reserve(job.GetTarget(TargetIndex.A),job,1,-1,null,errorOnFailed) &&
            pawn.Reserve(job.GetTarget(TargetIndex.B),job,1,-1,null,errorOnFailed);}
        private static bool MayCarryR047(RHFOperation op,bool debris)
        {return op!=null && (op.Phase==3 || (op.FortifyPrison && ((op.Phase==1 && (debris || op.TryPod)) || (debris && op.Phase==2 && op.PodLaunchCompleted))));}

        protected override IEnumerable<Toil> MakeNewToils()
        {
            Thing original=job.targetA.Thing;
            bool debris=NJRVipDebrisR045.IsDebris(original);
            this.FailOn(() => pawn.Dead || pawn.Downed || pawn.Map==null ||
                !MayCarryR047(RHFRescuePlannerS41.MissionFor(pawn.Map,pawn),debris));
            yield return Toils_Goto.GotoThing(TargetIndex.A,PathEndMode.ClosestTouch);
            this.FailOnDestroyedOrNull(TargetIndex.A);
            yield return Toils_Haul.StartCarryThing(TargetIndex.A);
            // Track split stacks immediately, even if CE interrupts the haul.
            yield return Toils_General.DoAtomic(() =>
            {
                RHFOperation op=RHFRescuePlannerS41.MissionFor(pawn.Map,pawn);
                Thing carried=pawn.carryTracker?.CarriedThing;
                if(op==null || carried==null || debris)return;
                if(!op.PodCargoStacksS41.Contains(carried))op.PodCargoStacksS41.Add(carried);
                if(carried.def==ThingDefOf.Chemfuel && !op.PodFuelStacksS311.Contains(carried))op.PodFuelStacksS311.Add(carried);
            });
            yield return Toils_Goto.GotoCell(TargetIndex.B,PathEndMode.OnCell);
            yield return Toils_General.DoAtomic(() =>
            {
                RHFOperation op=RHFRescuePlannerS41.MissionFor(pawn.Map,pawn);
                if(op==null || pawn.carryTracker?.CarriedThing==null)return;
                if(debris && !NJRVipDebrisR045.SafeDrop(op,pawn.Map,job.targetB.Cell,pawn))return;
                if(!pawn.carryTracker.TryDropCarriedThing(job.targetB.Cell,ThingPlaceMode.Direct,out Thing dropped))
                    return; // native job cleanup safely releases carry; next plan rechecks actual cell
                if(dropped==null)return;
                if(debris)
                {
                    NJRVipDebrisR045.Completed(op,original,dropped);
                    Log.Message("[NJR][R046] physically cleared drop debris "+dropped.Position);
                    return;
                }
                if(!op.PodCargoStacksS41.Contains(dropped))op.PodCargoStacksS41.Add(dropped);
                if(original!=null && (original.Destroyed || !original.Spawned))
                {
                    if(op.PodSteel==original)op.PodSteel=dropped;
                    if(op.PodSteelExtra==original)op.PodSteelExtra=dropped;
                    if(op.PodComponents==original)op.PodComponents=dropped;
                }
                if(dropped.def==ThingDefOf.Chemfuel && !op.PodFuelStacksS311.Contains(dropped))op.PodFuelStacksS311.Add(dropped);
                Log.Message("[NJR][R044][Fortify] moved mission cargo "+dropped+" to "+dropped.Position);
            });
        }
    }

}
