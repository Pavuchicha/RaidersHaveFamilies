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
        DropRescueWithDiversion
    }

    public static class RHFDebugActions
    {
        private const string Category = "RHF - ТЕСТЫ | РЕЙДЫ";
        private const string PrisonersCategory = "RHF - ТЕСТЫ | ПЛЕННИКИ";
        private const string ChecksCategory = "RHF - ТЕСТЫ | ДИАГНОСТИКА";
        private const int MaxDebugTargets = 24;
        // Direct developer actions execute incidents synchronously (no wait queue).
        private const int MaxCoalitionParties = 5;

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

        [DebugAction(Category, "01 - Rescue: breach and escape", actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void Rescue() { Start(RHFDebugScenario.Rescue); }

        [DebugAction(Category, "02 - Rescue: mass prisoners", actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void MassRescue() { Start(RHFDebugScenario.MassRescue); }

        [DebugAction(Category, "03 - Rescue: important prisoner", actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void VipRescue() { Start(RHFDebugScenario.VipRescue); }

        [DebugAction(Category, "04 - Rescue: field pod prototype", actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void FieldPod() { Start(RHFDebugScenario.FieldPod); }

        [DebugAction(Category, "05 - Rescue: hired mercenaries", actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void Mercenaries() { Start(RHFDebugScenario.MercenaryRescue); }

        [DebugAction(Category, "06 - Rescue: coalition (all captive factions + optional hire)", actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void Coalition() { Start(RHFDebugScenario.CoalitionRescue); }

        [DebugAction(Category, "07 - Vengeance: direct assault", actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void Vengeance() { Start(RHFDebugScenario.Vengeance); }

        [DebugAction(Category, "08 - Vengeance: tribal animals", actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void TribalVengeance() { Start(RHFDebugScenario.TribalVengeance); }

        [DebugAction(Category, "09 - Vengeance: high-tech drop", actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void HighTechDrop() { Start(RHFDebugScenario.HighTechDrop); }

        [DebugAction(Category, "10 - Vengeance: high-tech siege", actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void HighTechSiege() { Start(RHFDebugScenario.HighTechSiege); }

        [DebugAction(Category, "11 - Combined strike: one faction splits rescue / diversion", actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void CombinedSingle() { Start(RHFDebugScenario.CombinedSingleFaction); }

        [DebugAction(Category, "12 - Coalition: three or more hostage factions", actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void ThreeFactions() { Start(RHFDebugScenario.CoalitionManyFactions); }

        [DebugAction(Category, "13 - Coalition: hostage faction + hired distraction", actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void HiredDistraction() { Start(RHFDebugScenario.CoalitionHiredDiversion); }

        [DebugAction(Category, "14 - Combined: rescue, breach, drop or frontal diversion", actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void MixedCombined() { Start(RHFDebugScenario.CombinedMixedApproach); }

        [DebugAction(Category, "15 - Combined: rescue by pods + frontal diversion", actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void DropRescue() { Start(RHFDebugScenario.DropRescueWithDiversion); }

        [DebugAction(PrisonersCategory, "Создать VIP ранга 6: другая враждебная фракция", actionType = DebugActionType.ToolMap,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void OtherRank6() { PrepareAt(UI.MouseCell(), 6, true); }

        [DebugAction(PrisonersCategory, "Создать VIP ранга 8: другая враждебная фракция", actionType = DebugActionType.ToolMap,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void OtherRank8() { PrepareAt(UI.MouseCell(), 8, true); }

        [DebugAction(ChecksCategory, "17 - Inspect active rescue/coalition parties", actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void CoalitionDiagnostics()
        {
            RHFOperationController controller = Find.CurrentMap?.GetComponent<RHFOperationController>();
            if (controller != null)
                Find.WindowStack.Add(new Dialog_MessageBox(controller.DebugStatus()));
        }

        [DebugAction(ChecksCategory, "18 - Cancel RHF test operations (not spawned pawns)", actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void CancelTestOperations()
        {
            RHFOperationController controller = Find.CurrentMap?.GetComponent<RHFOperationController>();
            if (controller != null)
                Notify(Find.CurrentMap,"Отменено тестовых сценариев: " +
                    controller.CancelDebugOperations() + ". Уже созданные рейдеры НЕ удалены.");
        }

        [DebugAction(ChecksCategory, "11 - Show test setup and limitations", actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void Explain() { ExplainCurrentMap(); }

        [DebugAction(ChecksCategory, "12 - Inspect selected pawn: rank and RHF origins", actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void InspectSelectedPawn()
        {
            Pawn pawn = Find.Selector?.SingleSelectedThing as Pawn;
            if (pawn == null)
            { Messages.Message("Выбери пешку на карте, затем запусти проверку RHF.", MessageTypeDefOf.RejectInput, false); return; }
            RHFRankUI.OpenDetails(pawn);
        }

        [DebugAction(ChecksCategory, "13 - Add family to selected outsider (test)", actionType = DebugActionType.Action,
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

        [DebugAction(ChecksCategory, "14 - Show surgery hooks / selected prisoner evidence", actionType = DebugActionType.Action,
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

        [DebugAction(ChecksCategory, "15 - Test disclosure on selected prisoner", actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void TestDisclosure()
        {
            Pawn pawn = Find.Selector?.SingleSelectedThing as Pawn;
            RHFHostageRecord r = pawn == null ? null : Current.Game?.GetComponent<RHFGameLedger>()?.FindHostage(pawn);
            if (r != null) Find.WindowStack.Add(new Dialog_MessageBox(RHFHostageDiplomacy.ReportCondition(r)));
            else Messages.Message("Выберите пленника, уже зарегистрированного RHF.", MessageTypeDefOf.RejectInput, false);
        }

        [DebugAction(ChecksCategory, "16 - Simulate recorded surgery on selected hostage", actionType = DebugActionType.Action,
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

        private static void PrepareAt(IntVec3 requested, int rank, bool nextFaction = false)
        {
            Map map = Find.CurrentMap;
            if (map == null || !map.IsPlayerHome)
            { Notify(map, "Открой карту своей колонии, чтобы создать тестового пленника."); return; }
            Faction faction = nextFaction ? FirstUnusedEnemy(map) : FirstEnemy(null, false, false);
            if (faction == null)
            { Notify(map, nextFaction ?
                "Все доступные враждебные фракции уже представлены пленниками. Для коалиции нужны дополнительные фракции." :
                "Нет подходящей враждебной фракции. Создай её в сценарии и повтори."); return; }
            try
            {
                PawnKindDef basic = DefDatabase<PawnKindDef>.GetNamedSilentFail("Colonist");
                if (basic == null)
                { Notify(map, "Не найден базовый PawnKind 'Colonist' в этой сборке."); return; }
                Pawn test = PawnGenerator.GeneratePawn(basic, faction);
                if (test == null || test.guest == null)
                { Notify(map, "Игра не смогла создать тестового персонажа."); return; }
                IntVec3 place = requested;
                if (!place.IsValid || !place.InBounds(map) || !place.Standable(map))
                { Notify(map, "Нажмите на проходимую клетку для спавна пленника."); return; }
                GenSpawn.Spawn(test, place, map);
                test.guest.SetGuestStatus(Faction.OfPlayer, GuestStatus.Prisoner);
                RHFHostageRecord record = Current.Game?.GetComponent<RHFGameLedger>()?.TrackHostage(test);
                if (record != null) { record.Importance = Mathf.Clamp(rank, 0, 8); record.RankSchema = 3; }
                Notify(map, "Создан тестовый пленник с рангом RHF " + rank + "/8 фракции " + faction.Name +
                    ". При желании добавь VIP другой фракции для коалиции.");
            }
            catch (Exception e)
            {
                Log.Error("[Raiders Have Families][Debug] Prepare prisoner failed: " + e);
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
                scenario == RHFDebugScenario.HighTechSiege;
            bool rescue = !vengeance;
            bool coalition = scenario == RHFDebugScenario.CoalitionRescue ||
                scenario == RHFDebugScenario.CoalitionManyFactions ||
                scenario == RHFDebugScenario.CoalitionHiredDiversion ||
                scenario == RHFDebugScenario.CombinedMixedApproach ||
                scenario == RHFDebugScenario.DropRescueWithDiversion;
            bool combined = coalition || scenario == RHFDebugScenario.CombinedSingleFaction;
            bool tribal = scenario == RHFDebugScenario.TribalVengeance;
            bool industrial = scenario == RHFDebugScenario.HighTechDrop ||
                scenario == RHFDebugScenario.HighTechSiege;

            List<Pawn> prisoners = Captives(map);
            Faction sponsor = rescue ? (prisoners.Count>0 ? SourceFaction(prisoners[0]) : null) :
                FirstEnemy(null, tribal, industrial);
            if (sponsor == null)
            { Notify(map, rescue ? "Нужен реальный живой пленник. Сначала создай его в разделе RHF - ТЕСТЫ | ПЛЕННИКИ." :
                "Подходящей враждебной фракции для выбранного типа нападения нет."); return; }

            List<Faction> sponsors = new List<Faction> { sponsor };
            List<Pawn> targets = new List<Pawn>();
            if (rescue)
            {
                for (int i=0;i<prisoners.Count && targets.Count<MaxDebugTargets;i++)
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
                scenario == RHFDebugScenario.DropRescueWithDiversion || coalition)
                mercenary = FindHiredEnemy(sponsors, true);
            if ((scenario == RHFDebugScenario.MercenaryRescue ||
                 scenario == RHFDebugScenario.CoalitionHiredDiversion) && mercenary == null)
            { Notify(map, "Не найдена отдельная враждебная индустриальная фракция для найма."); return; }
            Faction executor = (scenario == RHFDebugScenario.MercenaryRescue ||
                scenario == RHFDebugScenario.FieldPod) && mercenary != null ? mercenary : sponsor;
            if (!executor.HostileTo(Faction.OfPlayer))
            { Notify(map, "Фракция-исполнитель должна быть враждебна игроку."); return; }

            int now = Find.TickManager.TicksGame;
            float basePoints = StorytellerUtility.DefaultThreatPointsNow(map);
            float factor = scenario == RHFDebugScenario.MassRescue ? 2f :
                tribal ? 2.2f : scenario == RHFDebugScenario.HighTechSiege ? 2f : 1.65f;
            float total = Mathf.Clamp(Mathf.Max(500f, basePoints) * factor, 650f, 12000f);
            RHFOperation op = new RHFOperation
            {
                DebugForced = true, Sponsor = sponsor, Executor = executor,
                SecondSponsor = sponsors.Count>1 ? sponsors[1] : null,
                Sponsors = sponsors, Targets = targets,
                Type = (int)(rescue ? RHFOperationType.Rescue : RHFOperationType.Vengeance),
                CombinedStrike = combined,
                Vip = scenario == RHFDebugScenario.VipRescue ||
                      scenario == RHFDebugScenario.FieldPod ? targets[0] : null,
                TryPod = scenario == RHFDebugScenario.FieldPod,
                CreatedTick = now, SpawnTick = now,
                ExpiryTick = now + 180000, ThreatPoints = total
            };

            if (vengeance)
            {
                op.Parties.Add(new RHFStrikeParty(executor, RHFStrikeRole.Vengeance, total,
                    drop: scenario == RHFDebugScenario.HighTechDrop,
                    siege: scenario == RHFDebugScenario.HighTechSiege));
            }
            else if (scenario == RHFDebugScenario.DropRescueWithDiversion)
            {
                Faction rescuer = sponsor.def.techLevel >= TechLevel.Industrial ? sponsor : mercenary;
                if (rescuer == null)
                { Notify(map,"Нет индустриальной фракции для десантного спасения."); return; }
                op.Executor = rescuer;
                Faction distraction = rescuer == sponsor && mercenary != null ? mercenary : sponsor;
                op.Parties.Add(new RHFStrikeParty(rescuer, RHFStrikeRole.Rescue,
                    total * .58f, drop: true));
                op.Parties.Add(new RHFStrikeParty(distraction, RHFStrikeRole.Diversion,
                    total * .42f));
            }
            else if (scenario == RHFDebugScenario.CombinedSingleFaction)
            {
                // Same faction, two separately deployed elements with distinct tasks.
                op.Parties.Add(new RHFStrikeParty(sponsor, RHFStrikeRole.Rescue, total * .60f,
                    breach: true));
                op.Parties.Add(new RHFStrikeParty(sponsor, RHFStrikeRole.Diversion, total * .40f));
            }
            else if (scenario == RHFDebugScenario.MercenaryRescue ||
                     scenario == RHFDebugScenario.FieldPod)
            {
                op.Parties.Add(new RHFStrikeParty(executor, RHFStrikeRole.Rescue, total,
                    breach: true));
            }
            else if (coalition)
            {
                // 1..4 hostage sponsors + optional hired assault wing. They need
                // not all have captives, and the mercenary never requires its own.
                int rescueCount = sponsors.Count;
                bool addHire = mercenary != null && rescueCount < MaxCoalitionParties &&
                    scenario != RHFDebugScenario.CoalitionManyFactions;
                int partyCount = rescueCount + (addHire ? 1 : 0);
                total = Mathf.Max(total, 350f * partyCount);
                float per = total / partyCount;
                for (int i=0; i<rescueCount && i<4; i++)
                    op.Parties.Add(new RHFStrikeParty(sponsors[i], RHFStrikeRole.Rescue,
                        per, breach: true));
                if (addHire)
                    op.Parties.Add(new RHFStrikeParty(mercenary, RHFStrikeRole.Diversion, per,
                        drop: scenario == RHFDebugScenario.CombinedMixedApproach &&
                              mercenary.def.techLevel >= TechLevel.Industrial));
                // Even without hired soldiers, split one sponsor into a distraction.
                else if (sponsors.Count == 1)
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
                sponsors.Count + "; всего бюджет " + Mathf.RoundToInt(total) +
                ". Если использованы капсулы, дождись открытия капсул, не повторяй вызов.");
        }

        private static Faction FindUnusedEnemyCandidate(Faction skip, HashSet<Faction> used)
        {
            foreach (Faction faction in Find.FactionManager.AllFactionsListForReading)
                if (faction != null && faction != skip && !used.Contains(faction) &&
                    !faction.IsPlayer && faction.def != null && !faction.def.hidden &&
                    faction.HostileTo(Faction.OfPlayer)) return faction;
            return null;
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
            Notify(map, "RHF: пленников враждебных фракций " + prisoners +
                ". Пленников рангов 0..8 создавай щелчком на карте. " +
                "Коалиция: одна и более фракций-покровителей + наёмники, отдельный " +
                "тест для 3+ VIP. Комбинированный рейд = спасение + фронтальная атака. " +
                "Тесты запускают рейды немедленно, без 150-тик. очереди. VIP pod — опытная реализация, " +
                "не настоящая ванильная транспортная капсула. Тестируй только на копии сохранения.");
        }

        private static void Notify(Map map, string message)
        {
            Log.Message("[Raiders Have Families][Debug] " + message);
            if (map != null)
                Messages.Message(message, new TargetInfo(map.Center, map), MessageTypeDefOf.NeutralEvent, false);
        }
    }
}
