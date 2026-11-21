// RHF v0.12.5: objective-aware threat response, bounded flanking and deliberate breaching.
// This AI only acts on members of an existing RHF operation. It never patches every pawn tick.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RaidersHaveFamilies
{
    internal sealed class RHFCombatStateV125
    {
        public readonly List<Thing> Threats = new List<Thing>(24);
        public readonly HashSet<Pawn> Engaged = new HashSet<Pawn>();
        public readonly Dictionary<int, int> LastCombatOrder = new Dictionary<int, int>();
        public readonly Dictionary<int, int> BreachReadyAt = new Dictionary<int, int>();
        public readonly Dictionary<int, Thing> BreachTarget = new Dictionary<int, Thing>();
        public int LastScanTick = -9000;
        public int LastFlankTick = -9000;
        public int LastApproachTick = -9000;
        public int ScannedPawns;
        public int ScannedBuildings;
        public int ShotsOrdered;
        public int ManeuversOrdered;
        public int BreachToolsUsed;
        public IntVec3 Approach = IntVec3.Invalid;
        public IntVec3 ApproachFor = IntVec3.Invalid;
        public string Decision = "обстановка ещё не оценена";
    }

    internal static class RHFCombatPlannerV125
    {
        private const int MaxSquad = 96;
        private const int MaxThreats = 24;
        private const int MaxPawnScan = 160;
        private const int MaxStructureScan = 240;
        private const int MaxFlankCandidates = 20;
        private const int MaxTacticalOrders = 8;
        private const int ThreatRefreshTicks = 360;
        private const int FlankRefreshTicks = 900;
        private static readonly ConditionalWeakTable<RHFOperation, RHFCombatStateV125> States =
            new ConditionalWeakTable<RHFOperation, RHFCombatStateV125>();
        private static readonly MethodInfo BreachMethod =
            AccessTools.Method(typeof(RHFOperationController), "TryBreachCharge");

        private static bool Active(Pawn pawn, Map map)
        {
            return pawn != null && pawn.Map == map && pawn.Spawned && !pawn.Dead &&
                   !pawn.Downed && pawn.jobs != null;
        }

        internal static bool IsBreachableStructure(Thing obstacle)
        {
            if (!(obstacle is Building building) || obstacle.Destroyed ||
                obstacle.def == null || obstacle.Faction != Faction.OfPlayer) return false;
            if (building is Building_Door) return true;
            string name = obstacle.def.defName ?? "";
            // Do not demolish generators, batteries, furniture or production equipment
            // merely because they are impassable and happen to be near a prison.
            return obstacle.def.passability == Traversability.Impassable &&
                (name.IndexOf("wall", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 name.IndexOf("bulkhead", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 name.IndexOf("embrasure", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 name.IndexOf("barricade", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 name.IndexOf("fence", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        // This is the actual first obstruction on a destructible path to the captive,
        // not the nearest unrelated door. Re-evaluate after EACH layer is breached.
        internal static Thing FirstObjectiveObstacle(Pawn rescuer, Pawn captive)
        {
            if (rescuer == null || captive == null || rescuer.Map == null ||
                rescuer.Map != captive.Map || !rescuer.Spawned || !captive.Spawned) return null;
            try
            {
                Map map = rescuer.Map;
                using (PawnPath path = map.pathFinder.FindPathNow(
                    rescuer.Position, captive,
                    TraverseParms.For(rescuer, Danger.Deadly, TraverseMode.PassAllDestroyableThings),
                    null, PathEndMode.Touch))
                {
                    if (path == null || !path.Found) return null;
                    IntVec3 before;
                    Thing obstacle = path.FirstBlockingBuilding(out before, rescuer);
                    if (IsBreachableStructure(obstacle) &&
                        rescuer.CanReach(before, PathEndMode.OnCell, Danger.Deadly))
                        return obstacle;
                }
            }
            catch (Exception e)
            {
                if (RHFMod.Settings?.VerboseLogging == true)
                    Log.Warning("[RHF][CombatV125] Destructible route rejected: " +
                        e.GetBaseException().Message);
            }
            return null;
        }

        private static bool IsTurret(Building building)
        {
            if (building == null || building.Destroyed || !building.Spawned ||
                building.Faction != Faction.OfPlayer) return false;
            string typeName = building.GetType().Name;
            string defName = building.def?.defName ?? "";
            return typeName.IndexOf("Turret", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   defName.IndexOf("Turret", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   defName.IndexOf("Autocannon", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool PlayerFighter(Pawn pawn, Map map)
        {
            return Active(pawn, map) && pawn.Faction == Faction.OfPlayer &&
                pawn.RaceProps != null && pawn.RaceProps.Humanlike &&
                (pawn.equipment?.Primary != null || pawn.RaceProps.ToolUser);
        }

        private static bool RelevantThreat(Thing candidate, RHFOperation op, Map map,
            Pawn captive, List<Pawn> fighters)
        {
            if (candidate == null || candidate.Destroyed || !candidate.Spawned ||
                candidate.Map != map) return false;
            IntVec3 position = candidate.Position;
            if (captive != null && (position - captive.Position).LengthHorizontalSquared <= 625)
                return true;
            int limit = Math.Min(fighters.Count, MaxSquad);
            for (int j = 0; j < limit; j++)
            {
                Pawn fighter = fighters[j];
                if (Active(fighter, map) &&
                    (fighter.Position - position).LengthHorizontalSquared <= 1225)
                    return true;
            }
            return false;
        }

        private static void Scan(RHFOperation op, Map map, Pawn captive,
            List<Pawn> fighters, RHFCombatStateV125 state, int now)
        {
            if (now - state.LastScanTick < ThreatRefreshTicks) return;
            state.LastScanTick = now;
            state.Threats.Clear();
            state.ScannedPawns = state.ScannedBuildings = 0;
            if (map == null || fighters == null) return;
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (++state.ScannedPawns > MaxPawnScan) break;
                if (PlayerFighter(pawn, map) && RelevantThreat(pawn, op, map, captive, fighters))
                { state.Threats.Add(pawn); if (state.Threats.Count >= MaxThreats) return; }
            }
            // Vanilla and CE turret buildings; do not misidentify generators as combat targets.
            foreach (Building building in map.listerBuildings.allBuildingsColonist)
            {
                if (++state.ScannedBuildings > MaxStructureScan) break;
                if (IsTurret(building) && RelevantThreat(building, op, map, captive, fighters))
                { state.Threats.Add(building); if (state.Threats.Count >= MaxThreats) return; }
            }
        }

        private static float WeaponRange(Pawn pawn)
        {
            if (pawn?.equipment?.Primary?.def?.IsRangedWeapon != true) return 0f;
            List<VerbProperties> verbs = pawn.equipment.Primary.def.Verbs;
            float range = 0f;
            if (verbs != null)
                for (int i=0;i<verbs.Count;i++)
                    if (verbs[i] != null && verbs[i].range > range) range=verbs[i].range;
            // CE may substitute different effective verbs at runtime; this is only
            // a conservative tactical placement estimate, not a combat rules override.
            return Mathf.Clamp(range, 4f, 55f);
        }

        private static bool SafelyEngage(Pawn pawn, Thing enemy, Map map, Pawn captive)
        {
            if (pawn == null || enemy == null || !enemy.Spawned || map == null)
                return false;
            if (captive != null &&
                (enemy.Position-captive.Position).LengthHorizontalSquared <= 4)
                return false; // keep fire away from a captive under close guard
            if (!GenSight.LineOfSight(pawn.Position, enemy.Position, map)) return false;
            float range = WeaponRange(pawn);
            if (range <= 0f) return (pawn.Position-enemy.Position).LengthHorizontalSquared < 64;
            if ((pawn.Position-enemy.Position).LengthHorizontalSquared > range*range) return false;
            // Avoid scripted grenade fire near prisoners or other rescuers.
            ThingDef weapon = pawn.equipment?.Primary?.def;
            if (weapon != null && weapon.defName != null &&
                (weapon.defName.IndexOf("grenade", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 weapon.defName.IndexOf("launcher", StringComparison.OrdinalIgnoreCase) >= 0))
                return false; // vanilla/CE can still use these in ordinary self-defence
            return true;
        }

        private static bool GiveCombat(Pawn pawn, JobDef job, LocalTargetInfo target,
            RHFCombatStateV125 state, int now)
        {
            if (pawn?.jobs == null || job == null) return false;
            Job current = pawn.jobs.curJob;
            if (current != null && current.def == job &&
                (target.Thing != null ? current.targetA.Thing == target.Thing :
                                        current.targetA.Cell == target.Cell)) return true;
            if (state.LastCombatOrder.TryGetValue(pawn.thingIDNumber, out int recent) &&
                now-recent < 240) return false;
            try
            {
                pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(job,target));
                state.LastCombatOrder[pawn.thingIDNumber] = now;
                return pawn.jobs.curJob?.def == job;
            }
            catch (Exception e)
            {
                state.Decision = "приказ подавления не принят: " + e.GetBaseException().Message;
                return false;
            }
        }

        private static IntVec3 FlankCell(Pawn fighter, Thing enemy, Map map,
            RHFCombatStateV125 state)
        {
            if (fighter == null || enemy == null) return IntVec3.Invalid;
            float range = WeaponRange(fighter);
            int desired = Mathf.RoundToInt(range > 0f ? Mathf.Min(range*0.7f, 14f) : 4f);
            desired = Math.Max(4, desired);
            IntVec3 best = IntVec3.Invalid;
            float bestScore = float.MaxValue;
            int checks = 0;
            for (int r = 0; r < 2; r++)
            for (int side = 0; side < 8 && checks < MaxFlankCandidates; side++)
            {
                checks++;
                int radius = desired + r*3;
                int dx = side==0 ? radius : side==1 ? -radius :
                    side==2 || side==3 ? 0 : side==4 || side==5 ? radius : -radius;
                int dz = side==2 ? radius : side==3 ? -radius :
                    side==0 || side==1 ? 0 : side==4 || side==6 ? radius : -radius;
                IntVec3 c = new IntVec3(enemy.Position.x+dx,0,enemy.Position.z+dz);
                if (!c.InBounds(map) || !c.Standable(map) ||
                    !fighter.CanReach(c,PathEndMode.OnCell,Danger.Deadly)) continue;
                bool targetVisible = GenSight.LineOfSight(c,enemy.Position,map);
                if (range>0f && !targetVisible) continue;
                int otherThreats = 0;
                for (int t = 0; t < state.Threats.Count; t++)
                {
                    Thing other = state.Threats[t];
                    if (other == null || other == enemy || !other.Spawned) continue;
                    if ((other.Position-c).LengthHorizontalSquared < 900 &&
                        GenSight.LineOfSight(other.Position,c,map)) otherThreats++;
                }
                // Prefer firing lines that are NOT simultaneously exposed to all
                // enemy turrets; do not simply run straight into overlapping arcs.
                float score = otherThreats * 1000f +
                    (c-fighter.Position).LengthHorizontalSquared * 0.6f +
                    (c-enemy.Position).LengthHorizontalSquared * 0.1f;
                if (score < bestScore) {bestScore=score;best=c;}
            }
            return best;
        }

        private static int ThreatScore(Thing enemy, Pawn rescuer, Pawn captive, Map map,
            Dictionary<Thing,int> assignments)
        {
            if (enemy == null || !enemy.Spawned) return int.MinValue;
            int distance = (enemy.Position-rescuer.Position).LengthHorizontalSquared;
            int score = 1000 - Math.Min(distance,1000);
            if (enemy is Building) score += 350;
            if (GenSight.LineOfSight(rescuer.Position,enemy.Position,map)) score += 350;
            if (captive != null && (enemy.Position-captive.Position).LengthHorizontalSquared < 225)
                score += 90;
            if (assignments.TryGetValue(enemy,out int assigned)) score -= 400*assigned;
            return score;
        }

        private static int AliveCount(List<Pawn> fighters, Map map)
        {
            int total = 0;
            for(int i=0;i<fighters.Count && i<MaxSquad;i++)
                if(Active(fighters[i],map))total++;
            return total;
        }

        // Returns the support subset. Remaining mission pawns MUST continue
        // breaching / reaching / escorting rather than standing around in defence.
        internal static HashSet<Pawn> Response(RHFOperation op, Map map, Pawn captive,
            List<Pawn> fighters, int now, bool extraction)
        {
            RHFCombatStateV125 state = States.GetValue(op,_=>new RHFCombatStateV125());
            state.Engaged.Clear();
            if (RHFMod.Settings?.TacticalCombat != true || map == null ||
                fighters == null || fighters.Count == 0) return state.Engaged;
            Scan(op,map,captive,fighters,state,now);
            if (state.Threats.Count == 0)
            { state.Decision="активных защитников рядом с маршрутом не обнаружено";return state.Engaged; }
            int alive=AliveCount(fighters,map);
            int maxSupport=alive<=2 ? 1 : Mathf.Clamp(Math.Max(state.Threats.Count,alive/3),1,alive-2);
            maxSupport=Math.Min(maxSupport,MaxTacticalOrders);
            var assignments = new Dictionary<Thing,int>();
            // Preference for covering shooters; other armed members can respond
            // to fire if a nominal specialist has been killed or lacks ammunition.
            int evaluated=0;
            int evaluationBudget=Math.Min(32,Math.Max(12,maxSupport*3));
            for(int pass=0;pass<2 && state.Engaged.Count<maxSupport && evaluated<evaluationBudget;pass++)
            for(int i=0;i<fighters.Count && i<MaxSquad &&
                state.Engaged.Count<maxSupport && evaluated<evaluationBudget;i++)
            {
                Pawn fighter=fighters[i];
                if(!Active(fighter,map) || state.Engaged.Contains(fighter))continue;
                RHFTacticalMember member=RHFTactical.Member(op,fighter);
                bool specialist=member?.Role==(int)RHFCombatRole.Cover ||
                                member?.Role==(int)RHFCombatRole.Flanker;
                if((pass==0)!=specialist)continue;
                if(WeaponRange(fighter)<=0f && pass==0)continue;
                evaluated++;
                if(extraction && op.Escorts != null)
                {
                    bool assignedEscort=false;
                    for(int e=0;e<op.Escorts.Count;e++)
                    {
                        RHFEscortLink link=op.Escorts[e];
                        if(link?.Escort==fighter && link.Captive!=null &&
                            link.Captive.MapHeld==map && !link.Captive.Dead)
                        {assignedEscort=true;break;}
                    }
                    if(assignedEscort)continue; // never steal an assigned VIP escort
                }
                Thing best=null;int score=int.MinValue;
                for(int j=0;j<state.Threats.Count && j<MaxThreats;j++)
                {
                    Thing threat=state.Threats[j];
                    if(threat==null || !threat.Spawned || threat.Destroyed)continue;
                    int candidate=ThreatScore(threat,fighter,captive,map,assignments);
                    if(candidate>score){score=candidate;best=threat;}
                }
                if(best==null)continue;
                bool ordered=false;
                if(SafelyEngage(fighter,best,map,captive))
                {
                    JobDef attack=WeaponRange(fighter)>0f ? JobDefOf.AttackStatic : JobDefOf.AttackMelee;
                    ordered=GiveCombat(fighter,attack,best,state,now);
                    if(ordered)state.ShotsOrdered++;
                }
                else if(now-state.LastFlankTick>=FlankRefreshTicks &&
                        (fighter.Position-best.Position).LengthHorizontalSquared<=1600)
                {
                    IntVec3 flank=FlankCell(fighter,best,map,state);
                    if(flank.IsValid)
                    {
                        ordered=GiveCombat(fighter,JobDefOf.Goto,flank,state,now);
                        if(ordered){state.ManeuversOrdered++;state.LastFlankTick=now;}
                    }
                }
                // Do not reserve a useless 'support' pawn: if it cannot shoot,
                // flank or hold a meaningful fighting job it returns to rescue.
                if(!ordered)continue;
                assignments[best]=assignments.TryGetValue(best,out int used)?used+1:1;
                state.Engaged.Add(fighter);
            }
            state.Decision="угроз="+state.Threats.Count+", прикрытие="+
                state.Engaged.Count+", остальные продолжают " +
                (extraction ? "эвакуацию" : "поиск пленника и прорыв");
            return state.Engaged;
        }

        // Reposition primary objective team around static arcs when a safer
        // reachable staging point exists. This is a preference, not a teleport.
        internal static IntVec3 SafeApproach(RHFOperation op, Map map,
            Pawn leader, Pawn captive, int now)
        {
            if (op == null || map == null || leader == null || captive == null) return IntVec3.Invalid;
            RHFCombatStateV125 state=States.GetValue(op,_=>new RHFCombatStateV125());
            if (state.ApproachFor==captive.Position &&
                now-state.LastApproachTick<1200) return state.Approach;
            state.LastApproachTick=now;
            state.ApproachFor=captive.Position;
            state.Approach=IntVec3.Invalid;
            bool hasStaticDefense=false;
            for(int j=0;j<state.Threats.Count;j++)
                if(state.Threats[j] is Building && state.Threats[j].Spawned)
                {hasStaticDefense=true;break;}
            if(!hasStaticDefense) return IntVec3.Invalid;
            int dangerOnDirectApproach=0;
            for(int segment=1;segment<=6;segment++)
            {
                IntVec3 along=new IntVec3(
                    leader.Position.x+(captive.Position.x-leader.Position.x)*segment/7,
                    0,leader.Position.z+(captive.Position.z-leader.Position.z)*segment/7);
                if(!along.InBounds(map))continue;
                for(int j=0;j<state.Threats.Count;j++)
                    if(state.Threats[j] is Building guard && guard.Spawned &&
                       (guard.Position-along).LengthHorizontalSquared<1225 &&
                       GenSight.LineOfSight(guard.Position,along,map))
                       dangerOnDirectApproach++;
            }
            IntVec3 best=IntVec3.Invalid;
            int bestDanger=int.MaxValue;
            int bestTravel=int.MaxValue;
            for (int side=0;side<12;side++)
            {
                int distance=side<8?12:18;
                int dx = (side%4==0)?distance:(side%4==1)?-distance:
                    (side%4==2)?distance/2:-distance/2;
                int dz = (side%4==2)?distance:(side%4==3)?-distance:
                    (side%4==0)?distance/2:-distance/2;
                IntVec3 cell=new IntVec3(captive.Position.x+dx,0,captive.Position.z+dz);
                if (!cell.InBounds(map) || !cell.Standable(map) ||
                    !leader.CanReach(cell,PathEndMode.OnCell,Danger.Deadly))continue;
                int exposed=0;
                for(int j=0;j<state.Threats.Count;j++)
                    if(state.Threats[j] is Building tower && tower.Spawned &&
                       (tower.Position-cell).LengthHorizontalSquared<1225 &&
                       GenSight.LineOfSight(tower.Position,cell,map))exposed++;
                int travel=(leader.Position-cell).LengthHorizontalSquared;
                // Sample the approach corridor, not just the destination. It is
                // better to flank around a turret than to walk through its fire
                // to reach an otherwise safe cell behind the building.
                for(int segment=1;segment<=5;segment++)
                {
                    IntVec3 along=new IntVec3(
                        leader.Position.x+(cell.x-leader.Position.x)*segment/6,
                        0,leader.Position.z+(cell.z-leader.Position.z)*segment/6);
                    if(!along.InBounds(map))continue;
                    for(int j=0;j<state.Threats.Count;j++)
                        if(state.Threats[j] is Building guard && guard.Spawned &&
                           (guard.Position-along).LengthHorizontalSquared<1225 &&
                           GenSight.LineOfSight(guard.Position,along,map))exposed++;
                }
                if(exposed<bestDanger || (exposed==bestDanger && travel<bestTravel))
                {bestDanger=exposed;bestTravel=travel;best=cell;}
            }
            // Do not wait for the enemy turret to start shooting: compare the
            // projected kill corridor with an alternate approach in advance.
            if(best.IsValid && bestDanger+1<dangerOnDirectApproach)
                state.Approach=best;
            return state.Approach;
        }

        // Give the primary team several nearby assembly cells instead of
        // stacking an entire rifle squad on one grenade-sized tile.
        internal static IntVec3 FormationCell(IntVec3 center,Pawn fighter,Map map)
        {
            if(!center.IsValid || fighter==null || map==null)return center;
            int slot=Math.Abs(fighter.thingIDNumber%8);
            for(int pass=0;pass<8;pass++)
            {
                int i=(slot+pass)%8;
                int dx=i==0?0:i==1?2:i==2?-2:i==3?0:
                    i==4?2:i==5?-2:i==6?2:-2;
                int dz=i==0?0:i==1?0:i==2?0:i==3?2:
                    i==4?2:i==5?2:i==6?-2:-2;
                IntVec3 chosen=new IntVec3(center.x+dx,0,center.z+dz);
                if(chosen.InBounds(map) && chosen.Standable(map) &&
                   fighter.CanReach(chosen,PathEndMode.OnCell,Danger.Deadly))
                   return chosen;
            }
            return center;
        }

        internal static bool TryPaidBreach(RHFOperationController controller,
            RHFOperation op,Pawn captive,Thing barrier,Pawn rescuer,int now)
        {
            if (controller == null || op == null || captive == null || barrier == null ||
                rescuer == null || !IsBreachableStructure(barrier)) return false;
            RHFCombatStateV125 state=States.GetValue(op,_=>new RHFCombatStateV125());
            // Existing paid near-prison breaching takes precedence. Its real
            // inventory, timed charge and ally-safe damage rules are retained.
            if (BreachMethod != null &&
                (barrier.Position-captive.Position).LengthHorizontalSquared<=49)
            {
                try
                {
                    bool used=(bool)BreachMethod.Invoke(controller,
                        new object[]{op,captive,barrier,rescuer,now});
                    if(used)return true;
                }
                catch(Exception e)
                {
                    if(RHFMod.Settings?.VerboseLogging==true)
                        Log.Warning("[RHF][CombatV125] Breach API: "+e.GetBaseException().Message);
                }
            }
            // Outer perimeters may be many cells away from the hostage. They
            // still require a PHYSICAL breach kit paid for at squad creation.
            RHFTacticalMember member=RHFTactical.Member(op,rescuer);
            if (member==null || member.Role!=(int)RHFCombatRole.Breacher ||
                !RHFTactical.HasBreachKit(rescuer) || op.BreachChargesUsed>=4 ||
                (barrier.Position-rescuer.Position).LengthHorizontalSquared>16)
                return false;
            int id=rescuer.thingIDNumber;
            if(!state.BreachTarget.TryGetValue(id,out Thing previous) || previous!=barrier)
            {
                state.BreachTarget[id]=barrier;
                state.BreachReadyAt[id]=now+750;
                return true; // an actual sapper spends time placing the charge
            }
            if(now<state.BreachReadyAt[id])return true;
            state.BreachTarget.Remove(id);
            state.BreachReadyAt.Remove(id);
            if(!RHFTactical.ConsumeBreachKit(rescuer))return false;
            bool clearBlast=(rescuer.Position-barrier.Position).LengthHorizontalSquared>=9;
            if(clearBlast)
            {
                int checkedPawns=0;
                foreach(Pawn nearby in rescuer.Map.mapPawns.AllPawnsSpawned)
                {
                    if(++checkedPawns>MaxPawnScan){clearBlast=false;break;}
                    if(nearby==null || !nearby.Spawned || nearby.Dead)continue;
                    if((nearby.Position-barrier.Position).LengthHorizontalSquared<=9)
                    {clearBlast=false;break;}
                }
            }
            if(clearBlast)
                GenExplosion.DoExplosion(barrier.Position,rescuer.Map,1.15f,
                    DamageDefOf.Bomb,rescuer,52);
            else
                barrier.TakeDamage(new DamageInfo(DamageDefOf.Blunt,150f,0f,-1f,rescuer));
            op.BreachChargesUsed++;
            state.BreachToolsUsed++;
            return true;
        }

        // Vengeance and diversion retain their vanilla combat Lord. Upgrade
        // coverage against real defensive weapons, not random colony furniture.
        internal static void ImproveAttack(RHFOperation op,Map map,int now)
        {
            if(op == null || map == null || !op.Deployed || op.Finished ||
               RHFMod.Settings?.TacticalCombat != true || now%600>90)return;
            List<Pawn> wing=op.Diversion.Count>0?op.Diversion:op.Squad;
            if(wing==null || wing.Count==0)return;
            Response(op,map,op.HuntTarget,wing,now,false);
            // No forced movement for the rest: siege/assault/traitor Lords retain
            // their original doctrines, and shooting defenders happens naturally.
        }

        internal static string Diagnostic(Map map)
        {
            if(map==null)return "Нет карты.";
            var ctrl=map.GetComponent<RHFOperationController>();
            if(ctrl==null)return "Нет контроллера RHF.";
            var field=AccessTools.Field(typeof(RHFOperationController),"operations");
            var list=field?.GetValue(ctrl) as List<RHFOperation>;
            if(list==null || list.Count==0)return "Нет активных операций RHF.";
            var result=new StringBuilder(1200);
            result.AppendLine("RHF v0.12.5: тактический контроллер, огонь и обход");
            int checkedOps=0;
            foreach(RHFOperation op in list)
            {
                if(op==null || op.Finished || ++checkedOps>4)continue;
                var s=States.GetValue(op,_=>new RHFCombatStateV125());
                result.AppendLine("Операция "+checkedOps+": "+
                    (RHFDoctrine)op.Doctrine+", фаза="+op.Phase+
                    ", угроз="+s.Threats.Count+", бойцов прикрытия="+s.Engaged.Count);
                result.AppendLine("План: "+s.Decision+"; безопасный заход="+
                    (s.Approach.IsValid?s.Approach.ToString():"пока не найден"));
                result.AppendLine("Приказы огня="+s.ShotsOrdered+", обхода="+
                    s.ManeuversOrdered+", использование сапёрного средства="+
                    s.BreachToolsUsed+", осмотрено пешек/строений="+
                    s.ScannedPawns+"/"+s.ScannedBuildings);
            }
            return result.ToString();
        }
    }

    [HarmonyPatch(typeof(RHFTactical),nameof(RHFTactical.PressureAssault))]
    internal static class RHFCombatVengeancePatchV125
    {
        [HarmonyPostfix]
        private static void Postfix(Map map,RHFOperation op,int now)
        {RHFCombatPlannerV125.ImproveAttack(op,map,now);}
    }

    public static class RHFCombatAuditV125
    {
        [DebugAction("RHF - ТЕСТЫ | ДИАГНОСТИКА","30 — Угрозы, прикрытие и обход",
            actionType=DebugActionType.Action,allowedGameStates=AllowedGameStates.PlayingOnMap)]
        public static void Show()
        {Find.WindowStack.Add(new Dialog_MessageBox(RHFCombatPlannerV125.Diagnostic(Find.CurrentMap)));}
    }
}
