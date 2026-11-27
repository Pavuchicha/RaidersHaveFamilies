// Raiders Have Families 0.6.0 — bounded multi-party faction intervention system.
//  * Only scheduled operations have an active controller.
//  * A forced rescue uses an actual raiding party and in-map goals, never a fake letter.
//  * Every action has a bounded decision budget; no global pawn scans in idle time.
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
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
    // unrelated map raids are never enlisted. Live parties are never truncated for TPS.
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
        // S4.2: separate, REAL raid-points deductions for animals and loadouts.
        public float AnimalBudgetS42;
        public float AnimalSpentS42;
        public float LoadoutBudgetS42;
        public float LoadoutSpentS42;
        public int HeavyWeaponsIssuedS42;
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
            Scribe_Values.Look(ref AnimalBudgetS42,"njrAnimalBudgetS42",0f);
            Scribe_Values.Look(ref AnimalSpentS42,"njrAnimalSpentS42",0f);
            Scribe_Values.Look(ref LoadoutBudgetS42,"njrLoadoutBudgetS42",0f);
            Scribe_Values.Look(ref LoadoutSpentS42,"njrLoadoutSpentS42",0f);
            Scribe_Values.Look(ref HeavyWeaponsIssuedS42,"njrHeavyIssuedS42",0);
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
        // S4.1-A saveable mission blackboard; legacy saves initialize at load.
        public RHFTacticalBoardS41 BoardS41 = new RHFTacticalBoardS41();
        public RHFTacticalBoardS41 OffenseBoardS41 = new RHFTacticalBoardS41();
        public List<RHFNativeWingS41> NativeWingsS41 = new List<RHFNativeWingS41>();
        public int NextNativeObservationS41;
        public List<Pawn> NativeAnimalsS41 = new List<Pawn>();
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
        // S3.1.1: persist owner, native stages and the real refueling cargo.
        public bool PodNativeS311;
        public Faction PodCargoOwnerFactionS311;
        public List<Thing> PodFuelStacksS311 = new List<Thing>();
        // Physical inventory cargo, persisted as references, never replenished
        // by a save/load or spawned from a blueprint on an arbitrary tick.
        public List<Thing> PodCargoStacksS41 = new List<Thing>();
        public int PodStageStartedTickS311;
        public Thing PodSteel;
        public Thing PodSteelExtra;
        public Thing PodComponents;
        public Thing PodLauncherThing;
        public Thing PodVehicleThing;
        public IntVec3 PodLauncherCell = IntVec3.Invalid;
        public IntVec3 PodPrisonAnchorR043 = IntVec3.Invalid;
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
        // R052: immutable purchased fortification quota and material, persisted with the operation.
        public ThingDef SealStuffR052;
        public int SealCountR052;
        public int SealUnitsR052;
        public float SealCostR052;
        public int EarlyPadProbeTickR052 = -1;
        public Pawn EarlyCarrierR052;
        public int SealLimitR052 => SealCountR052>0 ? SealCountR052 : 2;
        public int FortifyStartedTick;
        public IntVec3 BarricadeCell = IntVec3.Invalid;
        public List<IntVec3> CompletedBarricades = new List<IntVec3>();
        // Physical wall blueprints/frames still being built, not completed walls.
        public List<IntVec3> PlannedBarricadesR055 = new List<IntVec3>();
        public int ParallelSealProbeTickR055 = -1;
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
            Scribe_Deep.Look(ref BoardS41, "rhfBlackboardS41");
            Scribe_Deep.Look(ref OffenseBoardS41, "rhfOffenseBoardS41AG");
            Scribe_Collections.Look(ref NativeWingsS41, "rhfNativeWingsS41AH", LookMode.Deep);
            Scribe_Values.Look(ref NextNativeObservationS41, "rhfNextNativeScanS41AH", 0);
            Scribe_Collections.Look(ref NativeAnimalsS41, "rhfNativeAnimalsS41AI", LookMode.Reference);
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
            Scribe_Values.Look(ref PodNativeS311, "rhfNativeS311", false);
            Scribe_References.Look(ref PodCargoOwnerFactionS311, "rhfCargoOwnerS311");
            Scribe_Collections.Look(ref PodFuelStacksS311, "rhfFuelStacksS311", LookMode.Reference);
            Scribe_Collections.Look(ref PodCargoStacksS41, "rhfPodCargoInventoryS41AO", LookMode.Reference);
            Scribe_Values.Look(ref PodStageStartedTickS311, "rhfNativeStageTimeS311", 0);
            Scribe_References.Look(ref PodSteel, "podSteel");
            Scribe_References.Look(ref PodSteelExtra, "rhfPodSteelExtraV130");
            Scribe_References.Look(ref PodComponents, "rhfPodComponentsV130");
            Scribe_References.Look(ref PodLauncherThing, "rhfVanillaLauncherV130");
            Scribe_References.Look(ref PodVehicleThing, "rhfVanillaPodV130");
            Scribe_Values.Look(ref PodLauncherCell, "rhfVanillaLauncherCellV130", IntVec3.Invalid);
            Scribe_Values.Look(ref PodPrisonAnchorR043, "njrPrisonAnchorR043", IntVec3.Invalid);
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
            Scribe_Defs.Look(ref SealStuffR052,"njrSealStuffR052");
            Scribe_Values.Look(ref SealCountR052,"njrSealCountR052",0);
            Scribe_Values.Look(ref SealUnitsR052,"njrSealUnitsR052",0);
            Scribe_Values.Look(ref SealCostR052,"njrSealCostR052",0f);
            Scribe_Values.Look(ref EarlyPadProbeTickR052,"njrEarlyPadProbeTickR052",-1);
            Scribe_References.Look(ref EarlyCarrierR052,"njrEarlyCarrierR052");
            Scribe_Values.Look(ref FortifyStartedTick, "rhfFortifyStartedV12", 0);
            Scribe_Values.Look(ref BarricadeCell, "rhfBarricadeCellV12", IntVec3.Invalid);
            Scribe_Collections.Look(ref CompletedBarricades, "rhfFinishedBarricadesV12", LookMode.Value);
            Scribe_Collections.Look(ref PlannedBarricadesR055,"njrPlannedBarricadesR055",LookMode.Value);
            Scribe_Values.Look(ref ParallelSealProbeTickR055,"njrParallelSealProbeTickR055",-1);
            Scribe_Values.Look(ref LastHuntOrderTick, "rhfLastHuntOrderV12", 0);
            Scribe_Values.Look(ref LastInfrastructureOrderTick,"rhfLastInfrastructureOrderV12",0);
            Scribe_Values.Look(ref RoofRemovalStartTick, "rhfRoofRemovalTickV12", 0);
            Scribe_Values.Look(ref PodLaunchCompleted, "rhfPodLaunchV12", false);
            Scribe_Values.Look(ref PodBudgetPaid, "rhfPodBudgetPaidV12", false);
            Scribe_Values.Look(ref PodSupplyCost, "rhfPodSupplyCostV12", 0f);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (BoardS41 == null) BoardS41 = new RHFTacticalBoardS41();
                if (OffenseBoardS41 == null) OffenseBoardS41 = new RHFTacticalBoardS41();
                if (NativeWingsS41 == null) NativeWingsS41 = new List<RHFNativeWingS41>();
                if (NativeAnimalsS41 == null) NativeAnimalsS41 = new List<Pawn>();
                if (Targets == null) Targets = new List<Pawn>();
                if (Squad == null) Squad = new List<Pawn>();
                if (Freed == null) Freed = new List<Pawn>();
                if (Escorts == null) Escorts = new List<RHFEscortLink>();
                if (Diversion == null) Diversion = new List<Pawn>();
                if (Sponsors == null) Sponsors = new List<Faction>();
                if (Parties == null) Parties = new List<RHFStrikeParty>();
                if (TacticalMembers == null) TacticalMembers = new List<RHFTacticalMember>();
                if (CompletedBarricades == null) CompletedBarricades = new List<IntVec3>();
                if (PlannedBarricadesR055 == null) PlannedBarricadesR055 = new List<IntVec3>();
                if (PodFuelStacksS311 == null) PodFuelStacksS311 = new List<Thing>();
                if (PodCargoStacksS41 == null) PodCargoStacksS41 = new List<Thing>();
                // Existing v0.3.x operations were already queued through the storyteller.
                // Do not create a duplicate raid when upgrading a saved game.
                if (Parties.Count == 0) Deployed = true;
            }
        }
    }

    public sealed partial class RHFOperationController : MapComponent
    {
        // Active missions and their participant lists are not artificially capped.
        private const int DecisionInterval = 90;
        private const int MaxOperationDuration = 180000;
        private RHFOperation activeOperation;
        private RHFStrikeParty activeParty;
        private List<RHFOperation> operations = new List<RHFOperation>();
        private int nextDecision;
        private sealed class RHFDropCaptureV136
        {
            internal readonly Dictionary<Faction,HashSet<int>> Before =
                new Dictionary<Faction,HashSet<int>>();
            internal int UntilTick;
        }
        private static readonly ConditionalWeakTable<RHFOperation,RHFDropCaptureV136>
            DropCapture = new ConditionalWeakTable<RHFOperation,RHFDropCaptureV136>();

        public RHFOperationController(Map map) : base(map) { }

        // Only the active synchronous vanilla incident may alter its generated
        // roster; delayed or unrelated raids can never inherit this operation.
        internal static RHFStrikeParty ActiveRaidParty(Map incidentMap,RHFOperation op)
        {
            RHFOperationController owner=incidentMap?.GetComponent<RHFOperationController>();
            return owner!=null && owner.activeOperation==op ? owner.activeParty : null;
        }

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
            if (operation == null || operation.Executor == null)
                return false;
            operations.Add(operation);
            nextDecision = Math.Min(nextDecision, operation.SpawnTick);
            return true;
        }

        // Reuse a genuinely scheduled rescue for the disappeared VIP rather than
        // spawning a duplicate expedition when the order already existed.
        // A prisoner may be targeted by several simultaneous expeditions, but
        // only ONE live operation owns the liberated pawn and its escort.
        internal RHFOperation FreedOwnerR028(Pawn captive)
        {
            if(captive==null)return null;
            string lordKey=(captive.GetLord()?.LordJob as RHFRescueLordJobV124)?.MissionKey;
            RHFOperation first=null;
            foreach(RHFOperation op in operations)
            {
                if(op==null || op.Finished || !op.Deployed ||
                   op.Type!=(int)RHFOperationType.Rescue ||
                   op.Freed==null || !op.Freed.Contains(captive))continue;
                if(first==null)first=op;
                if(lordKey!=null && RHFRescueCommandV124.Key(op)==lordKey)return op;
            }
            return first;
        }

        public RHFOperation FindRescueFor(string victimId)
        {
            if (string.IsNullOrEmpty(victimId)) return null;
            // Actual ownership outranks earlier scheduled target references.
            foreach(RHFOperation candidate in operations)
            {
                if(candidate==null || candidate.Finished || candidate.Freed==null)continue;
                foreach(Pawn captive in candidate.Freed)
                    if(captive?.ThingID==victimId)
                    {
                        RHFOperation owner=FreedOwnerR028(captive);
                        if(owner!=null)return owner;
                    }
            }
            for (int i=0;i<operations.Count;i++)
            {
                RHFOperation op=operations[i];
                // A case key labels a rescue history; it does NOT cancel the
                // escort's authority over that operation or its freed pawns.
                // Do not identify a targetless missing-person search as a live
                // prisoner rescue solely because op.Vip still has an identity.
                if (op==null || op.Finished ||
                    op.Type!=(int)RHFOperationType.Rescue) continue;
                bool hasTargets=op.Targets!=null && op.Targets.Count>0;
                bool hasFreed=op.Freed!=null && op.Freed.Count>0;
                if(!hasTargets && !hasFreed)continue;
                if(hasTargets)
                    for(int j=0;j<op.Targets.Count;j++)
                        if(op.Targets[j]?.ThingID==victimId)return op;
                if(hasFreed)
                    for(int j=0;j<op.Freed.Count;j++)
                        if(op.Freed[j]?.ThingID==victimId)return op;
            }
            return null;
        }

        // Debug inspection is computed only when clicked, never per tick.
        public string DebugStatus()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("NJR: активные спецоперации " + operations.Count);
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
                for (int k=0;k<op.Parties.Count;k++)
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
            sb.AppendLine("NJR: аудит тактических ролей и реального снаряжения");
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
                        (RHFStrikeRole)party.Role + ", " + party.Members + " бойцов " +
                        "(минимум " + NJRForceStrengthR017.RequiredMembers(party) +
                        "), очки операции " + Mathf.RoundToInt(party.Points) + ", " +
                        party.BreachKits + " бойцов с проломным оружием, " + party.Medics +
                        " санитаров, без оружия " + party.UnarmedCount + "; бюджет " +
                        Mathf.RoundToInt(party.SupplySpent) + "/" + Mathf.RoundToInt(party.SupplyBudget));
                }
                int shown=0;
                for (int j=0;j<op.TacticalMembers.Count;j++)
                {
                    RHFTacticalMember m=op.TacticalMembers[j];
                    if (m?.Pawn == null) continue;
                    sb.AppendLine("  " + m.Pawn.LabelShortCap + " — " +
                        RHFTactical.RoleName(m.Role) + ", оружие=" + (!m.WeaponMissing) +
                        ", топор для пролома=" + RHFTactical.IsBreachAxe(m.Pawn));
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
                pawn.RaceProps == null) return;
            if (!pawn.RaceProps.Humanlike)
            {
                // Never enlist unrelated animals on the map: ONLY active
                // incident scope may claim a native non-human raider.
                if (pawn.RaceProps.Animal &&
                    activeOperation != null && activeParty != null &&
                    pawn.Faction == activeParty.Faction &&
                    activeParty.Role != (int)RHFStrikeRole.Rescue)
                {
                    if (activeOperation.NativeAnimalsS41 == null)
                        activeOperation.NativeAnimalsS41 = new List<Pawn>();
                    if (!activeOperation.NativeAnimalsS41.Contains(pawn))
                        activeOperation.NativeAnimalsS41.Add(pawn);
                }
                return;
            }
            if (activeOperation != null && activeParty != null &&
                pawn.Faction == activeParty.Faction)
            {
                List<Pawn> assigned = activeParty.Role == (int)RHFStrikeRole.Rescue ?
                    activeOperation.Squad : activeOperation.Diversion;
                if (!assigned.Contains(pawn))
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
                for (int k=0;k<operation.Parties.Count;k++)
                {
                    RHFStrikeParty delayed=operation.Parties[k];
                    if (delayed == null || !delayed.Succeeded || !delayed.Drop ||
                        delayed.Faction != pawn.Faction) continue;
                    List<Pawn> wing = delayed.Role == (int)RHFStrikeRole.Rescue ?
                        operation.Squad : operation.Diversion;
                    if (!wing.Contains(pawn))
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
                    now > op.SpawnTick + 3600) continue;
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

        // Native CenterDrop may accept landings on furniture and destroy it.
        // Rank only genuinely open pads; never claim a room fits a squad
        // because the vanilla crash finder considers its chairs disposable.
        private static bool SafeRescueDropPad(Map map,IntVec3 cell)
        {
            return cell.InBounds(map) && cell.Standable(map) &&
                cell.GetEdifice(map)==null &&
                cell.GetFirstBuilding(map)==null &&
                cell.GetFirstPawn(map)==null &&
                DropCellFinder.IsGoodDropSpot(cell,map,true,true);
        }

        private IntVec3 RescueDropCenter(RHFOperation op,RHFStrikeParty party)
        {
            if(op?.Targets==null || party==null)return IntVec3.Invalid;
            IntVec3 best=IntVec3.Invalid;
            int bestScore=int.MaxValue;
            for(int i=0;i<op.Targets.Count;i++)
            {
                Pawn vip=op.Targets[i];
                if(vip==null || vip.Dead || !vip.Spawned || vip.Map!=map ||
                   !(vip.IsPrisonerOfColony || vip.IsSlaveOfColony))continue;
                RHFHostageRecord record=Current.Game?.GetComponent<RHFGameLedger>()?.FindHostage(vip);
                Faction sponsor=record?.OriginalFaction ?? vip.Faction;
                if(op.Sponsors!=null && op.Sponsors.Contains(party.Faction) &&
                   sponsor!=party.Faction)continue;
                Room prison=vip.Position.GetRoom(map);
                for(int radius=2;radius<=28;radius++)
                for(int dx=-radius;dx<=radius;dx++)
                for(int dz=-radius;dz<=radius;dz++)
                {
                    if(Math.Max(Math.Abs(dx),Math.Abs(dz))!=radius)continue;
                    IntVec3 c=vip.Position+new IntVec3(dx,0,dz);
                    if(!SafeRescueDropPad(map,c))continue;
                    Room room=c.GetRoom(map);
                    if(room==null)continue;
                    RoofDef roof=map.roofGrid.RoofAt(c);
                    if(roof!=null && roof.isThickRoof)continue;
                    // Falling roofs can hurt bystanders. Do not put a roof
                    // punch within four tiles of ANY named captive.
                    bool hazard=false;
                    for(int j=0;j<op.Targets.Count;j++)
                    {
                        Pawn captive=op.Targets[j];
                        if(captive?.Spawned!=true || captive.Map!=map)continue;
                        int squared=(captive.Position-c).LengthHorizontalSquared;
                        if(squared<16 || (roof!=null && squared<25))
                        {hazard=true;break;}
                    }
                    if(hazard)continue;
                    int tier=room==prison ? 0 :
                        !room.PsychologicallyOutdoors &&
                        NJRRescueInteriorS41.DirectDoor(map,prison,room,vip.Position)?1 :
                        !room.PsychologicallyOutdoors?2:3;
                    // Avoid occupied room and corridor defense installations.
                    int risk=NJRRescueInteriorS41.Exposure(map,c);
                    if(risk>0 && tier<3)continue;
                    int space=0;
                    for(int ox=-8;ox<=8;ox++)
                    for(int oz=-8;oz<=8;oz++)
                    {
                        IntVec3 nearby=c+new IntVec3(ox,0,oz);
                        if(nearby.InBounds(map) && nearby.GetRoom(map)==room &&
                           SafeRescueDropPad(map,nearby))space++;
                    }
                    // The vanilla CenterDrop scatters an entire wing. A room
                    // that fits only ONE pod is NOT a safe squad landing zone.
                    // op.Squad is normally empty BEFORE arrival: using it
                    // underestimated the physical landing footprint and let
                    // the remaining pods smash beds and workshops.
                    int required=Math.Max(6,
                        Mathf.CeilToInt(party.Points/180f));
                    if(space<required && tier<3)continue;
                    int score=tier*100000+risk*20000+
                        (c-vip.Position).LengthHorizontalSquared*5-space*12;
                    if(score<bestScore){bestScore=score;best=c;}
                }
            }
            return best;
        }

        // Explicit incident-scoped recovery when a modded arrival mode omits
        // Pawn.SpawnSetup or spawns pawns before our observer can bind them.
        // Never steal a pre-existing pawn of the same faction, and never reassign
        // a soldier from another RHF operation / already captured strike party.
        private void RecoverFreshRescueMembers(RHFOperation op, RHFStrikeParty party,
            HashSet<int> existingPawns)
        {
            if (op == null || party == null || existingPawns == null ||
                party.Role != (int)RHFStrikeRole.Rescue ||
                op.Type != (int)RHFOperationType.Rescue) return;
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                
                if (pawn == null || !pawn.Spawned || pawn.Dead ||
                    pawn.Faction != party.Faction ||
                    pawn.RaceProps == null || !pawn.RaceProps.Humanlike ||
                    existingPawns.Contains(pawn.thingIDNumber) ||
                    op.Squad.Contains(pawn) || op.Diversion.Contains(pawn)) continue;
                op.Squad.Add(pawn);
                party.Members++;
                RHFTactical.Register(op, party, pawn);
            }
        }

        // S2: DropPodIncoming can spawn pawns after RaidEnemy.TryExecute ends.
        // Keep the PRE-incident pawn IDs so the watchdog can claim only new
        // members of the exact hired rescue faction, not unrelated enemies.
        private void RecoverLateDropRescuers(RHFOperation op,int now)
        {
            if(op==null || !op.Deployed || op.Type!=(int)RHFOperationType.Rescue ||
               !DropCapture.TryGetValue(op,out RHFDropCaptureV136 capture) ||
               now>capture.UntilTick)return;
            int recovered=0;
            foreach(Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if(pawn==null || !pawn.Spawned || pawn.Dead ||
                   pawn.RaceProps?.Humanlike!=true || pawn.Faction==null ||
                   !capture.Before.TryGetValue(pawn.Faction,out HashSet<int> before) ||
                   before.Contains(pawn.thingIDNumber) ||
                   op.Squad.Contains(pawn) || op.Diversion.Contains(pawn))continue;
                bool otherOperation=false;
                foreach(RHFOperation other in operations)
                    if(other!=op && other!=null &&
                       ((other.Squad!=null && other.Squad.Contains(pawn)) ||
                        (other.Diversion!=null && other.Diversion.Contains(pawn))))
                    {otherOperation=true;break;}
                if(otherOperation)continue;
                RHFStrikeParty rescueParty=null;
                foreach(RHFStrikeParty party in op.Parties)
                    if(party!=null && party.Succeeded && party.Drop &&
                       party.Role==(int)RHFStrikeRole.Rescue && party.Faction==pawn.Faction)
                    {rescueParty=party;break;}
                if(rescueParty==null)continue;
                op.Squad.Add(pawn);
                rescueParty.Members++;
                RHFTactical.Register(op,rescueParty,pawn);
                recovered++;
            }
            if(recovered>0)
                Log.Message("[RHF][DropV136] Registered "+recovered+
                    " late rescue pawns; mission now has "+op.Squad.Count+
                    " members, phase="+op.Phase);
        }

        // The before/after delta is consumed while the incident call is
        // still synchronous. No world/faction scans on ordinary tactical ticks.
        private void RecoverIncidentBeastsS41(RHFOperation op,
            RHFStrikeParty party,HashSet<int> before)
        {
            if(op==null || party?.Faction==null || before==null ||
               party.Role==(int)RHFStrikeRole.Rescue)return;
            if(op.NativeAnimalsS41==null)op.NativeAnimalsS41=new List<Pawn>();
            foreach(Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                
                if(pawn==null || pawn.Dead || !pawn.Spawned ||
                   pawn.RaceProps?.Animal!=true ||
                   pawn.Faction!=party.Faction ||
                   before.Contains(pawn.thingIDNumber) ||
                   op.NativeAnimalsS41.Contains(pawn))continue;
                op.NativeAnimalsS41.Add(pawn);
            }
        }

        private bool Deploy(RHFOperation op)
        {
            if (op == null || op.Deployed) return op != null && op.SuccessfulParties > 0;
            op.Deployed = true;
            if (op.Parties == null || op.Parties.Count == 0) return false;
            // A land-arriving rescue NEVER gains an evacuation pod mission.
            // Only explicitly funded DROP RESCUE parties are eligible.
            if(op.TryPod)
            {
                bool dropRescue=false;
                foreach(RHFStrikeParty planned in op.Parties)
                    if(planned!=null && planned.Drop &&
                       NJRForceCompositionS42.CanUsePods(planned.Faction) &&
                       planned.Role==(int)RHFStrikeRole.Rescue)
                    {dropRescue=true;break;}
                if(!dropRescue)
                {
                    op.TryPod=false;op.FortifyPrison=false;
                    Log.Warning("[RHF][PodGateS21] Land-arriving rescuers use ground evacuation; no field pod kit.");
                }
            }
            int success = 0;
            for (int i=0; i<op.Parties.Count; i++)
            {
                RHFStrikeParty party = op.Parties[i];
                if (party == null || party.Faction == null || party.Attempted) continue;
                // R017: minimum viable tactical wing is budgeted BEFORE the
                // native PawnGroupMaker consumes the faction's raid points.
                NJRForceStrengthR017.CommitMissionBudget(op,party);
                party.Attempted = true;
                activeOperation = op;
                activeParty = party;
                // Snapshot exactly this synchronous incident's non-human pawns.
                // A faction-wide delayed search is NOT safe and is prohibited.
                HashSet<int> beastsBeforeRaid=null;
                if(op.Type==(int)RHFOperationType.Vengeance)
                {
                    beastsBeforeRaid=new HashSet<int>();
                    foreach(Pawn already in map.mapPawns.AllPawnsSpawned)
                        if(already?.RaceProps?.Animal==true)
                            beastsBeforeRaid.Add(already.thingIDNumber);
                }
                // Snapshot the map just before THIS incident; never identify
                // an unrelated pre-existing enemy as part of the rescue.
                HashSet<int> presentBeforeRaid = null;
                if (op.Type == (int)RHFOperationType.Rescue &&
                    party.Role == (int)RHFStrikeRole.Rescue)
                {
                    presentBeforeRaid = new HashSet<int>();
                    foreach (Pawn existing in map.mapPawns.AllPawnsSpawned)
                        if (existing != null)
                            presentBeforeRaid.Add(existing.thingIDNumber);
                    if (party.Drop)
                    {
                        RHFDropCaptureV136 capture=DropCapture.GetValue(op,_=>new RHFDropCaptureV136());
                        if(!capture.Before.TryGetValue(party.Faction,out HashSet<int> recorded))
                            capture.Before[party.Faction]=presentBeforeRaid;
                        else
                            recorded.UnionWith(presentBeforeRaid);
                        capture.UntilTick=Find.TickManager.TicksGame+2400;
                    }
                }
                bool accepted = false;
                // Preserve the verified ground insertion location across try/finally.
                // Native reinforcements must share the same real entry cell.
                IntVec3 rescueReinforcementEntry = IntVec3.Invalid;
                try
                {
                    // Fund all role tools from the same force budget; reserve at most
                    // twelve percent and never multiply cost by coalition size.
                    party.SupplyBudget = RHFMod.Settings?.RoleEquipment == true ?
                        Mathf.Min(300f, Mathf.Max(0f, party.Points * 0.12f)) : 0f;
                    // Exotic equipment and animal companions replace a slice
                    // of the same storyteller threat budget. No free megafauna.
                    NJRForceCompositionS42.Reserve(op,party);
                    // Validate technological access BEFORE charging the party
                    // for VIP construction supplies. A low-tech "drop" cannot
                    // own a real native pod and must not consume that budget.
                    if(party.Drop && !NJRForceCompositionS42.CanUsePods(party.Faction))
                        party.Drop=false;
                    if(party.Siege && !NJRForceCompositionS42.CanUseSiege(party.Faction))
                        party.Siege=false;
                    float effectivePoints = party.Points - party.SupplyBudget -
                        party.AnimalBudgetS42 - party.LoadoutBudgetS42;
                    effectivePoints=Mathf.Max(1f,effectivePoints);
                    if (op.TryPod && party.Drop &&
                        party.Role==(int)RHFStrikeRole.Rescue && !op.PodBudgetPaid)
                    {
                        // Genuine vanilla PodLauncher/TransportPod have separate
                        // Steel and ComponentIndustrial costs. Both are charged
                        // from this party's threat budget (and are placed physically).
                        ThingDef launcher=DefDatabase<ThingDef>.GetNamedSilentFail("PodLauncher");
                        ThingDef pod=DefDatabase<ThingDef>.GetNamedSilentFail("TransportPod");
                        int steel=FieldCost(launcher,ThingDefOf.Steel) +
                            FieldCost(pod,ThingDefOf.Steel);
                        int components=FieldCost(launcher,ThingDefOf.ComponentIndustrial) +
                            FieldCost(pod,ThingDefOf.ComponentIndustrial);
                        CompProperties_Refuelable refuelProps=
                            launcher?.GetCompProperties<CompProperties_Refuelable>();
                        int fuel=refuelProps==null?0:Mathf.CeilToInt(refuelProps.fuelCapacity);
                        float cost=steel*ThingDefOf.Steel.BaseMarketValue+
                            components*ThingDefOf.ComponentIndustrial.BaseMarketValue+
                            fuel*ThingDefOf.Chemfuel.BaseMarketValue;
                        // Pod materials must not consume the last four
                        // rescuers. Commit the mission's missing resources as
                        // explicit points BEFORE handing the balance to native
                        // pawn generation. This is not a free installed pod.
                        float core=Mathf.Max(350f,NJRForceStrengthR017.HumanCoreFloor(party));
                        bool sealFunded=!op.FortifyPrison || NJRSealBudgetR052.Plan(op,party.Points,
                            Math.Max(0f,effectivePoints-cost-core));
                        cost+=op.SealCostR052;
                        float required=cost+core;
                        if(sealFunded && launcher!=null && pod!=null && steel>0 && components>0 && fuel>0 &&
                           effectivePoints<required)
                        {
                            float additional=required-effectivePoints;
                            if(party.Points+additional<=15000f)
                            {
                                party.Points+=additional;
                                op.ThreatPoints+=additional;
                                effectivePoints+=additional;
                                Log.Message("[NJR][ForceR017] VIP pod materials funded " +
                                    "without eating the rescue human core: +"+
                                    additional.ToString("F0")+" committed points.");
                            }
                        }
                        if(sealFunded && launcher!=null && pod!=null && steel>0 && components>0 && fuel>0 &&
                            effectivePoints>=required)
                        {effectivePoints-=cost;op.PodSupplyCost=cost;op.PodBudgetPaid=true;
                            if(op.FortifyPrison)Log.Message("[NJR][R052][SealBudget] walls="+op.SealCountR052+" stuff="+op.SealStuffR052?.defName+
                                " units="+op.SealUnitsR052+" paid="+op.SealCostR052.ToString("F1")+" remaining="+effectivePoints.ToString("F1"));}
                        else
                        {
                            op.TryPod=false;op.FortifyPrison=false;
                            Log.Warning("[RHF] VIP field construction unavailable: vanilla defs, " +
                                "components or affordable resources missing. Land extraction selected.");
                        }
                    }
                    IntVec3 plannedRescueEntry=IntVec3.Invalid;
                    var parms = new IncidentParms
                    { target = map, faction = party.Faction,
                      // Never mint 300 vanilla raid points after spending
                      // the party budget on animals, gear, fuel or materials.
                      // Let the native incident reject genuinely tiny parties.
                      points = Mathf.Min(effectivePoints, 15000f) };
                    // Technology was already enforced before any pod charge.
                    // A vanilla breaching lord selects arbitrary colony walls and
                    // infrastructure. Rescue is objective-driven by RHF, never
                    // a generic Breaching raid, including during its first ticks.
                    parms.raidStrategy = op.Type == (int)RHFOperationType.Rescue ?
                        PickStrategy("ImmediateAttack") :
                        party.Siege ? PickStrategy("Siege", "ImmediateAttackSmart", "ImmediateAttack") :
                        party.Breach ? PickStrategy("Breaching", "ImmediateAttackSmart", "ImmediateAttack") :
                        PickStrategy("ImmediateAttackSmart", "ImmediateAttack");
                    if(NJRForceCompositionS42.LowTech(party.Faction) &&
                       op.Type==(int)RHFOperationType.Vengeance)
                        parms.raidArrivalMode=PawnsArrivalModeDefOf.EdgeWalkIn;
                    if (!party.Drop && op.Type == (int)RHFOperationType.Rescue)
                    {
                        // Do not let vanilla pick a random high-tech center-drop
                        // into the player's bedroom for a ground prison breakout.
                        parms.raidArrivalMode = DefDatabase<PawnsArrivalModeDef>.GetNamedSilentFail("EdgeWalkIn");
                        if (parms.raidArrivalMode == null)
                            throw new InvalidOperationException("NJR: EdgeWalkIn not available for rescue");
                        // A vanilla random edge can insert a rescue wing into
                        // an obvious turret killzone. Choose a bounded safe
                        // edge first; EdgeWalkIn honors valid spawnCenter.
                        // An unsafe random fallback is NOT a rescue: a squad
                        // stranded inside a mountain does not reach its target.
                        try
                        {
                            IntVec3 safer=RHFRescueGroundEntryS41.Choose(map,op);
                            if(safer.IsValid)
                            {plannedRescueEntry=safer;parms.spawnCenter=safer;
                             rescueReinforcementEntry=safer;}
                        }
                        catch(Exception e)
                        {
                            parms.spawnCenter=IntVec3.Invalid;
                            Log.Warning("[RHF][EntryS41AP] Tactical entry scan failed; " +
                                "cannot safely choose ground entry: " + e);
                        }
                        if(!parms.spawnCenter.IsValid)
                            throw new InvalidOperationException(
                                "NJR: no verified walk-in edge reachable from the prison; " +
                                "refusing a random cave/remote map spawn");
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
                                throw new InvalidOperationException("NJR: нет допустимой площадки " +
                                    "для десанта вблизи тюрьмы; рейд отменён, а не подменён " +
                                    "обычным десантом в центр колонии.");
                        }
                    }
                    // RaidEnemy.Worker.TryExecute fires *now*. The Harmony SpawnSetup
                    // hook binds each generated pawn to this specific party.
                    try
                    {
                        accepted=RHFMissionIntegrityV130.ExecuteRaidWithoutVanillaLetter(parms,op);
                    }
                    catch(Exception firstFailure)
                    {
                        // No scripted spawn. Preserve the native incident but
                        // let its own edge finder retry after our optional
                        // tactical insertion failed. A partial raid cannot be
                        // duplicated: the member/arrival capture guards below.
                        if(party.Drop || party.Members>0)throw;
                        Log.Warning("[RHF][EntryS41AP] First ground incident threw; " +
                            "native retry requested: " + firstFailure);
                        accepted=false;
                    }
                    // If a special strategy is invalid for this modded faction,
                    // retry a plain immediate assault only when no pawns spawned.
                    if (!accepted && !party.Drop && party.Members == 0)
                    {
                        // Keep the verified position on retry. Unconditionally
                        // clearing spawnCenter sent rescuers into distant caves.
                        parms.raidStrategy=RaidStrategyDefOf.ImmediateAttack;
                        if(op.Type==(int)RHFOperationType.Rescue)
                        {
                            parms.raidArrivalMode=PawnsArrivalModeDefOf.EdgeWalkIn;
                            parms.spawnCenter=plannedRescueEntry;
                        }
                        else {parms.raidArrivalMode=null;
                            parms.spawnCenter=IntVec3.Invalid;}
                        Log.Warning("[NJR][EntryS41] Native retry with safe rescue " +
                            "spawnCenter=" + parms.spawnCenter + ", faction=" + party.Faction);
                        accepted=RHFMissionIntegrityV130.ExecuteRaidWithoutVanillaLetter(parms,op);
                    }
                }
                catch (Exception e)
                {
                    Log.Error("[RHF] Immediate raid failed for " + party.Faction.Name +
                        " role " + party.Role + ": " + e);
                }
                finally { activeOperation = null; activeParty = null; }
                RecoverFreshRescueMembers(op, party, presentBeforeRaid);
                RecoverIncidentBeastsS41(op,party,beastsBeforeRaid);
                party.Succeeded = accepted || party.Members > 0;
                // Native raid success only means at least one faction pawn
                // spawned. Do not mistake a lone elite for an assault squad.
                if(party.Succeeded && !party.Drop &&
                   op.Type==(int)RHFOperationType.Rescue &&
                   party.Role==(int)RHFStrikeRole.Rescue)
                    NJRForceStrengthR017.CompleteGroundRescue(op,party,map,
                        rescueReinforcementEntry);
                if(party.Succeeded && party.Members<
                    NJRForceStrengthR017.RequiredMembers(party) && !party.Drop)
                    Log.Warning("[NJR][ForceR017] Partial native wing "+
                        party.Faction.Name+": "+party.Members+"/"+
                        NJRForceStrengthR017.RequiredMembers(party)+
                        " expected; incident was not a full-strength formation.");
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
            if (success == 0 && !arrivalStillPending && op.DebugForced)
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
                        // Debug recovery may replace rejected parties, but it
                        // must never mint a free 650-point minimum on a tiny colony.
                        Mathf.Max(1f,op.ThreatPoints),breach: op.Type==(int)RHFOperationType.Rescue);
                    op.Parties.Add(reserve);
                    activeOperation=op;
                    activeParty=reserve;
                    try
                    {
                        reserve.SupplyBudget = RHFMod.Settings?.RoleEquipment == true ?
                            Mathf.Min(300f, reserve.Points * 0.12f) : 0f;
                        NJRForceCompositionS42.Reserve(op,reserve);
                        var parms=new IncidentParms { target=map, faction=alternate,
                            points=Mathf.Max(1f,reserve.Points-reserve.SupplyBudget-
                                reserve.AnimalBudgetS42-reserve.LoadoutBudgetS42),
                            raidStrategy=RaidStrategyDefOf.ImmediateAttack };
                        // A reserve RESCUE is still a rescue: never permit
                        // the vanilla random edge or arbitrary center drop.
                        // Otherwise the last-chance squad can reappear in an
                        // isolated mountain cave, exactly like the original bug.
                        if(op.Type==(int)RHFOperationType.Rescue)
                        {
                            parms.raidArrivalMode=PawnsArrivalModeDefOf.EdgeWalkIn;
                            IntVec3 safeBackup=RHFRescueGroundEntryS41.Choose(map,op);
                            if(!safeBackup.IsValid)
                                throw new InvalidOperationException(
                                    "NJR: no verified reachable ground entry for reserve rescue");
                            parms.spawnCenter=safeBackup;
                        }
                        else if(NJRForceCompositionS42.LowTech(alternate))
                            parms.raidArrivalMode=PawnsArrivalModeDefOf.EdgeWalkIn;
                        reserve.Attempted=true;
                        reserve.Succeeded=RHFMissionIntegrityV130.ExecuteRaidWithoutVanillaLetter(parms, op);
                    }
                    catch(Exception e)
                    { Log.Warning("[RHF] Reserve raider " + alternate.Name + " failed: " + e.Message); }
                    finally { activeOperation=null;activeParty=null; }
                    if (reserve.Succeeded || reserve.Members>0)
                    {
                        reserve.Succeeded=true;
                        op.Executor=alternate;
                        success++;
                        Messages.Message("NJR: исходная фракция не смогла собрать рейд; " +
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
            // This is the actual operation owner. Never depend on a chain of
            // Harmony-postfixed old rescue controllers to capture vanilla raid lords.
            if (success > 0 && op.Type == (int)RHFOperationType.Rescue)
                RHFRescueCommandV124.OnDeploy(this, op, true);

            if (op.Type == (int)RHFOperationType.Rescue && op.Squad.Count == 0 && success > 0)
                Log.Message("[RHF] Rescue dispatched but no active on-map rescuers yet. " +
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
                for (int i=0; i<op.Parties.Count; i++)
                {
                    RHFStrikeParty party=op.Parties[i];
                    if (party == null || !party.Succeeded) continue;
                    string task=party.Role == (int)RHFStrikeRole.Rescue ? "освобождение" :
                        party.Role == (int)RHFStrikeRole.Diversion ? "отвлекающий штурм" : "месть";
                    report.AppendLine("• " + party.Faction.Name + ": " + task +
                        (party.Drop ? ", десантирование" : party.Breach ? ", прорыв" :
                        party.Siege ? ", осада" : ", наземное прибытие") +
                        "; сила около " + Mathf.RoundToInt(party.Points) + " очков." +
                        " Бойцов с проломным оружием: " + party.BreachKits + ", санитары: " + party.Medics +
                        ", без основного оружия: " + party.UnarmedCount + ".");
                }
                // RaidEnemy already issued an arrival letter with an actual raid
                // location. A second letter pointing at map.Center looks like a
                // duplicate or phantom raid. Keep the tactical report in the log.
                Log.Message("[RHF] " + report.ToString());
                // Native RaidEnemy letter is already customized and deduplicated
                // during incident dispatch. A second custom letter is a bug.
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
            if (op == null || op.SplitApplied || op.Type != (int)RHFOperationType.Rescue) return;
            // V132: an arbitrary fraction of the rescue squad must never be
            // detached into a VANILLA destructive raid. A distinct diversion
            // exists ONLY when a separate RHFStrikeParty was explicitly funded.
            // All rescue members retain their hostage objectives.
            op.SplitApplied = true;
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
                { if(op!=null)RHFOffenseDirectorS41.End(op,map);
                  operations.RemoveAt(i); continue; }
                if (now >= op.SpawnTick && !op.Deployed && op.Parties.Count > 0)
                {
                    if (!Deploy(op))
                    {
                        RHFReprisalSystem.OnOperationFailed(op);
                        Messages.Message("NJR: фракции не удалось собрать рейд. Проверьте журнал ошибок и тип фракции.",
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
                    // S4.1-A/G: no external TryTakeOrderedJob in vengeance.
                    // Native siege/animal lords remain intact, mission pawns
                    // outside them receive only ThinkTree role orders.
                    if(RHFMod.Settings?.TacticalCombat == true)
                    {
                        RHFOffenseDirectorS41.Tick(op,map,now);
                        NJROrbitalSupportS42.Tick(op,map,now);
                    }
                    int vengeanceDuration=op.HuntTarget!=null ||
                        op.Doctrine==(int)RHFDoctrine.SiegeAndRaze ? 48000 : 18000;
                    if (now > op.SpawnTick + vengeanceDuration)
                    { RHFOffenseDirectorS41.End(op,map); operations.RemoveAt(i); }
                    continue; // vanilla Lord still controls ordinary assault formations
                }
                if (now < op.SpawnTick) continue;
                // Recover airdropped rescue pawns before the Phase 0 empty-squad
                // deadline can drop the mission and leave vanilla AssaultColony.
                RecoverLateDropRescuers(op,now);
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
                // One source of truth for rescue authority. Reconcile late
                // capsule arrivals before AI gets another assault ThinkTree tick.
                RHFRescueCommandV124.OnDeploy(this, op, true);
                // Opposite-flank diversion uses its own tactical board and Lords.
                if(op.Diversion!=null && op.Diversion.Count>0)
                    RHFOffenseDirectorS41.Tick(op,map,now);
                // Use actually paid, worn heavy tools during active rescue,
                // never during the escorted ground withdrawal phase.
                if(RHFMod.Settings?.TacticalCombat==true)
                    NJROrbitalSupportS42.Tick(op,map,now);
                // Real kit is dropped by an arrived rescue party, before VIP liberation.
                if (op.TryPod && op.PodBudgetPaid && !op.PodSuppliesIssued)
                    NativeIssueCargoOnArrivalS311(op, now);
                if (!string.IsNullOrEmpty(op.CaseKeyV10) && op.Targets.Count == 0 &&
                    op.Type == (int)RHFOperationType.Rescue && op.Phase == 1)
                    SearchMissingVip(op, now);
                else if (op.Phase == 1)
                {
                    // A tribal diversion attached to a RESCUE operation may
                    // use budgeted animals. The primary rescue wing stays
                    // human-only so its captive Lord and escort duties remain
                    // coherent. Companions remain in the diversion's native Lord.
                    if(!op.SupportUsed && op.Diversion!=null && op.Diversion.Count>0)
                        DeployVengeanceSupport(op);
                    // Every rescue with live targets uses the SAME route,
                    // suppression and obstacle logic, including case-keyed
                    // searches that found an actual prisoner.
                    if(op.Phase==1 && op.TryPod && op.FortifyPrison && op.PodSuppliesIssued)
                    {
                        op.BoardS41 ??= new RHFTacticalBoardS41();
                        NativePrepareBeforeReleaseR051(op,now);
                    }
                    RHFRescueCommandV124.Infiltrate(this, op, now);
                }
                else if (op.Phase == 2)
                {
                    // No legacy fallback: it bypasses MissionExit and may
                    // deadlock when a squad has one downed captive.
                    if(!RHFRescueCommandV124.Extract(this, op, now))
                        Log.ErrorOnce("[RHF][ExtractS1] Rescue extraction has no controller: " +
                            op.CreatedTick, op.CreatedTick ^ 135220);
                }
                else if (op.Phase == 3) BuildAndLaunch(op, now);
                else if (op.Phase == 5) PostExtractionAssault(op, now);
                else if (op.Phase == 4)
                { RHFOffenseDirectorS41.End(op,map); op.Finished = true; operations.RemoveAt(i); }
                NJRPrisonerDefenseR047.Pulse(op,now);
            }
        }

        private void DeployVengeanceSupport(RHFOperation op)
        {
            op.SupportUsed=true;
            if(op.Parties==null)return;
            // Exactly one event-triggered spawn per selected party; no pawns
            // conjured by idle per-tick polling. No animal detachment from
            // unrelated native/vanilla Lord groups.
            foreach(RHFStrikeParty party in op.Parties)
                NJRForceCompositionS42.DeployBudgetedAnimals(op,party,map);
        }

        private void SearchMissingVip(RHFOperation op, int now)
        {
            // Searchers physically advance toward the dead VIP's last known prison
            // position. No global room query when no mission exists.
            bool reached = false;
            int ordered = 0;
            for (int i=0; i<op.Squad.Count; i++)
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
                    if (oldLord != null && !(oldLord.LordJob is RHFRescueLordJobV124))
                        oldLord.Notify_PawnLost(p, PawnLostCondition.LeftVoluntarily);
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
            for (int i = op.Squad.Count-1; i>=0; i--)
            {
                Pawn rescuer = op.Squad[i];
                if (rescuer == null || rescuer.Dead || rescuer.Map != map) { op.Squad.RemoveAt(i); continue; }
                if (rescuer.Downed) continue;
                // ALL rescue members leave vanilla assault command before any
                // cover/advance order. Otherwise a covering pawn may remain in
                // an assault lord and attack unrelated power infrastructure.
                // v0.13.3-S0: never strip a soldier from the RHF mission Lord that
                // RHFRescueCommandV124.ClaimGroup (OnDeploy, every decision cycle)
                // just gave him; only a foreign/vanilla Lord is relinquished.
                Lord oldLord = rescuer.GetLord();
                if (oldLord != null && !(oldLord.LordJob is RHFRescueLordJobV124))
                    oldLord.Notify_PawnLost(rescuer, PawnLostCondition.LeftVoluntarily);
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
                        // Real melee weapon damage only, no remote or timer damage.
                        // Do not restart each 90 ticks: that resets melee swings.
                        if (rescuer.jobs?.curJob?.def != JobDefOf.AttackMelee ||
                            rescuer.jobs.curJob.targetA.Thing != barrier)
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
                for (int i=0;i<op.Squad.Count;i++)
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
                // No arbitrary number of reroutes. A later real tactical
                // opportunity may exist even after earlier blocked attempts.
                // Operation-wide expiry and casualty retreat remain separate.
            }
        }

        private void HoldFreedUntilExtraction(RHFOperation op)
        {
            int issued=0;
            for(int i=0;i<op.Freed.Count;i++)
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
            for (int i=0;i<op.Targets.Count;i++)
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
            RHFOperation owner=FreedOwnerR028(p);
            if(owner!=null && owner!=op)return;
            // Claim BEFORE SetGuestStatus triggers the synchronous release hook.
            if (!op.Freed.Contains(p)) op.Freed.Add(p);
            if(op.FortifyPrison && p==op.Vip && !op.PodPrisonAnchorR043.IsValid)
                op.PodPrisonAnchorR043=p.Position;
            bool wasSlave = p.IsSlaveOfColony;
            // A breakout cancels ransom conditions. The player cannot collect payment.
            RHFHostageRecord record = Current.Game?.GetComponent<RHFGameLedger>()?.FindHostage(p);
            if (record != null) { record.Negotiated = false; record.PendingSilver = 0; record.PendingGoodwill = false; }
            p.guest.SetGuestStatus(null);
            // In RimWorld player slaves belong to the colony's faction; a
            // successful rescue restores their pre-enslavement allegiance.
            if (record?.OriginalFaction != null && !record.OriginalFaction.IsPlayer &&
                (wasSlave || p.Faction == null || p.Faction == Faction.OfPlayer))
                p.SetFaction(record.OriginalFaction);
            // Establish the escort BEFORE the former prisoner receives a free
            // think-tree decision. A still-active enemy lord must not commandeer it.
            EnsureEscort(op,p);
            RHFRescueCommandV124.StabilizeFreed(op,p);
            Pawn next = FindCaptive(op);
            if (next == null) // dead/escaped/invalid targets do not block extraction
            {
                op.Phase = op.TryPod && op.Vip != null && op.Vip.Map == map ? 3 : 2;
                op.PhaseStartTick = Find.TickManager.TicksGame;
                if (!op.ExitCell.IsValid && op.Squad.Count > 0)
                    op.ExitCell = ChooseExit(op.Squad[0].Position);
                // The liberated pawn must be claimed in THIS tick, not after
                // a 90-tick delay when their vanilla ExitMapBest job can loop.
                RHFRescueCommandV124.StabilizeFreed(op,p);
                Log.Message("[RHF][V131] Freed="+p.LabelShortCap+
                    "; phase="+op.Phase+"; pod="+op.TryPod+
                    "; financed="+op.PodBudgetPaid+
                    "; missionLord="+(p.GetLord()?.LordJob is RHFRescueLordJobV124));
                // Take an initial step together immediately instead of waiting
                // another 450 ticks while vanilla AI sends them different ways.
                if (!RHFRescuePlannerS41.Enabled && !p.Downed)
                {
                    RHFEscortLink link=FindEscort(op,p);
                    if (link?.Escort != null)
                        OrderGotoIfNeeded(p,link.Escort.Position,9);
                }
            }
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
            for (int i=0;i<op.Squad.Count;i++)
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

            int stillOnMap=0;
            bool waitingForAny=false;
            if (!op.ExitCell.IsValid || !op.ExitCell.InBounds(map))
                op.ExitCell = ChooseExit(op.Squad.Count>0 && op.Squad[0]!=null ?
                    op.Squad[0].Position : map.Center);
            for (int i=0;i<op.Freed.Count;i++)
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
                    if (captive.Spawned) TryAssignCarrier(op,captive);
                    waitingForAny=true;
                    continue;
                }
                if (!captive.Spawned) { waitingForAny=true; continue; }
                if (escort == null || !escort.Spawned)
                {
                    // No valid rescuer: NEVER show this as a successful escorted
                    // extraction. Let the captive seek the exit but keep the
                    // operation active for a possible replacement escort.
                    { OrderGotoIfNeeded(captive,op.ExitCell);  }
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
                    { OrderGotoIfNeeded(captive,escort.Position,16);  }
                    if (escort.jobs != null)
                    { Order(escort,JobDefOf.Wait,escort.Position);  }
                    waitingForAny=true;
                }
                else
                {
                    { OrderGotoIfNeeded(captive,op.ExitCell);  }
                    // An escort stays alongside the released prisoner instead
                    // of sprinting ahead, fighting and abandoning their charge.
                    {
                        if (separation > 49)
                            Order(escort,JobDefOf.Wait,escort.Position);
                        else OrderGotoIfNeeded(escort,op.ExitCell);
                        
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
                    Messages.Message("NJR: спасённые покинули карту. Оставшиеся штурмовики " +
                        "переходят к карательной фазе.",MessageTypeDefOf.ThreatSmall,false);
                    return;
                }
                for(int i=0;i<op.Squad.Count;i++)
                {
                    Pawn rescuer=op.Squad[i];
                    if (rescuer==null || rescuer.Dead || rescuer.Downed || rescuer.Map!=map) continue;
                    if ((rescuer.Position-op.ExitCell).LengthHorizontalSquared<=10)
                        rescuer.ExitMap(false,Rot4.Invalid);
                    else { OrderGotoIfNeeded(rescuer,op.ExitCell);  }
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

        internal bool TryAssignCarrier(RHFOperation op, Pawn captive)
        {
            JobDef rescueJob = DefDatabase<JobDef>.GetNamedSilentFail("RHF_CarryDowned");
            if (rescueJob == null || captive == null || !captive.Spawned ||
                !op.ExitCell.IsValid || !op.ExitCell.InBounds(map)) return false;
            // Do not send a second carrier while the first has a live carry job.
            // This is checked before selecting from otherwise idle rescuers.
            foreach(Pawn rescuer in op.Squad)
                if(rescuer?.Map==map && rescuer.jobs?.curJob?.def==rescueJob &&
                   rescuer.jobs.curJob.targetA.Thing==captive)return false; // already transporting; no new order
            Pawn chosen=null;
            int best=int.MaxValue;
            // All assigned rescuers are candidates; do not truncate the roster.
            for(int i=0;i<op.Squad.Count;i++)
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
                if(!pawn.CanReach(captive,PathEndMode.ClosestTouch,Danger.Deadly) ||
                   !pawn.CanReach(op.ExitCell,PathEndMode.OnCell,Danger.Deadly))continue;
                chosen=pawn;best=distance;
            }
            if(chosen == null)return false;
            Job job=JobMaker.MakeJob(rescueJob,captive,op.ExitCell);
            job.count=1;
            chosen.jobs.TryTakeOrderedJob(job);
            return true;
        }

        // S2.2: barricades protect the EXTRACTION SITE, never the captive's
        // door. Every candidate is simulated as impassable BEFORE it is built.
        // This bounded flood fill ensures a wall cannot seal the prisoner or
        // the engineers away from the launcher. Failure simply skips the wall.
        private bool OpenRouteExcept(IntVec3 source, IntVec3 destination, IntVec3 excluded)
        {
            if (!source.IsValid || !destination.IsValid || source == excluded ||
                destination == excluded || !source.InBounds(map) ||
                !destination.InBounds(map)) return false;
            int minX=Math.Max(0,Math.Min(source.x,destination.x)-15);
            int maxX=Math.Min(map.Size.x-1,Math.Max(source.x,destination.x)+15);
            int minZ=Math.Max(0,Math.Min(source.z,destination.z)-15);
            int maxZ=Math.Min(map.Size.z-1,Math.Max(source.z,destination.z)+15);
            var visited=new HashSet<IntVec3>();
            var pending=new Queue<IntVec3>();
            visited.Add(source);pending.Enqueue(source);
            while(pending.Count>0)
            {
                IntVec3 cell=pending.Dequeue();
                if(cell==destination)return true;
                for(int direction=0;direction<4;direction++)
                {
                    IntVec3 next=cell+(direction==0?IntVec3.North:
                        direction==1?IntVec3.South:
                        direction==2?IntVec3.East:IntVec3.West);
                    if(next.x<minX || next.x>maxX || next.z<minZ || next.z>maxZ ||
                        next==excluded || visited.Contains(next) ||
                        !next.InBounds(map))continue;
                    // The geometry-only preview must allow a CLOSED door to be
                    // opened or breached later. A wall remains impassable. This
                    // is an optimistic preview, not a claim that the VIP already
                    // has permission to open hostile doors; CanReach is checked
                    // again after the actual barricade has been constructed.
                    if(!next.Standable(map) &&
                       !(next.GetEdifice(map) is Building_Door))continue;
                    visited.Add(next);pending.Enqueue(next);
                }
            }
            return false;
        }

        private bool BarricadeLeavesExit(RHFOperation op,Pawn vip,IntVec3 candidate)
        {
            if(vip==null || vip.Map!=map || !op.PodCell.IsValid ||
               !op.PodLauncherCell.IsValid ||
               !OpenRouteExcept(vip.Position,op.PodCell,candidate))return false;
            Pawn engineer=FieldEngineer(op,op.PodLauncherCell);
            return engineer!=null &&
                OpenRouteExcept(engineer.Position,op.PodLauncherCell,candidate) &&
                OpenRouteExcept(engineer.Position,op.PodCell,candidate);
        }

        private IntVec3 BarricadeWorkCell(Pawn engineer,IntVec3 wall)
        {
            IntVec3 best=IntVec3.Invalid;int bestDistance=int.MaxValue;
            for(int direction=0;direction<4;direction++)
            {
                IntVec3 c=wall+(direction==0?IntVec3.North:
                    direction==1?IntVec3.South:
                    direction==2?IntVec3.East:IntVec3.West);
                if(!c.InBounds(map) || !c.Standable(map) ||
                   c.GetEdifice(map)!=null ||
                   !engineer.CanReach(c,PathEndMode.OnCell,Danger.Deadly))continue;
                int distance=(engineer.Position-c).LengthHorizontalSquared;
                if(distance<bestDistance){best=c;bestDistance=distance;}
            }
            return best;
        }

        private void TryBuildPrisonBarricade(RHFOperation op,Pawn vip,int now)
        {
            if(op==null || vip==null || !vip.Spawned || !op.PodCell.IsValid ||
               !op.PodLauncherCell.IsValid ||
               !HasFieldSteel(op,FieldCost(
                   DefDatabase<ThingDef>.GetNamedSilentFail("PodLauncher"),ThingDefOf.Steel)+
                   FieldCost(DefDatabase<ThingDef>.GetNamedSilentFail("TransportPod"),ThingDefOf.Steel)+10))
                return;
            if(!op.BarricadeCell.IsValid)
            {
                // A short defensive perimeter close to the launcher, never a
                // wall in the prison's only doorway. Not even attempt a wall
                // when no alternative traversable VIP/engineer route exists.
                IntVec3 center=op.PodLauncherCell;
                for(int radius=3;radius<=5 && !op.BarricadeCell.IsValid;radius++)
                for(int dx=-radius;dx<=radius && !op.BarricadeCell.IsValid;dx++)
                for(int dz=-radius;dz<=radius && !op.BarricadeCell.IsValid;dz++)
                {
                    if(Math.Max(Math.Abs(dx),Math.Abs(dz))!=radius)continue;
                    IntVec3 cell=new IntVec3(center.x+dx,0,center.z+dz);
                    if(!cell.InBounds(map) || !cell.Standable(map) ||
                       map.roofGrid.RoofAt(cell)!=null ||
                       cell.GetFirstBuilding(map)!=null ||
                       cell.GetFirstPawn(map)!=null ||
                       (cell-vip.Position).LengthHorizontalSquared<=9 ||
                       (cell-op.PodCell).LengthHorizontalSquared<=9 ||
                       (cell-op.PodLauncherCell).LengthHorizontalSquared<=9 ||
                       op.CompletedBarricades.Contains(cell) ||
                       !BarricadeLeavesExit(op,vip,cell))continue;
                    op.BarricadeCell=cell;
                }
                if(!op.BarricadeCell.IsValid)return;
                op.FortifyStartedTick=0;
            }
            Pawn engineer=FieldEngineer(op,op.BarricadeCell);
            if(engineer==null || !BarricadeLeavesExit(op,vip,op.BarricadeCell))
            {op.BarricadeCell=IntVec3.Invalid;op.FortifyStartedTick=0;return;}
            IntVec3 stand=BarricadeWorkCell(engineer,op.BarricadeCell);
            if(!stand.IsValid)
            {op.BarricadeCell=IntVec3.Invalid;op.FortifyStartedTick=0;return;}
            if(engineer.Position!=stand)
            {
                OrderGotoIfNeeded(engineer,stand,0);
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
            if(!op.BarricadeCell.Standable(map) ||
               op.BarricadeCell.GetFirstBuilding(map)!=null ||
               op.BarricadeCell.GetFirstPawn(map)!=null ||
               !BarricadeLeavesExit(op,vip,op.BarricadeCell))
            {op.BarricadeCell=IntVec3.Invalid;op.FortifyStartedTick=0;return;}
            try
            {
                Thing wall=ThingMaker.MakeThing(ThingDefOf.Wall,ThingDefOf.Steel);
                wall.SetFaction(engineer.Faction);
                GenSpawn.Spawn(wall,op.BarricadeCell,map);
                // A second validation against the ACTUALLY spawned obstacle:
                // if topology changed during work, dismantle our wall at once.
                bool accessible=vip.CanReach(op.PodCell,PathEndMode.OnCell,Danger.Deadly) &&
                    engineer.CanReach(op.PodLauncherCell,PathEndMode.Touch,Danger.Deadly);
                if(!accessible)
                {
                    wall.Destroy();
                    Log.Warning("[RHF][FortifyS22] Rolled back wall blocking VIP/engineer access.");
                }
                else
                {
                    SpendFieldSteel(op,10);
                    op.CompletedBarricades.Add(op.BarricadeCell);
                    op.BarricadesBuilt++;
                    Log.Message("[RHF][FortifyS22] Launcher defended; route verified "+op.BarricadeCell);
                }
            }
            catch(Exception e){Log.Warning("[RHF][FortifyS22] Wall aborted: "+e.Message);}
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
            for(int i=0;i<huntLimit;i++)
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
            for(int i=0;i<huntLimit;i++)
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
            for(int i=0;i<buildings.Count;i++)
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
            for(int i=first;i<attackers.Count;i++)
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
            // S4.1-A/G: no legacy direct jobs after extraction.
            if(RHFOffenseDirectorS41.Enabled)
            {
                RHFOffenseDirectorS41.Tick(op,map,now);
                if(now-op.PhaseStartTick>24000)
                { RHFOffenseDirectorS41.End(op,map); op.Phase=4; }
                return;
            }
            if(now-op.PhaseStartTick>24000){op.Phase=4;return;}
            // Existing rescued soldiers, not extra free enemies, now attack.
            // Review every live candidate and use native CE/vanilla attack verbs.
            int orders=0;
            for(int i=0;i<op.Squad.Count;i++)
            {
                Pawn fighter=op.Squad[i];
                if(fighter==null || fighter.Map!=map || fighter.Dead || fighter.Downed ||
                   fighter.jobs==null)continue;
                Pawn enemy=null;int best=625;
                foreach(Pawn candidate in map.mapPawns.FreeColonistsSpawned)
                {
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

        private bool FieldFootprintFree(IntVec3 cell, ThingDef def, Pawn vip,Rot4 rotation=default)
        {
            if(def==null || !cell.IsValid)return false;
            foreach(IntVec3 c in GenAdj.OccupiedRect(cell,rotation,def.size))
            {
                RoofDef roof=c.InBounds(map) ? map.roofGrid.RoofAt(c) : null;
                if(!c.InBounds(map) || !c.Standable(map) ||
                    (roof!=null && roof.isThickRoof) ||
                    c.GetEdifice(map)!=null ||
                    c.GetFirstBuilding(map)!=null || c==vip.Position)return false;
            }
            return true;
        }

        // Room-aware native build site. Priorities: prisoner room (if safe),
        // room connected through a real door, next reachable indoor room,
        // then the least exposed open yard. A thin roof is not magic launch
        // permission: the engineer must remove the pod roof with actual work.
        private bool FindFieldConstructionSite(Pawn vip,ThingDef launcher,ThingDef pod,
            out IntVec3 launchCell,out IntVec3 podCell,bool indoorOnlyR043=false,IntVec3 anchorR043=default)
        {
            launchCell=IntVec3.Invalid;podCell=IntVec3.Invalid;
            if(vip==null || !vip.Spawned || vip.Map!=map)return false;
            Room prison=(indoorOnlyR043?anchorR043:vip.Position).GetRoom(map);
            int bestScore=int.MaxValue,bestTier=int.MaxValue;
            // R019: evaluate every real candidate ONCE within a local
            // construction perimeter, not O(mapRadius^3) nested ring scans.
            // When no viable pad exists nearby, ground extraction is honest;
            // a launcher across the map is not a defensible rescue site.
            const int siteRadius=32;
            int freeFootprints=0,reachable=0,sameRoom=0;
            var roomTiers=new Dictionary<Room,int>();
            var activeTurrets=new List<Building>();
            foreach(Building gun in map.listerBuildings.allBuildingsColonist)
                if(RHFCombatPlannerV125.IsTurret(gun))activeTurrets.Add(gun);
            for(int x=Math.Max(0,vip.Position.x-siteRadius);
                x<=Math.Min(map.Size.x-1,vip.Position.x+siteRadius);x++)
            for(int z=Math.Max(0,vip.Position.z-siteRadius);
                z<=Math.Min(map.Size.z-1,vip.Position.z+siteRadius);z++)
            foreach(Rot4 rotation in indoorOnlyR043?NJRPodSiteClearS41.Rotations:new[]{Rot4.North})
            {
                if(rotation!=Rot4.North && !launcher.rotatable)continue;
                IntVec3 cell=new IntVec3(x,0,z);
                IntVec3 vehicle=FuelingPortUtility.GetFuelingPortCell(cell,rotation);
                if(!vehicle.InBounds(map))continue;
                int clearance=NJRPodSiteClearS41.Needed(map,cell,vehicle,
                    launcher,pod,vip,rotation);
                // Do not demolish existing walls or colony furniture for a
                // slightly closer site: search empty indoor footprints first.
                if(clearance!=0)continue;
                freeFootprints++;
                if(!vip.CanReach(cell,PathEndMode.OnCell,Danger.Deadly) ||
                   !vip.CanReach(vehicle,PathEndMode.OnCell,Danger.Deadly))continue;
                reachable++;
                Room candidate=cell.GetRoom(map);
                if(candidate==null || vehicle.GetRoom(map)!=candidate)continue;
                sameRoom++;
                // Keep roof-punch labor away from the captive, even if the
                // same prison room is large enough for the built structures.
                RoofDef roof=map.roofGrid.RoofAt(vehicle);
                // The roof work driver removes only the launch-cell thin
                // roof, never the VIP's tile. Reject thick mountain roofs,
                // not otherwise valid compact indoor prison-room footprints.
                if(roof!=null && roof.isThickRoof)continue;
                if(!roomTiers.TryGetValue(candidate,out int tier))
                {
                    tier = candidate==prison ? 0 :
                        !candidate.PsychologicallyOutdoors &&
                        NJRRescueInteriorS41.DirectDoor(map,prison,candidate,vip.Position) ? 1 :
                        !candidate.PsychologicallyOutdoors ? 2 : 3;
                    roomTiers[candidate]=tier;
                }
                if(indoorOnlyR043)
                {
                    if(!NJRRescueInteriorS41.ShelteredPadR043(map,cell,vehicle))continue;
                    if(candidate==prison && (!candidate.PsychologicallyOutdoors || (cell-anchorR043).LengthHorizontalSquared<=36))tier=0;
                    else if(tier!=1)tier=2;
                    if(tier>bestTier)continue;
                }
                int risk=0;
                foreach(Building turret in activeTurrets)
                {
                    // Count all real threats at the candidate launch cell.
                    if(!RHFCombatPlannerV125.IsTurret(turret))continue;
                    if(RHFObjectiveRouteV128.TurretCoversR037(turret,map,vehicle) ||
                       RHFObjectiveRouteV128.TurretCoversR037(turret,map,cell))risk+=16000;
                }
                int dist=(vehicle-vip.Position).LengthHorizontalSquared;
                // A room with a door and cover is preferable even if an
                // unprotected outdoor tile is geometrically closer.
                int score=tier*100000+risk+dist*6 +
                    (roof!=null ? 150 : 0);
                if((indoorOnlyR043 && tier<bestTier) || score<bestScore)
                {
                    if(indoorOnlyR043 && !NJRRescueAccessR040.DirectWorkLaneR043(map,vip,launcher,pod,cell,vehicle))continue;
                    bestScore=score;bestTier=tier;launchCell=cell;podCell=vehicle;
                }
            }
            Log.Message("[NJR][R040][PodSite] free="+freeFootprints+" reachable="+reachable+
                " sameRoom="+sameRoom+" launcher="+launchCell+" pod="+podCell);
            return launchCell.IsValid;
        }

        private bool HasFieldSteel(RHFOperation op,int required)
        {
            int total=0;
            IEnumerable<Thing> stocks=op.PodCargoStacksS41!=null &&
                op.PodCargoStacksS41.Count>0 ? op.PodCargoStacksS41 :
                (IEnumerable<Thing>)new Thing[]{op.PodSteel,op.PodSteelExtra};
            foreach(Thing stack in stocks)
                if(stack!=null && stack.def==ThingDefOf.Steel &&
                   !stack.Destroyed && stack.Map==map &&
                   (stack.Position-op.PodCell).LengthHorizontalSquared<=100)
                    total+=stack.stackCount;
            return total>=required;
        }

        private bool SpendFieldSteel(RHFOperation op,int amount)
        {
            if(!HasFieldSteel(op,amount))return false;
            IEnumerable<Thing> steelStocks=op.PodCargoStacksS41!=null &&
                op.PodCargoStacksS41.Count>0 ? op.PodCargoStacksS41 :
                (IEnumerable<Thing>)new Thing[]{op.PodSteel,op.PodSteelExtra};
            foreach(Thing stack in steelStocks)
            {
                if(amount==0)break;
                if(stack==null || stack.def!=ThingDefOf.Steel ||
                   stack.Destroyed || stack.Map!=map)continue;
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
            for(int i=0;i<op.Squad.Count;i++)
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
            op.TryPod=false;
            Log.Message("[RHF][VIP] Native transport fallback to ground exit: " + reason);
            Messages.Message("NJR: воздушная эвакуация VIP невозможна ("+reason+"). " +
                "Группа переходит к наземному отходу.",
                MessageTypeDefOf.ThreatSmall,false);
        }

        private void BuildAndLaunch(RHFOperation op,int now)
        {
            // S3.1.1 native path supersedes legacy hand-spawned buildings
            // and Skyfaller. Legacy BODY retained solely for historical
            // regression compatibility; NEVER called for a funded pod.
            if (op.TryPod || op.PodNativeS311)
            {
                if (op.PodSuppliesIssued && !op.PodNativeS311)
                { FallBackToLand(op,now,"legacy untracked kit cannot be upgraded mid-save"); return; }
                BuildAndLaunchNativeS311(op,now);
                return;
            }
            // All non-air operations withdraw; legacy handmade buildings are disabled.
            if (!op.TryPod)
            { FallBackToLand(op,now,"air-rescue only; legacy launch disabled"); return; }
            bool successfulDropRescue=false;
            if(op.Parties!=null)
                foreach(RHFStrikeParty party in op.Parties)
                    if(party!=null && party.Drop && party.Succeeded &&
                       party.Role==(int)RHFStrikeRole.Rescue)
                    {successfulDropRescue=true;break;}
            if(!successfulDropRescue)
            {
                FallBackToLand(op,now,"no successful drop-rescue unit to construct transport");
                return;
            }
            Pawn vip=op.Vip;
            if(vip!=null && op.Freed.Contains(vip) && vip.Spawned)
                RHFRescueCommandV124.StabilizeFreed(op,vip);
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
                if(steel-first>ThingDefOf.Steel.stackLimit ||
                    components>ThingDefOf.ComponentIndustrial.stackLimit)
                {
                    materials.Destroy();
                    if(extra!=null)extra.Destroy();
                    FallBackToLand(op,now,"modded transport cost exceeds current supply stack capacity");
                    return;
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
                Log.Message("[RHF][V131][Pod] Construction site="+op.PodLauncherCell+
                    "; transport="+op.PodCell+"; steel="+steel+
                    "; components="+components+"; squad="+op.Squad.Count);
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
            // S2.2.1: ONE WORK ORDER at a time. Barricade work takes the
            // engineer first, but ONLY in this bounded opening window. Never
            // let BuildAndLaunch countermand a fortification Goto/Wait every
            // 90 ticks; conversely, an unfinished wall cannot hold the launch
            // phase hostage when the window expires.
            bool fortificationOpen=op.PodConstructionStage==0 &&
                op.FortifyPrison && op.BarricadesBuilt<2 &&
                now-op.PhaseStartTick<3000;
            if(fortificationOpen)
            {
                int before=op.BarricadesBuilt;
                TryBuildPrisonBarricade(op,vip,now);
                if(op.BarricadeCell.IsValid || op.BarricadesBuilt>before)
                {
                    // Pause rather than advance the launcher timer while the
                    // same worker is building the protective wall.
                    op.PodConstructionTick=0;
                    return;
                }
            }
            else if(op.BarricadeCell.IsValid)
            {
                Log.Warning("[RHF][FortifyS221] Abandoning unfinished wall " +
                    "to free engineer for VIP launch, cell="+op.BarricadeCell);
                op.BarricadeCell=IntVec3.Invalid;
                op.FortifyStartedTick=0;
            }

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
                if(now-op.PhaseStartTick>1800 &&
                    (now-op.PhaseStartTick)%1800<90)
                    Log.Warning("[RHF][BuildS22] No engineer can reach stage="+
                        stage+"; site="+site+"; vip="+vip.Position+
                        "; elapsed="+(now-op.PhaseStartTick));
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
            // The engineer must actually be on site and capable of working;
            // time spent suppressed / fleeing CE fire is not build progress.
            if(RHFCombatPlannerV125.IsCombatEmergencyJob(engineer))
            {op.PodConstructionTick=0;return;}
            if(op.PodConstructionTick==0)
            {
                op.PodConstructionTick=now;
                Log.Message("[RHF][BuildS22] Engineer "+engineer.LabelShortCap+
                    " started stage="+stage+" at "+site);
            }
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
                Log.Message("[RHF][V131][Pod] Built="+construction.defName+
                    "; stage="+op.PodConstructionStage+"; VIP="+vip.LabelShortCap);
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
            for (int offset=0;offset<(edge<2?map.Size.z:map.Size.x);offset++)
            {
                int x = edge==0 ? 0 : edge==1 ? map.Size.x-1 : offset;
                int z = edge==2 ? 0 : edge==3 ? map.Size.z-1 : offset;
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
