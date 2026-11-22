// RHF S2.1: optional exit JobGiver guard.  RimWorld 1.7 may no longer
// declare TryGiveJob on either named ExitMapBest/Panic class.  NEVER allow
// an unavailable optional hook to abort the mod's critical Harmony patches.
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace RaidersHaveFamilies
{
    internal static class RHFControlledFreedExitJobV131
    {
        internal static int Install(Harmony harmony)
        {
            int installed=0;
            HashSet<MethodBase> seen=new HashSet<MethodBase>();
            MethodInfo prefix=typeof(RHFControlledFreedExitJobV131).GetMethod(
                nameof(Prefix),BindingFlags.Static|BindingFlags.NonPublic);
            if(harmony==null || prefix==null)return 0;
            foreach(string name in new [] {
                "Verse.AI.JobGiver_ExitMapBest",
                "Verse.AI.JobGiver_ExitMapPanic",
                "Verse.AI.JobGiver_ExitMap",
                "RimWorld.JobGiver_ExitMapBest",
                "RimWorld.JobGiver_ExitMapPanic",
                "RimWorld.JobGiver_ExitMap" })
            {
                Type targetType=AccessTools.TypeByName(name);
                if(targetType==null)continue;
                // Do not patch shared ThinkNode_JobGiver or other universal
                // base methods: such a patch would hijack unrelated jobs.
                MethodInfo target=null;
                for(Type current=targetType;
                    current!=null && current.Name.IndexOf("ExitMap",StringComparison.OrdinalIgnoreCase)>=0;
                    current=current.BaseType)
                {
                    foreach(MethodInfo method in current.GetMethods(
                        BindingFlags.Instance|BindingFlags.Public|
                        BindingFlags.NonPublic|BindingFlags.DeclaredOnly))
                    {
                        ParameterInfo[] parameters=method.GetParameters();
                        if(method.Name=="TryGiveJob" && !method.IsAbstract &&
                           method.ReturnType==typeof(Job) && parameters.Length==1 &&
                           parameters[0].ParameterType==typeof(Pawn))
                        {target=method;break;}
                    }
                    if(target!=null)break;
                }
                if(target==null || !seen.Add(target))continue;
                try
                {
                    harmony.Patch(target,prefix:new HarmonyMethod(prefix));
                    installed++;
                    Log.Message("[RHF][HarmonyS21] Optional exit job guard installed: "+
                        target.DeclaringType?.FullName+"."+target.Name);
                }
                catch(Exception e)
                {Log.Warning("[RHF][HarmonyS21] Optional exit guard skipped ("+
                    name+"): "+e.GetBaseException().Message);}
            }
            if(installed==0)
                Log.Warning("[RHF][HarmonyS21] ExitMap job-giver hook unavailable; "+
                    "rescue mission control remains active, monitor released pawn exit jobs.");
            return installed;
        }

        // Positional Harmony argument, independent of vanilla parameter name.
        private static bool Prefix(Pawn __0,ref Job __result)
        {
            if(!RHFRescueCommandV124.IsControlledFreed(__0))return true;
            __result=null;
            return false;
        }
    }
}
