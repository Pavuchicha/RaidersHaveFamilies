// RimWorld 1.7: contextual status for the character tab, prisoner tab, and info-card stats.
// GUI-only Harmony postfixes; no work during ordinary game ticks.
using System;
using System.Reflection;
using System.Text;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RaidersHaveFamilies
{
    // Records ONLY explicit modifications made since this schema was installed.
    // Derived in-game relationships may be inferred from RHF-authored parentage.
    public sealed class RHFProvenanceLink : IExposable
    {
        public Pawn A;
        public Pawn B;
        public string Kind;
        public RHFProvenanceLink() { }
        public RHFProvenanceLink(Pawn a, Pawn b, string kind)
        { A = a; B = b; Kind = kind; }
        public void ExposeData()
        {
            Scribe_References.Look(ref A, "a");
            Scribe_References.Look(ref B, "b");
            Scribe_Values.Look(ref Kind, "kind", "");
        }
    }

    public static class RHFRankUI
    {
        private static readonly FieldInfo SizeField = AccessTools.Field(typeof(InspectTabBase), "size");
        private static Pawn cachePawn;
        private static int cacheTick = -999999;
        private static string cacheShort;

        public static Vector2 GetSize(InspectTabBase tab)
        {
            return SizeField == null ? Vector2.zero : (Vector2)SizeField.GetValue(tab);
        }
        public static void SetSize(InspectTabBase tab, Vector2 size)
        {
            if (SizeField != null) SizeField.SetValue(tab, size);
        }

        public static int EffectiveRank(Pawn p)
        {
            if (p == null) return 0;
            RHFHostageRecord hostage = Current.Game?.GetComponent<RHFGameLedger>()?.FindHostage(p);
            return hostage == null ? RHFRankResolver.RankOf(p) : hostage.Importance;
        }

        private static int TotalRelated(Pawn p)
        {
            int result = 0;
            if (p?.relations == null) return 0;
            foreach (Pawn other in p.relations.RelatedPawns)
                if (other != null && other != p && result < 1024) result++;
            return result;
        }

        public static string Short(Pawn p)
        {
            if (p == null || p.RaceProps == null || !p.RaceProps.Humanlike) return "";
            int tick = Find.TickManager?.TicksGame ?? 0;
            if (p == cachePawn && cacheShort != null && tick - cacheTick < 120) return cacheShort;
            int rank = EffectiveRank(p);
            int marked = Current.Game?.GetComponent<RHFGameLedger>()?.ConfirmedLinks(p) ?? 0;
            cachePawn = p;
            cacheTick = tick;
            cacheShort = "Значимость RHF: " + rank + "/8 — " + RHFRankResolver.RankLabel(rank) +
                (Current.Game?.GetComponent<RHFGameLedger>()?.FindHostage(p) != null ? " (при пленении)" : " (по роли)") +
                "\nПодтверждено связей RHF: " + marked + "; всего родственных и социальных: " + TotalRelated(p);
            return cacheShort;
        }

        public static string Details(Pawn p)
        {
            if (p == null) return "Выбери персонажа на карте.";
            var b = new StringBuilder(700);
            b.AppendLine(p.LabelShortCap + " — Raiders Have Families");
            b.AppendLine(Short(p));
            b.AppendLine();
            b.AppendLine("Подтверждённые изменения отношений нашего мода:");
            RHFGameLedger ledger = Current.Game?.GetComponent<RHFGameLedger>();
            int shown = 0;
            if (ledger?.ProvenanceLinks != null)
            {
                for (int i = 0; i < ledger.ProvenanceLinks.Count; i++)
                {
                    RHFProvenanceLink edge = ledger.ProvenanceLinks[i];
                    if (edge == null || edge.A == null || edge.B == null) continue;
                    if (edge.A != p && edge.B != p) continue;
                    Pawn other = edge.A == p ? edge.B : edge.A;
                    if (shown < 24)
                        b.AppendLine("  • " + (edge.Kind ?? "связь") + ": " + other.LabelShortCap);
                    shown++;
                }
            }
            if (shown == 0)
                b.AppendLine("  Нет записей происхождения для этой пешки.");
            else if (shown > 24)
                b.AppendLine("  ... и ещё " + (shown - 24) + " записей.");
            b.AppendLine();
            b.AppendLine("Важно: записи создаются с v0.4.0. Старые отношения до этого обновления, а также ванильные/чужие связи, не приписываются RHF.");
            b.AppendLine("Братья, сёстры и другие родственные отношения могут вычисляться игрой из установленных RHF родителей.");
            return b.ToString();
        }

        public static void OpenDetails(Pawn p)
        {
            if (p == null) return;
            Find.WindowStack.Add(new Dialog_MessageBox(Details(p)));
        }

        public static void DrawStatus(Rect area, Pawn pawn)
        {
            if (pawn == null || pawn.RaceProps == null || !pawn.RaceProps.Humanlike) return;
            Widgets.DrawMenuSection(area);
            Text.Font = GameFont.Tiny;
            Rect lines = area.ContractedBy(7f);
            lines.height = area.height - 28f;
            Widgets.Label(lines, Short(pawn));
            Rect btn = new Rect(area.x + 7f, area.yMax - 25f, area.width - 14f, 20f);
            if (Widgets.ButtonText(btn, "RHF: показать источники связей")) OpenDetails(pawn);
            Text.Font = GameFont.Small;
        }
    }

    [HarmonyPatch(typeof(ITab_Pawn_Character), MethodType.Constructor)]
    public static class RHFCharacterTabSize
    {
        [HarmonyPostfix]
        public static void Postfix(ITab_Pawn_Character __instance)
        {
            if (RHFRankUI.GetSize(__instance) is Vector2 current && current.y > 0f)
                RHFRankUI.SetSize(__instance, new Vector2(current.x, current.y + 97f));
        }
    }

    [HarmonyPatch(typeof(ITab_Pawn_Character), "FillTab")]
    public static class RHFCharacterTabDisplay
    {
        [HarmonyPostfix]
        public static void Postfix(ITab_Pawn_Character __instance)
        {
            Pawn p = Find.Selector?.SingleSelectedThing as Pawn;
            if (p == null) return;
            Vector2 size = RHFRankUI.GetSize(__instance);
            if (size.x < 100f || size.y < 120f) return;
            RHFRankUI.DrawStatus(new Rect(17f, size.y - 96f, size.x - 34f, 87f), p);
        }
    }

    [HarmonyPatch(typeof(ITab_Pawn_Visitor), "FillTab")]
    public static class RHFPrisonerTabDisplay
    {
        [HarmonyPostfix]
        public static void Postfix(ITab_Pawn_Visitor __instance)
        {
            if (!(__instance is ITab_Pawn_Prisoner)) return;
            Pawn p = Find.Selector?.SingleSelectedThing as Pawn;
            if (p == null || !p.IsPrisonerOfColony) return;
            Vector2 old = RHFRankUI.GetSize(__instance);
            if (old.x < 100f) return;
            RHFRankUI.SetSize(__instance, new Vector2(old.x, old.y + 100f));
            RHFRankUI.DrawStatus(new Rect(8f, old.y + 3f, old.x - 16f, 88f), p);
        }
    }

    public sealed class RHFRankStatWorker : StatWorker
    {
        public override bool ShouldShowFor(StatRequest req)
        {
            return req.Thing is Pawn p && p.RaceProps != null && p.RaceProps.Humanlike;
        }
        public override float GetValueUnfinalized(StatRequest req, bool applyPostProcess = true)
        {
            return req.Thing is Pawn p ? RHFRankUI.EffectiveRank(p) : 0f;
        }
        public override string GetStatDrawEntryLabel(StatDef stat, float value,
            ToStringNumberSense numberSense, StatRequest optionalReq)
        {
            Pawn p = optionalReq.Thing as Pawn;
            return p == null ? "—" : RHFRankUI.EffectiveRank(p) + "/8 — " +
                RHFRankResolver.RankLabel(RHFRankUI.EffectiveRank(p));
        }
        public override string GetExplanationUnfinalized(StatRequest req, ToStringNumberSense numberSense)
        {
            return req.Thing is Pawn p ? RHFRankUI.Details(p) : "";
        }
    }

    public sealed class RHFProvenanceStatWorker : StatWorker
    {
        public override bool ShouldShowFor(StatRequest req)
        {
            return req.Thing is Pawn p && p.RaceProps != null && p.RaceProps.Humanlike;
        }
        public override float GetValueUnfinalized(StatRequest req, bool applyPostProcess = true)
        {
            return req.Thing is Pawn p ? Current.Game?.GetComponent<RHFGameLedger>()?.ConfirmedLinks(p) ?? 0 : 0;
        }
        public override string GetStatDrawEntryLabel(StatDef stat, float value,
            ToStringNumberSense numberSense, StatRequest optionalReq)
        {
            return ((int)value) + " подтверждено (с v0.4.0)";
        }
        public override string GetExplanationUnfinalized(StatRequest req, ToStringNumberSense numberSense)
        {
            return req.Thing is Pawn p ? RHFRankUI.Details(p) : "";
        }
    }
}
