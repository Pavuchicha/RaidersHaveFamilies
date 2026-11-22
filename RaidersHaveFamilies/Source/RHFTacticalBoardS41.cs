// RHF S4.1-A — persistent mission blackboard. This does NOT issue Pawn jobs.
using System;
using System.Collections.Generic;
using System.Text;
using LudeonTK;
using Verse.AI.Group;
using RimWorld;
using Verse;

namespace RaidersHaveFamilies
{
    internal enum RHFRoleS41
    {
        None = 0, Breach = 1, Advance = 2, Screen = 3,
        Escort = 4, FollowEscort = 5, Hold = 6, Withdraw = 7,
        SuppressTurret = 8, Engineer = 9, BoardPod = 10, PodAssembly = 11,
        OffenseStrike = 12, OffenseFlank = 13, OffenseHunt = 14,
        OffenseCover = 15, OffenseRally = 16, PodSupply = 17
    }

    // Individual assignment is mission-owned and saveable, NOT a second job writer.
    public sealed class RHFOrderS41 : IExposable
    {
        public Pawn Member;
        public int Role;
        public Thing Objective;
        public IntVec3 Cell = IntVec3.Invalid;
        public int ChangedTick;
        public RHFOrderS41() { }
        internal RHFOrderS41(Pawn pawn) { Member = pawn; }
        public void ExposeData()
        {
            Scribe_References.Look(ref Member, "member");
            Scribe_Values.Look(ref Role, "role", 0);
            Scribe_References.Look(ref Objective, "objective");
            Scribe_Values.Look(ref Cell, "cell", IntVec3.Invalid);
            Scribe_Values.Look(ref ChangedTick, "changedTick", 0);
        }
    }

    public sealed class RHFTacticalBoardS41 : IExposable
    {
        public int Version = 1;
        public int ObservedPhase = -1;
        public int LastPlanTick;
        public int LastProgressTick;
        public int NextReplanTick;
        public int LastObstacleHP;
        public int LastThreatHP;
        public int Replans;
        public IntVec3 LeadProgressCell = IntVec3.Invalid;
        public int BestLeadDistance = int.MaxValue;
        public Pawn RescueTarget;
        public Pawn Breacher;
        public Thing Barrier;
        public Thing Threat;
        public Thing PodWorkTargetS41;
        public int PodWorkStageS41 = -1;
        public Pawn PodEngineerS41;
        public IntVec3 LastKnownCell = IntVec3.Invalid;
        public IntVec3 WithdrawalCell = IntVec3.Invalid;
        public string Decision = "not planned";
        public List<RHFOrderS41> Orders = new List<RHFOrderS41>();

        public RHFTacticalBoardS41() { }

        internal RHFOrderS41 For(Pawn pawn, bool create = true)
        {
            if (pawn == null) return null;
            if (Orders == null) Orders = new List<RHFOrderS41>();
            for (int i = 0; i < Orders.Count; i++)
                if (Orders[i]?.Member == pawn) return Orders[i];
            if (!create) return null;
            if (Orders.Count >= 128) return null; // hard cap for malicious/modded raids
            var order = new RHFOrderS41(pawn);
            Orders.Add(order);
            return order;
        }

        internal void Assign(Pawn pawn, RHFRoleS41 role, Thing objective,
            IntVec3 cell, int now)
        {
            RHFOrderS41 order = For(pawn);
            if (order == null) return;
            if (order.Role == (int)role && order.Objective == objective &&
                order.Cell == cell) return;
            order.Role = (int)role;
            order.Objective = objective;
            order.Cell = cell;
            order.ChangedTick = now;
        }

        internal void Prune(Map map)
        {
            if (Orders == null) { Orders = new List<RHFOrderS41>(); return; }
            for (int i = Orders.Count - 1; i >= 0; i--)
                if (Orders[i]?.Member == null || Orders[i].Member.Dead ||
                    Orders[i].Member.MapHeld != map)
                    Orders.RemoveAt(i);
            if (Breacher != null && (Breacher.Dead || Breacher.Downed ||
                Breacher.InMentalState || Breacher.Map != map ||
                Breacher.health?.capacities==null ||
                !Breacher.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation)))
                Breacher = null;
            if (Barrier != null && (Barrier.Destroyed || Barrier.Map != map))
            { Barrier = null; Breacher = null; }
            if (Threat != null && (Threat.Destroyed || Threat.Map != map))
                Threat = null;
            if (PodEngineerS41 != null &&
                (PodEngineerS41.Dead || PodEngineerS41.Downed ||
                 PodEngineerS41.InMentalState || PodEngineerS41.Map != map ||
                 PodEngineerS41.health?.capacities==null ||
                 !PodEngineerS41.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation)))
                PodEngineerS41 = null;
            if (PodWorkTargetS41 != null &&
                (PodWorkTargetS41.Destroyed || PodWorkTargetS41.Map != map))
                PodWorkTargetS41 = null;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref Version, "version", 1);
            Scribe_Values.Look(ref ObservedPhase, "observedPhase", -1);
            Scribe_Values.Look(ref LastPlanTick, "lastPlanTick", 0);
            Scribe_Values.Look(ref LastProgressTick, "lastProgressTick", 0);
            Scribe_Values.Look(ref NextReplanTick, "nextReplanTick", 0);
            Scribe_Values.Look(ref LastObstacleHP, "lastObstacleHP", 0);
            Scribe_Values.Look(ref LastThreatHP, "lastThreatHPS41AD", 0);
            Scribe_Values.Look(ref Replans, "replans", 0);
            Scribe_Values.Look(ref LeadProgressCell, "leadProgressCell", IntVec3.Invalid);
            Scribe_Values.Look(ref BestLeadDistance, "bestLeadDistance", int.MaxValue);
            Scribe_References.Look(ref RescueTarget, "rescueTarget");
            Scribe_References.Look(ref Breacher, "breacher");
            Scribe_References.Look(ref Barrier, "barrier");
            Scribe_References.Look(ref Threat, "threatS41AD");
            Scribe_References.Look(ref PodWorkTargetS41, "podWorkTargetS41AE");
            Scribe_Values.Look(ref PodWorkStageS41, "podWorkStageS41AE", -1);
            Scribe_References.Look(ref PodEngineerS41, "podEngineerS41AE");
            Scribe_Values.Look(ref LastKnownCell, "lastKnownCell", IntVec3.Invalid);
            Scribe_Values.Look(ref WithdrawalCell, "withdrawalCell", IntVec3.Invalid);
            Scribe_Values.Look(ref Decision, "decision", "not planned");
            Scribe_Collections.Look(ref Orders, "orders", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && Orders == null)
                Orders = new List<RHFOrderS41>();
        }
    }

    // Read-only diagnostic, run on demand; no per-tick allocations.
    public static class RHFS41Diagnostics
    {
        [DebugAction("RHF - ТЕСТЫ | ДИАГНОСТИКА",
            "36 — S4.1 Blackboard, роли, приказы",
            actionType=DebugActionType.Action,
            allowedGameStates=AllowedGameStates.PlayingOnMap)]
        public static void Show()
        {
            Map map=Find.CurrentMap;
            var ops=RHFMissionIntegrityV130.Operations(map);
            var sb=new StringBuilder(2400);
            sb.AppendLine("RHF S4.1-A/B review: strategy vs actual pawn job");
            if(ops==null || ops.Count==0)sb.AppendLine("No active RHF operations");
            else for(int i=0;i<ops.Count && i<4;i++)
            {
                RHFOperation op=ops[i];
                if(op==null)continue;
                RHFTacticalBoardS41 board=op.BoardS41;
                sb.AppendLine("Op "+i+" type="+op.Type+" phase="+op.Phase+
                    " doctrine="+op.Doctrine+" prisoners="+(op.Targets?.Count??0)+
                    " freed="+(op.Freed?.Count??0)+" escorted="+op.EscapedWithEscort);
                if(board==null){sb.AppendLine("Board MISSING");continue;}
                sb.AppendLine("Target="+(board.RescueTarget?.LabelShortCap??"none")+
                    " barrier="+(board.Barrier?.LabelShortCap??"none")+
                    " HP="+board.LastObstacleHP+
                    " threat="+(board.Threat?.LabelShortCap??"none")+
                    " breacher="+(board.Breacher?.LabelShortCap??"none")+
                    " replan="+board.NextReplanTick+
                    " progress="+board.LastProgressTick);
                sb.AppendLine("Decision: "+board.Decision);
                int shown=0;
                foreach(RHFOrderS41 order in board.Orders)
                {
                    Pawn p=order?.Member;
                    if(p?.Map!=map || shown++>=12)continue;
                    sb.AppendLine("  "+p.LabelShortCap+" role="+(RHFRoleS41)order.Role+
                        " cell="+order.Cell+
                        " job="+(p.CurJobDef?.defName??"none")+
                        " duty="+(p.mindState?.duty?.def?.defName??"none")+
                        " lord="+(p.GetLord()?.LordJob?.GetType().Name??"none"));
                }
            }
            Find.WindowStack.Add(new Dialog_MessageBox(sb.ToString()));
        }
    }
}
