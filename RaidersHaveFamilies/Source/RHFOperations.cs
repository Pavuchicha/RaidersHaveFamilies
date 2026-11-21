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
    // Components are orthogonal: entry via drop/breach/edge, objective rescue/
    // hunt/punish, exit by foot/pod, aftermath withdrawal/punitive assault.
    // Persist the resolved mission so reloading never rerolls its purpose.
    public enum RHFDoctrine
    {
        PrisonBreakout = 0, InfiltrationAndExit = 1, FortifiedVipExtraction = 2,
        RescueAndReprisal = 3, PunitiveAssault = 4, TargetedTraitorHunt = 5,
        SiegeAndRaze = 6, RescueThenAssault = 7
    }

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

    // Persistent, REAL escort assignments; liberated prisoners are not left
    // to the vanilla hostile pawn think tree after opening the prison door.
    public sealed class RHFEscortLink : IExposable
    {
        public Pawn Captive;
        public Pawn Escort;
        public RHFEscortLink() { }
        public RHFEscortLink(Pawn captive, Pawn escort)
        { Captive = captive; Escort = escort; }
        public void ExposeData()
        {
            Scribe_References.Look(ref Captive, "captive");
            Scribe_References.Look(ref Escort, "escort");
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
        public List<RHFEscortLink> Escorts = new List<RHFEscortLink>();
        public int EscapedWithEscort;
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
        public Thing PodSteelExtra;
        public Thing PodComponents;
        public Thing PodLauncherThing;
        public Thing PodVehicleThing;
        public IntVec3 PodLauncherCell = IntVec3.Invalid;
        public int PodConstructionStage;
        public int PodConstructionTick;
        public bool PodReady;
        public bool SupportUsed;
        public float ThreatPoints;
        public IntVec3 ExitCell = IntVec3.Invalid;
        public IntVec3 PodCell = IntVec3.Invalid;
        public bool Finished;
        public bool DebugForced; // developer-mode test; does not change player settings
        public string CaseKeyV10 = ""; // sealed victim disappearance or public atrocity
        public IntVec3 CaseSearchCellV10 = IntVec3.Invalid;
        // v0.12 campaign operation specification.
        public int Doctrine = (int)RHFDoctrine.PrisonBreakout;
        public Pawn HuntTarget;
        // Last actual line-of-sight confirmation, not omniscient pawn tracking.
        public IntVec3 HuntLastSeenCell = IntVec3.Invalid;
        public int HuntLastSeenTick;
        public int SourceImportance;
        public bool AssaultAfterObjective;
        public bool FortifyPrison;
        public int BarricadesBuilt;
        public int FortifyStartedTick;
        public IntVec3 BarricadeCell = IntVec3.Invalid;
        public List<IntVec3> CompletedBarricades = new List<IntVec3>();
        public int LastHuntOrderTick;
        public int LastInfrastructureOrderTick;
        public int RoofRemovalStartTick;
        public bool PodLaunchCompleted;
        public bool PodBudgetPaid;
        public float PodSupplyCost;

        public RHFOperation() { }
        public void ExposeData()
        {
            Scribe_References.Look(ref Sponsor, "sponsor");
            Scribe_References.Look(ref Executor, "executor");
            Scribe_References.Look(ref SecondSponsor, "secondSponsor");
            Scribe_Collections.Look(ref Targets, "targets", LookMode.Reference);
            Scribe_Collections.Look(ref Squad, "squad", LookMode.Reference);
            Scribe_Collections.Look(ref Freed, "freed", LookMode.Reference);
            Scribe_Collections.Look(ref Escorts, "rhfEscortsV11", LookMode.Deep);
            Scribe_Values.Look(ref EscapedWithEscort, "rhfEvacuatedV11", 0);
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
            Scribe_References.Look(ref PodSteelExtra, "rhfPodSteelExtraV130");
            Scribe_References.Look(ref PodComponents, "rhfPodComponentsV130");
            Scribe_References.Look(ref PodLauncherThing, "rhfVanillaLauncherV130");
            Scribe_References.Look(ref PodVehicleThing, "rhfVanillaPodV130");
            Scribe_Values.Look(ref PodLauncherCell, "rhfVanillaLauncherCellV130", IntVec3.Invalid);
            Scribe_Values.Look(ref PodConstructionStage, "rhfPodBuildStageV130", 0);
            Scribe_Values.Look(ref PodConstructionTick, "rhfPodBuildTimeV130", 0);
            Scribe_Values.Look(ref PodReady, "podReady", false);
            Scribe_Values.Look(ref SupportUsed, "supportUsed", false);
            Scribe_Values.Look(ref ThreatPoints, "threatPoints", 0f);
            Scribe_Values.Look(ref ExitCell, "exitCell", IntVec3.Invalid);
            Scribe_Values.Look(ref PodCell, "podCell", IntVec3.Invalid);
            Scribe_Values.Look(ref Finished, "finished", false);
            Scribe_Values.Look(ref DebugForced, "debugForced", false);
            Scribe_Values.Look(ref CaseKeyV10, "rhfCaseKeyV10", "");
            Scribe_Values.Look(ref CaseSearchCellV10, "rhfCaseSearchCellV10", IntVec3.Invalid);
            Scribe_Values.Look(ref Doctrine, "rhfDoctrineV12", (int)RHFDoctrine.PrisonBreakout);
            Scribe_References.Look(ref HuntTarget, "rhfHuntTargetV12");
            Scribe_Values.Look(ref HuntLastSeenCell, "rhfHuntLastSeenCellV121", IntVec3.Invalid);
            Scribe_Values.Look(ref HuntLastSeenTick, "rhfHuntLastSeenTickV121", 0);
            Scribe_Values.Look(ref SourceImportance, "rhfSourceImportanceV12", 0);
            Scribe_Values.Look(ref AssaultAfterObjective, "rhfAftermathAssaultV12", false);
            Scribe_Values.Look(ref FortifyPrison, "rhfFortifyPrisonV12", false);
            Scribe_Values.Look(ref BarricadesBuilt, "rhfBarricadesBuiltV12", 0);
            Scribe_Values.Look(ref FortifyStartedTick, "rhfFortifyStartedV12", 0);
            Scribe_Values.Look(ref BarricadeCell, "rhfBarricadeCellV12", IntVec3.Invalid);
            Scribe_Collections.Look(ref CompletedBarricades, "rhfFinishedBarricadesV12", LookMode.Value);
            Scribe_Values.Look(ref LastHuntOrderTick, "rhfLastHuntOrderV12", 0);
            Scribe_Values.Look(ref LastInfrastructureOrderTick,"rhfLastInfrastructureOrderV12",0);
            Scribe_Values.Look(ref RoofRemovalStartTick, "rhfRoofRemovalTickV12", 0);
            Scribe_Values.Look(ref PodLaunchCompleted, "rhfPodLaunchV12", false);
            Scribe_Values.Look(ref PodBudgetPaid, "rhfPodBudgetPaidV12", false);
            Scribe_Values.Look(ref PodSupplyCost, "rhfPodSupplyCostV12", 0f);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (Targets == null) Targets = new List<Pawn>();
                if (Squad == null) Squad = new List<Pawn>();
                if (Freed == null) Freed = new List<Pawn>();
                if (Escorts == null) Escorts = new List<RHFEscortLink>();
                if (Diversion == null) Diversion = new List<Pawn>();
                if (Sponsors == null) Sponsors = new List<Faction>();
                if (Parties == null) Parties = new List<RHFStrikeParty>();
                if (TacticalMembers == null) TacticalMembers = new List<RHFTacticalMember>();
                if (CompletedBarricades == null) CompletedBarricades = new List<IntVec3>();
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
        private const int DecisionInterval = 90;
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

        // Reuse a genuinely scheduled rescue for the disappeared VIP rather than
        // spawning a duplicate expedition when the order already existed.
        public RHFOperation FindRescueFor(string victimId)
        {
            if (string.IsNullOrEmpty(victimId)) return null;
            for (int i=0;i<operations.Count;i++)
            {
                RHFOperation op=operations[i];
                if (op==null || op.Finished || op.Type!=(int)RHFOperationType.Rescue ||
                    !string.IsNullOrEmpty(op.CaseKeyV10)) continue;
                if (op.Vip?.ThingID == victimId) return op;
                if (op.Targets == null) continue;
                for (int j=0;j<op.Targets.Count && j<MaxTargets;j++)
                    if (op.Targets[j]?.ThingID == victimId) return op;
            }
            return null;
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
                    "; цели=" + op.Targets.Count + "; сопровождающие пары=" + op.Escorts.Count +
                    "; успешно выведено=" + op.EscapedWithEscort +
                    "; доктрина=" + (RHFDoctrine)op.Doctrine +
                    "; охотятся за=" + (op.HuntTarget?.LabelShortCap ?? "нет") +
                    "; баррикады=" + op.BarricadesBuilt +
                    "; капсула запущена=" + op.PodLaunchCompleted +
                    "; бюджет полевой капсулы=" + op.PodSupplyCost +
                    "; фракции-заявители=" + op.Sponsors.Count);
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
            // A rescue is NOT an excuse to land on the VIP's head. Land outside
            // the detention room, at least six tiles from every named captive.
            // Airdropped squads then enter normally and may be intercepted.
            IntVec3 best = IntVec3.Invalid;
            int bestScore = int.MaxValue;
            for (int i=0; i<op.Targets.Count && i<MaxTargets;i++)
            {
                Pawn prisoner=op.Targets[i];
                if(prisoner==null || !prisoner.Spawned || prisoner.Map!=map ||
                    !(prisoner.IsPrisonerOfColony || prisoner.IsSlaveOfColony))continue;
                RHFHostageRecord record=Current.Game?.GetComponent<RHFGameLedger>()?.FindHostage(prisoner);
                Faction sponsor=record?.OriginalFaction ?? prisoner.Faction;
                bool partySponsor=op.Sponsors!=null && op.Sponsors.Contains(party.Faction);
                if(partySponsor && sponsor!=party.Faction)continue;
                Room prisonRoom=prisoner.Position.GetRoom(map);
                // Bounded, deterministic 27x27 annulus around the objective.
                for(int radius=14;radius<=22;radius++)
                for(int dx=-radius;dx<=radius;dx++)
                for(int dz=-radius;dz<=radius;dz++)
                {
                    if(Math.Max(Math.Abs(dx),Math.Abs(dz))!=radius)continue;
                    IntVec3 cell=new IntVec3(prisoner.Position.x+dx,0,prisoner.Position.z+dz);
                    if(!cell.InBounds(map) || !DropCellFinder.IsGoodDropSpot(cell,map,true,true) ||
                        map.roofGrid.RoofAt(cell)!=null ||
                        (prisonRoom!=null && cell.GetRoom(map)==prisonRoom))continue;
                    bool nearHostage=false;
                    for(int j=0;j<op.Targets.Count && j<MaxTargets;j++)
                        if(op.Targets[j]?.Map==map &&
                           (op.Targets[j].Position-cell).LengthHorizontalSquared<196)
                        {nearHostage=true;break;}
                    if(nearHostage)continue;
                    // Do not prioritize the first / nearest cell inside turret fire.
                    int danger=0, checkedTurrets=0;
                    foreach(Building building in map.listerBuildings.allBuildingsColonist)
                    {
                        if(++checkedTurrets>160)break;
                        if(RHFCombatPlannerV125.IsTurret(building) &&
                            (building.Position-cell).LengthHorizontalSquared<900 &&
                            GenSight.LineOfSight(building.Position,cell,map))danger+=1500;
                    }
                    int score=danger + (cell-prisoner.Position).LengthHorizontalSquared;
                    if(score<bestScore){best=cell;bestScore=score;}
                }
                if(best.IsValid)return best;
            }
            // No safe insertion: refuse the drop, never fake a spawn inside VIP.
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
                    float effectivePoints = party.Points - party.SupplyBudget;
                    if (op.TryPod && party.Role==(int)RHFStrikeRole.Rescue &&
                        !op.PodBudgetPaid)
                    {
                        // Genuine vanilla PodLauncher/TransportPod have separate
                        // Steel and ComponentIndustrial costs. Both are charged
                        // from this party's threat budget (and are placed physically).
                        ThingDef launcher=DefDatabase<ThingDef>.GetNamedSilentFail("PodLauncher");
                        ThingDef pod=DefDatabase<ThingDef>.GetNamedSilentFail("TransportPod");
                        int steel=FieldCost(launcher,ThingDefOf.Steel) +
                            FieldCost(pod,ThingDefOf.Steel) + (op.FortifyPrison?20:0);
                        int components=FieldCost(launcher,ThingDefOf.ComponentIndustrial) +
                            FieldCost(pod,ThingDefOf.ComponentIndustrial);
                        float cost=steel*ThingDefOf.Steel.BaseMarketValue+
                            components*ThingDefOf.ComponentIndustrial.BaseMarketValue;
                        if(launcher!=null && pod!=null && steel>0 && components>0 &&
                            effectivePoints>=cost+350f)
                        {effectivePoints-=cost;op.PodSupplyCost=cost;op.PodBudgetPaid=true;}
                        else
                        {
                            op.TryPod=false;op.FortifyPrison=false;
                            Log.Warning("[RHF] VIP field construction unavailable: vanilla defs, " +
                                "components or affordable resources missing. Land extraction selected.");
                        }
                    }
                    var parms = new IncidentParms
                    { target = map, faction = party.Faction,
                      points = Mathf.Clamp(effectivePoints, 300f, 15000f) };
                    // A vanilla breaching lord selects arbitrary colony walls and
                    // infrastructure. Rescue is objective-driven by RHF, never
                    // a generic Breaching raid, including during its first ticks.
                    parms.raidStrategy = op.Type == (int)RHFOperationType.Rescue ?
                        PickStrategy("ImmediateAttackSmart", "ImmediateAttack") :
                        party.Siege ? PickStrategy("Siege", "ImmediateAttackSmart", "ImmediateAttack") :
                        party.Breach ? PickStrategy("Breaching", "ImmediateAttackSmart", "ImmediateAttack") :
                        PickStrategy("ImmediateAttackSmart", "ImmediateAttack");
                    if (!party.Drop && op.Type == (int)RHFOperationType.Rescue)
                    {
                        // Do not let vanilla pick a random high-tech center-drop
                        // into the player's bedroom for a ground prison breakout.
                        parms.raidArrivalMode = DefDatabase<PawnsArrivalModeDef>.GetNamedSilentFail("EdgeWalkIn");
                        if (parms.raidArrivalMode == null)
                            throw new InvalidOperationException("RHF: EdgeWalkIn not available for rescue");
                    }
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
                    accepted = RHFMissionIntegrityV130.ExecuteRaidWithoutVanillaLetter(parms);
                    // If a special strategy is invalid for this modded faction,
                    // retry a plain immediate assault only when no pawns spawned.
                    if (!accepted && !party.Drop && party.Members == 0)
                    {
                        // A refused prison drop is NOT an ordinary center raid.
                        // Keep the selected arrival mode and insertion point.
                        parms.raidStrategy = RaidStrategyDefOf.ImmediateAttack;
                        if (!party.Drop && op.Type != (int)RHFOperationType.Rescue) parms.raidArrivalMode = null;
                        accepted = RHFMissionIntegrityV130.ExecuteRaidWithoutVanillaLetter(parms);
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
            // Do not dispatch a reserve raid while a drop-pod launch can still
            // be in flight but its occupants have not been spawned.
            bool arrivalStillPending=false;
            for(int x=0;x<op.Parties.Count;x++)
                if(op.Parties[x]!=null && op.Parties[x].Attempted && op.Parties[x].Drop)
                {arrivalStillPending=true;break;}
            // Last-chance recovery for debug tests: some modded factions are
            // non-raiding and their RaidEnemy worker rejects all strategies.
            // Try another hostile executor instead of silently showing nothing.
            if (success == 0 && !arrivalStillPending && op.DebugForced && op.Parties.Count < MaxStrikeParties)
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
                        reserve.Succeeded=RHFMissionIntegrityV130.ExecuteRaidWithoutVanillaLetter(parms);
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
                report.AppendLine("План операции: " + RHFMissionProfiles.Label(op) + ".");
                report.AppendLine("После основной задачи: " +
                    (op.AssaultAfterObjective ? "штурм и разрушение обороны" : "отход" ) + ".");
                if (op.HuntTarget!=null) report.AppendLine("Приоритетная цель: " +
                    op.HuntTarget.LabelShortCap + " (предполагаемый перебежчик).");
                report.AppendLine(op.Type == (int)RHFOperationType.Rescue ?
                    "ЦЕЛЬ ОПЕРАЦИИ: добраться до удерживаемых пленников, освободить их и вывести. " +
                    "Ударная группа не обязана грабить или разрушать всё поселение." :
                    "ЦЕЛЬ ОПЕРАЦИИ: наказать колонию за удержание или жестокое обращение с пленниками.");
                if (op.Type == (int)RHFOperationType.Rescue && op.Targets != null)
                {
                    report.AppendLine("Целевых пленников: " + op.Targets.Count + ".");
                    report.AppendLine("Спасатели обязаны создать пары сопровождения и вместе " +
                        "с освобождёнными отойти к доступному краю карты. " +
                        "Отвлекающая группа остаётся под управлением штурмового ИИ.");
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
                // RaidEnemy already issued an arrival letter with an actual raid
                // location. A second letter pointing at map.Center looks like a
                // duplicate or phantom raid. Keep the tactical report in the log.
                Log.Message("[RHF] " + report.ToString());
                RHFMissionIntegrityV130.ShowMissionLetter(op,map);
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
                        RHFReprisalSystem.OnOperationFailed(op);
                        Messages.Message("RHF: фракции не удалось собрать рейд. Проверьте журнал ошибок и тип фракции.",
                            MessageTypeDefOf.RejectInput, false);
                        operations.RemoveAt(i);
                        continue;
                    }
                    RHFReprisalSystem.OnOperationDeployed(op);
                }
                if (op.Type == (int)RHFOperationType.Vengeance)
                {
                    if (now >= op.SpawnTick && (op.Diversion.Count>0 || op.Squad.Count>0) && !op.SupportUsed)
                        DeployVengeanceSupport(op);
                    if (RHFMod.Settings?.TacticalCombat == true)
                    {
                        RHFTactical.PressureAssault(map, op, now);
                        if (op.HuntTarget != null) PursuePriorityTarget(op, now);
                        if (op.Doctrine==(int)RHFDoctrine.SiegeAndRaze ||
                            op.Doctrine==(int)RHFDoctrine.PunitiveAssault ||
                            (op.HuntTarget != null && op.AssaultAfterObjective))
                            AssaultStrategicStructures(op, now);
                    }
                    int vengeanceDuration=op.HuntTarget!=null ||
                        op.Doctrine==(int)RHFDoctrine.SiegeAndRaze ? 48000 : 18000;
                    if (now > op.SpawnTick + vengeanceDuration) operations.RemoveAt(i);
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
                if (!string.IsNullOrEmpty(op.CaseKeyV10) && op.Targets.Count == 0 &&
                    op.Type == (int)RHFOperationType.Rescue && op.Phase == 1)
                    SearchMissingVip(op, now);
                else if (op.Phase == 1) Infiltrate(op, now);
                else if (op.Phase == 2) Extract(op, now);
                else if (op.Phase == 3) BuildAndLaunch(op, now);
                else if (op.Phase == 5) PostExtractionAssault(op, now);
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

        private void SearchMissingVip(RHFOperation op, int now)
        {
            // Searchers physically advance toward the dead VIP's last known prison
            // position. No per-tick global room query, and at most 5 Goto orders.
            bool reached = false;
            int ordered = 0;
            for (int i=0; i<op.Squad.Count && i<MaxSquad && ordered<5; i++)
            {
                Pawn p = op.Squad[i];
                if (p == null || p.Dead || p.Downed || p.Map != map) continue;
                if (!op.CaseSearchCellV10.IsValid || !op.CaseSearchCellV10.InBounds(map))
                { reached = now >= op.SpawnTick + 2500; break; }
                if ((p.Position-op.CaseSearchCellV10).LengthHorizontalSquared <= 25)
                { reached = true; break; }
                if (p.CanReach(op.CaseSearchCellV10, PathEndMode.OnCell, Danger.Deadly) &&
                    p.jobs?.curJob?.def != JobDefOf.Goto)
                {
                    Lord oldLord = p.GetLord();
                    if (oldLord != null) oldLord.Notify_PawnLost(p, PawnLostCondition.LeftVoluntarily);
                    Order(p, JobDefOf.Goto, op.CaseSearchCellV10);
                    ordered++;
                }
            }
            // Fail-safe for surrounded/obliterated prison: an expedition eventually
            // establishes that the named prisoner is not there, not endless reorders.
            if (reached || now >= op.SpawnTick + 9000)
            {
                RHFReprisalSystem.OnSearchFailed(op);
                op.Phase = 2;
                op.PhaseStartTick = now;
            }
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
            // Resolve an access barrier once per bounded decision cycle, not
            // separately for every soldier. The prison-door preference is shared.
            Thing sharedBarrier = null;
            bool barrierProbed = false;
            // If we already liberated another prisoner, hold that person with
            // an escort until the rest of the group is ready. Otherwise the
            // vanilla released-pawn AI can walk into combat or escape alone.
            HoldFreedUntilExtraction(op);
            for (int i = op.Squad.Count-1; i>=0 && orders < MaxOrdersPerDecision; i--)
            {
                Pawn rescuer = op.Squad[i];
                if (rescuer == null || rescuer.Dead || rescuer.Map != map) { op.Squad.RemoveAt(i); continue; }
                if (rescuer.Downed) continue;
                // ALL rescue members leave vanilla assault command before any
                // cover/advance order. Otherwise a covering pawn may remain in
                // an assault lord and attack unrelated power infrastructure.
                Lord oldLord = rescuer.GetLord();
                if (oldLord != null) oldLord.Notify_PawnLost(rescuer, PawnLostCondition.LeftVoluntarily);
                if (RHFMod.Settings?.TacticalCombat == true && coverOrders < 2 &&
                    RHFTactical.TryCover(map, op, rescuer, target, now))
                { coverOrders++; orders++; continue; }
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
                    if (!barrierProbed)
                    {
                        sharedBarrier = FindBarrier(target, rescuer);
                        barrierProbed = true;
                    }
                    Thing barrier = sharedBarrier;
                    if (barrier != null && !barrier.Destroyed)
                    {
                        bool planted=TryBreachCharge(op,target,barrier,rescuer,now);
                        // Do not restart a melee attack job every 90 ticks:
                        // doing so can indefinitely reset the actual strike.
                        if (!planted && (rescuer.jobs?.curJob?.def != JobDefOf.AttackMelee ||
                            rescuer.jobs.curJob.targetA.Thing != barrier))
                            Order(rescuer,JobDefOf.AttackMelee,barrier);
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

        private void HoldFreedUntilExtraction(RHFOperation op)
        {
            int issued=0;
            for(int i=0;i<op.Freed.Count && i<MaxTargets && issued<3;i++)
            {
                Pawn captive=op.Freed[i];
                if(captive==null || captive.Dead || captive.Downed ||
                    !captive.Spawned || captive.Map!=map)continue;
                RHFEscortLink link=EnsureEscort(op,captive);
                Pawn escort=link?.Escort;
                if(escort==null || escort.Downed || !escort.Spawned ||
                    escort.Map!=map)continue;
                int gap=(captive.Position-escort.Position).LengthHorizontalSquared;
                if(gap>49)
                {
                    OrderGotoIfNeeded(captive,escort.Position,9);
                    issued++;
                }
                else if(gap<=49 && captive.jobs?.curJob?.def!=JobDefOf.Wait)
                {
                    // Do not let an old flee-to-edge Goto pull a prisoner
                    // away before the other cells have been cleared.
                    Job job=captive.jobs?.curJob;
                    bool leaving=job==null || job.def!=JobDefOf.Goto ||
                        (job.targetA.Cell-escort.Position).LengthHorizontalSquared>100;
                    if(leaving)
                    {
                        Order(captive,JobDefOf.Wait,captive.Position);
                        issued++;
                    }
                }
            }
        }

        private Pawn FindCaptive(RHFOperation op)
        {
            // Priority target first, unless already freed or off-map.
            if (op.Vip != null && !op.Vip.Dead && op.Vip.Map == map &&
                (op.Vip.IsPrisonerOfColony || op.Vip.IsSlaveOfColony) && !op.Freed.Contains(op.Vip)) return op.Vip;
            for (int i=0;i<op.Targets.Count && i<MaxTargets;i++)
            {
                Pawn p = op.Targets[i];
                if (p != null && !p.Dead && p.Map == map && (p.IsPrisonerOfColony || p.IsSlaveOfColony) &&
                    !op.Freed.Contains(p)) return p;
            }
            return null;
        }

        private void FreeTarget(RHFOperation op, Pawn p)
        {
            if (p == null || p.guest == null ||
                !(p.IsPrisonerOfColony || p.IsSlaveOfColony)) return;
            bool wasSlave = p.IsSlaveOfColony;
            // A breakout cancels ransom conditions. The player cannot collect payment.
            RHFHostageRecord record = Current.Game?.GetComponent<RHFGameLedger>()?.FindHostage(p);
            if (record != null) { record.Negotiated = false; record.PendingSilver = 0; record.PendingGoodwill = false; }
            p.guest.SetGuestStatus(null);
            // In RimWorld player slaves belong to the colony's faction; a
            // successful rescue restores their pre-enslavement allegiance.
            if (wasSlave && record?.OriginalFaction != null &&
                p.Faction == Faction.OfPlayer && !record.OriginalFaction.IsPlayer)
                p.SetFaction(record.OriginalFaction);
            if (!op.Freed.Contains(p)) op.Freed.Add(p);
            // Establish the escort BEFORE the former prisoner receives a free
            // think-tree decision. A still-active enemy lord must not commandeer it.
            EnsureEscort(op,p);
            Pawn next = FindCaptive(op);
            if (next == null) // dead/escaped/invalid targets do not block extraction
            {
                op.Phase = op.TryPod && op.Vip != null && op.Vip.Map == map ? 3 : 2;
                op.PhaseStartTick = Find.TickManager.TicksGame;
                if (!op.ExitCell.IsValid && op.Squad.Count > 0)
                    op.ExitCell = ChooseExit(op.Squad[0].Position);
                // Take an initial step together immediately instead of waiting
                // another 450 ticks while vanilla AI sends them different ways.
                if (!p.Downed)
                {
                    RHFEscortLink link=FindEscort(op,p);
                    if (link?.Escort != null)
                        OrderGotoIfNeeded(p,link.Escort.Position,9);
                }
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
            // Explosive entry is forbidden with the operator OR any friendly
            // soldier/captive in the 3-tile danger zone. A breaching tool then
            // strikes the identified barrier directly instead of suiciding.
            bool clearBlast = industrial && safelyAway &&
                (rescuer.Position-barrier.Position).LengthHorizontalSquared >= 9;
            if (clearBlast)
            {
                int checkedPawns = 0;
                foreach (Pawn nearby in map.mapPawns.AllPawnsSpawned)
                {
                    if (checkedPawns++ >= 256) { clearBlast=false; break; }
                    if (nearby == null || nearby.Dead || !nearby.Spawned) continue;
                    if ((nearby.Position-barrier.Position).LengthHorizontalSquared <= 9)
                    { clearBlast=false; break; }
                }
            }
            if (clearBlast)
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
            if (captive == null || rescuer == null || !captive.Spawned) return null;
            // Unlike the old 11x11 random-wall scan, search the detention
            // room's accessible perimeter (including a door up to 16 tiles
            // away). Prefer its door over any wall, even if the wall is closer.
            // Called at most once per 90-tick decision, not once per soldier.
            Room prison = captive.Position.GetRoom(map);
            Building bestBarrier = null;
            long bestScore = long.MaxValue;
            IntVec3 center = captive.Position;
            for (int dx=-16; dx<=16; dx++)
            for (int dz=-16; dz<=16; dz++)
            {
                IntVec3 c = new IntVec3(center.x+dx,0,center.z+dz);
                if (!c.InBounds(map)) continue;
                Building edifice = c.GetEdifice(map);
                if (!RHFCombatPlannerV125.IsBreachableStructure(edifice)) continue;
                if (!rescuer.CanReach(c, PathEndMode.Touch, Danger.Deadly)) continue;
                bool touchesPrison = prison == null || c.GetRoom(map) == prison;
                if (!touchesPrison)
                {
                    IntVec3 north=c+IntVec3.North, south=c+IntVec3.South;
                    IntVec3 east=c+IntVec3.East, west=c+IntVec3.West;
                    touchesPrison = (north.InBounds(map) && north.GetRoom(map)==prison) ||
                        (south.InBounds(map) && south.GetRoom(map)==prison) ||
                        (east.InBounds(map) && east.GetRoom(map)==prison) ||
                        (west.InBounds(map) && west.GetRoom(map)==prison);
                }
                if (!touchesPrison) continue;
                long score=(long)RHFCombatPlannerV125.BreachWorkCost(edifice) * 4 +
                    (rescuer.Position-c).LengthHorizontalSquared +
                    (center-c).LengthHorizontalSquared;
                if (score < bestScore) { bestScore=score; bestBarrier=edifice; }
            }
            return bestBarrier;
        }

        private RHFEscortLink FindEscort(RHFOperation op, Pawn captive)
        {
            if (op?.Escorts == null || captive == null) return null;
            for (int i=0;i<op.Escorts.Count;i++)
                if (op.Escorts[i]?.Captive == captive) return op.Escorts[i];
            return null;
        }

        private RHFEscortLink EnsureEscort(RHFOperation op, Pawn captive)
        {
            if (op == null || captive == null) return null;
            RHFEscortLink current=FindEscort(op,captive);
            if (current?.Escort != null && !current.Escort.Dead &&
                !current.Escort.Downed && current.Escort.Map == map) return current;
            Pawn chosen=null;
            int best=int.MaxValue;
            for (int i=0;i<op.Squad.Count && i<MaxSquad;i++)
            {
                Pawn rescuer=op.Squad[i];
                if (rescuer == null || rescuer.Dead || rescuer.Downed || rescuer.Map != map ||
                    rescuer.Faction == null || captive.Faction == null ||
                    (rescuer.Faction.HostileTo(captive.Faction) &&
                     !RHFMissionIntegrityV130.MayEscort(op,rescuer,captive))) continue;
                RHFTacticalMember member=RHFTactical.Member(op,rescuer);
                int dist=(rescuer.Position-captive.Position).LengthHorizontalSquared;
                int score=dist + (member?.Role == (int)RHFCombatRole.Escort ? -36 : 0);
                // Prefer a dedicated escort and avoid assigning the same escort
                // to several living prisoners when alternatives exist.
                for (int k=0;k<op.Escorts.Count;k++)
                    if (op.Escorts[k]?.Escort==rescuer && op.Escorts[k].Captive!=captive)
                        score+=25000;
                if (score>=best) continue;
                best=score; chosen=rescuer;
            }
            if (chosen==null) return null;
            if (current == null)
            { current=new RHFEscortLink(captive,chosen); op.Escorts.Add(current); }
            else current.Escort=chosen;
            // Never detach a rescuer from the mission Lord just because he
            // was assigned to escort a prisoner. Coalition mercenaries remain
            // mission allies even when their persistent factions are hostile.
            Lord lord = chosen.GetLord();
            if (lord != null && !(lord.LordJob is RHFRescueLordJobV124))
                lord.Notify_PawnLost(chosen,PawnLostCondition.LeftVoluntarily);
            return current;
        }

        private static void OrderGotoIfNeeded(Pawn pawn, IntVec3 cell, int toleranceSquared=4)
        {
            if (pawn?.jobs == null || !pawn.Spawned || !cell.IsValid || pawn.Downed) return;
            if ((pawn.Position-cell).LengthHorizontalSquared <= toleranceSquared) return;
            if (pawn.jobs.curJob?.def == JobDefOf.Goto &&
                (pawn.jobs.curJob.targetA.Cell-cell).LengthHorizontalSquared <= toleranceSquared)
                return;
            Order(pawn,JobDefOf.Goto,cell);
        }

        private void Extract(RHFOperation op, int now)
        {
            int orderBudget=MaxOrdersPerDecision;
            int stillOnMap=0;
            bool waitingForAny=false;
            if (!op.ExitCell.IsValid || !op.ExitCell.InBounds(map))
                op.ExitCell = ChooseExit(op.Squad.Count>0 && op.Squad[0]!=null ?
                    op.Squad[0].Position : map.Center);
            for (int i=0;i<op.Freed.Count && i<MaxTargets;i++)
            {
                Pawn captive=op.Freed[i];
                // FlyShipLeaving keeps its real cargo in a ThingOwner while the
                // pod is still visible on THIS map. MapHeld==map is therefore
                // not proof the VIP remains available for a ground escort.
                if (op.PodLaunchCompleted && captive == op.Vip) continue;
                if (captive == null || captive.Dead || captive.MapHeld != map) continue;
                stillOnMap++;
                RHFEscortLink assigned=EnsureEscort(op,captive);
                Pawn escort=assigned?.Escort;
                if (captive.Downed)
                {
                    if (captive.Spawned && orderBudget>0 && TryAssignCarrier(op,captive))
                        orderBudget--;
                    waitingForAny=true;
                    continue;
                }
                if (!captive.Spawned) { waitingForAny=true; continue; }
                if (escort == null || !escort.Spawned)
                {
                    // No valid rescuer: NEVER show this as a successful escorted
                    // extraction. Let the captive seek the exit but keep the
                    // operation active for a possible replacement escort.
                    if (orderBudget>0) { OrderGotoIfNeeded(captive,op.ExitCell); orderBudget--; }
                    waitingForAny=true;
                    continue;
                }
                int separation=(escort.Position-captive.Position).LengthHorizontalSquared;
                if ((captive.Position-op.ExitCell).LengthHorizontalSquared<=10 &&
                    (escort.Position-op.ExitCell).LengthHorizontalSquared<=36 && separation<=64)
                {
                    captive.ExitMap(false,Rot4.Invalid);
                    op.EscapedWithEscort++;
                    // The assigned rescuer leaves only after the prisoner is
                    // physically off the map. Other rescuers form a rear guard.
                    if (escort.Spawned) escort.ExitMap(false,Rot4.Invalid);
                    continue;
                }
                if (separation > 144)
                {
                    // Prisoner and escort were separated by their vanilla AI.
                    // Both must rendezvous before the escape continues.
                    if (orderBudget>0) { OrderGotoIfNeeded(captive,escort.Position,16); orderBudget--; }
                    if (orderBudget>0 && escort.jobs != null)
                    { Order(escort,JobDefOf.Wait,escort.Position); orderBudget--; }
                    waitingForAny=true;
                }
                else
                {
                    if (orderBudget>0) { OrderGotoIfNeeded(captive,op.ExitCell); orderBudget--; }
                    // An escort stays alongside the released prisoner instead
                    // of sprinting ahead, fighting and abandoning their charge.
                    if (orderBudget>0)
                    {
                        if (separation > 49)
                            Order(escort,JobDefOf.Wait,escort.Position);
                        else OrderGotoIfNeeded(escort,op.ExitCell);
                        orderBudget--;
                    }
                }
                op.LastProgressTick=now;
            }
            // Rear guard moves only when ALL living freed prisoners have departed.
            bool allFree=stillOnMap==0;
            if (allFree)
            {
                if (op.AssaultAfterObjective)
                {
                    op.Phase=5; op.PhaseStartTick=now;
                    Messages.Message("RHF: спасённые покинули карту. Оставшиеся штурмовики " +
                        "переходят к карательной фазе.",MessageTypeDefOf.ThreatSmall,false);
                    return;
                }
                for(int i=0;i<op.Squad.Count && orderBudget>0;i++)
                {
                    Pawn rescuer=op.Squad[i];
                    if (rescuer==null || rescuer.Dead || rescuer.Downed || rescuer.Map!=map) continue;
                    if ((rescuer.Position-op.ExitCell).LengthHorizontalSquared<=10)
                        rescuer.ExitMap(false,Rot4.Invalid);
                    else { OrderGotoIfNeeded(rescuer,op.ExitCell); orderBudget--; }
                }
            }
            else if (waitingForAny && now-op.PhaseStartTick>30000 && op.ExitCell.IsValid)
            {
                // A blocked/unreachable map exit must not strand liberated
                // pawns indefinitely. Recompute a physically reachable edge.
                Pawn point=null;
                for(int i=0;i<op.Freed.Count;i++)
                    if(op.Freed[i]!=null && op.Freed[i].Spawned && op.Freed[i].Map==map)
                    { point=op.Freed[i]; break; }
                if(point!=null) op.ExitCell=ChooseExit(point.Position);
            }
            if (now-op.PhaseStartTick>90000) op.Phase=4;
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

        // A small self-contained building phase: choose a real doorway-adjacent
        // standable tile in the prison room, send an actual breacher there,
        // spend ten units of field-delivered steel, and only then spawn a wall.
        // No global construction AI override; at most 2 tiles and ~100 probes.
        private void TryBuildPrisonBarricade(RHFOperation op, Pawn vip, int now)
        {
            if(!HasFieldSteel(op, FieldCost(
                   DefDatabase<ThingDef>.GetNamedSilentFail("PodLauncher"),ThingDefOf.Steel)+
                   FieldCost(DefDatabase<ThingDef>.GetNamedSilentFail("TransportPod"),ThingDefOf.Steel)+10) ||
               vip==null || !vip.Spawned)return;
            if(!op.BarricadeCell.IsValid)
            {
                Room room=vip.Position.GetRoom(map);
                if(room==null)return;
                for(int dx=-5;dx<=5 && !op.BarricadeCell.IsValid;dx++)
                for(int dz=-5;dz<=5 && !op.BarricadeCell.IsValid;dz++)
                {
                    IntVec3 doorCell=new IntVec3(vip.Position.x+dx,0,vip.Position.z+dz);
                    if(!doorCell.InBounds(map) || !(doorCell.GetEdifice(map) is Building_Door))continue;
                    for(int direction=0;direction<4;direction++)
                    {
                        IntVec3 adjacent=doorCell+(direction==0?IntVec3.North:
                            direction==1?IntVec3.East:direction==2?IntVec3.South:IntVec3.West);
                        if(!adjacent.InBounds(map) || !adjacent.Standable(map) ||
                           adjacent.GetRoom(map)!=room ||
                           adjacent.GetEdifice(map)!=null ||
                           adjacent.GetFirstBuilding(map)!=null ||
                           (adjacent-vip.Position).LengthHorizontalSquared<=2 ||
                           (adjacent-op.PodCell).LengthHorizontalSquared<=2)continue;
                        bool used=false;
                        for(int x=0;x<op.CompletedBarricades.Count;x++)
                            if(op.CompletedBarricades[x]==adjacent){used=true;break;}
                        if(used)continue;
                        op.BarricadeCell=adjacent;
                        op.FortifyStartedTick=0;
                        break;
                    }
                }
                if(!op.BarricadeCell.IsValid)return; // No door, no invented wall.
            }
            Pawn engineer=null;
            for(int i=0;i<op.Squad.Count && i<32;i++)
            {
                Pawn p=op.Squad[i];
                if(p==null || p.Dead || p.Downed || p.Map!=map || p.jobs==null ||
                   p.health?.capacities==null ||
                   RHFTactical.Member(op,p)?.Role != (int)RHFCombatRole.Breacher ||
                   !p.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation) ||
                   !p.CanReach(op.BarricadeCell,PathEndMode.Touch,Danger.Deadly))continue;
                if(engineer==null || (p.Position-op.BarricadeCell).LengthHorizontalSquared<
                    (engineer.Position-op.BarricadeCell).LengthHorizontalSquared)engineer=p;
            }
            if(engineer==null) return;
            if((engineer.Position-op.BarricadeCell).LengthHorizontalSquared>4)
            {
                OrderGotoIfNeeded(engineer,op.BarricadeCell,2);
                op.FortifyStartedTick=0;
                return;
            }
            if(op.FortifyStartedTick==0)op.FortifyStartedTick=now;
            if(now-op.FortifyStartedTick<1350)
            {
                if(engineer.jobs?.curJob?.def!=JobDefOf.Wait)
                    Order(engineer,JobDefOf.Wait,engineer.Position);
                return;
            }
            // Abort if the tile was filled during construction. Do not destroy
            // player structures or spawn through a prisoner.
            if(!op.BarricadeCell.Standable(map) ||
               op.BarricadeCell.GetFirstBuilding(map)!=null ||
               (vip.Position-op.BarricadeCell).LengthHorizontalSquared<=2)
            { op.BarricadeCell=IntVec3.Invalid;op.FortifyStartedTick=0;return; }
            try
            {
                Thing wall=ThingMaker.MakeThing(ThingDefOf.Wall,ThingDefOf.Steel);
                wall.SetFaction(engineer.Faction);
                GenSpawn.Spawn(wall,op.BarricadeCell,map);
                SpendFieldSteel(op,10);
                op.CompletedBarricades.Add(op.BarricadeCell);
                op.BarricadesBuilt++;
                Messages.Message("RHF: сапёр возвёл стальную баррикаду у тюремного входа.",
                    wall,MessageTypeDefOf.ThreatSmall,false);
            }
            catch(Exception e){Log.Warning("[RHF] Fortification aborted: "+e.Message);}
            op.BarricadeCell=IntVec3.Invalid;op.FortifyStartedTick=0;
        }

        // The faction is hunting a *known person*, but does not have permanent
        // vision through walls, fog and mountains. An RHF scout has to observe
        // the target; only then can the squad attack or pursue its last sighting.
        private void PursuePriorityTarget(RHFOperation op,int now)
        {
            Pawn target=op.HuntTarget;
            if(target==null || target.Dead || target.Map!=map ||
                target.Faction!=Faction.OfPlayer || now-op.LastHuntOrderTick<900) return;
            List<Pawn> attackers=op.Diversion.Count>0?op.Diversion:op.Squad;
            int huntLimit=op.Diversion.Count==0 && attackers.Count>=6 ?
                attackers.Count/2 : attackers.Count;
            int scoutsChecked=0;
            bool contact=false;
            for(int i=0;i<huntLimit && i<MaxSquad && scoutsChecked<16;i++)
            {
                Pawn scout=attackers[i];
                if(scout==null || scout.Dead || scout.Downed || scout.Map!=map)continue;
                scoutsChecked++;
                if((scout.Position-target.Position).LengthHorizontalSquared>900)continue;
                if(target.Position.Fogged(map))continue;
                if(!GenSight.LineOfSight(scout.Position,target.Position,map))continue;
                contact=true;
                op.HuntLastSeenCell=target.Position;
                op.HuntLastSeenTick=now;
                break;
            }
            // Stale last-known information expires. No magical pursuit of a
            // teleported target; absent contact squads search the settlement.
            bool freshContact=op.HuntLastSeenCell.IsValid &&
                now-op.HuntLastSeenTick<=9000;
            IntVec3 waypoint=freshContact ? op.HuntLastSeenCell : map.Center;
            int issued=0;
            for(int i=0;i<huntLimit && i<MaxSquad && issued<4;i++)
            {
                Pawn hunter=attackers[i];
                if(hunter==null || hunter.Dead || hunter.Downed || hunter.Map!=map ||
                   hunter.jobs==null || hunter==target)continue;
                int d=(hunter.Position-target.Position).LengthHorizontalSquared;
                bool hunterSees=contact && d<=900 && !target.Position.Fogged(map) &&
                    GenSight.LineOfSight(hunter.Position,target.Position,map);
                if(hunterSees && d<400 &&
                    hunter.equipment?.Primary?.def?.IsRangedWeapon==true)
                {
                    Order(hunter,JobDefOf.AttackStatic,target);
                    issued++;
                }
                else if(hunterSees && hunter.CanReach(target,PathEndMode.Touch,Danger.Deadly))
                {
                    Order(hunter,JobDefOf.Goto,target);
                    issued++;
                }
                else if((hunter.Position-waypoint).LengthHorizontalSquared>64 &&
                    hunter.CanReach(waypoint,PathEndMode.OnCell,Danger.Deadly))
                {
                    OrderGotoIfNeeded(hunter,waypoint,16);
                    issued++;
                }
            }
            if(issued>0)op.LastHuntOrderTick=now;
        }

        // Assault orders come from a bounded shortlist of ACTUAL player
        // infrastructure. Targeting power/defences/production makes vengeance
        // meaningfully different from a standard hostile raid; no fabricated
        // demolition or generic colony-wide search on every pawn tick.
        private void AssaultStrategicStructures(RHFOperation op, int now)
        {
            if (op == null || now - op.LastInfrastructureOrderTick < 1250) return;
            Building objective=null;
            int bestScore=int.MinValue;
            int inspected=0;
            var buildings=map.listerBuildings.allBuildingsColonist;
            for(int i=0;i<buildings.Count && inspected<48;i++)
            {
                Building b=buildings[i];
                if(b==null || !b.Spawned || b.Destroyed || b.Faction!=Faction.OfPlayer)continue;
                inspected++;
                string name=b.def.defName.ToLowerInvariant();
                int value=name.Contains("turret")?1000:
                    name.Contains("power") || name.Contains("generator")?850:
                    name.Contains("battery") || name.Contains("comms")?750:
                    name.Contains("workbench") || name.Contains("fabricator")?500:100;
                if(value>bestScore){objective=b;bestScore=value;}
            }
            if(objective==null)return;
            // The specialist diversion pursues the traitor; other fighters
            // separately dismantle the colony. Share actual force/budget.
            List<Pawn> attackers=op.Squad.Count>0?op.Squad:op.Diversion;
            int first=op.HuntTarget!=null && op.Diversion.Count==0 &&
                attackers.Count>=6 ? attackers.Count/2 : 0;
            int ordered=0;
            for(int i=first;i<attackers.Count && ordered<3;i++)
            {
                Pawn fighter=attackers[i];
                if(fighter==null || fighter.Dead || fighter.Downed || fighter.Map!=map ||
                   fighter.jobs==null)continue;
                int d=(fighter.Position-objective.Position).LengthHorizontalSquared;
                if(d>900)continue;
                if(d<420 && fighter.equipment?.Primary?.def?.IsRangedWeapon==true)
                    Order(fighter,JobDefOf.AttackStatic,objective);
                else if(fighter.CanReach(objective,PathEndMode.Touch,Danger.Deadly))
                    Order(fighter,JobDefOf.AttackMelee,objective);
                else continue;
                ordered++;
            }
            if(ordered>0)op.LastInfrastructureOrderTick=now;
        }

        private void PostExtractionAssault(RHFOperation op,int now)
        {
            if(now-op.PhaseStartTick>24000){op.Phase=4;return;}
            // Existing rescued soldiers, not extra free enemies, now attack.
            // Cap target orders and let the rest use original CE/vanilla verbs.
            int orders=0;
            for(int i=0;i<op.Squad.Count && i<MaxSquad && orders<5;i++)
            {
                Pawn fighter=op.Squad[i];
                if(fighter==null || fighter.Map!=map || fighter.Dead || fighter.Downed ||
                   fighter.jobs==null)continue;
                Pawn enemy=null;int best=625;int seen=0;
                foreach(Pawn candidate in map.mapPawns.FreeColonistsSpawned)
                {
                    if(seen++>=24)break;
                    if(candidate==null || candidate.Dead || candidate.Downed)continue;
                    int d=(fighter.Position-candidate.Position).LengthHorizontalSquared;
                    if(d<best){best=d;enemy=candidate;}
                }
                if(enemy!=null)
                {
                    if(best<400 && fighter.equipment?.Primary?.def?.IsRangedWeapon==true)
                        Order(fighter,JobDefOf.AttackStatic,enemy);
                    else if(fighter.CanReach(enemy,PathEndMode.Touch,Danger.Deadly))
                        Order(fighter,JobDefOf.Goto,enemy);
                    orders++;
                }
            }
            if(op.Doctrine==(int)RHFDoctrine.RescueAndReprisal ||
               op.Doctrine==(int)RHFDoctrine.RescueThenAssault)
                AssaultStrategicStructures(op,now);
            if(orders==0 && now-op.PhaseStartTick>8000)op.Phase=4;
        }

        // Native building definitions, cost lists and physical VIP cargo.
        // Materials, work intervals and the structures are all saved on the op.
        private static int FieldCost(ThingDef building, ThingDef material)
        {
            if(building?.costList==null || material==null)return 0;
            int cost=0;
            foreach(ThingDefCountClass component in building.costList)
                if(component?.thingDef==material)cost+=component.count;
            return cost;
        }

        private bool FieldFootprintFree(IntVec3 cell, ThingDef def, Pawn vip)
        {
            if(def==null || !cell.IsValid)return false;
            for(int dx=0;dx<def.size.x;dx++)
            for(int dz=0;dz<def.size.z;dz++)
            {
                IntVec3 c=cell+new IntVec3(dx,0,dz);
                if(!c.InBounds(map) || !c.Standable(map) ||
                    map.roofGrid.RoofAt(c)!=null || c.GetEdifice(map)!=null ||
                    c.GetFirstBuilding(map)!=null || c==vip.Position)return false;
            }
            return true;
        }

        // Two non-overlapping footprints, at least six cells from the captive.
        // Modded map roofs/buildings are read from actual cells, not DefNames.
        private bool FindFieldConstructionSite(Pawn vip,ThingDef launcher,ThingDef pod,
            out IntVec3 launchCell,out IntVec3 podCell)
        {
            launchCell=IntVec3.Invalid;podCell=IntVec3.Invalid;
            for(int radius=6;radius<=20;radius++)
            for(int dx=-radius;dx<=radius;dx++)
            for(int dz=-radius;dz<=radius;dz++)
            {
                if(Math.Max(Math.Abs(dx),Math.Abs(dz))!=radius)continue;
                IntVec3 cell=new IntVec3(vip.Position.x+dx,0,vip.Position.z+dz);
                if(!FieldFootprintFree(cell,launcher,vip) ||
                    !vip.CanReach(cell,PathEndMode.OnCell,Danger.Deadly))continue;
                // A genuine launcher has its own footprint; put the transport
                // pod on a separate adjacent free tile. Never wipe the launcher.
                IntVec3 vehicle=cell+new IntVec3(launcher.size.x,0,0);
                if(!FieldFootprintFree(vehicle,pod,vip))continue;
                // Do not create the flight assembly in the hostile kill corridor
                // if a safe outdoor position is available.
                bool exposed=false;int inspected=0;
                foreach(Building defense in map.listerBuildings.allBuildingsColonist)
                {
                    if(++inspected>180)break;
                    if(!RHFCombatPlannerV125.IsTurret(defense))continue;
                    if((defense.Position-cell).LengthHorizontalSquared<625 &&
                        GenSight.LineOfSight(defense.Position,cell,map))
                    {exposed=true;break;}
                }
                if(exposed)continue;
                launchCell=cell;podCell=vehicle;return true;
            }
            return false;
        }

        private bool HasFieldSteel(RHFOperation op,int required)
        {
            int total=0;
            Thing[] stocks={op.PodSteel,op.PodSteelExtra};
            foreach(Thing stack in stocks)
                if(stack!=null && !stack.Destroyed && stack.Map==map &&
                   (stack.Position-op.PodCell).LengthHorizontalSquared<=100)
                    total+=stack.stackCount;
            return total>=required;
        }

        private bool SpendFieldSteel(RHFOperation op,int amount)
        {
            if(!HasFieldSteel(op,amount))return false;
            foreach(Thing stack in new Thing[]{op.PodSteel,op.PodSteelExtra})
            {
                if(amount==0)break;
                if(stack==null || stack.Destroyed || stack.Map!=map)continue;
                int take=Math.Min(stack.stackCount,amount);
                UseFieldMaterial(stack,take);
                amount-=take;
            }
            return amount==0;
        }

        private IntVec3 FieldSupplyCell(IntVec3 center,IntVec3 skip1,IntVec3 skip2)
        {
            for(int radius=1;radius<=5;radius++)
            for(int dx=-radius;dx<=radius;dx++)
            for(int dz=-radius;dz<=radius;dz++)
            {
                if(Math.Max(Math.Abs(dx),Math.Abs(dz))!=radius)continue;
                IntVec3 c=center+new IntVec3(dx,0,dz);
                if(c==skip1 || c==skip2 || !c.InBounds(map) || !c.Standable(map) ||
                   c.GetEdifice(map)!=null || c.GetFirstBuilding(map)!=null)continue;
                bool occupied=false;
                foreach(Thing t in map.thingGrid.ThingsListAt(c))
                    if(t.def.category==ThingCategory.Item){occupied=true;break;}
                if(!occupied)return c;
            }
            return IntVec3.Invalid;
        }

        private bool FieldMaterialsPresent(RHFOperation op,int steel,int components)
        {
            return HasFieldSteel(op,steel) &&
                op.PodComponents!=null && !op.PodComponents.Destroyed &&
                op.PodComponents.Map==map && op.PodComponents.stackCount>=components &&
                (op.PodComponents.Position-op.PodCell).LengthHorizontalSquared<=100;
        }

        private static void UseFieldMaterial(Thing item,int count)
        {
            if(count<=0 || item==null || item.Destroyed)return;
            if(item.stackCount<=count)item.Destroy();
            else item.stackCount-=count;
        }

        // Field builders must be real, present, capable soldiers with a legal
        // approach. They do not simply spawn structures from across the map.
        private Pawn FieldEngineer(RHFOperation op,IntVec3 workCell)
        {
            Pawn best=null;int distance=int.MaxValue;
            for(int i=0;i<op.Squad.Count && i<48;i++)
            {
                Pawn p=op.Squad[i];
                if(p==null || !p.Spawned || p.Map!=map || p.Dead || p.Downed ||
                    p.jobs==null || p.health?.capacities==null ||
                    !p.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation) ||
                    !p.CanReach(workCell,PathEndMode.Touch,Danger.Deadly))continue;
                int d=(p.Position-workCell).LengthHorizontalSquared;
                if(d<distance){best=p;distance=d;}
            }
            return best;
        }

        private void FallBackToLand(RHFOperation op,int now,string reason)
        {
            op.Phase=2;op.PhaseStartTick=now;
            Log.Warning("[RHF][VIP] Native transport fallback to ground exit: " + reason);
        }

        private void BuildAndLaunch(RHFOperation op,int now)
        {
            Pawn vip=op.Vip;
            if(!op.PodBudgetPaid || vip==null || vip.Dead || vip.Map!=map ||
                !vip.Spawned || vip.Downed)
            {FallBackToLand(op,now,"resources or live mobile VIP unavailable");return;}
            ThingDef launcher=DefDatabase<ThingDef>.GetNamedSilentFail("PodLauncher");
            ThingDef pod=DefDatabase<ThingDef>.GetNamedSilentFail("TransportPod");
            if(launcher==null || pod==null)
            {FallBackToLand(op,now,"vanilla transport definitions missing");return;}
            int launcherSteel=FieldCost(launcher,ThingDefOf.Steel);
            int podSteel=FieldCost(pod,ThingDefOf.Steel);
            int launcherComponents=FieldCost(launcher,ThingDefOf.ComponentIndustrial);
            int podComponents=FieldCost(pod,ThingDefOf.ComponentIndustrial);
            if(launcherSteel<=0 || podSteel<=0 ||
                launcherComponents<=0 || podComponents<=0)
            {FallBackToLand(op,now,"vanilla transport cost lists incomplete");return;}
            if(!op.PodSuppliesIssued)
            {
                if(!FindFieldConstructionSite(vip,launcher,pod,
                    out IntVec3 launchCell,out IntVec3 podCell))
                {FallBackToLand(op,now,"no uncovered buildable double footprint");return;}
                op.PodLauncherCell=launchCell;op.PodCell=podCell;
                // Supplies from the raid budget, not the colony's storage.
                int steel=launcherSteel+podSteel+(op.FortifyPrison?20:0);
                int components=launcherComponents+podComponents;
                Thing materials=ThingMaker.MakeThing(ThingDefOf.Steel);
                Thing extra=null;
                int first=Math.Min(steel,ThingDefOf.Steel.stackLimit);
                materials.stackCount=first;
                if(steel>first)
                {
                    extra=ThingMaker.MakeThing(ThingDefOf.Steel);
                    extra.stackCount=steel-first;
                }
                Thing parts=ThingMaker.MakeThing(ThingDefOf.ComponentIndustrial);
                parts.stackCount=components;
                // Keep the transport pad clear; deposits remain recoverable loot
                // if the squad loses the objective or the player steals them.
                IntVec3 supply=FieldSupplyCell(launchCell,IntVec3.Invalid,IntVec3.Invalid);
                IntVec3 extraCell=extra!=null ? FieldSupplyCell(launchCell,supply,IntVec3.Invalid) :
                    IntVec3.Invalid;
                IntVec3 componentCell=FieldSupplyCell(launchCell,supply,extraCell);
                if(!supply.IsValid || (extra!=null && !extraCell.IsValid) ||
                    !componentCell.IsValid)
                {
                    materials.Destroy();
                    if(extra!=null)extra.Destroy();
                    parts.Destroy();
                    FallBackToLand(op,now,"no safe separated supply deposits");
                    return;
                }
                GenSpawn.Spawn(materials,supply,map);
                if(extra!=null)GenSpawn.Spawn(extra,extraCell,map);
                GenSpawn.Spawn(parts,componentCell,map);
                op.PodSteel=materials;op.PodSteelExtra=extra;
                op.PodComponents=parts;
                op.PodSuppliesIssued=true;
                op.PodConstructionStage=0;
                op.PodConstructionTick=0;
                op.PhaseStartTick=now;
                // VIP waits with the unit, not in a drifting vanilla flee job.
                // Keep out of the footprint until the structure is finished.
                IntVec3 waiting=podCell+IntVec3.South*2;
                if(waiting.InBounds(map) && waiting.Standable(map) &&
                    vip.CanReach(waiting,PathEndMode.OnCell,Danger.Deadly))
                    OrderGotoIfNeeded(vip,waiting,4);
            }
            int active=0;
            for(int i=0;i<op.Squad.Count;i++)
                if(op.Squad[i]!=null && op.Squad[i].Spawned &&
                    op.Squad[i].Map==map && !op.Squad[i].Downed && !op.Squad[i].Dead)active++;
            if(active<1)
            {
                if(now-op.PhaseStartTick>5000)
                    FallBackToLand(op,now,"no surviving engineers");
                return;
            }
            if(op.PodConstructionStage==0 && op.FortifyPrison && op.BarricadesBuilt<2 &&
                now-op.PhaseStartTick<3000)
                TryBuildPrisonBarricade(op,vip,now);

            if(op.PodReady)
            {
                if(!op.PodCell.IsValid || !op.PodCell.InBounds(map))
                {FallBackToLand(op,now,"pod site invalidated");return;}
                if((vip.Position-op.PodCell).LengthHorizontalSquared>2)
                {
                    if(vip.CanReach(op.PodCell,PathEndMode.OnCell,Danger.Deadly))
                        OrderGotoIfNeeded(vip,op.PodCell,1);
                    if(now-op.PhaseStartTick>23000)
                        FallBackToLand(op,now,"VIP cannot board built pod");
                    return;
                }
                if(!TryLaunchPhysicalVipPod(op,vip))
                {FallBackToLand(op,now,"physical cargo boarding failed");return;}
                op.PodLaunchCompleted=true;
                op.Phase=2;op.PhaseStartTick=now;
                // This outcome is available in the operation diagnostics. No
                // redundant letter: the mission plan was in the arrival letter.
                return;
            }
            int stage=op.PodConstructionStage;
            IntVec3 site=stage==0?op.PodLauncherCell:op.PodCell;
            if(!site.IsValid || !site.InBounds(map))
            {FallBackToLand(op,now,"site disappeared");return;}
            Pawn engineer=FieldEngineer(op,site);
            if(engineer==null)
            {
                if(now-op.PhaseStartTick>9000)
                    FallBackToLand(op,now,"work location unreachable");
                return;
            }
            if((engineer.Position-site).LengthHorizontalSquared>9)
            {
                OrderGotoIfNeeded(engineer,site,6);
                op.PodConstructionTick=0;
                return;
            }
            if(op.PodConstructionTick==0)op.PodConstructionTick=now;
            int stageWork=stage==0?3000:1600;
            if(now-op.PodConstructionTick<stageWork)
            {
                // Work time is spent by a nearby real engineer; combat can
                // interrupt the process and the saved clock is not reset.
                return;
            }
            int requiredSteel=stage==0?launcherSteel:podSteel;
            int requiredComponents=stage==0?launcherComponents:podComponents;
            if(!FieldMaterialsPresent(op,requiredSteel,requiredComponents))
            {FallBackToLand(op,now,"construction supplies stolen or destroyed");return;}
            ThingDef construction=stage==0?launcher:pod;
            if(!FieldFootprintFree(site,construction,vip))
            {FallBackToLand(op,now,"build site occupied");return;}
            try
            {
                Thing completed=ThingMaker.MakeThing(construction);
                completed.SetFaction(engineer.Faction);
                GenSpawn.Spawn(completed,site,map);
                SpendFieldSteel(op,requiredSteel);
                UseFieldMaterial(op.PodComponents,requiredComponents);
                if(stage==0)op.PodLauncherThing=completed;
                else op.PodVehicleThing=completed;
                op.PodConstructionStage++;
                op.PodConstructionTick=0;
                if(op.PodConstructionStage>=2)op.PodReady=true;
            }
            catch(Exception e)
            {FallBackToLand(op,now,"native construction failed: "+e.GetBaseException().Message);}
        }

        // A real Skyfaller and ThingOwner transport. No teleport and no free
        // disappearance of a VIP: the launch only succeeds if the pawn was
        // actually added to the skyfaller cargo container.
        private bool TryLaunchPhysicalVipPod(RHFOperation op, Pawn vip)
        {
            if (vip == null || !vip.Spawned || vip.Map != map ||
                !op.PodCell.IsValid || (vip.Position-op.PodCell).LengthHorizontalSquared > 2)
                return false;
            // ThingOwner.TryAdd does NOT automatically despawn spawned Things.
            // Despawn the pawn before loading to avoid the same VIP existing on
            // the map and in the cargo at once. Roll back on every failure.
            ActiveTransporter transporter=null;
            bool departed=false;
            IntVec3 previous=vip.Position;
            try
            {
                transporter=ThingMaker.MakeThing(ThingDefOf.ActiveDropPod) as ActiveTransporter;
                if(transporter==null) return false;
                transporter.Contents=new ActiveTransporterInfo();
                vip.DeSpawn();
                if(!transporter.Contents.innerContainer.TryAdd(vip)) return false;
                FlyShipLeaving skyfaller=SkyfallerMaker.MakeSkyfaller(
                    ThingDefOf.DropPodLeaving,transporter) as FlyShipLeaving;
                if(skyfaller==null) return false;
                skyfaller.createWorldObject = false;
                // Spawn the genuine departing vehicle before removing the
                // decorative built shell; a failed launch must not erase it.
                GenSpawn.Spawn(skyfaller,op.PodCell,map);
                departed=skyfaller.Spawned;
                if(departed)
                {
                    // The REAL vanilla TransportPod shell is consumed by launch;
                    // leave the real PodLauncher behind as recoverable structure.
                    if(op.PodVehicleThing!=null && !op.PodVehicleThing.Destroyed &&
                        op.PodVehicleThing.Map==map)op.PodVehicleThing.Destroy();
                }
                return departed;
            }
            catch(Exception e)
            {
                Log.Warning("[RHF] Could not load/launch VIP transporter: "+e);
                return false;
            }
            finally
            {
                if(!departed && !vip.Spawned && !vip.Destroyed)
                {
                    try
                    {
                        if(vip.holdingOwner!=null && transporter?.Contents!=null)
                        {
                            Thing dropped;
                            transporter.Contents.innerContainer.TryDrop(vip,previous,
                                map,ThingPlaceMode.Near,out dropped);
                        }
                        if(!vip.Spawned && vip.holdingOwner==null)
                            GenSpawn.Spawn(vip,previous,map);
                    }
                    catch(Exception rollback)
                    { Log.Error("[RHF] Critical VIP boarding rollback: "+rollback); }
                }
            }
        }


        private IntVec3 ChooseExit(IntVec3 from)
        {
            // Check only a handful of nearby border cells, in distance order.
            // The previous version could choose an impassable cell behind a
            // cliff or fortified wall; rescued pawns then marched elsewhere.
            IntVec3 fallback = IntVec3.Invalid;
            int best=int.MaxValue;
            for (int edge=0;edge<4;edge++)
            for (int offset=-24;offset<=24;offset+=6)
            {
                int x = edge==0 ? 0 : edge==1 ? map.Size.x-1 :
                    Mathf.Clamp(from.x+offset,1,map.Size.x-2);
                int z = edge==2 ? 0 : edge==3 ? map.Size.z-1 :
                    Mathf.Clamp(from.z+offset,1,map.Size.z-2);
                IntVec3 cell=new IntVec3(x,0,z);
                if (!cell.InBounds(map) || !cell.Standable(map)) continue;
                int dist=(cell-from).LengthHorizontalSquared;
                if(dist>=best)continue;
                if(!map.reachability.CanReach(from,cell,PathEndMode.OnCell,
                    TraverseMode.PassDoors,Danger.Deadly))continue;
                best=dist;fallback=cell;
            }
            if (fallback.IsValid) return fallback;
            // A real edge must still be attempted when the map is walled off,
            // but NEVER claim that this fallback was path-verified.
            return new IntVec3(0,0,Mathf.Clamp(from.z,1,map.Size.z-2));
        }

        private static void Order(Pawn pawn, JobDef def, LocalTargetInfo target)
        {
            if (pawn?.jobs == null || def == null) return;
            Job job=JobMaker.MakeJob(def,target);
            pawn.jobs.TryTakeOrderedJob(job);
        }
    }
}
