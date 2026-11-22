// RHF S4.1-A/H: observation + support for native siege and animal Lords.
// This class NEVER issues jobs, makes/removes Lords, spawns supplies, or changes duties.
// Native siege construction/shells and animal handlers are always owned by vanilla.
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
    public sealed class RHFNativeWingS41 : IExposable
    {
        public Faction Faction;
        // One state belongs to one actual native Lord, NOT one faction/kind.
        // Lord implements ILoadReferenceable; reference survives Save/Load.
        public Lord OwnerLord;
        public int Kind;                  // 1 native siege; 2 animals/handlers
        public int Stage;                 // 0 approaching; 1 building/engaged; 2 assault
        public int Members;
        public int Animals;
        public int Builders;
        // Only observed physical positions: never infer a defender through walls.
        public IntVec3 LastVisibleEnemyCell = IntVec3.Invalid;
        public int LastEnemySeenTick;
        public int LastObservedTick;
        public IntVec3 Rally = IntVec3.Invalid;

        public RHFNativeWingS41() { }
        internal RHFNativeWingS41(Faction faction, Lord ownerLord, int kind)
        { Faction=faction; OwnerLord=ownerLord; Kind=kind; }
        public void ExposeData()
        {
            Scribe_References.Look(ref Faction,"rhfNativeFactionS41");
            Scribe_References.Look(ref OwnerLord,"rhfNativeOwnerLordS41AJ");
            Scribe_Values.Look(ref Kind,"rhfNativeKindS41",0);
            Scribe_Values.Look(ref Stage,"rhfNativeStageS41",0);
            Scribe_Values.Look(ref Members,"rhfNativeMembersS41",0);
            Scribe_Values.Look(ref Animals,"rhfNativeAnimalsS41",0);
            Scribe_Values.Look(ref Builders,"rhfNativeBuildersS41",0);
            Scribe_Values.Look(ref LastVisibleEnemyCell,"rhfNativeEnemyCellS41AI",IntVec3.Invalid);
            Scribe_Values.Look(ref LastEnemySeenTick,"rhfNativeEnemyTickS41AI",0);
            Scribe_Values.Look(ref LastObservedTick,"rhfNativeSeenS41",0);
            Scribe_Values.Look(ref Rally,"rhfNativeRallyS41",IntVec3.Invalid);
        }
    }

    internal static class RHFNativeDoctrineS41
    {
        internal const int Siege=1;
        internal const int Animals=2;
        internal const int Building=1;
        internal const int Assault=2;
        private const int MaxActors=96;
        private const int ReobserveTicks=360;
        private const int ExpireTicks=1200;

        internal static bool IsAnimalLord(Lord lord)
        {
            if(lord?.ownedPawns==null)return false;
            // Bound the observation even when another mod creates a huge Lord.
            for(int i=0;i<lord.ownedPawns.Count && i<MaxActors;i++)
                if(lord.ownedPawns[i]?.RaceProps?.Animal==true)
                    return true;
            return false;
        }

        // Identity must be exact: two siege or animal Lords may share a
        // faction, even in a single RHF operation.
        internal static RHFNativeWingS41 Find(RHFOperation op,
            Faction faction,int kind,Lord ownerLord)
        {
            if(op?.NativeWingsS41==null || faction==null || ownerLord==null)
                return null;
            foreach(RHFNativeWingS41 state in op.NativeWingsS41)
                if(state!=null && state.Kind==kind && state.Faction==faction &&
                   state.OwnerLord==ownerLord)return state;
            return null;
        }

        // Support chooses the nearest CURRENT native Lord, not an arbitrary
        // first entry of its faction. A past siege/pack must never hijack a
        // newly spawned independent wing after Save/Load or pawn losses.
        internal static RHFNativeWingS41 FindForSupport(RHFOperation op,
            Faction faction,int kind,IntVec3 supporterCell,Map map,int now)
        {
            if(op?.NativeWingsS41==null || faction==null || map==null)
                return null;
            RHFNativeWingS41 best=null;
            int bestScore=int.MaxValue;
            foreach(RHFNativeWingS41 state in op.NativeWingsS41)
            {
                if(state==null || state.Kind!=kind || state.Faction!=faction ||
                   state.OwnerLord==null || state.OwnerLord.faction!=faction ||
                   state.OwnerLord.ownedPawns==null ||
                   state.OwnerLord.ownedPawns.Count==0 ||
                   !state.Rally.IsValid || !state.Rally.InBounds(map) ||
                   now-state.LastObservedTick>ReobserveTicks+90 ||
                   (kind==Siege && state.Stage!=Building) ||
                   (kind==Animals && state.Animals<=0))continue;
                // Prefer actual near defenders. Don't send a supporting team
                // across the entire colony to the OTHER same-faction siege.
                int score=(state.Rally-supporterCell).LengthHorizontalSquared;
                if(score>=bestScore)continue;
                bestScore=score;best=state;
            }
            return best;
        }

        [DebugAction("NJR - ТЕСТЫ | ДИАГНОСТИКА",
            "37 — S4.1 Native Lords: siege / beasts",
            actionType=DebugActionType.Action,
            allowedGameStates=AllowedGameStates.PlayingOnMap)]
        public static void InspectNativeLords()
        {
            Map map=Verse.Find.CurrentMap;
            List<RHFOperation> ops=map==null ? null :
                RHFMissionIntegrityV130.Operations(map);
            var report=new StringBuilder(600);
            report.AppendLine("RHF S4.1 native Lord / support audit");
            if(ops==null || ops.Count==0)
                report.AppendLine("No registered operations on this map.");
            else for(int i=0;i<ops.Count && i<4;i++)
            {
                RHFOperation op=ops[i];
                if(op==null)continue;
                report.AppendLine("Operation "+op.CreatedTick+
                    " / type="+op.Type+" / doctrine="+op.Doctrine+
                    " / phase="+op.Phase);
                if(op.NativeWingsS41==null || op.NativeWingsS41.Count==0)
                    report.AppendLine("  No native siege/animal Lords observed.");
                else foreach(RHFNativeWingS41 wing in op.NativeWingsS41)
                {
                    if(wing==null)continue;
                    report.AppendLine("  "+(wing.Kind==Siege?"SIEGE":"ANIMALS")+
                        " / "+(wing.Faction?.Name??"unknown")+
                        " / lord="+(wing.OwnerLord?.GetUniqueLoadID()??"missing")+
                        " / stage="+wing.Stage+
                        " / members="+wing.Members+
                        " / animals="+wing.Animals+
                        " / builders="+wing.Builders+
                        " / rally="+wing.Rally+
                        " / last="+wing.LastObservedTick);
                }
            }
            Verse.Find.WindowStack.Add(new Dialog_MessageBox(report.ToString()));
        }

        internal static void Observe(RHFOperation op,Map map,int now)
        {
            if(op==null || map==null || !op.Deployed || op.Finished ||
               now<op.NextNativeObservationS41)return;
            op.NextNativeObservationS41=now+ReobserveTicks;
            if(op.NativeWingsS41==null)
                op.NativeWingsS41=new List<RHFNativeWingS41>();
            var seenLords=new HashSet<Lord>();
            ObserveList(op,map,now,op.Squad,seenLords);
            ObserveList(op,map,now,op.Diversion,seenLords);
            // Beast-only Lords do not contain a registered human raider.
            // Their pawns are bound only by the actual incident's SpawnSetup
            // or same-call before/after spawn snapshot, never by faction alone.
            ObserveList(op,map,now,op.NativeAnimalsS41,seenLords);
            for(int i=op.NativeWingsS41.Count-1;i>=0;i--)
            {
                RHFNativeWingS41 state=op.NativeWingsS41[i];
                if(state==null || state.Faction==null ||
                   state.OwnerLord==null || state.OwnerLord.ownedPawns==null ||
                   state.OwnerLord.ownedPawns.Count==0 ||
                   now-state.LastObservedTick>ExpireTicks)
                    op.NativeWingsS41.RemoveAt(i);
            }
        }

        private static void ObserveList(RHFOperation op,Map map,int now,
            List<Pawn> members,HashSet<Lord> seenLords)
        {
            if(members==null)return;
            for(int i=0;i<members.Count && i<MaxActors;i++)
            {
                Pawn p=members[i];
                if(p==null || p.Map!=map || p.Faction==null || p.Dead)continue;
                Lord lord=p.GetLord();
                if(lord==null || !seenLords.Add(lord))continue;
                int kind=lord.LordJob is LordJob_Siege ? Siege :
                    IsAnimalLord(lord) ? Animals : 0;
                if(kind==0)continue;
                RHFNativeWingS41 state=Find(op,p.Faction,kind,lord);
                if(state==null)
                {
                    if(op.NativeWingsS41.Count>=12)continue;
                    state=new RHFNativeWingS41(p.Faction,lord,kind);
                    op.NativeWingsS41.Add(state);
                }
                if(state.LastObservedTick!=now)
                {
                    state.Members=0;
                    state.Animals=0;
                    state.Builders=0;
                    state.LastObservedTick=now;
                }
                state.Stage=kind==Siege ?
                    lord.CurLordToil is LordToil_Siege ? Building :
                    lord.CurLordToil is LordToil_AssaultColony ? Assault : 0 : Assault;
                if(kind==Siege && lord.CurLordToil is LordToil_Siege siege &&
                   siege.FlagLoc.IsValid)
                    state.Rally=siege.FlagLoc;
                Pawn beastFront=null;
                int bestDistance=int.MaxValue;
                for(int j=0;j<lord.ownedPawns.Count && j<MaxActors;j++)
                {
                    Pawn member=lord.ownedPawns[j];
                    if(member==null || member.Map!=map || member.Dead)continue;
                    state.Members++;
                    if(member.RaceProps?.Animal==true)
                    {
                        state.Animals++;
                        int d=(member.Position-map.Center).LengthHorizontalSquared;
                        if(d<bestDistance){bestDistance=d;beastFront=member;}
                    }
                    if(member.mindState?.duty?.def==DutyDefOf.Build)
                        state.Builders++;
                }
                if(kind==Animals && beastFront!=null)
                {
                    state.Rally=beastFront.Position;
                    // Only line-of-sight contact by the current animal vanguard
                    // is eligible for a shared tactical threat indicator.
                    int scanned=0;
                    Pawn closest=null;
                    int closestDist=900;
                    foreach(Pawn enemy in map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer))
                    {
                        if(++scanned>48)break;
                        if(enemy==null || enemy.Dead || enemy.Downed ||
                           !enemy.Spawned || enemy.Position.Fogged(map))continue;
                        int d=(enemy.Position-beastFront.Position).LengthHorizontalSquared;
                        if(d>=closestDist ||
                           !GenSight.LineOfSight(beastFront.Position,enemy.Position,map))continue;
                        closest=enemy;
                        closestDist=d;
                    }
                    if(closest!=null)
                    {
                        state.LastVisibleEnemyCell=closest.Position;
                        state.LastEnemySeenTick=now;
                    }
                    else if(now-state.LastEnemySeenTick>900)
                        state.LastVisibleEnemyCell=IntVec3.Invalid;
                }
            }
        }

        // Select a *reachable*, flank-support position near the actual herd
        // but BEHIND its observed direction of attack. Limited to 12 candidates.
        // No forced job or changes to the animal's native Lord.
        internal static bool TryBeastSupportAnchor(RHFNativeWingS41 wing,
            Pawn supporter,Map map,int now,out IntVec3 anchor)
        {
            anchor=IntVec3.Invalid;
            if(wing==null || supporter==null || supporter.Map!=map ||
               !wing.Rally.IsValid || !wing.Rally.InBounds(map))return false;
            IntVec3 threat=wing.LastVisibleEnemyCell.IsValid &&
                now-wing.LastEnemySeenTick<=900 ? wing.LastVisibleEnemyCell : map.Center;
            int dx=threat.x-wing.Rally.x;
            int dz=threat.z-wing.Rally.z;
            int length=Math.Max(Math.Abs(dx),Math.Abs(dz));
            if(length==0)return false;
            // Perpendicular lateral flank; hold behind animals, not in their path.
            int perpX=-dz;
            int perpZ=dx;
            int side=supporter.Position.x*perpX+supporter.Position.z*perpZ>=
                wing.Rally.x*perpX+wing.Rally.z*perpZ ? 1 : -1;
            int ax=wing.Rally.x - dx*4/length + side*perpX*7/length;
            int az=wing.Rally.z - dz*4/length + side*perpZ*7/length;
            int[] ox={0,2,-2,0,0,3,-3,2,-2,4,-4,0};
            int[] oz={0,0,0,2,-2,2,-2,-2,2,0,0,4};
            for(int i=0;i<ox.Length;i++)
            {
                IntVec3 c=new IntVec3(ax+ox[i],0,az+oz[i]);
                if(!c.InBounds(map) || !c.Standable(map) ||
                   c.GetEdifice(map)!=null || c.Fogged(map) ||
                   (c-wing.Rally).LengthHorizontalSquared>225 ||
                   !supporter.CanReach(c,PathEndMode.OnCell,Danger.Some))continue;
                anchor=c;
                return true;
            }
            return false;
        }
    }
}
