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
        private const string Category = "Raiders Have Families";
        private const int MaxDebugTargets = 24;
        private const int DelayTicks = 150;

        // All commands are explicitly developer mode only and are deliberately
        // split into independent menu entries for reproducible testing.
        [DebugAction(Category, "00 - Prepare test prisoner", actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void PreparePrisoner() { Prepare(); }

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

        [DebugAction(Category, "11 - Show test setup and limitations", actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void Explain() { ExplainCurrentMap(); }

        private static void Prepare()
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
                IntVec3 place = IntVec3.Invalid;
                for (int radius = 0; radius <= 7 && place == IntVec3.Invalid; radius++)
                    for (int dx = -radius; dx <= radius && place == IntVec3.Invalid; dx++)
                        for (int dz = -radius; dz <= radius; dz++)
                        {
                            IntVec3 c = new IntVec3(map.Center.x + dx, 0, map.Center.z + dz);
                            if (c.InBounds(map) && c.Standable(map)) { place = c; break; }
                        }
                if (place == IntVec3.Invalid)
                { Notify(map, "Не нашлось подходящей свободной клетки возле центра карты."); return; }
                GenSpawn.Spawn(test, place, map);
                test.guest.SetGuestStatus(Faction.OfPlayer, GuestStatus.Prisoner);
                Current.Game?.GetComponent<RHFGameLedger>()?.TrackHostage(test);
                Notify(map, "Создан тестовый пленник фракции " + faction.Name +
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
                    "Сначала запусти '00 - Prepare test prisoner' или захвати рейдера.");
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
