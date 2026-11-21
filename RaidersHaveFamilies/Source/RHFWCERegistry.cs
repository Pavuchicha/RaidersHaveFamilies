// Registry derived from user's exact War Crimes Expanded 2 Core (Updated) 1.6 XML.
// It is optional and does not require linking or redistributing the WCE2 DLL.
using System;
using System.Collections.Generic;
namespace RaidersHaveFamilies
{
    public static class RHFWCERegistry
    {
        private static readonly Dictionary<string,int> severity = new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase)
        {
            {"WCE2_BashTeeth", 6},
            {"WCE2_BrandSlave", 5},
            {"WCE2_BreakJaw", 6},
            {"WCE2_CutEarOff", 6},
            {"WCE2_CutNoseOff", 7},
            {"WCE2_CutOpen", 4},
            {"WCE2_Elbowcap", 6},
            {"WCE2_GougeEye", 8},
            {"WCE2_HackOffLimb", 8},
            {"WCE2_HarvestMeatyGrowth", 0},
            {"WCE2_HarvestNeutroamineGrowth", 0},
            {"WCE2_ImplantSteelSpikes", 6},
            {"WCE2_InduceComa", 7},
            {"WCE2_InjureSpine", 8},
            {"WCE2_InstallAggressionAmplifier", 5},
            {"WCE2_InstallCoagulantSuppressor", 5},
            {"WCE2_InstallEMPKillswitch", 5},
            {"WCE2_InstallEMPRelay", 5},
            {"WCE2_InstallEmotionDestabilizer", 5},
            {"WCE2_InstallGrowthStimulator", 5},
            {"WCE2_InstallHealingInhibitor", 5},
            {"WCE2_InstallHeartIrregulator", 5},
            {"WCE2_InstallMemoryExpulsor", 5},
            {"WCE2_InstallNeutroamineIncubator", 5},
            {"WCE2_InstallPainAmplifier", 5},
            {"WCE2_InstallPanicInducer", 5},
            {"WCE2_InstallSleepDeprivator", 5},
            {"WCE2_InstallStomachClamp", 5},
            {"WCE2_Kneecap", 6},
            {"WCE2_Lobotomize", 9},
            {"WCE2_MangleFoot", 6},
            {"WCE2_MangleHand", 6},
            {"WCE2_MangleTongue", 6},
            {"WCE2_MutilateFace", 8},
            {"WCE2_PunctureEar", 6},
            {"WCE2_PunctureEye", 6},
            {"WCE2_RemoveAggressionAmplifier", 0},
            {"WCE2_RemoveBrand", 0},
            {"WCE2_RemoveCoagulantSuppressor", 0},
            {"WCE2_RemoveEMPKillswitch", 0},
            {"WCE2_RemoveEMPRelay", 0},
            {"WCE2_RemoveEmotionDestabilizer", 0},
            {"WCE2_RemoveHealingInhibitor", 0},
            {"WCE2_RemoveHeartIrregulator", 0},
            {"WCE2_RemoveMemoryExpulsor", 0},
            {"WCE2_RemovePainAmplifier", 0},
            {"WCE2_RemovePanicInducer", 0},
            {"WCE2_RemoveSleepDeprivator", 0},
            {"WCE2_RemoveSteelSpikes", 0},
            {"WCE2_RemoveStomachClamp", 0},
            {"WCE2_RemoveSutures", 0},
            {"WCE2_RemoveSuturesEye", 0},
            {"WCE2_RemoveVivisection", 0},
            {"WCE2_RipOutHeart", 12},
            {"WCE2_RipOutLiver", 11},
            {"WCE2_SutureEye", 5},
            {"WCE2_SutureMouth", 5},
            {"WCE2_TraumaSavant", 8},
            {"WCE2_Vivisect", 10},
            {"WCE2_WakeFromComa", 0},
        };
        // Zero means restorative/harvesting rather than deliberate mutilation.
        public static int SeverityOf(string defName)
        {
            if (string.IsNullOrEmpty(defName)) return 0;
            int value;
            return severity.TryGetValue(defName,out value) ? value : 0;
        }
        public static bool Known(string defName)
        {
            return !string.IsNullOrEmpty(defName) && severity.ContainsKey(defName);
        }
    }
}
