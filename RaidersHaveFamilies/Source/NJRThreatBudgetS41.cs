// NJR beta2: vanilla threat points are the unmodified basis.
// Each coalition faction and hired mercenary group receives its own actual
// pawn-generation budget, not a fraction of a pooled coalition budget.
using System;
using System.Collections.Generic;
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
        // Count actual living captives on this colony map, not the selected
        // mission target list (which may contain only one VIP).
        private static int LiveCaptives(Map map,Faction faction)
        {
            var seen=new HashSet<Pawn>();
            var ledger=Current.Game?.GetComponent<RHFGameLedger>();
            foreach(Pawn p in map.mapPawns.AllPawnsSpawned)
                AddCaptive(p,map,faction,ledger,seen);
            if(ledger?.Hostages!=null)
                foreach(RHFHostageRecord record in ledger.Hostages)
                    AddCaptive(record?.Pawn,map,faction,ledger,seen);
            return seen.Count;
        }
        private static void AddCaptive(Pawn p,Map map,Faction faction,
            RHFGameLedger ledger,HashSet<Pawn> seen)
        {
            if(p==null || p.Dead || p.MapHeld!=map ||
                !(p.IsPrisonerOfColony || p.IsSlaveOfColony))return;
            if((ledger?.FindHostage(p)?.OriginalFaction ?? p.Faction)==faction)
                seen.Add(p);
        }
        // The storyteller normally reads periodically cached map wealth. A debug
        // click (including paused wealth edits) or a scheduled dispatch must use
        // the current map, not the last periodic recount. Never do this on pawn ticks.
        internal static float RefreshVanillaThreat(Map map)
        {
            float before=StorytellerUtility.DefaultThreatPointsNow(map);
            float wealthBefore=map.wealthWatcher.WealthTotal;
            map.wealthWatcher.ForceRecount(false);
            float wealthAfter=map.wealthWatcher.WealthTotal;
            float after=StorytellerUtility.DefaultThreatPointsNow(map);
            var difficulty=Find.Storyteller?.difficulty;
            string independent="unknown";
            string scale="unknown";
            if(difficulty!=null)
            {
                // Read only: do not override the player's storyteller settings.
                var type=difficulty.GetType();
                var mode=HarmonyLib.AccessTools.Field(type,"wealthIndependentProgressMode");
                var factor=HarmonyLib.AccessTools.Field(type,"threatScale");
                if(mode!=null)independent=Convert.ToString(mode.GetValue(difficulty));
                if(factor!=null)scale=Convert.ToString(factor.GetValue(difficulty),System.Globalization.CultureInfo.InvariantCulture);
            }
            Log.Message("[NJR][R061][Wealth] map="+System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(map)+
                " tick="+Find.TickManager.TicksGame+" wealthBefore="+wealthBefore.ToString("F1")+
                " wealthAfter="+wealthAfter.ToString("F1")+" storytellerWealth="+map.PlayerWealthForStoryteller.ToString("F1")+
                " threatBefore="+before.ToString("F2")+" threatAfter="+after.ToString("F2")+
                " wealthIndependent="+independent+" threatScale="+scale);
            return after;
        }
        internal static float FactionAllocation(Map map,RHFOperation op,Faction faction,float? vanillaSnapshot=null)
        {
            return PartyPoints(map,faction,op.SourceImportance,LiveCaptives(map,faction),
                RHFMod.Settings.InterventionStrength,vanillaSnapshot);
        }
        // One faction, one complete budget, irrespective of role/number of wings.
        // A distinct hired faction receives its own complete budget. Existing
        // relative weights are preserved only BETWEEN wings of the SAME faction.
        // Called at scheduling and again immediately before arrival; not on ticks.
        internal static void AllocateOperation(Map map,RHFOperation op,float? vanillaSnapshot=null)
        {
            if(map==null || op?.Parties==null)return;
            var groups=new Dictionary<Faction,List<RHFStrikeParty>>();
            foreach(RHFStrikeParty party in op.Parties)
            {
                if(party?.Faction==null || party.Attempted)continue;
                if(!groups.TryGetValue(party.Faction,out List<RHFStrikeParty> list))
                    groups[party.Faction]=list=new List<RHFStrikeParty>();
                list.Add(party);
            }
            foreach(var group in groups)
            {
                float budget=FactionAllocation(map,op,group.Key,vanillaSnapshot);
                float weights=0f;
                foreach(RHFStrikeParty wing in group.Value)weights+=Mathf.Max(.0001f,wing.Points);
                float remaining=budget;
                for(int i=0;i<group.Value.Count;i++)
                {
                    RHFStrikeParty wing=group.Value[i];
                    float allocated=i==group.Value.Count-1 ? remaining :
                        Mathf.Min(remaining,budget*Mathf.Max(.0001f,wing.Points)/weights);
                    wing.Points=allocated;remaining-=allocated;
                    Log.Message("[NJR][R060][Wing] faction="+group.Key.Name+
                        " role="+wing.Role+" factionTotal="+budget.ToString("F2")+
                        " allocated="+allocated.ToString("F2"));
                }
            }
            float aggregate=0f;
            foreach(RHFStrikeParty party in op.Parties)
                if(party!=null)aggregate+=party.Points;
            op.ThreatPoints=aggregate;
        }
        internal static float PartyPoints(Map map,Faction sponsor,int importance,
            int sameFactionCaptives,float globalStrength,float? vanillaSnapshot=null)
        {
            float vanilla=vanillaSnapshot ?? StorytellerUtility.DefaultThreatPointsNow(map);
            int pressure=Mathf.Clamp(RHFPoliticalReputation.Pressure(sponsor),-100,0);
            // Up to +80% VIP importance, +72% multiple captives and +50%
            // political anger; zero pressure gives NO extra strength.
            float modifier=1f + .10f*Mathf.Clamp(importance,0,8) +
                .12f*Mathf.Clamp(sameFactionCaptives-1,0,6) +
                .005f*(-pressure);
            // Do not impose arbitrary 300..15000 floors/caps: the vanilla
            // storyteller and PawnGroupMaker determine practical raid scale.
            float strength=Mathf.Clamp(globalStrength,.5f,2f);
            float points=Mathf.Max(1f,vanilla*modifier*strength);
            Log.Message("[NJR][R059][Threat] map="+System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(map)+
                " faction="+sponsor?.Name+" vanilla="+vanilla.ToString("F1")+
                " importance="+importance+" captives="+sameFactionCaptives+
                " pressure="+pressure+" strength="+strength.ToString("F2")+
                " modifier="+modifier.ToString("F2")+" allocated="+points.ToString("F1"));
            return points;
        }
    }
}
