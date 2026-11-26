using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace RaidersHaveFamilies
{
    // R050: share verified incoming prisoner attacks with the local rescue squad.
    [HarmonyPatch(typeof(Thing),"TakeDamage",new System.Type[]{typeof(DamageInfo)})]
    internal static class NJRPrisonerDefenseR047
    {
        private sealed class Contact
        {
            internal Pawn Attacker, Victim;
            internal int Tick;
            internal readonly List<Pawn> Responders=new List<Pawn>(2);
        }
        private static readonly ConditionalWeakTable<RHFOperation,Contact> Contacts=new ConditionalWeakTable<RHFOperation,Contact>();
        private static bool ValidAttacker(RHFOperation op,Pawn attacker,Map map)
        {
            return op!=null && !op.Finished && op.Phase<=3 && op.FortifyPrison &&
                attacker!=null && attacker.IsPrisonerOfColony && !attacker.Dead && !attacker.Downed &&
                attacker.Spawned && attacker.Map==map && op.Targets?.Contains(attacker)!=true &&
                op.Freed?.Contains(attacker)!=true && op.Squad?.Contains(attacker)!=true;
        }
        private static bool Eligible(RHFOperation op,Pawn pawn,Pawn attacker)
        {
            return pawn!=null && pawn.Spawned && !pawn.Dead && !pawn.Downed && pawn.Map==attacker.Map &&
                op.Squad?.Contains(pawn)==true && op.Freed?.Contains(pawn)!=true &&
                !pawn.WorkTagIsDisabled(WorkTags.Violent) && !RHFCombatPlannerV125.IsCombatEmergencyJob(pawn) &&
                (pawn.Position-attacker.Position).LengthHorizontalSquared<=36;
        }
        private static bool BusyCritical(RHFOperation op,Pawn pawn)
        {
            if(op.Phase!=3)return false;
            RHFOrderS41 order=op.BoardS41?.For(pawn,false);
            return pawn==op.BoardS41?.PodEngineerS41 ||
                order?.Role==(int)RHFRoleS41.Engineer || order?.Role==(int)RHFRoleS41.PodSupply ||
                RHFPodSupplyS41.HasCargo(op,pawn);
        }
        private static void Prefix(Thing __instance,DamageInfo __0)
        {
            if(!(__instance is Pawn victim) || !(__0.Instigator is Pawn attacker) ||
               !attacker.IsPrisonerOfColony || victim.Map==null || attacker.Map!=victim.Map ||
               victim.Faction==Faction.OfPlayer || __0.Amount<=0f)return;
            RHFOperation op=RHFRescuePlannerS41.MissionFor(victim.Map,victim);
            if(!ValidAttacker(op,attacker,victim.Map) || op.Squad?.Contains(victim)!=true)return;
            int now=Find.TickManager.TicksGame;
            Contact c=Contacts.GetValue(op,_=>new Contact());
            bool fresh=c.Attacker!=attacker || now-c.Tick>360;
            c.Attacker=attacker;c.Victim=victim;c.Tick=now;
            // A real hit authorizes local defense, not a general hunt.
            // Keep at most two responders; reserve the primary builder and
            // supply workers for evacuation unless personally threatened.
            if(fresh)c.Responders.Clear();
            for(int i=c.Responders.Count-1;i>=0;i--)
                if(!Eligible(op,c.Responders[i],attacker) ||
                   (c.Responders[i]!=victim && BusyCritical(op,c.Responders[i])))
                    c.Responders.RemoveAt(i);
            if(Eligible(op,victim,attacker) && !c.Responders.Contains(victim) && c.Responders.Count<2)
                c.Responders.Add(victim);
            while(c.Responders.Count<2)
            {
                Pawn best=null;int score=int.MaxValue;
                foreach(Pawn p in op.Squad)
                {
                    if(!Eligible(op,p,attacker) || c.Responders.Contains(p) || BusyCritical(op,p) ||
                       !GenSight.LineOfSight(p.Position,attacker.Position,p.Map) ||
                       !p.CanReach(attacker,PathEndMode.Touch,Danger.Deadly))continue;
                    int distance=(p.Position-attacker.Position).LengthHorizontalSquared;
                    if(distance<score){best=p;score=distance;}
                }
                if(best==null)break;
                c.Responders.Add(best);
                Log.Message("[NJR][R050][PrisonerDefense] assist="+best.LabelShortCap+" attacker="+attacker.LabelShortCap+" victim="+victim.LabelShortCap);
            }
            if(fresh)Log.Message("[NJR][R050][PrisonerDefense] incoming="+attacker.LabelShortCap+" victim="+victim.LabelShortCap+" responders="+c.Responders.Count);
        }
        internal static bool MayDefend(RHFOperation op,Pawn pawn,Pawn attacker)
        {
            return pawn!=null && ValidAttacker(op,attacker,pawn.Map) &&
                Contacts.TryGetValue(op,out Contact c) && c.Attacker==attacker &&
                Find.TickManager.TicksGame-c.Tick<=360 && c.Responders.Contains(pawn) &&
                Eligible(op,pawn,attacker) &&
                (pawn==c.Victim || (!BusyCritical(op,pawn) && GenSight.LineOfSight(pawn.Position,attacker.Position,pawn.Map)));
        }
        internal static Job Counter(RHFOperation op,Pawn pawn)
        {
            if(op==null || !Contacts.TryGetValue(op,out Contact c) || !MayDefend(op,pawn,c.Attacker))return null;
            Job job=JobMaker.MakeJob(JobDefOf.AttackMelee,c.Attacker);job.expiryInterval=90;job.checkOverrideOnExpire=true;return job;
        }
    }
}
