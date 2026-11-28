// Developer-only commands. Zero work in normal play: all searches and allocation
// happen on a manual click in RimWorld's developer Actions palette.
using System;
using System.Collections.Generic;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;

namespace RaidersHaveFamilies
{
    internal enum RHFDebugScenario
    {
        Rescue,
        MassRescue,
        VipRescue,
        FieldPod,
        MercenaryRescue,
        CoalitionRescue,
        Vengeance,
        TribalVengeance,
        HighTechDrop,
        HighTechSiege,
        CombinedSingleFaction,
        CoalitionManyFactions,
        CoalitionHiredDiversion,
        CombinedMixedApproach,
        DropRescueWithDiversion,
        TraitorHunt,
        FortifiedVipExtraction,
        RescueThenRaze
    }

    public static class RHFDebugActions
    {
        private const string Category = "NJR - ТЕСТЫ | РЕЙДЫ";
        private const string PrisonersCategory = "NJR - ТЕСТЫ | ПЛЕННИКИ";
        private const string ChecksCategory = "NJR - ТЕСТЫ | ДИАГНОСТИКА";
        private const int MaxDebugTargets = 24;
        // Direct developer actions execute incidents synchronously (no wait queue).
        private const int MaxCoalitionParties = 5;
        // Developer tools only: RNG never polls in regular gameplay.
        // An explicit pin is possible for reproducible tests; by default EVERY
        // spawn samples a random eligible faction, avoiding the previous draw
        // when there are two or more candidates. Reset on new game/world.
        private static Game factionSelectionGame;
        private static Faction lastRandomFaction;
        private static Faction pinnedTestFaction;

        private static void EnsureFactionSelectionGame()
        {
            if (ReferenceEquals(factionSelectionGame, Current.Game)) return;
            factionSelectionGame = Current.Game;
            lastRandomFaction = null;
            pinnedTestFaction = null;
        }

        [DebugAction(PrisonersCategory, "Фракция пленников: ЗАКРЕПИТЬ следующую (не случайно)", actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void CyclePrisonerFaction()
        {
            EnsureFactionSelectionGame();
            List<Faction> factions = EligibleTestFactions(false);
            if (factions.Count == 0)
            { Notify(Find.CurrentMap, "Нет доступных враждебных фракций с человекоподобными членами."); return; }
            int index = factions.IndexOf(pinnedTestFaction);
            pinnedTestFaction = factions[(index + 1) % factions.Count];
            Notify(Find.CurrentMap, "NJR: выбрана фракция «" + pinnedTestFaction.Name +
                "». Новые пленники любого ранга будут принадлежать ей до сброса выбора.");
        }

        [DebugAction(PrisonersCategory, "Фракция пленников: СЛУЧАЙНАЯ (снять закрепление)", actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void ResetPrisonerFaction()
        {
            EnsureFactionSelectionGame();
            pinnedTestFaction = null;
            Notify(Find.CurrentMap, "NJR: каждый новый пленник получит случайную подходящую фракцию; повтор подряд исключается, если доступно несколько.");
        }

        [DebugAction(PrisonersCategory, "Создать пленника: ранг 6 (индустриальная фракция)", actionType = DebugActionType.ToolMap,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void Rank6Industrial() { PrepareAt(UI.MouseCell(), 6, false, true); }

        [DebugAction(PrisonersCategory, "Создать пленника: ранг 8 (индустриальная фракция)", actionType = DebugActionType.ToolMap,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void Rank8Industrial() { PrepareAt(UI.MouseCell(), 8, false, true); }

        // All commands are explicitly developer mode only and are deliberately
        // split into independent menu entries for reproducible testing.
        [DebugAction(PrisonersCategory, "Создать пленника: ранг 0 (клик на карте)", actionType = DebugActionType.ToolMap,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void PreparePrisoner() { PrepareAt(UI.MouseCell(), 0); }
        [DebugAction(PrisonersCategory, "Создать пленника: ранг 1", actionType = DebugActionType.ToolMap,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void Rank1() { PrepareAt(UI.MouseCell(), 1); }
        [DebugAction(PrisonersCategory, "Создать пленника: ранг 2", actionType = DebugActionType.ToolMap,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void Rank2() { PrepareAt(UI.MouseCell(), 2); }
        [DebugAction(PrisonersCategory, "Создать пленника: ранг 3", actionType = DebugActionType.ToolMap,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void Rank3() { PrepareAt(UI.MouseCell(), 3); }
        [DebugAction(PrisonersCategory, "Создать пленника: ранг 4", actionType = DebugActionType.ToolMap,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void Rank4() { PrepareAt(UI.MouseCell(), 4); }
        [DebugAction(PrisonersCategory, "Создать пленника: ранг 5", actionType = DebugActionType.ToolMap,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void Rank5() { PrepareAt(UI.MouseCell(), 5); }
        [DebugAction(PrisonersCategory, "Создать пленника: ранг 6", actionType = DebugActionType.ToolMap,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void Rank6() { PrepareAt(UI.MouseCell(), 6); }
        [DebugAction(PrisonersCategory, "Создать пленника: ранг 7", actionType = DebugActionType.ToolMap,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void Rank7() { PrepareAt(UI.MouseCell(), 7); }
        [DebugAction(PrisonersCategory, "Создать пленника: ранг 8 (тестовый VIP)", actionType = DebugActionType.ToolMap,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void Rank8() { PrepareAt(UI.MouseCell(), 8); }

        [DebugAction(Category, "01 — Спасение: пролом и побег", actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void Rescue() { Start(RHFDebugScenario.Rescue); }

        [DebugAction(Category, "02 — Спасение: группа пленников", actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void MassRescue() { Start(RHFDebugScenario.MassRescue); }

        [DebugAction(Category, "03 — Спасение: важный пленник", actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void VipRescue() { Start(RHFDebugScenario.VipRescue); }

        [DebugAction(Category, "04 — Спасение: полевая капсула (испытание)", actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void FieldPod() { Start(RHFDebugScenario.FieldPod); }

        [DebugAction(Category, "05 — Спасение: наёмники", actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void Mercenaries() { Start(RHFDebugScenario.MercenaryRescue); }

        [DebugAction(Category, "06 — Спасение: коалиция фракций", actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void Coalition() { Start(RHFDebugScenario.CoalitionRescue); }

        [DebugAction(Category, "07 — Месть: прямой штурм", actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void Vengeance() { Start(RHFDebugScenario.Vengeance); }

        [DebugAction(Category, "08 — Месть: низкотехнологичная фракция (вариативные силы)", actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void TribalVengeance() { Start(RHFDebugScenario.TribalVengeance); }

        [DebugAction(Category, "09 — Месть: десант с воздуха", actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void HighTechDrop() { Start(RHFDebugScenario.HighTechDrop); }

        [DebugAction(Category, "10 — Месть: высокотехнологичная осада", actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void HighTechSiege() { Start(RHFDebugScenario.HighTechSiege); }

        [DebugAction(Category, "11 — Комбинированный: спасение и отвлечение", actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void CombinedSingle() { Start(RHFDebugScenario.CombinedSingleFaction); }

        [DebugAction(Category, "12 — Коалиция: три и более фракции", actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void ThreeFactions() { Start(RHFDebugScenario.CoalitionManyFactions); }

        [DebugAction(Category, "13 — Коалиция: наёмники и отвлекающий штурм", actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void HiredDistraction() { Start(RHFDebugScenario.CoalitionHiredDiversion); }

        [DebugAction(Category, "14 — Комбинированный: пролом и штурм", actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void MixedCombined() { Start(RHFDebugScenario.CombinedMixedApproach); }

        [DebugAction(Category, "15 — Комбинированный: десант и отвлечение", actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void DropRescue() { Start(RHFDebugScenario.DropRescueWithDiversion); }

        [DebugAction(Category, "16 — VIP: десант, баррикады, полевая капсула", actionType=DebugActionType.Action,
            allowedGameStates=AllowedGameStates.PlayingOnMap)]
        public static void TestFortifiedVip(){Start(RHFDebugScenario.FortifiedVipExtraction);}
        [DebugAction(Category, "17 — Спасение, затем карательный штурм", actionType=DebugActionType.Action,
            allowedGameStates=AllowedGameStates.PlayingOnMap)]
        public static void TestRescueThenRaze(){Start(RHFDebugScenario.RescueThenRaze);}
        [DebugAction(Category, "18 — Месть: охота на завербованного VIP", actionType=DebugActionType.Action,
            allowedGameStates=AllowedGameStates.PlayingOnMap)]
        public static void TestTraitorHunt(){Start(RHFDebugScenario.TraitorHunt);}

        [DebugAction(PrisonersCategory, "Создать VIP ранга 6: другая враждебная фракция", actionType = DebugActionType.ToolMap,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void OtherRank6() { PrepareAt(UI.MouseCell(), 6, true); }

        [DebugAction(PrisonersCategory, "Создать VIP ранга 8: другая враждебная фракция", actionType = DebugActionType.ToolMap,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void OtherRank8() { PrepareAt(UI.MouseCell(), 8, true); }

        [DebugAction(ChecksCategory, "29 — NJR: пересчитать богатство и очки рейда", actionType=DebugActionType.Action,
            allowedGameStates=AllowedGameStates.PlayingOnMap)]
        public static void AuditThreatR061()
        {
            Map map=Find.CurrentMap;
            if(map==null || !map.IsPlayerHome)return;
            float points=NJRThreatBudgetS41.RefreshVanillaThreat(map);
            Find.WindowStack.Add(new Dialog_MessageBox("Богатство: "+map.wealthWatcher.WealthTotal.ToString("F0")+
                "\nБогатство для рассказчика: "+map.PlayerWealthForStoryteller.ToString("F0")+
                "\nБазовые очки угрозы: "+points.ToString("F2")+
                "\nРейд не вызван. Настройки рассказчика и богатство до/после записаны в R061 Wealth."));
        }

        [DebugAction(ChecksCategory, "17 — Проверить состав отрядов операции", actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void CoalitionDiagnostics()
        {
            RHFOperationController controller = Find.CurrentMap?.GetComponent<RHFOperationController>();
            if (controller != null)
                Find.WindowStack.Add(new Dialog_MessageBox(controller.DebugStatus()));
        }

        [DebugAction(ChecksCategory, "18 — Отменить сценарии (не удаляет рейдеров)", actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void CancelTestOperations()
        {
            RHFOperationController controller = Find.CurrentMap?.GetComponent<RHFOperationController>();
            if (controller != null)
                Notify(Find.CurrentMap,"Отменено тестовых сценариев: " +
                    controller.CancelDebugOperations() + ". Уже созданные рейдеры НЕ удалены.");
        }

        [DebugAction(ChecksCategory, "11 — Правила и ограничения тестов", actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void Explain() { ExplainCurrentMap(); }

        [DebugAction(ChecksCategory, "12 — Проверить ранг и происхождение связей", actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void InspectSelectedPawn()
        {
            Pawn pawn = Find.Selector?.SingleSelectedThing as Pawn;
            if (pawn == null)
            { Messages.Message("Выбери пешку на карте, затем запусти проверку RHF.", MessageTypeDefOf.RejectInput, false); return; }
            RHFRankUI.OpenDetails(pawn);
        }

        [DebugAction(ChecksCategory, "13 — Добавить семью выбранной чужой пешке", actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void EnrichSelectedPawn()
        {
            Pawn pawn = Find.Selector?.SingleSelectedThing as Pawn;
            if (pawn == null || pawn.Faction == null || pawn.Faction.IsPlayer ||
                pawn.RaceProps == null || !pawn.RaceProps.Humanlike)
            { Messages.Message("Выбери живого гуманоида из непользовательской фракции.", MessageTypeDefOf.RejectInput, false); return; }
            RHFMapComponent.EnrichSelectedForDebug(pawn);
            RHFRankUI.OpenDetails(pawn);
        }

        [DebugAction(ChecksCategory, "14 — Проверить перехват операций WCE2", actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void MedicalDiagnostics()
        {
            Pawn p = Find.Selector?.SingleSelectedThing as Pawn;
            RHFHostageRecord r = p == null ? null : Current.Game?.GetComponent<RHFGameLedger>()?.FindHostage(p);
            string text = "RHF surgery worker hooks: " + RHFWarCrimesIntegration.PatchedWorkers +
                "\nWCE2 harmful recipes: " + RHFWarCrimesIntegration.WceRecipesDetected +
                "\nVanilla surgical recipe candidates: " + RHFWarCrimesIntegration.VanillaRecipesDetected +
                (r == null ? "\nВыберите отслеживаемого пленника для деталей." :
                 "\n" + p.LabelShortCap + ": surgeries=" + r.SurgicalCrueltyCount +
                 ", reported=" + r.DisclosedOperations + ", crueltyScore=" + r.CrueltyScore +
                 "\nПоследняя операция: " + r.LastConfirmedOperation);
            Find.WindowStack.Add(new Dialog_MessageBox(text));
        }

        [DebugAction(ChecksCategory, "15 — Тест: раскрыть травмы выбранного пленника", actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void TestDisclosure()
        {
            Pawn pawn = Find.Selector?.SingleSelectedThing as Pawn;
            RHFHostageRecord r = pawn == null ? null : Current.Game?.GetComponent<RHFGameLedger>()?.FindHostage(pawn);
            if (r != null) Find.WindowStack.Add(new Dialog_MessageBox(RHFHostageDiplomacy.ReportCondition(r)));
            else Messages.Message("Выберите пленника, уже зарегистрированного RHF.", MessageTypeDefOf.RejectInput, false);
        }

        [DebugAction(ChecksCategory, "21 — Роль и вооружение выбранного бойца RHF", actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void InspectTacticalPawn()
        {
            Pawn pawn = Find.Selector?.SingleSelectedThing as Pawn;
            if (pawn == null || pawn.Map == null)
            { Notify(Find.CurrentMap, "Сначала выбери бойца на карте."); return; }
            RHFTacticalMember member = pawn.Map.GetComponent<RHFOperationController>()?
                .TacticalMember(pawn);
            string status = member == null ? "Пешка не состоит в активном специальном отряде RHF." :
                pawn.LabelShortCap + ": " + RHFTactical.RoleName(member.Role) +
                "; вооружён=" + (!member.WeaponMissing) +
                "; проломный топор=" + RHFTactical.IsBreachAxe(pawn) +
                "; медицинские припасы=" + member.MedicalSupplies +
                "; штатное оружие/патроны не заменены.";
            Find.WindowStack.Add(new Dialog_MessageBox(status));
        }

        [DebugAction(ChecksCategory, "20 — Аудит боевых ролей и снаряжения отрядов RHF", actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void AuditTacticalSquads()
        {
            Map map = Find.CurrentMap;
            RHFOperationController controller = map?.GetComponent<RHFOperationController>();
            Find.WindowStack.Add(new Dialog_MessageBox(RHFTactical.Audit(controller)));
        }

        [DebugAction(ChecksCategory, "19 — Проверить бонусы идеологии и милосердия", actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void MoodDiagnostic()
        {
            Pawn p = Find.Selector?.SingleSelectedThing as Pawn;
            RHFHostageRecord r = p == null ? null :
                Current.Game?.GetComponent<RHFGameLedger>()?.FindHostage(p);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("NJR: проверка условий мыслей (ничего не выдаёт)");
            if (r != null)
            {
                sb.AppendLine("Пленник: " + p.LabelShortCap);
                sb.AppendLine("Издевательства: " + r.CrueltyScore +
                    "; операции: " + r.WceOperations +
                    "; уже раскрыто: " + r.DisclosedCrueltyScore);
                sb.AppendLine("Новое НЕраскрытое издевательство: " +
                    Math.Max(0, r.CrueltyScore - r.DisclosedCrueltyScore) +
                    "; предполагаемый уровень бонуса: " +
                    (r.CrueltyScore > r.DisclosedCrueltyScore ?
                        RHFMoralResponse.MemoryLevel(r.CrueltyScore-r.DisclosedCrueltyScore,r.Importance).ToString()+"/6" :
                        "нет нового события"));
                sb.AppendLine("Можно наградить за милосердие: " + RHFMercy.Eligible(r));
            }
            else sb.AppendLine("Выбери пленника RHF для проверки истории операций.");
            Map map = Find.CurrentMap;
            if (map != null)
                foreach (Pawn colonist in map.mapPawns.FreeColonistsSpawned)
                {
                    if (colonist?.needs?.mood == null) continue;
                    int mercy = RHFMercy.PreceptLevel(colonist);
                    string trait = RHFMoralResponse.EligibleTraitReason(colonist, RHFPublicityKind.Torture);
                    bool ideology = RHFMoralResponse.EligibleIdeology(colonist, p, RHFPublicityKind.Torture);
                    int visibleMoodEntries = 0;
                    if (colonist.Ideo != null)
                        foreach (Precept precept in colonist.Ideo.PreceptsListForReading)
                            if (precept?.def?.comps != null)
                                foreach (PreceptComp comp in precept.def.comps)
                                    if (comp is RHFPreceptMoodEntry) visibleMoodEntries++;
                    sb.AppendLine(colonist.LabelShortCap + ": милосердие " + mercy +
                        "/3; жестокость RHF: черта=" + (trait ?? "нет") +
                        ", идеология=" + (ideology ? "да" : "нет") +
                        "; получит бонус при новом докладе=" + (trait != null || ideology) +
                        "; строк RHF в разделе Настроение: " + visibleMoodEntries);
                }
            Find.WindowStack.Add(new Dialog_MessageBox(sb.ToString()));
        }

        [DebugAction(ChecksCategory, "22 — Аудит смерти, разделки и поедания пленников", actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void AuditDeadCaptives()
        {
            RHFGameLedger ledger = Current.Game?.GetComponent<RHFGameLedger>();
            if (ledger == null) return;
            var sb = new System.Text.StringBuilder(500);
            sb.AppendLine("RHF v0.10: посмертные дела (не меняет состояние игры)");
            int pending = 0;
            foreach (RHFHostageRecord record in ledger.Hostages)
            {
                if (!RHFHostageDiplomacy.IsDeadCase(record)) continue;
                if (++pending > 24) { sb.AppendLine("… показаны первые 24 дела"); break; }
                sb.AppendLine((record.NameAtCapture ?? "?") +
                    "; фракция=" + (record.OriginalFaction?.Name ?? "нет") +
                    "; ранг=" + record.Importance +
                    "; убийство игроком=" + record.PlayerCausedDeath +
                    "; казнь=" + record.Executed +
                    "; разделка=" + record.BodyButchered +
                    "; поедание тела=" + record.BodyConsumedDirectly +
                    "; возможно признание каннибализма=" +
                    RHFHostageDiplomacy.CanConfessCannibalism(record) +
                    "; бонус за смерть уровень=" +
                    RHFMoralResponse.MemoryLevel(Math.Max(record.CrueltyScore,
                        record.Executed ? 40 + record.Importance * 7 :
                        record.PlayerCausedDeath ? 25 + record.Importance * 5 : 0),
                        record.Importance));
            }
            sb.AppendLine("Неопубликованных дел о смерти: " + pending);
            sb.AppendLine("Последнее употребление человеческого мяса/блюда: " +
                ledger.LastHumanMeatConsumptionTick);
            Find.WindowStack.Add(new Dialog_MessageBox(sb.ToString()));
        }

        [DebugAction(ChecksCategory, "23 — Аудит тайных дел и коалиционной мести", actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void AuditReprisalCases()
        {
            RHFGameLedger ledger = Current.Game?.GetComponent<RHFGameLedger>();
            if (ledger?.ReprisalCases == null) return;
            var sb = new System.Text.StringBuilder(500);
            sb.AppendLine("NJR: тайные поиски / карательные операции: " + ledger.ReprisalCases.Count);
            int n=0;
            foreach (RHFReprisalCase c in ledger.ReprisalCases)
            {
                if (c == null) continue;
                if (++n>24) { sb.AppendLine("… показаны первые 24 дела"); break; }
                sb.AppendLine(c.VictimName + " [" + (RHFReprisalCause)c.Cause +
                    "] фракция=" + (c.Faction?.Name ?? "нет") +
                    ", состояние=" + c.Stage +
                    ", следующие действия тик=" + c.DueTick +
                    ", неудачных попыток=" + c.FailedAttempts);
            }
            Find.WindowStack.Add(new Dialog_MessageBox(sb.ToString()));
        }

        [DebugAction(ChecksCategory, "26 — Аудит вербовки и смены идеологий", actionType=DebugActionType.Action,
            allowedGameStates=AllowedGameStates.PlayingOnMap)]
        public static void AuditConvertedRecruitment()
        {
            RHFGameLedger ledger=Current.Game?.GetComponent<RHFGameLedger>();
            if(ledger==null)return;
            var sb=new System.Text.StringBuilder(900);
            sb.AppendLine("RHF v0.12 — события смены фракции/идеологии (только запись):");
            int count=0;
            foreach(RHFHostageRecord r in ledger.Hostages)
            {
                if(r==null || (!r.Recruited && !r.IdeologyConverted))continue;
                if(++count>24){sb.AppendLine("...остальные записи не показаны");break;}
                sb.AppendLine((r.NameAtCapture??"?")+" / "+r.Importance+"/8 / "+
                    (r.OriginalFaction?.Name??"?")+": завербован="+r.Recruited+
                    ", сообщение о вербовке="+r.RecruitmentReported+
                    ", месть="+r.RecruitmentRevengeQueued+
                    ", до начала мести тик="+r.NextRetaliationTick+
                    ", обращён="+r.IdeologyConverted+
                    ", новая идеология="+(r.ConvertedIdeo?.name??"нет")+
                    ", старая="+(r.OriginalIdeo?.name??"нет")+
                    ", сообщение об обращении="+r.IdeologyReported);
            }
            sb.AppendLine("Записей: "+count+". Отсутствие события означает, что Harmony hook не зарегистрировал смену.");
            Find.WindowStack.Add(new Dialog_MessageBox(sb.ToString()));
        }

        [DebugAction(ChecksCategory, "28 — NJR: групповые выкупы и доставка",
            actionType=DebugActionType.Action,
            allowedGameStates=AllowedGameStates.PlayingOnMap)]
        public static void AuditGroupRansom()
        {
            Find.WindowStack.Add(new Dialog_MessageBox(NJRGroupRansomV15.DebugStatus()));
        }

        [DebugAction(ChecksCategory, "27 — Аудит боевой доктрины / охоты / капсул", actionType=DebugActionType.Action,
            allowedGameStates=AllowedGameStates.PlayingOnMap)]
        public static void AuditMissionComposition()
        {
            RHFOperationController controller=Find.CurrentMap?.GetComponent<RHFOperationController>();
            if(controller==null)return;
            Find.WindowStack.Add(new Dialog_MessageBox(controller.DebugStatus()));
        }

        [DebugAction(ChecksCategory, "24 — Аудит автоматических семей и очереди", actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void AuditAutomaticFamilies()
        {
            Map map=Find.CurrentMap;
            var ledger=Current.Game?.GetComponent<RHFGameLedger>();
            if(map==null || ledger==null) return;
            string result=map.GetComponent<RHFMapComponent>()?.FamilyAudit() ??
                "Компонент семей на карте не обнаружен.";
            Pawn selected=Find.Selector?.SingleSelectedThing as Pawn;
            if(selected!=null && selected.Faction!=null && !selected.Faction.IsPlayer)
            {
                int visible=0;
                if(selected.relations!=null)
                    foreach(Pawn related in selected.relations.RelatedPawns)
                        if(related!=null && related!=selected && ++visible>=64) break;
                result+="\n\nВыбран: "+selected.LabelShortCap+
                    "; видимых родственных/социальных связей: "+visible+
                    "; созданных RHF: "+ledger.ConfirmedLinks(selected);
            }
            Find.WindowStack.Add(new Dialog_MessageBox(result));
        }

        [DebugAction(ChecksCategory, "25 — Аудит эвакуации и сопровождающих", actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void AuditEscorts()
        {
            RHFOperationController controller=Find.CurrentMap?.GetComponent<RHFOperationController>();
            Find.WindowStack.Add(new Dialog_MessageBox(controller?.DebugStatus() ??
                "На текущей карте нет контроллера специальных операций RHF."));
        }

        [DebugAction(ChecksCategory, "16 — Имитировать хирургическое увечье", actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void SimulateSurgery()
        {
            Pawn victim = Find.Selector?.SingleSelectedThing as Pawn;
            RHFHostageRecord record = victim == null ? null :
                Current.Game?.GetComponent<RHFGameLedger>()?.FindHostage(victim);
            if (record == null)
            { Messages.Message("Сначала выбери зарегистрированного пленника RHF.", MessageTypeDefOf.RejectInput, false); return; }
            Pawn doctor = null;
            foreach (Pawn colonist in victim.Map.mapPawns.FreeColonistsSpawned) { doctor = colonist; break; }
            if (doctor == null)
            { Messages.Message("На карте нужен живой колонист для тестового врача.", MessageTypeDefOf.RejectInput, false); return; }
            RHFHostageDiplomacy.RecordWceOperation(victim, doctor, "RHF_DEV_SIMULATED", record, 8,
                "ТЕСТОВАЯ запись хирургического вреда (здоровье НЕ изменено)");
            Messages.Message("Только тест: событие занесено в журнал RHF. Открой радиопереговоры и сообщи о нём.",
                MessageTypeDefOf.NeutralEvent, false);
        }

        private static void PrepareAt(IntVec3 requested, int rank, bool nextFaction = false,
            bool industrialOnly = false)
        {
            Map map = Find.CurrentMap;
            if (map == null || !map.IsPlayerHome)
            { Notify(map, "Открой карту своей колонии, чтобы создать тестового пленника."); return; }
            Faction faction = nextFaction ? FirstUnusedEnemy(map) : ChooseTestFaction(industrialOnly);
            if (faction == null)
            { Notify(map, nextFaction ?
                "Все подходящие враждебные фракции уже представлены пленниками. Для коалиции нужны дополнительные фракции." :
                industrialOnly ? "Нет доступной индустриальной враждебной фракции с человекоподобными членами." :
                "Нет подходящей враждебной фракции. Проверь список фракций мира."); return; }
            try
            {
                // Preserve the selected faction's own race, equipment, culture and
                // modded member kind. Never force all factions to spawn a Colonist.
                PawnKindDef basic = DebugMemberKind(faction);
                if (basic == null)
                { Notify(map, "Фракция «" + faction.Name + "» не имеет подходящего человекоподобного PawnKind."); return; }
                Pawn test = PawnGenerator.GeneratePawn(basic, faction);
                if (test == null || test.guest == null)
                { Notify(map, "Игра не смогла создать тестового персонажа."); return; }
                IntVec3 place = requested;
                if (!place.IsValid || !place.InBounds(map) || !place.Standable(map))
                { Notify(map, "Нажмите на проходимую клетку для спавна пленника."); return; }
                GenSpawn.Spawn(test, place, map);
                test.guest.SetGuestStatus(Faction.OfPlayer, GuestStatus.Prisoner);
                RHFHostageRecord record = Current.Game?.GetComponent<RHFGameLedger>()?.TrackHostage(test);
                if (record != null)
                {
                    record.Importance = Mathf.Clamp(rank, 0, 8);
                    record.RankSchema = 3;
                    record.OfficeName = RHFRankResolver.AssignTestOffice(test, rank);
                    record.DebugOffice = true;
                }
                string choiceMode = nextFaction ? "другая ещё не представленная фракция" :
                    pinnedTestFaction == faction ? "ЗАКРЕПЛЕНА вручную: снимите закрепление в NJR — пленники" :
                    "случайная фракция; следующий выбор не повторит эту, если кандидатов несколько";
                int eligibleCount = nextFaction ? -1 : EligibleTestFactions(industrialOnly).Count;
                Notify(map, "Создан пленник NJR " + rank + "/8: " + test.LabelShortCap +
                    "; фракция: «" + faction.Name + "» (" + faction.def.techLevel +
                    "). Выбор: " + choiceMode +
                    (eligibleCount == 1 ? ". В этом мире для команды подходит только одна фракция." : ""));
            }
            catch (Exception e)
            {
                Log.Error("[NJR — Not Just Raiders][Debug] Prepare prisoner failed: " + e);
                Notify(map, "Не удалось создать пленника. Подробности в игровом логе.");
            }
        }

        // Every debug invocation dispatches NOW. No incident queue, no 150-tick timer.
        // One budget is divided among up to five actual vanilla raid parties.
        private static void Start(RHFDebugScenario scenario)
        {
            Map map = Find.CurrentMap;
            if (map == null || !map.IsPlayerHome)
            { Notify(map, "Тесты запускаются только на карте колонии."); return; }
            RHFOperationController controller = map.GetComponent<RHFOperationController>();
            if (controller == null)
            { Notify(map, "Контроллер специальных операций не найден; проверь загрузку RHF."); return; }
            bool vengeance = scenario == RHFDebugScenario.Vengeance ||
                scenario == RHFDebugScenario.TribalVengeance ||
                scenario == RHFDebugScenario.HighTechDrop ||
                scenario == RHFDebugScenario.HighTechSiege ||
                scenario == RHFDebugScenario.TraitorHunt;
            bool rescue = !vengeance;
            bool coalition = scenario == RHFDebugScenario.CoalitionRescue ||
                scenario == RHFDebugScenario.CoalitionManyFactions ||
                scenario == RHFDebugScenario.CoalitionHiredDiversion ||
                scenario == RHFDebugScenario.CombinedMixedApproach ||
                scenario == RHFDebugScenario.DropRescueWithDiversion;
            bool combined = scenario == RHFDebugScenario.CombinedSingleFaction ||
                scenario == RHFDebugScenario.CoalitionHiredDiversion ||
                scenario == RHFDebugScenario.CombinedMixedApproach ||
                scenario == RHFDebugScenario.DropRescueWithDiversion;
            bool tribal = scenario == RHFDebugScenario.TribalVengeance;
            bool industrial = scenario == RHFDebugScenario.HighTechDrop ||
                scenario == RHFDebugScenario.HighTechSiege;

            List<Pawn> prisoners = Captives(map);
            // VIP missions must target the MOST important eligible hostage,
            // not whichever hostage happened to spawn first.
            bool vipMission = scenario == RHFDebugScenario.VipRescue ||
                scenario == RHFDebugScenario.FieldPod ||
                scenario == RHFDebugScenario.FortifiedVipExtraction;
            Pawn selectedVip = null;
            int selectedRank = -1;
            if (vipMission)
            {
                foreach (Pawn candidate in prisoners)
                {
                    int rank = HostageRank(candidate);
                    if (rank > selectedRank)
                    { selectedVip = candidate; selectedRank = rank; }
                }
                if (selectedVip == null)
                { Notify(map, "Сначала создай живого пленника RHF для VIP-операции."); return; }
                if (scenario == RHFDebugScenario.FortifiedVipExtraction && selectedRank < 6)
                { Notify(map, "Для операции 16 нужен пленник ранга 6–8. Создай его командой «Создать пленника: ранг 8»."); return; }
            }
            RHFHostageRecord traitorCase=null;
            if(scenario==RHFDebugScenario.TraitorHunt)
            {
                RHFGameLedger ledger=Current.Game?.GetComponent<RHFGameLedger>();
                if(ledger!=null)
                    foreach(RHFHostageRecord record in ledger.Hostages)
                        if(record!=null && record.Recruited && record.Importance>=6 &&
                           record.Pawn!=null && record.Pawn.Map==map && !record.Pawn.Dead &&
                           record.Pawn.Faction==Faction.OfPlayer && record.OriginalFaction!=null &&
                           record.OriginalFaction.HostileTo(Faction.OfPlayer))
                        {traitorCase=record;break;}
                if(traitorCase==null)
                {Notify(map,"Сначала завербуй настоящего VIP ранга 6–8, который находится на карте колонии.");return;}
            }
            Faction sponsor = scenario==RHFDebugScenario.TraitorHunt ?
                traitorCase.OriginalFaction : rescue ?
                (selectedVip != null ? SourceFaction(selectedVip) :
                 prisoners.Count>0 ? SourceFaction(prisoners[0]) : null) :
                FirstEnemy(null, tribal, industrial);
            if (sponsor == null)
            { Notify(map, rescue ? "Нужен реальный живой пленник. Сначала создай его в разделе NJR - ТЕСТЫ | ПЛЕННИКИ." :
                "Подходящей враждебной фракции для выбранного типа нападения нет."); return; }

            List<Faction> sponsors = new List<Faction> { sponsor };
            List<Pawn> targets = new List<Pawn>();
            if (rescue)
            {
                if (selectedVip != null) targets.Add(selectedVip);
                for (int i=0;i<prisoners.Count && targets.Count<MaxDebugTargets && !vipMission;i++)
                {
                    Pawn p = prisoners[i];
                    Faction f = SourceFaction(p);
                    if (f == null) continue;
                    if (coalition && !sponsors.Contains(f) && sponsors.Count < 4)
                        sponsors.Add(f);
                    if (!coalition && f != sponsor) continue;
                    if (!sponsors.Contains(f)) continue;
                    if ((scenario == RHFDebugScenario.Rescue ||
                         scenario == RHFDebugScenario.VipRescue ||
                         scenario == RHFDebugScenario.FieldPod ||
                         scenario == RHFDebugScenario.CombinedSingleFaction) && targets.Count >= 1)
                        continue;
                    targets.Add(p);
                }
                if (targets.Count == 0) { Notify(map, "Нет пригодных целей для спасения."); return; }
                // Rank was checked across ALL living prisoners above. Industrial
                // extraction capability is checked independently below.
                if (scenario == RHFDebugScenario.MassRescue && targets.Count < 2)
                { Notify(map, "Для массового освобождения необходимо как минимум два пленника."); return; }
                if (scenario == RHFDebugScenario.CoalitionManyFactions && sponsors.Count < 3)
                { Notify(map, "Для теста 3+ фракций создай трёх VIP разных фракций (команда 'другая фракция')."); return; }
            }
            Faction mercenary = null;
            if (scenario == RHFDebugScenario.MercenaryRescue ||
                scenario == RHFDebugScenario.FieldPod ||
                scenario == RHFDebugScenario.CoalitionHiredDiversion ||
                scenario == RHFDebugScenario.CombinedMixedApproach ||
                scenario == RHFDebugScenario.DropRescueWithDiversion ||
                scenario == RHFDebugScenario.FortifiedVipExtraction || coalition)
                mercenary = FindHiredEnemy(sponsors, true);
            if ((scenario == RHFDebugScenario.MercenaryRescue ||
                 scenario == RHFDebugScenario.CoalitionHiredDiversion) && mercenary == null)
            { Notify(map, "Не найдена отдельная враждебная индустриальная фракция для найма."); return; }
            Faction executor = ((scenario == RHFDebugScenario.MercenaryRescue ||
                scenario == RHFDebugScenario.FieldPod) && mercenary != null) ||
                (scenario == RHFDebugScenario.FortifiedVipExtraction &&
                 sponsor.def.techLevel < TechLevel.Industrial && mercenary != null) ? mercenary : sponsor;
            // Both field-pod rescue scenarios require an actual drop-equipped
            // rescuing faction; a land raid must never assemble launch pods.
            if ((scenario == RHFDebugScenario.FieldPod ||
                 scenario == RHFDebugScenario.FortifiedVipExtraction) &&
                executor.def.techLevel < TechLevel.Industrial)
            {
                Notify(map, "Для спасения с эвакуационными капсулами требуется индустриальная " +
                    "враждебная фракция-исполнитель с десантными капсулами. " +
                    "Обычное VIP-спасение доступно через команду 03.");
                return;
            }

            if (!executor.HostileTo(Faction.OfPlayer))
            { Notify(map, "Фракция-исполнитель должна быть враждебна игроку."); return; }

            int now = Find.TickManager.TicksGame;
            int debugImportance=scenario==RHFDebugScenario.TraitorHunt ? traitorCase.Importance : 0;
            foreach(Pawn target in targets)debugImportance=Math.Max(debugImportance,HostageRank(target));
            float total=NJRThreatBudgetS41.PartyPoints(map,executor,
                debugImportance,Math.Max(1,targets.Count),
                RHFMod.Settings.InterventionStrength);
            // Debug scenarios use exactly the natural threat budget; an
            // unaffordable pod mission reports a ground fallback, not a free raid.
            RHFOperation op = new RHFOperation
            {
                DebugForced = true, Sponsor = sponsor, Executor = executor,
                SecondSponsor = sponsors.Count>1 ? sponsors[1] : null,
                Sponsors = sponsors, Targets = targets,
                Type = (int)(rescue ? RHFOperationType.Rescue : RHFOperationType.Vengeance),
                CombinedStrike = combined,
                Vip = scenario == RHFDebugScenario.VipRescue ||
                      scenario == RHFDebugScenario.FieldPod ||
                      scenario == RHFDebugScenario.FortifiedVipExtraction ? targets[0] : null,
                TryPod = scenario == RHFDebugScenario.FieldPod ||
                    scenario == RHFDebugScenario.FortifiedVipExtraction,
                FortifyPrison=scenario==RHFDebugScenario.FortifiedVipExtraction ||
                    scenario==RHFDebugScenario.FieldPod,
                AssaultAfterObjective=scenario==RHFDebugScenario.RescueThenRaze,
                HuntTarget=scenario==RHFDebugScenario.TraitorHunt ? traitorCase.Pawn : null,
                SourceImportance=debugImportance,
                Doctrine=(int)(scenario==RHFDebugScenario.TraitorHunt ?
                    RHFDoctrine.TargetedTraitorHunt :
                    scenario==RHFDebugScenario.FortifiedVipExtraction ?
                    RHFDoctrine.FortifiedVipExtraction :
                    scenario==RHFDebugScenario.RescueThenRaze ?
                    RHFDoctrine.RescueThenAssault :
                    scenario==RHFDebugScenario.MassRescue ?
                    RHFDoctrine.InfiltrationAndExit :
                    rescue ? RHFDoctrine.PrisonBreakout : RHFDoctrine.PunitiveAssault),
                CreatedTick = now, SpawnTick = now,
                ExpiryTick = now + 180000, ThreatPoints = total
            };

            if (vengeance)
            {
                op.Parties.Add(new RHFStrikeParty(executor, RHFStrikeRole.Vengeance, total,
                    drop: scenario == RHFDebugScenario.HighTechDrop ||
                        (scenario==RHFDebugScenario.TraitorHunt &&
                         executor.def.techLevel>=TechLevel.Industrial),
                    siege: scenario == RHFDebugScenario.HighTechSiege));
            }
            else if (scenario == RHFDebugScenario.DropRescueWithDiversion)
            {
                Faction rescuer = sponsor.def.techLevel >= TechLevel.Industrial ? sponsor : mercenary;
                if (rescuer == null)
                { Notify(map,"Нет индустриальной фракции для десантного спасения."); return; }
                op.Executor = rescuer;
                Faction distraction = rescuer == sponsor && mercenary != null ? mercenary : sponsor;
                bool sameFaction=rescuer==distraction;
                float rescuePoints=NJRThreatBudgetS41.FactionAllocation(map,op,rescuer);
                float diversionPoints=sameFaction ? rescuePoints*.42f :
                    NJRThreatBudgetS41.FactionAllocation(map,op,distraction);
                op.Parties.Add(new RHFStrikeParty(rescuer, RHFStrikeRole.Rescue,
                    sameFaction ? rescuePoints-diversionPoints : rescuePoints, drop: true));
                op.Parties.Add(new RHFStrikeParty(distraction, RHFStrikeRole.Diversion,
                    diversionPoints));
            }
            else if (scenario == RHFDebugScenario.CombinedSingleFaction)
            {
                // Same faction, two separately deployed elements with distinct tasks.
                op.Parties.Add(new RHFStrikeParty(sponsor, RHFStrikeRole.Rescue, total * .60f,
                    breach: true));
                op.Parties.Add(new RHFStrikeParty(sponsor, RHFStrikeRole.Diversion, total * .40f));
            }
            else if (scenario == RHFDebugScenario.MercenaryRescue ||
                     scenario == RHFDebugScenario.FieldPod ||
                     scenario == RHFDebugScenario.FortifiedVipExtraction)
            {
                op.Parties.Add(new RHFStrikeParty(executor, RHFStrikeRole.Rescue, total,
                    drop: (scenario==RHFDebugScenario.FieldPod ||
                           scenario==RHFDebugScenario.FortifiedVipExtraction) &&
                           executor.def.techLevel>=TechLevel.Industrial,
                    breach: true));
            }
            else if (coalition)
            {
                // 1..4 hostage sponsors + optional hired assault wing. They need
                // not all have captives, and the mercenary never requires its own.
                int rescueCount = sponsors.Count;
                bool addHire = combined && mercenary != null &&
                    rescueCount < MaxCoalitionParties &&
                    scenario != RHFDebugScenario.CoalitionManyFactions;
                int partyCount = rescueCount + (addHire ? 1 : 0);
                // Each coalition/mercenary wing has independent vanilla base.
                float per=total;
                for (int i=0; i<rescueCount && i<4; i++)
                    op.Parties.Add(new RHFStrikeParty(sponsors[i], RHFStrikeRole.Rescue,
                        NJRThreatBudgetS41.PartyPoints(map,sponsors[i],
                            debugImportance,Math.Max(1,NJRThreatBudgetS41.CaptivesOf(
                                sponsors[i],op,map)),RHFMod.Settings.InterventionStrength),
                        breach: true));
                if (addHire)
                    op.Parties.Add(new RHFStrikeParty(mercenary, RHFStrikeRole.Diversion,
                        NJRThreatBudgetS41.PartyPoints(map,mercenary,debugImportance,1,
                            RHFMod.Settings.InterventionStrength),
                        drop: scenario == RHFDebugScenario.CombinedMixedApproach &&
                              mercenary.def.techLevel >= TechLevel.Industrial));
                // Even without hired soldiers, split one sponsor into a distraction.
                else if (sponsors.Count == 1 && combined)
                {
                    op.Parties[0].Points = total * .60f;
                    op.Parties.Add(new RHFStrikeParty(sponsor, RHFStrikeRole.Diversion,
                        total * .40f));
                }
            }
            else
            {
                op.Parties.Add(new RHFStrikeParty(sponsor, RHFStrikeRole.Rescue, total,
                    breach: true));
            }
            op.ThreatPoints=0f;
            foreach(RHFStrikeParty party in op.Parties)
                if(party!=null)op.ThreatPoints+=party.Points;
            // A DebugAction does not affect the natural raid scheduler. The raid
            // is performed on this click, not silently scheduled for later.
            if (!controller.Register(op))
            { Notify(map, "Достигнут лимит: максимум 4 параллельные специальные операции."); return; }
            bool appeared = controller.ForceDeployNow(op);
            if (!appeared)
            { Notify(map, "Рейд НЕ появился: RimWorld отклонила генерацию фракции/стратегии. " +
                "Проверь журнал ошибок. Запись операции отменена."); return; }
            Notify(map, "Рейд вызван СЕЙЧАС: " + scenario + ". Успешных групп: " +
                op.SuccessfulParties + "/" + op.Parties.Count + "; спонсоров: " +
                sponsors.Count + "; суммарный бюджет групп " + Mathf.RoundToInt(op.ThreatPoints) +
                ". Если использованы капсулы, дождись открытия капсул, не повторяй вызов.");
        }

        private static PawnKindDef DebugMemberKind(Faction faction)
        {
            if (faction?.def == null) return null;
            PawnKindDef basic = faction.def.basicMemberKind;
            if (basic?.race?.race?.Humanlike == true) return basic;
            // Modded factions often declare only pawnGroupMakers.options, with
            // no basicMemberKind and no PawnKind.defaultFactionDef. Read the
            // faction's real group entries before using the global fallback.
            if (faction.def.pawnGroupMakers != null)
                foreach (PawnGroupMaker maker in faction.def.pawnGroupMakers)
                    if (maker?.options != null)
                        foreach (PawnGenOption option in maker.options)
                        {
                            PawnKindDef kind = option?.kind;
                            if (kind?.race?.race?.Humanlike == true) return kind;
                        }
            // Last resort: older faction mods declare defaultFactionDef only.
            // This lookup runs exclusively on developer clicks.
            foreach (PawnKindDef candidate in DefDatabase<PawnKindDef>.AllDefsListForReading)
                if (candidate != null && candidate.defaultFactionDef == faction.def &&
                    candidate.race?.race?.Humanlike == true)
                    return candidate;
            return null;
        }

        private static bool EligibleTestFaction(Faction faction, bool industrialOnly)
        {
            return faction != null && faction.def != null && !faction.def.hidden &&
                !faction.IsPlayer && faction.HostileTo(Faction.OfPlayer) &&
                DebugMemberKind(faction) != null &&
                (!industrialOnly || faction.def.techLevel >= TechLevel.Industrial);
        }

        private static List<Faction> EligibleTestFactions(bool industrialOnly)
        {
            var result = new List<Faction>();
            foreach (Faction faction in Find.FactionManager.AllFactionsListForReading)
                if (EligibleTestFaction(faction, industrialOnly)) result.Add(faction);
            return result;
        }

        // True RNG on each debug invocation. Exclude only the immediately
        // preceding draw (not a deterministic round-robin or shuffle cycle).
        // Uniform over remaining eligible candidates, no allocations per tick.
        private static Faction RandomTestFaction(List<Faction> candidates)
        {
            EnsureFactionSelectionGame();
            if (candidates == null || candidates.Count == 0) return null;
            var choices = new List<Faction>(candidates.Count);
            if (candidates.Count > 1)
                foreach (Faction candidate in candidates)
                    if (candidate != lastRandomFaction) choices.Add(candidate);
            if (choices.Count == 0) choices.AddRange(candidates);
            Faction chosen = choices[Rand.Range(0, choices.Count)];
            lastRandomFaction = chosen;
            return chosen;
        }

        private static Faction ChooseTestFaction(bool industrialOnly)
        {
            EnsureFactionSelectionGame();
            List<Faction> candidates = EligibleTestFactions(industrialOnly);
            if (candidates.Count == 0) return null;
            if (EligibleTestFaction(pinnedTestFaction, industrialOnly) &&
                candidates.Contains(pinnedTestFaction)) return pinnedTestFaction;
            return RandomTestFaction(candidates);
        }

        private static Faction FindUnusedEnemyCandidate(Faction skip, HashSet<Faction> used)
        {
            var candidates = new List<Faction>();
            foreach (Faction faction in Find.FactionManager.AllFactionsListForReading)
                if (faction != skip && !used.Contains(faction) &&
                    EligibleTestFaction(faction, false)) candidates.Add(faction);
            // The special "another faction" tools still require an UNUSED
            // sponsor, but choose randomly among all currently unused ones.
            return RandomTestFaction(candidates);
        }

        private static Faction FirstUnusedEnemy(Map map)
        {
            HashSet<Faction> used = new HashSet<Faction>();
            foreach (Pawn pawn in Captives(map))
            {
                Faction faction = SourceFaction(pawn);
                if (faction != null) used.Add(faction);
            }
            return FindUnusedEnemyCandidate(null, used);
        }

        private static Faction FindHiredEnemy(List<Faction> sponsors, bool industrial)
        {
            foreach (Faction faction in Find.FactionManager.AllFactionsListForReading)
                if (faction != null && !sponsors.Contains(faction) &&
                    !faction.IsPlayer && faction.def != null && !faction.def.hidden &&
                    faction.HostileTo(Faction.OfPlayer) &&
                    (!industrial || faction.def.techLevel >= TechLevel.Industrial))
                    return faction;
            return null;
        }

        private static List<Pawn> Captives(Map map)
        {
            var list = new List<Pawn>();
            var seen = new HashSet<Pawn>();
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (pawn == null || pawn.Dead || !pawn.IsPrisonerOfColony ||
                    !pawn.RaceProps.Humanlike || !seen.Add(pawn)) continue;
                Faction faction = SourceFaction(pawn);
                if (faction == null || !faction.HostileTo(Faction.OfPlayer)) continue;
                list.Add(pawn);
            }
            return list;
        }

        private static int HostageRank(Pawn pawn)
        {
            RHFHostageRecord record = Current.Game?.GetComponent<RHFGameLedger>()?.FindHostage(pawn);
            return record != null ? record.Importance : RHFHostageDiplomacy.RankOf(pawn);
        }

        private static Faction SourceFaction(Pawn pawn)
        {
            RHFHostageRecord record = Current.Game?.GetComponent<RHFGameLedger>()?.FindHostage(pawn);
            return record?.OriginalFaction ?? pawn?.Faction;
        }

        private static Faction FirstEnemy(Faction exclude, bool tribal, bool industrial)
        {
            foreach (Faction faction in Find.FactionManager.AllFactionsListForReading)
            {
                if (faction == null || faction == exclude || faction.IsPlayer ||
                    faction.def == null || faction.def.hidden ||
                    !faction.HostileTo(Faction.OfPlayer)) continue;
                if (tribal && faction.def.techLevel >= TechLevel.Industrial) continue;
                if (industrial && faction.def.techLevel < TechLevel.Industrial) continue;
                return faction;
            }
            return null;
        }

        private static RaidStrategyDef Strategy(params string[] names)
        {
            foreach (string name in names)
            {
                RaidStrategyDef def = DefDatabase<RaidStrategyDef>.GetNamedSilentFail(name);
                if (def != null) return def;
            }
            return RaidStrategyDefOf.ImmediateAttack;
        }

        private static void ExplainCurrentMap()
        {
            Map map = Find.CurrentMap;
            if (map == null) return;
            int prisoners = Captives(map).Count;
            Notify(map, "NJR: пленников враждебных фракций " + prisoners +
                ". Пленников рангов 0..8 создавай щелчком на карте. " +
                "Коалиция: одна и более фракций-покровителей + наёмники, отдельный " +
                "тест для 3+ VIP. Комбинированный рейд = спасение + фронтальная атака. " +
                "Тесты запускают рейды немедленно, без 150-тик. очереди. VIP pod — опытная реализация, " +
                "не настоящая ванильная транспортная капсула. Тестируй только на копии сохранения.");
        }

        private static void Notify(Map map, string message)
        {
            Log.Message("[NJR — Not Just Raiders][Debug] " + message);
            if (map != null)
                Messages.Message(message, new TargetInfo(map.Center, map), MessageTypeDefOf.NeutralEvent, false);
        }
    }
}
