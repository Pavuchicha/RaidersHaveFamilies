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
    public static class RHFOrdinarySaleHook
    {
        private static readonly FieldInfo PawnField = AccessTools.Field(typeof(Pawn_RelationsTracker), "pawn");
        public static void Postfix(Pawn_RelationsTracker __instance)
        {
            Pawn pawn = PawnField?.GetValue(__instance) as Pawn;
            if (pawn != null) RHFMercy.OnOrdinarySale(pawn);
        }
    }

    [StaticConstructorOnStartup]
    public static class RHFPatchInit
    {
        static RHFPatchInit()
        {
            var harmony = new Harmony("pavuk.raidshavefamilies");
            int installed=0,failed=0;
            // A single obsolete [HarmonyPatch] must never disable SpawnSetup,
            // coalition hostility, raid letters, prisoner exits and every other
            // unrelated integration. Patch each decorated class independently.
            Type[] patchTypes;
            try { patchTypes=typeof(RHFPatchInit).Assembly.GetTypes(); }
            catch(ReflectionTypeLoadException e)
            {
                patchTypes=e.Types;
                Log.Error("[RHF][HarmonyS21] Partial type load: " + e);
            }
            foreach(Type type in patchTypes)
            {
                if(type==null || !Attribute.IsDefined(type,typeof(HarmonyPatch),false))continue;
                try
                {
                    harmony.CreateClassProcessor(type).Patch();
                    installed++;
                }
                catch(Exception e)
                {
                    failed++;
                    Log.Error("[RHF][HarmonyS21] FAILED class " + type.FullName +
                        ": " + e.GetBaseException());
                }
            }
            try { RHFControlledFreedExitJobV131.Install(harmony); }
            catch(Exception e) { Log.Warning("[RHF][HarmonyS21] Optional exit hook: "+e); }
            try { RHFWarCrimesIntegration.Patch(harmony); }
            catch(Exception e) { Log.Error("[RHF][HarmonyS21] WCE2 integration failed: "+e); }
            // Optional vanilla sale hook is also isolated.
            try
            {
                MethodInfo sold=AccessTools.Method(typeof(Pawn_RelationsTracker),"Notify_PawnSold");
                MethodInfo postfix=AccessTools.Method(typeof(RHFOrdinarySaleHook),
                    nameof(RHFOrdinarySaleHook.Postfix));
                if(sold!=null && postfix!=null)
                    harmony.Patch(sold,postfix:new HarmonyMethod(postfix));
            }
            catch(Exception e) { Log.Warning("[RHF][HarmonyS21] Optional ordinary sale hook: "+e.Message); }
            Log.Message("[RHF][HarmonyS21] Class patches installed="+installed+
                ", failed="+failed+". Each failure is isolated and logged by class.");
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.SpawnSetup))]
    public static class RHFSpawnEvent
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn __instance, Map map, bool respawningAfterLoad)
        {
            if (respawningAfterLoad || map == null || __instance?.Faction == null) return;
            // Debug-forced rescue raids also need squad registration when regular
            // family generation is disabled in mod settings.
            map.GetComponent<RHFOperationController>()?.ObserveSpawn(__instance);
            if (RHFMod.Settings?.Enabled == true)
                map.GetComponent<RHFMapComponent>()?.Enqueue(__instance);
        }
    }

    // Arrest is recorded immediately, not discovered through a high-frequency scan.
    [HarmonyPatch(typeof(Pawn_GuestTracker), nameof(Pawn_GuestTracker.SetGuestStatus))]
    public static class RHFCaptureEvent
    {
        private static readonly FieldInfo PawnField = AccessTools.Field(typeof(Pawn_GuestTracker), "pawn");
        [HarmonyPostfix]
        public static void Postfix(Pawn_GuestTracker __instance)
        {
            // In 1.7 the second SetGuestStatus parameter is a GuestStatus enum,
            // not the pre-1.7 bool. Inspect the final state instead of signature.
            if (RHFMod.Settings?.Enabled != true) return;
            Pawn p = PawnField?.GetValue(__instance) as Pawn;
            if (p != null && p.IsPrisonerOfColony)
                Current.Game?.GetComponent<RHFGameLedger>()?.TrackHostage(p);
        }
    }

    // The VANILLA radio job is intentionally left intact. Faction.TryOpenComms
    // opens its normal Dialog_Negotiation only after the pawn reaches the console.
    [HarmonyPatch(typeof(FactionDialogMaker), nameof(FactionDialogMaker.FactionDialogFor))]
    public static class RHFFactionConversation
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn negotiator, Faction faction, ref DiaNode __result)
        {
            if (RHFMod.Settings?.Enabled != true || RHFMod.Settings.Negotiations != true ||
                negotiator == null || negotiator.Map == null || faction == null ||
                __result == null || !RHFDialogue.HasCaptives(faction)) return;
            var option = new DiaOption("Обсудить возвращение наших пленников…")
            { linkLateBind = () => RHFDialogue.Captives(negotiator, faction) };
            int index = Math.Max(0, __result.options.Count - 1);
            __result.options.Insert(index, option);
        }
    }

    public struct RHFExitState
    {
        public Map Map;
        public bool Released;
        public IntVec3 ExitCell;
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
                !(__instance.IsPrisonerOfColony || __instance.IsSlaveOfColony)) return;
            __state = Current.Game?.GetComponent<RHFGameLedger>()?.FindHostage(__instance);
            if (__state != null && __instance.Spawned)
                __state.LastHeldCell = __instance.Position;
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

    // Executed only for actual physical corpse-related events; no periodic scan.
    [HarmonyPatch(typeof(Corpse), nameof(Corpse.ButcherProducts))]
    public static class RHFDeadButcheredEvent
    {
        [HarmonyPrefix]
        public static void Prefix(Corpse __instance, Pawn butcher)
        { RHFHostageDiplomacy.RecordCorpseButchered(__instance, butcher); }
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.Ingested))]
    public static class RHFDeadConsumedEvent
    {
        [HarmonyPrefix]
        public static void Prefix(Thing __instance, Pawn ingester)
        {
            if (__instance is Corpse corpse)
                RHFHostageDiplomacy.RecordCorpseConsumed(corpse, ingester);
            else
                RHFHostageDiplomacy.RecordHumanMeatConsumption(__instance, ingester);
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.ExitMap))]
    public static class RHFExitEvent
    {
        [HarmonyPrefix]
        public static bool Prefix(Pawn __instance, out RHFExitState __state)
        {
            __state = new RHFExitState
            {
                Map = __instance?.Map,
                ExitCell = __instance?.Position ?? IntVec3.Invalid,
                Released = __instance?.guest != null && __instance.guest.Released
            };
            // Filter by explicit mission membership and actual rescuer contact.
            return RHFRescuePlannerS41.GuardExit(__instance);
        }

        [HarmonyPostfix]
        public static void Postfix(Pawn __instance, RHFExitState __state)
        {
            if (RHFMod.Settings?.Enabled != true || !__state.Released ||
                __instance == null || __instance.Map != null) return;
            RHFHostageDiplomacy.OnPrisonerExit(__instance, __state.Map, true, __state.ExitCell);
        }
    }

    // RimWorld 1.7 medical-change fallback: invoked on actual Hediff additions,
    // not on every health tick and not on all damage. Other mods' surgical
    // workers can bypass the expected RecipeWorker.ApplyOnPawn implementation.
    [HarmonyPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.AddHediff),
        new Type[] { typeof(Hediff), typeof(BodyPartRecord), typeof(DamageInfo?),
            typeof(DamageWorker.DamageResult) })]
    public static class RHFMedicalMissingPartEvent
    {
        private static readonly FieldInfo PawnField = AccessTools.Field(typeof(Pawn_HealthTracker), "pawn");
        [HarmonyPostfix]
        public static void Postfix(Pawn_HealthTracker __instance, Hediff hediff, DamageInfo? dinfo)
        {
            if (!(hediff is Hediff_MissingPart) || dinfo.HasValue) return;
            Pawn victim = PawnField?.GetValue(__instance) as Pawn;
            RHFHostageDiplomacy.RecordMedicalMissingPart(victim, hediff, dinfo);
        }
    }

    // Surgical events: one Harmony patch for each distinct applicable recipe worker
    // method at startup. The postfix is ONLY invoked when the game executes a surgery.
    // It never scans the map, pawns, health trackers, or hediffs per tick.
    public static class RHFWarCrimesIntegration
    {
        private static readonly HashSet<MethodBase> patched = new HashSet<MethodBase>();
        public static int PatchedWorkers;
        public static int WceRecipesDetected;
        public static int VanillaRecipesDetected;

        // Vanilla organ harvesting may use the generic RemoveBodyPart worker;
        // only violations that REMOVE organs/parts are classified here.
        public static int SeverityOf(RecipeDef recipe)
        {
            if (recipe == null) return 0;
            if (RHFWCERegistry.Known(recipe.defName))
                return RHFMod.Settings?.WCE2Events == true ? RHFWCERegistry.SeverityOf(recipe.defName) : 0;
            if (!recipe.isViolation) return 0;
            string n = (recipe.defName ?? "").ToLowerInvariant();
            string label = (recipe.label ?? "").ToLowerInvariant();
            string worker = (recipe.workerClass?.Name ?? "").ToLowerInvariant();
            bool organ = n.Contains("kidney") || n.Contains("lung") || n.Contains("liver") ||
                n.Contains("heart") || n.Contains("stomach") || n.Contains("organ") ||
                label.Contains("kidney") || label.Contains("lung") || label.Contains("liver") ||
                label.Contains("heart") || label.Contains("орган") || label.Contains("почку") ||
                label.Contains("лёгк") || label.Contains("печень") || label.Contains("сердце");
            bool removal = n.Contains("harvest") || n.Contains("extract") ||
                n.Contains("remove") || label.Contains("harvest") ||
                label.Contains("извлеч") || label.Contains("удалить") ||
                worker.Contains("removebodypart");
            return removal && organ ? 8 : (worker.Contains("removebodypart") && removal ? 4 : 0);
        }

        public static void Patch(Harmony harmony)
        {
            int workers = 0;
            WceRecipesDetected = 0; VanillaRecipesDetected = 0;
            MethodInfo postfix = AccessTools.Method(typeof(RHFWarCrimesIntegration), nameof(OnSurgery));
            MethodInfo prefix = AccessTools.Method(typeof(RHFWarCrimesIntegration), nameof(BeforeSurgery));
            foreach (RecipeDef recipe in DefDatabase<RecipeDef>.AllDefsListForReading)
            {
                if (recipe?.workerClass == null || SeverityOf(recipe) <= 0) continue;
                if (RHFWCERegistry.Known(recipe.defName)) WceRecipesDetected++;
                else VanillaRecipesDetected++;
                MethodInfo target = AccessTools.Method(recipe.workerClass, "ApplyOnPawn", new Type[] {
                    typeof(Pawn), typeof(BodyPartRecord), typeof(Pawn),
                    typeof(List<Thing>), typeof(Bill) });
                if (target == null || !patched.Add(target)) continue;
                try
                {
                    harmony.Patch(target, prefix: new HarmonyMethod(prefix), postfix: new HarmonyMethod(postfix));
                    workers++;
                }
                catch (Exception e)
                {
                    patched.Remove(target);
                    Log.Warning("[RHF] Medical patch skipped: " + e.Message);
                }
            }
            PatchedWorkers = workers;
            Log.Message("[RHF] Surgical hooks: " + workers +
                ", harmful WCE2 recipes: " + WceRecipesDetected +
                ", vanilla organ/violation recipes: " + VanillaRecipesDetected);
        }

        public static void BeforeSurgery(RecipeWorker __instance, Pawn __0, Pawn __2,
            out RHFHostageRecord __state)
        {
            Pawn pawn=__0, billDoer=__2;
            __state = null;
            if (pawn == null || billDoer?.Faction != Faction.OfPlayer ||
                !pawn.IsPrisonerOfColony || SeverityOf(__instance?.recipe) <= 0) return;
            __state = Current.Game?.GetComponent<RHFGameLedger>()?.TrackHostage(pawn);
        }

        public static void OnSurgery(RecipeWorker __instance, Pawn __0, Pawn __2,
            RHFHostageRecord __state)
        {
            Pawn pawn=__0, billDoer=__2;
            RecipeDef recipe = __instance?.recipe;
            int severity = SeverityOf(recipe);
            if (__state == null || severity <= 0) return;
            RHFHostageDiplomacy.RecordWceOperation(pawn, billDoer,
                recipe.defName, __state, severity, recipe.label);
        }
    }
}

namespace RaidersHaveFamilies
{
    // Inspection-only decoration: the original faction's pawn title/gear remain intact.
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetInspectString))]
    public static class RHFTacticalInspectEvent
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn __instance, ref string __result)
        {
            if (__instance?.Map == null || RHFMod.Settings?.TacticalCombat != true) return;
            RHFTacticalMember m = __instance.Map.GetComponent<RHFOperationController>()?
                .TacticalMember(__instance);
            if (m == null) return;
            __result += "\nRHF: " + RHFTactical.RoleName(m.Role) +
                (m.KitIssued > 0 ? "; реальный комплект взлома" : "") +
                (m.WeaponMissing ? "; нет основного оружия" : "");
        }
    }
}
