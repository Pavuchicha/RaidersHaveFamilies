// RHF v0.13.0 — mission-scoped identification and single operational raid letter.
// Faction relations are not edited. Protections apply ONLY to named members
// and hostages of a currently active RHF rescue on their actual map.
using System;
using System.Collections.Generic;
using System.Reflection;
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
        private static int suppressedRaidLetterDepth;

        internal static bool SuppressVanillaRaidLetters => suppressedRaidLetterDepth > 0;

        internal static bool ExecuteRaidWithoutVanillaLetter(IncidentParms parms)
        {
            suppressedRaidLetterDepth++;
            try { return IncidentDefOf.RaidEnemy.Worker.TryExecute(parms); }
            finally { suppressedRaidLetterDepth--; }
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
                return "RHF: карательная операция";
            if (op.TryPod && op.Vip != null)
                return "RHF: эвакуация важного пленника";
            if (op.Targets != null && op.Targets.Count > 1)
                return "RHF: массовое освобождение";
            return "RHF: освобождение пленника";
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
                    sb.AppendLine("План VIP: удержать плацдарм, собрать обычную пусковую " +
                        "установку и транспортную капсулу, эвакуировать VIP по воздуху. " +
                        "При невозможности запуска — выход наземной группой.");
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
                    if(p==null || !p.Succeeded)continue;
                    sb.AppendLine("• " + (p.Faction?.Name ?? "неизвестные") + ": " +
                        (p.Role==(int)RHFStrikeRole.Rescue ? "спасательная группа" :
                        p.Role==(int)RHFStrikeRole.Diversion ? "отвлекающий отряд" :
                        "ударный отряд") +
                        (p.Drop ? ", десант" : p.Siege ? ", осада" : ", наземное прибытие") + ".");
                }
            return sb.ToString();
        }

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
            var sb = new StringBuilder("RHF v0.13.0: свой–чужой / VIP / связь\n");
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

    [HarmonyPatch]
    internal static class RHFOnlyOneRaidLetterV130
    {
        // RimWorld may use either a Letter object or a string overload.
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach(MethodInfo method in AccessTools.GetDeclaredMethods(typeof(LetterStack)))
                if(method.Name=="ReceiveLetter") yield return method;
        }
        private static bool Prefix() => !RHFMissionIntegrityV130.SuppressVanillaRaidLetters;
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
        [DebugAction("RHF - ТЕСТЫ | ДИАГНОСТИКА", "32 — Свой-чужой, VIP и этапы операции",
            actionType=DebugActionType.Action, allowedGameStates=AllowedGameStates.PlayingOnMap)]
        public static void Show()
        {Find.WindowStack.Add(new Dialog_MessageBox(RHFMissionIntegrityV130.Audit(Find.CurrentMap)));}
    }
}
