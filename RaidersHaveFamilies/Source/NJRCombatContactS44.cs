// NJR R011: mission-scoped physical-contact response shared by rescue and offense.
// No global pawn patches, no fake damage, no omniscient target acquisition.
using System;
using RimWorld;
using Verse;
using Verse.AI;

namespace RaidersHaveFamilies
{
    internal static class NJRCombatContactS44
    {
        private static bool Active(Pawn pawn, Map map) =>
            pawn != null && pawn.Spawned && pawn.Map == map && !pawn.Dead &&
            !pawn.Downed;

        private static bool Protected(RHFOperation op, Pawn pawn) =>
            pawn != null && op != null &&
            (op.Squad?.Contains(pawn) == true ||
             op.Diversion?.Contains(pawn) == true ||
             op.Targets?.Contains(pawn) == true ||
             op.Freed?.Contains(pawn) == true ||
             op.NativeAnimalsS41?.Contains(pawn) == true);

        // A contact is relevant if it threatens the soldier or the real mission
        // corridor. A stationary turret behind an unrelated mountain is NOT
        // omnisciently selected, but a visible gun across the entry corridor is.
        private static bool OnCourse(IntVec3 from, IntVec3 objective, IntVec3 threat)
        {
            if (!objective.IsValid || objective == from)return false;
            long dx=objective.x-from.x, dz=objective.z-from.z;
            long px=threat.x-from.x, pz=threat.z-from.z;
            long length=dx*dx+dz*dz;
            long projection=px*dx+pz*dz;
            if(projection<0 || projection>length)return false;
            long cross=dx*pz-dz*px;
            return cross*cross<=144L*length;
        }

        internal static Thing VisibleContact(Pawn soldier,RHFOperation op,
            IntVec3 objective,int radius)
        {
            Map map=soldier?.Map;
            if(map==null || op==null)return null;
            float range=RHFCombatPlannerV125.WeaponRange(soldier);
            int maxRadius=Math.Max(radius,(int)Math.Ceiling(range)+4);
            int maxSq=maxRadius*maxRadius;
            Thing best=null;
            int bestScore=int.MaxValue;
            foreach(Pawn enemy in map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer))
            {
                if(!Active(enemy,map) || Protected(op,enemy) || enemy==soldier)continue;
                int distance=(soldier.Position-enemy.Position).LengthHorizontalSquared;
                if(distance>maxSq ||
                   !GenSight.LineOfSight(soldier.Position,enemy.Position,map))continue;
                bool onCourse=OnCourse(soldier.Position,objective,enemy.Position);
                if(!onCourse && distance>144)continue;
                if(RHFRescueFireDisciplineV136.UnsafeLine(op,soldier,enemy))continue;
                int score=distance-(onCourse?120:0)-250;
                if(score<bestScore){best=enemy;bestScore=score;}
            }
            foreach(Building gun in map.listerBuildings.allBuildingsColonist)
            {
                if(!RHFCombatPlannerV125.IsTurret(gun))continue;
                int distance=(soldier.Position-gun.Position).LengthHorizontalSquared;
                if(distance>maxSq ||
                   !GenSight.LineOfSight(soldier.Position,gun.Position,map))continue;
                bool onCourse=OnCourse(soldier.Position,objective,gun.Position);
                if(!onCourse && distance>225 && op.Phase==2)continue;
                if(RHFRescueFireDisciplineV136.UnsafeLine(op,soldier,gun))continue;
                int score=distance-(onCourse?180:0)-220;
                if(score<bestScore){best=gun;bestScore=score;}
            }
            return best;
        }

        private static Job Order(JobDef def,LocalTargetInfo target,int duration)
        {
            Job job=JobMaker.MakeJob(def,target);
            job.expiryInterval=duration;
            job.checkOverrideOnExpire=true;
            return job;
        }

        // Leave protected VIPs, boarders and the designated engineer alone.
        // A guard may manoeuvre to a reachable firing position, but does not
        // chase a target beyond the operation's immediate contact perimeter.
        internal static Job Respond(Pawn soldier,RHFOperation op,IntVec3 anchor,
            bool mayManeuver,int radius=22)
        {
            if(soldier==null || op==null || soldier.Map==null ||
               soldier.WorkTagIsDisabled(WorkTags.Violent) ||
               RHFCombatPlannerV125.IsCombatEmergencyJob(soldier) ||
               Protected(op,soldier) && op.Freed?.Contains(soldier)==true)
                return null;
            Map map=soldier.Map;
            Thing enemy=VisibleContact(soldier,op,anchor,radius);
            if(enemy==null)return null;
            int distance=(soldier.Position-enemy.Position).LengthHorizontalSquared;
            float weaponRange=RHFCombatPlannerV125.WeaponRange(soldier);
            if(weaponRange>3f && distance<=weaponRange*weaponRange &&
               GenSight.LineOfSight(soldier.Position,enemy.Position,map))
                return Order(JobDefOf.AttackStatic,enemy,150);
            if(weaponRange<=3f)
            {
                if((mayManeuver || distance<=9) &&
                   soldier.CanReach(enemy,PathEndMode.Touch,Danger.Deadly))
                    return Order(JobDefOf.AttackMelee,enemy,180);
                return null;
            }
            if(!mayManeuver)return null;
            // Find an actual reachable firing tile rather than continuing a
            // blind Goto past a visible turret. Exhaust the physical map if
            // necessary; no arbitrary combat/TPS search cap.
            IntVec3 best=IntVec3.Invalid;
            int score=int.MaxValue;
            for(int x=0;x<map.Size.x;x++)for(int z=0;z<map.Size.z;z++)
            {
                IntVec3 cell=new IntVec3(x,0,z);
                if(!cell.Standable(map) || cell.GetEdifice(map)!=null)continue;
                int shot=(cell-enemy.Position).LengthHorizontalSquared;
                if(shot>weaponRange*weaponRange || shot<9 ||
                   !GenSight.LineOfSight(cell,enemy.Position,map))continue;
                int move=(cell-soldier.Position).LengthHorizontalSquared;
                if(move>=score || move>radius*radius ||
                   !soldier.CanReach(cell,PathEndMode.OnCell,Danger.Deadly))continue;
                best=cell;score=move;
            }
            return best.IsValid && best!=soldier.Position ?
                Order(JobDefOf.Goto,best,150) : null;
        }

        // When there is no open way through a hostile perimeter, find a REAL
        // accessible blocking door or wall. Never target chairs, supplies or
        // unowned scenery just to keep the pawn busy.
        internal static Building BlockingStructure(Pawn soldier,IntVec3 destination)
        {
            Map map=soldier?.Map;
            if(map==null || !destination.IsValid)return null;
            Building best=null;int bestScore=int.MaxValue;
            foreach(Building barrier in map.listerBuildings.allBuildingsColonist)
            {
                if(!RHFCombatPlannerV125.RequiresBreach(barrier) ||
                   !OnCourse(soldier.Position,destination,barrier.Position) ||
                   !soldier.CanReach(barrier.Position,PathEndMode.Touch,Danger.Deadly))continue;
                long dx=destination.x-soldier.Position.x;
                long dz=destination.z-soldier.Position.z;
                long bx=barrier.Position.x-soldier.Position.x;
                long bz=barrier.Position.z-soldier.Position.z;
                long cross=dx*bz-dz*bx;
                long length=dx*dx+dz*dz;
                if(length==0 || cross*cross>25L*length)continue;
                int score=(barrier.Position-destination).LengthHorizontalSquared+
                    (barrier.Position-soldier.Position).LengthHorizontalSquared+
                    RHFCombatPlannerV125.BreachWorkCost(barrier);
                if(score<bestScore){bestScore=score;best=barrier;}
            }
            return best;
        }

        internal static Job BreachToward(Pawn soldier,RHFOperation op,IntVec3 destination)
        {
            if(soldier?.Map==null || op==null || !destination.IsValid ||
               soldier.WorkTagIsDisabled(WorkTags.Violent) ||
               soldier.health?.capacities==null ||
               !soldier.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation))
                return null;
            Building best=BlockingStructure(soldier,destination);
            if(best==null)return null;
            // Prefer a real, safe physical attack over a fake route through a wall.
            if(soldier.CanReach(best.Position,PathEndMode.Touch,Danger.Deadly))
                return Order(JobDefOf.AttackMelee,best,600);
            return null;
        }
    }
}
