// RHF v0.13.0 — mission-scoped identification and single operational raid letter.
// Faction relations are not edited. Protections apply ONLY to named members
// and hostages of a currently active RHF rescue on their actual map.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace RaidersHaveFamilies
{
    internal static class RHFMissionIntegrityV130
    {
        private static readonly FieldInfo OperationsField =
            AccessTools.Field(typeof(RHFOperationController), "operations");
        private const int MaxMissions = 4;
        // The native raid incident must produce exactly ONE letter per RHF
        // operation. Reuse that actual arrival letter (including its true raid
        // jump target), instead of creating another letter after deployment.
        // The dispatch context only exists for this explicit RHF incident; all
        // unrelated vanilla/storyteller events retain their native messages.
        private sealed class RHFNativeLetterState { internal bool Sent; }
        private static readonly ConditionalWeakTable<RHFOperation, RHFNativeLetterState>
            NativeLetterStates = new ConditionalWeakTable<RHFOperation, RHFNativeLetterState>();
        [ThreadStatic] private static RHFOperation activeRaidLetterOperation;
        [ThreadStatic] private static int receiveLetterNesting;

        internal static RHFOperation ActiveRaidLetterOperation => activeRaidLetterOperation;

        internal static bool ExecuteRaidWithoutVanillaLetter(IncidentParms parms, RHFOperation op)
        {
            // Retained method name for compatibility; the NATIVE raid letter
            // now carries the RHF briefing, instead of being deleted and then
            // duplicated by a synthetic custom notice.
            RHFOperation previous = activeRaidLetterOperation;
            activeRaidLetterOperation = op;
            try { return IncidentDefOf.RaidEnemy.Worker.TryExecute(parms); }
            finally { activeRaidLetterOperation = previous; }
        }

        internal static bool AllowOneNativeLetter(out bool entered)
        {
            entered = false;
            RHFOperation operation = activeRaidLetterOperation;
            if (operation == null) return true;
            // ReceiveLetter(string,...) may forward internally to
            // ReceiveLetter(Letter). Both must be allowed for the FIRST letter.
            if (receiveLetterNesting > 0)
            { receiveLetterNesting++; entered = true; return true; }
            RHFNativeLetterState state = NativeLetterStates.GetValue(
                operation, _ => new RHFNativeLetterState());
            if (state.Sent) return false; // second coalition/auxiliary incident
            state.Sent = true;
            receiveLetterNesting++;
            entered = true;
            return true;
        }

        internal static void NativeLetterReturned(bool entered)
        {
            if (entered && receiveLetterNesting > 0) receiveLetterNesting--;
        }

        internal static List<RHFOperation> Operations(Map map)
        {
            RHFOperationController controller = map?.GetComponent<RHFOperationController>();
            return controller == null ? null :
                OperationsField?.GetValue(controller) as List<RHFOperation>;
        }

        private static bool LiveRescue(RHFOperation op)
        {
            return op != null && !op.Finished && op.Deployed &&
                op.Type == (int)RHFOperationType.Rescue &&
                op.Parties != null && op.Squad != null && op.Targets != null;
        }

        private static bool Soldier(RHFOperation op, Pawn pawn)
        {
            return pawn != null && (op.Squad.Contains(pawn) ||
                (op.Diversion != null && op.Diversion.Contains(pawn)));
        }

        private static bool ProtectedPerson(RHFOperation op, Pawn pawn)
        {
            return pawn != null && (Soldier(op, pawn) ||
                op.Targets.Contains(pawn) ||
                (op.Freed != null && op.Freed.Contains(pawn)));
        }

        // An escort may be a hired soldier of a faction hostile to the sponsor.
        // This mission-specific authorization is intentionally NOT faction-wide.
        internal static bool MayEscort(RHFOperation op, Pawn escort, Pawn captive)
        {
            return op != null && escort != null && captive != null &&
                Soldier(op, escort) &&
                (op.Targets.Contains(captive) ||
                 (op.Freed != null && op.Freed.Contains(captive)));
        }

        internal static bool SameSide(Thing a, Thing b)
        {
            Pawn pa = a as Pawn, pb = b as Pawn;
            Map map = a?.MapHeld ?? b?.MapHeld;
            if (pa == null || pb == null || map == null || pa == pb ||
                a.MapHeld != map || b.MapHeld != map) return false;
            List<RHFOperation> operations = Operations(map);
            if (operations == null) return false;
            for (int i=0;i<operations.Count && i<MaxMissions;i++)
            {
                RHFOperation op = operations[i];
                if (!LiveRescue(op)) continue;
                // At least one actual soldier is required. This cannot make
                // two unrelated colony prisoners friendly to each other.
                if ((Soldier(op,pa) && ProtectedPerson(op,pb)) ||
                    (Soldier(op,pb) && ProtectedPerson(op,pa))) return true;
            }
            return false;
        }

        internal static bool SameSide(Thing target, Faction attackingFaction)
        {
            Pawn pawn = target as Pawn;
            Map map = pawn?.MapHeld;
            if (pawn == null || map == null || attackingFaction == null ||
                attackingFaction.IsPlayer) return false;
            List<RHFOperation> operations = Operations(map);
            if (operations == null) return false;
            for(int i=0;i<operations.Count && i<MaxMissions;i++)
            {
                RHFOperation op = operations[i];
                if (!LiveRescue(op) || !ProtectedPerson(op,pawn)) continue;
                for(int j=0;j<op.Parties.Count && j<5;j++)
                {
                    RHFStrikeParty party = op.Parties[j];
                    if(party != null && party.Faction==attackingFaction && party.Attempted)
                        return true;
                }
            }
            return false;
        }

        internal static string MissionTitle(RHFOperation op)
        {
            if (op.Type == (int)RHFOperationType.Vengeance)
                return "NJR: карательная операция";
            if (op.TryPod && op.Vip != null)
                return "NJR: эвакуация важного пленника";
            if (op.Targets != null && op.Targets.Count > 1)
                return "NJR: массовое освобождение";
            return "NJR: освобождение пленника";
        }

        internal static string MissionBriefing(RHFOperation op)
        {
            var sb = new StringBuilder(600);
            sb.AppendLine("Тип операции: " + RHFMissionProfiles.Label(op) + ".");
            if (op.Type == (int)RHFOperationType.Rescue)
            {
                sb.AppendLine("План: проникнуть в тюрьму, освободить " +
                    (op.Targets?.Count ?? 0) + " пленников и организованно вывести их.");
                if (op.TryPod && op.Vip != null)
                    sb.AppendLine("План VIP: удержать плацдарм, возвести укрепления, " +
                        "собрать пусковую установку и эвакуировать пленника по воздуху. " +
                        "Даже при нехватке дальности прямого полёта фракция заранее " +
                        "подготовила промежуточные площадки для дозаправки — " +
                        "дальнейший маршрут выполняется последовательными перелётами " +
                        "(без отдельных игровых карт площадок). " +
                        "Наземный отход — только при невозможности запуска.");
                else if (op.Targets?.Count > 1)
                    sb.AppendLine("Группы освобождения должны найти каждого пленника; " +
                        "эвакуация одного не означает завершения операции.");
                else
                    sb.AppendLine("Приоритет: один пленник; посторонние здания не являются целью.");
                if (op.CombinedStrike)
                    sb.AppendLine("Участники временно опознают друг друга как союзников " +
                        "только в рамках этой операции; дипломатия фракций не изменяется.");
            }
            else
                sb.AppendLine("Задача: " + RHFMissionProfiles.Label(op) + ".");
            if (op.Parties != null)
                for(int i=0;i<op.Parties.Count && i<5;i++)
                {
                    var p=op.Parties[i];
                    if(p==null)continue;
                    sb.AppendLine("• " + (p.Faction?.Name ?? "неизвестные") + ": " +
                        (p.Role==(int)RHFStrikeRole.Rescue ? "спасательная группа" :
                        p.Role==(int)RHFStrikeRole.Diversion ? "отвлекающий отряд" :
                        "ударный отряд") +
                        (p.Drop ? ", десант" : p.Siege ? ", осада" : ", наземное прибытие") + ".");
                }
            return sb.ToString();
        }

        // Compatibility-only. NEVER call this after RaidEnemy.TryExecute:
        // the native arrival letter is already rewritten by the hooks below.
        internal static void ShowMissionLetter(RHFOperation op, Map map)
        {
            if (map == null || op == null || op.SuccessfulParties <= 0) return;
            IntVec3 site = map.Center;
            for (int i=0;i<op.Squad.Count;i++)
                if(op.Squad[i]?.Spawned == true && op.Squad[i].Map == map)
                {site=op.Squad[i].Position;break;}
            if(site == map.Center && op.Diversion != null)
                for(int i=0;i<op.Diversion.Count;i++)
                    if(op.Diversion[i]?.Spawned == true && op.Diversion[i].Map==map)
                    {site=op.Diversion[i].Position;break;}
            if (site == map.Center && op.Vip?.Spawned == true)
                site=op.Vip.Position; // truthful fallback while drop pods are opening
            Find.LetterStack.ReceiveLetter(MissionTitle(op), MissionBriefing(op),
                LetterDefOf.ThreatBig, new LookTargets(site, map));
        }

        internal static string Audit(Map map)
        {
            var sb = new StringBuilder("NJR: свой–чужой / VIP / связь\n");
            var operations = Operations(map);
            if (operations == null || operations.Count == 0) return sb.Append("Нет операций.").ToString();
            for (int i=0;i<operations.Count && i<4;i++)
            {
                var op=operations[i];
                if(!LiveRescue(op))continue;
                sb.AppendLine("Спасение " + (i+1) + ": фаза " + op.Phase +
                    ", отряд " + op.Squad.Count + ", коалиция " + op.Parties.Count +
                    ", VIP " + (op.Vip?.LabelShortCap ?? "нет") +
                    ", пусковая оплачена " + op.PodBudgetPaid +
                    ", ресурсы доставлены " + op.PodSuppliesIssued +
                    ", строительная стадия " + op.PodConstructionStage +
                    ", ожидаемая стройплощадка " + op.PodCell +
                    ", начало работы " + op.PodConstructionTick +
                    ", запуск " + op.PodLaunchCompleted + ".");
                if(op.Freed != null)
                    for(int j=0;j<op.Freed.Count && j<6;j++)
                    {Pawn c=op.Freed[j]; if(c==null)continue;
                     sb.AppendLine("  освобождён " + c.LabelShortCap +
                        "; фракция=" + (c.Faction?.Name ?? "нет") +
                        "; миссионный командир=" + (c.GetLord()?.LordJob?.GetType().Name ?? "нет") +
                        "; задание=" + (c.jobs?.curJob?.def?.defName ?? "нет") +
                        "; сопровождающий " + (op.Escorts?.Find(e=>e?.Captive==c)?.Escort?.LabelShortCap ?? "нет"));}
            }
            return sb.ToString();
        }
    }

    // Change the actual RaidEnemy arrival letter, not a second letter after
    // the raid has spawned. All other storyteller and mod incidents are untouched.
    [HarmonyPatch(typeof(IncidentWorker_RaidEnemy), "GetLetterLabel")]
    internal static class RHFNativeRaidTitleV133
    {
        [HarmonyPostfix]
        private static void Postfix(ref string __result)
        {
            RHFOperation op = RHFMissionIntegrityV130.ActiveRaidLetterOperation;
            if (op != null) __result = RHFMissionIntegrityV130.MissionTitle(op);
        }
    }

    [HarmonyPatch(typeof(IncidentWorker_RaidEnemy), "GetLetterText")]
    internal static class RHFNativeRaidBriefingV133
    {
        [HarmonyPostfix]
        private static void Postfix(ref string __result)
        {
            RHFOperation op = RHFMissionIntegrityV130.ActiveRaidLetterOperation;
            if (op != null) __result = RHFMissionIntegrityV130.MissionBriefing(op);
        }
    }

    [HarmonyPatch]
    internal static class RHFOnlyOneRaidLetterV130
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (MethodInfo method in AccessTools.GetDeclaredMethods(typeof(LetterStack)))
                if (method.Name == "ReceiveLetter") yield return method;
        }
        [HarmonyPrefix]
        private static bool Prefix(out bool __state)
        { return RHFMissionIntegrityV130.AllowOneNativeLetter(out __state); }

        [HarmonyPostfix]
        private static void Postfix(bool __state)
        { RHFMissionIntegrityV130.NativeLetterReturned(__state); }
    }

    [HarmonyPatch(typeof(GenHostility), nameof(GenHostility.HostileTo),
        new Type[] {typeof(Thing),typeof(Thing)})]
    internal static class RHFCoalitionThingHostilityV130
    {
        private static void Postfix(Thing a, Thing b, ref bool __result)
        {if(__result && RHFMissionIntegrityV130.SameSide(a,b)) __result=false;}
    }

    [HarmonyPatch(typeof(GenHostility), nameof(GenHostility.HostileTo),
        new Type[] {typeof(Thing),typeof(Faction)})]
    internal static class RHFCoalitionFactionHostilityV130
    {
        private static void Postfix(Thing t, Faction fac, ref bool __result)
        {if(__result && RHFMissionIntegrityV130.SameSide(t,fac)) __result=false;}
    }

    public static class RHFMissionAuditV130
    {
        [DebugAction("NJR - ТЕСТЫ | ДИАГНОСТИКА", "32 — Свой-чужой, VIP и этапы операции",
            actionType=DebugActionType.Action, allowedGameStates=AllowedGameStates.PlayingOnMap)]
        public static void Show()
        {Find.WindowStack.Add(new Dialog_MessageBox(RHFMissionIntegrityV130.Audit(Find.CurrentMap)));}
    }
}
