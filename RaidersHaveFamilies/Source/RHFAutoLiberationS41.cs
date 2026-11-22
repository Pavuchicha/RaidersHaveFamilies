// RHF S4.1-B: scoped zero-delay event capture; no global prisoner/faction changes.
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RaidersHaveFamilies
{
    [HarmonyPatch(typeof(Pawn_GuestTracker),nameof(Pawn_GuestTracker.SetGuestStatus))]
    internal static class RHFS41GuestStatusChanged
    {
        private static readonly FieldInfo PawnField=AccessTools.Field(typeof(Pawn_GuestTracker),"pawn");
        [HarmonyPostfix]
        private static void Postfix(Pawn_GuestTracker __instance)
        {
            Pawn pawn=PawnField?.GetValue(__instance) as Pawn;
            if(pawn!=null)RHFRescuePlannerS41.TryAdoptReleased(pawn);
        }
    }

    [HarmonyPatch(typeof(GenGuest),nameof(GenGuest.PrisonerRelease))]
    internal static class RHFS41VanillaReleased
    {
        [HarmonyPostfix]
        private static void Postfix(Pawn p)
        {
            if(p!=null)RHFRescuePlannerS41.TryAdoptReleased(p);
        }
    }
    // Scoped structural invalidation. This does NOT grant liberty to any
    // prisoner merely because a prison wall or door was destroyed.
    [HarmonyPatch(typeof(Building), nameof(Building.Destroy))]
    internal static class RHFS41BarrierGone
    {
        [HarmonyPrefix]
        private static void Prefix(Building __instance,out Map __state)
        {
            __state=null;
            if(__instance!=null && __instance.Faction==Faction.OfPlayer &&
               (__instance is Building_Door ||
                (__instance.def?.destroyable==true &&
                 __instance.def.passability==Traversability.Impassable)))
                __state=__instance.Map;
        }
        [HarmonyPostfix]
        private static void Postfix(Building __instance,Map __state)
        {
            if(__state!=null && __instance!=null && __instance.Destroyed)
                RHFRescuePlannerS41.OnBarrierDestroyed(__state,__instance);
        }
    }

}
