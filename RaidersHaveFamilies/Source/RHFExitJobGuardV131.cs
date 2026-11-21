// RHF v0.13.1: prevent vanilla flee job-givers from repeatedly attempting
// blocked map exits for a prisoner under actual RHF rescue command.
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace RaidersHaveFamilies
{
    [HarmonyPatch]
    internal static class RHFControlledFreedExitJobV131
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            // Resolve by name so CE/Waylith versions can change accessibility
            // without breaking C# compilation against RimWorld 1.7 references.
            foreach (string name in new [] {
                "Verse.AI.JobGiver_ExitMapBest",
                "Verse.AI.JobGiver_ExitMapPanic" })
            {
                var type = AccessTools.TypeByName(name);
                MethodInfo method = type == null ? null :
                    AccessTools.DeclaredMethod(type,"TryGiveJob");
                if (method != null) yield return method;
            }
        }

        [HarmonyPrefix]
        private static bool Prefix(Pawn pawn, ref Job __result)
        {
            if (!RHFRescueCommandV124.IsControlledFreed(pawn)) return true;
            // The existing escorted ground exit and real transport pod launch
            // retain control; never create an ExitMapBest job that is then
            // denied by RHF's Pawn.ExitMap prefix in the very same tick.
            __result = null;
            return false;
        }
    }
}
