// RHF S2: mission-scoped target selection and conservative CE firing lanes.
// No Harmony patch to generic pawns/verbs; only RHF rescue idle duties use it.
using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace RaidersHaveFamilies
{
    internal static class RHFRescueFireDisciplineV136
    {
        internal static RHFOperation RescueFor(Pawn pawn)
        {
            Map map=pawn?.Map;
            if(map==null)return null;
            List<RHFOperation> operations=RHFMissionIntegrityV130.Operations(map);
            if(operations==null)return null;
            for(int i=0;i<operations.Count && i<4;i++)
            {
                RHFOperation op=operations[i];
                if(op!=null && op.Type==(int)RHFOperationType.Rescue &&
                   op.Deployed && !op.Finished && op.Squad!=null &&
                   op.Squad.Contains(pawn))return op;
            }
            return null;
        }

        private static bool IsProtected(RHFOperation op,Pawn pawn)
        {
            return pawn!=null && op!=null &&
                ((op.Squad!=null && op.Squad.Contains(pawn)) ||
                 (op.Diversion!=null && op.Diversion.Contains(pawn)) ||
                 (op.Targets!=null && op.Targets.Contains(pawn)) ||
                 (op.Freed!=null && op.Freed.Contains(pawn)) ||
                 (op.NativeAnimalsS41!=null && op.NativeAnimalsS41.Contains(pawn)));
        }

        // CE fires physical projectiles. Neutrality via HostileTo alone is not
        // enough. Reject directed fire whose approximate two-cell corridor
        // intersects a protected hostage or another RHF operative, including
        // friendly mercenaries from a faction still hostile diplomatically.
        // This is NOT a claim to prevent all inaccuracy, ricochets or explosions.
        internal static bool UnsafeLine(RHFOperation op,Pawn shooter,Thing target)
        {
            if(op==null || shooter==null || target==null)return true;
            if(target is Pawn attacked && IsProtected(op,attacked))return true;
            Map map=shooter.Map;
            if(map==null || target.MapHeld!=map)return true;
            long dx=target.Position.x-shooter.Position.x;
            long dz=target.Position.z-shooter.Position.z;
            long len=dx*dx+dz*dz;
            if(len==0)return true;
            // Iterate only the bounded mission roster, not every pawn on a
            // heavily modded map, and never alter global faction relationships.
            return MemberInLine(op.Squad,shooter,target,dx,dz,len) ||
                MemberInLine(op.Diversion,shooter,target,dx,dz,len) ||
                MemberInLine(op.Targets,shooter,target,dx,dz,len) ||
                MemberInLine(op.Freed,shooter,target,dx,dz,len) ||
                MemberInLine(op.NativeAnimalsS41,shooter,target,dx,dz,len);
        }

        private static bool MemberInLine(List<Pawn> members,Pawn shooter,Thing target,
            long dx,long dz,long len)
        {
            if(members==null)return false;
            for(int i=0;i<members.Count && i<96;i++)
            {
                Pawn pawn=members[i];
                if(pawn==null || pawn==shooter || pawn==target ||
                   !pawn.Spawned || pawn.Dead || pawn.Map!=shooter.Map)continue;
                long px=pawn.Position.x-shooter.Position.x;
                long pz=pawn.Position.z-shooter.Position.z;
                long dot=px*dx+pz*dz;
                if(dot<=0 || dot>=len)continue;
                long cross=px*dz-pz*dx;
                if(cross*cross<=4L*len)return true;
            }
            return false;
        }
    }

    // This executes ONLY in the RHF rescue Lord's custom hold duty. Unlike
    // JobGiver_AIFightEnemies it cannot choose captives or allied mercenaries
    // as targets and it cannot chase a colonist all across the settlement.
    public sealed class RHFJobGiverMissionDefenseV136 : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            if(pawn==null || !pawn.Spawned || pawn.Downed || pawn.Map==null ||
               pawn.GetLord()?.LordJob is not RHFRescueLordJobV124)return null;
            if(RHFCombatPlannerV125.IsCombatEmergencyJob(pawn))return null;
            RHFOperation op=RHFRescueFireDisciplineV136.RescueFor(pawn);
            if(op==null)return null;
            Map map=pawn.Map;
            float range=RHFCombatPlannerV125.WeaponRange(pawn);
            float safeRange=range>0f?Math.Min(range,55f):2f;
            float limit=safeRange*safeRange;
            Thing target=null;
            float best=float.MaxValue;
            // All player-owned defenders: colonists, Biotech mechanoids,
            // trained combat animals. Never fire at RHF-protected captives.
            foreach(Pawn defender in map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer))
            {
                if(defender==null || !defender.Spawned || defender.Dead ||
                   defender.Downed || defender==pawn || defender.Map!=map)continue;
                float dist=(defender.Position-pawn.Position).LengthHorizontalSquared;
                if(dist>limit || dist>=best ||
                   !GenSight.LineOfSight(pawn.Position,defender.Position,map) ||
                   RHFRescueFireDisciplineV136.UnsafeLine(op,pawn,defender))continue;
                target=defender;best=dist;
            }
            // Turrets are lethal defensive combatants, not ordinary structures.
            // Do not turn this into a generic attack on colony buildings.
            foreach(Building turret in map.listerBuildings.allBuildingsColonist)
            {
                if(!RHFCombatPlannerV125.IsTurret(turret))continue;
                float dist=(turret.Position-pawn.Position).LengthHorizontalSquared;
                if(dist>limit || dist>=best ||
                   !GenSight.LineOfSight(pawn.Position,turret.Position,map) ||
                   RHFRescueFireDisciplineV136.UnsafeLine(op,pawn,turret))continue;
                target=turret;best=dist;
            }
            if(target==null)return null;
            Job job=JobMaker.MakeJob(range>0f?JobDefOf.AttackStatic:JobDefOf.AttackMelee,target);
            job.expiryInterval=240;
            job.checkOverrideOnExpire=true;
            return job;
        }
    }
}
