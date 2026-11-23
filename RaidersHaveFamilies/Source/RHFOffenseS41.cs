// RHF S4.1-A/G: faction-isolated tactical command for vengeance, diversion,
// and post-extraction punishment. The map planner writes only a SAVEABLE board.
// Pawn jobs are emitted exclusively by the associated ThinkTree JobGivers.
// Preserve NATIVE siege, animal group and other-mod Lord state machines.
using System;
using System.Collections.Generic;
using System.Text;
using LudeonTK;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace RaidersHaveFamilies
{
    public sealed class RHFOffenseLordJobS41 : LordJob
    {
        public string MissionKey = "";
        public bool Retreat;
        public RHFOffenseLordJobS41() { }
        internal RHFOffenseLordJobS41(string key) { MissionKey = key; }
        public override StateGraph CreateGraph()
        {
            var graph = new StateGraph();
            var toil = new RHFOffenseToilS41();
            graph.AddToil(toil);
            graph.StartingToil = toil;
            return graph;
        }
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref MissionKey,"rhfOffenseMissionS41","");
            Scribe_Values.Look(ref Retreat,"rhfOffenseRetreatS41",false);
        }
    }

    public sealed class RHFOffenseToilS41 : LordToil
    {
        public override void UpdateAllDuties()
        {
            if(lord?.ownedPawns==null)return;
            bool exit=(lord.LordJob as RHFOffenseLordJobS41)?.Retreat==true;
            DutyDef attack=DefDatabase<DutyDef>.GetNamedSilentFail("RHF_OffenseMissionS41");
            foreach(Pawn p in lord.ownedPawns)
            {
                if(p?.mindState==null || !p.Spawned)continue;
                if(exit)
                    p.mindState.duty=new PawnDuty(DutyDefOf.ExitMapBest);
                else if(attack!=null)
                    p.mindState.duty=new PawnDuty(attack,p.Position);
            }
        }
    }

    // The mission is authoritative only for its own currently registered pawns.
    // No globally patched ThinkTree, no command to an unrelated player raid.
    internal static class RHFOffenseDirectorS41
    {
        internal static bool Enabled => true;
        private const int ReplanTicks=270;
        // The mission inspects every member, observer, structure and viable flank.

        private static bool Active(Pawn p,Map map) =>
            p!=null && p.Spawned && p.Map==map && !p.Dead && !p.Downed &&
            !p.InMentalState && p.jobs!=null;

        // Key contains the role and faction identity, never the random index
        // of a spawned pawn. Persisted by LordJob so reloads are deterministic.
        private static string Key(RHFOperation op,Faction faction,int wing) =>
            "RHF:S41:"+op.CreatedTick+":"+op.SpawnTick+":"+
            wing+":"+faction.GetUniqueLoadID();

        internal static RHFOperation MissionFor(Pawn soldier)
        {
            Map map=soldier?.Map;
            RHFOffenseLordJobS41 job=soldier?.GetLord()?.LordJob as RHFOffenseLordJobS41;
            if(map==null || job==null || job.Retreat)return null;
            List<RHFOperation> ops=RHFMissionIntegrityV130.Operations(map);
            if(ops==null)return null;
            for(int i=0;i<ops.Count;i++)
            {
                RHFOperation op=ops[i];
                if(op==null || !op.Deployed || op.Finished ||
                   op.OffenseBoardS41?.For(soldier,false)==null)continue;
                if(op.Type==(int)RHFOperationType.Vengeance)
                {
                    if(op.Diversion!=null && op.Diversion.Contains(soldier) &&
                       job.MissionKey==Key(op,soldier.Faction,2))return op;
                    if(op.Squad!=null && op.Squad.Contains(soldier) &&
                       job.MissionKey==Key(op,soldier.Faction,3))return op;
                }
                if(op.Type==(int)RHFOperationType.Rescue)
                {
                    if(op.Diversion!=null && op.Diversion.Contains(soldier) &&
                       job.MissionKey==Key(op,soldier.Faction,1))return op;
                    if(op.Phase==5 && op.Squad!=null &&
                       op.Squad.Contains(soldier) &&
                       job.MissionKey==Key(op,soldier.Faction,4))return op;
                }
            }
            return null;
        }

        internal static void Tick(RHFOperation op,Map map,int now)
        {
            if(op==null || !op.Deployed || op.Finished || map==null)return;
            bool vengeance=op.Type==(int)RHFOperationType.Vengeance;
            bool rescue=op.Type==(int)RHFOperationType.Rescue;
            if(!vengeance && !rescue)return;
            // Observe native Lords without changing their jobs or pawns.
            RHFNativeDoctrineS41.Observe(op,map,now);
            // Rescue squad stays entirely under RHFRescueLordJobV124 until the
            // real hostage has left. Diversion soldiers are an independent wing.
            RHFTacticalBoardS41 board=op.OffenseBoardS41 ??= new RHFTacticalBoardS41();
            board.Prune(map);
            bool replan=now>=board.NextReplanTick || board.ObservedPhase!=op.Phase ||
                HasInvalidActiveOrders(board,map);
            if(rescue && op.Diversion!=null && op.Diversion.Count>0)
            {
                // The diversion covers ongoing extraction AND pod assembly;
                // don't send it home as soon as Phase 2/3 begins. Release
                // only after the captive is actually gone from this map.
                bool liveEvac=op.Phase==3 || CaptivesStillOnMap(op,map);
                if(op.Phase==4 ||
                   (op.Phase==2 && !liveEvac &&
                    (op.EscapedWithEscort>0 || op.PodLaunchCompleted)))
                    ReleaseWing(op,op.Diversion,map);
                else
                    PlanWing(op,op.Diversion,map,now,1,replan);
            }
            if(vengeance)
            {
                if(op.Diversion!=null && op.Diversion.Count>0)
                    PlanWing(op,op.Diversion,map,now,2,replan);
                if(op.Squad!=null && op.Squad.Count>0)
                    PlanWing(op,op.Squad,map,now,3,replan);
            }
            if(rescue && op.Phase==5 && op.Squad!=null && op.Squad.Count>0)
                PlanWing(op,op.Squad,map,now,4,replan);
            if(replan)
            {
                board.NextReplanTick=now+ReplanTicks;
                board.ObservedPhase=op.Phase;
                board.LastPlanTick=now;
            }
        }

        // A destroyed breach target or lost defender must not leave the whole
        // wing doing nothing until the next arbitrary 270-tick planning slot.
        // Re-evaluate at the next existing operation pulse, not every game tick.
        private static bool HasInvalidActiveOrders(RHFTacticalBoardS41 board,Map map)
        {
            if(board?.Orders==null)return false;
            foreach(RHFOrderS41 order in board.Orders)
            {
                if(order?.Member==null || !Active(order.Member,map))continue;
                Thing target=order.Objective;
                if(target!=null && (target.Destroyed || !target.Spawned ||
                   target.Map!=map ||
                   (target is Pawn && target.Faction!=Faction.OfPlayer) ||
                   (target is Building && target.Faction!=Faction.OfPlayer)))
                    return true;
                if((RHFRoleS41)order.Role==RHFRoleS41.Breach &&
                   !RHFCombatPlannerV125.RequiresBreach(target))return true;
            }
            return false;
        }

        private static bool CaptivesStillOnMap(RHFOperation op,Map map)
        {
            if(op?.Targets!=null)
                for(int i=0;i<op.Targets.Count;i++)
                {
                    Pawn captive=op.Targets[i];
                    if(captive!=null && !captive.Dead && captive.Map==map)
                        return true;
                }
            if(op?.Freed!=null)
                for(int i=0;i<op.Freed.Count;i++)
                {
                    Pawn captive=op.Freed[i];
                    if(captive!=null && !captive.Dead && captive.Map==map)
                        return true;
                }
            return false;
        }

        private static bool NativeOwner(RHFOperation op,Pawn p)
        {
            Lord lord=p?.GetLord();
            if(lord?.LordJob is LordJob_Siege)return true;
            if(p?.RaceProps?.Humanlike!=true)return true; // fauna never claimed
            if(RHFNativeDoctrineS41.IsAnimalLord(lord))
                return true; // keep animal handlers with their native Lord
            // Only protect still-unclaimed pawns during pending siege spawn.
            // Previously, one siege party excluded every soldier of its entire
            // faction, including a completely independent support wing.
            if(lord==null && op.Parties!=null)
                foreach(RHFStrikeParty party in op.Parties)
                    if(party!=null && party.Faction==p.Faction && party.Siege &&
                       op.Doctrine==(int)RHFDoctrine.SiegeAndRaze)
                        return true;
            return false;
        }

        private static void PlanWing(RHFOperation op,List<Pawn> wing,Map map,int now,int wingId,
            bool replan)
        {
            if(wing==null || wing.Count==0)return;
            RHFTacticalBoardS41 board=op.OffenseBoardS41 ??= new RHFTacticalBoardS41();
            board.Prune(map);
            // One Lord per *faction AND wing*. Cross-faction coalition pawns
            // are never inserted into a Lord belonging to another faction.
            var visited=new HashSet<Faction>();
            for(int i=0;i<wing.Count;i++)
            {
                Pawn leader=wing[i];
                if(!Active(leader,map) || leader.Faction==null ||
                   !visited.Add(leader.Faction))continue;
                var factionMembers=new List<Pawn>(32);
                // Per-pawn native ownership, never a faction-wide veto.
                // Siege artillery + beast handlers stay in their *own Lords*;
                // an independent supporting raid from the same faction can act.
                for(int j=0;j<wing.Count;j++)
                {
                    Pawn candidate=wing[j];
                    if(!Active(candidate,map) || candidate.Faction!=leader.Faction ||
                       candidate.RaceProps?.Humanlike!=true ||
                       NativeOwner(op,candidate))continue;
                    factionMembers.Add(candidate);
                }
                if(factionMembers.Count==0)continue;
                ClaimFaction(op,map,factionMembers,wingId);
                bool unassigned=false;
                foreach(Pawn member in factionMembers)
                    if(board.For(member,false)==null){unassigned=true;break;}
                if(replan || unassigned)
                    PlanFaction(op,board,map,factionMembers,wingId,now);
            }
            // Tick publishes replanning deadline AFTER all factional wings
            // have had a turn to write their own orders.
        }

        private static void ClaimFaction(RHFOperation op,Map map,
            List<Pawn> members,int wing)
        {
            if(members.Count==0)return;
            Faction faction=members[0].Faction;
            string key=Key(op,faction,wing);
            Lord destination=null;
            foreach(Pawn p in members)
            {
                Lord current=p.GetLord();
                if(current?.LordJob is RHFOffenseLordJobS41 active &&
                   !active.Retreat && active.MissionKey==key)
                { destination=current; break; }
            }
            if(destination==null)
            {
                foreach(Pawn p in members)
                {
                    Lord previous=p.GetLord();
                    if(previous!=null)previous.Notify_PawnLost(
                        p,PawnLostCondition.LeftVoluntarily);
                }
                try
                {
                    destination=LordMaker.MakeNewLord(faction,
                        new RHFOffenseLordJobS41(key),map,members);
                }
                catch(Exception ex)
                {
                    Log.ErrorOnce("[RHF S41][Offense] Lord claim failed: "+ex,
                        op.CreatedTick ^ faction.GetHashCode() ^ wing);
                    return;
                }
            }
            else foreach(Pawn p in members)
            {
                if(p.GetLord()==destination)continue;
                Lord previous=p.GetLord();
                if(previous!=null)previous.Notify_PawnLost(
                    p,PawnLostCondition.LeftVoluntarily);
                destination.AddPawn(p);
            }
            // Newly created Lords assign their duties on Init. Repeating the
            // assignment every 90 ticks would also reset duty focus in combat.
        }

        private static bool Seen(Pawn observer,Thing target,Map map)
        {
            // Player fog-of-war is NOT the enemy faction's knowledge.
            // Use physical attacker-target LoS; no global target coordinates.
            if(!Active(observer,map) || target==null || !target.Spawned ||
               target.Map!=map)return false;
            // Physical sight, not a hard-coded 60-tile observation ceiling.
            // The target must be seen by this actual pawn, not by the player.
            return GenSight.LineOfSight(observer.Position,target.Position,map);
        }

        // Detect the actual person only after physical observation. The last
        // confirmed cell has a short lifetime; teleporting a traitor does not
        // give the raiders omniscient tracking.
        private static bool ObserveHunt(RHFOperation op,List<Pawn> wing,Map map,int now)
        {
            Pawn target=op.HuntTarget;
            if(target==null || target.Dead || !target.Spawned ||
               target.Map!=map || target.Faction!=Faction.OfPlayer)return false;
            // Count actual active observers, not roster slots. An injured
            // scout at index 0 must not consume scarce reconnaissance budget.
            for(int i=0;i<wing.Count;i++)
            {
                Pawn scout=wing[i];
                if(!Active(scout,map))continue;
                if(Seen(scout,target,map))
                {
                    op.HuntLastSeenCell=target.Position;
                    op.HuntLastSeenTick=now;
                    return true;
                }
            }
            return false;
        }

        // The faction learns a threat only if a living member can see it.
        // All calls are bounded and occur during the 270-tick planning pulse.
        private static bool TeamSees(List<Pawn> team,Thing target,Map map)
        {
            if(team==null || target==null)return false;
            for(int i=0;i<team.Count;i++)
            {
                Pawn scout=team[i];
                if(!Active(scout,map))continue;
                if(Seen(scout,target,map))return true;
            }
            return false;
        }

        private static List<Building> KnownTurrets(RHFTacticalBoardS41 board,
            Faction faction,List<Pawn> members,Map map,int now)
        {
            // A faction remembers a turret its scouts actually observed. If a
            // door later hides the gun, its last-known stationary position is
            // still considered when evaluating a new breach route. Destroyed
            // turrets and vanished map objects are pruned by board.Prune.
            var threats=new List<Building>(16);
            if(board==null || faction==null)return threats;
            string key=faction.GetUniqueLoadID();
            if(board.ObservedTurretsS46==null)
                board.ObservedTurretsS46=new List<RHFObservedTurretS46>();
            List<RHFObservedTurretS46> memory=board.ObservedTurretsS46;
            foreach(Building turret in map.listerBuildings.allBuildingsColonist)
            {
                if(!RHFCombatPlannerV125.IsTurret(turret) ||
                   !TeamSees(members,turret,map))continue;
                bool alreadyKnown=false;
                foreach(RHFObservedTurretS46 entry in memory)
                    if(entry!=null && entry.FactionKey==key && entry.Turret==turret)
                    {entry.LastSeenTick=now;alreadyKnown=true;break;}
                if(!alreadyKnown)
                    memory.Add(new RHFObservedTurretS46(key,turret,now));
            }
            foreach(RHFObservedTurretS46 entry in memory)
                if(entry!=null && entry.FactionKey==key &&
                   entry.Turret is Building turret && turret.Spawned &&
                   !turret.Destroyed && turret.Map==map &&
                   RHFCombatPlannerV125.IsTurret(turret))
                    threats.Add(turret);
            return threats;
        }

        // A turret registered first by the map's building list may be at the
        // other end of the settlement. Use the nearest OBSERVED turret to an
        // active soldier; all candidates have already passed TeamSees.
        private static Building NearestObservedTurret(List<Pawn> members,
            List<Building> observed,Map map)
        {
            Building best=null;
            int nearest=int.MaxValue;
            foreach(Building gun in observed)
            {
                if(!RHFCombatPlannerV125.IsTurret(gun))continue;
                foreach(Pawn soldier in members)
                {
                    if(!Active(soldier,map))continue;
                    int distance=(soldier.Position-gun.Position).LengthHorizontalSquared;
                    if(distance<nearest)
                    {nearest=distance;best=gun;}
                }
            }
            return best;
        }

        // Orders may contain a static last-seen location shared by scouts;
        // they must NOT contain the live Pawn object for soldiers who have no
        // physical line of sight. That would track a moving defector through rock.
        private static Thing IndividuallyConfirmed(Pawn soldier,Thing objective,Map map)
        {
            return objective is Pawn && !Seen(soldier,objective,map) ?
                null : objective;
        }

        private static Thing VisibleObjective(List<Pawn> team,Map map)
        {
            Thing best=null;int score=int.MinValue;
            var sites=map.listerBuildings.allBuildingsColonist;
            for(int k=0;k<sites.Count;k++)
            {
                Building b=sites[k];
                if(b==null || !b.Spawned || b.Destroyed ||
                   b.Faction!=Faction.OfPlayer)continue;
                if(!TeamSees(team,b,map))continue;
                string n=b.def?.defName??"";
                int s=b is Building_TurretGun ? 950 :
                    n.IndexOf("Turret",StringComparison.OrdinalIgnoreCase)>=0?900:
                    n.IndexOf("Generator",StringComparison.OrdinalIgnoreCase)>=0?740:
                    n.IndexOf("Power",StringComparison.OrdinalIgnoreCase)>=0?700:
                    n.IndexOf("Battery",StringComparison.OrdinalIgnoreCase)>=0?630:
                    n.IndexOf("Comms",StringComparison.OrdinalIgnoreCase)>=0?520:130;
                if(s>score){score=s;best=b;}
            }
            return best;
        }

        private static Thing NearestVisibleEnemy(RHFOperation op,List<Pawn> team,Map map)
        {
            Pawn best=null;int score=int.MaxValue;
            foreach(Pawn p in map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer))
            {
                if(p==null || p.Dead || p.Downed ||
                   (p!=op.HuntTarget &&
                   (op.Targets?.Contains(p)==true || op.Freed?.Contains(p)==true)) ||
                   op.Squad?.Contains(p)==true || op.Diversion?.Contains(p)==true)
                    continue;
                for(int i=0;i<team.Count;i++)
                {
                    Pawn scout=team[i];
                    if(!Active(scout,map))continue;
                    if(!Seen(scout,p,map))continue;
                    int dist=(scout.Position-p.Position).LengthHorizontalSquared;
                    if(dist<score){score=dist;best=p;}
                    // Every observer matters. The first soldier to see a
                    // defender is not necessarily the one in danger.
                }
            }
            return best;
        }

        // Only turrets that the wing has physically seen enter this estimate.
        // A CLOSED door conceals shots today, but not after it is destroyed:
        // evaluate the destination-side room, not only current gun line of sight.
        private static int KnownGunExposure(IntVec3 barrier,IntVec3 goal,
            Map map,List<Building> knownTurrets)
        {
            if(knownTurrets==null || knownTurrets.Count==0)return 0;
            IntVec3 other=IntVec3.Invalid;
            int nearest=int.MaxValue;
            foreach(IntVec3 dir in GenAdj.CardinalDirections)
            {
                IntVec3 neighbor=barrier+dir;
                if(!neighbor.InBounds(map) || !neighbor.Standable(map))continue;
                int dist=(neighbor-goal).LengthHorizontalSquared;
                if(dist>=nearest)continue;
                nearest=dist;
                other=neighbor;
            }
            Room destination=other.IsValid?other.GetRoom(map):null;
            int exposed=0;
            foreach(Building gun in knownTurrets)
            {
                if(!RHFCombatPlannerV125.IsTurret(gun))continue;
                if((gun.Position-barrier).LengthHorizontalSquared>1600)continue;
                Room gunRoom=gun.Position.GetRoom(map);
                if((destination!=null && destination==gunRoom) ||
                   GenSight.LineOfSight(gun.Position,barrier,map) ||
                   (other.IsValid && GenSight.LineOfSight(gun.Position,other,map)))
                    exposed++;
            }
            return exposed;
        }

        // Evaluate every candidate door near the objective, not the first
        // 12 doors (or first 220 unrelated buildings) in construction order.
        // A visible turret corridor outranks a lower structural door cost.
        private static Thing AccessibleDoor(Pawn lead,IntVec3 goal,Map map,
            List<Building> knownTurrets,out int exposure)
        {
            exposure=int.MaxValue;
            if(!goal.IsValid)return null;
            Thing best=null;
            int bestScore=int.MaxValue;
            foreach(Building b in map.listerBuildings.allBuildingsColonist)
            {
                if(!(b is Building_Door) ||
                   !RHFCombatPlannerV125.RequiresBreach(b))continue;
                int near=(b.Position-goal).LengthHorizontalSquared;
                if(near>225)continue;
                int risk=KnownGunExposure(b.Position,goal,map,knownTurrets);
                int score=near*3 +
                    (b.Position-lead.Position).LengthHorizontalSquared/12 +
                    RHFCombatPlannerV125.BreachWorkCost(b)/8;
                if(risk>0)score+=risk*100000;
                if(score>=bestScore)continue;
                if(!lead.CanReach(b.Position,PathEndMode.Touch,Danger.Some))continue;
                bestScore=score;
                best=b;
                exposure=risk;
            }
            return best;
        }

        private static Thing FirstBarrier(Pawn lead,IntVec3 goal,Map map,
            List<Building> knownTurrets)
        {
            // If a normal walkable path exists, preserve vanilla navigation.
            // (A true route-wide danger comparison remains separate work.)
            if(lead==null || !goal.IsValid)return null;
            if(lead.CanReach(goal,PathEndMode.Touch,Danger.Some))return null;
            int doorRisk;
            Thing door=AccessibleDoor(lead,goal,map,knownTurrets,out doorRisk);
            if(door!=null && doorRisk==0)return door;
            // A door through an observed turret room is not automatically
            // better than breaching an extra wall outside its firing sector.
            IntVec3 a=lead.Position;
            int dx=goal.x-a.x,dz=goal.z-a.z;
            int denominator=Math.Max(Math.Abs(dx),Math.Abs(dz));
            int steps=denominator;
            if(steps<=0)return door;
            int sideX=dz>=0 ? -1 : 1;
            int sideZ=dx>=0 ? 1 : -1;
            int[] offsets={0,1,-1,2,-2};
            var checkedWalls=new HashSet<Thing>();
            Thing best=null;
            int bestCost=int.MaxValue,bestDanger=int.MaxValue;
            for(int i=1;i<=steps;i++)
            {
                int x=a.x+(dx*i/denominator),z=a.z+(dz*i/denominator);
                for(int side=0;side<offsets.Length;side++)
                {
                    IntVec3 cell=new IntVec3(x+sideX*offsets[side],0,
                        z+sideZ*offsets[side]);
                    if(!cell.InBounds(map))continue;
                    Thing wall=cell.GetEdifice(map);
                    if(wall==null || wall.Destroyed || wall.Faction==lead.Faction ||
                       !checkedWalls.Add(wall) ||
                       !RHFCombatPlannerV125.RequiresBreach(wall))continue;
                    int risk=KnownGunExposure(cell,goal,map,knownTurrets);
                    if(risk>bestDanger)continue;
                    if(!lead.CanReach(cell,PathEndMode.Touch,Danger.Some))continue;
                    int score=i*3+Math.Max(0,wall.HitPoints)/20+
                        Math.Abs(offsets[side])*3;
                    if(risk<bestDanger || (risk==bestDanger && score<bestCost))
                    {best=wall;bestCost=score;bestDanger=risk;}
                }
                // A safe wall is a strong candidate, but continue to inspect
                // nearby alternatives up to the bounded reachability budget.
            }
            if(best!=null && (door==null || bestDanger<doorRisk))return best;
            return door??best;
        }

        private static IntVec3 Formation(Pawn pawn,IntVec3 goal,Map map,int index,
            HashSet<IntVec3> occupied,List<Building> knownTurrets)
        {
            // Spread 96 fighters into bounded tactical lanes: no 96-pawn
            // single-cell waypoint stampede.
            int[] dx={-3,0,3,5,5,3,0,-3,-5,-5,-3,0,3,6,0,-6};
            int[] dz={-5,-6,-5,-3,3,5,6,5,3,-3,0,-3,0,0,6,0};
            IntVec3 best=IntVec3.Invalid;
            int bestScore=int.MaxValue;
            // In a narrow passage the exact next *empty* waypoint is often
            // the only valid position. Do not force every fighter into a
            // 3-to-6-cell ring, where a one-man team can never advance.
            if(goal.InBounds(map) && goal.Standable(map) &&
               goal.GetEdifice(map)==null && goal.GetFirstPawn(map)==null &&
               !occupied.Contains(goal) &&
               pawn.CanReach(goal,PathEndMode.OnCell,Danger.Some))
            {
                bool exposed=false;
                if(knownTurrets!=null)
                    foreach(Building gun in knownTurrets)
                        if(gun!=null && gun.Spawned && !gun.Destroyed &&
                           (gun.Position-goal).LengthHorizontalSquared<=1600 &&
                           GenSight.LineOfSight(gun.Position,goal,map))
                        {exposed=true;break;}
                if(!exposed){occupied.Add(goal);return goal;}
            }
            // Expand formation rings until the physical map is exhausted.
            for(int d=0;d<dx.Length*Math.Max(map.Size.x,map.Size.z);d++)
            {
                int j=(index+d)%dx.Length;
                int ring=1+(index+d)/dx.Length;
                // Up to 96 unique slots in expanding rings; do not crowd
                // a single doorway or write the same target to 80 pawns.
                IntVec3 cell=goal+new IntVec3(dx[j]*ring,0,dz[j]*ring);
                if(!cell.InBounds(map) || !cell.Standable(map) ||
                   cell.GetEdifice(map)!=null || occupied.Contains(cell) ||
                   !pawn.CanReach(cell,PathEndMode.OnCell,Danger.Some))continue;
                int score=(pawn.Position-cell).LengthHorizontalSquared/4;
                if(knownTurrets!=null)
                    foreach(Building turret in knownTurrets)
                    {
                        if(turret==null || turret.Destroyed || !turret.Spawned)continue;
                        int range=(turret.Position-cell).LengthHorizontalSquared;
                        if(range<=1600 &&
                           GenSight.LineOfSight(turret.Position,cell,map))
                            score+=30000; // observable kill zone, not a random flank
                    }
                if(score<bestScore){best=cell;bestScore=score;}
                if(score==0)break;
            }
            if(best.IsValid)occupied.Add(best);
            // When every exposed slot is dangerous, least risk beats paralysis.
            return best;
        }

        private static void PlanFaction(RHFOperation op,RHFTacticalBoardS41 board,
            Map map,List<Pawn> members,int wingId,int now)
        {
            if(members.Count==0)return;
            bool hunt=op.HuntTarget!=null &&
                (op.Doctrine==(int)RHFDoctrine.TargetedTraitorHunt ||
                 op.Type==(int)RHFOperationType.Vengeance);
            bool hasContact=hunt && ObserveHunt(op,members,map,now);
            Thing defender=NearestVisibleEnemy(op,members,map);
            // Destructive targets require an explicit raze/retaliation doctrine.
            // A named-target hunt is not permission to destroy every workshop.
            bool raze=op.AssaultAfterObjective ||
                op.Doctrine==(int)RHFDoctrine.SiegeAndRaze ||
                op.Doctrine==(int)RHFDoctrine.PunitiveAssault;
            List<Building> knownTurrets=KnownTurrets(board,members[0].Faction,
                members,map,now);
            Thing building=raze ?
                (knownTurrets.Count>0 ?
                    (Thing)NearestObservedTurret(members,knownTurrets,map) :
                    VisibleObjective(members,map)) : null;
            // A raider under direct contact prioritizes a visible defender
            // over distant infrastructure. Otherwise follow the doctrine's
            // explicit destruction/traitor objective.
            bool urgent=false;
            if(defender!=null)
                foreach(Pawn member in members)
                    if(Active(member,map) &&
                       (member.Position-defender.Position).LengthHorizontalSquared<=625)
                    {urgent=true;break;}
            Thing objective=hasContact ? op.HuntTarget :
                urgent ? defender : building ?? defender;
            IntVec3 goal=objective!=null ? objective.Position :
                hunt && op.HuntLastSeenCell.IsValid &&
                now-op.HuntLastSeenTick<9000 ? op.HuntLastSeenCell : map.Center;
            // Distinct diversion flank: not the rescue team's path to prison.
            if(wingId==1 && objective==null)
            {
                int sign=members[0].Position.x<map.Center.x ? 1 : -1;
                goal=new IntVec3(Math.Max(2,Math.Min(map.Size.x-3,
                    map.Center.x+sign*12)),0,map.Center.z);
            }
            // Native siege keeps the mortar/building StateGraph. Separate
            // infantry guards artillery instead of prematurely charging the
            // walls. On vanilla's assault transition, normal offense resumes.
            RHFNativeWingS41 siegeWing=RHFNativeDoctrineS41.FindForSupport(op,
                members[0].Faction,RHFNativeDoctrineS41.Siege,
                members[0].Position,map,now);
            bool guardSiege=siegeWing!=null &&
                siegeWing.Stage==RHFNativeDoctrineS41.Building &&
                siegeWing.Rally.IsValid && siegeWing.Rally.InBounds(map) &&
                now-siegeWing.LastObservedTick<=900;
            RHFNativeWingS41 beastWing=RHFNativeDoctrineS41.FindForSupport(op,
                members[0].Faction,RHFNativeDoctrineS41.Animals,
                members[0].Position,map,now);
            bool supportBeasts=!guardSiege && beastWing!=null &&
                beastWing.Animals>0 && beastWing.Rally.IsValid &&
                beastWing.Rally.InBounds(map) &&
                now-beastWing.LastObservedTick<=900;
            if(guardSiege)
            {
                var perimeter=new HashSet<IntVec3>();
                bool nearbyThreat=defender!=null &&
                    (defender.Position-siegeWing.Rally).LengthHorizontalSquared<=900;
                for(int i=0;i<members.Count;i++)
                {
                    Pawn soldier=members[i];
                    if(!Active(soldier,map))continue;
                    IntVec3 post=Formation(soldier,siegeWing.Rally,map,i,perimeter,knownTurrets);
                    if(!post.IsValid)post=soldier.Position;
                    board.Assign(soldier,nearbyThreat &&
                        RHFCombatPlannerV125.WeaponRange(soldier)>4f ?
                        RHFRoleS41.OffenseCover : RHFRoleS41.OffenseRally,
                        nearbyThreat?IndividuallyConfirmed(soldier,defender,map):null,
                        post,now);
                }
                board.Decision="native siege: holding independent protection wing";
                return;
            }
            if(supportBeasts)
            {
                // The current animal front is observed in the exact native
                // Lord. Physical flank is selected by reachable path, not by
                // a fixed (+7,+5) coordinate that may be behind a prison wall.
                if(RHFNativeDoctrineS41.TryBeastSupportAnchor(beastWing,
                    members[0],map,now,out IntVec3 flank))goal=flank;
            }
            // Reconnaissance without a confirmed target is not authorization
            // to break a random colony wall on the straight ray to map center.
            // A merely walkable path is NOT necessarily a safe attack route.
            // Examine the whole map, including doors/walls and observed guns.
            // Stage the wing on a reachable corridor, not across its gunfire.
            bool hasObjective=objective!=null ||
                (hunt && op.HuntLastSeenCell.IsValid &&
                 now-op.HuntLastSeenTick<9000);
            NJROffenseRouteS45 attackRoute=hasObjective ?
                NJROffensePathS45.Plan(members[0],goal,knownTurrets) : null;
            Thing barrier=attackRoute?.Found==true ? attackRoute.FirstBarrier :
                hasObjective ? FirstBarrier(members[0],goal,map,knownTurrets) : null;
            IntVec3 staging=attackRoute?.Found==true &&
                attackRoute.Approach.IsValid ? attackRoute.Approach : goal;
            Pawn breacher=null;
            if(barrier!=null)
                for(int pass=0;pass<2 && breacher==null;pass++)
                    foreach(Pawn p in members)
                    {
                        if(!Active(p,map) ||
                           p.WorkTagIsDisabled(WorkTags.Violent) ||
                           p.health?.capacities==null ||
                           !p.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation) ||
                           (pass==0 && p.equipment?.Primary?.def?.IsMeleeWeapon!=true))continue;
                        if(p.CanReach(barrier.Position,PathEndMode.Touch,Danger.Some))
                        { breacher=p;break; }
                    }
            var occupied=new HashSet<IntVec3>();
            for(int i=0;i<members.Count;i++)
            {
                Pawn soldier=members[i];
                if(!Active(soldier,map))continue;
                if(soldier==breacher)
                {
                    board.Assign(soldier,RHFRoleS41.Breach,barrier,
                        barrier.Position,now);
                    continue;
                }
                bool ranged=RHFCombatPlannerV125.WeaponRange(soldier)>4f;
                if(hasContact && (i%4==0 || i%4==1))
                {
                    // A scout may RADIO the last observed cell. Soldiers who
                    // never personally saw the defector do not get a live
                    // moving Pawn target through walls or teleportation.
                    Thing confirmed=Seen(soldier,op.HuntTarget,map) ?
                        (Thing)op.HuntTarget : null;
                    board.Assign(soldier,RHFRoleS41.OffenseHunt,
                        confirmed,op.HuntLastSeenCell,now);
                    continue;
                }
                if(ranged && defender!=null && i%3==0)
                {
                    board.Assign(soldier,RHFRoleS41.OffenseCover,
                        IndividuallyConfirmed(soldier,defender,map),goal,now);
                    continue;
                }
                if(barrier!=null)
                {
                    if(breacher==null)
                    {
                        // A visible obstacle does NOT imply an available
                        // manipulation-capable breacher. The previous code
                        // dereferenced the missing operator here and crashed for
                        // all-ranged, disabled or wounded assault parties.
                        // Reallocate the wing to PHYSICAL attacks against the
                        // actual wall: ranged pawns need LOS and safe lanes;
                        // melee pawns need a real reachable touch position.
                        // A protected, unreachable barrier is NOT teleported
                        // through and we never invent a sapper.
                        IntVec3 post=Formation(soldier,barrier.Position,map,i,occupied,knownTurrets);
                        if(!post.IsValid)post=soldier.Position;
                        board.Assign(soldier,RHFRoleS41.OffenseStrike,
                            barrier,post,now);
                    }
                    else
                    {
                        IntVec3 cover=Formation(soldier,breacher.Position,map,i,occupied,knownTurrets);
                        // No safe formation slot does not mean 'stay here forever'.
                        // Follow the breacher using a separate physical route.
                        if(!cover.IsValid)cover=breacher.Position;
                        board.Assign(soldier,RHFRoleS41.OffenseRally,
                            null,cover,now);
                    }
                    continue;
                }
                IntVec3 lane=Formation(soldier,staging,map,i,occupied,knownTurrets);
                if(!lane.IsValid)lane=staging;
                // Actual weapons and the observed situation decide roles;
                // a random modulo of roster order is not tactical doctrine.
                RHFRoleS41 role=ranged && defender!=null && i%3==1 ?
                    RHFRoleS41.OffenseCover :
                    !ranged && (i%3==0) ? RHFRoleS41.OffenseFlank :
                    RHFRoleS41.OffenseStrike;
                Thing assignment=role==RHFRoleS41.OffenseCover && defender!=null ?
                    defender : objective;
                board.Assign(soldier,role,
                    IndividuallyConfirmed(soldier,assignment,map),lane,now);
            }
            // Do not overwrite board.Breacher/Barrier: one per-pawn order is
            // required for independent simultaneous coalition breachers.
            board.LastKnownCell=goal;
            board.Decision="S41 offense " + wingId +
                (supportBeasts?" / native beast support":"")+ " / " +
                (hasContact?"confirmed target":
                 barrier!=null && breacher==null ? "no sapper / physical attacks" :
                 barrier!=null ? "physical breach" :
                 objective!=null?"visible infrastructure":"reconnaissance")+
                " / route="+(attackRoute?.Reason??"none")+
                " / faction "+members[0].Faction.Name;
        }

        private static void ReleaseWing(RHFOperation op,List<Pawn> wing,Map map)
        {
            if(wing==null)return;
            foreach(Pawn p in wing)
            {
                if(p==null || p.Map!=map)continue;
                if(p.GetLord()?.LordJob is RHFOffenseLordJobS41 job && !job.Retreat)
                { job.Retreat=true; p.GetLord().CurLordToil?.UpdateAllDuties(); }
            }
        }

        internal static void End(RHFOperation op,Map map)
        {
            if(op==null || map==null)return;
            ReleaseWing(op,op.Diversion,map);
            ReleaseWing(op,op.Squad,map);
            op.OffenseBoardS41?.Orders?.Clear();
        }
    }

    // On-demand forensic view for every offensive wing. This is never
    // called by MapComponentTick and cannot issue a Pawn job.
    public static class NJROffenseDiagnosticsS41
    {
        [DebugAction("NJR - ТЕСТЫ | ДИАГНОСТИКА",
            "38 — NJR: наступательные рейды, приказы и наблюдения",
            actionType=DebugActionType.Action,
            allowedGameStates=AllowedGameStates.PlayingOnMap)]
        public static void Inspect()
        {
            Map map=Find.CurrentMap;
            var ops=RHFMissionIntegrityV130.Operations(map);
            var report=new StringBuilder(1800);
            report.AppendLine("NJR offense: actual Lord / role / target / last sighting");
            if(ops==null || ops.Count==0)
                report.AppendLine("Нет зарегистрированных операций.");
            else for(int i=0;i<ops.Count;i++)
            {
                RHFOperation op=ops[i];
                if(op==null)continue;
                bool relevant=op.Type==(int)RHFOperationType.Vengeance ||
                    (op.Type==(int)RHFOperationType.Rescue &&
                     ((op.Diversion!=null && op.Diversion.Count>0) ||
                      op.Phase==5));
                if(!relevant)continue;
                RHFTacticalBoardS41 board=op.OffenseBoardS41;
                report.AppendLine("  remembered turrets by faction="+
                    (board?.ObservedTurretsS46?.Count??0)+
                    " (not shared between coalition factions)");
                report.AppendLine("Op "+op.CreatedTick+" phase="+op.Phase+
                    " doctrine="+(RHFDoctrine)op.Doctrine+
                    " finished="+op.Finished);
                report.AppendLine("  hunt="+(op.HuntTarget?.LabelShortCap??"none")+
                    " last="+op.HuntLastSeenCell+
                    " since="+(Find.TickManager.TicksGame-op.HuntLastSeenTick)+
                    " plan="+(board?.Decision??"missing"));
                if(board?.Orders==null)continue;
                int uncontrolled=0;
                int shown=0;
                foreach(RHFOrderS41 order in board.Orders)
                {
                    Pawn p=order?.Member;
                    if(p==null || !p.Spawned || p.Map!=map)continue;
                    shown++;
                    Lord lord=p.GetLord();
                    if(!(lord?.LordJob is RHFOffenseLordJobS41))uncontrolled++;
                    report.AppendLine("  "+p.LabelShortCap+" / "+
                        (p.Faction?.Name??"?")+" / "+(RHFRoleS41)order.Role+
                        " / "+order.Cell+
                        " / target="+(order.Objective?.LabelShortCap??"none")+
                        " / job="+(p.CurJobDef?.defName??"none")+
                        " / lord="+(lord?.LordJob?.GetType().Name??"none"));
                }
                report.AppendLine("  assigned="+shown+
                    " non-Offense-Lord="+uncontrolled+
                    " (native siege/beasts may be intentional)");
            }
            Find.WindowStack.Add(new Dialog_MessageBox(report.ToString()));
        }
    }

    // Role instructions are COMPLETELY read-only wrt strategic plan. They
    // never call TryTakeOrderedJob, unlike the deprecated map component logic.
    public sealed class RHFOffenseRoleJobGiverS41 : ThinkNode_JobGiver
    {
        // For an infantry-only wing without a qualified breacher: choose a
        // REAL reachable standing cell beside the obstacle. A melee pawn
        // cannot attack from a 5-cell formation slot, and cannot Goto the
        // impassable wall's occupied cell. No teleport or global path scan.
        private static IntVec3 ReachableBreachStand(Pawn pawn,Thing barrier)
        {
            Map map=pawn?.Map;
            if(map==null || barrier==null || !barrier.Spawned ||
               barrier.Map!=map)return IntVec3.Invalid;
            IntVec3[] deltas={IntVec3.North,IntVec3.South,
                IntVec3.East,IntVec3.West};
            IntVec3 best=IntVec3.Invalid;
            int distance=int.MaxValue;
            for(int i=0;i<deltas.Length;i++)
            {
                IntVec3 cell=barrier.Position+deltas[i];
                if(!cell.InBounds(map) || !cell.Standable(map) ||
                   cell.GetEdifice(map)!=null ||
                   !pawn.CanReach(cell,PathEndMode.OnCell,Danger.Some))continue;
                int score=(pawn.Position-cell).LengthHorizontalSquared;
                if(score<distance){best=cell;distance=score;}
            }
            return best;
        }
        private static Job Task(JobDef def,LocalTargetInfo target,int ticks)
        {
            Job job=JobMaker.MakeJob(def,target);
            job.expiryInterval=ticks;
            job.checkOverrideOnExpire=true;
            return job;
        }
        // Only the current faction's observed turrets influence its tactical
        // path. Foreign coalition sightings cannot become omniscient knowledge.
        private static List<Building> RememberedGuns(Pawn pawn,RHFOperation op)
        {
            var result=new List<Building>();
            if(pawn?.Faction==null || op?.OffenseBoardS41?.ObservedTurretsS46==null)
                return result;
            string key=pawn.Faction.GetUniqueLoadID();
            foreach(RHFObservedTurretS46 record in
                    op.OffenseBoardS41.ObservedTurretsS46)
                if(record!=null && record.FactionKey==key &&
                   record.Turret is Building turret && turret.Spawned &&
                   turret.Map==pawn.Map && RHFCombatPlannerV125.IsTurret(turret))
                    result.Add(turret);
            return result;
        }

        // The strategic leader's safe corridor is not a valid path for every
        // other member of the squad. Plan from the pawn's physical position;
        // issue one adjacent A* cell per Goto so the engine cannot shortcut
        // via a lethal corridor in its independent long-distance pathfinder.
        private static Job FollowAssessedRoute(Pawn pawn,RHFOperation op,
            IntVec3 destination)
        {
            Map map=pawn?.Map;
            if(map==null || !destination.IsValid || !destination.InBounds(map) ||
               pawn.Position==destination)return null;
            NJROffenseRouteS45 route=NJROffensePathS45.Plan(pawn,destination,
                RememberedGuns(pawn,op));
            if(!route.Found)return null;
            IntVec3 next=route.NextStep;
            if(!next.IsValid || !next.InBounds(map) || next==pawn.Position)
                return null;
            Building blocking=next.GetEdifice(map);
            if(blocking!=null && RHFCombatPlannerV125.RequiresBreach(blocking))
            {
                if(pawn.WorkTagIsDisabled(WorkTags.Violent) ||
                   !pawn.CanReach(blocking,PathEndMode.Touch,Danger.Deadly))
                    return null;
                return Task(JobDefOf.AttackMelee,blocking,600);
            }
            // An OPEN door is walkable but not Standable in vanilla
            // GenGrid, because its edifice has pass-through-only def flags.
            bool openDoor=blocking is Building_Door door && door.Open;
            if((!next.Standable(map) && !openDoor) ||
               !pawn.CanReach(next,PathEndMode.OnCell,Danger.Deadly))
                return null;
            return Task(JobDefOf.Goto,next,90);
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            if(pawn==null || pawn.Dead || pawn.Downed || !pawn.Spawned ||
               pawn.Map==null || RHFCombatPlannerV125.IsCombatEmergencyJob(pawn))
                return null;
            RHFOperation op=RHFOffenseDirectorS41.MissionFor(pawn);
            RHFOrderS41 order=op?.OffenseBoardS41?.For(pawn,false);
            if(order==null)return null;
            IntVec3 contactGoal=order.Objective!=null && order.Objective.Spawned &&
                order.Objective.Faction==Faction.OfPlayer &&
                (!(order.Objective is Pawn) || GenSight.LineOfSight(
                    pawn.Position,order.Objective.Position,pawn.Map)) ?
                order.Objective.Position : order.Cell;
            Job contact=NJRCombatContactS44.Respond(pawn,op,contactGoal,true,28);
            if(contact!=null)return contact;
            Map map=pawn.Map;
            Thing target=order.Objective;
            IntVec3 cell=order.Cell;
            // A pawn that left the player's faction, or a captured building,
            // is no longer a valid offensive objective. Never wait for the
            // strategic pulse before suppressing a now-friendly target.
            if(target!=null && target.Faction!=Faction.OfPlayer)
                target=null;
            int d=target!=null && target.Spawned ?
                (pawn.Position-target.Position).LengthHorizontalSquared:int.MaxValue;
            float range=RHFCombatPlannerV125.WeaponRange(pawn);
            RHFRoleS41 role=(RHFRoleS41)order.Role;
            // Lose a living target when it leaves THIS soldier's sight.
            // Keep the saved waypoint; do not recompute from an unseen Pawn.
            if(target is Pawn && (role==RHFRoleS41.OffenseHunt &&
               target!=op.HuntTarget || !GenSight.LineOfSight(
                   pawn.Position,target.Position,map)))
            { target=null; d=int.MaxValue; }
            bool shoot=target!=null && target.Spawned && target.Map==map &&
                range>3f && d<=range*range &&
                GenSight.LineOfSight(pawn.Position,target.Position,map) &&
                !RHFRescueFireDisciplineV136.UnsafeLine(op,pawn,target);
            if(role==RHFRoleS41.Breach)
            {
                if(pawn.WorkTagIsDisabled(WorkTags.Violent))return null;
                if(target==null || target.Destroyed || target.Map!=map ||
                   !RHFCombatPlannerV125.RequiresBreach(target))return null;
                // Real melee/ranged JobDrivers do all structure damage.
                if(pawn.equipment?.Primary?.def?.IsMeleeWeapon==true || range<=3f)
                {
                    IntVec3 stand=ReachableBreachStand(pawn,target);
                    if(!stand.IsValid)return null;
                    if(pawn.Position!=stand)return Task(JobDefOf.Goto,stand,180);
                    return Task(JobDefOf.AttackMelee,target,600);
                }
                if(shoot)return Task(JobDefOf.AttackStatic,target,180);
                // A ranged breacher still has hands: when its gun cannot
                // safely hit a nearby wall (no LoS, explosive VIP danger,
                // incompatible modded verb), move to a REAL adjacent cell
                // and use the native melee JobDriver instead of Wait forever.
                IntVec3 meleeStand=ReachableBreachStand(pawn,target);
                if(!meleeStand.IsValid)return null;
                if(pawn.Position!=meleeStand)return Task(JobDefOf.Goto,meleeStand,180);
                return pawn.CanReach(target,PathEndMode.Touch,Danger.Some) ?
                    Task(JobDefOf.AttackMelee,target,600) : null;
            }
            if(role==RHFRoleS41.OffenseCover || role==RHFRoleS41.OffenseHunt ||
               role==RHFRoleS41.OffenseStrike || role==RHFRoleS41.OffenseFlank)
            {
                if(target is Pawn enemy && enemy.Downed &&
                   role==RHFRoleS41.OffenseHunt &&
                   pawn.CanReach(enemy,PathEndMode.Touch,Danger.Some))
                {
                    Job kill=Task(JobDefOf.AttackMelee,enemy,150);
                    kill.killIncappedTarget=true;
                    return kill;
                }
                if(shoot && !pawn.WorkTagIsDisabled(WorkTags.Violent))
                    return Task(JobDefOf.AttackStatic,target,150);
                if(target!=null && d<9 &&
                   !pawn.WorkTagIsDisabled(WorkTags.Violent) &&
                   pawn.CanReach(target.Position,PathEndMode.Touch,Danger.Some))
                    return Task(JobDefOf.AttackMelee,target,150);
                // The non-sapper plan may assign this pawn to damage a wall.
                // Without a reachable adjacent stand cell a melee pawn could
                // spend the entire raid at its 3-6 cell formation slot.
                if(role==RHFRoleS41.OffenseStrike && target!=null &&
                   RHFCombatPlannerV125.RequiresBreach(target) &&
                   range<=3f)
                {
                    IntVec3 approach=ReachableBreachStand(pawn,target);
                    if(approach.IsValid && pawn.Position!=approach)
                        return Task(JobDefOf.Goto,approach,180);
                }
            }
            if(cell.IsValid && cell.InBounds(map) && pawn.Position!=cell)
            {
                // Never replace our safe-first map search with a completely
                // separate full-distance vanilla Goto. Even in a heavy raid,
                // every fighter gets its OWN immediately reachable route step.
                Job step=FollowAssessedRoute(pawn,op,cell);
                if(step!=null)return step;
                Job breach=NJRCombatContactS44.BreachToward(pawn,op,cell);
                if(breach!=null)return breach;
            }
            if(target!=null && target.Spawned && target.Map==map)
            {
                Job breach=NJRCombatContactS44.BreachToward(pawn,op,target.Position);
                if(breach!=null)return breach;
            }
            return null; // genuinely inaccessible rock, not a phantom path
        }
    }

    // Does not target coalition members, prisoners or neutral colonists. It
    // reacts only to physical close-range hostiles of the PLAYER faction.
    public sealed class RHFOffenseDefenseJobGiverS41 : ThinkNode_JobGiver
    {
        internal static Pawn ImmediateEnemy(Pawn p,RHFOperation op)
        {
            Map map=p?.Map;
            if(map==null || op==null)return null;
            Pawn target=null;int dist=144;
            foreach(Pawn enemy in map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer))
            {
                if(enemy==null || enemy.Dead || enemy.Downed ||
                   (enemy!=op.HuntTarget &&
                    (op.Targets?.Contains(enemy)==true ||
                     op.Freed?.Contains(enemy)==true)) ||
                   op.Squad?.Contains(enemy)==true ||
                   op.Diversion?.Contains(enemy)==true)continue;
                int d=(p.Position-enemy.Position).LengthHorizontalSquared;
                if(d>=dist || !GenSight.LineOfSight(p.Position,enemy.Position,map) ||
                   RHFRescueFireDisciplineV136.UnsafeLine(op,p,enemy))continue;
                dist=d;target=enemy;
            }
            return target;
        }
        protected override Job TryGiveJob(Pawn pawn)
        {
            if(pawn==null || !pawn.Spawned || pawn.Downed || pawn.Dead ||
               RHFCombatPlannerV125.IsCombatEmergencyJob(pawn))return null;
            RHFOperation op=RHFOffenseDirectorS41.MissionFor(pawn);
            if(op==null)return null;
            return NJRCombatContactS44.Respond(pawn,op,pawn.Position,false,18);
        }
    }
}
