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
        public int RankSchema = 3;
        public int OfferDeadlineTick;
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
        public bool Executed;
        public bool DeathReportMade;
        public int DeathTick;
        public string LastConfirmedOperation = "";
        public int LastConfirmedOperationTick;
        public int LastConfirmedOperationSeverity;
        public int SurgicalCrueltyCount;
        public string OfficeName = "";
        public bool DebugOffice;
        public int RefusalCount;
        public int HighDemandRejected;
        public int LastRejectionTick;
        public int SelfAdmittedMutilations;
        public int LastAdmittedMissing;
        public bool MercyAwarded; // avoids repeated ideology memories in exit/sale hooks
        // Dead hostages remain negotiable until the player explicitly settles the case.
        public bool PlayerCausedDeath;
        public bool BodyButchered;
        public bool BodyConsumedDirectly;
        public int ButcheredTick;
        public string NameAtCapture = "";
        public IntVec3 LastHeldCell = IntVec3.Invalid;

        public RHFHostageRecord() { }
        public RHFHostageRecord(Pawn pawn)
        {
            Pawn = pawn;
            OriginalFaction = pawn.Faction;
            Importance = RHFHostageDiplomacy.RankOf(pawn);
            OfficeName = RHFRankResolver.OfficeFor(pawn, Importance);
            CaptureTick = Find.TickManager.TicksGame;
            InitialIdeology = pawn.Ideo?.ToString() ?? "";
            NameAtCapture = pawn.LabelShortCap;
            if (pawn.Spawned) LastHeldCell = pawn.Position;
            InitialMissing = RHFHostageDiplomacy.MissingParts(pawn);
            LastAdmittedMissing = InitialMissing;
            LastReportedMissing = InitialMissing;
        }

        public void ExposeData()
        {
            Scribe_References.Look(ref Pawn, "pawn");
            Scribe_References.Look(ref OriginalFaction, "originalFaction");
            Scribe_Values.Look(ref Importance, "importance", 0);
            Scribe_Values.Look(ref RankSchema, "rankSchema", 0);
            Scribe_Values.Look(ref OfferDeadlineTick, "offerDeadlineTick", 0);
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
            Scribe_Values.Look(ref Executed, "executed", false);
            Scribe_Values.Look(ref DeathReportMade, "deathReportMade", false);
            Scribe_Values.Look(ref DeathTick, "deathTick", 0);
            Scribe_Values.Look(ref LastConfirmedOperation, "lastConfirmedOperation", "");
            Scribe_Values.Look(ref LastConfirmedOperationTick, "lastConfirmedOpTick", 0);
            Scribe_Values.Look(ref LastConfirmedOperationSeverity, "lastConfirmedSeverity", 0);
            Scribe_Values.Look(ref SurgicalCrueltyCount, "surgicalCrueltyCount", 0);
            Scribe_Values.Look(ref OfficeName, "rhfOfficeNameV7", "");
            Scribe_Values.Look(ref DebugOffice, "rhfDebugOfficeV7", false);
            Scribe_Values.Look(ref RefusalCount, "rhfRefusalCountV7", 0);
            Scribe_Values.Look(ref HighDemandRejected, "rhfHighDemandRejectedV7", 0);
            Scribe_Values.Look(ref LastRejectionTick, "rhfLastRejectionTickV7", 0);
            Scribe_Values.Look(ref SelfAdmittedMutilations, "rhfSelfAdmittedV7", 0);
            Scribe_Values.Look(ref LastAdmittedMissing, "rhfLastAdmittedMissingV7", 0);
            Scribe_Values.Look(ref MercyAwarded, "rhfMercyAwardedV8", false);
            Scribe_Values.Look(ref PlayerCausedDeath, "rhfPlayerCausedDeathV10", false);
            Scribe_Values.Look(ref BodyButchered, "rhfBodyButcheredV10", false);
            Scribe_Values.Look(ref BodyConsumedDirectly, "rhfBodyConsumedDirectlyV10", false);
            Scribe_Values.Look(ref ButcheredTick, "rhfButcheredTickV10", 0);
            Scribe_Values.Look(ref NameAtCapture, "rhfNameAtCaptureV10", "");
            Scribe_Values.Look(ref LastHeldCell, "rhfLastHeldCellV10", IntVec3.Invalid);
            if (Scribe.mode==LoadSaveMode.PostLoadInit && RankSchema<3)
            { Importance=RHFRankResolver.RankOf(Pawn); RankSchema=3; }
        }
    }

    public static class RHFHostageDiplomacy
    {
        public static bool IsDeadCase(RHFHostageRecord record)
        { return record != null && (record.DeathTick > 0 || record.Pawn?.Dead == true); }

        public static bool HostageEligible(Pawn pawn)
        {
            return pawn != null && !pawn.Dead && pawn.RaceProps.Humanlike &&
                pawn.guest != null &&
                ((pawn.IsPrisonerOfColony && pawn.Faction != null && !pawn.Faction.IsPlayer) ||
                 (pawn.IsSlaveOfColony && Current.Game?.GetComponent<RHFGameLedger>()?.FindHostage(pawn)?.OriginalFaction != null));
        }

        public static int RankOf(Pawn pawn) { return RHFRankResolver.RankOf(pawn); }

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
            float rank = RHFRankResolver.RansomMultiplier(record.Importance);
            float faction = record.OriginalFaction?.def != null &&
                record.OriginalFaction.def.techLevel >= TechLevel.Industrial ? 1.45f : 1f;
            float assessed = Math.Max(0.15f, health - partLoss) * quality;
            return Math.Max(50, Mathf.RoundToInt(220f * rank * faction * assessed));
        }

        public static int GoodwillReward(RHFHostageRecord r)
        {
            if (r?.Pawn == null) return 0;
            return Mathf.Clamp(5 + r.Importance * 7 + ImprovedProsthetics(r.Pawn) * 3 -
                r.CrueltyScore * 2, 0, 80);
        }

        // The text is explicitly offered AFTER the player agrees to release a hostage.
        public static void AgreeRelease(RHFHostageRecord r, int silver, bool goodwill)
        {
            if (r == null || !HostageEligible(r.Pawn) || r.Paid) return;
            r.PendingSilver = Math.Max(0, silver);
            r.PendingGoodwill = goodwill;
            r.Negotiated = true;
            r.Refused = false;
            r.OfferDeadlineTick = 0;
            Messages.Message("Договорённость достигнута. Отпустите " + r.Pawn.LabelShortCap +
                ": награда будет доставлена после выхода пленника с карты.",
                MessageTypeDefOf.NeutralEvent, false);
        }

        public static int NegotiatorSocial(Pawn negotiator)
        {
            return negotiator?.skills == null ? 0 :
                Math.Max(0, negotiator.skills.GetSkill(SkillDefOf.Social)?.Level ?? 0);
        }

        public static int NegotiatedPrice(RHFHostageRecord r, Pawn negotiator)
        {
            if (r == null) return 0;
            // Physical condition is already in SilverPrice. Social expertise and
            // recorded hostility change the amount demanded in the current dialogue.
            float social = 1f + Mathf.Clamp(NegotiatorSocial(negotiator) - 5, -5, 15) * .015f;
            float tension = 1f + Math.Min(.32f,
                r.HighDemandRejected * .04f + r.RefusalCount * .06f +
                Math.Max(0, r.CrueltyScore - r.DisclosedCrueltyScore) * .003f);
            return Math.Max(50, Mathf.RoundToInt(SilverPrice(r) * social * tension));
        }

        public static int CounterOfferChancePercent(RHFHostageRecord r, float factor,
            Pawn negotiator = null)
        {
            if (r?.Pawn == null) return 0;
            float health = r.Pawn.health?.summaryHealth?.SummaryHealthPercent ?? 1f;
            float skill = Mathf.Clamp(NegotiatorSocial(negotiator) - 6, -6, 14) * .018f;
            float priorHistory = r.HighDemandRejected * .055f + r.RefusalCount * .075f;
            float cruelty = Math.Min(.33f, r.CrueltyScore * .004f);
            float anger = Math.Min(.24f, Math.Max(0,-RHFPoliticalReputation.Pressure(r.OriginalFaction)) * .0005f);
            float fearOfLoss = Mathf.Min(.18f, r.Importance * .027f +
                (1f-health) * .10f);
            // Some factions may pay more under duress, but failed talks and public
            // injury reports increase the risk of violent retaliation instead.
            float chance = Mathf.Clamp(.67f + skill + fearOfLoss - (factor-1f)*.63f -
                priorHistory - cruelty - anger, .04f, .96f);
            return Mathf.RoundToInt(chance * 100f);
        }

        public static bool RequestHigherRansom(RHFHostageRecord r, float factor,
            Pawn negotiator = null)
        {
            if (r?.OriginalFaction == null || !HostageEligible(r.Pawn) || r.Negotiated) return false;
            if (Rand.RangeInclusive(1,100) <= CounterOfferChancePercent(r, factor, negotiator))
            {
                AgreeRelease(r, Mathf.RoundToInt(NegotiatedPrice(r,negotiator)*factor), false);
                return true;
            }
            r.HighDemandRejected++;
            r.LastRejectionTick = Find.TickManager.TicksGame;
            RHFPoliticalReputation.Change(r.OriginalFaction,-4);
            if ((r.Importance >= 5 || r.RefusalCount > 0 || r.CrueltyScore > 20) &&
                Rand.Chance(Mathf.Clamp(.16f + r.Importance*.035f + r.RefusalCount*.08f, .1f,.8f)))
                QueueRetaliation(r, true);
            return false;
        }

        // Compatibility with RimWorld 1.7 prisoner interaction mode storage.
        // In 1.7 the old Pawn_GuestTracker.interactionMode field is no longer
        // available as a public API. Resolve the CURRENT selection via the
        // game's IsInteractionEnabled(PrisonerInteractionModeDef) method if
        // present, rather than referencing the removed field. Reflection is
        // resolved ONCE, invoked only for a hostage dialogue/evidence audit.
        // No work is added to the ordinary pawn or world tick.
        private static readonly System.Reflection.MethodInfo WceModeEnabledApi =
            ResolveWceModeEnabledApi();

        private static System.Reflection.MethodInfo ResolveWceModeEnabledApi()
        {
            const System.Reflection.BindingFlags flags =
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic;
            foreach (System.Reflection.MethodInfo method in typeof(Pawn_GuestTracker).GetMethods(flags))
            {
                if (method.Name != "IsInteractionEnabled" || method.ReturnType != typeof(bool))
                    continue;
                System.Reflection.ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length == 1 &&
                    parameters[0].ParameterType.IsAssignableFrom(typeof(PrisonerInteractionModeDef)))
                    return method;
            }
            return null; // Do NOT infer torture when the actual mode is unknown.
        }

        private static bool IsWceMutilationModeEnabled(Pawn pawn)
        {
            if (pawn?.guest == null || WceModeEnabledApi == null) return false;
            PrisonerInteractionModeDef mode =
                DefDatabase<PrisonerInteractionModeDef>.GetNamedSilentFail("WCE2_Mutilate");
            if (mode == null) return false;
            try
            {
                return WceModeEnabledApi.Invoke(pawn.guest, new object[] { mode }) is bool active && active;
            }
            catch (Exception e)
            {
                if (RHFMod.Settings?.VerboseLogging == true)
                    Log.Warning("[RHF] Cannot read prisoner mutilation mode: " + e.Message);
                return false;
            }
        }

        // Some prisoner interaction mods bypass RecipeWorker.ApplyOnPawn.
        // Infer ONLY when the explicit WCE2 mutilation mode is selected AND
        // a body part newly went missing; a normal combat injury alone is
        // not automatically a crime. Missing proof can be reported manually.
        public static void AuditInteractionMutilation(RHFHostageRecord r)
        {
            if (r?.Pawn == null || RHFMod.Settings?.WCE2Events != true ||
                !IsWceMutilationModeEnabled(r.Pawn)) return;
            int newMissing = MissingParts(r.Pawn) - Math.Max(r.InitialMissing, r.LastAdmittedMissing);
            if (newMissing <= 0) return;
            r.LastAdmittedMissing = MissingParts(r.Pawn);
            // Inference from a selected WCE2 mode is distinct from a proven
            // surgery hook. The record says so, rather than inventing a doctor.
            r.WceOperations = Math.Min(1000, r.WceOperations + 1);
            r.SurgicalCrueltyCount = Math.Min(1000, r.SurgicalCrueltyCount + newMissing);
            int severity = Math.Min(12, 6 + newMissing * 2);
            r.CrueltyScore = Math.Min(500, r.CrueltyScore + severity * newMissing);
            r.LastConfirmedOperation = "Утрата частей тела в режиме увечий пленника (вероятное вмешательство WCE2)";
            r.LastConfirmedOperationSeverity = severity;
            r.LastConfirmedOperationTick = Find.TickManager?.TicksGame ?? 0;
            if (RHFMod.Settings.VerboseLogging)
                Log.Message("[RHF] Prisoner mutilation interaction inferred for " + r.Pawn +
                    "; parts=" + newMissing);
        }

        public static int UnreportedMissing(RHFHostageRecord r)
        {
            return r?.Pawn == null ? 0 : Math.Max(0, MissingParts(r.Pawn) - r.LastReportedMissing);
        }

        public static bool HasNewEvidence(RHFHostageRecord r)
        {
            if (r == null || r.Pawn == null) return false;
            AuditInteractionMutilation(r);
            return UnreportedMissing(r) > 0 ||
                r.WceOperations > r.DisclosedOperations ||
                r.CrueltyScore > r.DisclosedCrueltyScore ||
                (r.InitialMissing > 0 && !r.ReportedInitialInjuries);
        }

        public static bool CanAdmitMutilation(RHFHostageRecord r)
        {
            return r?.Pawn != null &&
                MissingParts(r.Pawn) > Math.Max(r.InitialMissing, r.LastAdmittedMissing);
        }

        // Explicitly self-report deliberate injury when a third-party medical mod
        // bypasses our recipe hooks. No automatic criminal attribution for battle wounds.
        public static string AdmitMutilation(RHFHostageRecord r)
        {
            if (!CanAdmitMutilation(r))
                return "Новых увечий, совершение которых вы можете подтвердить, не найдено.";
            int missing = MissingParts(r.Pawn) - Math.Max(r.InitialMissing, r.LastAdmittedMissing);
            r.LastAdmittedMissing = MissingParts(r.Pawn);
            r.SelfAdmittedMutilations += missing;
            r.CrueltyScore = Math.Min(500, r.CrueltyScore + missing * (10 + r.Importance * 2));
            r.LastConfirmedOperation = "Признанное игроком умышленное увечье (" + missing + " частей тела)";
            r.LastConfirmedOperationSeverity = Math.Min(12, 6 + missing * 2);
            r.LastConfirmedOperationTick = Find.TickManager?.TicksGame ?? 0;
            // ReportCondition applies diplomatic and mood consequences exactly once.
            return ReportCondition(r);
        }

        public static string ReportCondition(RHFHostageRecord r)
        {
            AuditInteractionMutilation(r);
            if (r?.OriginalFaction == null || r.Pawn == null)
                return "Связь прервана: установить личность и фракцию пленника не удалось.";
            int extraMissing = Math.Max(0, MissingParts(r.Pawn) - r.LastReportedMissing);
            int newCrimes = Math.Max(0, r.WceOperations - r.DisclosedOperations);
            int newCruelty = Math.Max(0, r.CrueltyScore - r.DisclosedCrueltyScore);
            if (extraMissing == 0 && newCrimes == 0 && newCruelty == 0 &&
                (r.ReportedInitialInjuries || r.InitialMissing == 0))
                return "«Мы уже получили эти сведения. Если состояние пленника изменится, сообщите отдельно».";
            int requestedPenalty = Math.Min(90, extraMissing * 2 + newCruelty * (3 + r.Importance * 2));
            int goodwillBefore = r.OriginalFaction.GoodwillWith(Faction.OfPlayer);
            int pressureBefore = RHFPoliticalReputation.Pressure(r.OriginalFaction);
            if (requestedPenalty > 0) RHFPoliticalReputation.Change(r.OriginalFaction, -requestedPenalty);
            int actualGoodwill = r.OriginalFaction.GoodwillWith(Faction.OfPlayer) - goodwillBefore;
            int actualPressure = RHFPoliticalReputation.Pressure(r.OriginalFaction) - pressureBefore;
            int moods = 0;
            if (newCruelty > 0)
            {
                RHFPublicityKind kind = newCrimes > 0 ? RHFPublicityKind.Torture :
                    r.ForcedConversion && !r.Enslaved ? RHFPublicityKind.ForcedConversion :
                    r.Enslaved ? RHFPublicityKind.Slavery : RHFPublicityKind.Torture;
                moods = RHFMoralResponse.GivePublicCrueltyThoughts(r.Pawn, newCruelty, r.Importance, kind);
            }
            string accusation = newCrimes >= 3 ?
                "«ТРИ И БОЛЕЕ операции? Мне трудно даже произнести это вслух. Дайте мне секунду. Семья узнает всё. Вы ответите за каждого пострадавшего»." :
                r.LastConfirmedOperationSeverity >= 10 ?
                "«Вы лишили его жизненно важного органа? ... Нет. Я должен сообщить это семье лично. Я передаю показания командованию»." :
                r.LastConfirmedOperationSeverity >= 8 ?
                "«Вы искалечили его намеренно. Я слышу, как его родственник плачет рядом. Мы требуем независимого врача и его возвращения»." :
                "«Вы называете это операцией? Ему причиняли боль нарочно! Я запишу каждое ваше слово для семьи и властей».";
            string crime = newCrimes > 0 ? accusation :
                newCruelty > 0 ? "«Мы услышали вас. Это обращение с пленником будет иметь последствия»." :
                extraMissing > 0 ? "«У него отсутствуют части тела. Мы требуем медицинскую выписку и объяснений»." :
                "«Мы приняли сведения о ранениях. Нам нужно знать, кто их нанёс».";
            string evidence = newCrimes > 0 ? "\nДокументированная операция: " +
                (string.IsNullOrEmpty(r.LastConfirmedOperation) ? "умышленное хирургическое вмешательство" : r.LastConfirmedOperation) +
                ". Зафиксировано вредоносных операций: " + r.SurgicalCrueltyCount + "." :
                extraMissing > 0 ? "\nПосле захвата обнаружены дополнительные отсутствующие части тела; исполнитель операции НЕ подтверждён журналом." :
                (r.InitialMissing > 0 && !r.ReportedInitialInjuries ?
                    "\nПри захвате уже отсутствовали части тела: " + r.InitialMissing +
                    ". Такие травмы НЕ считаются пыткой без подтверждения их происхождения." :
                    "\nНового подтверждённого хирургического вреда нет.");
            string result = "\nИзменение обычной репутации RimWorld: " +
                (actualGoodwill > 0 ? "+" : "") + actualGoodwill + "." +
                (r.OriginalFaction.def.permanentEnemy ?
                    " Постоянно враждебная фракция: goodwill изменить невозможно." :
                    requestedPenalty > 0 && actualGoodwill == 0 ?
                    " Репутация уже на границе шкалы или заблокирована игровыми правилами." : "") +
                "\nИзменение отношения по учёту RHF: " +
                (actualPressure > 0 ? "+" : "") + actualPressure +
                " (отрицательные значения означают большую враждебность; учитываются в переговорах)." +
                "\nДополнительное настроение RHF получили: " + moods + " колонистов.";
            r.DisclosedOperations = r.WceOperations;
            r.DisclosedCrueltyScore = r.CrueltyScore;
            r.LastReportedMissing = MissingParts(r.Pawn);
            r.ReportedInitialInjuries = true;
            if (newCruelty > 0 &&
                Rand.Chance(Mathf.Clamp(.10f + r.Importance*.055f + r.RefusalCount*.08f +
                    newCruelty*.008f, .1f, .92f))) QueueRetaliation(r, true);
            return crime + evidence + result;
        }

        public static void Disclose(RHFHostageRecord r)
        {
            Find.WindowStack.Add(new Dialog_MessageBox(ReportCondition(r)));
        }

        // Three mutually exclusive terminal dispositions. None can be repeated:
        // the hostage is removed from the normal ledger immediately after the choice.
        // Concealed VIPs retain only a tiny separate world event record, not a captive.
        public static string ResolveDeadHostage(RHFHostageRecord r, int answer)
        {
            RHFGameLedger ledger = Current.Game?.GetComponent<RHFGameLedger>();
            if (r == null || ledger == null || !ledger.Hostages.Contains(r) ||
                !IsDeadCase(r) || r.OriginalFaction == null)
                return "Сведения об этом пленнике уже закрыты или недоступны.";
            int now = Find.TickManager.TicksGame;
            string name = string.IsNullOrEmpty(r.NameAtCapture) ?
                (r.Pawn?.LabelShortCap ?? "неизвестный пленник") : r.NameAtCapture;
            Faction faction = r.OriginalFaction;
            bool cannibalism = answer == 2;
            if (cannibalism && !CanConfessCannibalism(r))
                return "Нельзя подтвердить каннибализм: нет записи о разделке или поедании тела пленника.";
            ledger.Hostages.Remove(r);
            r.DeathReportMade = answer != 0;
            r.OfferDeadlineTick = 0;
            r.NextRetaliationTick = 0;
            if (answer == 0)
            {
                // No instant magical knowledge for the other side: keep a sealed case
                // only if a VIP search is believable. It is NOT a negotiable hostage.
                if (r.Importance >= 6 && RHFMod.Settings?.Retaliation == true)
                {
                    var secret = RHFReprisalCase.FromRecord(r, RHFReprisalCause.ConcealedVip, now);
                    ledger.ReprisalCases.Add(secret);
                    return "Вы не отвечаете на вопросы. «Тогда мы найдём его сами», — обрывает разговор представитель " +
                        faction.Name + ". Имя исчезло из списка RHF; о смерти фракции не сообщено. " +
                        "Для возможного поиска VIP оставлена закрытая запись без доступа к переговорам.";
                }
                return "Вы предпочли молчание. Запись «" + name + "» окончательно закрыта в реестре пленников RHF.";
            }
            if (answer == 3) r.PlayerCausedDeath = true; // player's explicit confession
            int severity = Math.Max(r.CrueltyScore,
                r.Executed ? 40 + r.Importance * 7 :
                r.PlayerCausedDeath ? 25 + r.Importance * 5 : 0);
            string effect = "";
            if (answer == 2)
            {
                // Exceptional crime: distinct from the torture memory and significantly
                // escalates diplomatic consequences and guaranteed attempted reprisal.
                severity = Math.Max(severity, 340 + r.Importance * 10);
                RHFPoliticalReputation.Change(faction, -Mathf.Clamp(65 + r.Importance * 5, 0, 100));
                int moods = RHFMoralResponse.GivePublicCrueltyThoughts(r.Pawn, severity,
                    r.Importance, RHFPublicityKind.Cannibalism);
                effect = "\nRHF: злорадство от сообщения о каннибализме получили " + moods + " колонистов.";
                if (RHFMod.Settings?.Retaliation == true)
                    ledger.ReprisalCases.Add(RHFReprisalCase.FromRecord(r, RHFReprisalCause.Cannibalism, now));
                return "«Вы... съели " + name + "? Я передам запись, но дальше говорить не могу»." +
                    "\nФракция " + faction.Name + " получила признание о каннибализме." +
                    "\nПри включённых ответных операциях готовится мощная коалиционная месть." + effect;
            }
            RHFPoliticalReputation.Change(faction,
                -Mathf.Clamp((r.PlayerCausedDeath || r.Executed ? 25 : 6) +
                    r.Importance * (r.PlayerCausedDeath ? 8 : 3) + r.CrueltyScore / 4, 0, 100));
            int rewarded = r.PlayerCausedDeath || r.Executed ?
                RHFMoralResponse.GivePublicCrueltyThoughts(r.Pawn, severity,
                    r.Importance, RHFPublicityKind.Execution) : 0;
            if (RHFMod.Settings?.Retaliation == true && (r.PlayerCausedDeath || r.Executed) && r.Importance >= 3)
                ledger.ReprisalCases.Add(RHFReprisalCase.FromRecord(r, RHFReprisalCause.DisclosedDeath, now));
            return (r.Executed ? "«Вы казнили " : r.PlayerCausedDeath ? "«Вы убили " : "«Значит, умер ") +
                name + "... Мы услышали вас. Теперь эти слова станут официальными»." +
                "\nДело закрыто. RHF-бонус за публичное признание получили " + rewarded + " колонистов.";
        }

        public static bool CanConfessCannibalism(RHFHostageRecord r)
        {
            if (!IsDeadCase(r)) return false;
            // Direct corpse eating is conclusive. Butchery allows an explicit PLAYER
            // confession about subsequent consumption: produced meat/meal stacks lose
            // victim identity in vanilla, so no automatic attribution is invented.
            RHFGameLedger ledger = Current.Game?.GetComponent<RHFGameLedger>();
            return r.BodyConsumedDirectly ||
                (r.BodyButchered && ledger != null &&
                 ledger.LastHumanMeatConsumptionTick >= r.ButcheredTick &&
                 ledger.LastHumanMeatConsumptionTick > 0);
        }

        public static void RecordCorpseButchered(Corpse corpse, Pawn butcher)
        {
            if (RHFMod.Settings?.Enabled != true || corpse?.InnerPawn == null ||
                butcher?.Faction != Faction.OfPlayer) return;
            RHFHostageRecord record = Current.Game?.GetComponent<RHFGameLedger>()?.FindHostage(corpse.InnerPawn);
            if (record == null || !record.Pawn.Dead) return;
            record.BodyButchered = true;
            record.ButcheredTick = Find.TickManager.TicksGame;
        }

        // Human meat and meals lose source identity after butchering/stacking.
        // Track actual consumption globally, then require an explicit statement by
        // the player to link it to this particular butchered captive.
        public static void RecordHumanMeatConsumption(Thing food, Pawn ingester)
        {
            if (RHFMod.Settings?.Enabled != true || food == null ||
                ingester?.Faction != Faction.OfPlayer) return;
            bool humanMeat = string.Equals(food.def?.defName, "Meat_Human",
                StringComparison.OrdinalIgnoreCase);
            if (!humanMeat)
            {
                CompIngredients ingredients = food.TryGetComp<CompIngredients>();
                if (ingredients?.ingredients != null)
                    for (int i = 0; i < ingredients.ingredients.Count; i++)
                        if (ingredients.ingredients[i]?.defName == "Meat_Human")
                        { humanMeat = true; break; }
            }
            if (humanMeat)
            {
                RHFGameLedger ledger = Current.Game?.GetComponent<RHFGameLedger>();
                if (ledger != null) ledger.LastHumanMeatConsumptionTick =
                    Find.TickManager.TicksGame;
            }
        }

        public static void RecordCorpseConsumed(Corpse corpse, Pawn ingester)
        {
            if (RHFMod.Settings?.Enabled != true || corpse?.InnerPawn == null ||
                ingester?.Faction != Faction.OfPlayer) return;
            RHFHostageRecord record = Current.Game?.GetComponent<RHFGameLedger>()?.FindHostage(corpse.InnerPawn);
            if (record != null && record.Pawn.Dead) record.BodyConsumedDirectly = true;
        }

        public static void Refuse(RHFHostageRecord r)
        {
            if (r == null) return;
            r.Refused = true;
            r.RefusalCount++;
            r.LastRejectionTick = Find.TickManager.TicksGame;
            r.OfferDeadlineTick=0;
            r.PendingSilver = 0;
            r.PendingGoodwill = false;
            r.Negotiated = false;
            QueueRetaliation(r);
            Messages.Message(r.OriginalFaction.Name + " восприняла отказ как провокацию.", MessageTypeDefOf.ThreatSmall, false);
        }

        private static void QueueRetaliation(RHFHostageRecord r, bool provoked = false)
        {
            if (RHFMod.Settings?.Retaliation != true || r.RetaliationIssued || r.NextRetaliationTick > 0) return;
            // A direct public humiliation or repeat refusal can prompt a near-term
            // response; otherwise the faction has to assemble an expedition.
            r.NextRetaliationTick = Find.TickManager.TicksGame +
                (provoked ? Rand.Range(15000, 100000) : Rand.Range(100000, 220000));
        }

        public static void UpdateOffers(RHFGameLedger ledger, int tick)
        {
            for (int i = ledger.Hostages.Count - 1; i >= 0; i--)
            {
                RHFHostageRecord r = ledger.Hostages[i];
                if (r == null || (r.Pawn == null && !IsDeadCase(r)) || r.Paid ||
                    (r.Pawn != null && r.Pawn.Map == null && !r.Pawn.IsPrisonerOfColony && !r.Negotiated && !r.Pawn.Dead &&
                     (r.NextRetaliationTick <= 0 || r.RetaliationIssued)))
                {
                    // Discard stale tracking; never alter relationships belonging to other mods.
                    ledger.Hostages.RemoveAt(i);
                    continue;
                }
                if (IsDeadCase(r)) continue; // deceased cases remain for an actual radio decision
                if (r.Pawn.IsSlaveOfColony && !r.Enslaved)
                {
                    r.Enslaved = true;
                    r.CrueltyScore = Math.Min(500, r.CrueltyScore + 5);
                }
                if (r.Pawn.IsPrisonerOfColony && !r.ForcedConversion &&
                    !string.Equals(r.InitialIdeology, r.Pawn.Ideo?.ToString() ?? "", StringComparison.Ordinal))
                {
                    r.ForcedConversion = true;
                    r.CrueltyScore = Math.Min(500, r.CrueltyScore + 4);
                }
                // Ignoring a proposal has consequences; do not instantly attack when
                // an NPC opens the communication dialog.
                if (r.OfferDeadlineTick > 0 && tick >= r.OfferDeadlineTick &&
                    !r.Negotiated && !r.Refused && !r.RetaliationIssued)
                {
                    r.OfferDeadlineTick = 0;
                    QueueRetaliation(r);
                }
                if (!r.Pawn.Dead && r.NextRetaliationTick > 0 && tick >= r.NextRetaliationTick && !r.RetaliationIssued)
                    StartRetaliation(r);
                if (r.InitialOfferMade || r.Negotiated || r.Importance < 3 ||
                    !HostageEligible(r.Pawn) || tick - r.CaptureTick < 60000 ||
                    Rand.Value > (r.Importance >= 7 ? .95f : r.Importance >= 5 ? .85f : .45f)) continue;
                r.InitialOfferMade = true;
                r.OfferDeadlineTick=tick+90000;
                RHFHostageRecord captured = r;
                Find.WindowStack.Add(new Dialog_MessageBox(
                    "Срочное сообщение от " + r.OriginalFaction.Name + ":\n\n" +
                    "Мы требуем возвращения " + r.Pawn.LabelShortCap + ". " +
                    "Готовы выплатить " + SilverPrice(r) + " серебра после безопасного освобождения. " +
                    "За увечья, нанесённые в плену, вы будете отвечать.",
                    "Принять условия", () => { captured.PendingSilver = SilverPrice(captured);
                        captured.Negotiated = true; captured.OfferDeadlineTick=0; },
                    "Отказать", () => Refuse(captured)));
            }
            // Rescue attempts may also be initiated over many ordinary hostages.
            // One bounded count per faction, no global faction/pawn scan.
            var heldByFaction=new Dictionary<Faction,int>();
            for(int i=0;i<ledger.Hostages.Count;i++)
            {
                RHFHostageRecord item=ledger.Hostages[i];
                if(item?.OriginalFaction==null || item.Pawn==null || item.Pawn.Dead ||
                    !item.Pawn.IsPrisonerOfColony || item.Pawn.Map==null)continue;
                int old;
                heldByFaction.TryGetValue(item.OriginalFaction,out old);
                heldByFaction[item.OriginalFaction]=old+1;
            }
            if (RHFMod.Settings?.Retaliation == true)
                foreach (var group in heldByFaction)
                {
                    if (group.Value<6 || !group.Key.HostileTo(Faction.OfPlayer)) continue;
                    for(int i=0;i<ledger.Hostages.Count;i++)
                    {
                        RHFHostageRecord anchor=ledger.Hostages[i];
                        if(anchor?.OriginalFaction!=group.Key || anchor.RetaliationIssued ||
                            anchor.NextRetaliationTick>0 || tick-anchor.CaptureTick<120000)continue;
                        if (Rand.Chance(.22f)) QueueRetaliation(anchor);
                        break;
                    }
                }
        }

        private static void StartRetaliation(RHFHostageRecord r)
        {
            if (r == null || RHFMod.Settings?.Retaliation != true || r.OriginalFaction == null ||
                r.RetaliationIssued || !r.OriginalFaction.HostileTo(Faction.OfPlayer)) return;
            Map map = r.Pawn?.Map;
            if (map == null || !map.IsPlayerHome)
                for (int mi=0; mi<Find.Maps.Count; mi++)
                    if (Find.Maps[mi].IsPlayerHome) { map=Find.Maps[mi]; break; }
            if (map == null) return;
            int now = Find.TickManager.TicksGame;
            RHFGameLedger ledger = Current.Game?.GetComponent<RHFGameLedger>();
            List<Pawn> targets = new List<Pawn>();
            // Up to four sponsoring factions, with additional hired allies possible.
            // VIP captives of three or four distinct factions can trigger one joint raid;
            // ordinary prisoners of each member are included in rescue targets.
            List<Faction> sponsors = new List<Faction> { r.OriginalFaction };
            if (ledger != null)
            {
                for (int i=0;i<ledger.Hostages.Count && sponsors.Count<4;i++)
                {
                    RHFHostageRecord candidate=ledger.Hostages[i];
                    if (candidate?.Pawn==null || candidate.Pawn.Dead ||
                        candidate.Pawn.Map!=map || !candidate.Pawn.IsPrisonerOfColony ||
                        candidate.Importance<5 || candidate.OriginalFaction==null ||
                        !candidate.OriginalFaction.HostileTo(Faction.OfPlayer) ||
                        sponsors.Contains(candidate.OriginalFaction)) continue;
                    sponsors.Add(candidate.OriginalFaction);
                }
                for(int i=0;i<ledger.Hostages.Count && targets.Count<24;i++)
                {
                    RHFHostageRecord candidate=ledger.Hostages[i];
                    if(candidate?.Pawn==null || candidate.Pawn.Dead ||
                       candidate.Pawn.Map!=map || !candidate.Pawn.IsPrisonerOfColony ||
                       !sponsors.Contains(candidate.OriginalFaction)) continue;
                    targets.Add(candidate.Pawn);
                }
            }
            bool revenge = r.Pawn == null || r.Pawn.Dead || r.CrueltyScore >= 28 ||
                (r.Refused && r.Importance >= 6 && Rand.Chance(.50f));
            RHFOperationType type = revenge || targets.Count==0 ||
                RHFMod.Settings.EnableRescueOperations == false ?
                RHFOperationType.Vengeance : RHFOperationType.Rescue;
            if (type == RHFOperationType.Vengeance)
                sponsors.RemoveRange(1, sponsors.Count-1);
            Faction hired = null;
            // Hiring is independent of whether the mercenary faction has captives.
            // No duplicate sponsor is allowed in the mercenary slot.
            if (type == RHFOperationType.Rescue &&
                (r.OriginalFaction.def.techLevel < TechLevel.Industrial ||
                 r.Importance >= 6 || sponsors.Count > 1) && Rand.Chance(.52f))
            {
                foreach (Faction f in Find.FactionManager.AllFactionsListForReading)
                {
                    if (f==null || sponsors.Contains(f) || f.IsPlayer || f.def==null ||
                        f.def.hidden || !f.HostileTo(Faction.OfPlayer) ||
                        f.def.techLevel < TechLevel.Industrial) continue;
                    hired = f; break;
                }
            }
            float threat = StorytellerUtility.DefaultThreatPointsNow(map);
            float bonus = 1.25f + r.Importance * .22f +
                Math.Min(.90f,r.CrueltyScore*.015f) + Math.Min(1.0f,targets.Count*.12f) +
                Mathf.Clamp(-RHFPoliticalReputation.Pressure(r.OriginalFaction)/800f,0f,.6f);
            if (type == RHFOperationType.Vengeance) bonus += .70f;
            float points = Mathf.Clamp(threat * bonus + (type == RHFOperationType.Rescue ? 450f : 200f),
                550f, 15000f) * RHFMod.Settings.InterventionStrength;
            int fireTick = now + 5000; // natural response keeps preparation time
            RHFOperation op = new RHFOperation
            {
                Sponsor = r.OriginalFaction,
                Executor = r.OriginalFaction,
                SecondSponsor = sponsors.Count>1 ? sponsors[1] : null,
                Sponsors = sponsors,
                Type = (int)type,
                Targets = type == RHFOperationType.Rescue ? targets : new List<Pawn>(),
                Vip = r.Importance >= 6 && type == RHFOperationType.Rescue ? r.Pawn : null,
                TryPod = type == RHFOperationType.Rescue && RHFMod.Settings.FieldEvacPod &&
                    r.Importance >= 7 && r.OriginalFaction.def.techLevel >= TechLevel.Industrial &&
                    Rand.Chance(.28f),
                CreatedTick = now, SpawnTick = fireTick,
                ThreatPoints = points, ExpiryTick = fireTick + 180000,
                CombinedStrike = type == RHFOperationType.Rescue &&
                    (sponsors.Count>1 || hired!=null || Rand.Chance(.44f))
            };
            if (type == RHFOperationType.Vengeance)
            {
                Faction attacker = ChooseExecutor(r.OriginalFaction);
                op.Executor = attacker;
                op.Parties.Add(new RHFStrikeParty(attacker,RHFStrikeRole.Vengeance,points,
                    drop: attacker.def.techLevel>=TechLevel.Industrial && Rand.Chance(.35f),
                    siege: attacker.def.techLevel>=TechLevel.Industrial && Rand.Chance(.30f)));
            }
            else
            {
                int partyCount = sponsors.Count + (hired!=null ? 1 : 0);
                bool singleSplit = op.CombinedStrike && partyCount==1;
                if (singleSplit) partyCount=2;
                // Total threat is shared, not multiplied by the number of coalition members.
                points=Mathf.Max(points,350f*partyCount);
                float perParty=Mathf.Clamp(points / partyCount, 350f, 9000f);
                for (int i=0;i<sponsors.Count;i++)
                {
                    Faction member = sponsors[i];
                    bool podAssault=member.def.techLevel>=TechLevel.Industrial &&
                        !op.TryPod && Rand.Chance(.18f);
                    op.Parties.Add(new RHFStrikeParty(member,RHFStrikeRole.Rescue,
                        singleSplit ? points*.62f : perParty,
                        drop: podAssault, breach: !podAssault));
                }
                if (hired != null)
                    op.Parties.Add(new RHFStrikeParty(hired,RHFStrikeRole.Diversion,perParty,
                        drop: hired.def.techLevel>=TechLevel.Industrial && Rand.Chance(.25f)));
                else if (singleSplit)
                    op.Parties.Add(new RHFStrikeParty(r.OriginalFaction,RHFStrikeRole.Diversion,
                        points*.38f));
            }
            RHFOperationController controller=map.GetComponent<RHFOperationController>();
            if (controller == null || !controller.Register(op)) return;
            r.RetaliationIssued=true;
            if (ledger != null)
                for (int i=0;i<ledger.Hostages.Count;i++)
                {
                    RHFHostageRecord member=ledger.Hostages[i];
                    if (member != null && sponsors.Contains(member.OriginalFaction))
                        member.RetaliationIssued=true;
                }
            string intent=type == RHFOperationType.Rescue ?
                "начинает подготовку спасательной операции" :
                "готовит карательный удар по вашей колонии";
            string allies = sponsors.Count>1 ?
                " В коалиции участвуют " + sponsors.Count + " фракции с захваченными представителями." : "";
            string contract = hired!=null ? " Для отвлекающего удара нанята фракция " + hired.Name + "." : "";
            string split = op.CombinedStrike ?
                " Возможны одновременные удары по тюрьме и основным оборонительным позициям." : "";
            Find.LetterStack.ReceiveLetter("Реакция фракций: " + r.OriginalFaction.Name,
                r.OriginalFaction.Name + " " + intent + "." + allies + contract + split,
                LetterDefOf.ThreatBig, r.Pawn);
        }

        private static RaidStrategyDef FindStrategy(params string[] defNames)
        {
            for (int i=0;i<defNames.Length;i++)
            {
                RaidStrategyDef def=DefDatabase<RaidStrategyDef>.GetNamedSilentFail(defNames[i]);
                if (def!=null) return def;
            }
            return RaidStrategyDefOf.ImmediateAttack;
        }

        private static Faction ChooseExecutor(Faction sponsor)
        {
            if (sponsor==null || sponsor.def.techLevel>=TechLevel.Industrial || !Rand.Chance(.42f))
                return sponsor;
            Faction selected=null;
            foreach (Faction f in Find.FactionManager.AllFactionsListForReading)
            {
                if (f==null || f==sponsor || f.IsPlayer || !f.HostileTo(Faction.OfPlayer) ||
                    f.def.techLevel< TechLevel.Industrial || f.leader==null) continue;
                if (selected==null || f.def.techLevel>selected.def.techLevel) selected=f;
            }
            return selected??sponsor;
        }

        public static void OnPrisonerExit(Pawn pawn, Map previousMap, bool wasReleased, IntVec3 exitCell)
        {
            if (!wasReleased || previousMap == null || pawn == null || pawn.Dead) return;
            var ledger = Current.Game?.GetComponent<RHFGameLedger>();
            RHFHostageRecord r = ledger?.FindHostage(pawn);
            if (r == null || r.Paid || r.OriginalFaction == null) return;
            // A standard, unnegotiated prisoner release is also a genuine humane
            // release. Never grant the RHF ransom payment or goodwill twice.
            if (!r.Negotiated)
            {
                RHFMercy.TryAward(r, ordinarySale: false);
                ledger.Hostages.Remove(r);
                return;
            }
            r.Paid = true;
            if (r.PendingGoodwill && r.PendingSilver == 0)
                RHFMercy.TryAward(r, ordinarySale: false);
            int goodwill = GoodwillReward(r);
            int undisclosed = Math.Max(0, r.CrueltyScore - r.DisclosedCrueltyScore);
            // Family discovers concealed cruelty when the released pawn returns.
            // No player mood bonus: they did not publicize it themselves.
            if (undisclosed > 0)
                RHFPoliticalReputation.Change(r.OriginalFaction,
                    -Math.Min(95, undisclosed * (6 + r.Importance * 3)));
            if (r.PendingGoodwill && goodwill > 0)
            {
                int beforeGoodwill = r.OriginalFaction.GoodwillWith(Faction.OfPlayer);
                RHFPoliticalReputation.Change(r.OriginalFaction, goodwill);
                int realGain = r.OriginalFaction.GoodwillWith(Faction.OfPlayer) - beforeGoodwill;
                string noticeGoodwill = realGain > 0 ?
                    "Безвозмездное освобождение " + pawn.LabelShortCap + ": репутация с «" +
                    r.OriginalFaction.Name + "» улучшена на +" + realGain + "." :
                    "Пленник освобождён бесплатно. Обычная репутация не изменилась (постоянная вражда/граница шкалы); внутреннее отношение RHF улучшено.";
                Messages.Message(noticeGoodwill, MessageTypeDefOf.NeutralEvent, true);
            }
            if (r.PendingSilver > 0)
            {
                // Vanilla orbit-trade style delivery, NEVER a loose stack at the
                // prisoner's exit edge. Drops near powered beacons/comms when possible.
                var cargo = new List<Thing>();
                int remaining = r.PendingSilver;
                while (remaining > 0)
                {
                    Thing silver = ThingMaker.MakeThing(ThingDefOf.Silver);
                    int count = Math.Min(remaining, silver.def.stackLimit);
                    silver.stackCount = count;
                    cargo.Add(silver);
                    remaining -= count;
                }
                try
                {
                    IntVec3 dropCenter = DropCellFinder.TradeDropSpot(previousMap);
                    if (!dropCenter.IsValid || !dropCenter.InBounds(previousMap))
                        dropCenter = previousMap.Center;
                    // Creates incoming skyfallers and their ActiveDropPod containers.
                    // Three stable arguments; vanilla decides suitable roof and
                    // arrival handling for its incoming transport pods.
                    DropPodUtility.DropThingsNear(dropCenter, previousMap, cargo);
                    Messages.Message("Выкуп за " + pawn.LabelShortCap + ": " +
                        r.PendingSilver + " серебра доставляется транспортными капсулами. " +
                        "Нажмите для перехода в зону падения.",
                        new LookTargets(dropCenter, previousMap),
                        MessageTypeDefOf.PositiveEvent, true);
                }
                catch (Exception e)
                {
                    Log.Error("[RHF] Failed to send ransom drop pods: " + e);
                    // Never silently claim a paid ransom arrived if drop pod spawning failed.
                    Messages.Message("Ошибка доставки выкупа за " + pawn.LabelShortCap +
                        ". Проверьте журнал ошибок RHF.", MessageTypeDefOf.RejectInput, true);
                    // Unspawned cargo cannot be left free-floating in a world save.
                    foreach (Thing item in cargo)
                        if (item != null && !item.Spawned && !item.Destroyed &&
                            item.ParentHolder == null) item.Destroy(DestroyMode.Vanish);
                }
            }
            ledger.Hostages.Remove(r);
        }

        public static void OnHostageDeath(Pawn victim, bool causedByPlayer, bool execution=false)
        {
            var ledger = Current.Game?.GetComponent<RHFGameLedger>();
            RHFHostageRecord r = ledger?.FindHostage(victim);
            if (r == null || r.OriginalFaction == null || r.Paid) return;
            if (r.DeathTick > 0) return; // one event, even with nested death hooks
            r.DeathTick=Find.TickManager.TicksGame;
            r.Executed=execution;
            r.PlayerCausedDeath = causedByPlayer || execution ||
                (r.LastConfirmedOperationSeverity > 0 &&
                 r.LastConfirmedOperationTick > 0 &&
                 r.DeathTick - r.LastConfirmedOperationTick <= 120);
            r.Negotiated = false;
            r.PendingSilver = 0;
            r.PendingGoodwill = false;
            r.OfferDeadlineTick = 0;
            // Death is NOT magically announced to another faction. Player chooses
            // disclosure or concealment in a later, actual comms dialogue.
            r.NextRetaliationTick = 0;
            // No immediate RHF mood: enjoyment comes from telling the family/faction.
        }

        public static void RecordWceOperation(Pawn victim, Pawn doctor, string recipe,
            RHFHostageRecord originalRecord = null, int severityOverride = 0, string label = null)
        {
            if (RHFMod.Settings?.Enabled != true || victim == null || doctor == null ||
                doctor.Faction != Faction.OfPlayer) return;
            // Lethal surgeries can kill the pawn before the WCE postfix executes.
            RHFHostageRecord r = originalRecord ??
                Current.Game?.GetComponent<RHFGameLedger>()?.TrackHostage(victim);
            if (r == null) return;
            int severity = severityOverride > 0 ? severityOverride : RHFWCERegistry.SeverityOf(recipe);
            if (severity <= 0) return; // Removing a torture implant is not another torture event.
            r.WceOperations = Math.Min(1000, r.WceOperations + 1);
            float pain = victim.health?.hediffSet?.PainTotal ?? 0f;
            int painBonus = Mathf.Clamp(Mathf.CeilToInt(pain * 5f), 0, 6);
            // Convex escalation: repeat operations on one victim become worse than
            // their arithmetic sum, yet values saturate for savegame and TPS safety.
            double multiplier = Math.Min(5.0, Math.Pow(1.13, Math.Min(14, r.WceOperations - 1)));
            int delta = (int)Math.Ceiling((severity + painBonus) * multiplier);
            r.CrueltyScore = Math.Min(500, r.CrueltyScore + delta);
            r.SurgicalCrueltyCount = Math.Min(1000, r.SurgicalCrueltyCount + 1);
            // An actual recipe hook takes precedence over the contextual Mutilate audit.
            // Do not count the same severed limb a second time on the next dialogue.
            r.LastAdmittedMissing = Math.Max(r.LastAdmittedMissing, MissingParts(victim));
            r.LastConfirmedOperation = string.IsNullOrEmpty(label) ? recipe : label;
            r.LastConfirmedOperationSeverity = severity;
            r.LastConfirmedOperationTick = Find.TickManager?.TicksGame ?? 0;
            if (RHFMod.Settings.VerboseLogging)
                Log.Message("[RHF][Surgery] Recorded " + r.LastConfirmedOperation +
                    " for " + victim.LabelShortCap + "; severity " + severity +
                    "; score " + r.CrueltyScore);
            // Do not grant mood until public disclosure. WCE2's *own* immediate
            // Ideology thoughts remain WCE2-owned and are not patched out.

        }
    }

    public enum RHFPublicityKind { Torture, Execution, Cannibalism, Slavery, ForcedConversion }

    // One-off psychological cruelty: the reward is for deliberately TELLING the
    // victim's relatives/faction about suffering, never for surgery itself.
    // Vanilla and WCE2 immediate surgery thoughts are not modified.
    public static class RHFMoralResponse
    {
        // Character and ideology are independent paths. A pawn with both still
        // receives just ONE RHF memory (the greatest current level replaces prior).
        public static string EligibleTraitReason(Pawn colonist, RHFPublicityKind kind)
        {
            if (colonist?.story?.traits == null) return null;
            bool violence = kind == RHFPublicityKind.Torture ||
                kind == RHFPublicityKind.Execution || kind == RHFPublicityKind.Cannibalism;
            bool humiliation = violence || kind == RHFPublicityKind.Slavery ||
                kind == RHFPublicityKind.ForcedConversion;
            foreach (Trait trait in colonist.story.traits.allTraits)
            {
                string n = trait?.def?.defName ?? "";
                if (violence && n.Equals("Bloodlust", StringComparison.OrdinalIgnoreCase))
                    return "Кровожадность";
                if (humiliation && n.Equals("Psychopath", StringComparison.OrdinalIgnoreCase))
                    return "Психопат";
                // Optional third-party sadism traits. These are only matched when
                // the exact TraitDef actually exists on this pawn. No dependency on
                // any particular character or ideology mod is introduced.
                if (humiliation && (n.Equals("Sadist", StringComparison.OrdinalIgnoreCase) ||
                    n.Equals("Sadistic", StringComparison.OrdinalIgnoreCase) ||
                    n.Equals("Torturer", StringComparison.OrdinalIgnoreCase) ||
                    n.EndsWith("_Sadist", StringComparison.OrdinalIgnoreCase) ||
                    n.EndsWith("_Sadistic", StringComparison.OrdinalIgnoreCase)))
                    return "Садизм (модовая черта: " + trait.def.defName + ")";
                if (kind == RHFPublicityKind.Cannibalism &&
                    n.Equals("Cannibal", StringComparison.OrdinalIgnoreCase))
                    return "Каннибал";
            }
            return null;
        }

        public static bool EligibleIdeology(Pawn colonist, Pawn victim, RHFPublicityKind kind)
        {
            if (!ModsConfig.IdeologyActive || colonist?.Ideo == null) return false;
            foreach (Precept precept in colonist.Ideo.PreceptsListForReading)
            {
                string n = precept?.def?.defName?.ToLowerInvariant() ?? "";
                if (kind == RHFPublicityKind.Torture &&
                    (n == "wce2_torture_alwaysrespected" ||
                    (n == "wce2_torture_respectedguilty" && victim?.guilt?.IsGuilty == true)))
                    return true;
                if (kind == RHFPublicityKind.Execution &&
                    (n.Contains("execution_required") || n.Contains("execution_respected")))
                    return true;
                if (kind == RHFPublicityKind.Cannibalism &&
                    (n.Contains("cannibalism_required") || n.Contains("cannibalism_respected")))
                    return true;
                if (kind == RHFPublicityKind.Slavery &&
                    (n.Contains("slavery_honorable") || n.Contains("slavery_required")))
                    return true;
                if (kind == RHFPublicityKind.ForcedConversion &&
                    (n.Contains("conversion_respected") || n.Contains("proselytizing_required")))
                    return true;
            }
            return false;
        }

        public static int MemoryLevel(int newCruelty, int importance)
        {
            // Prior logarithmic formula saturated at tier 6 after a single limb
            // removal on a VIP. Deliberate bounded thresholds preserve differences.
            float weight = Mathf.Max(0, newCruelty) * (1f +
                Mathf.Clamp(importance, 0, 8) * 0.20f);
            if (weight < 18f) return 1;
            if (weight < 40f) return 2;
            if (weight < 85f) return 3;
            if (weight < 170f) return 4;
            if (weight < 310f) return 5;
            return 6;
        }

        public static int GivePublicCrueltyThoughts(Pawn victim, int newCruelty, int importance,
            RHFPublicityKind kind = RHFPublicityKind.Torture)
        {
            if (newCruelty <= 0 || RHFMod.Settings?.Enabled != true) return 0;
            // No per-tick work: this is called only from a completed radio report.
            ThoughtDef memory = DefDatabase<ThoughtDef>.GetNamedSilentFail(
                "RHF_PublicCruelty" + MemoryLevel(newCruelty, importance));
            if (memory == null) return 0;
            int awarded = 0;
            foreach (Map map in Find.Maps)
            {
                if (!map.IsPlayerHome) continue;
                foreach (Pawn p in map.mapPawns.FreeColonistsSpawned)
                {
                    if (p?.needs?.mood == null) continue;
                    if (EligibleTraitReason(p, kind) == null && !EligibleIdeology(p, victim, kind))
                        continue;
                    for (int old = 1; old <= 6; old++)
                    {
                        ThoughtDef previous = DefDatabase<ThoughtDef>.GetNamedSilentFail("RHF_PublicCruelty" + old);
                        if (previous != null)
                            p.needs.mood.thoughts.memories.RemoveMemoriesOfDef(previous);
                    }
                    p.needs.mood.thoughts.memories.TryGainMemory(memory);
                    awarded++;
                }
            }
            return awarded;
        }
    }
}
