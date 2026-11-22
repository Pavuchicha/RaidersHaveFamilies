// RHF v0.12.3: rescue mission ownership and real objective tracking.
// Additive Harmony corrections for the v0.12.2 operation controller.
// No changes to vanilla pawns/ticks without an active RHF rescue operation.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace RaidersHaveFamilies
{
    internal sealed class RHFRescueLiveState
    {
        public int LastWarningTick;
        public int LastActiveTick;
        public int BlockedTicks;
        public string LastProblem = "ещё не обследован маршрут";
        public readonly Dictionary<int, int> PreviousDistance = new Dictionary<int, int>();
        public readonly Dictionary<int, int> LastMovementTick = new Dictionary<int, int>();
    }

    // All scans are event-scoped (one raid deployment) or scheduled at >=90 ticks
    // only while at least one RHF mission exists. No map-wide scanning when idle.
    internal static class RHFRescueControlV123
    {
        private const int MaxSquad = 96;
        private const int MaxTargets = 24;
        private const int MaxOrdersPerDecision = 14;
        private const int DecisionInterval = 90;
        private static readonly FieldInfo MapField = AccessTools.Field(typeof(MapComponent), "map");
        private static readonly FieldInfo OperationsField = AccessTools.Field(typeof(RHFOperationController), "operations");
        private static readonly MethodInfo FreeTargetMethod = AccessTools.Method(typeof(RHFOperationController), "FreeTarget");
        private static readonly MethodInfo FindBarrierMethod = AccessTools.Method(typeof(RHFOperationController), "FindBarrier");
        private static readonly ConditionalWeakTable<RHFOperation, RHFRescueLiveState> LiveStates =
            new ConditionalWeakTable<RHFOperation, RHFRescueLiveState>();

        internal static Map MapFor(RHFOperationController controller)
        {
            return (MapField?.GetValue(controller) as Map) ?? Find.CurrentMap;
        }

        private static List<RHFOperation> Operations(RHFOperationController controller)
        {
            return OperationsField?.GetValue(controller) as List<RHFOperation>;
        }

        private static bool Rescuing(RHFOperation op)
        {
            return op != null && op.Deployed && !op.Finished &&
                op.Type == (int)RHFOperationType.Rescue && op.Phase <= 1 &&
                op.Targets != null && op.Targets.Count > 0 &&
                string.IsNullOrEmpty(op.CaseKeyV10);
        }

        private static Pawn ActiveCaptive(RHFOperation op, Map map)
        {
            Pawn v = op.Vip;
            if (EligibleCaptive(op, v, map)) return v;
            for (int i = 0; i < op.Targets.Count && i < MaxTargets; i++)
            {
                Pawn p = op.Targets[i];
                if (EligibleCaptive(op, p, map)) return p;
            }
            return null;
        }

        private static bool EligibleCaptive(RHFOperation op, Pawn prisoner, Map map)
        {
            return prisoner != null && !prisoner.Dead && prisoner.Spawned &&
                prisoner.Map == map && (prisoner.IsPrisonerOfColony || prisoner.IsSlaveOfColony) &&
                !op.Freed.Contains(prisoner);
        }

        private static bool HealthyRescuer(Pawn pawn, Map map)
        {
            return pawn != null && pawn.Spawned && pawn.Map == map &&
                !pawn.Dead && !pawn.Downed && pawn.jobs != null;
        }

        // Called AFTER the vanilla RaidEnemy worker has finished spawning troops
        // and attaching a vanilla assault Lord. The older SpawnSetup hook can
        // miss some modded/transport arrivals: reconcile only newly spawned ids.
        internal static HashSet<int> Snapshot(RHFOperationController controller, RHFOperation op)
        {
            if (op == null || op.Type != (int)RHFOperationType.Rescue) return null;
            Map map = MapFor(controller);
            if (map == null) return null;
            var ids = new HashSet<int>();
            foreach (Pawn p in map.mapPawns.AllPawnsSpawned)
                if (p != null) ids.Add(p.thingIDNumber);
            return ids;
        }

        internal static void OnDeployed(RHFOperationController controller, RHFOperation op,
            HashSet<int> previous, bool incidentSucceeded)
        {
            if (!incidentSucceeded || op == null || op.Type != (int)RHFOperationType.Rescue) return;
            Map map = MapFor(controller);
            if (map == null) return;
            // Fallback ONLY if the original SpawnSetup hook did not register a
            // rescue wing. Never steal unrelated existing pawns of that faction.
            if (previous != null && (op.Squad.Count == 0 ||
                (op.Parties.Count == 1 && op.Parties[0].Role == (int)RHFStrikeRole.Rescue)))
            {
                for (int i = 0; i < op.Parties.Count && op.Squad.Count < MaxSquad; i++)
                {
                    RHFStrikeParty party = op.Parties[i];
                    if (party == null || !party.Succeeded ||
                        party.Role != (int)RHFStrikeRole.Rescue) continue;
                    foreach (Pawn p in map.mapPawns.AllPawnsSpawned)
                    {
                        if (op.Squad.Count >= MaxSquad) break;
                        if (p == null || !p.Spawned || p.Faction != party.Faction ||
                            previous.Contains(p.thingIDNumber) || !p.RaceProps.Humanlike ||
                            op.Squad.Contains(p) || op.Diversion.Contains(p)) continue;
                        op.Squad.Add(p);
                        party.Members++;
                        RHFTactical.Register(op, party, p);
                    }
                }
            }
            Pawn target = ActiveCaptive(op, map);
            if (target == null)
            {
                Log.Warning("[RHF][RescueV123] Deploy succeeded but no valid captive on the map; " +
                    "cannot declare mission success. Check prisoner status/targets.");
                return;
            }
            int taken = 0;
            for (int i = 0; i < op.Squad.Count && i < MaxSquad; i++)
            {
                Pawn rescuer = op.Squad[i];
                if (!HealthyRescuer(rescuer, map)) continue;
                TakeMissionOwnership(rescuer);
                if (taken < 8 && rescuer.CanReach(target, PathEndMode.Touch, Danger.Deadly))
                {
                    GiveOrder(rescuer, JobDefOf.Goto, target);
                    taken++;
                }
            }
            Log.Message("[RHF][RescueV123] Mission claimed immediately after deployment: " +
                "rescuers=" + op.Squad.Count + ", target=" + target.LabelShortCap +
                ", early orders=" + taken + ".");
        }

        // A vanilla raid Lord retains its own assault/flee behavior; rescue
        // troops must NOT remain part of that independent decision maker.
        private static void TakeMissionOwnership(Pawn pawn)
        {
            Lord lord = pawn.GetLord();
            if (lord != null)
                lord.Notify_PawnLost(pawn, PawnLostCondition.LeftVoluntarily);
        }

        internal static void RefreshOwnership(RHFOperationController controller)
        {
            int now = Find.TickManager.TicksGame;
            if (now % DecisionInterval != 0) return;
            List<RHFOperation> ops = Operations(controller);
            if (ops == null || ops.Count == 0) return;
            Map map = MapFor(controller);
            if (map == null) return;
            for (int k = 0; k < ops.Count && k < 4; k++)
            {
                RHFOperation op = ops[k];
                if (!Rescuing(op)) continue;
                for (int i = 0; i < op.Squad.Count && i < MaxSquad; i++)
                    if (HealthyRescuer(op.Squad[i], map)) TakeMissionOwnership(op.Squad[i]);
            }
        }

        internal static void Infiltrate(RHFOperationController controller, RHFOperation op, int now)
        {
            if (!Rescuing(op)) return;
            Map map = MapFor(controller);
            if (map == null) return;
            RHFRescueLiveState state = LiveStates.GetValue(op, _ => new RHFRescueLiveState());
            Pawn captive = ActiveCaptive(op, map);
            if (captive == null)
            {
                if (op.Freed.Count != 0)
                {
                    op.Phase = 2;
                    op.PhaseStartTick = now;
                    state.LastProblem = "пленники освобождены; эвакуация";
                }
                else
                {
                    state.LastProblem = "цель недоступна или уже не является пленником";
                    op.Finished = true;
                    Log.Warning("[RHF][RescueV123] Mission aborted WITHOUT extraction: " +
                        state.LastProblem + ".");
                }
                return;
            }

            int alive = 0;
            int jobCount = 0;
            int progressing = 0;
            int blocked = 0;
            int orders = 0;
            // Controlled workers retain their current orders if still correct.
            // Restarting a melee job every 90 ticks prevents ever hitting a door.
            for (int i = 0; i < op.Squad.Count && i < MaxSquad; i++)
            {
                Pawn rescuer = op.Squad[i];
                if (!HealthyRescuer(rescuer, map)) continue;
                alive++;
                TakeMissionOwnership(rescuer);
                int distance = (rescuer.Position - captive.Position).LengthHorizontalSquared;
                int previous;
                if (state.PreviousDistance.TryGetValue(rescuer.thingIDNumber, out previous))
                {
                    if (distance < previous)
                    {
                        progressing++;
                        state.LastMovementTick[rescuer.thingIDNumber] = now;
                    }
                }
                else state.LastMovementTick[rescuer.thingIDNumber] = now;
                state.PreviousDistance[rescuer.thingIDNumber] = distance;
                int lastMove = now;
                state.LastMovementTick.TryGetValue(rescuer.thingIDNumber, out lastMove);
                bool routeStalled = now - lastMove >= 900;

                if (distance <= 5 && rescuer.CanReach(captive, PathEndMode.Touch, Danger.Deadly))
                {
                    if (FreeTargetMethod != null)
                    {
                        FreeTargetMethod.Invoke(controller, new object[] { op, captive });
                        state.LastActiveTick = now;
                        state.LastProblem = "пленник достигнут и освобождён";
                        return;
                    }
                    state.LastProblem = "не найден метод освобождения пленника";
                    break;
                }
                if (orders >= MaxOrdersPerDecision) continue;
                Job current = rescuer.jobs.curJob;
                // A reachability result is only a planning estimate. If a Goto
                // job has made no measurable progress for 900 ticks, stop trusting
                // that estimate and try to breach the prison-room door directly.
                if (!routeStalled && rescuer.CanReach(captive, PathEndMode.Touch, Danger.Deadly))
                {
                    // Do not overwrite a valid approach or attack with Wait/ExitMap.
                    bool approaching = current != null && current.def == JobDefOf.Goto &&
                        (current.targetA.Thing == captive ||
                         (current.targetA.Cell - captive.Position).LengthHorizontalSquared <= 5);
                    if (!approaching)
                    {
                        GiveOrder(rescuer, JobDefOf.Goto, captive);
                        orders++;
                    }
                    jobCount++;
                    continue;
                }

                // FindBarrier is the existing bounded 33x33 detention-perimeter
                // search: it strongly prefers accessible doors to solid walls.
                Thing barrier = null;
                try
                {
                    if (FindBarrierMethod != null)
                        barrier = FindBarrierMethod.Invoke(controller, new object[] { captive, rescuer }) as Thing;
                }
                catch (Exception error)
                {
                    state.LastProblem = "ошибка поиска двери: " + error.GetBaseException().Message;
                }
                if (barrier != null && !barrier.Destroyed)
                {
                    bool attacking = current != null && current.def == JobDefOf.AttackMelee &&
                        current.targetA.Thing == barrier;
                    if (!attacking)
                    {
                        GiveOrder(rescuer, JobDefOf.AttackMelee, barrier);
                        orders++;
                    }
                    if (routeStalled) state.LastProblem = "маршрут к пленнику застрял; переход к пролому";
                    jobCount++;
                }
                else
                {
                    blocked++;
                    // If the estimate was optimistic but there is no sensible
                    // breaching target, retain a valid movement job; otherwise
                    // await a route change and report the exact blockage.
                    if (routeStalled && rescuer.jobs.curJob?.def == JobDefOf.Goto)
                        state.LastProblem = "переход к пленнику заблокирован, дверь не обнаружена";
                }
            }
            if (alive == 0)
            {
                op.Finished = true;
                state.LastProblem = "нет боеспособных спасателей";
                Log.Warning("[RHF][RescueV123] Mission FAILED: no active rescue troops, captive=" +
                    captive.LabelShortCap + ".");
                return;
            }
            if (progressing > 0)
            {
                op.LastProgressTick = now;
                state.LastActiveTick = now;
                state.BlockedTicks = 0;
                state.LastProblem = "продвижение к тюрьме";
            }
            else if (jobCount > 0)
            {
                state.LastProblem = "бойцы исполняют приказ подхода/пролома";
            }
            else
            {
                state.BlockedTicks += DecisionInterval;
                state.LastProblem = "не найден доступный путь или разрушаемая дверь/стена";
            }
            op.LastOrderTick = now;
            if (state.BlockedTicks >= 1800 && now - state.LastWarningTick >= 1800)
            {
                state.LastWarningTick = now;
                Log.Warning("[RHF][RescueV123] Stalled rescue: " + captive.LabelShortCap +
                    ", " + state.LastProblem + ", rescuers=" + alive +
                    ", blocked=" + blocked + ". Mission NOT reported as successful.");
            }
            // Intentional: no time-based automatic retreat from Phase 1 merely
            // because it has taken too long to open the prison. An explicit
            // objective outcome (rescue/death/lost troops) is required.
        }

        // The vanilla raid Lord may order an unassigned/idle fighter to escape
        // before our next mission cycle. Do not let a *healthy active rescuer*
        // disappear while the named hostage is still in captivity. This only
        // runs on actual ExitMap calls, and never interferes with ordinary raids,
        // escorts after liberation, or medically forced withdrawal.
        internal static bool PermitExit(Pawn pawn)
        {
            if (pawn == null || !pawn.Spawned || pawn.Map == null || pawn.Dead ||
                pawn.Downed || pawn.Faction == null) return true;
            Map map = pawn.Map;
            var ctrl = map.GetComponent<RHFOperationController>();
            var ops = ctrl != null ? Operations(ctrl) : null;
            if (ops == null || ops.Count == 0) return true;
            for (int k = 0; k < ops.Count && k < 4; k++)
            {
                RHFOperation op = ops[k];
                if (!Rescuing(op) || !op.Squad.Contains(pawn)) continue;
                Pawn prisoner = ActiveCaptive(op, map);
                if (prisoner == null) return true;
                // At critical health a retreat is a genuine loss, not a fake
                // successful extraction. Do not trap a dying pawn on-map.
                if (pawn.health?.summaryHealth != null &&
                    pawn.health.summaryHealth.SummaryHealthPercent < 0.20f)
                    return true;
                TakeMissionOwnership(pawn);
                if (pawn.CanReach(prisoner, PathEndMode.Touch, Danger.Deadly))
                    GiveOrder(pawn, JobDefOf.Goto, prisoner);
                else
                {
                    Thing obstacle = null;
                    if (FindBarrierMethod != null)
                        try { obstacle = FindBarrierMethod.Invoke(ctrl, new object[] { prisoner, pawn }) as Thing; }
                        catch (Exception e)
                        {
                            Log.Warning("[RHF][RescueV123] Exit redirection barrier error: " +
                                e.GetBaseException().Message);
                        }
                    if (obstacle != null && !obstacle.Destroyed)
                        GiveOrder(pawn, JobDefOf.AttackMelee, obstacle);
                }
                RHFRescueLiveState state = LiveStates.GetValue(op, _ => new RHFRescueLiveState());
                state.LastProblem = "отменён самовольный выход бойца до спасения пленника";
                int now = Find.TickManager.TicksGame;
                if (now - state.LastWarningTick > 1800)
                {
                    state.LastWarningTick = now;
                    Log.Warning("[RHF][RescueV123] Blocked premature vanilla raid withdrawal: " +
                        pawn.LabelShortCap + ", objective=" + prisoner.LabelShortCap + ".");
                }
                return false;
            }
            return true;
        }

        private static void GiveOrder(Pawn pawn, JobDef def, LocalTargetInfo target)
        {
            if (pawn?.jobs == null || def == null) return;
            Job job = JobMaker.MakeJob(def, target);
            pawn.jobs.TryTakeOrderedJob(job);
        }

        internal static string Audit(Map map)
        {
            if (map == null) return "Нет текущей карты.";
            List<RHFOperation> ops = Operations(map.GetComponent<RHFOperationController>());
            if (ops == null || ops.Count == 0) return "Нет активных операций RHF.";
            var sb = new StringBuilder();
            sb.AppendLine("RHF 0.12.3: живое состояние спасательных миссий");
            int printed = 0;
            foreach (RHFOperation op in ops)
            {
                if (op == null || op.Type != (int)RHFOperationType.Rescue) continue;
                if (++printed > 4) break;
                var state = LiveStates.GetValue(op, _ => new RHFRescueLiveState());
                Pawn target = ActiveCaptive(op, map);
                int onMap = 0;
                int lordAttached = 0;
                for (int i = 0; i < op.Squad.Count && i < MaxSquad; i++)
                {
                    Pawn fighter = op.Squad[i];
                    if (!HealthyRescuer(fighter, map)) continue;
                    onMap++;
                    if (fighter.GetLord() != null) lordAttached++;
                }
                sb.AppendLine("Операция " + printed + ": фаза=" + op.Phase +
                    ", жива=" + (!op.Finished) + ", цель=" +
                    (target?.LabelShortCap ?? "не определена") +
                    ", координаты=" + (target != null ? target.Position.ToString() : "-") +
                    ", назначено=" + op.Squad.Count + ", на карте=" + onMap +
                    ", оставшихся в обычном Lord=" + lordAttached +
                    ", выведено=" + op.EscapedWithEscort);
                sb.AppendLine("Состояние: " + state.LastProblem +
                    "; последний прогресс, тик=" + op.LastProgressTick);
                int shown = 0;
                foreach (Pawn rescuer in op.Squad)
                {
                    if (!HealthyRescuer(rescuer, map)) continue;
                    sb.AppendLine("  " + rescuer.LabelShortCap + " — " +
                        (rescuer.jobs.curJob?.def?.defName ?? "нет задачи") +
                        ", клетка=" + rescuer.Position);
                    if (++shown == 6) break;
                }
            }
            return sb.ToString();
        }
    }

    // Legacy hook disabled; mission controller performs bounded post-spawn capture.
    internal static class RHFRescueDeploymentPatchV123
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(RHFOperationController), "Deploy");
        }
        private static void Prefix(RHFOperationController __instance, RHFOperation op,
            out HashSet<int> __state)
        {
            __state = RHFRescueControlV123.Snapshot(__instance, op);
        }
        private static void Postfix(RHFOperationController __instance, RHFOperation op,
            bool __result, HashSet<int> __state)
        {
            RHFRescueControlV123.OnDeployed(__instance, op, __state, __result);
        }
    }

    // Legacy hook disabled; direct mission dispatch owns this tick.
    internal static class RHFRescueTickPatchV123
    {
        private static void Prefix(RHFOperationController __instance)
        {
            RHFRescueControlV123.RefreshOwnership(__instance);
        }
    }

    // Legacy hook disabled; does not intercept the operation's Infiltrate method.
    internal static class RHFRescueInfiltratePatchV123
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(RHFOperationController), "Infiltrate");
        }
        private static bool Prefix(RHFOperationController __instance, RHFOperation op, int now)
        {
            if (op == null || op.Type != (int)RHFOperationType.Rescue ||
                !string.IsNullOrEmpty(op.CaseKeyV10)) return true;
            RHFRescueControlV123.Infiltrate(__instance, op, now);
            return false;
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.ExitMap))]
    internal static class RHFRescueExitPatchV123
    {
        private static bool Prefix(Pawn __instance)
        {
            // S4.1 rescue uses the same scoped exit permission as RHFExitEvent.
            // Do not wake the obsolete V123 external job writer.
            if (RHFRescuePlannerS41.Enabled)
                return RHFRescuePlannerS41.GuardExit(__instance);
            return RHFRescueControlV123.PermitExit(__instance);
        }
    }

    public static class RHFRescueMissionDiagnosticsV123
    {
        [DebugAction("RHF - ТЕСТЫ | ДИАГНОСТИКА", "28 — Причина остановки спасателей",
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void Show()
        {
            Find.WindowStack.Add(new Dialog_MessageBox(
                RHFRescueControlV123.Audit(Find.CurrentMap)));
        }
    }
}
