// Raiders Have Families 0.3.1 — faction intervention system.
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

    public sealed class RHFOperation : IExposable
    {
        public Faction Sponsor;
        public Faction Executor;
        public Faction SecondSponsor;
        public List<Pawn> Targets = new List<Pawn>();
        public List<Pawn> Squad = new List<Pawn>();
        public List<Pawn> Freed = new List<Pawn>();
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
            }
        }
    }

    public sealed class RHFOperationController : MapComponent
    {
        private const int MaxOperationsPerMap = 4;
        private const int MaxSquad = 96;
        private const int MaxTargets = 24;
        private const int DecisionInterval = 300;
        private const int MaxOrdersPerDecision = 14;
        private const int MaxOperationDuration = 180000;
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
            nextDecision = operation.SpawnTick + 60;
            return true;
        }

        // Used only when scheduling a debug raid fails; otherwise remove finished records normally.
        public void CancelDebugOperation(RHFOperation operation)
        {
            if (operation != null && operation.DebugForced) operations.Remove(operation);
        }

        // Called by SpawnSetup; constant work and only if a special raid is pending.
        public void ObserveSpawn(Pawn pawn)
        {
            if (pawn == null || !pawn.Spawned || pawn.Faction == null || operations.Count == 0) return;
            int now = Find.TickManager.TicksGame;
            for (int i=0;i<operations.Count;i++)
            {
                RHFOperation op = operations[i];
                if (op.Finished || pawn.Faction != op.Executor || now < op.SpawnTick - 120 ||
                    now > op.SpawnTick + 3600 || op.Squad.Count >= MaxSquad ||
                    pawn.Dead || pawn.RaceProps.Humanlike == false) continue;
                if (!op.Squad.Contains(pawn)) op.Squad.Add(pawn);
                break;
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
                if (op.Type == (int)RHFOperationType.Vengeance)
                {
                    if (now >= op.SpawnTick && op.Squad.Count>0 && !op.SupportUsed)
                        DeployVengeanceSupport(op);
                    if (now > op.SpawnTick + 7200) operations.RemoveAt(i);
                    continue; // vanilla raid AI controls vengeance
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
                if (op.Phase == 1) Infiltrate(op, now);
                else if (op.Phase == 2) Extract(op, now);
                else if (op.Phase == 3) BuildAndLaunch(op, now);
                else if (op.Phase == 4) { op.Finished = true; operations.RemoveAt(i); }
            }
        }

        private void DeployVengeanceSupport(RHFOperation op)
        {
            op.SupportUsed=true;
            if(op.Executor?.def.techLevel>=TechLevel.Industrial || op.Squad.Count==0)
                return; // Higher-tech raids use vanilla strategy/arrival modes.
            PawnKindDef kind=DefDatabase<PawnKindDef>.GetNamedSilentFail("Warg") ??
                DefDatabase<PawnKindDef>.GetNamedSilentFail("Wolf");
            if(kind==null) return;
            Pawn leader=op.Squad[0];
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
                // The rescue coordinator takes command away from the vanilla assault lord.
                // Only for the special response squad, never ordinary raids.
                Lord oldLord = rescuer.GetLord();
                if (oldLord != null) oldLord.Notify_PawnLost(rescuer, PawnLostCondition.LeftVoluntarily);
                if ((rescuer.Position - target.Position).LengthHorizontalSquared <= 5)
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
            if(op.Executor?.def.techLevel < TechLevel.Industrial ||
                op.BreachChargesUsed>=2 || barrier==null || rescuer==null ||
                (barrier.Position-captive.Position).LengthHorizontalSquared<9 ||
                (rescuer.Position-barrier.Position).LengthHorizontalSquared>10) return false;
            if(op.BreachChargeTick==0)
            {
                op.BreachChargeTick=now;
                op.BreachCell=barrier.Position;
                Messages.Message("Спасатели устанавливают заряд у стены тюрьмы.",
                    barrier,MessageTypeDefOf.ThreatSmall,false);
                return true;
            }
            if(op.BreachCell!=barrier.Position) return true;
            if(now-op.BreachChargeTick>=900)
            {
                GenExplosion.DoExplosion(op.BreachCell,map,1.35f,DamageDefOf.Bomb,rescuer,58);
                op.BreachChargesUsed++;
                op.BreachChargeTick=0;
                op.BreachCell=IntVec3.Invalid;
                op.LastProgressTick=now;
            }
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
                if (p == null || p.Dead || p.Map != map || p.Downed) continue;
                if ((p.Position-op.ExitCell).LengthHorizontalSquared <= 10)
                { p.ExitMap(false, Rot4.Invalid); continue; }
                if (p.jobs?.curJob?.def != JobDefOf.Goto)
                { Order(p, JobDefOf.Goto, op.ExitCell); orderBudget--; }
            }
            // Keep a rearguard until living captives have crossed the map edge.
            bool allFree = true;
            for (int i=0;i<op.Freed.Count;i++)
                if (op.Freed[i] != null && !op.Freed[i].Dead && op.Freed[i].Map == map)
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
