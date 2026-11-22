// RHF S4.1-A/G: faction-isolated tactical command for vengeance, diversion,
// and post-extraction punishment. The map planner writes only a SAVEABLE board.
// Pawn jobs are emitted exclusively by the associated ThinkTree JobGivers.
// Preserve NATIVE siege, animal group and other-mod Lord state machines.
using System;
using System.Collections.Generic;
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
        private const int MaxMembers=96;
        private const int MaxOperations=4;
        private const int ReplanTicks=270;
        private const int MaxStructures=64;
        private const int MaxObserve=8;
        private const int MaxCandidateCells=10;

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
            for(int i=0;i<ops.Count && i<MaxOperations;i++)
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
            bool replan=now>=board.NextReplanTick || board.ObservedPhase!=op.Phase;
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

        private static bool CaptivesStillOnMap(RHFOperation op,Map map)
        {
            if(op?.Targets!=null)
                for(int i=0;i<op.Targets.Count && i<24;i++)
                {
                    Pawn captive=op.Targets[i];
                    if(captive!=null && !captive.Dead && captive.Map==map)
                        return true;
                }
            if(op?.Freed!=null)
                for(int i=0;i<op.Freed.Count && i<24;i++)
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
            for(int i=0;i<wing.Count && i<MaxMembers;i++)
            {
                Pawn leader=wing[i];
                if(!Active(leader,map) || leader.Faction==null ||
                   !visited.Add(leader.Faction))continue;
                var factionMembers=new List<Pawn>(32);
                // Per-pawn native ownership, never a faction-wide veto.
                // Siege artillery + beast handlers stay in their *own Lords*;
                // an independent supporting raid from the same faction can act.
                for(int j=0;j<wing.Count && j<MaxMembers;j++)
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
            if(!Active(observer,map) || target==null || !target.Spawned ||
               target.Map!=map || target.Position.Fogged(map))return false;
            int dist=(observer.Position-target.Position).LengthHorizontalSquared;
            return dist<3600 && GenSight.LineOfSight(observer.Position,target.Position,map);
        }

        // Detect the actual person only after physical observation. The last
        // confirmed cell has a short lifetime; teleporting a traitor does not
        // give the raiders omniscient tracking.
        private static bool ObserveHunt(RHFOperation op,List<Pawn> wing,Map map,int now)
        {
            Pawn target=op.HuntTarget;
            if(target==null || target.Dead || !target.Spawned ||
               target.Map!=map || target.Faction!=Faction.OfPlayer)return false;
            for(int i=0;i<wing.Count && i<MaxObserve;i++)
                if(Seen(wing[i],target,map))
                {
                    op.HuntLastSeenCell=target.Position;
                    op.HuntLastSeenTick=now;
                    return true;
                }
            return false;
        }

        private static Thing VisibleObjective(List<Pawn> team,Map map)
        {
            Thing best=null;int score=int.MinValue;
            var sites=map.listerBuildings.allBuildingsColonist;
            int stride=Math.Max(1,sites.Count/MaxStructures);
            int inspected=0;
            for(int k=0;k<sites.Count && inspected<MaxStructures;k+=stride)
            {
                Building b=sites[k];
                inspected++;
                if(b==null || !b.Spawned || b.Destroyed ||
                   b.Faction!=Faction.OfPlayer)continue;
                bool seen=false;
                for(int i=0;i<team.Count && i<MaxObserve;i++)
                    if(Seen(team[i],b,map)){seen=true;break;}
                if(!seen)continue;
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

        private static Thing NearestVisibleEnemy(List<Pawn> team,Map map)
        {
            Pawn best=null;int score=int.MaxValue;
            int checkedPawns=0;
            foreach(Pawn p in map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer))
            {
                if(++checkedPawns>100)break;
                if(p==null || p.Dead || p.Downed)continue;
                for(int i=0;i<team.Count && i<MaxObserve;i++)
                    if(Seen(team[i],p,map))
                    {
                        int dist=(team[i].Position-p.Position).LengthHorizontalSquared;
                        if(dist<score){score=dist;best=p;}
                        break;
                    }
            }
            return best;
        }

        private static Thing FirstBarrier(Pawn lead,IntVec3 goal,Map map)
        {
            // The first wall directly on a ray is NOT necessarily the cheapest
            // or reachable entry. Inspect a narrow corridor with 12 maximum
            // reachability probes, then choose physical HP+distance cost.
            if(lead==null || !goal.IsValid)return null;
            IntVec3 a=lead.Position;
            int dx=goal.x-a.x,dz=goal.z-a.z;
            int denominator=Math.Max(Math.Abs(dx),Math.Abs(dz));
            int steps=Math.Min(90,denominator);
            if(steps<=0)return null;
            int sideX=dz>=0 ? -1 : 1;
            int sideZ=dx>=0 ? 1 : -1;
            int[] offsets={0,1,-1,2,-2};
            var checkedWalls=new HashSet<Thing>();
            Thing best=null;
            int bestCost=int.MaxValue;
            int reachableProbes=0;
            for(int i=1;i<=steps && reachableProbes<12;i++)
            {
                int x=a.x+(dx*i/denominator),z=a.z+(dz*i/denominator);
                for(int side=0;side<offsets.Length && reachableProbes<12;side++)
                {
                    IntVec3 cell=new IntVec3(x+sideX*offsets[side],0,
                        z+sideZ*offsets[side]);
                    if(!cell.InBounds(map))continue;
                    Thing wall=cell.GetEdifice(map);
                    if(wall==null || wall.Destroyed || wall.Faction==lead.Faction ||
                       !checkedWalls.Add(wall) ||
                       !RHFCombatPlannerV125.IsBreachableStructure(wall))continue;
                    reachableProbes++;
                    if(!lead.CanReach(cell,PathEndMode.Touch,Danger.Some))continue;
                    int score=i*3+Math.Max(0,wall.HitPoints)/20+
                        Math.Abs(offsets[side])*3;
                    if(score<bestCost){best=wall;bestCost=score;}
                }
                // Once a reachable barrier has been found, inspect a small
                // neighborhood rather than looking behind the sealed wall.
                if(best!=null && i>bestCost/3+7)break;
            }
            return best;
        }

        private static IntVec3 Formation(Pawn pawn,IntVec3 goal,Map map,int index,
            HashSet<IntVec3> occupied)
        {
            // Spread 96 fighters into bounded tactical lanes: no 96-pawn
            // single-cell waypoint stampede.
            int[] dx={-3,0,3,5,5,3,0,-3,-5,-5,-3,0,3,6,0,-6};
            int[] dz={-5,-6,-5,-3,3,5,6,5,3,-3,0,-3,0,0,6,0};
            for(int d=0;d<MaxCandidateCells;d++)
            {
                int j=(index+d)%dx.Length;
                int ring=1+(index+d)/dx.Length;
                // Up to 96 unique slots in expanding rings; do not crowd
                // a single doorway or write the same target to 80 pawns.
                IntVec3 cell=goal+new IntVec3(dx[j]*ring,0,dz[j]*ring);
                if(!cell.InBounds(map) || !cell.Standable(map) ||
                   cell.GetEdifice(map)!=null || occupied.Contains(cell) ||
                   !pawn.CanReach(cell,PathEndMode.OnCell,Danger.Some))continue;
                occupied.Add(cell);
                return cell;
            }
            // Do not invent a reachable cell after all tests fail.
            return IntVec3.Invalid;
        }

        private static void PlanFaction(RHFOperation op,RHFTacticalBoardS41 board,
            Map map,List<Pawn> members,int wingId,int now)
        {
            if(members.Count==0)return;
            bool hunt=op.HuntTarget!=null &&
                (op.Doctrine==(int)RHFDoctrine.TargetedTraitorHunt ||
                 op.Type==(int)RHFOperationType.Vengeance);
            bool hasContact=hunt && ObserveHunt(op,members,map,now);
            Thing defender=NearestVisibleEnemy(members,map);
            // Destructive targets require an explicit raze/retaliation doctrine.
            // A named-target hunt is not permission to destroy every workshop.
            bool raze=op.AssaultAfterObjective ||
                op.Doctrine==(int)RHFDoctrine.SiegeAndRaze ||
                op.Doctrine==(int)RHFDoctrine.PunitiveAssault;
            Thing building=raze ? VisibleObjective(members,map) : null;
            Thing objective=hasContact ? op.HuntTarget :
                building ?? defender;
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
                    IntVec3 post=Formation(soldier,siegeWing.Rally,map,i,perimeter);
                    if(!post.IsValid)post=soldier.Position;
                    board.Assign(soldier,nearbyThreat &&
                        RHFCombatPlannerV125.WeaponRange(soldier)>4f ?
                        RHFRoleS41.OffenseCover : RHFRoleS41.OffenseRally,
                        nearbyThreat?defender:null,post,now);
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
            Thing barrier=FirstBarrier(members[0],goal,map);
            Pawn breacher=null;
            if(barrier!=null)
                for(int pass=0;pass<2 && breacher==null;pass++)
                    foreach(Pawn p in members)
                    {
                        if(!Active(p,map) ||
                           p.health?.capacities==null ||
                           !p.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation) ||
                           (pass==0 && !RHFTactical.HasBreachKit(p)))continue;
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
                    board.Assign(soldier,RHFRoleS41.OffenseHunt,
                        op.HuntTarget,op.HuntLastSeenCell,now);
                    continue;
                }
                if(ranged && defender!=null && i%3==0)
                {
                    board.Assign(soldier,RHFRoleS41.OffenseCover,
                        defender,goal,now);
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
                        IntVec3 post=Formation(soldier,barrier.Position,map,i,occupied);
                        if(!post.IsValid)post=soldier.Position;
                        board.Assign(soldier,RHFRoleS41.OffenseStrike,
                            barrier,post,now);
                    }
                    else
                    {
                        IntVec3 cover=Formation(soldier,breacher.Position,map,i,occupied);
                        if(!cover.IsValid)cover=soldier.Position;
                        board.Assign(soldier,RHFRoleS41.OffenseRally,
                            null,cover,now);
                    }
                    continue;
                }
                IntVec3 lane=Formation(soldier,goal,map,i,occupied);
                if(!lane.IsValid)lane=soldier.Position;
                board.Assign(soldier,i%5==2 ? RHFRoleS41.OffenseFlank :
                    RHFRoleS41.OffenseStrike,objective,lane,now);
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
        protected override Job TryGiveJob(Pawn pawn)
        {
            if(pawn==null || pawn.Dead || pawn.Downed || !pawn.Spawned ||
               pawn.Map==null || RHFCombatPlannerV125.IsCombatEmergencyJob(pawn))
                return null;
            RHFOperation op=RHFOffenseDirectorS41.MissionFor(pawn);
            RHFOrderS41 order=op?.OffenseBoardS41?.For(pawn,false);
            if(order==null)return null;
            if(RHFOffenseDefenseJobGiverS41.ImmediateEnemy(pawn,op)!=null)
                return null; // self defence job giver below owns close contact
            Map map=pawn.Map;
            Thing target=order.Objective;
            IntVec3 cell=order.Cell;
            int d=target!=null && target.Spawned ?
                (pawn.Position-target.Position).LengthHorizontalSquared:int.MaxValue;
            float range=RHFCombatPlannerV125.WeaponRange(pawn);
            bool shoot=target!=null && target.Spawned && target.Map==map &&
                range>3f && d<=range*range &&
                GenSight.LineOfSight(pawn.Position,target.Position,map) &&
                !RHFRescueFireDisciplineV136.UnsafeLine(op,pawn,target);
            RHFRoleS41 role=(RHFRoleS41)order.Role;
            if(role==RHFRoleS41.Breach)
            {
                if(target==null || target.Destroyed || target.Map!=map ||
                   !RHFCombatPlannerV125.IsBreachableStructure(target))return null;
                if(RHFTactical.HasBreachKit(pawn) && op.BreachChargesUsed<4 &&
                   pawn.CanReach(target.Position,PathEndMode.Touch,Danger.Some))
                {
                    JobDef def=DefDatabase<JobDef>.GetNamedSilentFail("RHF_PlantBreachChargeS41");
                    if(def!=null)return Task(def,target,0);
                }
                if(shoot)return Task(JobDefOf.AttackStatic,target,150);
                if(pawn.CanReach(target.Position,PathEndMode.Touch,Danger.Some))
                    return Task(JobDefOf.AttackMelee,target,150);
                return null;
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
                if(shoot)return Task(JobDefOf.AttackStatic,target,150);
                if(target!=null && d<9 &&
                   pawn.CanReach(target.Position,PathEndMode.Touch,Danger.Some))
                    return Task(JobDefOf.AttackMelee,target,150);
                // The non-sapper plan may assign this pawn to damage a wall.
                // Without a reachable adjacent stand cell a melee pawn could
                // spend the entire raid at its 3-6 cell formation slot.
                if(role==RHFRoleS41.OffenseStrike && target!=null &&
                   RHFCombatPlannerV125.IsBreachableStructure(target) &&
                   range<=3f)
                {
                    IntVec3 approach=ReachableBreachStand(pawn,target);
                    if(approach.IsValid && pawn.Position!=approach)
                        return Task(JobDefOf.Goto,approach,180);
                }
            }
            if(cell.IsValid && cell.InBounds(map) &&
               (pawn.Position-cell).LengthHorizontalSquared>6 &&
               pawn.CanReach(cell,PathEndMode.OnCell,Danger.Some))
                return Task(JobDefOf.Goto,cell,90);
            return null; // scoped defence, then waiting duty
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
            Pawn target=null;int dist=144,seen=0;
            foreach(Pawn enemy in map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer))
            {
                if(seen++>=48)break;
                if(enemy==null || enemy.Dead || enemy.Downed ||
                   op.Targets?.Contains(enemy)==true ||
                   op.Freed?.Contains(enemy)==true)continue;
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
            Pawn enemy=ImmediateEnemy(pawn,op);
            if(enemy==null)return null;
            float range=RHFCombatPlannerV125.WeaponRange(pawn);
            int d=(pawn.Position-enemy.Position).LengthHorizontalSquared;
            JobDef def=range>3f && d<=range*range ?
                JobDefOf.AttackStatic : JobDefOf.AttackMelee;
            if(def==JobDefOf.AttackMelee &&
               !pawn.CanReach(enemy,PathEndMode.Touch,Danger.Some))return null;
            Job job=JobMaker.MakeJob(def,enemy);
            job.expiryInterval=120;
            job.checkOverrideOnExpire=true;
            return job;
        }
    }
}
