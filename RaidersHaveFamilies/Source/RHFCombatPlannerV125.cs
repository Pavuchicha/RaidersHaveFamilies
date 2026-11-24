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
        public readonly Dictionary<int, int> LastTurretHp = new Dictionary<int, int>();
        public readonly Dictionary<int, int> BreachReadyAt = new Dictionary<int, int>();
        public readonly Dictionary<int, Thing> BreachTarget = new Dictionary<int, Thing>();
        // A covering shooter retains the assigned target and firing position
        // while moving. Otherwise the 90-tick rescue loop overwrites Goto and
        // sends the shooter into the turret at melee range.
        public readonly Dictionary<int, Thing> CoverTarget = new Dictionary<int, Thing>();
        public readonly Dictionary<int, IntVec3> CoverWaypoint = new Dictionary<int, IntVec3>();
        public readonly Dictionary<int, int> LastFlankPerPawn = new Dictionary<int, int>();
        public int LastScanTick = -9000;
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
        // No arbitrary combat search, target, squad or order ceilings.
        private const int ThreatRefreshTicks = 180;
        private const int FlankRefreshTicks = 900;
        private static readonly ConditionalWeakTable<RHFOperation, RHFCombatStateV125> States =
            new ConditionalWeakTable<RHFOperation, RHFCombatStateV125>();

        private static bool Active(Pawn pawn, Map map)
        {
            return pawn != null && pawn.Map == map && pawn.Spawned && !pawn.Dead &&
                   !pawn.Downed && pawn.jobs != null;
        }

        // Def-agnostic obstruction identification. This works for modded walls,
        // doors, materials and modded human-scale fortifications; no string search
        // for "wall" or any assumed material name. Furniture and machinery which
        // do not serve as an impassable edifice are excluded.
        internal static bool IsBreachableStructure(Thing obstacle)
        {
            if (!(obstacle is Building building) || obstacle.Destroyed ||
                obstacle.def == null || obstacle.Faction != Faction.OfPlayer ||
                !obstacle.def.destroyable || !obstacle.def.useHitPoints ||
                obstacle.HitPoints <= 0) return false;
            if (building is Building_Door) return true;
            return obstacle.def.IsEdifice() &&
                obstacle.def.passability == Traversability.Impassable;
        }

        // An opened door has a physical passage now. Never schedule a breach
        // through an opening just because the edifice still belongs to the player.
        // The next tactical replan will reconsider a door that closes later.
        internal static bool RequiresBreach(Thing obstacle)
        {
            return IsBreachableStructure(obstacle) &&
                !(obstacle is Building_Door door && door.Open);
        }

        // A single universal HP-based break cost shared by walls and doors.
        // Actual HitPoints already reflects stuff/material multipliers and damage.
        // No arbitrary per-type weight, name, or cap that makes 3000 HP look
        // the same as 500 HP. Fixed overhead covers switching to a breach job.
        internal static int BreachWorkCost(Thing obstacle)
        {
            if (!IsBreachableStructure(obstacle)) return -1;
            return 16 + Math.Min(1000000, obstacle.HitPoints) * 2;
        }

        // A budgeted A* route with a combat-appropriate STRUCTURAL cost model.
        // The vanilla PassAllDestroyableThings pathfinder can prefer a straight
        // line through three stone walls over a slightly longer corridor with
        // two doors. We compare the TOTAL cost of getting to the prisoner.
        // No global pawn tick, no artificial damage and no arbitrary door scan.
        private struct RouteFrontier
        {
            internal int Index;
            internal int Priority;
            internal RouteFrontier(int index, int priority)
            { Index=index; Priority=priority; }
        }
        private static void PushRoute(List<RouteFrontier> queue, int index, int priority)
        {
            RouteFrontier node=new RouteFrontier(index,priority);
            queue.Add(node);
            int i=queue.Count-1;
            while(i>0)
            {
                int parent=(i-1)/2;
                if(queue[parent].Priority<=priority)break;
                queue[i]=queue[parent];i=parent;
            }
            queue[i]=node;
        }
        private static RouteFrontier PopRoute(List<RouteFrontier> queue)
        {
            RouteFrontier first=queue[0];
            RouteFrontier last=queue[queue.Count-1];
            queue.RemoveAt(queue.Count-1);
            if(queue.Count==0)return first;
            int i=0;
            while(true)
            {
                int child=i*2+1;
                if(child>=queue.Count)break;
                if(child+1<queue.Count && queue[child+1].Priority<queue[child].Priority)
                    child++;
                if(queue[child].Priority>=last.Priority)break;
                queue[i]=queue[child];i=child;
            }
            queue[i]=last;
            return first;
        }
        private static int StructuralStep(Map map, IntVec3 cell, List<IntVec3> turrets)
        {
            Building block=cell.GetEdifice(map);
            int breachCost=0;
            if (block != null)
            {
                int damageWork=RequiresBreach(block) ? BreachWorkCost(block) : -1;
                if (damageWork>=0) breachCost=damageWork;
                else if (block is Building_Door && block.Faction!=Faction.OfPlayer)
                    breachCost=12; // accessible allied/neutral door, not a hostile breach
                else if (block.def.passability==Traversability.Impassable)
                    return -1; // non-destroyable infrastructure is not a breach route
            }
            else if(!cell.Standable(map))return -1;
            int danger=0;
            // Approximate static suppression envelopes without any expensive
            // per-cell LoS calls. Exact LoS is used later for shooting orders.
            for(int i=0;i<turrets.Count;i++)
            {
                int d=(cell-turrets[i]).LengthHorizontalSquared;
                if(d<169)danger+=40;
                else if(d<625)danger+=22;
                else if(d<1225)danger+=8;
            }
            return 10+breachCost+danger;
        }
        private static Thing WeightedObjectiveObstacle(Pawn rescuer, Pawn captive, out bool routeFound)
        {
            routeFound=false;
            Map map=rescuer.Map;
            IntVec3 origin=rescuer.Position, end=captive.Position;
            if(!origin.InBounds(map)||!end.InBounds(map))return null;
            // Examine the full physical map, not a 20-cell corridor.
            int minX=0,maxX=map.Size.x-1,minZ=0,maxZ=map.Size.z-1;
            int width=maxX-minX+1,height=maxZ-minZ+1;
            int count=width*height;
            var distance=new int[count];
            var parent=new int[count];
            for(int i=0;i<count;i++){distance[i]=int.MaxValue;parent[i]=-1;}
            int from=(origin.z-minZ)*width+origin.x-minX;
            int goal=(end.z-minZ)*width+end.x-minX;
            // The fallback structural route must not consider a hostile gun
            // absent merely because ordinary walls/furniture precede it in
            // allBuildingsColonist or twelve other turrets were registered first.
            // This scan runs only when planning an active rescue/breach route.
            var turrets=new List<IntVec3>(32);
            foreach(Building building in map.listerBuildings.allBuildingsColonist)
                if(IsTurret(building))turrets.Add(building.Position);
            var queue=new List<RouteFrontier>(64);
            distance[from]=0;
            PushRoute(queue,from,(Math.Abs(origin.x-end.x)+Math.Abs(origin.z-end.z))*10);
            int[] offsets={1,-1,width,-width};
            while(queue.Count>0)
            {
                RouteFrontier current=PopRoute(queue);
                int at=current.Index;
                int x=at%width+minX,z=at/width+minZ;
                int heuristic=(Math.Abs(x-end.x)+Math.Abs(z-end.z))*10;
                if(current.Priority!=distance[at]+heuristic)continue;
                if(at==goal)break;
                for(int direction=0;direction<4;direction++)
                {
                    int nx=x+(direction==0?1:direction==1?-1:0);
                    int nz=z+(direction==2?1:direction==3?-1:0);
                    if(nx<minX||nx>maxX||nz<minZ||nz>maxZ)continue;
                    int next=at+offsets[direction];
                    IntVec3 cell=new IntVec3(nx,0,nz);
                    int step=StructuralStep(map,cell,turrets);
                    if(step<0||distance[at]>int.MaxValue-step)continue;
                    int value=distance[at]+step;
                    if(value>=distance[next])continue;
                    distance[next]=value;parent[next]=at;
                    int remaining=(Math.Abs(nx-end.x)+Math.Abs(nz-end.z))*10;
                    PushRoute(queue,next,value+remaining);
                }
            }
            if(distance[goal]==int.MaxValue)return null;
            routeFound=true; // skip destructive vanilla fallback on a clear corridor
            // Follow the CHEAPEST complete route. Its first blocked element is
            // necessarily relevant to reaching the captive; an adjacent power
            // room's door cannot win just by being nearest to the prisoner.
            var chain=new List<int>(96);
            int cursor=goal;
            while(cursor!=from && cursor>=0)
            {chain.Add(cursor);cursor=parent[cursor];}
            if(cursor!=from)return null;
            for(int i=chain.Count-1;i>=0;i--)
            {
                int n=chain[i];
                IntVec3 cell=new IntVec3(n%width+minX,0,n/width+minZ);
                Building barrier=cell.GetEdifice(map);
                if(!IsBreachableStructure(barrier))continue;
                if(rescuer.CanReach(barrier.Position,PathEndMode.Touch,Danger.Deadly))
                    return barrier;
                // A geometrically planned wall is not proof it is physically
                // accessible to the breacher. Allow the guarded fallback.
                routeFound=false;
                return null;
            }
            return null;
        }

        // This is the first obstacle on a weighted route; vanilla destructible
        // pathfinding remains a bounded fallback when the search has no solution.
        internal static Thing FirstObjectiveObstacle(Pawn rescuer, Pawn captive, out bool clearRoute)
        {
            clearRoute=false;
            if (rescuer == null || captive == null || rescuer.Map == null ||
                rescuer.Map != captive.Map || !rescuer.Spawned || !captive.Spawned) return null;
            try
            {
                Map map = rescuer.Map;
                bool planned;
                Thing weighted = WeightedObjectiveObstacle(rescuer,captive,out planned);
                if(planned)
                {
                    // A complete open corridor is a valid answer: do not
                    // overwrite it with vanilla's shorter destroyable-wall path.
                    clearRoute=weighted==null;
                    return weighted;
                }
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

        internal static bool IsTurret(Building building)
        {
            if (building == null || building.Destroyed || !building.Spawned ||
                building.Faction != Faction.OfPlayer) return false;
            // Prefer actual combat capabilities over faction-specific names.
            if (building is Building_TurretGun ||
                building.def?.building?.turretGunDef != null) return true;
            // Compatibility fallback for modded turret classes that do not
            // expose vanilla building.turretGunDef.
            string typeName = building.GetType().Name;
            string defName = building.def?.defName ?? "";
            return typeName.IndexOf("Turret", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   defName.IndexOf("Turret", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   defName.IndexOf("Autocannon", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static bool TurretDamageProgress(RHFOperation op)
        {
            if(op==null || !States.TryGetValue(op,out RHFCombatStateV125 state))
                return false;
            bool damaged=false;
            foreach(Thing enemy in state.Threats)
            {
                if(!(enemy is Building turret) || !IsTurret(turret))continue;
                int id=enemy.thingIDNumber;
                if(state.LastTurretHp.TryGetValue(id,out int previous) &&
                   enemy.HitPoints<previous)damaged=true;
                state.LastTurretHp[id]=enemy.HitPoints;
            }
            return damaged;
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
            int limit = fighters.Count;
            for (int j = 0; j < limit; j++)
            {
                Pawn fighter = fighters[j];
                if (Active(fighter, map) &&
                    (fighter.Position - position).LengthHorizontalSquared <= 4225)
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
                state.ScannedPawns++;
                if (PlayerFighter(pawn, map) && RelevantThreat(pawn, op, map, captive, fighters))
                { state.Threats.Add(pawn); }
            }
            // Vanilla and CE turret buildings; do not misidentify generators as combat targets.
            foreach (Building building in map.listerBuildings.allBuildingsColonist)
            {
                state.ScannedBuildings++;
                if (IsTurret(building) && RelevantThreat(building, op, map, captive, fighters))
                { state.Threats.Add(building); }
            }
        }

        internal static float WeaponRange(Pawn pawn)
        {
            if (pawn?.equipment?.Primary?.def?.IsRangedWeapon != true) return 0f;
            List<VerbProperties> verbs = pawn.equipment.Primary.def.Verbs;
            float range = 0f;
            if (verbs != null)
                for (int i=0;i<verbs.Count;i++)
                    if (verbs[i] != null && verbs[i].range > range) range=verbs[i].range;
            // CE may substitute different effective verbs at runtime; this is only
            // a conservative tactical placement estimate, not a combat rules override.
            // A long-range weapon from a faction mod must retain its genuine
            // declared range. Never turn 80-cell fire support into 55-cell
            // sight simply to limit the tactical search.
            return Math.Max(0f,range);
        }

        // In CE these jobs are produced by combat/suppression logic, not by our
        // objective planner. In particular, never pre-empt a seek-cover or
        // suppression recovery job with a new 90-tick movement order.
        internal static bool IsCombatEmergencyJob(Pawn pawn)
        {
            if(pawn==null)return false;
            if(pawn.Downed || pawn.InMentalState)return true;
            // Combat Extended suppression may be a PRONE POSTURE inside a
            // vanilla combat job rather than a custom job named "Suppress".
            if(pawn.Spawned && pawn.jobs!=null &&
               (pawn.CurJobDef==JobDefOf.Wait_Combat ||
                pawn.CurJobDef==JobDefOf.AttackStatic) &&
               pawn.GetPosture()!=PawnPosture.Standing)return true;
            string name = pawn.jobs?.curJob?.def?.defName ?? "";
            return name.IndexOf("Suppress",StringComparison.OrdinalIgnoreCase)>=0 ||
                name.IndexOf("TakeCover",StringComparison.OrdinalIgnoreCase)>=0 ||
                name.IndexOf("RunForCover",StringComparison.OrdinalIgnoreCase)>=0 ||
                name.IndexOf("SeekCover",StringComparison.OrdinalIgnoreCase)>=0 ||
                name.IndexOf("Flee",StringComparison.OrdinalIgnoreCase)>=0 ||
                name.IndexOf("Reload",StringComparison.OrdinalIgnoreCase)>=0;
        }

        private static bool SafelyEngage(Pawn pawn, Thing enemy, Map map,
            Pawn captive, RHFOperation op)
        {
            if (pawn == null || enemy == null || !enemy.Spawned || map == null)
                return false;
            if (captive != null &&
                (enemy.Position-captive.Position).LengthHorizontalSquared <= 4)
                return false; // keep fire away from a captive under close guard
            if (!GenSight.LineOfSight(pawn.Position, enemy.Position, map)) return false;
            if (RHFRescueFireDisciplineV136.UnsafeLine(op,pawn,enemy)) return false;
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
            if (IsCombatEmergencyJob(pawn)) return false;
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

        // The cover wing itself must not use the vanilla Goto pathfinder to
        // cross a turret field just to reach a theoretically good firing tile.
        // This is a conservative sampling safeguard; no assumption about a
        // particular modded turret name or material.
        private static bool UnsafeCombatApproach(Map map,IntVec3 start,
            IntVec3 end,RHFCombatStateV125 state)
        {
            int deltaX=end.x-start.x,deltaZ=end.z-start.z;
            int steps=Math.Max(Math.Abs(deltaX),Math.Abs(deltaZ));
            if(steps<=1)return false;
            int samples=steps;
            int exposedSamples=0;
            for(int index=1;index<=samples;index++)
            {
                int at=index*steps/samples;
                IntVec3 point=new IntVec3(start.x+deltaX*at/steps,
                    0,start.z+deltaZ*at/steps);
                int overlapping=0;
                for(int i=0;i<state.Threats.Count;i++)
                {
                    if(!(state.Threats[i] is Building turret) ||
                       !turret.Spawned || !IsTurret(turret))continue;
                    float range=0f;
                    ThingDef gun=turret.def?.building?.turretGunDef;
                    if(gun?.Verbs!=null)
                        for(int j=0;j<gun.Verbs.Count;j++)
                            if(gun.Verbs[j]!=null && gun.Verbs[j].range>range)
                                range=gun.Verbs[j].range;
                    if(range<=0f)range=30f;
                    if((point-turret.Position).LengthHorizontalSquared>range*range)continue;
                    if(GenSight.LineOfSight(turret.Position,point,map))overlapping++;
                    if(overlapping>=2)return true; // no direct entry into crossfire
                }
                if(overlapping>0)exposedSamples++;
            }
            // A fire-team may advance briefly under ONE turret to enter its
            // own weapon range, but never march a long exposed corridor.
            return exposedSamples>4 && steps>12;
        }
        private static IntVec3 FlankCell(Pawn fighter, Thing enemy, Map map,
            RHFCombatStateV125 state)
        {
            if (fighter == null || enemy == null) return IntVec3.Invalid;
            float range = WeaponRange(fighter);
            // Use the gun's effective distance instead of a fixed 14-cell ring.
            // A 30-cell rifle must not be ordered to hug a turret at 14 cells.
            int desired=Math.Min(Math.Max(map.Size.x,map.Size.z),
                Math.Max(4,Mathf.FloorToInt(range>0f?range*0.85f:4f)));
            IntVec3 best = IntVec3.Invalid;
            float bestScore = float.MaxValue;
            // Exhaustively search feasible firing rings out to weapon range.
            for(int radius=desired;radius>=3;radius--)
            for(int dx=-radius;dx<=radius;dx++)
            for(int dz=-radius;dz<=radius;dz++)
            {
                if(Math.Max(Math.Abs(dx),Math.Abs(dz))!=radius)continue;
                IntVec3 c = new IntVec3(enemy.Position.x+dx,0,enemy.Position.z+dz);
                if (!c.InBounds(map) || !c.Standable(map) ||
                    !fighter.CanReach(c,PathEndMode.OnCell,Danger.Deadly)) continue;
                bool targetVisible = GenSight.LineOfSight(c,enemy.Position,map);
                if (range>0f && !targetVisible) continue;
                if(range>0f && (c-enemy.Position).LengthHorizontalSquared>range*range)
                    continue; // a visual position is not necessarily in firing range
                if(UnsafeCombatApproach(map,fighter.Position,c,state))continue;
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
                    (c-fighter.Position).LengthHorizontalSquared * 0.45f +
                    (c-enemy.Position).LengthHorizontalSquared * 0.08f;
                if(range>0f && (c-enemy.Position).LengthHorizontalSquared<
                    range*range*0.45f)score+=450f; // reject needlessly close positions
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
            // A defender actually firing at the group must not lose priority
            // to a nearby silent turret or an otherwise irrelevant building.
            if (enemy is Pawn activeShooter &&
                activeShooter.jobs?.curJob?.def == JobDefOf.AttackStatic)
                score += 850;
            if (GenSight.LineOfSight(rescuer.Position,enemy.Position,map)) score += 350;
            if (captive != null && (enemy.Position-captive.Position).LengthHorizontalSquared < 225)
                score += 90;
            if (assignments.TryGetValue(enemy,out int assigned)) score -= 400*assigned;
            return score;
        }

        private static int AliveCount(List<Pawn> fighters, Map map)
        {
            int total = 0;
            for(int i=0;i<fighters.Count;i++)
                if(Active(fighters[i],map))total++;
            return total;
        }

        private static bool AssignedEscort(RHFOperation op,Pawn fighter,Map map)
        {
            if(op?.Escorts==null)return false;
            for(int e=0;e<op.Escorts.Count;e++)
            {
                RHFEscortLink link=op.Escorts[e];
                if(link?.Escort==fighter && link.Captive!=null &&
                    link.Captive.MapHeld==map && !link.Captive.Dead)return true;
            }
            return false;
        }

        // Returns the support subset. Remaining mission pawns MUST continue
        // breaching / reaching / escorting rather than standing around in defence.
        internal static HashSet<Pawn> Response(RHFOperation op, Map map, Pawn captive,
            List<Pawn> fighters, int now, bool extraction, bool blockedByTurrets=false)
        {
            RHFCombatStateV125 state = States.GetValue(op,_=>new RHFCombatStateV125());
            state.Engaged.Clear();
            if (RHFMod.Settings?.TacticalCombat != true || map == null ||
                fighters == null || fighters.Count == 0) return state.Engaged;
            // Reservation is not limited by the fire-team size: every pawn
            // suppressed by CE keeps the native recovery/cover job.
            for (int i=0;i<fighters.Count;i++)
                if (Active(fighters[i],map) && IsCombatEmergencyJob(fighters[i]))
                    state.Engaged.Add(fighters[i]);
            Scan(op,map,captive,fighters,state,now);
            if (state.Threats.Count == 0)
            { state.Decision="активных защитников рядом с маршрутом не обнаружено";return state.Engaged; }
            int alive=AliveCount(fighters,map);
            int maxSupport=alive<=2 ? 1 : Mathf.Clamp(Math.Max(state.Threats.Count,alive/3),1,alive-2);
            // When the safe route ends at an unavoidable turret firing line,
            // most of the armed team fights it rather than waiting for the
            // entire breach squad to run through the killing zone.
            if(blockedByTurrets && alive>3)
                maxSupport=Math.Max(maxSupport,Math.Min(alive-2,(alive*2)/3));
            // Existing CE reservations are not counted as available shooters;
            // those pawns aren't in a position to provide reliable cover.
            maxSupport=Math.Min(alive,maxSupport+state.Engaged.Count);
            var assignments=new Dictionary<Thing,int>();
            // Keep previously assigned shooters on their standoff movement job.
            // Without this persistence the main rescue planner overwrites their
            // movement 90 ticks later and they charge a turret instead.
            for(int i=0;i<fighters.Count && state.Engaged.Count<maxSupport;i++)
            {
                Pawn pawn=fighters[i];
                if(!Active(pawn,map))continue;
                int id=pawn.thingIDNumber;
                if(!state.CoverTarget.TryGetValue(id,out Thing assigned) ||
                    assigned==null || assigned.Destroyed || !assigned.Spawned ||
                    !state.Threats.Contains(assigned))
                {state.CoverTarget.Remove(id);state.CoverWaypoint.Remove(id);continue;}
                if(extraction && AssignedEscort(op,pawn,map))continue;
                bool onTarget=SafelyEngage(pawn,assigned,map,captive,op);
                if(onTarget)
                {
                    JobDef def=WeaponRange(pawn)>0f?JobDefOf.AttackStatic:JobDefOf.AttackMelee;
                    if(GiveCombat(pawn,def,assigned,state,now))state.ShotsOrdered++;
                    state.Engaged.Add(pawn);
                }
                else if(state.CoverWaypoint.TryGetValue(id,out IntVec3 point) &&
                    point.IsValid && point.InBounds(map) &&
                    pawn.CanReach(point,PathEndMode.OnCell,Danger.Deadly))
                {
                    // Resume the waypoint if the Lord temporarily replaced it.
                    // Keep the fighter reserved even during the order cooldown.
                    if((pawn.Position-point).LengthHorizontalSquared<=5)
                    {
                        // An arrived shooter with no firing line must replan.
                        // Never reserve an idle cover slot forever.
                        state.CoverTarget.Remove(id);
                        state.CoverWaypoint.Remove(id);
                        continue;
                    }
                    GiveCombat(pawn,JobDefOf.Goto,point,state,now);
                    state.Engaged.Add(pawn);
                }
                else
                {state.CoverTarget.Remove(id);state.CoverWaypoint.Remove(id);continue;}
                assignments[assigned]=assignments.TryGetValue(assigned,out int used)?used+1:1;
            }
            for(int pass=0;pass<2 && state.Engaged.Count<maxSupport;pass++)
            for(int i=0;i<fighters.Count && state.Engaged.Count<maxSupport;i++)
            {
                Pawn fighter=fighters[i];
                if(!Active(fighter,map) || state.Engaged.Contains(fighter))continue;
                RHFTacticalMember member=RHFTactical.Member(op,fighter);
                bool specialist=member?.Role==(int)RHFCombatRole.Cover ||
                                member?.Role==(int)RHFCombatRole.Flanker;
                if((pass==0)!=specialist)continue;
                if(WeaponRange(fighter)<=0f && pass==0)continue;
                if(extraction && AssignedEscort(op,fighter,map))continue;
                Thing best=null;int score=int.MinValue;
                for(int j=0;j<state.Threats.Count;j++)
                {
                    Thing threat=state.Threats[j];
                    if(threat==null || !threat.Spawned || threat.Destroyed)continue;
                    int candidate=ThreatScore(threat,fighter,captive,map,assignments);
                    if(candidate>score){score=candidate;best=threat;}
                }
                if(best==null)continue;
                bool ordered=false;
                if(SafelyEngage(fighter,best,map,captive,op))
                {
                    JobDef attack=WeaponRange(fighter)>0f?JobDefOf.AttackStatic:JobDefOf.AttackMelee;
                    ordered=GiveCombat(fighter,attack,best,state,now);
                    if(ordered)state.ShotsOrdered++;
                }
                else if((fighter.Position-best.Position).LengthHorizontalSquared<=4225 &&
                        (!state.LastFlankPerPawn.TryGetValue(fighter.thingIDNumber,out int lastFlank) ||
                          now-lastFlank>=FlankRefreshTicks))
                {
                    IntVec3 flank=FlankCell(fighter,best,map,state);
                    if(flank.IsValid)
                    {
                        ordered=GiveCombat(fighter,JobDefOf.Goto,flank,state,now);
                        if(ordered)
                        {
                            state.ManeuversOrdered++;
                            state.LastFlankPerPawn[fighter.thingIDNumber]=now;
                            state.CoverWaypoint[fighter.thingIDNumber]=flank;
                        }
                    }
                }
                // A stationary shooter or a soldier moving toward a firing line
                // remains allocated to cover. Other soldiers pursue the hostage.
                if(!ordered)continue;
                assignments[best]=assignments.TryGetValue(best,out int used)?used+1:1;
                state.CoverTarget[fighter.thingIDNumber]=best;
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
            // Check each traversed cell instead of six samples that can miss
            // narrow turret corridors. Candidate staging tiles cover the real
            // map; performance is intentionally not a tactical cutoff.
            // Cache exact tile exposure within THIS tactical plan. Exhaustive
            // evaluation remains unrestricted, but does not redundantly call
            // LineOfSight for the same tile millions of times.
            var exposureCache=new int[map.Size.x*map.Size.z];
            for(int k=0;k<exposureCache.Length;k++)exposureCache[k]=-1;
            Func<IntVec3,int> exposureAt=cell=>
            {
                if(!cell.InBounds(map))return 0;
                int index=cell.z*map.Size.x+cell.x;
                if(exposureCache[index]>=0)return exposureCache[index];
                int exposure=0;
                foreach(Thing threat in state.Threats)
                    if(threat is Building gun && gun.Spawned &&
                       (gun.Position-cell).LengthHorizontalSquared<1225 &&
                       GenSight.LineOfSight(gun.Position,cell,map))exposure++;
                exposureCache[index]=exposure;
                return exposure;
            };
            int dangerOnDirectApproach=0;
            int directSteps=Math.Max(Math.Abs(captive.Position.x-leader.Position.x),
                Math.Abs(captive.Position.z-leader.Position.z));
            for(int segment=1;segment<=directSteps;segment++)
            {
                IntVec3 along=new IntVec3(
                    leader.Position.x+(captive.Position.x-leader.Position.x)*segment/directSteps,
                    0,leader.Position.z+(captive.Position.z-leader.Position.z)*segment/directSteps);
                if(!along.InBounds(map))continue;
                dangerOnDirectApproach+=exposureAt(along);
            }
            IntVec3 best=IntVec3.Invalid;
            int bestDanger=int.MaxValue;
            int bestRemaining=int.MaxValue;
            int bestTravel=int.MaxValue;
            int initialRemaining=(leader.Position-captive.Position).LengthHorizontalSquared;
            for(int x=0;x<map.Size.x;x++)
            for(int z=0;z<map.Size.z;z++)
            {
                IntVec3 cell=new IntVec3(x,0,z);
                int remaining=(cell-captive.Position).LengthHorizontalSquared;
                // A staging tile must make actual objective progress, not
                // relocate the squad to an arbitrarily distant safe corner.
                if(remaining>=initialRemaining || !cell.Standable(map) ||
                   cell.GetEdifice(map)!=null ||
                   !leader.CanReach(cell,PathEndMode.OnCell,Danger.Deadly))continue;
                int exposed=exposureAt(cell);
                int travel=(leader.Position-cell).LengthHorizontalSquared;
                int steps=Math.Max(Math.Abs(cell.x-leader.Position.x),
                    Math.Abs(cell.z-leader.Position.z));
                for(int segment=1;segment<=steps;segment++)
                {
                    IntVec3 along=new IntVec3(
                        leader.Position.x+(cell.x-leader.Position.x)*segment/steps,
                        0,leader.Position.z+(cell.z-leader.Position.z)*segment/steps);
                    if(!along.InBounds(map))continue;
                    exposed+=exposureAt(along);
                }
                if(exposed<bestDanger ||
                   (exposed==bestDanger && (remaining<bestRemaining ||
                    (remaining==bestRemaining && travel<bestTravel))))
                {bestDanger=exposed;bestRemaining=remaining;bestTravel=travel;best=cell;}
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
            // Stable pawn-specific ring preference, but every physical staging
            // cell remains eligible; there are no eight global formation slots.
            int radiusLimit=Math.Max(map.Size.x,map.Size.z);
            int wantedRing=1+Math.Abs(fighter.thingIDNumber%7);
            for(int phase=0;phase<2;phase++)
            for(int radius=phase==0?wantedRing:0;
                radius<=radiusLimit;radius++)
            {
                if(phase==1 && radius>=wantedRing)break;
                for(int dx=-radius;dx<=radius;dx++)
                for(int dz=-radius;dz<=radius;dz++)
                {
                    if(Math.Max(Math.Abs(dx),Math.Abs(dz))!=radius)continue;
                    IntVec3 cell=center+new IntVec3(dx,0,dz);
                    if(cell.InBounds(map) && cell.Standable(map) &&
                       cell.GetEdifice(map)==null &&
                       fighter.CanReach(cell,PathEndMode.OnCell,Danger.Deadly))
                        return cell;
                }
            }
            return center;
        }

        // S2.2: one obstacle has one selected breacher. Prefer a paid charge,
        // then an equipped melee specialist. A grenade is NOT a melee tool.
        internal static Pawn SelectBreachOperator(RHFOperation op,Map map,
            Thing barrier,HashSet<Pawn> covering)
        {
            if(op?.Squad==null || map==null || !IsBreachableStructure(barrier))return null;
            Pawn best=null;int bestScore=int.MinValue;
            for(int i=0;i<op.Squad.Count;i++)
            {
                Pawn pawn=op.Squad[i];
                if(!Active(pawn,map) || covering?.Contains(pawn)==true ||
                   IsCombatEmergencyJob(pawn) ||
                   pawn.WorkTagIsDisabled(WorkTags.Violent) ||
                   pawn.health?.capacities==null ||
                   !pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation) ||
                   !pawn.CanReach(barrier.Position,PathEndMode.Touch,Danger.Deadly))continue;
                RHFTacticalMember member=RHFTactical.Member(op,pawn);
                bool specialist=member?.Role==(int)RHFCombatRole.Breacher;
                ThingDef weapon=pawn.equipment?.Primary?.def;
                bool melee=weapon==null || weapon.IsMeleeWeapon;
                // Never ask a grenade-only fighter to attack a wall with the
                // grenade as a melee weapon. If nothing is safe, hold position.
                bool safeRifle=weapon?.IsRangedWeapon==true &&
                    weapon.defName.IndexOf("Grenade",StringComparison.OrdinalIgnoreCase)<0 &&
                    weapon.defName.IndexOf("Launcher",StringComparison.OrdinalIgnoreCase)<0 &&
                    weapon.defName.IndexOf("Rocket",StringComparison.OrdinalIgnoreCase)<0 &&
                    !RHFRescueFireDisciplineV136.UnsafeLine(op,pawn,barrier);
                // A safe rifle shot is preferred, never a prerequisite for
                // carrying out the ONLY physically reachable rescue breach.
                // Launchers/rifles with an unsafe firing lane use native
                // AttackMelee (weapon butt/fists); no grenades into the VIP.
                int skill=pawn.skills?.GetSkill(SkillDefOf.Melee)?.Level ?? 0;
                // Equipment, not the old nominal specialist role, decides priority.
                // Distance remains relevant among equally suitable weapons.
                int score=(RHFTactical.IsBreachAxe(pawn)?10000:0)+
                    (weapon?.IsMeleeWeapon==true?1200:0)+(specialist && melee?300:0)+
                    (safeRifle?200:0)+skill*15-
                    Math.Min(2000,(pawn.Position-barrier.Position).LengthHorizontalSquared/3);
                if(score>bestScore){best=pawn;bestScore=score;}
            }
            return best;
        }

        internal static JobDef BreachAttack(Pawn pawn)
        {
            if(pawn==null || pawn.WorkTagIsDisabled(WorkTags.Violent))return null;
            ThingDef weapon=pawn.equipment?.Primary?.def;
            if(weapon==null || weapon.IsMeleeWeapon)return JobDefOf.AttackMelee;
            if(weapon.IsRangedWeapon &&
                weapon.defName.IndexOf("Grenade",StringComparison.OrdinalIgnoreCase)<0 &&
                weapon.defName.IndexOf("Launcher",StringComparison.OrdinalIgnoreCase)<0 &&
                weapon.defName.IndexOf("Rocket",StringComparison.OrdinalIgnoreCase)<0)
                return JobDefOf.AttackStatic;
            // Explosive-only weapons must NEVER disqualify an otherwise valid
            // breacher: melee against the barrier remains physically legal.
            return JobDefOf.AttackMelee;
        }

        // Removed: damage is dealt only by the native attack JobDriver.
        internal static bool TryPaidBreach(RHFOperationController controller,
            RHFOperation op,Pawn captive,Thing barrier,Pawn rescuer,int now)
        {
            return false;
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
                result.AppendLine("Закреплённых огневых задач="+s.CoverTarget.Count+
                    ", огневых позиций="+s.CoverWaypoint.Count);
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
        [DebugAction("NJR - ТЕСТЫ | ДИАГНОСТИКА","30 — Угрозы, прикрытие и обход",
            actionType=DebugActionType.Action,allowedGameStates=AllowedGameStates.PlayingOnMap)]
        public static void Show()
        {Find.WindowStack.Add(new Dialog_MessageBox(RHFCombatPlannerV125.Diagnostic(Find.CurrentMap)));}
    }
}
