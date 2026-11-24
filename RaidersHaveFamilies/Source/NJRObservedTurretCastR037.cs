using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace RaidersHaveFamilies
{
    // Observe the native result of starting a turret shot, including CE.
    // Never modify a verb, projectile, hit result or suppression behavior.
    [HarmonyPatch]
    internal static class NJRObservedTurretCastR037
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
        private static void Postfix(Verb __instance,LocalTargetInfo __0,bool __result)
        {
            if(!__result || !(__0.Thing is Pawn pawn) || !pawn.Spawned ||
               !(pawn.GetLord()?.LordJob is RHFRescueLordJobV124))return;
            Building turret;
            try{turret=CasterProperty?.GetValue(__instance) as Building;}
            catch(Exception){return;}
            if(turret==null ||
               turret.Map!=pawn.Map || !RHFCombatPlannerV125.IsTurret(turret))return;
            RHFObjectiveRouteV128.ObserveTurretRangeR037(turret,pawn.Position);
            NJRRescueRallyR033.ObserveIncomingR037(pawn,Find.TickManager.TicksGame);
        }
    }
}
