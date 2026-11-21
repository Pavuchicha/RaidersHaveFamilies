// Raiders Have Families v0.1.0 — RimWorld 1.6
// Standalone MapComponent, no Harmony patches; game-owned relationships only.
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RaidersHaveFamilies
{
    public sealed class RHFSettings : ModSettings
    {
        public bool Enabled = true;
        public float ParentChance = 0.72f;
        public float PartnerChance = 0.38f;
        public float RaidmatePartnerChance = 0.22f;
        public float SharedParentChance = 0.22f;
        public int MaxPerWave = 12;
        public int MaxPerSave = 300;
        public bool VerboseLogging = false;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref Enabled, "enabled", true);
            Scribe_Values.Look(ref ParentChance, "parentChance", 0.72f);
            Scribe_Values.Look(ref PartnerChance, "partnerChance", 0.38f);
            Scribe_Values.Look(ref RaidmatePartnerChance, "raidmatePartnerChance", 0.22f);
            Scribe_Values.Look(ref SharedParentChance, "sharedParentChance", 0.22f);
            Scribe_Values.Look(ref MaxPerWave, "maxPerWave", 12);
            Scribe_Values.Look(ref MaxPerSave, "maxPerSave", 300);
            Scribe_Values.Look(ref VerboseLogging, "verboseLogging", false);
        }
    }

    public sealed class RHFMod : Mod
    {
        public static RHFSettings Settings;
        public RHFMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<RHFSettings>();
        }

        public override string SettingsCategory()
        {
            return "Raiders Have Families";
        }

        public override void DoSettingsWindowContents(Rect rect)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(rect);
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
            listing.Label("RHF_PerWave".Translate(Settings.MaxPerWave));
            Settings.MaxPerWave = (int)listing.Slider(Settings.MaxPerWave, 0f, 30f);
            listing.Label("RHF_PerSave".Translate(Settings.MaxPerSave));
            Settings.MaxPerSave = (int)listing.Slider(Settings.MaxPerSave, 0f, 2000f);
            listing.CheckboxLabeled("RHF_Logging".Translate(), ref Settings.VerboseLogging);
            listing.End();
        }
    }

    // The global cap is saved once per game, across all colonies and maps.
    public sealed class RHFGameLedger : GameComponent
    {
        public int CreatedWorldPawns;
        public RHFGameLedger(Game game) { }
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref CreatedWorldPawns, "rhfWorldPawnsCreated", 0);
        }
    }

    // Detect new hostile human pawns on player-home maps after raids actually arrive.
    // This also covers nonstandard raid arrival modes without patching the raid generator.
    public sealed class RHFMapComponent : MapComponent
    {
        private List<int> processedPawnIds = new List<int>();
        private HashSet<int> processed = new HashSet<int>();
        private const int ScanInterval = 250;
        private const int MaxBatchSize = 120;

        public RHFMapComponent(Map map) : base(map) { }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref processedPawnIds, "rhfProcessedPawnIds", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (processedPawnIds == null) processedPawnIds = new List<int>();
                processed = new HashSet<int>(processedPawnIds);
            }
        }

        public override void MapComponentTick()
        {
            if (RHFMod.Settings == null || !RHFMod.Settings.Enabled || !map.IsPlayerHome)
                return;
            if (Find.TickManager.TicksGame % ScanInterval != 0) return;
            List<Pawn> newcomers = new List<Pawn>();
            IReadOnlyList<Pawn> seen = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < seen.Count; i++)
            {
                Pawn p = seen[i];
                if (!Eligible(p) || processed.Contains(p.thingIDNumber)) continue;
                processed.Add(p.thingIDNumber);
                processedPawnIds.Add(p.thingIDNumber);
                newcomers.Add(p);
                if (newcomers.Count >= MaxBatchSize) break;
            }
            if (newcomers.Count == 0) return;
            try { Enrich(newcomers); }
            catch (Exception e) { Log.Warning("[Raiders Have Families] Encountered an unexpected relationship-generation issue: " + e); }
        }

        private static bool Eligible(Pawn p)
        {
            if (p == null || p.Dead || p.Destroyed || !p.Spawned || p.Faction == null ||
                p.Faction.IsPlayer || !p.Faction.HostileTo(Faction.OfPlayer) ||
                p.def != ThingDefOf.Human || p.relations == null || p.ageTracker == null ||
                p.gender == Gender.None || p.ageTracker.AgeBiologicalYearsFloat < 16f)
                return false;
            return true;
        }

        private static void Enrich(List<Pawn> newcomers)
        {
            RHFSettings cfg = RHFMod.Settings;
            RHFGameLedger ledger = Current.Game.GetComponent<RHFGameLedger>();
            int created = 0;
            int links = 0;

            // Create a few relationships inside this group first: no extra world pawns.
            HashSet<Pawn> paired = new HashSet<Pawn>();
            for (int i = 0; i < newcomers.Count; i++)
            {
                Pawn p = newcomers[i];
                if (paired.Contains(p) || !CanLove(p) || !Rand.Chance(cfg.RaidmatePartnerChance)) continue;
                for (int j = i + 1; j < newcomers.Count; j++)
                {
                    Pawn q = newcomers[j];
                    if (paired.Contains(q) || !CanLove(q) || p.Faction != q.Faction ||
                        p.gender == q.gender || Math.Abs(Age(p) - Age(q)) > 16f ||
                        p.GetRelations(q).Any()) continue;
                    p.relations.AddDirectRelation(Rand.Chance(0.35f) ? PawnRelationDefOf.Spouse : PawnRelationDefOf.Lover, q);
                    paired.Add(p); paired.Add(q); links++;
                    break;
                }
            }

            // A shared real parent makes two raidmates siblings in vanilla's relation system.
            HashSet<Pawn> grouped = new HashSet<Pawn>();
            for (int i = 0; i < newcomers.Count; i++)
            {
                Pawn p = newcomers[i];
                if (grouped.Contains(p) || p.GetFather() != null ||
                    !Rand.Chance(cfg.SharedParentChance)) continue;
                for (int j = i + 1; j < newcomers.Count; j++)
                {
                    Pawn q = newcomers[j];
                    if (grouped.Contains(q) || p.Faction != q.Faction || q.GetFather() != null ||
                        Math.Abs(Age(p) - Age(q)) > 13f || p.GetRelations(q).Any()) continue;
                    if (!CanCreate(ledger, cfg, created)) break;
                    float age = Math.Max(Age(p), Age(q)) + Rand.Range(23f, 37f);
                    Pawn father = NewWorldPawn(p, Gender.Male, age, true);
                    if (father == null) break;
                    ledger.CreatedWorldPawns++; created++;
                    p.SetFather(father);
                    q.SetFather(father);
                    grouped.Add(p); grouped.Add(q);
                    links += 2;
                    break;
                }
            }

            // Parents and lovers that remain off-map are actual persistent world pawns.
            for (int i = 0; i < newcomers.Count; i++)
            {
                Pawn p = newcomers[i];
                if (!CanCreate(ledger, cfg, created)) break;
                bool haveFamily = p.GetFather() != null || p.GetMother() != null;
                if (!haveFamily && Age(p) < 70f && Rand.Chance(cfg.ParentChance))
                {
                    Gender gender = Rand.Bool ? Gender.Female : Gender.Male;
                    float age = Age(p) + Rand.Range(22f, 36f);
                    Pawn parent = NewWorldPawn(p, gender, age, gender == Gender.Male);
                    if (parent != null)
                    {
                        ledger.CreatedWorldPawns++; created++;
                        if (gender == Gender.Female) p.SetMother(parent);
                        else p.SetFather(parent);
                        links++;
                    }
                }
                if (!CanCreate(ledger, cfg, created)) break;
                if (CanLove(p) && Rand.Chance(cfg.PartnerChance))
                {
                    Gender g = p.gender == Gender.Male ? Gender.Female : Gender.Male;
                    float age = Math.Max(18f, Age(p) + Rand.Range(-8f, 9f));
                    Pawn partner = NewWorldPawn(p, g, age, false);
                    if (partner != null)
                    {
                        ledger.CreatedWorldPawns++; created++;
                        p.relations.AddDirectRelation(
                            Rand.Chance(0.32f) ? PawnRelationDefOf.Spouse : PawnRelationDefOf.Lover, partner);
                        links++;
                    }
                }
            }
            if (cfg.VerboseLogging)
                Log.Message("[Raiders Have Families] Processed " + newcomers.Count +
                    " raiders, created " + created + " world pawns, added " + links + " ties. Total: " +
                    ledger.CreatedWorldPawns + ".");
        }

        private static bool CanCreate(RHFGameLedger ledger, RHFSettings cfg, int inWave)
        {
            return ledger != null && inWave < cfg.MaxPerWave && ledger.CreatedWorldPawns < cfg.MaxPerSave;
        }

        private static float Age(Pawn p) { return p.ageTracker.AgeBiologicalYearsFloat; }
        private static bool CanLove(Pawn p)
        {
            return p != null && Age(p) >= 18f &&
                   !LovePartnerRelationUtility.HasAnyLovePartner(p);
        }

        private static Pawn NewWorldPawn(Pawn relativeOf, Gender gender, float bioAge, bool matchSurname)
        {
            try
            {
                string surname = null;
                if (matchSurname)
                {
                    NameTriple triple = relativeOf.Name as NameTriple;
                    if (triple != null) surname = triple.Last;
                }
                PawnGenerationRequest request = new PawnGenerationRequest(
                    PawnKindDefOf.Villager, relativeOf.Faction, PawnGenerationContext.NonPlayer,
                    forceGenerateNewPawn: true, canGeneratePawnRelations: false,
                    fixedBiologicalAge: bioAge, fixedChronologicalAge: bioAge,
                    fixedGender: gender, fixedLastName: surname,
                    forbidAnyTitle: true, forceNoGear: true);
                Pawn result = PawnGenerator.GeneratePawn(request);
                if (result == null || result.def != relativeOf.def || result.gender != gender)
                    return null;
                // Keep this real character on the world pawn list so relations survive saving/loading.
                if (!Find.WorldPawns.Contains(result))
                    Find.WorldPawns.PassToWorld(result, PawnDiscardDecideMode.KeepForever);
                return result;
            }
            catch (Exception e)
            {
                Log.Warning("[Raiders Have Families] Could not generate off-map relative: " + e);
                return null;
            }
        }
    }
}
