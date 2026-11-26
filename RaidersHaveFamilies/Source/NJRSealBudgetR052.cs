using System;
using RimWorld;
using Verse;
using UnityEngine;

namespace RaidersHaveFamilies
{
    internal static class NJRSealBudgetR052
    {
        private static bool Finite(float value)=>!float.IsNaN(value) && !float.IsInfinity(value) && value>0f;
        // Mandatory two seals at the existing minimum mission size. More are
        // bought as raid strength grows; keep at most eight per local site.
        internal static int DesiredCount(float points)=>Mathf.Clamp(2+Mathf.FloorToInt(points/1500f),2,8);
        internal static bool Plan(RHFOperation op,float points,float available)
        {
            ThingDef wall=ThingDefOf.Wall;
            if(wall==null || !wall.MadeFromStuff || wall.CostStuffCount<=0 || wall.stuffCategories==null)return false;
            float discretionary=Math.Min(points*0.10f,available);
            float maxMass=Mathf.Clamp(18f+points*0.002f,18f,42f);
            for(int count=DesiredCount(points);count>=2;count--)
            {
                ThingDef best=null,cheapest=null;float bestScore=float.MinValue,bestCost=0f,cheapCost=float.MaxValue;
                int bestUnits=0,cheapUnits=0;
                foreach(ThingDef stuff in DefDatabase<ThingDef>.AllDefsListForReading)
                {
                    if(stuff==null || !stuff.IsStuff || stuff.category!=ThingCategory.Item || stuff.stackLimit<=0 ||
                       stuff.stuffProps?.categories==null || stuff.MadeFromStuff)continue;
                    if(!stuff.stuffProps.categories.Exists(c=>wall.stuffCategories.Contains(c)))continue;
                    try
                    {
                        // Native adjusted recipe includes modded material volume,
                        // fixed ingredients and any material-specific cost changes.
                        float cost=0f,weight=0f;int units=0;bool valid=true;
                        foreach(ThingDefCountClass item in wall.CostListAdjusted(stuff,errorOnNullStuff:false))
                        {
                            if(item?.thingDef==null || item.count<=0 || item.thingDef.stackLimit<=0 || item.thingDef.MadeFromStuff)
                            {valid=false;break;}
                            float price=item.thingDef.BaseMarketValue,mass=item.thingDef.GetStatValueAbstract(StatDefOf.Mass);
                            if(!Finite(price) || !Finite(mass)){valid=false;break;}
                            cost+=count*item.count*price;weight+=count*item.count*mass;
                            if(item.thingDef==stuff)units+=item.count;
                        }
                        if(!valid || units<=0 || !Finite(cost) || !Finite(weight) || weight>maxMass)continue;
                        float hp=wall.GetStatValueAbstract(StatDefOf.MaxHitPoints,stuff);
                        float work=wall.GetStatValueAbstract(StatDefOf.WorkToBuild,stuff);
                        if(!Finite(hp) || !Finite(work))continue;
                        if(cost<cheapCost){cheapest=stuff;cheapCost=cost;cheapUnits=units;}
                        if(cost>discretionary)continue;
                        // Strong walls, fast deployment, low carried mass and reasonable price.
                        float score=hp/(Mathf.Pow(Math.Max(1f,work),0.35f)*Mathf.Pow(Math.Max(1f,cost),0.25f)*Mathf.Pow(Math.Max(1f,weight),0.15f));
                        if(score>bestScore){best=stuff;bestCost=cost;bestUnits=units;bestScore=score;}
                    }
                    catch(Exception){continue;} // a malformed optional mod def is not a mission failure
                }
                // The existing explicit minimum-mission top-up may fund the
                // cheapest viable pair; never create unpaid materials.
                if(best==null && count==2){best=cheapest;bestCost=cheapCost;bestUnits=cheapUnits;}
                if(best==null)continue;
                op.SealStuffR052=best;op.SealCountR052=count;op.SealUnitsR052=bestUnits;op.SealCostR052=bestCost;
                return true;
            }
            return false;
        }
    }
}
