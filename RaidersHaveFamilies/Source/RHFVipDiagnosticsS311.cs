// RHF S3.1.1 - live, bounded state diagnostics. No mutation.
using System.Text;
using LudeonTK;
using Verse;
using RimWorld;

namespace RaidersHaveFamilies
{
    public static class RHFVipDiagnosticsS311
    {
        [DebugAction("RHF - ТЕСТЫ | ДИАГНОСТИКА",
            "34 — S3.1.1 реальная стройка и загрузка VIP",
            actionType=DebugActionType.Action,
            allowedGameStates=AllowedGameStates.PlayingOnMap)]
        public static void Show()
        {
            Map map = Find.CurrentMap;
            if (map == null) return;
            var ops = RHFMissionIntegrityV130.Operations(map);
            var sb = new StringBuilder("RHF S3.1.1 — physical cargo / native work / boarding\n");
            if (ops == null || ops.Count == 0) sb.AppendLine("No active RHF operations");
            else for (int i = 0; i < ops.Count && i < 4; i++)
            {
                RHFOperation op = ops[i];
                if (op == null) continue;
                sb.AppendLine("#" + (i + 1) + " mission=" + op.Type +
                    " phase=" + op.Phase + " air=" + op.TryPod +
                    " funded=" + op.PodBudgetPaid + " price=" + op.PodSupplyCost);
                sb.AppendLine("  native=" + op.PodNativeS311 +
                    " issued=" + op.PodSuppliesIssued +
                    " owner=" + (op.PodCargoOwnerFactionS311?.Name ?? "none") +
                    " stage=" + op.PodConstructionStage +
                    " startedAt=" + op.PodStageStartedTickS311);
                sb.AppendLine("  launcher=" + op.PodLauncherCell +
                    " pod=" + op.PodCell + " launchBuild=" +
                    (op.PodLauncherThing?.def?.defName ?? "none") +
                    " podBuild=" + (op.PodVehicleThing?.def?.defName ?? "none") +
                    " ready=" + op.PodReady + " realLaunch=" + op.PodLaunchCompleted);
                sb.AppendLine("  steel=" + Stack(op.PodSteel) + "+" + Stack(op.PodSteelExtra) +
                    " components=" + Stack(op.PodComponents) +
                    " fuelStacks=" + (op.PodFuelStacksS311?.Count ?? 0));
                Pawn vip = op.Vip;
                sb.AppendLine("  VIP=" + (vip?.LabelShortCap ?? "none") +
                    " spawned=" + (vip?.Spawned ?? false) +
                    " holder=" + (vip?.holdingOwner?.GetType().Name ?? "none"));
                if (op.Squad == null) continue;
                int shown = 0;
                foreach (Pawn pawn in op.Squad)
                {
                    if (pawn == null || !pawn.Spawned || pawn.Map != map) continue;
                    if (shown++ >= 8) break;
                    sb.AppendLine("  " + pawn.LabelShortCap + " faction=" +
                        (pawn.Faction?.Name ?? "none") +
                        " job=" + (pawn.jobs?.curJob?.def?.defName ?? "idle") +
                        " pos=" + pawn.Position);
                }
            }
            Find.WindowStack.Add(new Dialog_MessageBox(sb.ToString()));
        }

        private static string Stack(Thing item)
        {
            return item == null ? "missing" : item.Destroyed ? "destroyed" :
                item.stackCount + "@" + item.PositionHeld;
        }
    }
}
