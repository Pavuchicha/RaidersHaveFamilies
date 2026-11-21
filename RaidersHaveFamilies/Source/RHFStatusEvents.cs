// RHF v0.12 — actual prisoner recruitment and ideological conversion events.
// Harmony hooks only execute on real faction/ideology changes, not every tick.
using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RaidersHaveFamilies
{
    // Vanilla RecruitUtility removes prisoner status BEFORE Pawn.SetFaction.
    // Therefore SetFaction alone cannot reliably detect ordinary recruitment.
    [HarmonyPatch(typeof(RecruitUtility), nameof(RecruitUtility.Recruit))]
    public static class RHFVanillaRecruitmentHook
    {
        [HarmonyPrefix]
        public static void Prefix(Pawn pawn, Faction faction, out bool __state)
        {
            __state=RHFMod.Settings?.Enabled == true && pawn!=null &&
                faction==Faction.OfPlayer && pawn.IsPrisonerOfColony &&
                Current.Game?.GetComponent<RHFGameLedger>()?.FindHostage(pawn)!=null;
        }
        [HarmonyPostfix]
        public static void Postfix(Pawn pawn, Faction faction, Pawn recruiter, bool __state)
        {
            if (__state && pawn!=null && pawn.Faction==Faction.OfPlayer)
                RHFHostageDiplomacy.RecordRecruitment(pawn,recruiter);
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.SetFaction),
        new Type[] { typeof(Faction), typeof(Pawn) })]
    public static class RHFRecruitmentHook
    {
        [HarmonyPrefix]
        public static void Prefix(Pawn __instance, Faction newFaction, Pawn recruiter, out bool __state)
        {
            __state=RHFMod.Settings?.Enabled == true && __instance!=null &&
                recruiter!=null && newFaction==Faction.OfPlayer &&
                Current.Game?.GetComponent<RHFGameLedger>()?.FindHostage(__instance)!=null;
        }
        [HarmonyPostfix]
        public static void Postfix(Pawn __instance, Faction newFaction, Pawn recruiter, bool __state)
        {
            if (__state && __instance!=null && __instance.Faction==Faction.OfPlayer)
                RHFHostageDiplomacy.RecordRecruitment(__instance,recruiter);
        }
    }

    [HarmonyPatch(typeof(Pawn_IdeoTracker), nameof(Pawn_IdeoTracker.SetIdeo))]
    public static class RHFConversionHook
    {
        private static readonly FieldInfo PawnField=AccessTools.Field(typeof(Pawn_IdeoTracker),"pawn");
        [HarmonyPrefix]
        public static void Prefix(Pawn_IdeoTracker __instance, out Ideo __state)
        { __state=__instance?.Ideo; }
        [HarmonyPostfix]
        public static void Postfix(Pawn_IdeoTracker __instance, Ideo __state)
        {
            if (RHFMod.Settings?.Enabled!=true || __instance==null ||
                __state==__instance.Ideo) return;
            Pawn pawn=PawnField?.GetValue(__instance) as Pawn;
            if(pawn!=null) RHFHostageDiplomacy.RecordConversion(pawn,__state,__instance.Ideo);
        }
    }
}
