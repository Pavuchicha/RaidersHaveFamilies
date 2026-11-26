using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace RaidersHaveFamilies
{
    [HarmonyPatch]
    internal static class NJRPrisonerCastGuardR050
    {
        private static readonly PropertyInfo CasterProperty=typeof(Verb).GetProperty("Caster",
            BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach(MethodInfo method in AccessTools.GetDeclaredMethods(typeof(Verb)))
            {
                var args=method.GetParameters();
                if(method.Name=="TryStartCastOn" && method.ReturnType==typeof(bool) &&
                   args.Length>0 && args[0].ParameterType==typeof(LocalTargetInfo))yield return method;
            }
        }
        private static bool Prefix(Verb __instance,LocalTargetInfo __0,ref bool __result)
        {
            if(!(__0.Thing is Pawn prisoner) || !prisoner.IsPrisonerOfColony)return true;
            Pawn pawn=CasterProperty?.GetValue(__instance) as Pawn;
            if(pawn?.Spawned!=true || !(pawn.GetLord()?.LordJob is RHFRescueLordJobV124))return true;
            RHFOperation op=RHFRescuePlannerS41.MissionFor(pawn.Map,pawn);
            if(op?.FortifyPrison!=true || op.Phase>3)return true;
            // Restrict response to melee only. No shot through a captive or
            // unprovoked automatic melee attack from a movement/hold job.
            if(__instance.verbProps?.IsMeleeAttack==true &&
               NJRPrisonerDefenseR047.MayDefend(op,pawn,prisoner))return true;
            __result=false;return false;
        }
    }
}
