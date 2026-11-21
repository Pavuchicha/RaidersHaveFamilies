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
        HighTechSiege
    }

    public static class RHFDebugActions
    {
        private const string Category = "RHF - ТЕСТЫ | РЕЙДЫ";
        private const string PrisonersCategory = "RHF - ТЕСТЫ | ПЛЕННИКИ";
        private const string ChecksCategory = "RHF - ТЕСТЫ | ДИАГНОСТИКА";
        private const int MaxDebugTargets = 24;
        private const int DelayTicks = 150;

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

        [DebugAction(Category, "06 - Rescue: two-faction coalition", actionType = DebugActionType.Action,
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

        private static void PrepareAt(IntVec3 requested, int rank)
        {
            Map map = Find.CurrentMap;
            if (map == null || !map.IsPlayerHome)
            { Notify(map, "Открой карту своей колонии, чтобы создать тестового пленника."); return; }
            Faction faction = FirstEnemy(null, false, false);
            if (faction == null)
            { Notify(map, "Нет подходящей враждебной фракции. Создай её в сценарии и повтори."); return; }
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
                    ". Для коалиции нужны пленники двух разных фракций; можно создать их через обычные средства разработчика.");
            }
            catch (Exception e)
            {
                Log.Error("[Raiders Have Families][Debug] Prepare prisoner failed: " + e);
                Notify(map, "Не удалось создать пленника. Подробности в игровом логе.");
            }
        }

        private static void Start(RHFDebugScenario scenario)
        {
            Map map = Find.CurrentMap;
            if (map == null || !map.IsPlayerHome)
            { Notify(map, "Тестовые рейды запускаются только на карте колонии."); return; }
            var controller = map.GetComponent<RHFOperationController>();
            if (controller == null)
            { Notify(map, "Не найден контроллер спасательных операций."); return; }

            bool rescue = scenario == RHFDebugScenario.Rescue ||
                scenario == RHFDebugScenario.MassRescue ||
                scenario == RHFDebugScenario.VipRescue ||
                scenario == RHFDebugScenario.FieldPod ||
                scenario == RHFDebugScenario.MercenaryRescue ||
                scenario == RHFDebugScenario.CoalitionRescue;
            bool tribal = scenario == RHFDebugScenario.TribalVengeance;
            bool industrial = scenario == RHFDebugScenario.FieldPod ||
                scenario == RHFDebugScenario.HighTechDrop ||
                scenario == RHFDebugScenario.HighTechSiege;

            var prisoners = Captives(map);
            Faction sponsor = rescue ? (prisoners.Count > 0 ? SourceFaction(prisoners[0]) : null) : null;
            if (rescue && sponsor == null)
            {
                Notify(map, "Для спасательной операции нужен пленник враждебной фракции. " +
                    "Сначала создай пленника в категории RHF - ТЕСТЫ | ПЛЕННИКИ или захвати рейдера.");
                return;
            }
            Faction secondSponsor = null;
            if (scenario == RHFDebugScenario.CoalitionRescue)
            {
                foreach (Pawn p in prisoners)
                    if (SourceFaction(p) != null && SourceFaction(p) != sponsor)
                    { secondSponsor = SourceFaction(p); break; }
                if (secondSponsor == null)
                {
                    Notify(map, "Для коалиционного теста нужны пленники ДВУХ разных враждебных фракций.");
                    return;
                }
            }
            if (!rescue)
            {
                sponsor = FirstEnemy(null, tribal, industrial);
                if (sponsor == null)
                { Notify(map, "Для выбранного типа нет подходящей враждебной фракции."); return; }
            }

            Faction executor = sponsor;
            if (scenario == RHFDebugScenario.MercenaryRescue || scenario == RHFDebugScenario.FieldPod)
            {
                executor = FirstEnemy(sponsor, false, true);
                if (executor == null)
                {
                    if (scenario == RHFDebugScenario.FieldPod && sponsor.def.techLevel >= TechLevel.Industrial)
                        executor = sponsor;
                    else
                    { Notify(map, "Нужна дополнительная враждебная индустриальная фракция-исполнитель."); return; }
                }
            }
            if (scenario == RHFDebugScenario.CoalitionRescue &&
                sponsor.def.techLevel < TechLevel.Industrial)
            {
                Faction hire = FirstEnemy(sponsor, false, true);
                if (hire != null) executor = hire;
            }
            if (executor == null || !executor.HostileTo(Faction.OfPlayer))
            { Notify(map, "Фракция-исполнитель должна быть враждебна колонии."); return; }

            var targets = new List<Pawn>();
            if (rescue)
            {
                for (int i=0;i<prisoners.Count && targets.Count<MaxDebugTargets;i++)
                {
                    Pawn p = prisoners[i];
                    Faction f = SourceFaction(p);
                    if (f != sponsor && f != secondSponsor) continue;
                    if (scenario == RHFDebugScenario.Rescue && targets.Count >= 1) break;
                    if (scenario == RHFDebugScenario.VipRescue && targets.Count >= 1) break;
                    if (scenario == RHFDebugScenario.FieldPod && targets.Count >= 1) break;
                    targets.Add(p);
                }
                if (targets.Count == 0)
                { Notify(map, "Не нашлось живого пленника для операции."); return; }
                if (scenario == RHFDebugScenario.MassRescue && targets.Count < 2)
                { Notify(map, "Для массового освобождения нужно хотя бы два пленника одной фракции."); return; }
            }

            int now = Find.TickManager.TicksGame;
            float basePoints = StorytellerUtility.DefaultThreatPointsNow(map);
            float factor = scenario == RHFDebugScenario.MassRescue ? 2.0f :
                scenario == RHFDebugScenario.TribalVengeance ? 2.2f :
                scenario == RHFDebugScenario.HighTechSiege ? 2.0f : 1.6f;
            float points = Mathf.Clamp(Mathf.Max(500f, basePoints) * factor, 600f, 12000f);
            var op = new RHFOperation
            {
                DebugForced = true,
                Sponsor = sponsor,
                Executor = executor,
                SecondSponsor = secondSponsor,
                Targets = targets,
                Type = (int)(rescue ? RHFOperationType.Rescue : RHFOperationType.Vengeance),
                Vip = scenario == RHFDebugScenario.VipRescue || scenario == RHFDebugScenario.FieldPod ? targets[0] : null,
                TryPod = scenario == RHFDebugScenario.FieldPod,
                CreatedTick = now,
                SpawnTick = now + DelayTicks,
                ExpiryTick = now + DelayTicks + 180000,
                ThreatPoints = points
            };
            if (!controller.Register(op))
            { Notify(map, "Лимит: не более четырёх одновременных специальных операций на карту."); return; }

            var parms = new IncidentParms { target = map, faction = executor, points = points };
            if (rescue)
            {
                parms.raidStrategy = Strategy("Breaching", "ImmediateAttackSmart", "ImmediateAttack");
                // Field construction needs a real path in, so not drop-on-prison.
            }
            else if (scenario == RHFDebugScenario.HighTechSiege)
                parms.raidStrategy = Strategy("Siege", "ImmediateAttackSmart", "ImmediateAttack");
            else
                parms.raidStrategy = Strategy("ImmediateAttackSmart", "ImmediateAttack");
            if (scenario == RHFDebugScenario.HighTechDrop)
            {
                PawnsArrivalModeDef drop = DefDatabase<PawnsArrivalModeDef>.GetNamedSilentFail("CenterDrop");
                if (drop == null)
                {
                    controller.CancelDebugOperation(op);
                    Notify(map, "В этой сборке/наборе модов нет режима CenterDrop. Тест десанта отменён.");
                    return;
                }
                parms.raidArrivalMode = drop;
            }
            if (scenario == RHFDebugScenario.HighTechSiege &&
                DefDatabase<RaidStrategyDef>.GetNamedSilentFail("Siege") == null)
            {
                controller.CancelDebugOperation(op);
                Notify(map, "В этой сборке/наборе модов нет стратегии Siege. Тест осады отменён.");
                return;
            }
            if (scenario == RHFDebugScenario.TribalVengeance)
                parms.points *= .92f; // reserve some budget for animals

            // This is the SAME incident/AI entry point as real faction reprisals.
            // The dev command bypasses chance, diplomacy thresholds, and cooldowns.
            if (!Find.Storyteller.incidentQueue.Add(IncidentDefOf.RaidEnemy,
                op.SpawnTick, parms))
            {
                controller.CancelDebugOperation(op);
                Notify(map, "RimWorld отказался поставить этот рейд в очередь.");
                return;
            }
            Notify(map, "Тестовый рейд запланирован: " + scenario +
                "; исполнитель: " + executor.Name + "; сила: " + Mathf.RoundToInt(points) +
                "; появление через " + DelayTicks + " тиков. Сохранение НЕ делается автоматически.");
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
                ". '00' создаёт тестового пленника. Rescue/VIP/mercenaries/coalition требуют " +
                "пленников (коалиция — две фракции, массовый рейд — двух пленников). " +
                "Тестовые рейды игнорируют переговоры/таймеры; VIP pod — опытная реализация, " +
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
