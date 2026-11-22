// v0.10: persisted, finite cases for concealed VIP disappearances and public atrocities.
// Unlike ordinary hostage records these contain NO negotiable prisoner or family list.
// All expensive work is event-based or once each 30,000 ticks.
using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RaidersHaveFamilies
{
    public enum RHFReprisalCause { ConcealedVip = 1, Cannibalism = 2, DisclosedDeath = 3 }

    public sealed class RHFReprisalCase : IExposable
    {
        public string Key = "";
        public string VictimName = "";
        public string VictimId = "";
        public IntVec3 SearchCell = IntVec3.Invalid;
        public Faction Faction;
        public int Importance;
        public int Cruelty;
        public int Cause;
        // 0 pending search/revenge, 1 search registered, 5 search deployed,
        // 2 disappearance confirmed, 3 revenge registered, 4 revenge deployed.
        public int Stage;
        public int DueTick;
        public int CreatedTick;
        public int LastTryTick;
        public int FailedAttempts;

        public RHFReprisalCase() { }

        public static RHFReprisalCase FromRecord(RHFHostageRecord r, RHFReprisalCause kind, int now)
        {
            return new RHFReprisalCase {
                Key = (r.Pawn?.ThingID ?? "unknown") + "-" + now + "-" + (int)kind,
                VictimId = r.Pawn?.ThingID ?? "",
                SearchCell = r.LastHeldCell,
                VictimName = string.IsNullOrEmpty(r.NameAtCapture) ?
                    r.Pawn?.LabelShortCap ?? "неизвестный" : r.NameAtCapture,
                Faction = r.OriginalFaction, Importance = r.Importance,
                Cruelty = r.CrueltyScore, Cause = (int)kind,
                CreatedTick = now,
                DueTick = kind == RHFReprisalCause.ConcealedVip ? now : now + Rand.Range(20000, 90000)
            };
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref Key, "key", "");
            Scribe_Values.Look(ref VictimName, "victimName", "");
            Scribe_Values.Look(ref VictimId, "victimId", "");
            Scribe_Values.Look(ref SearchCell, "searchCell", IntVec3.Invalid);
            Scribe_References.Look(ref Faction, "faction");
            Scribe_Values.Look(ref Importance, "importance", 0);
            Scribe_Values.Look(ref Cruelty, "cruelty", 0);
            Scribe_Values.Look(ref Cause, "cause", 0);
            Scribe_Values.Look(ref Stage, "stage", 0);
            Scribe_Values.Look(ref DueTick, "dueTick", 0);
            Scribe_Values.Look(ref CreatedTick, "createdTick", 0);
            Scribe_Values.Look(ref LastTryTick, "lastTryTick", 0);
            Scribe_Values.Look(ref FailedAttempts, "failedAttempts", 0);
        }
    }

    public static class RHFReprisalSystem
    {
        private const int MaxCases = 128;

        private static Map HomeMap()
        {
            if (Find.Maps == null) return null;
            for (int i = 0; i < Find.Maps.Count; i++)
                if (Find.Maps[i].IsPlayerHome) return Find.Maps[i];
            return null;
        }

        public static void Update(RHFGameLedger ledger, int now)
        {
            if (ledger?.ReprisalCases == null || RHFMod.Settings?.Retaliation != true) return;
            for (int i = ledger.ReprisalCases.Count - 1; i >= 0; i--)
            {
                RHFReprisalCase c = ledger.ReprisalCases[i];
                if (c == null || c.Faction == null || c.Stage == 4)
                { ledger.ReprisalCases.RemoveAt(i); continue; }
                if (c.Stage == 5 && now - c.LastTryTick > 18000)
                {
                    // Real raid deployed but no reachable prison cell / no squad;
                    // the expedition still reports the missing prisoner.
                    ConfirmDisappearance(c, now);
                }
                if (c.Stage == 1 || c.Stage == 3 || c.Stage == 5 || now < c.DueTick) continue;
                // Bounded retries on failed generation or a temporarily full controller;
                // never claim that an actual raid happened merely because it was planned.
                if (now - c.LastTryTick < 10000) continue;
                c.LastTryTick = now;
                Map map = HomeMap();
                if (map == null) continue;
                bool rescue = c.Cause == (int)RHFReprisalCause.ConcealedVip && c.Stage == 0;
                if (!Schedule(c, map, now, rescue))
                {
                    c.FailedAttempts++;
                    if (c.FailedAttempts == 3)
                        Log.Warning("[RHF] Не удалось начать отряд для дела " + c.VictimName +
                            ". Фракция или слот операций недоступны; будут новые попытки.");
                }
            }
            if (ledger.ReprisalCases.Count > MaxCases)
            {
                // Defensive bound: drop only completed cases, never pending atrocities.
                ledger.ReprisalCases.RemoveAll(c => c == null || c.Stage == 4);
            }
        }

        private static bool Schedule(RHFReprisalCase c, Map map, int now, bool search)
        {
            RHFOperationController controller = map.GetComponent<RHFOperationController>();
            if (controller == null || c.Faction == null) return false;
            if (search && !string.IsNullOrEmpty(c.VictimId))
            {
                RHFOperation existing = controller.FindRescueFor(c.VictimId);
                if (existing != null)
                {
                    c.Stage = 1;
                    existing.CaseKeyV10 = c.Key;
                    existing.CaseSearchCellV10 = c.SearchCell;
                    if (existing.Deployed && existing.SuccessfulParties > 0)
                        OnOperationDeployed(existing);
                    return true;
                }
            }
            List<Faction> participants = new List<Faction>();
            if (c.Faction.HostileTo(Faction.OfPlayer)) participants.Add(c.Faction);
            // Factions not currently hostile need an actual hostile executor: vanilla
            // RaidEnemy will not magically turn their ordinary visitors into enemies.
            List<Faction> candidates = new List<Faction>();
            foreach (Faction other in Find.FactionManager.AllFactionsListForReading)
            {
                if (other == null || other == c.Faction || other.IsPlayer ||
                    other.def == null || other.def.hidden ||
                    !other.HostileTo(Faction.OfPlayer) ||
                    other.def.techLevel < TechLevel.Industrial) continue;
                // Real cooperation: allied wings must not treat each other as enemies.
                // Select compatible factions at generation time instead of patching
                // Faction.HostileTo globally (which would damage TPS/world diplomacy).
                if (other.HostileTo(c.Faction) || c.Faction.HostileTo(other)) continue;
                candidates.Add(other);
            }
            if (participants.Count == 0 && candidates.Count > 0)
            { participants.Add(candidates[0]); candidates.RemoveAt(0); }
            if (participants.Count == 0) return false;
            float normal = StorytellerUtility.DefaultThreatPointsNow(map);
            bool catastrophic = c.Cause == (int)RHFReprisalCause.Cannibalism ||
                (c.Cause == (int)RHFReprisalCause.ConcealedVip && !search);
            float multiplier = catastrophic ? 2.1f + c.Importance * 0.28f :
                search ? 1.05f + c.Importance * 0.12f : 1.35f + c.Importance * 0.18f;
            float points = Mathf.Clamp((normal * multiplier + (catastrophic ? 900f : 300f)) *
                RHFMod.Settings.InterventionStrength, 550f, 15000f);
            RHFOperation operation = new RHFOperation {
                Sponsor = c.Faction, Executor = participants[0],
                Sponsors = new List<Faction>{ c.Faction },
                Type = (int)(search ? RHFOperationType.Rescue : RHFOperationType.Vengeance),
                Targets = new List<Pawn>(),
                CombinedStrike = !search,
                Doctrine=(int)(search ? RHFDoctrine.PrisonBreakout :
                    catastrophic ? RHFDoctrine.SiegeAndRaze : RHFDoctrine.PunitiveAssault),
                SourceImportance=c.Importance,
                ThreatPoints = points,
                CaseKeyV10 = c.Key,
                CaseSearchCellV10 = search ? c.SearchCell : IntVec3.Invalid,
                CreatedTick = now, SpawnTick = now + 1200,
                ExpiryTick = now + 90000
            };
            RHFMissionProfiles.ConfigureCase(operation,c,search);
            if (search)
            {
                operation.Parties.Add(new RHFStrikeParty(participants[0], RHFStrikeRole.Rescue, points,
                    breach: true));
            }
            else
            {
                int hires = catastrophic ? 2 : 1;
                for (int i=0; i<candidates.Count && hires>0; i++)
                {
                    Faction mercenary = candidates[i];
                    bool compatible = true;
                    for (int j=0; j<participants.Count; j++)
                        if (participants[j].HostileTo(mercenary) ||
                            mercenary.HostileTo(participants[j]))
                        { compatible = false; break; }
                    if (!compatible) continue;
                    participants.Add(mercenary);
                    hires--;
                }
                // When no actual hired faction exists, keep a second physical wing
                // of the attacking faction, but never mislabel it as hired mercenaries.
                if (participants.Count == 1 && catastrophic) participants.Add(participants[0]);
                float perParty = Mathf.Max(400f, points / participants.Count);
                for (int i=0;i<participants.Count;i++)
                {
                    Faction member = participants[i];
                    RHFStrikeRole role = i == 0 ? RHFStrikeRole.Vengeance : RHFStrikeRole.Diversion;
                    operation.Parties.Add(new RHFStrikeParty(member, role, perParty,
                        drop: member.def.techLevel >= TechLevel.Industrial && i > 0,
                        siege: member.def.techLevel >= TechLevel.Industrial && i == 0));
                }
            }
            if (!controller.Register(operation)) return false;
            c.Stage = search ? 1 : 3;
            // The op is scheduled for a FUTURE spawn tick. No raid letter here:
            // the single RHF briefing is attached to actual vanilla arrival.
            // Prematurely issuing a second threat letter incorrectly implied
            // there were two separate raids.
            Log.Message("[RHF] Reprisal scheduled: " +
                (search ? "search" : "vengeance") + ", participants=" +
                participants.Count + ", spawn tick=" + operation.SpawnTick);
            return true;
        }

        public static void OnOperationDeployed(RHFOperation operation)
        {
            if (operation == null || string.IsNullOrEmpty(operation.CaseKeyV10)) return;
            RHFGameLedger ledger = Current.Game?.GetComponent<RHFGameLedger>();
            if (ledger?.ReprisalCases == null) return;
            foreach (RHFReprisalCase c in ledger.ReprisalCases)
            {
                if (c == null || c.Key != operation.CaseKeyV10) continue;
                if (c.Stage == 1)
                {
                    c.Stage = 5; // Searchers have arrived; let them approach the prison.
                    c.FailedAttempts = 0;
                    c.LastTryTick = Find.TickManager.TicksGame;
                }
                else if (c.Stage == 3) c.Stage = 4;
                return;
            }
        }

        // Only after boots-on-ground inspection (or a bounded failed search)
        // does the hidden death become known to the enemy command.
        public static void OnSearchFailed(RHFOperation operation)
        {
            if (operation == null || string.IsNullOrEmpty(operation.CaseKeyV10)) return;
            RHFGameLedger ledger = Current.Game?.GetComponent<RHFGameLedger>();
            if (ledger?.ReprisalCases == null) return;
            foreach (RHFReprisalCase c in ledger.ReprisalCases)
                if (c != null && c.Key == operation.CaseKeyV10 && c.Stage == 5)
                { ConfirmDisappearance(c, Find.TickManager.TicksGame); return; }
        }

        private static void ConfirmDisappearance(RHFReprisalCase c, int now)
        {
            if (c.Stage != 5) return;
            c.Stage = 2;
            c.FailedAttempts = 0;
            // <= 3 in-game days even after the next 30,000-tick ledger update.
            c.DueTick = now + Rand.Range(15000, 135000);
            Map map = HomeMap();
            if (map == null) return;
            Find.LetterStack.ReceiveLetter("RHF: пропажа важного пленника обнаружена",
                "Поисковая группа фракции " + c.Faction.Name +
                " прибыла за «" + c.VictimName + "», но пленника в указанном месте нет. " +
                "Сообщение о смерти было скрыто. Командование получило донесение " +
                "и в ближайшие трое суток подготовит усиленную коалиционную месть.",
                LetterDefOf.ThreatBig, new LookTargets(map.Center, map));
        }

        public static void OnOperationFailed(RHFOperation operation)
        {
            if (operation == null || string.IsNullOrEmpty(operation.CaseKeyV10)) return;
            RHFGameLedger ledger = Current.Game?.GetComponent<RHFGameLedger>();
            if (ledger?.ReprisalCases == null) return;
            foreach (RHFReprisalCase c in ledger.ReprisalCases)
                if (c != null && c.Key == operation.CaseKeyV10)
                {
                    if (c.Stage == 1) c.Stage = 0;
                    if (c.Stage == 3) c.Stage = c.Cause == (int)RHFReprisalCause.ConcealedVip ? 2 : 0;
                    c.FailedAttempts++;
                    return;
                }
        }
    }
}
