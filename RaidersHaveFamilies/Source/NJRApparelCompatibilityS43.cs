// NJR R007: keep faction/race/mod-owned worn apparel intact on raid arrival.
// The ordinary RimWorld apparel compatibility decision is authoritative.
using System;
using RimWorld;
using Verse;

namespace RaidersHaveFamilies
{
    internal static class NJRApparelCompatibilityS43
    {
        internal static bool CanAdd(Pawn pawn,ThingDef apparel)
        {
            if(pawn?.apparel==null || apparel?.apparel==null ||
               pawn.RaceProps?.body==null)return false;
            // Never let a generated support item strip gear supplied by
            // HAR, CE, a faction PawnKind, or another apparel/equipment mod.
            // If a modded body/apparel compatibility hook throws, skip the
            // OPTIONAL upgrade rather than abort the faction's raid.
            try
            {
                foreach(Apparel worn in pawn.apparel.WornApparel)
                {
                    if(worn?.def==null ||
                       !ApparelUtility.CanWearTogether(worn.def,apparel,pawn.RaceProps.body))
                        return false;
                }
                return true;
            }
            catch(Exception e)
            {
                if(RHFMod.Settings?.VerboseLogging==true)
                    Log.Warning("[NJR][ApparelS43] Skipping optional gear for "+
                        pawn.LabelShortCap+": "+e.Message);
                return false;
            }
        }
    }
}
