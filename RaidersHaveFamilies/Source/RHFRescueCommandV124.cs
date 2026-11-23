// RHF v0.12.4 - persistent rescue command, explicit access planning and cohesive extraction.
// These patches supersede the experimental 0.12.3 controller where necessary.
// All work is event-scoped or limited to active RHF missions at most once every 90 ticks.
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
    // A rescue is an actual Lord mission, not unaffiliated enemy pawns with Goto jobs.
    // A one-toil state graph has no vanilla timed retreat, morale-loss retreat or
    // independent attack-colony order. RHF drives objectives; vanilla combat still
    // handles local self-defence, gunfire, wounds and individual job execution.
    public sealed class RHFRescueLordJobV124 : LordJob
    {
        public string MissionKey = "";
        public IntVec3 Focus = IntVec3.Invalid;
        public RHFRescueLordJobV124() { }
        public RHFRescueLordJobV124(string missionKey, IntVec3 focus)
        { MissionKey = missionKey ?? ""; Focus = focus; }
        public override StateGraph CreateGraph()
        {
            StateGraph graph = new StateGraph();
            var hold = new RHFRescueHoldToilV124();
            graph.AddToil(hold);
            graph.StartingToil = hold;
            return graph;
        }
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref MissionKey, "rhfRescueMissionKeyV124", "");
            Scribe_Values.Look(ref Focus, "rhfRescueFocusV124", IntVec3.Invalid);
        }
    }

    public sealed class RHFRescueHoldToilV124 : LordToil
    {
        public override void UpdateAllDuties()
        {
            IntVec3 focus = (lord?.LordJob as RHFRescueLordJobV124)?.Focus ?? IntVec3.Invalid;
            if (lord?.ownedPawns == null) return;
            foreach (Pawn pawn in lord.ownedPawns)
            {
                if (pawn?.mindState == null || !pawn.Spawned) continue;
                // A rescue must NEVER inherit vanilla AssaultColony/Defend-point
                // missions that pursue the prisoner or trash buildings by default.
                // Explicit RHF Goto, rescue, extraction and fire-support jobs are
                // the only route movers. This custom idle duty fights immediate
                // threats only, then waits near the soldier's *current* location.
                DutyDef missionHold = DefDatabase<DutyDef>.GetNamedSilentFail(
                    "RHF_HoldForMissionOrders");
                if (missionHold == null)
                {
                    // Never substitute the broad vanilla Defend duty. The
                    // fallback recreated the destructive rescue regression.
                    Log.ErrorOnce("[RHF][MissionV136] RHF_HoldForMissionOrders Def missing; " +
                        "rescue combat duty disabled",130136);
                    pawn.mindState.duty = null;
                    continue;
                }
                pawn.mindState.duty = new PawnDuty(missionHold, pawn.Position);
            }
        }
    }

    internal sealed class RHFRescueCommandStateV124
    {
        public readonly Dictionary<int, IntVec3> LastPosition = new Dictionary<int, IntVec3>();
        public readonly Dictionary<int, int> LastMovedAt = new Dictionary<int, int>();
        public readonly Dictionary<int, int> BestTargetDistance = new Dictionary<int, int>();
        public readonly Dictionary<int, int> LastCloserAt = new Dictionary<int, int>();
        public readonly Dictionary<int, int> LastOrderedAt = new Dictionary<int, int>();
        public Thing CurrentBarrier;
        public Thing ProgressBarrier;
        public int ProgressBarrierHP;
        public int NextBarrierSearch;
        public int LastDiagnosticTick;
        public IntVec3 BarrierForCaptive = IntVec3.Invalid;
        public string LastDecision = "ожидается начало операции";
        public int RecoveredFromVanilla;
        public int OrderedBreach;
        public int OrderedApproach;
        public int Escorted;
        public int InvalidOrders;
    }

    internal static class RHFRescueCommandV124
    {
        // Tactical writer is scoped to its own operation, not a truncated roster.
        private const int Step = 90;
        private static readonly FieldInfo MapField = AccessTools.Field(typeof(MapComponent), "map");
        private static readonly FieldInfo OperationsField = AccessTools.Field(typeof(RHFOperationController), "operations");
        private static readonly MethodInfo FreeMethod = AccessTools.Method(typeof(RHFOperationController), "FreeTarget");
        private static readonly MethodInfo FindBarrierMethod = AccessTools.Method(typeof(RHFOperationController), "FindBarrier");
        private static readonly MethodInfo ChooseExitMethod = AccessTools.Method(typeof(RHFOperationController), "ChooseExit");
        private static readonly ConditionalWeakTable<RHFOperation, RHFRescueCommandStateV124> States =
            new ConditionalWeakTable<RHFOperation, RHFRescueCommandStateV124>();
        // Permission exists only during one explicit mission-controlled exit.
        // Ordinary hostile AI cannot use it to abandon an escorted prisoner.
        private static readonly HashSet<int> AuthorizedExits = new HashSet<int>();

        internal static void MissionExit(Pawn pawn)
        {
            if (pawn == null || !pawn.Spawned) return;
            int id = pawn.thingIDNumber;
            AuthorizedExits.Add(id);
            try { pawn.ExitMap(false, Rot4.Invalid); }
            catch(Exception e)
            {
                // Preserve the error for diagnosis, but one bad NPC state
                // must not spam the console every 90 ticks forever.
                Log.ErrorOnce("[RHF][Evacuation] ExitMap rejected " +
                    pawn.LabelShortCap + ": " + e.GetBaseException(),
                    pawn.thingIDNumber ^ 130013);
            }
            finally { AuthorizedExits.Remove(id); }
        }

        // Called ONLY by the physical JobDriver_RHFCarryDowned after the
        // carrier reached the edge and put its passenger on the ground.
        // Both exits use the exact same authorization as conscious captives.
        internal static void CompleteCarriedExtraction(Pawn carrier, Pawn rescued)
        {
            Map map=rescued?.Map;
            if(map==null || carrier==null || carrier.Map!=map ||
               !rescued.Spawned || !rescued.Downed)return;
            int x=carrier.Position.x, z=carrier.Position.z;
            if(x>5 && z>5 && x<map.Size.x-6 && z<map.Size.z-6)
            {
                Log.Warning("[RHF][ExtractS1] Carrier attempted extraction away from map edge.");
                return;
            }
            RHFOperationController controller=map.GetComponent<RHFOperationController>();
            RHFOperation op=controller?.FindRescueFor(rescued.ThingID);
            if(!IsRescue(op) || op.Phase!=2 || op.Freed==null ||
               !op.Freed.Contains(rescued) || !op.Squad.Contains(carrier))return;
            // A downed pair must use the SAME conservative order as a
            // walking pair. If a pawn cannot exit, never fabricate success.
            MissionExit(carrier);
            if(carrier.MapHeld==map)return;
            MissionExit(rescued);
            if(rescued.MapHeld!=map)
                op.EscapedWithEscort++;
            else
            {
                // The carrier left but the patient remained at the edge.
                // They remain an on-map rescued VIP and must receive a new
                // escort; do not mark the operation as successful.
                if(op.Escorts!=null)
                    foreach(RHFEscortLink link in op.Escorts)
                        if(link?.Captive==rescued && link.Escort==carrier)
                            link.Escort=null;
                if(op.BoardS41!=null)
                {
                    op.BoardS41.NextReplanTick=0;
                    op.BoardS41.LastPlanTick=0;
                }
            }
        }

        private static Map MapFor(RHFOperationController controller)
        { return controller == null ? null : MapField?.GetValue(controller) as Map; }

        private static List<RHFOperation> Operations(RHFOperationController controller)
        { return controller == null ? null : OperationsField?.GetValue(controller) as List<RHFOperation>; }

        private static bool IsRescue(RHFOperation op)
        { return op != null && op.Deployed && !op.Finished &&
            op.Type == (int)RHFOperationType.Rescue; }

        private static string Key(RHFOperation op)
        {
            string captiveId = op.Vip?.ThingID;
            if (string.IsNullOrEmpty(captiveId) && op.Targets != null && op.Targets.Count > 0)
                captiveId = op.Targets[0]?.ThingID;
            return op.CreatedTick + ":" + (captiveId ?? "unknown") + ":rescue";
        }

        private static Pawn Target(RHFOperation op, Map map)
        {
            if (op?.Targets == null || map == null) return null;
            if (CanRescue(op.Vip, op, map)) return op.Vip;
            for (int i = 0; i < op.Targets.Count; i++)
                if (CanRescue(op.Targets[i], op, map)) return op.Targets[i];
            return null;
        }

        private static bool CanRescue(Pawn prisoner, RHFOperation op, Map map)
        {
            return prisoner != null && prisoner.Spawned && !prisoner.Dead &&
                prisoner.Map == map && (prisoner.IsPrisonerOfColony || prisoner.IsSlaveOfColony) &&
                !op.Freed.Contains(prisoner);
        }

        private static bool ActivePawn(Pawn p, Map map)
        { return p != null && p.Spawned && p.Map == map && !p.Dead &&
                 !p.Downed && p.jobs != null; }

        private static void RelinquishVanilla(Pawn pawn)
        {
            Lord current = pawn.GetLord();
            if (current != null && !(current.LordJob is RHFRescueLordJobV124))
                current.Notify_PawnLost(pawn, PawnLostCondition.LeftVoluntarily);
        }

        // Pair each sponsor's rescuers with their own persistent Lord. This
        // prevents a different coalition or vanilla raid of the same faction
        // from being commandeered. Lord jobs survive save/load independently.
        private static void ClaimGroup(RHFOperation op, Map map)
        {
            if (!IsRescue(op) || map == null || op.Squad == null) return;
            string key = Key(op);
            Pawn goal = Target(op, map);
            // Phase 3 is a HOLD/BUILD operation, NOT an exit order.
            // The old code focused the whole Lord on ExitCell as soon as VIP
            // was liberated, leaving nobody to construct the field launcher.
            IntVec3 focus = op.Phase == 3 ?
                (op.PodCell.IsValid ? op.PodCell :
                    op.Vip != null && op.Vip.Spawned ? op.Vip.Position : op.ExitCell) :
                op.Phase >= 2 && op.ExitCell.IsValid ? op.ExitCell :
                goal != null ? goal.Position :
                !string.IsNullOrEmpty(op.CaseKeyV10) && op.CaseSearchCellV10.IsValid ?
                    op.CaseSearchCellV10 : op.ExitCell;
            var factions = new List<Faction>(5);
            for (int i = 0; i < op.Squad.Count; i++)
            {
                Pawn p = op.Squad[i];
                if (!ActivePawn(p, map) || p.Faction == null || factions.Contains(p.Faction)) continue;
                factions.Add(p.Faction);
            }
            foreach (Faction f in factions)
            {
                var members = new List<Pawn>();
                Lord missionLord = null;
                for (int i = 0; i < op.Squad.Count; i++)
                {
                    Pawn p = op.Squad[i];
                    if (!ActivePawn(p, map) || p.Faction != f) continue;
                    members.Add(p);
                    Lord l = p.GetLord();
                    if (l?.LordJob is RHFRescueLordJobV124 existing &&
                        existing.MissionKey == key) missionLord = l;
                }
                if (members.Count == 0) continue;
                if (missionLord == null)
                {
                    foreach (Pawn p in members) RelinquishVanilla(p);
                    try
                    {
                        missionLord = LordMaker.MakeNewLord(f,
                            new RHFRescueLordJobV124(key, focus), map, members);
                        States.GetValue(op, _ => new RHFRescueCommandStateV124()).RecoveredFromVanilla += members.Count;
                    }
                    catch (Exception e)
                    {
                        Log.Error("[RHF][RescueV124] Cannot assign mission Lord: " + e);
                        continue;
                    }
                }
                else
                {
                    foreach (Pawn p in members)
                    {
                        if (p.GetLord() == missionLord) continue;
                        RelinquishVanilla(p);
                        missionLord.AddPawn(p);
                    }
                }
                if (missionLord?.LordJob is RHFRescueLordJobV124 job &&
                    focus.IsValid && job.Focus != focus)
                {
                    job.Focus = focus;
                    missionLord.CurLordToil?.UpdateAllDuties();
                }
                // R020: adopting a native raid Lord does not itself abort a
                // previously issued vanilla attack on a random building.
                // Cancel ONLY stale structural combat orders, not shooting at
                // player defenders or turrets. The mission's board may issue
                // precisely one structural breach (or a field-pod access breach).
                foreach(Pawn p in members)
                {
                    if(!ActivePawn(p,map) || p.GetLord()!=missionLord)continue;
                    Job running=p.jobs?.curJob;
                    if(running==null ||
                       (running.def!=JobDefOf.AttackMelee &&
                        running.def!=JobDefOf.AttackStatic))continue;
                    Building structure=running.targetA.Thing as Building;
                    if(structure==null || RHFCombatPlannerV125.IsTurret(structure))continue;
                    RHFOrderS41 issued=op.BoardS41?.For(p,false);
                    bool permitted=op.Phase==1 &&
                        op.BoardS41?.Barrier==structure &&
                        op.BoardS41?.Breacher==p;
                    if(!permitted && (op.Phase==2 || op.Phase==3) && issued!=null)
                        permitted=(RHFRoleS41)issued.Role==RHFRoleS41.Breach &&
                            issued.Objective==structure;
                    if(permitted)continue;
                    Log.WarningOnce("[NJR][R020] Cancelled stale native structure " +
                        "attack: "+p.LabelShortCap+" / "+structure.LabelShortCap,
                        p.thingIDNumber ^ structure.thingIDNumber ^ 2020);
                    p.jobs.EndCurrentJob(JobCondition.InterruptForced);
                }
                // Fail closed: unclaimed rescue members cannot remain in a
                // vanilla ImmediateAttack/Breaching lord while a mission is live.
                // If a modded pawn cannot join our Lord, detach it and hold
                // position, recording the problem instead of wrecking furniture.
                foreach (Pawn p in members)
                {
                    if (!ActivePawn(p,map)) continue;
                    Lord actual=p.GetLord();
                    if (actual?.LordJob is RHFRescueLordJobV124 assigned &&
                        assigned.MissionKey==key) continue;
                    if (actual!=null) RelinquishVanilla(p);
                    if (p.GetLord()==null)
                    {
                        try { p.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Wait,p.Position)); }
                        catch (Exception e)
                        { Log.ErrorOnce("[RHF][MissionV132] Uncontrolled pawn: "+e.GetBaseException(),
                            p.thingIDNumber ^ 132500); }
                    }
                    Log.ErrorOnce("[RHF][MissionV132] Rescue soldier without mission Lord: "+
                        p.LabelShortCap+"; mission="+key+"; vanilla assault disabled",
                        p.thingIDNumber ^ 132210);
                }
            }
        }

        internal static void OnDeploy(RHFOperationController controller, RHFOperation op,
            bool incidentSucceeded)
        {
            if (!incidentSucceeded || !IsRescue(op)) return;
            ClaimGroup(op, MapFor(controller));
            // v0.13.3-S0: Refresh() was only reachable through the retired
            // RefreshOwnership hook, so freed captives were bound once at release
            // and never again. Re-assert mission ownership each decision cycle.
            // StabilizeFreed is idempotent and ignores pawns off-map or in a pod.
            if (op.Freed != null)
                for (int j = 0; j < op.Freed.Count; j++)
                    StabilizeFreed(op, op.Freed[j]);
        }

        // Replaces the 0.12.3 detach-all-Lords loop. Even a pawn that finishes
        // its ordered Goto still remains assigned to the RHF mission, not an
        // uncommanded enemy who chooses to wander to the map edge.
        internal static void Refresh(RHFOperationController controller)
        {
            int now = Find.TickManager.TicksGame;
            if (now % Step != 0) return;
            List<RHFOperation> ops = Operations(controller);
            if (ops == null || ops.Count == 0) return;
            Map map = MapFor(controller);
            if (map == null) return;
            for (int i = 0; i < ops.Count; i++)
            {
                RHFOperation op=ops[i];
                if(!IsRescue(op))continue;
                ClaimGroup(op,map);
                if(op.Freed!=null)
                    for(int j=0;j<op.Freed.Count;j++)
                        StabilizeFreed(op,op.Freed[j]);
            }
        }

        private static int LastMove(Pawn p, RHFRescueCommandStateV124 state, int now)
        {
            int id = p.thingIDNumber;
            if (!state.LastPosition.TryGetValue(id, out IntVec3 old) || old != p.Position)
            {
                state.LastPosition[id] = p.Position;
                state.LastMovedAt[id] = now;
            }
            return state.LastMovedAt.TryGetValue(id, out int tick) ? tick : now;
        }

        private static int LastCloser(Pawn pawn, Pawn captive,
            RHFRescueCommandStateV124 state, int now)
        {
            int id = pawn.thingIDNumber;
            int distance = (pawn.Position - captive.Position).LengthHorizontalSquared;
            if (!state.BestTargetDistance.TryGetValue(id, out int closest) || distance < closest)
            {
                state.BestTargetDistance[id] = distance;
                state.LastCloserAt[id] = now;
            }
            return state.LastCloserAt.TryGetValue(id,out int last) ? last : now;
        }

        private static bool Give(Pawn p, JobDef jobDef, LocalTargetInfo target,
            RHFRescueCommandStateV124 state, int now)
        {
            if (p?.jobs == null || jobDef == null) return false;
            // A path assignment cannot be restarted every 90 ticks. Jobs that
            // genuinely fail are retried with a different goal after 600 ticks.
            Job current = p.jobs.curJob;
            // Combat Extended suppression and cover take precedence over RHF
            // waypoint pulses. Never bounce a pinned soldier between a forced
            // Goto and the game's suppression/cover response.
            if (RHFCombatPlannerV125.IsCombatEmergencyJob(p)) return false;
            if (current != null && current.def == jobDef &&
                (target.Thing != null ? current.targetA.Thing == target.Thing :
                    current.targetA.Cell == target.Cell)) return true;
            if (state.LastOrderedAt.TryGetValue(p.thingIDNumber, out int last) &&
                now - last < 180 && current != null && current.def == jobDef)
                return true;
            try
            {
                p.jobs.TryTakeOrderedJob(JobMaker.MakeJob(jobDef, target));
                state.LastOrderedAt[p.thingIDNumber] = now;
                if (p.jobs.curJob?.def == jobDef) return true;
            }
            catch (Exception e)
            {
                state.LastDecision = "исключение при выдаче приказа: " + e.GetBaseException().Message;
            }
            state.InvalidOrders++;
            return false;
        }

        // Priority: an accessible prison door, then an accessible outer door,
        // and only then a load-bearing wall. The search is shared by the wing,
        // may search every physical map obstacle on demand; no arbitrary probe cutoff.
        private static Thing AccessObstacle(RHFOperationController controller,
            RHFOperation op, Pawn leader, Pawn captive, RHFRescueCommandStateV124 state, int now)
        {
            Map map = captive.Map;
            // 0.12.8: one authoritative end-to-end path (movement AND breach).
            // Do not fall back to the nearest unrelated wall when it is blocked.
            if (RHFObjectiveRouteV128.TryObjective(op,map,leader,captive,now,
                out RHFRoutePlanV128 missionRoute))
            {
                state.CurrentBarrier = missionRoute.Valid ? missionRoute.FirstBarrier : null;
                state.BarrierForCaptive = captive.Position;
                state.NextBarrierSearch = now + 600;
                if (!missionRoute.Valid)
                {
                    state.LastDecision = "маршрут не построен: " + missionRoute.Explanation;
                    return null;
                }
                if (state.CurrentBarrier!=null && !state.CurrentBarrier.Destroyed &&
                    leader.CanReach(state.CurrentBarrier.Position,PathEndMode.Touch,Danger.Deadly))
                    return state.CurrentBarrier;
                return null; // proceed to the stored corridor's staging waypoint
            }
            if (state.CurrentBarrier != null && !state.CurrentBarrier.Destroyed &&
                state.BarrierForCaptive == captive.Position && now < state.NextBarrierSearch &&
                leader.CanReach(state.CurrentBarrier.Position, PathEndMode.Touch, Danger.Deadly))
                return state.CurrentBarrier;
            if (state.CurrentBarrier == null && now < state.NextBarrierSearch &&
                state.BarrierForCaptive == captive.Position) return null;
            if (state.CurrentBarrier != null && !state.CurrentBarrier.Destroyed &&
                now < state.NextBarrierSearch) return null;
            state.CurrentBarrier = null;
            state.NextBarrierSearch = now + 600;
            state.BarrierForCaptive = captive.Position;
            // First check the actual destructible path to the hostage. The
            // old nearest-door search sometimes chose the storage/battery room.
            bool clearCorridor;
            Thing goalObstacle = RHFCombatPlannerV125.FirstObjectiveObstacle(
                leader,captive,out clearCorridor);
            if (clearCorridor)
            {
                // The weighted route reaches the prisoner via genuine open
                // passages. Do not fall back to a nearby irrelevant wall/door.
                state.CurrentBarrier=null;
                return null;
            }
            if (goalObstacle != null && !goalObstacle.Destroyed &&
                leader.CanReach(goalObstacle.Position,PathEndMode.Touch,Danger.Deadly))
            { state.CurrentBarrier=goalObstacle; return goalObstacle; }
            try
            {
                if (FindBarrierMethod != null)
                {
                    Thing immediate = FindBarrierMethod.Invoke(controller,
                        new object[] { captive, leader }) as Thing;
                    if (RHFCombatPlannerV125.IsBreachableStructure(immediate) &&
                        leader.CanReach(immediate.Position, PathEndMode.Touch, Danger.Deadly))
                    { state.CurrentBarrier = immediate; return immediate; }
                }
            }
            catch (Exception e)
            { state.LastDecision = "поиск входа: " + e.GetBaseException().Message; }

            Room prison = captive.Position.GetRoom(map);
            var candidates = new List<Thing>();
            var scores = new List<int>();
            // Examine the entire map for a real usable prison perimeter,
            // not an arbitrary 65x65 window around a captive.
            for (int x = 0; x < map.Size.x; x++)
            for (int z = 0; z < map.Size.z; z++)
            {
                IntVec3 cell = new IntVec3(x, 0, z);
                if (!cell.InBounds(map)) continue;
                Building edifice = cell.GetEdifice(map);
                if (!RHFCombatPlannerV125.IsBreachableStructure(edifice))
                    continue;
                bool door = edifice is Building_Door;
                bool prisonEdge = false;
                if (prison != null)
                {
                    for (int direction = 0; direction < 4; direction++)
                    {
                        IntVec3 neighbor = direction == 0 ? cell+IntVec3.North :
                            direction == 1 ? cell+IntVec3.South :
                            direction == 2 ? cell+IntVec3.East : cell+IntVec3.West;
                        if (neighbor.InBounds(map) && neighbor.GetRoom(map) == prison)
                        { prisonEdge = true; break; }
                    }
                }
                // Lower is better. Never demolish an inner wall when there is
                // a reachable doorway serving the same enclosure.
                int score = (prisonEdge ? 0 : 8000) + (door ? 0 : 1000) +
                    (cell - captive.Position).LengthHorizontalSquared / 4 +
                    (cell - leader.Position).LengthHorizontalSquared / 8;
                int at = 0;
                while (at < scores.Count && scores[at] <= score) at++;
                candidates.Insert(at, edifice);
                scores.Insert(at, score);
            }
            for (int i = 0; i < candidates.Count; i++)
            {
                Thing candidate = candidates[i];
                if (leader.CanReach(candidate.Position, PathEndMode.Touch, Danger.Deadly))
                { state.CurrentBarrier = candidate; return candidate; }
            }
            return null;
        }

        internal static void Infiltrate(RHFOperationController controller, RHFOperation op, int now)
        {
            // S4.1 replaces the regular legacy external pawn-job writer.
            // Keep older code only as a migration reference, not a second commander.
            if (RHFRescuePlannerS41.Enabled)
            { RHFRescuePlannerS41.Infiltrate(controller,op,now); return; }
            if (!IsRescue(op)) return; // CaseKeyV10 is NOT a routing override.
            Map map = MapFor(controller);
            if (map == null) return;
            ClaimGroup(op, map);
            var state = States.GetValue(op, _ => new RHFRescueCommandStateV124());
            Pawn captive = Target(op, map);
            if (captive == null)
            {
                RecoverReleasedTargets(op,map,now);
                // Do not report a rescue victory if the last prisoner died,
                // escaped alone or became unreachable for a different reason.
                op.Phase = op.Freed.Count > 0 && op.TryPod && op.Vip != null &&
                    op.Freed.Contains(op.Vip) && op.Vip.Map==map ? 3 : 2;
                op.PhaseStartTick = now;
                state.LastDecision = op.Freed.Count > 0 ?
                    (op.Phase==3 ? "VIP освобождён: инженерная эвакуация" :
                    "все доступные цели освобождены: формирование отхода") :
                    "пленник утрачен: отход без объявления успешного спасения";
                return;
            }
            int alive = 0;
            int orders = 0;
            int stalled = 0;
            int start = op.Squad.Count > 0 ? (now / Step) % op.Squad.Count : 0;
            Pawn lead = null;
            for (int i=0; i<op.Squad.Count; i++)
                if (ActivePawn(op.Squad[i], map)) { lead=op.Squad[i]; break; }
            if (lead == null)
            {
                state.LastDecision = "нет боеспособных спасателей, пленник не освобождён";
                op.Finished = true;
                return;
            }
            // Tactical coverage also applies to diversion wings; their existing
            // vanilla Lord is still responsible for the overarching assault.
            if(op.Diversion != null && op.Diversion.Count > 0)
                RHFCombatPlannerV125.ImproveAttack(op,map,now);
            // Combat-aware allocation: at least most of the team continues to
            // the prisoner while a bounded wing shoots or flanks real threats.
            // Route selection must happen BEFORE splitting the squad: if the
            // route still crosses direct turret fire, allocate extra shooters
            // and hold the assault wing before the first exposed cell.
            RHFRoutePlanV128 route = RHFObjectiveRouteV128.Resolve(op,map,lead,captive,now);
            bool needsFireSuppression = route.Valid &&
                route.FirstTurretExposureIndex >= 0;
            HashSet<Pawn> covering = RHFCombatPlannerV125.Response(
                op,map,captive,op.Squad,now,false,needsFireSuppression);
            // One pre-contact assembly: five dispersed columns, staggered
            // ranks, release on 75% readiness or a short bounded deadline.
            // Only THIS mission controller issues the Goto jobs.
            bool gathering=RHFStagedAdvanceV139.Holding(op,map,route,lead,captive,
                covering,now,out Dictionary<int,IntVec3> assemblySlots);
            IntVec3 safeApproach = route.Valid ||
                route.Explanation!="неверная цель или карта" ? IntVec3.Invalid :
                RHFCombatPlannerV125.SafeApproach(op,map,lead,captive,now);
            // A one-cell wall cannot be attacked by four sappers at once.
            // Choose one qualified operator per decision, keep all other
            // fighters available for screening and protection of the objective.
            Pawn breachOperator=RHFCombatPlannerV125.SelectBreachOperator(
                op,map,route.Valid?route.FirstBarrier:null,covering);
            int assignedBreaches=0;
            bool routeOpen = lead.CanReach(captive, PathEndMode.Touch, Danger.Deadly);
            int leadLastMove = LastMove(lead, state, now);
            int leadLastCloser = LastCloser(lead,captive,state,now);
            Thing sharedBarrier = route.Valid ? route.FirstBarrier : null;
            // No valid plan means NO wall-permission. Previous fallback picked
            // unrelated doors, batteries, and colony infrastructure.
            if (sharedBarrier != null && sharedBarrier.Destroyed) sharedBarrier=null;
            bool objectiveProgress = false;
            Thing lastBarrier = state.ProgressBarrier;
            if (sharedBarrier != lastBarrier)
            {
                state.ProgressBarrier=sharedBarrier;
                state.ProgressBarrierHP=sharedBarrier?.HitPoints ?? 0;
            }
            else if (sharedBarrier != null && sharedBarrier.HitPoints < state.ProgressBarrierHP)
            {
                state.ProgressBarrierHP=sharedBarrier.HitPoints;
                objectiveProgress=true;
            }

            for (int j=0;j<op.Squad.Count;j++)
            {
                Pawn rescuer = op.Squad[(start+j) % op.Squad.Count];
                if (!ActivePawn(rescuer, map)) continue;
                alive++;
                if (covering.Contains(rescuer)) continue; // keep real suppressive fire
                if(gathering)
                {
                    if(assemblySlots!=null &&
                       assemblySlots.TryGetValue(rescuer.thingIDNumber,out IntVec3 slot) &&
                       (rescuer.Position-slot).LengthHorizontalSquared>4 &&
                       rescuer.CanReach(slot,PathEndMode.OnCell,Danger.Deadly) &&
                       Give(rescuer,JobDefOf.Goto,slot,state,now))
                    {orders++;state.OrderedApproach++;}
                    state.LastDecision="группа собирается перед контактом; "+
                        "синхронное выдвижение после построения";
                    continue;
                }
                int moved = LastMove(rescuer, state, now);
                int closer = LastCloser(rescuer,captive,state,now);
                int sqdist = (rescuer.Position-captive.Position).LengthHorizontalSquared;
                // Real progress: approaching the target or damaging the planned
                // obstacle, NOT merely reissuing an identical ordered job.
                // Under a turret firebreak, shuffling around the holding line
                // does NOT count as progress toward an achievable objective.
                if (!needsFireSuppression && now-moved<=Step && now-closer<=Step)
                    objectiveProgress=true;
                if (sqdist<=5 && rescuer.CanReach(captive, PathEndMode.Touch, Danger.Deadly))
                {
                    if (FreeMethod == null)
                    { state.LastDecision = "нет метода освобождения пленника"; break; }
                    try
                    {
                        FreeMethod.Invoke(controller, new object[] { op, captive });
                        state.LastDecision = "достигнут пленник " + captive.LabelShortCap +
                            "; переход к следующему пленнику/эвакуации";
                    }
                    catch(Exception e)
                    { state.LastDecision = "ошибка освобождения: " + e.GetBaseException().Message; }
                    return;
                }
                // Let useful movement and attacks finish. Do not reset jobs at
                // a fixed frequency even when navigation is temporarily complex.
                Job current=rescuer.jobs.curJob;
                bool attacking = current?.def == JobDefOf.AttackMelee &&
                    sharedBarrier != null && current.targetA.Thing == sharedBarrier &&
                    !sharedBarrier.Destroyed;
                bool canPlaceBreach=false; // no demolition charges in RHF
                if (attacking && !canPlaceBreach && now-moved<1600) continue;
                // Do not reissue a different waypoint every 90 ticks. The
                // current waypoint is still on the costed corridor and must
                // be allowed to finish unless the soldier has genuinely stalled.
                if (route.Valid && current?.def==JobDefOf.Goto &&
                    current.targetA.Thing==null &&
                    RHFObjectiveRouteV128.IsAllowedWaypoint(route,current.targetA.Cell) &&
                    now-moved<900) continue;
                bool approaching = current?.def == JobDefOf.Goto &&
                    (current.targetA.Thing==captive ||
                     (current.targetA.Cell-captive.Position).LengthHorizontalSquared<=9);
                if (approaching && now-moved<900 && now-closer<1200 &&
                    !route.Valid &&
                    (!safeApproach.IsValid ||
                     (rescuer.Position-safeApproach).LengthHorizontalSquared<=16)) continue;

                // A safer staging flank around static defence takes precedence
                // over a suicidal shortest path down the main kill corridor.
                IntVec3 staged = route.Valid ?
                    RHFObjectiveRouteV128.Waypoint(route,rescuer,map) :
                    safeApproach.IsValid ?
                    RHFCombatPlannerV125.FormationCell(safeApproach,rescuer,map) :
                    IntVec3.Invalid;
                if (staged.IsValid && !attacking &&
                    (rescuer.Position-staged).LengthHorizontalSquared>
                        (route.Valid ? 0 : 16) &&
                    (rescuer.Position-captive.Position).LengthHorizontalSquared>16 &&
                    rescuer.CanReach(staged,PathEndMode.OnCell,Danger.Deadly))
                {
                    if(Give(rescuer,JobDefOf.Goto,staged,state,now))
                    {orders++;state.OrderedApproach++;}
                    continue;
                }
                bool canApproach = rescuer.CanReach(captive, PathEndMode.Touch, Danger.Deadly);
                if (canApproach && !route.Valid &&
                    route.Explanation=="неверная цель или карта" &&
                    now-moved<900 && now-closer<1200)
                {
                    if (Give(rescuer, JobDefOf.Goto, captive, state, now))
                    { orders++; state.OrderedApproach++; }
                    continue;
                }
                // A geometrically possible breach is NOT permission to march
                // through a firing arc just to reach that wall. First deal with
                // the turret using the separate, combat-capable cover wing.
                if (route.Valid && needsFireSuppression &&
                    (route.FirstBarrierIndex<0 ||
                     route.FirstBarrierIndex>=route.FirstTurretExposureIndex))
                {
                    if(current?.def==JobDefOf.Goto &&
                       current.targetA.Thing==null &&
                       !RHFObjectiveRouteV128.IsAllowedWaypoint(route,current.targetA.Cell))
                        Give(rescuer,JobDefOf.Wait,rescuer.Position,state,now);
                    stalled++;
                    state.LastDecision="огневой рубеж: сначала подавить турели, " +
                        "затем продолжить прорыв";
                    continue;
                }
                Thing barrier = sharedBarrier;
                if (barrier != null && (!barrier.Spawned || barrier.Destroyed ||
                    !rescuer.CanReach(barrier.Position, PathEndMode.Touch, Danger.Deadly)))
                    barrier=null;
                // Only the first obstacle of the costed objective route can be
                // breached; do not choose unrelated visible destructibles.
                if (RHFCombatPlannerV125.IsBreachableStructure(barrier))
                {
                    // One dedicated specialist, never a grenade wielder
                    // ordered to slowly butt-stroke an impassable wall.
                    if(rescuer!=breachOperator || assignedBreaches>0)
                    {stalled++;continue;}
                    assignedBreaches++;
                    bool placed=RHFCombatPlannerV125.TryPaidBreach(
                        controller,op,captive,barrier,rescuer,now);
                    JobDef attack=RHFCombatPlannerV125.BreachAttack(rescuer);
                    if(placed || (attack!=null &&
                        (attack!=JobDefOf.AttackStatic ||
                         !RHFRescueFireDisciplineV136.UnsafeLine(op,rescuer,barrier)) &&
                        Give(rescuer,attack,barrier,state,now)))
                    {orders++;state.OrderedBreach++;}
                    else stalled++;
                    state.LastDecision="пролом назначен: "+
                        rescuer.LabelShortCap+" → "+barrier.LabelShortCap;
                }
                else
                {
                    stalled++;
                    // If a route is reachable for this individual but the
                    // previously assigned job failed, retry it once rather than
                    // inventing a teleport or silently ordering a retreat.
                    if (canApproach && (
                        (!route.Valid && route.Explanation=="неверная цель или карта") ||
                        (route.Valid && (rescuer.Position-captive.Position).LengthHorizontalSquared<=16)) &&
                        Give(rescuer, JobDefOf.Goto, captive, state, now))
                    { orders++; state.OrderedApproach++; }
                    state.LastDecision = "маршрут к пленнику заблокирован, доступный вход не найден";
                }
            }
            op.LastOrderTick = now;
            if (RHFCombatPlannerV125.TurretDamageProgress(op))
                objectiveProgress=true;
            if (objectiveProgress) op.LastProgressTick = now;
            // No available route and no effective covering fire is a mission
            // failure, not a license to march the main squad into turrets for
            // hours. Withdraw survivors after a sustained, observable stalemate.
            if(stalled>0 && orders==0 &&
                now-Math.Max(op.LastProgressTick,op.PhaseStartTick)>
                    (needsFireSuppression ? 15000 : 27000))
            {
                state.LastDecision="спасение отменено: безопасный маршрут/пролом " +
                    "не найден или оборону не удалось подавить; организованный отход";
                op.Phase=2;
                op.PhaseStartTick=now;
                Log.Warning("[RHF][FirebreakV129] Rescue withdrew after failing to " +
                    "neutralize stationary defences; captive="+captive.LabelShortCap);
                return;
            }
            if (stalled > 0 && now-state.LastDiagnosticTick >= 1800)
            {
                state.LastDiagnosticTick = now;
                Log.Warning("[RHF][RescueV124] Stalled " + captive.LabelShortCap +
                    ", blocked=" + stalled + ", ordered=" + orders +
                    ", crew=" + op.Squad.Count + ", reason=" + state.LastDecision);
            }
        }

        private static IntVec3 ExitCell(RHFOperationController controller,
            RHFOperation op, Pawn origin, Map map)
        {
            if (op.ExitCell.IsValid && op.ExitCell.InBounds(map) &&
                (origin == null || origin.CanReach(op.ExitCell, PathEndMode.OnCell, Danger.Deadly)))
                return op.ExitCell;
            if (origin != null && ChooseExitMethod != null)
            {
                try
                {
                    IntVec3 choice = (IntVec3)ChooseExitMethod.Invoke(controller,
                        new object[] { origin.Position });
                    if (choice.IsValid && choice.InBounds(map))
                    { op.ExitCell = choice; return choice; }
                }
                catch(Exception e)
                { Log.Warning("[RHF][RescueV124] Exit selection: " + e.GetBaseException().Message); }
            }
            return op.ExitCell;
        }

        // Avoid the old EnsureEscort implementation here: it removes the
        // assigned soldier from whichever Lord owns them. The new rescue Lord
        // must remain authoritative all the way through extraction.
        private static RHFEscortLink Escort(RHFOperation op, Pawn captive, Map map)
        {
            if (op?.Escorts == null || captive == null) return null;
            RHFEscortLink current = null;
            foreach (RHFEscortLink link in op.Escorts)
                if (link?.Captive == captive) { current = link; break; }
            if (ActivePawn(current?.Escort, map)) return current;
            Pawn chosen = null;
            int best = int.MaxValue;
            for (int i = 0; i < op.Squad.Count; i++)
            {
                Pawn p = op.Squad[i];
                if (!ActivePawn(p, map) || p.Faction == null || captive.Faction == null ||
                    (p.Faction.HostileTo(captive.Faction) &&
                     !RHFMissionIntegrityV130.MayEscort(op,p,captive))) continue;
                int score = (p.Position-captive.Position).LengthHorizontalSquared;
                RHFTacticalMember member=RHFTactical.Member(op,p);
                if (member?.Role == (int)RHFCombatRole.Escort) score -= 36;
                foreach (RHFEscortLink link in op.Escorts)
                    if (link?.Escort == p && link.Captive != captive &&
                        link.Captive != null && link.Captive.MapHeld == map)
                    { score += 25000; break; }
                if (score >= best) continue;
                best = score; chosen = p;
            }
            if (chosen == null) return null;
            if (current == null)
            { current = new RHFEscortLink(captive,chosen); op.Escorts.Add(current); }
            else current.Escort = chosen;
            return current;
        }

        // Some prison-break/door mechanics release a pawn BEFORE our explicit
        // FreeTarget method can be called. Only adopt such a target when an
        // actual rescuer is close enough to have reached the detention area.
        private static void RecoverReleasedTargets(RHFOperation op,Map map,int now)
        {
            if(op?.Targets==null || op.Squad==null)return;
            for(int i=0;i<op.Targets.Count;i++)
            {
                Pawn p=op.Targets[i];
                if(p==null || p.Dead || !p.Spawned || p.Map!=map ||
                    p.IsPrisonerOfColony || p.IsSlaveOfColony ||
                    p.Faction==Faction.OfPlayer || op.Freed.Contains(p))continue;
                bool present=false;
                for(int j=0;j<op.Squad.Count;j++)
                {
                    Pawn rescuer=op.Squad[j];
                    if(ActivePawn(rescuer,map) &&
                       (rescuer.Position-p.Position).LengthHorizontalSquared<=144)
                    {present=true;break;}
                }
                if(!present)continue;
                op.Freed.Add(p);
                StabilizeFreed(op,p);
                Log.Message("[RHF][V131] Recovered auto-liberated captive "+
                    p.LabelShortCap+" near rescue squad; phase="+op.Phase);
            }
        }

        internal static void StabilizeFreed(RHFOperation op,Pawn captive)
        {
            if(op==null || captive==null || !captive.Spawned || captive.Map==null ||
               op.Freed==null || !op.Freed.Contains(captive))return;
            // Preserve the prisoner's authentic original affiliation while
            // giving them a rescue-specific Lord and defend objective.
            RebindFreed(op,captive);
            // Mission duty must be restored after faction and Lord handoff or load.
            Lord lord=captive.GetLord();
            if(lord?.LordJob is RHFRescueLordJobV124 job &&
               job.MissionKey==Key(op) &&
               captive.mindState?.duty?.def?.defName!="RHF_HoldForMissionOrders")
                lord.CurLordToil?.UpdateAllDuties();
        }

        internal static bool IsControlledFreed(Pawn captive)
        {
            if(captive==null || captive.Map==null || captive.Dead)return false;
            RHFOperation op=captive.Map.GetComponent<RHFOperationController>()?
                .FindRescueFor(captive.ThingID);
            return op!=null && op.Deployed && !op.Finished &&
                op.Freed!=null && op.Freed.Contains(captive) &&
                !(op.PodLaunchCompleted && captive==op.Vip);
        }

        private static void RebindFreed(RHFOperation op, Pawn captive)
        {
            if (captive == null || !captive.Spawned || captive.Map == null) return;
            if(captive.Faction==null)
            {
                Faction original=Current.Game?.GetComponent<RHFGameLedger>()?
                    .FindHostage(captive)?.OriginalFaction ?? op?.Sponsor;
                if(original!=null && !original.IsPlayer) captive.SetFaction(original);
            }
            string key = Key(op);
            if (captive.GetLord()?.LordJob is RHFRescueLordJobV124 already &&
                already.MissionKey == key) return;
            for (int i=0;i<op.Squad.Count;i++)
            {
                Pawn rescuer=op.Squad[i];
                Lord missionLord=rescuer?.GetLord();
                if (rescuer?.Faction != captive.Faction || missionLord == null ||
                    !(missionLord.LordJob is RHFRescueLordJobV124 job) || job.MissionKey != key)
                    continue;
                RelinquishVanilla(captive);
                missionLord.AddPawn(captive);
                return;
            }
            // A hired rescue team may belong to a DIFFERENT and ordinarily
            // hostile faction. Its captive still needs a coherent independent
            // mission duty rather than the freed-pawn flee/attack think tree.
            if(captive.Faction!=null)
            {
                RelinquishVanilla(captive);
                try
                {
                    LordMaker.MakeNewLord(captive.Faction,
                        new RHFRescueLordJobV124(key,captive.Position),
                        captive.Map,new List<Pawn>{captive});
                }
                catch(Exception e)
                { Log.Warning("[RHF] Freed captive mission Lord could not be created: " + e.Message); }
            }
        }

        // Escorted extraction is one group mission, not a bunch of independent
        // ExitMap jobs. Every freed pawn travels near its assigned escort and
        // the rear guard must remain close to the rescued group until its exit.
        internal static bool Extract(RHFOperationController controller, RHFOperation op, int now)
        {
            // S4.1 controls escort movement via role JobGivers, not 90-tick Goto.
            if (RHFRescuePlannerS41.Enabled)
                return RHFRescuePlannerS41.Extract(controller,op,now);
            if (!IsRescue(op)) return false;
            Map map = MapFor(controller);
            if (map == null) return false;
            if (op.Freed == null) return false;
            // Physical carrier jobs and conscious escorts share one mission
            // state machine. NEVER fall through to RHFOperations.Extract.
            ClaimGroup(op,map);
            var state=States.GetValue(op, _ => new RHFRescueCommandStateV124());
            // Extraction is still a combat operation. A small rear guard may
            // shoot turrets/hostiles instead of receiving compulsory Exit Goto.
            Pawn threatAnchor=null;
            for(int t=0;t<op.Freed.Count;t++)
                if(op.Freed[t]!=null && op.Freed[t].Spawned && op.Freed[t].Map==map)
                {threatAnchor=op.Freed[t];break;}
            HashSet<Pawn> covering=RHFCombatPlannerV125.Response(
                op,map,threatAnchor,op.Squad,now,true);
            Pawn anchor=null;
            for(int i=0;i<op.Freed.Count;i++)
                if(op.Freed[i]!=null && op.Freed[i].Spawned && op.Freed[i].Map==map && !op.Freed[i].Dead)
                {anchor=op.Freed[i];break;}
            if(anchor==null)
                for(int i=0;i<op.Squad.Count;i++)
                    if(ActivePawn(op.Squad[i],map)){anchor=op.Squad[i];break;}
            IntVec3 exit = ExitCell(controller,op,anchor,map);
            if(!exit.IsValid || !exit.InBounds(map))
            {state.LastDecision="не найден законный выход с карты";return true;}
            int orders=0;
            int remaining=0;
            Pawn slowest=null;
            int farthest=-1;
            for(int i=0;i<op.Freed.Count;i++)
            {
                Pawn captive=op.Freed[i];
                if(captive==null || captive.Dead || captive.MapHeld!=map ||
                    (op.PodLaunchCompleted && captive==op.Vip)) continue;
                remaining++;
                if(!captive.Spawned)continue; // already carried: let its JobDriver finish
                RebindFreed(op,captive);
                if(captive.Downed)
                {
                    // Assign an actual carrier who must walk to the hostage,
                    // carry them to the map edge and explicitly exit together.
                    // Never teleport an unconscious pawn or abandon a healthy
                    // partner because this captive cannot walk.
                    if(controller.TryAssignCarrier(op,captive))orders++;
                    state.LastDecision="перенос лежачего пленника к выходу";
                    continue;
                }
                RHFEscortLink link=Escort(op,captive,map);
                Pawn escort=link?.Escort;
                if(!ActivePawn(escort,map))
                {
                    state.LastDecision="пленник " + captive.LabelShortCap + " пока без сопровождающего";
                    continue;
                }
                int distance=(captive.Position-exit).LengthHorizontalSquared;
                if(distance>farthest){farthest=distance;slowest=captive;}
                int separation=(escort.Position-captive.Position).LengthHorizontalSquared;
                // Leave together only when both are visibly at the real map edge.
                if(distance<=10 && (escort.Position-exit).LengthHorizontalSquared<=36 &&
                   separation<=64)
                {
                    MissionExit(captive);
                    if (captive.MapHeld == map)
                    {
                        state.LastDecision="попытка выхода пленника отклонена движком; выход не засчитан";
                        continue;
                    }
                    if(escort.Spawned)MissionExit(escort);
                    if (escort.MapHeld != map)
                    {
                        op.EscapedWithEscort++;
                        state.Escorted++;
                        state.LastDecision="освобождённый и сопровождающий покинули карту";
                    }
                    else state.LastDecision="пленник вышел, сопровождающий задержан на карте";
                    continue;
                }
                if(separation>144)
                {
                    if(Give(captive,JobDefOf.Goto,escort.Position,state,now))orders++;
                    if(Give(escort,JobDefOf.Wait,escort.Position,state,now))orders++;
                }
                else
                {
                    if(Give(captive,JobDefOf.Goto,exit,state,now))orders++;
                    if(separation>49 &&
                       Give(escort,JobDefOf.Goto,captive.Position,state,now))orders++;
                    else if(separation<=49 &&
                       Give(escort,JobDefOf.Goto,exit,state,now))orders++;
                }
            }
            if(remaining==0)
            {
                if(op.AssaultAfterObjective)
                {
                    op.Phase=5;
                    op.PhaseStartTick=now;
                    state.LastDecision="эвакуация завершена; карательная фаза";
                    return true;
                }
                bool anyLeft=false;
                for(int i=0;i<op.Squad.Count;i++)
                {
                    Pawn rescuer=op.Squad[i];
                    if(!ActivePawn(rescuer,map))continue;
                    anyLeft=true;
                    if((rescuer.Position-exit).LengthHorizontalSquared<=10)
                        MissionExit(rescuer);
                    else if(rescuer.CanReach(exit,PathEndMode.OnCell,Danger.Deadly) &&
                        Give(rescuer,JobDefOf.Goto,exit,state,now))orders++;
                    else if(now-op.PhaseStartTick>3600)
                    {
                        // Change a blocked rally edge instead of keeping a
                        // survivor indefinitely at an unreachable boundary.
                        IntVec3 alternate=ExitCell(controller,op,rescuer,map);
                        if(alternate.IsValid && alternate != exit &&
                            rescuer.CanReach(alternate,PathEndMode.OnCell,Danger.Deadly))
                        {exit=alternate;op.ExitCell=alternate;}
                    }
                }
                if(!anyLeft)
                { op.Phase=4; op.Finished=true; }
                state.LastDecision="освобождённые выведены; отход оставшихся спасателей";
            }
            else
            {
                // No independent direction for the rear guard. Instead they
                // maintain a bounded 8-tile cordon around the last captive.
                IntVec3 rally=slowest?.Position ?? anchor?.Position ?? exit;
                for(int i=0;i<op.Squad.Count;i++)
                {
                    Pawn rescuer=op.Squad[i];
                    if(!ActivePawn(rescuer,map))continue;
                    bool paired=false;
                    foreach(RHFEscortLink link in op.Escorts)
                        if(link?.Escort==rescuer && link.Captive!=null &&
                            link.Captive.MapHeld==map && !link.Captive.Dead)
                        {paired=true;break;}
                    if(paired || covering.Contains(rescuer))continue;
                    if((rescuer.Position-rally).LengthHorizontalSquared>64 &&
                        Give(rescuer,JobDefOf.Goto,rally,state,now))orders++;
                }
                state.LastDecision="сопровождение к выходу, осталось пленников=" + remaining;
            }
            op.LastOrderTick=now;
            if(orders>0)op.LastProgressTick=now;
            return true;
        }

        internal static bool PermitExit(Pawn pawn)
        {
            if (pawn == null || pawn.Map == null || pawn.Dead || pawn.Downed) return true;
            if (AuthorizedExits.Contains(pawn.thingIDNumber)) return true;
            var controller=pawn.Map.GetComponent<RHFOperationController>();
            List<RHFOperation> ops=Operations(controller);
            if(ops==null)return true;
            for(int i=0;i<ops.Count;i++)
            {
                RHFOperation op=ops[i];
                if(!IsRescue(op))continue;
                bool fighter=op.Squad.Contains(pawn);
                bool freed=op.Freed.Contains(pawn);
                if(!fighter && !freed)continue;
                if(op.PodLaunchCompleted && pawn==op.Vip)return true;
                if(fighter && op.Phase==2)
                {
                    bool anyFreedStillOnMap=false;
                    foreach(Pawn rescued in op.Freed)
                        if(rescued!=null && !rescued.Dead && rescued.MapHeld==pawn.Map &&
                            !(op.PodLaunchCompleted && rescued==op.Vip))
                        { anyFreedStillOnMap=true; break; }
                    if(!anyFreedStillOnMap)return true;
                }
                if(fighter && pawn.health?.summaryHealth != null &&
                   pawn.health.summaryHealth.SummaryHealthPercent<0.20f)return true;
                States.GetValue(op,_=>new RHFRescueCommandStateV124()).LastDecision=
                    freed ? "пресечён самостоятельный выход пленника без сопровождающего" :
                    "пресечён самостоятельный выход спасателя до окончания операции";
                return false;
            }
            return true;
        }

        internal static string Diagnostic(Map map)
        {
            if(map==null)return "Нет карты.";
            var controller=map.GetComponent<RHFOperationController>();
            List<RHFOperation> ops=Operations(controller);
            if(ops==null || ops.Count==0)return "Нет активных операций RHF.";
            var sb=new StringBuilder(1500);
            sb.AppendLine("NJR — координатор спасения и отхода");
            for(int i=0;i<ops.Count;i++)
            {
                RHFOperation op=ops[i];
                if(!IsRescue(op))continue;
                var state=States.GetValue(op,_=>new RHFRescueCommandStateV124());
                Pawn target=Target(op,map);
                int lords=0, alive=0, exits=0;
                for(int j=0;j<op.Squad.Count;j++)
                {
                    Pawn p=op.Squad[j];
                    if(!ActivePawn(p,map))continue;
                    alive++;
                    if(p.GetLord()?.LordJob is RHFRescueLordJobV124)lords++;
                    if(p.jobs?.curJob?.def == JobDefOf.Goto)exits++;
                }
                sb.AppendLine("Операция: фаза="+op.Phase+", пленник="+
                    (target?.LabelShortCap??"уже освобождён/утрачен")+
                    ", спасателей на карте="+alive+", с командиром="+lords+
                    ", задачи Goto="+exits+", вывезено="+op.EscapedWithEscort+
                    ", сопровождающих="+op.Escorts.Count);
                sb.AppendLine("Текущий план: "+state.LastDecision);
                sb.AppendLine("Вход: "+(state.CurrentBarrier?.LabelShortCap??"не выбран")+
                    "; выход: "+op.ExitCell+"; успех/пролом/подход="+
                    state.Escorted+"/"+state.OrderedBreach+"/"+state.OrderedApproach+
                    "; ошибки приказов="+state.InvalidOrders);
                int shown=0;
                for(int j=0;j<op.Squad.Count;j++)
                {
                    Pawn p=op.Squad[j];
                    if(!ActivePawn(p,map))continue;
                    sb.AppendLine("  "+p.LabelShortCap+": "+
                        (p.jobs?.curJob?.def?.defName??"нет задачи")+
                        ", позиция="+p.Position+", Lord="+
                        (p.GetLord()?.LordJob?.GetType().Name??"нет"));
                    shown++;
                }
            }
            return sb.ToString();
        }
    }

    // Retired v0.12.3 pipeline: deployment is handled directly by RHFOperationController.
    internal static class RHFRescueDeploymentV124
    {
        [HarmonyPostfix]
        private static void Postfix(RHFOperationController controller, RHFOperation op,
            bool incidentSucceeded)
        { RHFRescueCommandV124.OnDeploy(controller,op,incidentSucceeded); }
    }

    // Retired v0.12.3 pipeline: operation controller refreshes mission Lords.
    internal static class RHFRescueOwnershipV124
    {
        [HarmonyPrefix]
        private static bool Prefix(RHFOperationController controller)
        { RHFRescueCommandV124.Refresh(controller); return false; }
    }

    // Retired v0.12.3 pipeline: mission controller calls Infiltrate directly.
    internal static class RHFRescueInfiltrationV124
    {
        [HarmonyPrefix]
        private static bool Prefix(RHFOperationController controller, RHFOperation op, int now)
        { RHFRescueCommandV124.Infiltrate(controller,op,now); return false; }
    }

    // Retired extraction intercept: controller explicitly calls RHF extraction.
    internal static class RHFRescueExtractionV124
    {
        [HarmonyPrefix]
        private static bool Prefix(RHFOperationController __instance, RHFOperation op, int now)
        { return !RHFRescueCommandV124.Extract(__instance,op,now); }
    }

    [HarmonyPatch(typeof(RHFRescueControlV123), "PermitExit")]
    internal static class RHFRescueExitV124
    {
        [HarmonyPrefix]
        private static bool Prefix(Pawn pawn, ref bool __result)
        { __result=RHFRescueCommandV124.PermitExit(pawn); return false; }
    }

    public static class RHFRescueDiagnosticsV124
    {
        [DebugAction("NJR - ТЕСТЫ | ДИАГНОСТИКА", "29 — Групповой командир и эвакуация RHF",
            actionType=DebugActionType.Action,allowedGameStates=AllowedGameStates.PlayingOnMap)]
        public static void Show()
        {
            Find.WindowStack.Add(new Dialog_MessageBox(
                RHFRescueCommandV124.Diagnostic(Find.CurrentMap)));
        }
    }
}
