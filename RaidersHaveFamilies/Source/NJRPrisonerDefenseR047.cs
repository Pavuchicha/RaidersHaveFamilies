using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace RaidersHaveFamilies
{
    [HarmonyPatch(typeof(Thing),"TakeDamage",new System.Type[]{typeof(DamageInfo)})]
    internal static class NJRPrisonerDefenseR047
    {
        private sealed class Contact { internal Pawn Attacker; internal int Tick; }
        private static readonly ConditionalWeakTable<Pawn,Contact> Contacts=new ConditionalWeakTable<Pawn,Contact>();
        private static void Prefix(Thing __instance,DamageInfo __0)
        {
            if(!(__instance is Pawn victim) || !(__0.Instigator is Pawn attacker) ||
               !attacker.IsPrisonerOfColony || victim.Map==null || attacker.Map!=victim.Map || victim.Faction==Faction.OfPlayer)return;
            RHFOperation op=RHFRescuePlannerS41.MissionFor(victim.Map,victim);
            if(op==null || !op.FortifyPrison || op.Squad?.Contains(victim)!=true || op.Targets?.Contains(attacker)==true)return;
            Contact c=Contacts.GetValue(victim,_=>new Contact());c.Attacker=attacker;c.Tick=Find.TickManager.TicksGame;
        }
        internal static bool MayDefend(RHFOperation op,Pawn pawn,Pawn attacker)
        {
            return op!=null && op.FortifyPrison && op.Squad?.Contains(pawn)==true && op.Freed?.Contains(pawn)!=true &&
                attacker!=null && attacker.IsPrisonerOfColony && !attacker.Dead && !attacker.Downed && attacker.Spawned && attacker.Map==pawn.Map &&
                op.Targets?.Contains(attacker)!=true && Contacts.TryGetValue(pawn,out Contact c) && c.Attacker==attacker &&
                Find.TickManager.TicksGame-c.Tick<=180 && (pawn.Position-attacker.Position).LengthHorizontalSquared<=2 &&
                !pawn.WorkTagIsDisabled(WorkTags.Violent) && !RHFCombatPlannerV125.IsCombatEmergencyJob(pawn);
        }
        internal static Job Counter(RHFOperation op,Pawn pawn)
        {
            if(op?.FortifyPrison!=true || !Contacts.TryGetValue(pawn,out Contact c) || !MayDefend(op,pawn,c.Attacker))return null;
            Job job=JobMaker.MakeJob(JobDefOf.AttackMelee,c.Attacker);job.expiryInterval=90;job.checkOverrideOnExpire=true;return job;
        }
    }
}
