// Internal consequences for factions whose vanilla goodwill is immutable,
// without altering their permanent hostility, alliances or faction definitions.
using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RaidersHaveFamilies
{
    public sealed class RHFFactionStanding : IExposable
    {
        public Faction Faction;
        public int Pressure;
        public RHFFactionStanding() { }
        public RHFFactionStanding(Faction faction) { Faction=faction; }
        public void ExposeData()
        {
            Scribe_References.Look(ref Faction,"faction");
            Scribe_Values.Look(ref Pressure,"pressure",0);
        }
    }

    public static class RHFPoliticalReputation
    {
        // Pressure is additional to vanilla goodwill, and never rewrites faction allegiance.
        public static int Pressure(Faction faction)
        {
            var ledger=Current.Game?.GetComponent<RHFGameLedger>();
            if(ledger?.FactionStandings==null || faction==null)return 0;
            for(int i=0;i<ledger.FactionStandings.Count;i++)
                if(ledger.FactionStandings[i]?.Faction==faction)
                    return ledger.FactionStandings[i].Pressure;
            return 0;
        }

        public static void Change(Faction faction, int delta)
        {
            if(faction==null || faction.IsPlayer || delta==0)return;
            // The regular in-game diplomacy remains the primary consequence.
            faction.TryAffectGoodwillWith(Faction.OfPlayer,delta,
                canSendMessage:true,canSendHostilityLetter:true);
            var ledger=Current.Game?.GetComponent<RHFGameLedger>();
            if(ledger==null)return;
            var list=ledger.FactionStandings;
            RHFFactionStanding entry=null;
            for(int i=0;i<list.Count;i++)
                if(list[i]?.Faction==faction) {entry=list[i];break;}
            if(entry==null)
            {
                if(list.Count>=256)return;
                entry=new RHFFactionStanding(faction);
                list.Add(entry);
            }
            entry.Pressure=Math.Max(-1000,Math.Min(1000,entry.Pressure+delta));
        }
    }
}
