// RimWorld 1.7: explain existing Raiders Have Families effects directly in Ideology editor.
// This is documentation-only UI augmentation: no extra thoughts, no per-tick work,
// no replacement of WCE2/VANILLA precepts, no dependency on WCE2 assemblies.
using System;
using RimWorld;
using Verse;

namespace RaidersHaveFamilies
{
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
                    string info = null;
                    if (name.Equals("WCE2_Torture_AlwaysRespected", StringComparison.OrdinalIgnoreCase))
                    {
                        info = "Настроение (RHF): +2 / +4 / +8 / +16 / +32 / +48 на 5 дней " +
                            "ТОЛЬКО после публичного раскрытия подтверждённых мучений.\n" +
                            "[Дополнительный эффект RHF] После того как через консоль связи фракции " +
                            "сообщено о подтверждённых пытках её пленника, колонисты с этим принципом " +
                            "получают ОДНО воспоминание +2 / +4 / +8 / +16 / +32 / +48 к настроению " +
                            "на 5 дней. Сила зависит от зафиксированной жестокости и важности пленника. " +
                            "Бонусы от War Crimes Expanded 2 (+5 и социальные эффекты) действуют отдельно. " +
                            "Без сообщения о пытках бонус RHF не выдаётся; боевые ранения сами по себе не считаются пыткой.";
                    }
                    else if (name.Equals("WCE2_Torture_RespectedGuilty", StringComparison.OrdinalIgnoreCase))
                    {
                        info = "Настроение (RHF): +2 / +4 / +8 / +16 / +32 / +48 на 5 дней " +
                            "после раскрытия пыток ВИНОВНОГО пленника.\n" +
                            "[Дополнительный эффект RHF] За сообщение фракции через консоль " +
                            "о подтверждённых пытках пленника: одно воспоминание +2 / +4 / +8 / +16 / +32 / +48 " +
                            "к настроению на 5 дней — ТОЛЬКО если игра считает жертву виновной " +
                            "на момент сообщения. Сила зависит от жестокости и статуса жертвы. " +
                            "Эффекты WCE2 действуют отдельно. Без публичного сообщения бонуса нет.";
                    }
                    else if (name.StartsWith("Cannibalism_", StringComparison.OrdinalIgnoreCase) &&
                        (name.IndexOf("Required", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         name.IndexOf("Respected", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        // This is intentionally NOT advertised as working: v0.6.0 has
                        // no event that calls RHFMoralResponse with Cannibalism.
                        info = "[Состояние интеграции RHF] В этом релизе дополнительные " +
                            "воспоминания за сообщение родственникам о поедании пленника ЕЩЁ НЕ РЕАЛИЗОВАНЫ. " +
                            "Показанные выше бонусы каннибализма принадлежат обычной Ideology. " +
                            "Это пояснение не меняет настроение и не создаёт дополнительных эффектов.";
                    }
                    else if ((name.StartsWith("Charity_", StringComparison.OrdinalIgnoreCase) ||
                              name.StartsWith("Mercy_", StringComparison.OrdinalIgnoreCase)) &&
                             (name.IndexOf("Worthwhile", StringComparison.OrdinalIgnoreCase) >= 0 ||
                              name.IndexOf("Important", StringComparison.OrdinalIgnoreCase) >= 0 ||
                              name.IndexOf("Essential", StringComparison.OrdinalIgnoreCase) >= 0 ||
                              name.IndexOf("Respected", StringComparison.OrdinalIgnoreCase) >= 0 ||
                              name.IndexOf("Required", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        int tier = name.IndexOf("Essential", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                   name.IndexOf("Required", StringComparison.OrdinalIgnoreCase) >= 0 ? 3 :
                                   name.IndexOf("Important", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                   name.IndexOf("Respected", StringComparison.OrdinalIgnoreCase) >= 0 ? 2 : 1;
                        int freeBonus = tier == 3 ? 9 : tier == 2 ? 6 : 3;
                        int saleBonus = tier == 3 ? 3 : tier == 2 ? 2 : 1;
                        info = "Настроение RHF: милосердное освобождение +" + freeBonus +
                            ", гуманная обычная продажа +" + saleBonus + " на 5 дней.\n" +
                            "[Дополнительный эффект RHF] Только для пленника, которого НЕ пытали, " +
                            "не держали рабом, не обращали принудительно в другую идеологию " +
                            "и не калечили после захвата. Бонус получают верующие " +
                            "в этот принцип после фактического ухода пленника или обычной продажи. " +
                            "Выкуп за серебро — НЕ благотворительность и бонуса не даёт. " +
                            "Новое воспоминание заменяет предыдущее и не складывается бесконечно.";
                    }
                    if (info == null || (def.description != null && def.description.Contains(Marker)))
                        continue;
                    // Static initialization, once per game boot; original text stays intact.
                    def.description = (def.description ?? "").TrimEnd() + "\n\n" + Marker + "\n" + info;
                }
            }
            catch (Exception e)
            {
                Log.Warning("[Raiders Have Families] Could not annotate Ideology tooltips: " + e);
            }
        }
    }
}
