// NJR beta2: vanilla threat points are the unmodified basis.
// Each coalition faction and hired mercenary group receives its own actual
// pawn-generation budget, not a fraction of a pooled coalition budget.
using System;
using RimWorld;
using UnityEngine;
using Verse;
namespace RaidersHaveFamilies
{
    internal static class NJRThreatBudgetS41
    {
        internal static int CaptivesOf(Faction faction, RHFOperation op, Map map)
        {
            if(faction==null || op?.Targets==null)return 0;
            RHFGameLedger ledger=Current.Game?.GetComponent<RHFGameLedger>();
            int count=0;
            foreach(Pawn pawn in op.Targets)
            {
                if(pawn==null || pawn.Dead || pawn.MapHeld!=map)continue;
                RHFHostageRecord record=ledger?.FindHostage(pawn);
                if((record?.OriginalFaction ?? pawn.Faction)==faction)count++;
            }
            return count;
        }
        internal static float PartyPoints(Map map,Faction sponsor,int importance,
            int sameFactionCaptives,float globalStrength)
        {
            float vanilla=StorytellerUtility.DefaultThreatPointsNow(map);
            int pressure=Mathf.Clamp(RHFPoliticalReputation.Pressure(sponsor),-100,0);
            // Up to +80% VIP importance, +72% multiple captives and +50%
            // political anger; zero pressure gives NO extra strength.
            float modifier=1f + .10f*Mathf.Clamp(importance,0,8) +
                .12f*Mathf.Clamp(sameFactionCaptives-1,0,6) +
                .005f*(-pressure);
            // Do not impose arbitrary 300..15000 floors/caps: the vanilla
            // storyteller and PawnGroupMaker determine practical raid scale.
            return Mathf.Max(1f,vanilla*modifier*
                Mathf.Clamp(globalStrength,.5f,2f));
        }
    }
}
