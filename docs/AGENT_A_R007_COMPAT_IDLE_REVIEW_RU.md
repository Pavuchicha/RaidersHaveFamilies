# Агент A — задание read-only для независимой ревизии R007
Работать с **обычными файлами**, не читать и не писать ZIP (ограничение Агента А). Агент НЕ запускался.

Вход: `RaidersHaveFamilies/Source`, тесты `tests/njr_214_compatibility_and_passive_cache.py`, отчёт `docs/NJR_R007_COMPATIBILITY_AND_IDLE_OPTIMIZATION_RU.md`.

1. Проверить `RHFGameLedger.FindHostage`: после PostLoad, after exit, sold/death/recruitment, payment, partial group exit, same-count remove+add, nullable pawn; добиться отсутствия удерживаемых ссылок и нарушений договора.
2. Проверить C# 1.7 API `ApparelUtility.CanWearTogether`, `Pawn_ApparelTracker.Remove`, `Apparel.MarketValue`, `GetUniqueLoadID`, nullable annotations и порядок Harmony patch lifecycle. Нужна настоящая DLL сборка, не string-match.
3. Проверить CE, HAR, VE: Does optional armor/targeter Wear throw or silently drop existing worn modded apparel? Does rollback leave free gear? Existing ranged CE pawn ammo stays untouched?
4. Проверить runtime cache `FactionDef`: соответствуют ли они реально генерации PawnKindDef модовых фракций? Есть ли моды с изменяемыми pawnGroupMakers после загрузки?
5. Снять TPS baseline idle vs R007 на одинаковом сохранении с 200+ пленниками/большими волнами, не оптимизировать combat AI ради цифры.
6. Самостоятельно проверить сценарии 01/16 и все 18 по `RHF_S22_ALL_18_RAIDS_ACCEPTANCE_MATRIX_RU.md`. Записывать GAME FAIL/PASS только по реальному игре.
7. Составить аудиторский отчёт `docs/AGENT_A_R007_FINDINGS_RU.md` с файлами/строками, P0/P1/P2, доказательствами. Без прямой работы с ZIP.
