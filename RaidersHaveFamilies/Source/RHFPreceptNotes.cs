// RimWorld 1.7 Ideology integration: add RHF effects to the *real* Mood section
// of Precept.GetTip(). Precept.GetTip groups PreceptComp_SelfTookMemoryThought
// instances in that section; editing PreceptDef.description cannot do that.
//
// These components describe existing RHF memories. They never grant a memory:
// the actual one-off grant happens in RHFMoralResponse/RHFMercy after the event.
// No tick hooks, no duplicate WCE2 thought grants, no dependency on WCE2 DLLs.
using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RaidersHaveFamilies
{
    // Precept.GetTip() in RimWorld 1.7 explicitly recognizes the vanilla
    // PreceptComp_SelfTookMemoryThought type as a Mood entry. We subclass it
    // for UI-only descriptions, and disable its normal event grant mechanism.
    // That prevents any extra thought from being awarded merely for having
    // the precept or for an unrelated vanilla HistoryEvent.
    public sealed class RHFPreceptMoodEntry : PreceptComp_SelfTookMemoryThought
    {
        public string RHFDescription;

        public override IEnumerable<string> GetDescriptions()
        {
            if (!string.IsNullOrEmpty(RHFDescription))
                yield return RHFDescription;
        }

        public override void Notify_MemberTookAction(HistoryEvent ev, Precept precept,
            bool canApplySelfTookThoughts)
        {
            // Intentionally empty: RHFMoralResponse/RHFMercy perform the actual
            // event-driven grant. This component only advertises that reward
            // in the game's native Mood list.
        }
    }

    [StaticConstructorOnStartup]
    public static class RHFPreceptNotes
    {
        private const string Marker = "[Raiders Have Families]";

        static RHFPreceptNotes()
        {
            if (!ModsConfig.IdeologyActive) return;
            try
            {
                foreach (PreceptDef def in DefDatabase<PreceptDef>.AllDefsListForReading)
                {
                    if (def == null || string.IsNullOrEmpty(def.defName)) continue;
                    string name = def.defName;
                    if (name.Equals("WCE2_Torture_AlwaysRespected", StringComparison.OrdinalIgnoreCase))
                    {
                        AddMoodEntry(def, "RHF_PublicCruelty1",
                            "RHF — Обнародованы мучения пленника его родным/фракции: " +
                            "+2 / +4 / +8 / +16 / +32 / +48 на 5 дней " +
                            "(тяжесть подтверждённых издевательств × статус пленника; " +
                            "только после первого сообщения, не за саму операцию)");
                        SetShortDescription(def,
                            "Дополнительное воспоминание RHF выдаётся после раскрытия мучений. " +
                            "Черты «Кровожадность», «Психопат» и поддерживаемый «Садист» " +
                            "тоже дают право на этот бонус без принципа; эффекты не суммируются. " +
                            "Бонус WCE2 за саму пытку действует отдельно.");
                    }
                    else if (name.Equals("WCE2_Torture_RespectedGuilty", StringComparison.OrdinalIgnoreCase))
                    {
                        AddMoodEntry(def, "RHF_PublicCruelty1",
                            "RHF — Обнародованы мучения ВИНОВНОГО пленника: " +
                            "+2 / +4 / +8 / +16 / +32 / +48 на 5 дней " +
                            "(тяжесть и статус; после первого сообщения родным или фракции; " +
                            "принцип работает только при виновности жертвы)");
                        SetShortDescription(def,
                            "Дополнительное воспоминание RHF возникает при сообщении о мучениях " +
                            "виновного пленника. Подходящие черты характера действуют независимо " +
                            "от принципа. Эффекты WCE2 за саму пытку остаются отдельными.");
                    }
                    else if (name.StartsWith("Execution_", StringComparison.OrdinalIgnoreCase) &&
                        (Contains(name, "Required") || Contains(name, "Respected") ||
                         Contains(name, "Honorable")))
                    {
                        AddMoodEntry(def, "RHF_PublicCruelty1",
                            "RHF — Признание казни пленника его семье/фракции: " +
                            "+2 / +4 / +8 / +16 / +32 / +48 на 5 дней " +
                            "(зависит от значимости жертвы; не за саму казнь)");
                        SetShortDescription(def,
                            "Отдельное воспоминание за сознательное раскрытие казни. " +
                            "Ванильные эффекты самого исполнения приговора не заменяются.");
                    }
                    else if (name.StartsWith("Cannibalism_", StringComparison.OrdinalIgnoreCase) &&
                        (Contains(name, "Abhorrent") || Contains(name,"Horrible") || Contains(name,"Disapproved")))
                    {
                        AddMoodEntry(def, "RHF_PublicCannibalismDisgust",
                            "RHF — Нам рассказали о каннибализме пленника: -8 на 5 дней " +
                            "(только при раскрытии случая, не за обычное питание)");
                        SetShortDescription(def,
                            "Отрицательное воспоминание за сознательное обнародование " +
                            "каннибализма колонистами; эффекты самого употребления мяса остаются отдельными.");
                    }
                    else if (name.StartsWith("Cannibalism_", StringComparison.OrdinalIgnoreCase) &&
                        (Contains(name, "Required") || Contains(name, "Respected")))
                    {
                        AddMoodEntry(def, "RHF_PublicCruelty6",
                            "RHF — Обнародован подтверждённый каннибализм с пленником: " +
                            "+48 на 5 дней (только при сознательном признании его фракции; " +
                            "после подтверждения разделки/употребления; не за обычное питание)");
                        SetShortDescription(def,
                            "Дополнительное воспоминание RHF за признание в каннибализме. " +
                            "Обычные бонусы Ideology за употребление человеческого мяса " +
                            "остаются отдельными. Признание вызывает попытку карательного рейда.");
                    }
                    else if ((name.StartsWith("Charity_", StringComparison.OrdinalIgnoreCase) ||
                              name.StartsWith("Mercy_", StringComparison.OrdinalIgnoreCase)) &&
                             (Contains(name, "Worthwhile") || Contains(name, "Important") ||
                              Contains(name, "Essential") || Contains(name, "Respected") ||
                              Contains(name, "Required") || Contains(name, "Accepted")))
                    {
                        int tier = Contains(name, "Essential") || Contains(name, "Required") ? 3 :
                                   Contains(name, "Important") || Contains(name, "Respected") ? 2 : 1;
                        int release = tier == 3 ? 9 : tier == 2 ? 6 : 3;
                        int sale = tier == 3 ? 3 : tier == 2 ? 2 : 1;
                        AddMoodEntry(def, "RHF_MercyFreedom" + tier,
                            "RHF — Гуманное освобождение пленника без выкупа: +" +
                            release + " на 5 дней (после фактического освобождения)");
                        AddMoodEntry(def, "RHF_MercyTrade" + tier,
                            "RHF — Гуманная обычная продажа пленника: +" +
                            sale + " на 5 дней (не выкуп)");
                        SetShortDescription(def,
                            "Оба эффекта RHF требуют отсутствия пыток, рабства, увечий " +
                            "и принудительного обращения. Обычные эффекты Ideology не заменяются.");
                    }
                }
            }
            catch (Exception e)
            {
                Log.Warning("[Raiders Have Families] Не удалось добавить RHF-эффекты в настроение принципов: " + e);
            }
        }

        private static bool Contains(string text, string part)
        {
            return text.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void AddMoodEntry(PreceptDef def, string thoughtName, string text)
        {
            ThoughtDef thought = DefDatabase<ThoughtDef>.GetNamedSilentFail(thoughtName);
            if (thought == null || thought.stages == null || thought.stages.Count == 0 ||
                thought.stages[0].baseMoodEffect == 0f)
            {
                Log.Warning("[RHF] Не найдено реальное воспоминание для отображения в Ideology: " + thoughtName);
                return;
            }
            if (def.comps == null) def.comps = new List<PreceptComp>();
            foreach (PreceptComp existing in def.comps)
            {
                if (existing is RHFPreceptMoodEntry entry && entry.thought == thought)
                    return; // Idempotent across startup/reload hooks.
            }
            def.comps.Add(new RHFPreceptMoodEntry
            {
                preceptDef = def,
                thought = thought,
                RHFDescription = text
            });
        }

        private static void SetShortDescription(PreceptDef def, string note)
        {
            if (string.IsNullOrEmpty(note) ||
                (def.description != null && def.description.Contains(Marker))) return;
            def.description = (def.description ?? "").TrimEnd() + "\n\n" + Marker + " " + note;
        }
    }
}
