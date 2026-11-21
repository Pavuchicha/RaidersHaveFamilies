// RimWorld 1.7 Harmony 3 event-driven hooks. No patching game tick, combat, faction generation.
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace RaidersHaveFamilies
{
    [StaticConstructorOnStartup]
    public static class RHFPatchInit
    {
        static RHFPatchInit()
        {
            try
            {
                var harmony = new Harmony("pavuk.raidshavefamilies");
                harmony.PatchAll(typeof(RHFPatchInit).Assembly);
                RHFWarCrimesIntegration.Patch(harmony);
                Log.Message("[Raiders Have Families] 0.3.0 event-driven Harmony integrations active.");
            }
            catch (Exception e) { Log.Error("[Raiders Have Families] Could not initialize Harmony: " + e); }
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.SpawnSetup))]
    public static class RHFSpawnEvent
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn __instance, Map map, bool respawningAfterLoad)
        {
            if (respawningAfterLoad || map == null || RHFMod.Settings?.Enabled != true ||
                __instance?.Faction == null) return;
            map.GetComponent<RHFMapComponent>()?.Enqueue(__instance);
            map.GetComponent<RHFOperationController>()?.ObserveSpawn(__instance);
        }
    }

    // Arrest is recorded immediately, not discovered through a high-frequency scan.
    [HarmonyPatch(typeof(Pawn_GuestTracker), nameof(Pawn_GuestTracker.SetGuestStatus))]
    public static class RHFCaptureEvent
    {
        private static readonly FieldInfo PawnField = AccessTools.Field(typeof(Pawn_GuestTracker), "pawn");
        [HarmonyPostfix]
        public static void Postfix(Pawn_GuestTracker __instance, Faction newHost, bool prisoner)
        {
            if (RHFMod.Settings?.Enabled != true || !prisoner || newHost != Faction.OfPlayer) return;
            Pawn p = PawnField?.GetValue(__instance) as Pawn;
            if (p != null && p.IsPrisonerOfColony)
                Current.Game?.GetComponent<RHFGameLedger>()?.TrackHostage(p);
        }
    }

    [HarmonyPatch(typeof(Building_CommsConsole), "GetFloatMenuOptions")]
    public static class RHFCommsEvent
    {
        [HarmonyPostfix]
        public static void Postfix(Building_CommsConsole __instance, Pawn myPawn,
            ref IEnumerable<FloatMenuOption> __result)
        {
            if (RHFMod.Settings?.Enabled != true || RHFMod.Settings.Negotiations != true ||
                __instance?.Map == null || !__instance.CanUseCommsNow ||
                myPawn == null || !myPawn.CanReach(__instance, PathEndMode.InteractionCell,
                    Danger.Some)) return;
            IEnumerable<FloatMenuOption> original = __result;
            Map colony = __instance.Map;
            __result = Append(original, colony);
        }

        private static IEnumerable<FloatMenuOption> Append(IEnumerable<FloatMenuOption> original, Map colony)
        {
            if (original != null)
                foreach (FloatMenuOption o in original) yield return o;
            yield return new FloatMenuOption("Связаться с родственниками и фракциями пленников…",
                () => RHFHostageDiplomacy.OpenForColony(colony));
        }
    }

    public struct RHFExitState
    {
        public Map Map;
        public bool Released;
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class RHFHostageDeathEvent
    {
        [HarmonyPrefix]
        public static void Prefix(Pawn __instance, DamageInfo? dinfo,
            out RHFHostageRecord __state)
        {
            __state = null;
            if (RHFMod.Settings?.Enabled != true || __instance == null ||
                !__instance.IsPrisonerOfColony) return;
            __state = Current.Game?.GetComponent<RHFGameLedger>()?.FindHostage(__instance);
        }
        [HarmonyPostfix]
        public static void Postfix(Pawn __instance, DamageInfo? dinfo, RHFHostageRecord __state)
        {
            if (__state == null || !__instance.Dead) return;
            bool playerDamage = dinfo.HasValue && dinfo.Value.Instigator != null &&
                dinfo.Value.Instigator.Faction == Faction.OfPlayer;
            Pawn actor=dinfo.HasValue ? dinfo.Value.Instigator as Pawn : null;
            string job=actor?.CurJobDef?.defName ?? "";
            bool executed=playerDamage && job.IndexOf("Execute",StringComparison.OrdinalIgnoreCase)>=0;
            RHFHostageDiplomacy.OnHostageDeath(__instance, playerDamage, executed);
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.ExitMap))]
    public static class RHFExitEvent
    {
        [HarmonyPrefix]
        public static void Prefix(Pawn __instance, out RHFExitState __state)
        {
            __state = new RHFExitState
            {
                Map = __instance?.Map,
                Released = __instance?.guest != null && __instance.guest.Released
            };
        }

        [HarmonyPostfix]
        public static void Postfix(Pawn __instance, RHFExitState __state)
        {
            if (RHFMod.Settings?.Enabled != true || !__state.Released ||
                __instance == null || __instance.Map != null) return;
            RHFHostageDiplomacy.OnPrisonerExit(__instance, __state.Map, true);
        }
    }

    // Optional deep integration: patch only workers attached to WCE2 recipe defs.
    // No compile-time dependence on the other mod and no blanket medical tick hooks.
    public static class RHFWarCrimesIntegration
    {
        private static readonly HashSet<MethodBase> patched = new HashSet<MethodBase>();

        public static void Patch(Harmony harmony)
        {
            if (RHFMod.Settings?.WCE2Events == false) return;
            bool installed = false;
            foreach (ModContentPack mod in LoadedModManager.RunningModsListForReading)
                if (string.Equals(mod.PackageId, "mersid.wce2updated.core", StringComparison.OrdinalIgnoreCase))
                { installed = true; break; }
            if (!installed) return;
            int count = 0;
            MethodInfo postfix = AccessTools.Method(typeof(RHFWarCrimesIntegration), nameof(OnSurgery));
            foreach (RecipeDef recipe in DefDatabase<RecipeDef>.AllDefsListForReading)
            {
                if (recipe?.workerClass == null || recipe.defName == null ||
                    !RHFWCERegistry.Known(recipe.defName)) continue;
                MethodInfo target = AccessTools.Method(recipe.workerClass, "ApplyOnPawn");
                if (target == null || !patched.Add(target)) continue;
                try {
                    harmony.Patch(target,
                        prefix: new HarmonyMethod(AccessTools.Method(typeof(RHFWarCrimesIntegration), nameof(BeforeSurgery))),
                        postfix: new HarmonyMethod(postfix));
                    count++;
                }
                catch (Exception e) { Log.Warning("[Raiders Have Families] Optional WCE2 worker skipped: " + e.Message); }
            }
            Log.Message("[Raiders Have Families] WCE2 surgery workers patched: " + count);
        }

        // Prefix captures the original prisoner record even for operations that kill
        // their patient before ApplyOnPawn returns (vivisection or organ removal).
        public static void BeforeSurgery(RecipeWorker __instance, Pawn pawn, Pawn billDoer,
            out RHFHostageRecord __state)
        {
            __state=null;
            if (pawn != null && billDoer?.Faction == Faction.OfPlayer && pawn.IsPrisonerOfColony &&
                RHFWCERegistry.SeverityOf(__instance?.recipe?.defName)>0)
                __state=Current.Game?.GetComponent<RHFGameLedger>()?.TrackHostage(pawn);
        }

        // Called after a successful WCE2 surgery. Routine removals are excluded.
        public static void OnSurgery(RecipeWorker __instance, Pawn pawn, Pawn billDoer,
            RHFHostageRecord __state)
        {
            if (__state==null || !RHFWCERegistry.Known(__instance?.recipe?.defName)) return;
            RHFHostageDiplomacy.RecordWceOperation(pawn, billDoer,
                __instance.recipe.defName, __state);
        }
    }
}
