// Raiders Have Families v0.2.0 — RimWorld 1.7
// Event-driven spawn processing via Harmony: no repeated map-pawn discovery.
// Family generation is bounded per arrival batch; diplomatic checks run infrequently.
using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RaidersHaveFamilies
{
    public sealed class RHFSettings : ModSettings
    {
        // Existing Scribe keys retained: old 0.1.x settings and game saves remain readable.
        public bool Enabled = true;
        public int SettingsSchema = 2;
        public float ParentChance = 0.75f;
        public float PartnerChance = 0.32f;
        public float RaidmatePartnerChance = 0.58f;
        public float SharedParentChance = 0.68f;
        public int MaxPerWave = 6; // bounded generation attempts per arriving group
        public int MaxPerSave = 256;
        public bool VerboseLogging = false;
        public bool Negotiations = true;
        public bool Retaliation = true;
        public bool IncludeVisitors = true;
        public bool WCE2Events = true;
        public bool EnableRescueOperations = true;
        public bool FieldEvacPod = true;
        public float InterventionStrength = 1f;
        public float FactionRelativeChance = 0.45f;

        // Hard safety ceilings also apply to previously saved settings.
        public const int HardMaxPerCycle = 8;
        public const int HardMaxPerSave = 512;

        public void Clamp()
        {
            ParentChance = ClampProbability(ParentChance);
            PartnerChance = ClampProbability(PartnerChance);
            RaidmatePartnerChance = ClampProbability(RaidmatePartnerChance);
            SharedParentChance = ClampProbability(SharedParentChance);
            FactionRelativeChance = ClampProbability(FactionRelativeChance);
            InterventionStrength = float.IsNaN(InterventionStrength) ? 1f :
                Math.Max(.5f,Math.Min(2f,InterventionStrength));
            MaxPerWave = Math.Max(0, Math.Min(MaxPerWave, HardMaxPerCycle));
            MaxPerSave = Math.Max(0, Math.Min(MaxPerSave, HardMaxPerSave));
        }

        private static float ClampProbability(float value)
        {
            // NaN otherwise bypasses ordinary range checks and breaks chance tests.
            if (float.IsNaN(value)) return 0f;
            return Math.Max(0f, Math.Min(1f, value));
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref Enabled, "enabled", true);
            Scribe_Values.Look(ref SettingsSchema, "rhfSettingsSchema", 1);
            Scribe_Values.Look(ref ParentChance, "parentChance", 0.75f);
            Scribe_Values.Look(ref PartnerChance, "partnerChance", 0.32f);
            Scribe_Values.Look(ref RaidmatePartnerChance, "raidmatePartnerChance", 0.50f);
            Scribe_Values.Look(ref SharedParentChance, "sharedParentChance", 0.55f);
            Scribe_Values.Look(ref MaxPerWave, "maxPerWave", 4);
            Scribe_Values.Look(ref MaxPerSave, "maxPerSave", 256);
            Scribe_Values.Look(ref VerboseLogging, "verboseLogging", false);
            Scribe_Values.Look(ref Negotiations, "negotiations", true);
            Scribe_Values.Look(ref Retaliation, "retaliation", true);
            Scribe_Values.Look(ref IncludeVisitors, "includeVisitors", true);
            Scribe_Values.Look(ref WCE2Events, "wce2Events", true);
            Scribe_Values.Look(ref EnableRescueOperations, "enableRescueOperations", true);
            Scribe_Values.Look(ref FieldEvacPod, "enableFieldEvacPod", true);
            Scribe_Values.Look(ref InterventionStrength, "interventionStrength", 1f);
            Scribe_Values.Look(ref FactionRelativeChance, "factionRelativeChance", 0.45f);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (SettingsSchema < 2)
                {
                    // Legacy v0.1.3 limit was one/two per tick cycle, not per arrival.
                    // Zero is a deliberate TPS setting and is preserved.
                    if (MaxPerWave == 1 || MaxPerWave == 2) MaxPerWave = 4;
                    if (MaxPerSave == 32 && MaxPerWave > 0) MaxPerSave = 256;
                    SettingsSchema = 2;
                }
                Clamp();
            }
        }
    }

    public sealed class RHFMod : Mod
    {
        public static RHFSettings Settings;
        private Vector2 settingsScroll = Vector2.zero;
        public RHFMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<RHFSettings>();
            Settings.Clamp();
        }

        public override string SettingsCategory() { return "Raiders Have Families"; }

        public override void DoSettingsWindowContents(Rect rect)
        {
            Rect content = new Rect(0f,0f,rect.width-20f,900f);
            Widgets.BeginScrollView(rect,ref settingsScroll,content);
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(content);
            listing.CheckboxLabeled("RHF_Enable".Translate(), ref Settings.Enabled);
            listing.GapLine();
            listing.Label("RHF_Parents".Translate(Settings.ParentChance.ToString("P0")));
            Settings.ParentChance = listing.Slider(Settings.ParentChance, 0f, 1f);
            listing.Label("RHF_Partners".Translate(Settings.PartnerChance.ToString("P0")));
            Settings.PartnerChance = listing.Slider(Settings.PartnerChance, 0f, 1f);
            listing.Label("RHF_RaidPartners".Translate(Settings.RaidmatePartnerChance.ToString("P0")));
            Settings.RaidmatePartnerChance = listing.Slider(Settings.RaidmatePartnerChance, 0f, 1f);
            listing.Label("RHF_Siblings".Translate(Settings.SharedParentChance.ToString("P0")));
            Settings.SharedParentChance = listing.Slider(Settings.SharedParentChance, 0f, 1f);
            listing.GapLine();
            listing.Label("RHF_PerCycle".Translate(Settings.MaxPerWave));
            Settings.MaxPerWave = (int)listing.Slider(Settings.MaxPerWave, 0f, RHFSettings.HardMaxPerCycle);
            listing.Label("RHF_PerSave".Translate(Settings.MaxPerSave));
            Settings.MaxPerSave = (int)listing.Slider(Settings.MaxPerSave, 0f, RHFSettings.HardMaxPerSave);
            listing.Label("RHF_ZeroWorldPawns".Translate());
            listing.GapLine();
            listing.CheckboxLabeled("RHF_Visitors".Translate(), ref Settings.IncludeVisitors);
            listing.Label("RHF_FactionRelatives".Translate(Settings.FactionRelativeChance.ToString("P0")));
            Settings.FactionRelativeChance = listing.Slider(Settings.FactionRelativeChance, 0f, 1f);
            listing.CheckboxLabeled("RHF_Negotiations".Translate(), ref Settings.Negotiations);
            listing.CheckboxLabeled("RHF_Retaliation".Translate(), ref Settings.Retaliation);
            listing.CheckboxLabeled("RHF_WCE2".Translate(), ref Settings.WCE2Events);
            listing.GapLine();
            listing.CheckboxLabeled("Включить тактические спасательные операции (эксперимент)",
                ref Settings.EnableRescueOperations);
            listing.CheckboxLabeled("Полевые капсулы эвакуации VIP (эксперимент)",
                ref Settings.FieldEvacPod);
            listing.Label("Мощность интервенций: " + Settings.InterventionStrength.ToString("P0"));
            Settings.InterventionStrength=listing.Slider(Settings.InterventionStrength,.5f,2f);
            listing.CheckboxLabeled("RHF_Logging".Translate(), ref Settings.VerboseLogging);
            listing.End();
            Widgets.EndScrollView();
            Settings.Clamp();
        }
    }

    public sealed class RHFGameLedger : GameComponent
    {
        // Persistent number of generated off-map pawns. Retains v0.1.x save key.
        public int CreatedWorldPawns;
        public List<RHFHostageRecord> Hostages = new List<RHFHostageRecord>();
        public List<RHFManagedLink> ManagedLinks = new List<RHFManagedLink>();
        public List<RHFWorldKin> GeneratedKin = new List<RHFWorldKin>();
        public List<RHFFactionStanding> FactionStandings = new List<RHFFactionStanding>();
        public int ActiveGeneratedKin => GeneratedKin?.Count ?? 0;
        private int nextHostageCheck;
        public RHFGameLedger(Game game) { }
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref CreatedWorldPawns, "rhfWorldPawnsCreated", 0);
            Scribe_Collections.Look(ref Hostages, "rhfHostagesV2", LookMode.Deep);
            Scribe_Collections.Look(ref ManagedLinks, "rhfLinksV2", LookMode.Deep);
            Scribe_Collections.Look(ref GeneratedKin, "rhfGeneratedKinV2", LookMode.Deep);
            Scribe_Collections.Look(ref FactionStandings, "rhfStandingV3", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                CreatedWorldPawns = Math.Max(0, CreatedWorldPawns);
                if (Hostages == null) Hostages = new List<RHFHostageRecord>();
                if (ManagedLinks == null) ManagedLinks = new List<RHFManagedLink>();
                if (GeneratedKin == null) GeneratedKin = new List<RHFWorldKin>();
                if (FactionStandings == null) FactionStandings = new List<RHFFactionStanding>();
                nextHostageCheck = Find.TickManager.TicksGame + 30000;
            }
        }

        public void RegisterGeneratedKin(Pawn kin)
        {
            if (kin == null) return;
            GeneratedKin.Add(new RHFWorldKin(kin));
            CreatedWorldPawns++;
        }

        private void CleanupGeneratedKin()
        {
            // World-pawn removal ONLY for this mod's own generated kin.
            // Never destroy a pawn while it or any relative is in active play.
            for (int i = GeneratedKin.Count - 1; i >= 0; i--)
            {
                var entry = GeneratedKin[i];
                Pawn kin = entry?.Kin;
                if (kin == null) { GeneratedKin.RemoveAt(i); continue; }
                if (kin.Spawned || kin.IsColonist || kin.IsPrisonerOfColony || kin.IsSlaveOfColony)
                    continue;
                // Never remove a generated pawn engaged in caravans, settlements,
                // trade inventories, or quest references. Only an ordinary free
                // world pawn is a candidate for cleanup.
                if (Find.WorldPawns == null || !Find.WorldPawns.Contains(kin) ||
                    Find.WorldPawns.GetSituation(kin) != WorldPawnSituation.Free)
                    continue;
                bool activeRelative = false;
                if (kin.relations != null)
                    foreach (Pawn p in kin.relations.RelatedPawns)
                        if (p != null && (p.Spawned || p.IsColonist || p.IsPrisonerOfColony || p.IsSlaveOfColony ||
                            (Find.WorldPawns != null && Find.WorldPawns.Contains(p) &&
                             Find.WorldPawns.GetSituation(p) != WorldPawnSituation.Free)))
                        { activeRelative = true; break; }
                if (activeRelative) continue;
                // Clear both sides before asking the world manager to discard this
                // generated pawn. Avoid operations on relations owned by other mods.
                try
                {
                    if (kin.relations != null)
                    {
                        var connections = new List<DirectPawnRelation>(kin.relations.DirectRelations);
                        for (int j = 0; j < connections.Count; j++)
                        {
                            DirectPawnRelation rel = connections[j];
                            if (rel.otherPawn != null)
                                kin.relations.RemoveDirectRelation(rel.def, rel.otherPawn);
                        }
                    }
                    if (Find.WorldPawns != null && Find.WorldPawns.Contains(kin))
                        Find.WorldPawns.RemovePawn(kin);
                    GeneratedKin.RemoveAt(i);
                }
                catch (Exception e)
                {
                    if (RHFMod.Settings?.VerboseLogging == true)
                        Log.Warning("[Raiders Have Families] Kin cleanup was deferred: " + e);
                }
            }
        }

        public void RegisterTemporaryLink(Pawn a, Pawn b, PawnRelationDef relation)
        {
            if (a == null || b == null || relation == null || ManagedLinks.Count >= 2048) return;
            ManagedLinks.Add(new RHFManagedLink(a, b, relation));
        }

        private void CleanupDeparted()
        {
            // Remove only temporary romantic relationships created by this mod.
            // Real parents / genealogy and player-relevant relationships are retained.
            for (int i = ManagedLinks.Count - 1; i >= 0; i--)
            {
                var edge = ManagedLinks[i];
                if (edge == null || edge.A == null || edge.B == null) { ManagedLinks.RemoveAt(i); continue; }
                if (edge.A.Spawned || edge.B.Spawned || edge.A.IsColonist || edge.B.IsColonist ||
                    edge.A.IsPrisonerOfColony || edge.B.IsPrisonerOfColony) continue;
                try
                {
                    if (edge.A.relations?.DirectRelationExists(edge.Relation, edge.B) == true)
                        edge.A.relations.RemoveDirectRelation(edge.Relation, edge.B);
                }
                catch (Exception e) { if (RHFMod.Settings.VerboseLogging) Log.Warning("[RHF] Link cleanup: " + e.Message); }
                ManagedLinks.RemoveAt(i);
            }
        }

        public RHFHostageRecord FindHostage(Pawn pawn)
        {
            if (pawn == null) return null;
            for (int i = 0; i < Hostages.Count; i++)
                if (Hostages[i].Pawn == pawn) return Hostages[i];
            return null;
        }

        public RHFHostageRecord TrackHostage(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.Faction == null || pawn.Faction.IsPlayer ||
                pawn.guest == null || !pawn.IsPrisonerOfColony) return null;
            RHFHostageRecord existing = FindHostage(pawn);
            if (existing != null) return existing;
            var record = new RHFHostageRecord(pawn);
            Hostages.Add(record);
            return record;
        }

        public override void GameComponentTick()
        {
            if (RHFMod.Settings == null || !RHFMod.Settings.Enabled) return;
            int tick = Find.TickManager.TicksGame;
            if (tick < nextHostageCheck) return;
            nextHostageCheck = tick + 30000;
            // This is never a general pawn scan each tick. Only current colony prisoners,
            // once per half-day, are inspected for possible independent ransom offers.
            if (RHFMod.Settings.Negotiations)
            {
                for (int mi = 0; mi < Find.Maps.Count; mi++)
                {
                    Map m = Find.Maps[mi];
                    if (!m.IsPlayerHome) continue;
                    var all = m.mapPawns.PrisonersOfColonySpawned;
                    for (int pi = 0; pi < all.Count; pi++) TrackHostage(all[pi]);
                }
                RHFHostageDiplomacy.UpdateOffers(this, tick);
            }
            CleanupDeparted();
            CleanupGeneratedKin();
        }
    }

    // Arrival batches are filled by the Pawn.SpawnSetup Harmony postfix.
    // This component does NOT repeatedly scan all pawns on the map.
    public sealed class RHFMapComponent : MapComponent
    {
        private const int FirstScanDelay = 120;
        private const int MaxBatchSize = 192; // bounds raidmate sets
        private const int MaxPairProbes = 48; // bounds matching work per newcomer
        private const int MaxRememberedIds = 8192; // guards old/long-running saves
        private const int DropOldestCount = 4096;

        // No new collections are allocated on empty MapComponentTick passes.
        private int nextScanTick;
        private List<Pawn> pending = new List<Pawn>(48);
        private List<int> processedPawnIds = new List<int>();
        private HashSet<int> processed = new HashSet<int>();
        private static readonly HashSet<string> warnedFactionDefs = new HashSet<string>();

        public RHFMapComponent(Map map) : base(map) { }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref processedPawnIds, "rhfProcessedPawnIds", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (processedPawnIds == null) processedPawnIds = new List<int>();
                if (processedPawnIds.Count > MaxRememberedIds)
                    processedPawnIds.RemoveRange(0, processedPawnIds.Count - MaxRememberedIds);
                processed = new HashSet<int>(processedPawnIds);
                // Runtime scheduling is not serialized; replaying an old save
                // does not generate another family for a processed pawn.
                nextScanTick = 0;
                pending = new List<Pawn>(48);
            }
        }

        // Called only when a humanlike pawn spawns, including from modded arrival methods.
        public void Enqueue(Pawn pawn)
        {
            if (!Eligible(pawn) || processed.Contains(pawn.thingIDNumber)) return;
            if (pending.Count >= MaxBatchSize) return;
            Remember(pawn.thingIDNumber);
            pending.Add(pawn);
            if (nextScanTick == 0) nextScanTick = Find.TickManager.TicksGame + FirstScanDelay;
        }

        public override void MapComponentTick()
        {
            RHFSettings cfg = RHFMod.Settings;
            if (cfg == null || !cfg.Enabled) return;

            if (pending.Count == 0) return;
            int now = Find.TickManager.TicksGame;
            if (now < nextScanTick) return;
            nextScanTick = 0;
            // Entire group is processed once, shortly after the arrival finishes.
            List<Pawn> batch = pending;
            pending = new List<Pawn>(48);
            batch.RemoveAll(p => p == null || !p.Spawned || p.Map != map || p.Dead);
            if (batch.Count == 0) return;
            try { Enrich(batch); }
            catch (Exception e) { Log.Warning("[Raiders Have Families] Arrival enrichment failed: " + e); }
        }

        private void Remember(int id)
        {
            if (!processed.Add(id)) return;
            processedPawnIds.Add(id);
            if (processedPawnIds.Count <= MaxRememberedIds) return;
            for (int i = 0; i < DropOldestCount; i++)
                processed.Remove(processedPawnIds[i]);
            processedPawnIds.RemoveRange(0, DropOldestCount);
        }

        private static bool Eligible(Pawn p)
        {
            // All humanlike races receive same-group links; off-map creation stays vanilla-human only.
            return p != null && !p.Dead && !p.Destroyed && p.Spawned &&
                p.RaceProps.Humanlike && p.relations != null &&
                p.ageTracker != null && p.gender != Gender.None &&
                Age(p) >= 16f && p.Faction != null &&
                !p.Faction.IsPlayer &&
                (RHFMod.Settings.IncludeVisitors || p.Faction.HostileTo(Faction.OfPlayer));
        }

        private static void Enrich(List<Pawn> newcomers)
        {
            RHFSettings cfg = RHFMod.Settings;
            if (cfg == null || !cfg.Enabled) return;
            RHFGameLedger ledger = Current.Game?.GetComponent<RHFGameLedger>();
            if (ledger == null) return;

            int created = 0;
            int generationAttempts = 0; // hard bound even when pawn generation fails
            int links = 0;
            // All pair matching runs once per batch, not on repeated ticks.
            HashSet<Pawn> paired = new HashSet<Pawn>();
            for (int i = 0; i < newcomers.Count; i++)
            {
                Pawn p = newcomers[i];
                if (paired.Contains(p) || !CanLove(p) ||
                    !Rand.Chance(cfg.RaidmatePartnerChance)) continue;
                int probed = 0;
                for (int j = i + 1; j < newcomers.Count && probed < MaxPairProbes; j++, probed++)
                {
                    Pawn q = newcomers[j];
                    if (paired.Contains(q) || p.Faction != q.Faction ||
                        p.def != q.def ||
                        !CanLove(q) || !RomanceGenderCompatible(p, q) ||
                        Math.Abs(Age(p) - Age(q)) > 16f ||
                        HaveAnyRelation(p, q)) continue;
                    float loveRoll = Rand.Value;
                    PawnRelationDef love = loveRoll < .30f ? PawnRelationDefOf.Spouse :
                        (loveRoll < .43f ? PawnRelationDefOf.Fiance : PawnRelationDefOf.Lover);
                    p.relations.AddDirectRelation(love, q);
                    ledger.RegisterTemporaryLink(p, q, love);
                    paired.Add(p);
                    paired.Add(q);
                    links++;
                    break;
                }
            }

            links += LinkGroupFamily(newcomers, ledger);
            // Reuse our existing retained relatives before generating another world pawn.
            links += ReuseExistingKin(newcomers, ledger, cfg);

            // Two group members may share one real off-map parent.
            HashSet<Pawn> grouped = new HashSet<Pawn>();
            for (int i = 0; i < newcomers.Count && CanCreate(ledger, cfg, created, generationAttempts); i++)
            {
                Pawn p = newcomers[i];
                if (p.def != ThingDefOf.Human || grouped.Contains(p) || !HasMissingParent(p) ||
                    !Rand.Chance(cfg.SharedParentChance)) continue;
                int probed = 0;
                for (int j = i + 1; j < newcomers.Count && probed < MaxPairProbes; j++, probed++)
                {
                    Pawn q = newcomers[j];
                    if (grouped.Contains(q) || p.Faction != q.Faction ||
                        p.def != q.def ||
                        !HasMissingParent(q) || Math.Abs(Age(p) - Age(q)) > 13f ||
                        HaveAnyRelation(p, q)) continue;

                    Gender g = MissingParentGenderForBoth(p, q);
                    if (g == Gender.None) continue;
                    generationAttempts++;
                    Pawn parent = NewWorldPawn(p, g,
                        Math.Max(Age(p), Age(q)) + Rand.Range(23f, 37f), true, true);
                    if (parent == null) break;
                    ledger.RegisterGeneratedKin(parent);
                    created++;
                    if (g == Gender.Male)
                    {
                        p.SetFather(parent);
                        q.SetFather(parent);
                    }
                    else
                    {
                        p.SetMother(parent);
                        q.SetMother(parent);
                    }
                    grouped.Add(p);
                    grouped.Add(q);
                    links += 2;
                    break;
                }
            }

            // At most 4 (8 if configured) off-map pawn-generation attempts per arrival.
            // The total generated across this save is also capped.
            for (int i = 0; i < newcomers.Count && CanCreate(ledger, cfg, created, generationAttempts); i++)
            {
                Pawn p = newcomers[i];
                if (p.def == ThingDefOf.Human && Age(p) < 70f && HasMissingParent(p) && Rand.Chance(cfg.ParentChance))
                {
                    Gender g = MissingParentGender(p);
                    generationAttempts++;
                    Pawn parent = NewWorldPawn(p, g, Age(p) + Rand.Range(22f, 36f), true, true);
                    if (parent != null)
                    {
                        ledger.RegisterGeneratedKin(parent);
                        created++;
                        if (g == Gender.Male) p.SetFather(parent);
                        else p.SetMother(parent);
                        links++;
                    }
                }
                if (!CanCreate(ledger, cfg, created, generationAttempts)) break;
                if (p.def != ThingDefOf.Human || !CanLove(p) || !Rand.Chance(cfg.PartnerChance)) continue;
                Gender partnerGender = DesiredPartnerGender(p);
                generationAttempts++;
                Pawn partner = NewWorldPawn(p, partnerGender,
                    Math.Max(18f, Age(p) + Rand.Range(-8f, 9f)), false, false);
                if (partner == null) continue;
                if (!RomanceGenderCompatible(p, partner))
                {
                    // Generated pawns can have conflicting sexuality. Do not connect
                    // those pawns. The failed candidate is not passed to WorldPawns.
                    continue;
                }
                KeepWorldPawn(partner);
                ledger.RegisterGeneratedKin(partner);
                created++;
                float roll = Rand.Value;
                PawnRelationDef relation = roll < .32f ? PawnRelationDefOf.Spouse :
                    (roll < .52f ? PawnRelationDefOf.Fiance : PawnRelationDefOf.Lover);
                p.relations.AddDirectRelation(relation, partner);
                // World pawn is bounded and persisted. Never delete an active family tree.
                links++;
            }
            if (cfg.VerboseLogging)
                Log.Message("[Raiders Have Families] batch=" + newcomers.Count +
                    ", world pawns=" + created + ", links=" + links +
                    ", total created=" + ledger.CreatedWorldPawns +
                    ", live generated=" + ledger.ActiveGeneratedKin);
        }

        // Reuse a bounded selection of stored world kin. This allows kinship between
        // different raids and caravans without scanning the world's entire pawn pool.
        // It also reduces the number of newly generated world pawns.
        private static int ReuseExistingKin(List<Pawn> newcomers, RHFGameLedger ledger, RHFSettings cfg)
        {
            int linked = 0;
            if (ledger.GeneratedKin == null || ledger.GeneratedKin.Count == 0) return linked;
            for(int i=0;i<newcomers.Count;i++)
            {
                Pawn pawn=newcomers[i];
                if(pawn.def!=ThingDefOf.Human || !Rand.Chance(cfg.FactionRelativeChance))continue;
                int visited=0;
                for(int j=ledger.GeneratedKin.Count-1;j>=0 && visited<32;j--,visited++)
                {
                    Pawn kin=ledger.GeneratedKin[j]?.Kin;
                    if(kin==null || kin.Dead || kin==pawn || kin.def!=pawn.def ||
                        kin.Faction!=pawn.Faction || HaveAnyRelation(pawn,kin)) continue;
                    float gap=Age(kin)-Age(pawn);
                    if(gap>=18f && kin.gender==Gender.Male && pawn.GetFather()==null)
                    { pawn.SetFather(kin); linked++; break; }
                    if(gap>=18f && kin.gender==Gender.Female && pawn.GetMother()==null)
                    { pawn.SetMother(kin); linked++; break; }
                    if(Math.Abs(gap)<=16f && CanLove(pawn) && CanLove(kin) &&
                        RomanceGenderCompatible(pawn,kin) && Rand.Chance(.25f))
                    {
                        PawnRelationDef rel=Rand.Bool?PawnRelationDefOf.Lover:PawnRelationDefOf.Fiance;
                        pawn.relations.AddDirectRelation(rel,kin);
                        linked++;break;
                    }
                }
            }
            return linked;
        }

        // Family composition inside the same group. Derived siblings, half siblings,
        // aunt/uncle and cousins appear via RimWorld's existing family-tree workers.
        private static int LinkGroupFamily(List<Pawn> newcomers, RHFGameLedger ledger)
        {
            int links = 0;
            for (int i = 0; i < newcomers.Count; i++)
            {
                Pawn p = newcomers[i];
                int checkedCount = 0;
                for (int j = i + 1; j < newcomers.Count && checkedCount < MaxPairProbes; j++, checkedCount++)
                {
                    Pawn q = newcomers[j];
                    if (p.Faction != q.Faction || p.def != q.def || HaveAnyRelation(p, q)) continue;
                    float older = Age(p) - Age(q);
                    // Genuine parent-child connections within a traveling group:
                    // keep at least an 18-year plausible biological age gap.
                    if (older >= 18f && Rand.Chance(.18f) && AttachParent(q, p)) { links++; continue; }
                    if (older <= -18f && Rand.Chance(.18f) && AttachParent(p, q)) { links++; continue; }
                    if (Math.Abs(older) > 19f) continue;
                    // Share a previously existing parent. No creation, no world-pawn cost.
                    if (Rand.Chance(RHFMod.Settings.FactionRelativeChance * .65f) && TryShareParent(p, q)) { links++; continue; }
                    if (Rand.Chance(.10f) && p.gender != Gender.None && q.gender != Gender.None &&
                        RomanceGenderCompatible(p, q) && Age(p) >= 18f && Age(q) >= 18f)
                    {
                        PawnRelationDef ex = Rand.Bool ? PawnRelationDefOf.ExLover : PawnRelationDefOf.ExSpouse;
                        p.relations.AddDirectRelation(ex, q);
                        ledger.RegisterTemporaryLink(p, q, ex);
                        links++;
                    }
                }
            }
            return links;
        }

        private static bool AttachParent(Pawn child, Pawn parent)
        {
            if (child == null || parent == null || child == parent ||
                child.def != parent.def || parent.RaceProps.Humanlike == false) return false;
            if (parent.gender == Gender.Male && child.GetFather() == null)
            { child.SetFather(parent); return true; }
            if (parent.gender == Gender.Female && child.GetMother() == null)
            { child.SetMother(parent); return true; }
            return false;
        }

        private static bool TryShareParent(Pawn a, Pawn b)
        {
            if (Math.Abs(Age(a) - Age(b)) > 18f) return false;
            Pawn father = a.GetFather();
            Pawn mother = a.GetMother();
            if (father != null && b.GetFather() == null && Age(father) >= Age(b) + 18f)
            { b.SetFather(father); return true; }
            if (mother != null && b.GetMother() == null && Age(mother) >= Age(b) + 18f)
            { b.SetMother(mother); return true; }
            father = b.GetFather();
            mother = b.GetMother();
            if (father != null && a.GetFather() == null && Age(father) >= Age(a) + 18f)
            { a.SetFather(father); return true; }
            if (mother != null && a.GetMother() == null && Age(mother) >= Age(a) + 18f)
            { a.SetMother(mother); return true; }
            return false;
        }

        private static bool CanCreate(RHFGameLedger ledger, RHFSettings cfg, int thisCycle, int attempts)
        {
            return attempts < cfg.MaxPerWave &&
                attempts < RHFSettings.HardMaxPerCycle &&
                thisCycle < cfg.MaxPerWave &&
                ledger.ActiveGeneratedKin < cfg.MaxPerSave &&
                ledger.ActiveGeneratedKin < RHFSettings.HardMaxPerSave;
        }

        private static float Age(Pawn p) { return p.ageTracker.AgeBiologicalYearsFloat; }
        private static bool HasMissingParent(Pawn p)
        {
            return p.GetFather() == null || p.GetMother() == null;
        }
        private static Gender MissingParentGender(Pawn p)
        {
            if (p.GetFather() != null) return Gender.Female;
            if (p.GetMother() != null) return Gender.Male;
            return Rand.Bool ? Gender.Female : Gender.Male;
        }
        private static Gender MissingParentGenderForBoth(Pawn p, Pawn q)
        {
            bool fatherMissing = p.GetFather() == null && q.GetFather() == null;
            bool motherMissing = p.GetMother() == null && q.GetMother() == null;
            if (fatherMissing && motherMissing) return Rand.Bool ? Gender.Female : Gender.Male;
            if (fatherMissing) return Gender.Male;
            if (motherMissing) return Gender.Female;
            return Gender.None;
        }
        private static bool HaveAnyRelation(Pawn p, Pawn q)
        {
            // No LINQ/ToList: relation enumeration happens only for rare newcomers.
            foreach (PawnRelationDef relation in p.GetRelations(q))
                return true;
            return false;
        }
        private static bool CanLove(Pawn p)
        {
            return p != null && Age(p) >= 18f &&
                !HasSexualityTrait(p, "Asexual") &&
                !LovePartnerRelationUtility.HasAnyLovePartner(p);
        }
        private static bool HasSexualityTrait(Pawn p, string defName)
        {
            // Avoid hard references to optional/modded trait defs.
            if (p.story == null || p.story.traits == null) return false;
            var traits = p.story.traits.allTraits;
            for (int i = 0; i < traits.Count; i++)
                if (traits[i].def.defName == defName) return true;
            return false;
        }
        private static Gender DesiredPartnerGender(Pawn p)
        {
            bool same = HasSexualityTrait(p, "Gay") ||
                (HasSexualityTrait(p, "Bisexual") && Rand.Bool);
            if (same) return p.gender;
            return p.gender == Gender.Male ? Gender.Female : Gender.Male;
        }
        private static bool RomanceGenderCompatible(Pawn p, Pawn q)
        {
            return p.gender != Gender.None && q.gender != Gender.None &&
                AcceptsGender(p, q.gender) && AcceptsGender(q, p.gender);
        }
        private static bool AcceptsGender(Pawn p, Gender partnerGender)
        {
            if (HasSexualityTrait(p, "Asexual")) return false;
            if (HasSexualityTrait(p, "Bisexual")) return true;
            return HasSexualityTrait(p, "Gay") ?
                p.gender == partnerGender : p.gender != partnerGender;
        }

        private static Pawn NewWorldPawn(Pawn relativeOf, Gender gender, float bioAge,
            bool matchSurname, bool keepImmediately)
        {
            // Kind restricted to the vanilla human template. Exotic pawn
            // generators failing on this template are isolated by a try/catch.
            try
            {
                NameTriple name = relativeOf.Name as NameTriple;
                string surname = matchSurname && name != null ? name.Last : null;
                PawnGenerationRequest request = new PawnGenerationRequest(
                    PawnKindDefOf.Villager, relativeOf.Faction, PawnGenerationContext.NonPlayer,
                    forceGenerateNewPawn: true, canGeneratePawnRelations: false,
                    fixedBiologicalAge: bioAge, fixedChronologicalAge: bioAge,
                    fixedGender: gender, fixedLastName: surname,
                    forbidAnyTitle: true, forceNoGear: true);
                Pawn result = PawnGenerator.GeneratePawn(request);
                if (result == null || result.def != ThingDefOf.Human || result.gender != gender)
                    return null;
                if (keepImmediately) KeepWorldPawn(result);
                return result;
            }
            catch (Exception e)
            {
                // One warning per faction def, not one expensive warning per raid.
                string key = relativeOf.Faction?.def?.defName ?? "unknown";
                if (warnedFactionDefs.Add(key))
                    Log.Warning("[Raiders Have Families] Off-map family generation skipped for faction " +
                        key + ": " + e);
                return null;
            }
        }

        private static void KeepWorldPawn(Pawn pawn)
        {
            if (!Find.WorldPawns.Contains(pawn))
                Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.KeepForever);
        }
    }
}
