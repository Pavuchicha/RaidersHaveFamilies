// Raiders Have Families 0.6.0 — bounded multi-party faction intervention system.
//  * Only scheduled operations have an active controller.
//  * A forced rescue uses an actual raiding party and in-map goals, never a fake letter.
//  * Every action has a bounded decision budget; no global pawn scans in idle time.
using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using UnityEngine;

namespace RaidersHaveFamilies
{
    public enum RHFOperationType { Rescue = 1, Vengeance = 2 }
    public enum RHFStrikeRole { Rescue = 1, Diversion = 2, Vengeance = 3 }

    // One actual vanilla raid per force/role. The operation owns all groups;
    // unrelated map raids are never enlisted. Capped at five parties for TPS.
    public sealed class RHFStrikeParty : IExposable
    {
        public Faction Faction;
        public int Role;
        public float Points;
        public bool Drop;
        public bool Siege;
        public bool Breach;
        public bool Attempted;
        public bool Succeeded;
        public int Members;
        // Consumables are paid for from this party's own points, not free bonuses.
        public float SupplyBudget;
        public float SupplySpent;
        public int BreachKits;
        public int Medics;
        public int UnarmedCount;
        public RHFStrikeParty() { }
        public RHFStrikeParty(Faction faction, RHFStrikeRole role, float points,
            bool drop = false, bool siege = false, bool breach = false)
        { Faction = faction; Role = (int)role; Points = points;
          Drop = drop; Siege = siege; Breach = breach; }
        public void ExposeData()
        {
            Scribe_References.Look(ref Faction, "faction");
            Scribe_Values.Look(ref Role, "role", 1);
            Scribe_Values.Look(ref Points, "points", 500f);
            Scribe_Values.Look(ref Drop, "drop", false);
            Scribe_Values.Look(ref Siege, "siege", false);
            Scribe_Values.Look(ref Breach, "breach", false);
            Scribe_Values.Look(ref Attempted, "attempted", false);
            Scribe_Values.Look(ref Succeeded, "succeeded", false);
            Scribe_Values.Look(ref Members, "members", 0);
            Scribe_Values.Look(ref SupplyBudget, "supplyBudget", 0f);
            Scribe_Values.Look(ref SupplySpent, "supplySpent", 0f);
            Scribe_Values.Look(ref BreachKits, "breachKits", 0);
            Scribe_Values.Look(ref Medics, "medics", 0);
            Scribe_Values.Look(ref UnarmedCount, "unarmed", 0);
        }
    }

    public sealed class RHFOperation : IExposable
    {
        public Faction Sponsor;
        public Faction Executor;
        public Faction SecondSponsor;
        public List<Pawn> Targets = new List<Pawn>();
        public List<Pawn> Squad = new List<Pawn>();
        public List<Pawn> Freed = new List<Pawn>();
        public List<Pawn> Diversion = new List<Pawn>();
        public List<Faction> Sponsors = new List<Faction>();
        public List<RHFStrikeParty> Parties = new List<RHFStrikeParty>();
        public List<RHFTacticalMember> TacticalMembers = new List<RHFTacticalMember>();
        public bool CombinedStrike;
        public bool SplitApplied;
        public bool Deployed;
        public int SuccessfulParties;
        public Pawn Vip;
        public int Type;
        public int SpawnTick;
        public int CreatedTick;
        public int ExpiryTick;
        public int Phase;           // 0 waiting, 1 breach/infiltrate, 2 exit, 3 pod construction, 4 complete
        public int PhaseStartTick;
        public int LastOrderTick;
        public int LastProgressTick;
        public int RerouteCount;
        public int BreachChargeTick;
        public int BreachChargesUsed;
        public IntVec3 BreachCell = IntVec3.Invalid;
        public Pawn BreachOwner;
        public bool TryPod;
        public bool PodSuppliesIssued;
        public Thing PodSteel;
        public bool PodReady;
        public bool SupportUsed;
        public float ThreatPoints;
        public IntVec3 ExitCell = IntVec3.Invalid;
        public IntVec3 PodCell = IntVec3.Invalid;
        public bool Finished;
        public bool DebugForced; // developer-mode test; does not change player settings

        public RHFOperation() { }
        public void ExposeData()
        {
            Scribe_References.Look(ref Sponsor, "sponsor");
            Scribe_References.Look(ref Executor, "executor");
            Scribe_References.Look(ref SecondSponsor, "secondSponsor");
            Scribe_Collections.Look(ref Targets, "targets", LookMode.Reference);
            Scribe_Collections.Look(ref Squad, "squad", LookMode.Reference);
            Scribe_Collections.Look(ref Freed, "freed", LookMode.Reference);
            Scribe_Collections.Look(ref Diversion, "diversion", LookMode.Reference);
            Scribe_Collections.Look(ref Sponsors, "sponsors", LookMode.Reference);
            Scribe_Collections.Look(ref Parties, "strikeParties", LookMode.Deep);
            Scribe_Collections.Look(ref TacticalMembers, "tacticalMembersV9", LookMode.Deep);
            Scribe_Values.Look(ref CombinedStrike, "combinedStrike", false);
            Scribe_Values.Look(ref SplitApplied, "splitApplied", false);
            Scribe_Values.Look(ref Deployed, "deployed", false);
            Scribe_Values.Look(ref SuccessfulParties, "successfulParties", 0);
            Scribe_References.Look(ref Vip, "vip");
            Scribe_Values.Look(ref Type, "type", 1);
            Scribe_Values.Look(ref SpawnTick, "spawnTick", 0);
            Scribe_Values.Look(ref CreatedTick, "createdTick", 0);
            Scribe_Values.Look(ref ExpiryTick, "expiryTick", 0);
            Scribe_Values.Look(ref Phase, "phase", 0);
            Scribe_Values.Look(ref PhaseStartTick, "phaseStartTick", 0);
            Scribe_Values.Look(ref LastOrderTick, "lastOrderTick", 0);
            Scribe_Values.Look(ref LastProgressTick, "lastProgressTick", 0);
            Scribe_Values.Look(ref RerouteCount, "rerouteCount", 0);
            Scribe_Values.Look(ref BreachChargeTick, "breachChargeTick", 0);
            Scribe_Values.Look(ref BreachChargesUsed, "breachChargesUsed", 0);
            Scribe_Values.Look(ref BreachCell, "breachCell", IntVec3.Invalid);
            Scribe_References.Look(ref BreachOwner, "breachOwner");
            Scribe_Values.Look(ref TryPod, "tryPod", false);
            Scribe_Values.Look(ref PodSuppliesIssued, "podSuppliesIssued", false);
            Scribe_References.Look(ref PodSteel, "podSteel");
            Scribe_Values.Look(ref PodReady, "podReady", false);
            Scribe_Values.Look(ref SupportUsed, "supportUsed", false);
            Scribe_Values.Look(ref ThreatPoints, "threatPoints", 0f);
            Scribe_Values.Look(ref ExitCell, "exitCell", IntVec3.Invalid);
            Scribe_Values.Look(ref PodCell, "podCell", IntVec3.Invalid);
            Scribe_Values.Look(ref Finished, "finished", false);
            Scribe_Values.Look(ref DebugForced, "debugForced", false);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (Targets == null) Targets = new List<Pawn>();
                if (Squad == null) Squad = new List<Pawn>();
                if (Freed == null) Freed = new List<Pawn>();
                if (Diversion == null) Diversion = new List<Pawn>();
                if (Sponsors == null) Sponsors = new List<Faction>();
                if (Parties == null) Parties = new List<RHFStrikeParty>();
                if (TacticalMembers == null) TacticalMembers = new List<RHFTacticalMember>();
                // Existing v0.3.x operations were already queued through the storyteller.
                // Do not create a duplicate raid when upgrading a saved game.
                if (Parties.Count == 0) Deployed = true;
            }
        }
    }

    public sealed class RHFOperationController : MapComponent
    {
        private const int MaxOperationsPerMap = 4;
        private const int MaxSquad = 96;
        private const int MaxTargets = 24;
        private const int DecisionInterval = 450;
        private const int MaxOrdersPerDecision = 14;
        private const int MaxOperationDuration = 180000;
        private const int MaxStrikeParties = 5;
        private RHFOperation activeOperation;
        private RHFStrikeParty activeParty;
        private List<RHFOperation> operations = new List<RHFOperation>();
        private int nextDecision;

        public RHFOperationController(Map map) : base(map) { }
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref operations, "rhfLiveOperations", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (operations == null) operations = new List<RHFOperation>();
                nextDecision = Find.TickManager.TicksGame + DecisionInterval;
            }
        }

        public bool Register(RHFOperation operation)
        {
            if (operation == null || operation.Executor == null || operations.Count >= MaxOperationsPerMap)
                return false;
            operations.Add(operation);
            nextDecision = Math.Min(nextDecision, operation.SpawnTick);
            return true;
        }

        // Debug inspection is computed only when clicked, never per tick.
        public string DebugStatus()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("RHF: активные спецоперации " + operations.Count + "/" + MaxOperationsPerMap);
            for (int i=0; i<operations.Count; i++)
            {
                RHFOperation op=operations[i];
                if (op==null) continue;
                sb.AppendLine("#" + (i+1) + " " +
                    (op.Type==(int)RHFOperationType.Rescue ? "СПАСЕНИЕ" : "МЕСТЬ") +
                    "; подготовка=" + (op.Deployed?"завершена":"ожидание") +
                    "; группы=" + op.SuccessfulParties + "/" + op.Parties.Count +
                    "; спасатели=" + op.Squad.Count + "; отвлекающие=" + op.Diversion.Count +
                    "; цели=" + op.Targets.Count + "; фракции-заявители=" + op.Sponsors.Count);
                for (int k=0;k<op.Parties.Count && k<MaxStrikeParties;k++)
                {
                    RHFStrikeParty party=op.Parties[k];
                    if (party==null) continue;
                    sb.AppendLine("  " + (party.Faction?.Name ?? "нет фракции") +
                        ": " + (RHFStrikeRole)party.Role +
                        ", принято=" + party.Succeeded +
                        ", пешек=" + party.Members + ", очки=" + Mathf.RoundToInt(party.Points));
                }
            }
            return sb.ToString();
        }

        public RHFTacticalMember TacticalMember(Pawn pawn)
        {
            if (pawn == null) return null;
            for (int i=0; i<operations.Count; i++)
            {
                RHFTacticalMember found=RHFTactical.Member(operations[i], pawn);
                if (found != null) return found;
            }
            return null;
        }

        public string TacticalDebugStatus()
        {
            var sb = new System.Text.StringBuilder(800);
            sb.AppendLine("RHF 0.9.0: аудит тактических ролей и реального снаряжения");
            sb.AppendLine("Управление отделениями: " + (RHFMod.Settings?.TacticalCombat == true));
            sb.AppendLine("Дополнительная экипировка: " + (RHFMod.Settings?.RoleEquipment == true));
            for (int i=0;i<operations.Count;i++)
            {
                RHFOperation op=operations[i];
                if (op == null) continue;
                sb.AppendLine("Операция " + (i+1) + "; бойцов с ролью=" + op.TacticalMembers.Count);
                for (int k=0;k<op.Parties.Count;k++)
                {
                    RHFStrikeParty party=op.Parties[k];
                    if (party==null) continue;
                    sb.AppendLine("• " + (party.Faction?.Name ?? "?") + ": " +
                        (RHFStrikeRole)party.Role + ", " + party.Members + " бойцов, " +
                        party.BreachKits + " комплектов взлома, " + party.Medics +
                        " санитаров, без оружия " + party.UnarmedCount + "; бюджет " +
                        Mathf.RoundToInt(party.SupplySpent) + "/" + Mathf.RoundToInt(party.SupplyBudget));
                }
                int shown=0;
                for (int j=0;j<op.TacticalMembers.Count && shown<12;j++)
                {
                    RHFTacticalMember m=op.TacticalMembers[j];
                    if (m?.Pawn == null) continue;
                    sb.AppendLine("  " + m.Pawn.LabelShortCap + " — " +
                        RHFTactical.RoleName(m.Role) + ", оружие=" + (!m.WeaponMissing) +
                        ", инструмент взлома=" + RHFTactical.HasBreachKit(m.Pawn));
                    shown++;
                }
                if (op.TacticalMembers.Count > shown) sb.AppendLine("  ...остальные по лимиту интерфейса");
            }
            return sb.ToString();
        }

        public int CancelDebugOperations()
        {
            int removed=0;
            for (int i=operations.Count-1;i>=0;i--)
                if (operations[i]!=null && operations[i].DebugForced)
                { operations.RemoveAt(i); removed++; }
            // Already arrived pawns remain on the map; this cancels only custom AI.
            return removed;
        }

        // Used only when scheduling a debug raid fails; otherwise remove finished records normally.
        public void CancelDebugOperation(RHFOperation operation)
        {
            if (operation != null && operation.DebugForced) operations.Remove(operation);
        }

        // A single explicit bound arrival prevents an unrelated raid of the same faction
        // from being confused with a rescue team. Zero allocation in ordinary ticks.
        public void ObserveSpawn(Pawn pawn)
        {
            if (pawn == null || !pawn.Spawned || pawn.Faction == null || pawn.Dead ||
                pawn.RaceProps == null || !pawn.RaceProps.Humanlike) return;
            if (activeOperation != null && activeParty != null &&
                pawn.Faction == activeParty.Faction)
            {
                List<Pawn> assigned = activeParty.Role == (int)RHFStrikeRole.Rescue ?
                    activeOperation.Squad : activeOperation.Diversion;
                if (assigned.Count < MaxSquad && !assigned.Contains(pawn))
                { assigned.Add(pawn); activeParty.Members++; RHFTactical.Register(activeOperation, activeParty, pawn); }
                return;
            }
            // Some arrival modes create transport pods immediately and spawn their
            // occupants a few ticks later. The party has a short, bounded bind window.
            int now = Find.TickManager.TicksGame;
            for (int i=0;i<operations.Count;i++)
            {
                RHFOperation operation=operations[i];
                if (operation==null || operation.Finished || !operation.Deployed ||
                    now < operation.SpawnTick || now>operation.SpawnTick+2400) continue;
                for (int k=0;k<operation.Parties.Count && k<MaxStrikeParties;k++)
                {
                    RHFStrikeParty delayed=operation.Parties[k];
                    if (delayed == null || !delayed.Succeeded || !delayed.Drop ||
                        delayed.Faction != pawn.Faction || delayed.Members >= MaxSquad) continue;
                    List<Pawn> wing = delayed.Role == (int)RHFStrikeRole.Rescue ?
                        operation.Squad : operation.Diversion;
                    if (!wing.Contains(pawn) && wing.Count < MaxSquad)
                    { wing.Add(pawn); delayed.Members++; RHFTactical.Register(operation, delayed, pawn); }
                    return;
                }
            }
            // Backward compatibility for saves made with the queued-raid v0.3.x API.
            for (int i=0; i<operations.Count; i++)
            {
                RHFOperation op = operations[i];
                if (op == null || op.Finished || op.Parties.Count != 0 ||
                    pawn.Faction != op.Executor || now < op.SpawnTick - 120 ||
                    now > op.SpawnTick + 3600 || op.Squad.Count >= MaxSquad) continue;
                if (!op.Squad.Contains(pawn)) op.Squad.Add(pawn);
                break;
            }
        }

        // Called synchronously by developer actions. No 150-tick queue and no
        // reliance on storyteller polling; reports when generation is rejected.
        public bool ForceDeployNow(RHFOperation op)
        {
            if (op == null || !operations.Contains(op)) return false;
            op.SpawnTick = Find.TickManager.TicksGame;
            bool result = Deploy(op);
            nextDecision = Find.TickManager.TicksGame;
            if (!result) CancelDebugOperation(op);
            else MapComponentTick();
            return result;
        }

        private IntVec3 RescueDropCenter(RHFOperation op, RHFStrikeParty party)
        {
            if (op?.Targets == null || party == null) return IntVec3.Invalid;
            // One bounded search per deployed drop squad, never per tick.
            for (int i = 0; i < op.Targets.Count && i < 24; i++)
            {
                Pawn prisoner = op.Targets[i];
                if (prisoner == null || !prisoner.Spawned || prisoner.Map != map ||
                    !prisoner.IsPrisonerOfColony) continue;
                RHFHostageRecord record = Current.Game?.GetComponent<RHFGameLedger>()?.FindHostage(prisoner);
                Faction owner = record?.OriginalFaction ?? prisoner.Faction;
                // In a coalition, a sponsoring faction aims at its OWN hostage.
                // A hired rescue contractor may target any listed sponsor's hostage.
                bool partyHasOwnCaptive = op.Sponsors != null &&
                    op.Sponsors.Contains(party.Faction);
                if (partyHasOwnCaptive && owner != party.Faction) continue;
                IntVec3 cell;
                if (DropCellFinder.TryFindDropSpotNear(prisoner.Position, map,
                    out cell, true, true) && cell.IsValid && cell.InBounds(map))
                    return cell;
            }
            // Do not fake a successful prison landing on an unsuitable map.
            return IntVec3.Invalid;
        }

        private bool Deploy(RHFOperation op)
        {
            if (op == null || op.Deployed) return op != null && op.SuccessfulParties > 0;
            op.Deployed = true;
            if (op.Parties == null || op.Parties.Count == 0) return false;
            int success = 0;
            for (int i=0; i<op.Parties.Count && i<MaxStrikeParties; i++)
            {
                RHFStrikeParty party = op.Parties[i];
                if (party == null || party.Faction == null || party.Attempted) continue;
                party.Attempted = true;
                activeOperation = op;
                activeParty = party;
                bool accepted = false;
                try
                {
                    // Fund all role tools from the same force budget; reserve at most
                    // twelve percent and never multiply cost by coalition size.
                    party.SupplyBudget = RHFMod.Settings?.RoleEquipment == true ?
                        Mathf.Min(300f, Mathf.Max(0f, party.Points * 0.12f)) : 0f;
                    var parms = new IncidentParms
                    { target = map, faction = party.Faction,
                      points = Mathf.Clamp(party.Points - party.SupplyBudget, 300f, 15000f) };
                    parms.raidStrategy = party.Siege ?
                        PickStrategy("Siege", "ImmediateAttackSmart", "ImmediateAttack") :
                        party.Breach ? PickStrategy("Breaching", "ImmediateAttackSmart", "ImmediateAttack") :
                        PickStrategy("ImmediateAttackSmart", "ImmediateAttack");
                    if (party.Drop)
                    {
                        parms.raidArrivalMode = DefDatabase<PawnsArrivalModeDef>.GetNamedSilentFail("CenterDrop");
                        if (party.Role == (int)RHFStrikeRole.Rescue)
                        {
                            // CenterDrop honors a valid spawnCenter. Pick an actual
                            // valid drop cell within reach of the prison rather than
                            // allowing the vanilla center-of-colony fallback.
                            IntVec3 nearPrison = RescueDropCenter(op, party);
                            if (nearPrison.IsValid && parms.raidArrivalMode != null)
                                parms.spawnCenter = nearPrison;
                            else
                                throw new InvalidOperationException("RHF: нет допустимой площадки " +
                                    "для десанта вблизи тюрьмы; рейд отменён, а не подменён " +
                                    "обычным десантом в центр колонии.");
                        }
                    }
                    // RaidEnemy.Worker.TryExecute fires *now*. The Harmony SpawnSetup
                    // hook binds each generated pawn to this specific party.
                    accepted = IncidentDefOf.RaidEnemy.Worker.TryExecute(parms);
                    // If a special strategy is invalid for this modded faction,
                    // retry a plain immediate assault only when no pawns spawned.
                    if (!accepted && party.Members == 0)
                    {
                        parms.raidStrategy = RaidStrategyDefOf.ImmediateAttack;
                        parms.raidArrivalMode = null;
                        accepted = IncidentDefOf.RaidEnemy.Worker.TryExecute(parms);
                    }
                }
                catch (Exception e)
                {
                    Log.Error("[RHF] Immediate raid failed for " + party.Faction.Name +
                        " role " + party.Role + ": " + e);
                }
                finally { activeOperation = null; activeParty = null; }
                party.Succeeded = accepted || party.Members > 0;
                if (party.Succeeded) success++;
                else Log.Warning("[RHF] RaidEnemy declined " + party.Faction.Name +
                    " (role=" + party.Role + ", points=" + party.Points +"). " +
                    "Inspect pawn group makers and raid restrictions for this faction.");
            }
            // Last-chance recovery for debug tests: some modded factions are
            // non-raiding and their RaidEnemy worker rejects all strategies.
            // Try another hostile executor instead of silently showing nothing.
            if (success == 0 && op.DebugForced && op.Parties.Count < MaxStrikeParties)
            {
                foreach (Faction alternate in Find.FactionManager.AllFactionsListForReading)
                {
                    if (alternate == null || alternate.IsPlayer || alternate.def == null ||
                        alternate.def.hidden || !alternate.HostileTo(Faction.OfPlayer) ||
                        alternate == op.Executor) continue;
                    bool tried=false;
                    for (int n=0;n<op.Parties.Count;n++)
                        if (op.Parties[n]!=null && op.Parties[n].Faction==alternate)
                        { tried=true; break; }
                    if (tried) continue;
                    RHFStrikeParty reserve=new RHFStrikeParty(alternate,
                        op.Type==(int)RHFOperationType.Rescue ? RHFStrikeRole.Rescue : RHFStrikeRole.Vengeance,
                        Mathf.Max(650f,op.ThreatPoints),breach: op.Type==(int)RHFOperationType.Rescue);
                    op.Parties.Add(reserve);
                    activeOperation=op;
                    activeParty=reserve;
                    try
                    {
                        reserve.SupplyBudget = RHFMod.Settings?.RoleEquipment == true ?
                            Mathf.Min(300f, reserve.Points * 0.12f) : 0f;
                        var parms=new IncidentParms { target=map, faction=alternate,
                            points=Mathf.Clamp(reserve.Points-reserve.SupplyBudget,650f,15000f),
                            raidStrategy=RaidStrategyDefOf.ImmediateAttack };
                        reserve.Attempted=true;
                        reserve.Succeeded=IncidentDefOf.RaidEnemy.Worker.TryExecute(parms);
                    }
                    catch(Exception e)
                    { Log.Warning("[RHF] Reserve raider " + alternate.Name + " failed: " + e.Message); }
                    finally { activeOperation=null;activeParty=null; }
                    if (reserve.Succeeded || reserve.Members>0)
                    {
                        reserve.Succeeded=true;
                        op.Executor=alternate;
                        success++;
                        Messages.Message("RHF: исходная фракция не смогла собрать рейд; " +
                            "операцию выполняет " + alternate.Name + ".",
                            MessageTypeDefOf.NeutralEvent,false);
                        break;
                    }
                    // Bounded: only one reserve candidate per manual test.
                    break;
                }
            }
            op.SuccessfulParties = success;
            if (op.Type == (int)RHFOperationType.Rescue && op.Squad.Count == 0 &&
                op.Diversion.Count > 2)
            {
                // A failed rescue wing can be replaced by a hired escort, but do not
                // commandeer all diversion troops or pretend the rescue succeeded.
                int n = Math.Max(2, op.Diversion.Count / 3);
                for (int i=0; i<n && op.Diversion.Count>2; i++)
                {
                    Pawn p = op.Diversion[op.Diversion.Count - 1];
                    op.Diversion.RemoveAt(op.Diversion.Count - 1);
                    op.Squad.Add(p);
                }
            }
            if (op.CombinedStrike) SplitRescueWing(op);
            if (op.Type == (int)RHFOperationType.Rescue && op.Squad.Count == 0 && success > 0)
                Log.Warning("[RHF] Rescue dispatched but no active on-map rescuers yet. " +
                    "Drop pods may still be opening.");
            if (success > 0)
            {
                var report = new System.Text.StringBuilder(500);
                report.AppendLine(op.Type == (int)RHFOperationType.Rescue ?
                    "ЦЕЛЬ ОПЕРАЦИИ: добраться до удерживаемых пленников, освободить их и вывести. " +
                    "Ударная группа не обязана грабить или разрушать всё поселение." :
                    "ЦЕЛЬ ОПЕРАЦИИ: наказать колонию за удержание или жестокое обращение с пленниками.");
                if (op.Type == (int)RHFOperationType.Rescue && op.Targets != null)
                {
                    report.AppendLine("Целевых пленников: " + op.Targets.Count + ".");
                    report.AppendLine("Для спасателей предусмотрены прорыв к тюрьме, вывод пленников и отход; " +
                        "отвлекающие группы ведут отдельный бой.");
                }
                for (int i=0; i<op.Parties.Count && i<MaxStrikeParties; i++)
                {
                    RHFStrikeParty party=op.Parties[i];
                    if (party == null || !party.Succeeded) continue;
                    string task=party.Role == (int)RHFStrikeRole.Rescue ? "освобождение" :
                        party.Role == (int)RHFStrikeRole.Diversion ? "отвлекающий штурм" : "месть";
                    report.AppendLine("• " + party.Faction.Name + ": " + task +
                        (party.Drop ? ", десантирование" : party.Breach ? ", прорыв" :
                        party.Siege ? ", осада" : ", наземное прибытие") +
                        "; сила около " + Mathf.RoundToInt(party.Points) + " очков." +
                        " Комплекты взлома: " + party.BreachKits + ", санитары: " + party.Medics +
                        ", без основного оружия: " + party.UnarmedCount + ".");
                }
                Find.LetterStack.ReceiveLetter("RHF: " +
                    (op.Type == (int)RHFOperationType.Rescue ? "спасательная операция" : "карательный рейд"),
                    report.ToString(), LetterDefOf.ThreatBig, new LookTargets(map.Center, map));
            }
            return success > 0;
        }

        private static RaidStrategyDef PickStrategy(params string[] names)
        {
            for (int i=0;i<names.Length;i++)
            {
                RaidStrategyDef strategy = DefDatabase<RaidStrategyDef>.GetNamedSilentFail(names[i]);
                if (strategy != null) return strategy;
            }
            return RaidStrategyDefOf.ImmediateAttack;
        }

        private void SplitRescueWing(RHFOperation op)
        {
            if (op.SplitApplied || op.Type != (int)RHFOperationType.Rescue) return;
            if (op.Diversion.Count != 0) { op.SplitApplied=true; return; }
            if (op.Squad.Count < 5) return;
            op.SplitApplied = true;
            // Keep >=3 dedicated rescuers. Non-rescuers retain their vanilla Lord
            // and pursue its hostile assault behavior as a distraction.
            int amount = Math.Min(op.Squad.Count / 3, op.Squad.Count - 3);
            for (int i=0;i<amount;i++)
            {
                int index = op.Squad.Count - 1 - i;
                Pawn p = op.Squad[index];
                op.Squad.RemoveAt(index);
                op.Diversion.Add(p);
            }
        }

        public override void MapComponentTick()
        {
            if (operations.Count == 0) return;
            // Test actions must run even if natural retaliation is disabled in settings.
            if (RHFMod.Settings?.Retaliation != true)
            {
                bool developerOperation = false;
                for (int j = 0; j < operations.Count; j++)
                    if (operations[j] != null && operations[j].DebugForced && !operations[j].Finished)
                    { developerOperation = true; break; }
                if (!developerOperation) return;
            }
            int now = Find.TickManager.TicksGame;
            if (now < nextDecision) return;
            nextDecision = now + DecisionInterval;
            for (int i=operations.Count-1; i>=0; i--)
            {
                RHFOperation op = operations[i];
                if (op == null || op.Finished || now > op.ExpiryTick ||
                    now - op.CreatedTick > MaxOperationDuration)
                { operations.RemoveAt(i); continue; }
                if (now >= op.SpawnTick && !op.Deployed && op.Parties.Count > 0)
                {
                    if (!Deploy(op))
                    {
                        Messages.Message("RHF: фракции не удалось собрать рейд. Проверьте журнал ошибок и тип фракции.",
                            MessageTypeDefOf.RejectInput, false);
                        operations.RemoveAt(i);
                        continue;
                    }
                }
                if (op.Type == (int)RHFOperationType.Vengeance)
                {
                    if (now >= op.SpawnTick && (op.Diversion.Count>0 || op.Squad.Count>0) && !op.SupportUsed)
                        DeployVengeanceSupport(op);
                    if (RHFMod.Settings?.TacticalCombat == true)
                        RHFTactical.PressureAssault(map, op, now);
                    if (now > op.SpawnTick + 7200) operations.RemoveAt(i);
                    continue; // vanilla Lord still controls ordinary assault formations
                }
                if (now < op.SpawnTick) continue;
                if (op.Phase == 0)
                {
                    if (op.Squad.Count == 0)
                    {
                        if (now > op.SpawnTick + 3800) { op.Finished = true; operations.RemoveAt(i); }
                        continue;
                    }
                    op.Phase = 1;
                    op.PhaseStartTick = now;
                    op.LastProgressTick = now;
                    if (op.ExitCell == IntVec3.Invalid) op.ExitCell = ChooseExit(op.Squad[0].Position);
                }
                if (op.CombinedStrike) SplitRescueWing(op);
                if (op.Phase == 1) Infiltrate(op, now);
                else if (op.Phase == 2) Extract(op, now);
                else if (op.Phase == 3) BuildAndLaunch(op, now);
                else if (op.Phase == 4) { op.Finished = true; operations.RemoveAt(i); }
            }
        }

        private void DeployVengeanceSupport(RHFOperation op)
        {
            op.SupportUsed=true;
            List<Pawn> attackers = op.Diversion.Count>0 ? op.Diversion : op.Squad;
            if(op.Executor?.def.techLevel>=TechLevel.Industrial || attackers.Count==0)
                return; // Higher-tech raids use vanilla strategy/arrival modes.
            PawnKindDef kind=DefDatabase<PawnKindDef>.GetNamedSilentFail("Warg") ??
                DefDatabase<PawnKindDef>.GetNamedSilentFail("Wolf");
            if(kind==null) return;
            Pawn leader=attackers[0];
            if(leader==null || leader.Map!=map)return;
            Lord lord=leader.GetLord();
            int desired=Mathf.Clamp(Mathf.RoundToInt(op.ThreatPoints/800f),2,8);
            int added=0;
            for(int i=0;i<desired;i++)
            {
                try
                {
                    Pawn animal=PawnGenerator.GeneratePawn(kind,op.Executor);
                    if(animal==null)continue;
                    IntVec3 cell=leader.Position+IntVec3.North;
                    if(!cell.InBounds(map) || !cell.Standable(map))cell=leader.Position;
                    GenSpawn.Spawn(animal,cell,map);
                    if(lord!=null)lord.AddPawn(animal);
                    added++;
                }
                catch(Exception e)
                {
                    Log.Warning("[Raiders Have Families] Faction combat animal rejected: " + e.Message);
                    break; // no retry storm with modded faction restrictions
                }
            }
            if(added>0)
                Messages.Message(op.Executor.Name + " выводит боевых животных вместе с рейдерами ("+
                    added+").",leader,MessageTypeDefOf.ThreatSmall,false);
        }

        private void Infiltrate(RHFOperation op, int now)
        {
            int orders = 0;
            int coverOrders = 0;
            Pawn target = FindCaptive(op);
            if (target == null)
            {
                op.Phase = 2; op.PhaseStartTick = now; return;
            }
            for (int i = op.Squad.Count-1; i>=0 && orders < MaxOrdersPerDecision; i--)
            {
                Pawn rescuer = op.Squad[i];
                if (rescuer == null || rescuer.Dead || rescuer.Map != map) { op.Squad.RemoveAt(i); continue; }
                if (rescuer.Downed) continue;
                if (RHFMod.Settings?.TacticalCombat == true && coverOrders < 2 &&
                    RHFTactical.TryCover(map, op, rescuer, target, now))
                { coverOrders++; orders++; continue; }
                // The rescue coordinator takes command away from the vanilla assault lord.
                // Only for the special response squad, never ordinary raids.
                Lord oldLord = rescuer.GetLord();
                if (oldLord != null) oldLord.Notify_PawnLost(rescuer, PawnLostCondition.LeftVoluntarily);
                if ((rescuer.Position - target.Position).LengthHorizontalSquared <= 5 &&
                    rescuer.CanReach(target, PathEndMode.Touch, Danger.Deadly))
                {
                    FreeTarget(op, target);
                    op.LastProgressTick = now;
                    continue;
                }
                if (rescuer.jobs?.curJob?.def == JobDefOf.Goto && now - op.LastOrderTick < 1800)
                    continue;
                if (rescuer.CanReach(target, PathEndMode.Touch, Danger.Deadly))
                {
                    Order(rescuer, JobDefOf.Goto, target);
                    orders++;
                }
                else
                {
                    Thing barrier = FindBarrier(target, rescuer);
                    if (barrier != null)
                    {
                        // After reaching a shielded prison wall, technological
                        // squads can use at most two controlled demolition charges.
                        bool planted=TryBreachCharge(op,target,barrier,rescuer,now);
                        if (!planted) Order(rescuer,JobDefOf.AttackMelee,barrier);
                        orders++;
                    }
                }
            }
            if (orders > 0) op.LastOrderTick = now;
            // Loss-sensitive withdrawal: a badly depleted rescue force must not
            // continue storming indefinitely; the diversion wing remains vanilla-led.
            if (now - op.PhaseStartTick >= 4200)
            {
                int capable = 0;
                for (int i=0;i<op.Squad.Count && i<MaxSquad;i++)
                {
                    Pawn fighter=op.Squad[i];
                    if (fighter!=null && !fighter.Dead && !fighter.Downed && fighter.Map==map)
                        capable++;
                }
                if (capable < Math.Max(1, op.Squad.Count / 4))
                { op.Phase = 2; op.PhaseStartTick=now; return; }
            }
            // If walls cannot be opened or the prison was rebuilt, do not freeze the raid forever.
            if (now - op.LastProgressTick > 18000)
            {
                op.RerouteCount++;
                op.LastProgressTick = now;
                if (op.RerouteCount >= 3)
                { op.Phase = 2; op.PhaseStartTick = now; }
            }
        }

        private Pawn FindCaptive(RHFOperation op)
        {
            // Priority target first, unless already freed or off-map.
            if (op.Vip != null && !op.Vip.Dead && op.Vip.Map == map &&
                op.Vip.IsPrisonerOfColony && !op.Freed.Contains(op.Vip)) return op.Vip;
            for (int i=0;i<op.Targets.Count && i<MaxTargets;i++)
            {
                Pawn p = op.Targets[i];
                if (p != null && !p.Dead && p.Map == map && p.IsPrisonerOfColony &&
                    !op.Freed.Contains(p)) return p;
            }
            return null;
        }

        private void FreeTarget(RHFOperation op, Pawn p)
        {
            if (p == null || p.guest == null || !p.IsPrisonerOfColony) return;
            // A breakout cancels ransom conditions. The player cannot collect payment.
            RHFHostageRecord record = Current.Game?.GetComponent<RHFGameLedger>()?.FindHostage(p);
            if (record != null) { record.Negotiated = false; record.PendingSilver = 0; record.PendingGoodwill = false; }
            p.guest.SetGuestStatus(null);
            if (!op.Freed.Contains(p)) op.Freed.Add(p);
            if (op.Freed.Count >= op.Targets.Count)
            {
                op.Phase = op.TryPod && op.Vip != null && op.Vip.Map == map ? 3 : 2;
                op.PhaseStartTick = Find.TickManager.TicksGame;
            }
        }

        private bool TryBreachCharge(RHFOperation op, Pawn captive, Thing barrier,
            Pawn rescuer, int now)
        {
            if (op == null || barrier == null || rescuer == null ||
                (barrier.Position-captive.Position).LengthHorizontalSquared > 49 ||
                (rescuer.Position-barrier.Position).LengthHorizontalSquared > 16 ||
                op.BreachChargesUsed >= 4 ||
                RHFMod.Settings?.RoleEquipment != true) return false;
            if (op.BreachChargeTick > 0 &&
                (op.BreachOwner == null || op.BreachOwner.Dead || op.BreachOwner.Map != map ||
                 !op.BreachCell.IsValid || op.BreachCell.GetEdifice(map) == null))
            {
                op.BreachChargeTick = 0;
                op.BreachCell = IntVec3.Invalid;
                op.BreachOwner = null;
            }
            RHFTacticalMember member = RHFTactical.Member(op, rescuer);
            if (member == null || member.Role != (int)RHFCombatRole.Breacher ||
                !RHFTactical.HasBreachKit(rescuer)) return false;
            if (op.BreachChargeTick == 0)
            {
                op.BreachChargeTick = now;
                op.BreachCell = barrier.Position;
                op.BreachOwner = rescuer;
                Messages.Message("Сапёр RHF устанавливает инструмент прорыва у преграды.",
                    barrier, MessageTypeDefOf.ThreatSmall, false);
                return true;
            }
            if (op.BreachCell != barrier.Position || op.BreachOwner != rescuer)
                return false; // Another operator must not consume this pawn's kit.
            if (now - op.BreachChargeTick < 750) return true;
            if (!RHFTactical.ConsumeBreachKit(rescuer))
            {
                op.BreachChargeTick = 0;
                op.BreachCell = IntVec3.Invalid;
                op.BreachOwner = null;
                return false;
            }
            // Primitive factions break the door manually. Industrial explosives are
            // forbidden right beside the captive to avoid killing the rescue target.
            bool industrial = rescuer.Faction?.def?.techLevel >= TechLevel.Industrial;
            bool safelyAway = (barrier.Position - captive.Position).LengthHorizontalSquared >= 7;
            if (industrial && safelyAway)
                GenExplosion.DoExplosion(op.BreachCell, map, 1.15f, DamageDefOf.Bomb, rescuer, 52);
            else
                barrier.TakeDamage(new DamageInfo(DamageDefOf.Blunt, 45f, 0f, -1f, rescuer));
            op.BreachChargesUsed++;
            op.BreachChargeTick = 0;
            op.BreachCell = IntVec3.Invalid;
            op.BreachOwner = null;
            op.LastProgressTick = now;
            return true;
        }

        private Thing FindBarrier(Pawn captive, Pawn rescuer)
        {
            // Local scan around a *known* prison room (at most 121 cells).
            // No whole-map path search or expensive room graph rebuild.
            Thing selected = null;
            int best = int.MaxValue;
            IntVec3 center = captive.Position;
            for (int dx=-5; dx<=5; dx++)
            for (int dz=-5; dz<=5; dz++)
            {
                IntVec3 c = new IntVec3(center.x + dx,0,center.z + dz);
                if (!c.InBounds(map)) continue;
                Building edifice = c.GetEdifice(map);
                if (edifice == null || edifice.Destroyed || edifice.Faction != Faction.OfPlayer ||
                    (edifice.def.passability != Traversability.Impassable &&
                     !(edifice is Building_Door))) continue;
                int score = (rescuer.Position-edifice.Position).LengthHorizontalSquared +
                    (center-edifice.Position).LengthHorizontalSquared*2;
                if (score < best) { best=score; selected=edifice; }
            }
            return selected;
        }

        private void Extract(RHFOperation op, int now)
        {
            int orderBudget = MaxOrdersPerDecision;
            for (int i=0;i<op.Freed.Count && orderBudget>0;i++)
            {
                Pawn p=op.Freed[i];
                if (p == null || p.Dead || p.MapHeld != map || !p.Spawned) continue;
                if (p.Downed)
                {
                    // An unconscious captive cannot obey a Goto order. An RHF
                    // escort must approach, carry and physically exit with them.
                    if (orderBudget > 0 && TryAssignCarrier(op, p)) orderBudget--;
                    continue;
                }
                if ((p.Position-op.ExitCell).LengthHorizontalSquared <= 10)
                { p.ExitMap(false, Rot4.Invalid); continue; }
                if (p.jobs?.curJob?.def != JobDefOf.Goto)
                { Order(p, JobDefOf.Goto, op.ExitCell); orderBudget--; }
            }
            // Keep a rearguard until living captives have crossed the map edge.
            bool allFree = true;
            for (int i=0;i<op.Freed.Count;i++)
                if (op.Freed[i] != null && !op.Freed[i].Dead && op.Freed[i].MapHeld == map)
                { allFree=false; break; }
            if (allFree || now - op.PhaseStartTick > 45000)
            {
                for(int i=0;i<op.Squad.Count && orderBudget>0;i++)
                {
                    Pawn rescuer=op.Squad[i];
                    if (rescuer == null || rescuer.Map != map || rescuer.Dead || rescuer.Downed) continue;
                    if ((rescuer.Position-op.ExitCell).LengthHorizontalSquared <= 10)
                        rescuer.ExitMap(false, Rot4.Invalid);
                    else if (rescuer.jobs?.curJob?.def != JobDefOf.Goto)
                    { Order(rescuer, JobDefOf.Goto, op.ExitCell); orderBudget--; }
                }
            }
            if (now - op.PhaseStartTick > 70000) op.Phase = 4;
        }

        private bool TryAssignCarrier(RHFOperation op, Pawn captive)
        {
            JobDef rescueJob = DefDatabase<JobDef>.GetNamedSilentFail("RHF_CarryDowned");
            if (rescueJob == null || captive == null || !captive.Spawned) return false;
            Pawn chosen=null;
            int best=int.MaxValue;
            // No full-map search and no more than 96 assigned rescuers inspected.
            for(int i=0;i<op.Squad.Count && i<96;i++)
            {
                Pawn pawn=op.Squad[i];
                if(pawn==null || pawn.Map!=map || pawn.Dead || pawn.Downed ||
                    pawn.carryTracker?.CarriedThing != null ||
                    pawn.health?.capacities == null ||
                    !pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation) ||
                    pawn.jobs == null) continue;
                if(pawn.jobs.curJob?.def == rescueJob) continue; // escort already busy
                RHFTacticalMember member = RHFTactical.Member(op,pawn);
                if(member != null && member.Role == (int)RHFCombatRole.Cover) continue;
                int distance=(pawn.Position-captive.Position).LengthHorizontalSquared;
                if(distance>=best || distance>2500) continue;
                if(!pawn.CanReach(captive,PathEndMode.ClosestTouch,Danger.Deadly))continue;
                chosen=pawn;best=distance;
            }
            if(chosen == null)return false;
            Job job=JobMaker.MakeJob(rescueJob,captive,op.ExitCell);
            job.count=1;
            chosen.jobs.TryTakeOrderedJob(job);
            return true;
        }

        // VIP rescue option: a field launcher is actually placed on the map; no free
        // out-of-nowhere teleport. Engineers must hold the area for several hours.
        // If the game cannot accept the cell/roof/resources, fall back to a breakout.
        private void BuildAndLaunch(RHFOperation op, int now)
        {
            Pawn vip=op.Vip;
            if (vip == null || vip.Dead || vip.Map != map || vip.Downed || op.Squad.Count < 2)
            { op.Phase=2; op.PhaseStartTick=now; return; }
            if (!op.PodSuppliesIssued)
            {
                op.PodCell = FindLandingCell(vip.Position);
                if (op.PodCell == IntVec3.Invalid)
                { op.Phase=2; op.PhaseStartTick=now; return; }
                // Supplies are part of the special raid's strategic points budget.
                // Explicit physical materials can be taken by the colony.
                Thing materials=ThingMaker.MakeThing(ThingDefOf.Steel);
                materials.stackCount=90;
                Thing placed;
                // Keep a reference to the supplied stack: never silently consume
                // unrelated colony steel when a cell is occupied or the stack moves.
                if (!GenPlace.TryPlaceThing(materials, op.PodCell, map,
                    ThingPlaceMode.Near, out placed))
                { op.Phase=2; op.PhaseStartTick=now; return; }
                op.PodSteel=placed;
                op.PodSuppliesIssued=true;
                op.PhaseStartTick=now;
                Find.LetterStack.ReceiveLetter("Полевой эвакуационный комплекс",
                    "Спасатели привезли материалы и готовят эвакуационную капсулу. " +
                    "Им понадобится время и свободное место без горной кровли.", LetterDefOf.ThreatSmall, vip);
            }
            // Engineers will not construct while everyone is incapacitated or gone.
            int active=0;
            for(int i=0;i<op.Squad.Count;i++)
                if (op.Squad[i]!=null && op.Squad[i].Map==map && !op.Squad[i].Downed &&
                    !op.Squad[i].Dead) active++;
            if(active<2 || now-op.PhaseStartTick<16500) return;
            if(op.PodReady)
            {
                // Boarding is physical; cannot launch a prisoner on the other side of the map.
                if((vip.Position-op.PodCell).LengthHorizontalSquared > 2)
                {
                    if(vip.jobs?.curJob?.def!=JobDefOf.Goto)
                        Order(vip,JobDefOf.Goto,op.PodCell);
                    if(now-op.PhaseStartTick > 36000)
                    { op.Phase=2;op.PhaseStartTick=now; }
                    return;
                }
                vip.ExitMap(false,Rot4.Invalid);
                Find.LetterStack.ReceiveLetter("Эвакуация важного пленника",
                    "Полевой комплекс запущен: " + vip.LabelShortCap + " эвакуирован.",
                    LetterDefOf.ThreatBig,vip);
                op.Phase=2; op.PhaseStartTick=now;
                return;
            }
            RoofDef roof=map.roofGrid.RoofAt(op.PodCell);
            if(roof == RoofDefOf.RoofRockThick)
            { op.Phase=2; op.PhaseStartTick=now; return; }
            if(roof!=null) map.roofGrid.SetRoof(op.PodCell,null);
            ThingDef pod=DefDatabase<ThingDef>.GetNamedSilentFail("RHF_ExtractionPod");
            ThingDef launcher=DefDatabase<ThingDef>.GetNamedSilentFail("RHF_FieldLauncher");
            if(pod==null || launcher==null || !op.PodCell.Standable(map))
            { op.Phase=2; op.PhaseStartTick=now; return; }
            // Spend only the materials delivered by this operation. If the player
            // took/moved the steel, the raiders must abandon the field launch.
            Thing supplied=op.PodSteel;
            if (supplied==null || supplied.Destroyed || supplied.Map!=map ||
                supplied.def!=ThingDefOf.Steel || supplied.stackCount<90 ||
                (supplied.Position-op.PodCell).LengthHorizontalSquared>16)
            { op.Phase=2; op.PhaseStartTick=now; return; }
            if (supplied.stackCount==90) supplied.Destroy();
            else supplied.stackCount-=90;
            op.PodSteel=null;
            IntVec3 adjacent=op.PodCell+IntVec3.East;
            if(!adjacent.InBounds(map) || !adjacent.Standable(map))
                adjacent=op.PodCell+IntVec3.West;
            if(!adjacent.InBounds(map) || !adjacent.Standable(map))
            { op.Phase=2; op.PhaseStartTick=now; return; }
            GenSpawn.Spawn(ThingMaker.MakeThing(pod),op.PodCell,map);
            GenSpawn.Spawn(ThingMaker.MakeThing(launcher),adjacent,map);
            op.PodReady=true;
            // Boarding happens in a subsequent decision cycle, after the VIP
            // reaches the physical pod. The same plan survives save/load.
        }

        private IntVec3 FindLandingCell(IntVec3 origin)
        {
            for (int radius=1; radius<=6; radius++)
            for (int x=-radius;x<=radius;x++)
            for (int z=-radius;z<=radius;z++)
            {
                IntVec3 c=new IntVec3(origin.x+x,0,origin.z+z);
                if(!c.InBounds(map) || !c.Standable(map))continue;
                if(map.roofGrid.RoofAt(c)==RoofDefOf.RoofRockThick)continue;
                return c;
            }
            return IntVec3.Invalid;
        }

        private IntVec3 ChooseExit(IntVec3 from)
        {
            int left=from.x, right=map.Size.x-from.x;
            int top=from.z, bottom=map.Size.z-from.z;
            if(left<=right && left<=top && left<=bottom)
                return new IntVec3(0,0,Mathf.Clamp(from.z,1,map.Size.z-2));
            if(right<=top && right<=bottom)
                return new IntVec3(map.Size.x-1,0,Mathf.Clamp(from.z,1,map.Size.z-2));
            if(top<=bottom)
                return new IntVec3(Mathf.Clamp(from.x,1,map.Size.x-2),0,0);
            return new IntVec3(Mathf.Clamp(from.x,1,map.Size.x-2),0,map.Size.z-1);
        }

        private static void Order(Pawn pawn, JobDef def, LocalTargetInfo target)
        {
            if (pawn?.jobs == null || def == null) return;
            Job job=JobMaker.MakeJob(def,target);
            pawn.jobs.TryTakeOrderedJob(job);
        }
    }
}
