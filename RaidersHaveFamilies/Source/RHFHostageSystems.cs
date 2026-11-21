// Raiders Have Families 0.2.0 - limited hostage diplomacy and event-driven accountability.
// Designed to coexist with other faction/health mods without owning their definitions.
using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using UnityEngine;

namespace RaidersHaveFamilies
{
    public sealed class RHFWorldKin : IExposable
    {
        public Pawn Kin;
        public RHFWorldKin() { }
        public RHFWorldKin(Pawn kin) { Kin = kin; }
        public void ExposeData() { Scribe_References.Look(ref Kin, "kin"); }
    }

    public sealed class RHFManagedLink : IExposable
    {
        public Pawn A;
        public Pawn B;
        public PawnRelationDef Relation;
        public RHFManagedLink() { }
        public RHFManagedLink(Pawn a, Pawn b, PawnRelationDef relation)
        { A = a; B = b; Relation = relation; }
        public void ExposeData()
        {
            Scribe_References.Look(ref A, "a");
            Scribe_References.Look(ref B, "b");
            Scribe_Defs.Look(ref Relation, "relation");
        }
    }

    public sealed class RHFHostageRecord : IExposable
    {
        public Pawn Pawn;
        public Faction OriginalFaction;
        public int Importance;
        public int CaptureTick;
        public int WorldSeenWceOperations;
        public int DisclosedOperations;
        public int WceOperations;
        public int CrueltyScore;
        public int DisclosedCrueltyScore;
        public bool Enslaved;
        public bool ForcedConversion;
        public string InitialIdeology;
        public int LastReportedMissing;
        public int InitialMissing;
        public int PendingSilver;
        public bool PendingGoodwill;
        public bool Negotiated;
        public bool Paid;
        public bool InitialOfferMade;
        public int NextRetaliationTick;
        public bool RetaliationIssued;
        public bool Refused;
        public bool ReportedInitialInjuries;

        public RHFHostageRecord() { }
        public RHFHostageRecord(Pawn pawn)
        {
            Pawn = pawn;
            OriginalFaction = pawn.Faction;
            Importance = RHFHostageDiplomacy.RankOf(pawn);
            CaptureTick = Find.TickManager.TicksGame;
            InitialIdeology = pawn.Ideo?.ToString() ?? "";
            InitialMissing = RHFHostageDiplomacy.MissingParts(pawn);
            LastReportedMissing = InitialMissing;
        }

        public void ExposeData()
        {
            Scribe_References.Look(ref Pawn, "pawn");
            Scribe_References.Look(ref OriginalFaction, "originalFaction");
            Scribe_Values.Look(ref Importance, "importance", 0);
            Scribe_Values.Look(ref CaptureTick, "captureTick", 0);
            Scribe_Values.Look(ref WorldSeenWceOperations, "worldSeenWceOps", 0);
            Scribe_Values.Look(ref DisclosedOperations, "disclosedOps", 0);
            Scribe_Values.Look(ref WceOperations, "wceOps", 0);
            Scribe_Values.Look(ref CrueltyScore, "crueltyScore", 0);
            Scribe_Values.Look(ref DisclosedCrueltyScore, "disclosedCrueltyScore", 0);
            Scribe_Values.Look(ref Enslaved, "enslaved", false);
            Scribe_Values.Look(ref ForcedConversion, "forcedConversion", false);
            Scribe_Values.Look(ref InitialIdeology, "initialIdeology", "");
            Scribe_Values.Look(ref LastReportedMissing, "lastReportedMissing", 0);
            Scribe_Values.Look(ref InitialMissing, "initialMissing", 0);
            Scribe_Values.Look(ref PendingSilver, "pendingSilver", 0);
            Scribe_Values.Look(ref PendingGoodwill, "pendingGoodwill", false);
            Scribe_Values.Look(ref Negotiated, "negotiated", false);
            Scribe_Values.Look(ref Paid, "paid", false);
            Scribe_Values.Look(ref InitialOfferMade, "initialOfferMade", false);
            Scribe_Values.Look(ref NextRetaliationTick, "retaliationTick", 0);
            Scribe_Values.Look(ref RetaliationIssued, "retaliationIssued", false);
            Scribe_Values.Look(ref Refused, "refused", false);
            Scribe_Values.Look(ref ReportedInitialInjuries, "reportedInitialInjuries", false);
        }
    }

    public static class RHFHostageDiplomacy
    {
        public static bool HostageEligible(Pawn pawn)
        {
            return pawn != null && !pawn.Dead && pawn.RaceProps.Humanlike &&
                pawn.guest != null &&
                ((pawn.IsPrisonerOfColony && pawn.Faction != null && !pawn.Faction.IsPlayer) ||
                 (pawn.IsSlaveOfColony && Current.Game?.GetComponent<RHFGameLedger>()?.FindHostage(pawn)?.OriginalFaction != null));
        }

        public static int RankOf(Pawn pawn)
        {
            if (pawn == null) return 0;
            if (pawn.Faction != null && pawn.Faction.leader == pawn) return 4;
            if (pawn.kindDef != null && pawn.kindDef.factionLeader) return 4;
            string name = ((pawn.kindDef?.defName ?? "") + " " +
                (pawn.kindDef?.label ?? "") + " " + (pawn.story?.title ?? "")).ToLowerInvariant();
            if (HasAny(name, "governor", "commander", "chieftain", "general", "archon", "overseer", "warlord")) return 3;
            if (HasAny(name, "captain", "mayor", "elder", "leader", "officer", "noble", "minister", "chief")) return 2;
            if (HasAny(name, "elite", "veteran", "knight", "priest", "lieutenant", "specialist")) return 1;
            return 0;
        }

        private static bool HasAny(string text, params string[] fragments)
        {
            foreach (string piece in fragments)
                if (text.Contains(piece)) return true;
            return false;
        }

        public static int MissingParts(Pawn pawn)
        {
            if (pawn?.health?.hediffSet?.hediffs == null) return 0;
            int count = 0;
            var hediffs = pawn.health.hediffSet.hediffs;
            for (int i = 0; i < hediffs.Count; i++)
                if (hediffs[i] is Hediff_MissingPart) count++;
            return count;
        }

        private static int ImprovedProsthetics(Pawn pawn)
        {
            if (pawn?.health?.hediffSet?.hediffs == null) return 0;
            int count = 0;
            foreach (Hediff h in pawn.health.hediffSet.hediffs)
            {
                if (h is Hediff_AddedPart part && part.def.addedPartProps != null &&
                    part.def.addedPartProps.partEfficiency > 1.01f) count++;
            }
            return count;
        }

        public static int SilverPrice(RHFHostageRecord record)
        {
            if (record?.Pawn == null) return 0;
            Pawn pawn = record.Pawn;
            float health = pawn.health?.summaryHealth?.SummaryHealthPercent ?? 1f;
            float partLoss = Math.Min(0.48f, MissingParts(pawn) * 0.065f);
            float quality = 1f + Math.Min(0.50f, ImprovedProsthetics(pawn) * 0.13f);
            float rank = record.Importance == 4 ? 20f : record.Importance == 3 ? 9f :
                record.Importance == 2 ? 4f : record.Importance == 1 ? 2f : 1f;
            float faction = record.OriginalFaction?.def != null &&
                record.OriginalFaction.def.techLevel >= TechLevel.Industrial ? 1.45f : 1f;
            float assessed = Math.Max(0.15f, health - partLoss) * quality;
            return Math.Max(50, Mathf.RoundToInt(220f * rank * faction * assessed));
        }

        public static string Summary(RHFHostageRecord r)
        {
            Pawn pawn = r.Pawn;
            string family = "";
            int count = 0;
            if (pawn?.relations != null)
            {
                foreach (Pawn person in pawn.relations.RelatedPawns)
                {
                    if (person == null || person == pawn || count >= 3) break;
                    if (person.Faction == r.OriginalFaction)
                    {
                        family += (family.Length == 0 ? "" : ", ") + person.LabelShort;
                        count++;
                    }
                }
            }
            if (family.Length == 0) family = "родственников в нашей базе нет";
            float health = pawn.health?.summaryHealth?.SummaryHealthPercent ?? 1f;
            string tone = r.Importance >= 3 ?
                "Совет фракции следит за судьбой пленника. Любое решение имеет политические последствия." :
                count > 0 ? "Родные просят хотя бы подтвердить, что пленник жив." :
                "Фракция проверяет ваши требования; решение принимают не сразу.";
            return r.OriginalFaction.Name + " — " + pawn.LabelShortCap + "\n" +
                "Статус: " + RankLabel(r.Importance) + ". Здоровье: " +
                Mathf.RoundToInt(health * 100f) + "%\n" +
                "Известные родные: " + family + ".\n\n" + tone;
        }

        public static string RankLabel(int rank)
        {
            switch (rank)
            {
                case 4: return "глава фракции";
                case 3: return "высокопоставленная персона";
                case 2: return "местный руководитель или офицер";
                case 1: return "опытный или известный представитель";
                default: return "обычный представитель фракции";
            }
        }

        public static void OpenForColony(Map map)
        {
            if (map == null) return;
            RHFGameLedger ledger = Current.Game?.GetComponent<RHFGameLedger>();
            if (ledger == null) return;
            List<FloatMenuOption> choices = new List<FloatMenuOption>();
            // Include former prisoners enslaved by the player, but only when tracked
            // before enslavement so we know their original faction.
            var prisoners = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < prisoners.Count; i++)
            {
                Pawn p = prisoners[i];
                if (!HostageEligible(p)) continue;
                RHFHostageRecord r = ledger.FindHostage(p) ?? ledger.TrackHostage(p);
                if (r == null || r.Paid) continue;
                RHFHostageRecord captured = r;
                choices.Add(new FloatMenuOption(p.LabelShortCap + " — " + r.OriginalFaction.Name +
                    " (" + SilverPrice(r) + " серебра)", () => OpenNegotiation(captured)));
            }
            if (choices.Count == 0)
                Messages.Message("Нет доступных для переговоров пленников с установленной фракцией.", MessageTypeDefOf.RejectInput, false);
            else Find.WindowStack.Add(new FloatMenu(choices));
        }

        public static void OpenNegotiation(RHFHostageRecord r)
        {
            if (r == null || !HostageEligible(r.Pawn) || r.Paid) return;
            string prompt = Summary(r) + "\n\nВыберите требование. Плата придёт только после того, как пленник действительно покинет карту живым.";
            Find.WindowStack.Add(new Dialog_MessageBox(prompt,
                "Выбрать условия", () => ChooseTerms(r),
                "Закончить разговор", null));
        }

        private static void ChooseTerms(RHFHostageRecord r)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            int silver = SilverPrice(r);
            options.Add(new FloatMenuOption("Потребовать выкуп: " + silver + " серебра", () =>
                {
                    r.PendingSilver = silver;
                    r.PendingGoodwill = false;
                    r.Negotiated = true;
                    Messages.Message("Условия приняты к рассмотрению. Отпустите пленника через вкладку 'Пленник'. Награда только после выхода с карты.", MessageTypeDefOf.NeutralEvent, false);
                }));
            options.Add(new FloatMenuOption("Отпустить без платы — жест доброй воли", () =>
                {
                    r.PendingSilver = 0;
                    r.PendingGoodwill = true;
                    r.Negotiated = true;
                    Messages.Message("Договорённость достигнута. Отпустите пленника; отношения изменятся после его выхода с карты.", MessageTypeDefOf.NeutralEvent, false);
                }));
            int missing = MissingParts(r.Pawn);
            if (r.InitialMissing > 0 && !r.ReportedInitialInjuries)
                options.Add(new FloatMenuOption("Сообщить о старых боевых ранениях (без обвинений)", () =>
                {
                    r.ReportedInitialInjuries = true;
                    Find.WindowStack.Add(new Dialog_MessageBox("Родственники потрясены состоянием пленника. " +
                        "Ранения, полученные до заключения под стражу, не объявлены пытками и не дают бонусов к настроению."));
                }));
            if (missing > r.LastReportedMissing || r.CrueltyScore > r.DisclosedCrueltyScore)
                options.Add(new FloatMenuOption("Сообщить родственникам о травмах и операциях", () => Disclose(r)));
            if (r.Importance >= 2)
                options.Add(new FloatMenuOption("Отказать фракции в возвращении пленника", () => Refuse(r)));
            Find.WindowStack.Add(new FloatMenu(options));
        }

        public static void Disclose(RHFHostageRecord r)
        {
            if (r == null || r.OriginalFaction == null) return;
            int extraMissing = Math.Max(0, MissingParts(r.Pawn) - r.LastReportedMissing);
            int newCrimes = Math.Max(0, r.WceOperations - r.DisclosedOperations);
            int newCruelty = Math.Max(0, r.CrueltyScore - r.DisclosedCrueltyScore);
            if (extraMissing == 0 && newCrimes == 0 && newCruelty == 0) return;
            // Ordinary battle wounds are not deliberate cruelty. WCE operations performed
            // by player doctors after capture count as deliberate acts; never guess from scars.
            int goodwillLoss = Math.Min(90, extraMissing * 2 +
                newCruelty * (3 + r.Importance * 2));
            if (goodwillLoss > 0)
                r.OriginalFaction.TryAffectGoodwillWith(Faction.OfPlayer, -goodwillLoss,
                    canSendMessage: true, canSendHostilityLetter: true);
            if (newCruelty > 0)
                RHFMoralResponse.GivePublicCrueltyThoughts(r.Pawn, newCruelty, r.Importance);
            r.DisclosedOperations = r.WceOperations;
            r.DisclosedCrueltyScore = r.CrueltyScore;
            r.LastReportedMissing = MissingParts(r.Pawn);
            if (newCruelty > 0 && r.Importance >= 2) QueueRetaliation(r);
            Find.WindowStack.Add(new Dialog_MessageBox(newCruelty > 0 ?
                "Фракция приняла сообщение об умышленных операциях. Родственники возмущены; власти требуют объяснений." :
                "Сообщение о телесных повреждениях принято. Родственники встревожены; происхождение травм не установлено."));
        }

        public static void Refuse(RHFHostageRecord r)
        {
            if (r == null) return;
            r.Refused = true;
            r.PendingSilver = 0;
            r.PendingGoodwill = false;
            r.Negotiated = false;
            QueueRetaliation(r);
            Messages.Message(r.OriginalFaction.Name + " восприняла отказ как провокацию.", MessageTypeDefOf.ThreatSmall, false);
        }

        private static void QueueRetaliation(RHFHostageRecord r)
        {
            if (RHFMod.Settings?.Retaliation != true || r.RetaliationIssued || r.NextRetaliationTick > 0) return;
            // This schedules an optional boosted vanilla raid, not the future custom
            // prison-infiltration AI. One operation per captive; hard bounded points.
            r.NextRetaliationTick = Find.TickManager.TicksGame + Rand.Range(100000, 220000);
        }

        public static void UpdateOffers(RHFGameLedger ledger, int tick)
        {
            for (int i = ledger.Hostages.Count - 1; i >= 0; i--)
            {
                RHFHostageRecord r = ledger.Hostages[i];
                if (r == null || r.Pawn == null || r.Paid ||
                    ((r.Pawn.Dead || (r.Pawn.Map == null && !r.Pawn.IsPrisonerOfColony && !r.Negotiated))
                     && (r.NextRetaliationTick <= 0 || r.RetaliationIssued)))
                {
                    // Discard stale tracking; never alter relationships belonging to other mods.
                    ledger.Hostages.RemoveAt(i);
                    continue;
                }
                if (r.Pawn.IsSlaveOfColony && !r.Enslaved)
                {
                    r.Enslaved = true;
                    r.CrueltyScore = Math.Min(200, r.CrueltyScore + 4);
                }
                if (r.Pawn.IsPrisonerOfColony && !r.ForcedConversion &&
                    !string.Equals(r.InitialIdeology, r.Pawn.Ideo?.ToString() ?? "", StringComparison.Ordinal))
                {
                    r.ForcedConversion = true;
                    r.CrueltyScore = Math.Min(200, r.CrueltyScore + 3);
                }
                if (r.NextRetaliationTick > 0 && tick >= r.NextRetaliationTick && !r.RetaliationIssued)
                    StartRetaliation(r);
                if (r.InitialOfferMade || r.Negotiated || r.Importance < 2 ||
                    !HostageEligible(r.Pawn) || tick - r.CaptureTick < 60000 ||
                    Rand.Value > (r.Importance >= 4 ? .85f : r.Importance == 3 ? .60f : .30f)) continue;
                r.InitialOfferMade = true;
                RHFHostageRecord captured = r;
                Find.WindowStack.Add(new Dialog_MessageBox(
                    "Срочное сообщение от " + r.OriginalFaction.Name + ":\n\n" +
                    "Мы требуем возвращения " + r.Pawn.LabelShortCap + ". " +
                    "Готовы выплатить " + SilverPrice(r) + " серебра после безопасного освобождения. " +
                    "За увечья, нанесённые в плену, вы будете отвечать.",
                    "Принять условия", () => { captured.PendingSilver = SilverPrice(captured); captured.Negotiated = true; },
                    "Отказать", () => Refuse(captured)));
            }
        }

        private static void StartRetaliation(RHFHostageRecord r)
        {
            r.RetaliationIssued = true;
            if (RHFMod.Settings?.Retaliation != true || r.OriginalFaction == null ||
                !r.OriginalFaction.HostileTo(Faction.OfPlayer)) return;
            Map map = r.Pawn?.Map;
            if (map == null || !map.IsPlayerHome)
            {
                foreach (Map candidate in Find.Maps)
                    if (candidate.IsPlayerHome) { map = candidate; break; }
            }
            if (map == null || !map.IsPlayerHome) return;
            try
            {
                IncidentParms p = new IncidentParms();
                p.target = map;
                p.faction = r.OriginalFaction;
                p.points = Mathf.Clamp(StorytellerUtility.DefaultThreatPointsNow(map) *
                    (1.2f + .35f * r.Importance + Math.Min(0.50f, .06f * r.WceOperations)), 200f, 18000f);
                p.raidStrategy = RaidStrategyDefOf.ImmediateAttack;
                Find.Storyteller.incidentQueue.Add(IncidentDefOf.RaidEnemy, Find.TickManager.TicksGame + 5000, p);
                Find.LetterStack.ReceiveLetter("Угроза возмездия", r.OriginalFaction.Name +
                    " готовит нападение из-за удержания пленников. Ожидается усиленный рейд.",
                    LetterDefOf.ThreatBig, r.Pawn);
            }
            catch (Exception e) { Log.Warning("[Raiders Have Families] Faction cannot stage reprisal: " + e); }
        }

        public static void OnPrisonerExit(Pawn pawn, Map previousMap, bool wasReleased)
        {
            if (!wasReleased || previousMap == null || pawn == null || pawn.Dead) return;
            var ledger = Current.Game?.GetComponent<RHFGameLedger>();
            RHFHostageRecord r = ledger?.FindHostage(pawn);
            if (r == null || !r.Negotiated || r.Paid || r.OriginalFaction == null) return;
            r.Paid = true;
            int goodwill = Mathf.Clamp(5 + r.Importance * 7 + ImprovedProsthetics(pawn) * 3 -
                r.CrueltyScore * 2, 0, 80);
            int undisclosed = Math.Max(0, r.CrueltyScore - r.DisclosedCrueltyScore);
            // Family discovers concealed cruelty when the released pawn returns.
            // No player mood bonus: they did not publicize it themselves.
            if (undisclosed > 0)
                r.OriginalFaction.TryAffectGoodwillWith(Faction.OfPlayer,
                    -Math.Min(95, undisclosed * (6 + r.Importance * 3)),
                    canSendMessage: true, canSendHostilityLetter: true);
            if (r.PendingGoodwill && goodwill > 0)
                r.OriginalFaction.TryAffectGoodwillWith(Faction.OfPlayer, goodwill, canSendMessage: true,
                    canSendHostilityLetter: true);
            if (r.PendingSilver > 0)
            {
                int remaining = r.PendingSilver;
                while (remaining > 0)
                {
                    Thing silver = ThingMaker.MakeThing(ThingDefOf.Silver);
                    int stack = Math.Min(remaining, silver.def.stackLimit);
                    silver.stackCount = stack;
                    GenPlace.TryPlaceThing(silver, previousMap.Center, previousMap, ThingPlaceMode.Near);
                    remaining -= stack;
                }
                Messages.Message("Получено " + r.PendingSilver + " серебра за возвращение " + pawn.LabelShortCap +
                    ".", MessageTypeDefOf.PositiveEvent, false);
            }
            ledger.Hostages.Remove(r);
        }

        public static void OnHostageDeath(Pawn victim, bool causedByPlayer)
        {
            var ledger = Current.Game?.GetComponent<RHFGameLedger>();
            RHFHostageRecord r = ledger?.FindHostage(victim);
            if (r == null || r.OriginalFaction == null || r.Paid) return;
            int goodwillLoss = causedByPlayer ? 10 + r.Importance * 13 : 2 + r.Importance * 3;
            if (r.WceOperations > r.DisclosedOperations)
                goodwillLoss += Math.Min(70, (r.CrueltyScore - r.DisclosedCrueltyScore) * 3);
            r.OriginalFaction.TryAffectGoodwillWith(Faction.OfPlayer, -Math.Min(100, goodwillLoss),
                canSendMessage: true, canSendHostilityLetter: true);
            if (r.Importance >= 2 && causedByPlayer) QueueRetaliation(r);
            r.Negotiated = false;
            r.PendingSilver = 0;
            // No joyous thoughts here. Disclosure is still needed for publicity bonuses.
        }

        public static void RecordWceOperation(Pawn victim, Pawn doctor, string recipe)
        {
            if (RHFMod.Settings?.WCE2Events != true || victim == null || doctor == null ||
                !victim.IsPrisonerOfColony || doctor.Faction != Faction.OfPlayer) return;
            RHFHostageRecord r = Current.Game?.GetComponent<RHFGameLedger>()?.TrackHostage(victim);
            if (r == null) return;
            // Record, don't reward. Reputation and mood require PUBLIC DISCLOSURE.
            r.WceOperations = Math.Min(1000, r.WceOperations + 1);
            string label = (recipe ?? "").ToLowerInvariant();
            int severity = label.Contains("vivisect") || label.Contains("lobotom") ? 5 :
                label.Contains("mutil") || label.Contains("brain") || label.Contains("amput") ? 4 :
                label.Contains("implant") || label.Contains("sew") || label.Contains("gouge") ? 3 : 2;
            // PainTotal exists for all humanlikes; no hardcoded prosthetic brands.
            float pain = victim.health?.hediffSet?.PainTotal ?? 0f;
            int painBonus = Mathf.Clamp(Mathf.CeilToInt(pain * 4f), 0, 4);
            r.CrueltyScore = Math.Min(200, r.CrueltyScore + severity + painBonus);
        }
    }

    public static class RHFMoralResponse
    {
        // No mood reward when operations remain unknown. Vanilla thought systems for
        // eating people, execution, slavery and conversion are never replaced.
        public static void GivePublicCrueltyThoughts(Pawn victim, int newCruelty, int importance)
        {
            // Exponential escalation in score, but bounded by six discrete memories.
            // This avoids destabilizing pawn moods or creating unbounded stacks.
            int severity = Mathf.Clamp(Mathf.CeilToInt(Mathf.Log(1f + newCruelty *
                (1f + importance * 0.5f), 1.7f)), 1, 6);
            ThoughtDef memory = DefDatabase<ThoughtDef>.GetNamedSilentFail("RHF_PublicCruelty" + severity);
            if (memory == null) return;
            foreach (Map m in Find.Maps)
            {
                if (!m.IsPlayerHome) continue;
                foreach (Pawn p in m.mapPawns.FreeColonistsSpawned)
                {
                    if (p.needs?.mood == null || p.story?.traits == null) continue;
                    bool bloodlust = p.story.traits.HasTrait(TraitDefOf.Bloodlust);
                    bool endorses = false;
                    if (ModsConfig.IdeologyActive && p.Ideo != null)
                    {
                        foreach (Precept precept in p.Ideo.PreceptsListForReading)
                        {
                            string n = precept.def?.defName?.ToLowerInvariant() ?? "";
                            if (n.Contains("torture") &&
                                (n.Contains("respected") || n.Contains("required") || n.Contains("honorable")))
                            { endorses = true; break; }
                        }
                    }
                    if (bloodlust || endorses)
                    {
                        for (int old = 1; old <= 6; old++)
                        {
                            ThoughtDef previous = DefDatabase<ThoughtDef>.GetNamedSilentFail("RHF_PublicCruelty" + old);
                            if (previous != null) p.needs.mood.thoughts.memories.RemoveMemoriesOfDef(previous);
                        }
                        p.needs.mood.thoughts.memories.TryGainMemory(memory);
                    }
                }
            }
        }
    }
}
