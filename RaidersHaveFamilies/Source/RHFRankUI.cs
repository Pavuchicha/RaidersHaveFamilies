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
            RHFHostageRecord record = Current.Game?.GetComponent<RHFGameLedger>()?.FindHostage(p);
            string office = record != null && !string.IsNullOrEmpty(record.OfficeName) ?
                record.OfficeName : RHFRankResolver.OfficeFor(p, rank);
            cacheShort = "Значимость RHF: " + rank + "/8 — " + office +
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
            RHFHostageRecord captured = Current.Game?.GetComponent<RHFGameLedger>()?.FindHostage(p);
            int rank = EffectiveRank(p);
            b.AppendLine();
            b.AppendLine(RHFRankResolver.OfficeStory(p, rank, captured?.OfficeName,
                captured != null && captured.DebugOffice));
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

    public static class RHFPrisonerInline
    {
        // Plain text, not a secondary gray menu panel in the prisoner tab.
        public static void Draw(Rect area, Pawn pawn)
        {
            if (pawn == null) return;
            var record = Current.Game?.GetComponent<RHFGameLedger>()?.FindHostage(pawn);
            int rank = RHFRankUI.EffectiveRank(pawn);
            string office = record?.OfficeName ?? RHFRankResolver.OfficeFor(pawn, rank);
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(area.x, area.y, area.width, 28f),
                "RHF: " + rank + "/8 — " + office);
            if (record != null)
                Widgets.Label(new Rect(area.x, area.y+23f, area.width, 40f),
                    "Выкуп: " + RHFHostageDiplomacy.SilverPrice(record) + " серебра; " +
                    "бесплатный возврат: до +" + RHFHostageDiplomacy.GoodwillReward(record) + " репутации");
            if (Widgets.ButtonText(new Rect(area.x, area.y+61f, area.width, 22f), "RHF: подробности и связи"))
                RHFRankUI.OpenDetails(pawn);
            Text.Font = GameFont.Small;
        }
    }

    // Vanilla kind labels ("жительница", "солдат") describe PawnKind, not
    // political importance. Add a separate truthful line rather than changing
    // PawnKindDef shared by every pawn of the same race/faction.
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetInspectString))]
    public static class RHFInspectStatus
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn __instance, ref string __result)
        {
            if (__instance?.RaceProps == null || !__instance.RaceProps.Humanlike ||
                __instance.Faction == null || __instance.Faction.IsPlayer ||
                RHFMod.Settings?.Enabled != true) return;
            int rank = RHFRankUI.EffectiveRank(__instance);
            RHFHostageRecord record = Current.Game?.GetComponent<RHFGameLedger>()?.FindHostage(__instance);
            string office = record != null && !string.IsNullOrEmpty(record.OfficeName) ?
                record.OfficeName : RHFRankResolver.OfficeFor(__instance, rank);
            __result = (__result ?? "").TrimEnd() + "\nRHF: " + rank + "/8 — " + office;
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
            RHFPrisonerInline.Draw(new Rect(11f, old.y + 3f, old.x - 22f, 88f), p);
        }
    }

    // RimWorld 1.7 StatWorker.GetStatDrawEntryLabel is not virtual in our reference DLL.
    // Alter only the displayed value of our own custom rank, never other mods' stats.
    [HarmonyPatch(typeof(StatDrawEntry), "get_ValueString")]
    public static class RHFRankStatText
    {
        [HarmonyPostfix]
        public static void Postfix(StatDrawEntry __instance, ref string __result)
        {
            if (__instance?.stat?.defName != "RHF_SocialImportance" ||
                !__instance.hasOptionalReq || !(__instance.optionalReq.Thing is Pawn pawn)) return;
            int rank = RHFRankUI.EffectiveRank(pawn);
            RHFHostageRecord r = Current.Game?.GetComponent<RHFGameLedger>()?.FindHostage(pawn);
            string office = r != null && !string.IsNullOrEmpty(r.OfficeName) ?
                r.OfficeName : RHFRankResolver.OfficeFor(pawn, rank);
            __result = rank + "/8 — " + office;
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
        // RimWorld 1.7 reference assemblies no longer expose the previous
        // virtual GetStatDrawEntryLabel signature. Let the engine render the
        // numeric rank (0..8); the human-readable category remains available
        // via GetExplanationUnfinalized and the RHF card on pawn tabs.
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
        // RimWorld 1.7 renders the numeric count. Provenance details are
        // available via GetExplanationUnfinalized and the RHF pawn-tab button.
        public override string GetExplanationUnfinalized(StatRequest req, ToStringNumberSense numberSense)
        {
            return req.Thing is Pawn p ? RHFRankUI.Details(p) : "";
        }
    }
}
