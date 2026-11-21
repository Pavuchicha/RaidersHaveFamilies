// RHF: one-off moral memories. Called ONLY when a tracked prisoner is released
// or genuinely sold through vanilla Pawn_RelationsTracker.Notify_PawnSold.
// Merciful = vanilla Charity_* precepts, plus compatible modded Mercy_* precepts.
using System;
using RimWorld;
using Verse;

namespace RaidersHaveFamilies
{
    public static class RHFMercy
    {
        private const int MaxMemoryLevel = 3;
        public static int PreceptLevel(Pawn colonist)
        {
            if (!ModsConfig.IdeologyActive || colonist?.Ideo == null) return 0;
            int level = 0;
            foreach (Precept precept in colonist.Ideo.PreceptsListForReading)
            {
                string n = precept?.def?.defName?.ToLowerInvariant() ?? "";
                if (!n.StartsWith("charity_") && !n.StartsWith("mercy_")) continue;
                if (n.Contains("essential") || n.Contains("required")) level = Math.Max(level, 3);
                else if (n.Contains("important") || n.Contains("respected")) level = Math.Max(level, 2);
                else if (n.Contains("worthwhile") || n.Contains("accepted")) level = Math.Max(level, 1);
            }
            return Math.Min(level, MaxMemoryLevel);
        }

        public static bool Eligible(RHFHostageRecord record)
        {
            if (record == null || record.Pawn == null || record.MercyAwarded ||
                record.Pawn.Dead || record.Enslaved || record.ForcedConversion ||
                record.CrueltyScore > 0 || record.WceOperations > 0 ||
                record.SurgicalCrueltyCount > 0 || record.SelfAdmittedMutilations > 0)
                return false;
            // We do not attribute unexplained post-capture amputations to violence,
            // but equally cannot certify that the captivity was wholly humane.
            return RHFHostageDiplomacy.MissingParts(record.Pawn) <= record.InitialMissing;
        }

        public static int TryAward(RHFHostageRecord record, bool ordinarySale)
        {
            if (!Eligible(record) || RHFMod.Settings?.Enabled != true) return 0;
            // Negotiated silver exchange is ransom, not ordinary trade.
            if (ordinarySale && (record.Negotiated || record.PendingSilver > 0)) return 0;
            int awarded = 0;
            foreach (Map map in Find.Maps)
            {
                if (!map.IsPlayerHome) continue;
                foreach (Pawn colonist in map.mapPawns.FreeColonistsSpawned)
                {
                    if (colonist?.needs?.mood == null) continue;
                    int level = PreceptLevel(colonist);
                    if (level == 0) continue;
                    string name = ordinarySale ? "RHF_MercyTrade" : "RHF_MercyFreedom";
                    ThoughtDef memory = DefDatabase<ThoughtDef>.GetNamedSilentFail(name + level);
                    if (memory == null) continue;
                    for (int j = 1; j <= 3; j++)
                    {
                        ThoughtDef a = DefDatabase<ThoughtDef>.GetNamedSilentFail("RHF_MercyTrade" + j);
                        ThoughtDef b = DefDatabase<ThoughtDef>.GetNamedSilentFail("RHF_MercyFreedom" + j);
                        if (a != null) colonist.needs.mood.thoughts.memories.RemoveMemoriesOfDef(a);
                        if (b != null) colonist.needs.mood.thoughts.memories.RemoveMemoriesOfDef(b);
                    }
                    colonist.needs.mood.thoughts.memories.TryGainMemory(memory);
                    awarded++;
                }
            }
            // Even when no believers currently exist, the event itself is over;
            // don't award retroactive memories to future colonists or on map reload.
            record.MercyAwarded = true;
            if (awarded > 0)
                Messages.Message("RHF: " + awarded + " поселенцев получили воспоминание за " +
                    (ordinarySale ? "гуманную обычную продажу" : "милосердное освобождение") +
                    " пленника " + record.Pawn.LabelShortCap + ".",
                    MessageTypeDefOf.PositiveEvent, false);
            return awarded;
        }

        public static void OnOrdinarySale(Pawn pawn)
        {
            if (pawn == null || RHFMod.Settings?.Enabled != true) return;
            RHFHostageRecord record = Current.Game?.GetComponent<RHFGameLedger>()?.FindHostage(pawn);
            TryAward(record, ordinarySale: true);
        }
    }
}
