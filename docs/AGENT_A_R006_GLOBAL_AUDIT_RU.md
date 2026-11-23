# ЗАДАНИЕ АГЕНТУ А — R006 (не запускалось)

Агент А может работать только с распакованными обычными текстовыми/C# файлами. ZIP не читать и не писать. Строго read-only независимый аудит (не вносить код без нового назначения).

1. Проверить R006 групповой выкуп: release pawn=null, `UpdateOffers`, `NJRGroupRansom.Audit/Ready/TryDeliver`, потеря карты, death/sale/recruitment, Scribe. Доказать, что R005 баг исправлен для долгой задержки между выходами.
2. Проверить настройки: `Negotiations=false`, `Retaliation=false`, `Enabled=false` — какие таймеры должны продолжаться; самостоятельная семантика каждого флага.
3. Проверить всю цепь уменьшения story points `NJRThreatBudgetS41`→`Reserve`→`Deploy`→`IncidentParms`→`PawnGroupMaker`; отдельный debug fallback и multi-party raids. Найти бесплатные очки/материалы.
4. Проверить все 18 сценариев сквозным статическим control-flow чтением; выявить потенциальные `Lord`, `ThinkTree`, job, release/convoi, VIP launch и turret avoidance нарушения.
5. Компиляционный risk-review API RimWorld 1.7, Harmony, DLC: цель — конкретные line-number findings, без фальшивого «COMPILE PASS».
6. Проверить passive TPS (idle scan), совместимость с CE, WCE2, modded races/factions, обязанности NPC and social ties. Особое внимание на 40-emitter turret cap.
7. Итог: список P0/P1/P2 с source location, reproducer, fix proposal и строго разграниченными SOURCE/PREDICTED/GAME доказательствами. Никакой записи в ZIP.
