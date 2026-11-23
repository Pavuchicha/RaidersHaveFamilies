# NJR — МАСТЕР-ПЕРЕДАЧА R008 · 23.11.2026

**ПЕРВЫМ ДЕЛОМ** читайте `docs/NJR_R008_MANUAL_FORENSIC_REVIEW_RU.md`, затем `docs/NJR_DEVELOPMENT_MASTER_R007_RU.md`, `REQUIREMENTS_AUDIT_MASTER_RU.md`, `IMPLEMENTATION_SPEC_RU.md`, `TACTICAL_DOCTRINE_DESIGN_RU.md` и предыдущую матрицу `docs/NJR_R006_GLOBAL_FUNCTIONAL_AUDIT_RU.md`.

Текущий пользовательский приоритет: не набивать бессмысленные галочки статических тестов, а **ручная построчная семантическая ревизия кода и реальных API**. НЕ заявлять «все 18 рейдов работают» и «все символы проверены», если это не доказано. Два независимых ZIP в КАЖДОМ ответе по разработке: накопительное полное файловое обновление GitHub Desktop и отдельный архив преемственности. Пользователь всё ещё без ПК. Результаты теста #01/16 были неудовлетворительными.

R008 подтверждённые исправления: `RHFObjectiveRouteV128` cache leader + moving captive; `RHFOperations` safe reserve rescue spawn; `NJRAdaptiveLoadouts` transactional weapon replacement; `RaidersHaveFamilies.cs` group ransom ID postload; `NJROrbitalSupport` typed vanilla CompApparelVerbOwner/Verb_PowerBeam; `RHFObjectiveRouteV128.Threats` теперь считает все турели, а не ближайшие 40 источников вперемешку со стрелками. Устранены те же неверные отсечения по порядку зданий в `RHFRescueGroundEntryS41`, `NJRRescueInteriorS41`, трёх поисках турелей `RHFRescuePlannerS41`, полевой площадке `RHFOperations`, `RHFStagedAdvanceV139` и резервном A* `RHFCombatPlannerV125` (F07–F09). Устранён принудительный минимум 650 очков у debug-резервного рейда (F10).

Главный ещё НЕ исправленный P0 — `RHFHostageSystems.OnPrisonerExit` необратимая потеря одиночного выкупа при ошибке/недоступности `DropPodUtility.DropThingsNear`. Нужна отдельная согласованная save-aware транзакция. Не забыть долгую память: Lord/ThinkTree и NPC/должности/семьи, старые сохранения. 1.7 external decompile сверена для CanLaunch, CompTransporter, FlyShipLeaving/IActiveTransporter, Pawn_HealthTracker.AddHediff и CompApparelVerbOwner.

Системные ключи сохранять: C# namespace/assembly `RaidersHaveFamilies`, `packageId=pavuk.raidshavefamilies`, все исторические Scribe; название в игре `NJR — Not Just Raiders`, установочная папка `NJR-NotJustRaiders`. Мод ThornCorps не активен, не смешивать.

CI: Python smoke не доказательство. .NET SDK отсутствует и RimWorld не запускался в текущей среде. `docs/NJR_R008_SAFETY_SMOKE_RESULTS.json` отражает 70 пройденных из 71 вызванных Python scripts; 71-й — проверка release ZIP, требующая отдельного аргумента (ожидаемая ошибка вызова), и не является проблемой C#.

Агент А: пользователь считает его способным выполнять code tasks, но нельзя читать/писать ZIP; никакой самостоятельный агент в этом этапе не вызывался. Смотрите `docs/AGENT_A_R008_MANUAL_REVIEW_RU.md`.
