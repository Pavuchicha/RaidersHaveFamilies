# NJR — MASTER CONTINUITY R005 / 2026-11-23

## Для следующей итерации

1. Это **единственный активный мод** NJR — Not Just Raiders (прежде Raiders Have Families). ThornCorps заброшен, не смешивать. Для всех исходников истинный путь — `PROJECT/RaidersHaveFamilies/Source/`, исторические одноимённые файлы в корне `PROJECT/` не компилируются.
2. Архив R005 содержит полный `PROJECT/` (код, XML, tests, `.github`, docs), `DELIVERY/` с отдельным накопительным ZIP для GitHub Desktop, `EVIDENCE/` со скриншотами прежних ошибок, `HISTORY/` с полным R004, манифест SHA-256. **Не редактировать ZIP внутри Агентом А** — ему передавать распакованные файлы.
3. Каждый ответ разработки — два **разных** ZIP: `NJR_GITHUB_DESKTOP_REPLACE_Rnnn.zip` (накопительный overlay в корень через GitHub Desktop) и `NJR_DEVELOPMENT_FULL_Rnnn_YYYY-MM-DD.zip` (моя полная база). В конце максимально ясно: «Что тебе делать». Пользователь пока без ПК; не просить действий или присылать архив снова без нужды.
4. Техническая база: RimWorld 1.7 / net10.0 / Harmony3; save identity `pavuk.raidshavefamilies`, namespace/assembly `RaidersHaveFamilies`, DefNames и старые Scribe tags сохранять; отображаемое имя NJR, установленная папка `NJR-NotJustRaiders`.
5. Исходное мастер-ТЗ: `PROJECT/IMPLEMENTATION_SPEC_RU.md`, `PROJECT/REQUIREMENTS_AUDIT_MASTER_RU.md`, `PROJECT/TACTICAL_DOCTRINE_DESIGN_RU.md`, плюс R001–R004 docs. Весь комплекс: семьи, родство, ранги 0–8, дипломатия, выкуп, конверсия/вербовка, медицина/жестокость, модули рейдов 01–18, фактическое физическое освобождение, защитные позиции/AI, реальное строительство pod, технология фракций, звери из бюджета, тяжёлые снаряжения, CE, passively event-driven/TPS.
6. **R005 реализовано:** истинная случайность выбора фракции при ручном dev-спавне пленника; запрет немедленного повтора, явный pinned override, ресет на новый Game, выбор неиспользованной фракции среди всех доступных, модовые `pawnGroupMakers` как источник PawnKind, UI-объяснения. См. `PROJECT/docs/NJR_R005_FORENSIC_SELF_AUDIT_RU.md`.
7. **R005 дополнительный исправленный дефект:** перенесено enforcement `Drop/Siege` по техуровню исполнителя ДО расчёта материалов VIP, резервный lowtech рейд явно `EdgeWalkIn`.
8. **Доказательства:** `PROJECT/docs/NJR_R005_TEST_RESULTS.json`: 68/68 Python static + reference models. **Нет C# compile PASS, GAME PASS, CE PASS, SaveLoad PASS, TPS замеров**. Статические тесты нельзя выдавать за реальные игровые. UI и игровые скриншоты показывали серьёзные провалы 01/16. Все 18 рейдов ещё не приняты в игре.
9. **Агент А:** может всё кроме чтения/записи ZIP. Задание R005: `PROJECT/docs/AGENT_A_R005_TARGETED_AUDIT_RU.md`. Аудит А в этой итерации не запускался.
10. **Приоритет следующей разработки:** получить зелёную CI компиляцию с реальными refs; испытать RNG/модовые фракции, №1/№16; проверить и устранить неподтверждённые R004 P0/P1, низкобюджетную инфляцию, многогрупповую привязку зверей, orbital/CE; возвращаться ко всем 18 рейдам.

## Архивы и порядок установки

- Пользователю выдавать отдельно `DELIVERY/NJR_GITHUB_DESKTOP_REPLACE_R005.zip` и полный `NJR_DEVELOPMENT_FULL_R005_2026-11-23.zip` как две ссылки.
- ZIP для GitHub Desktop: открыть `Repository → Show in Explorer`, распаковать **содержимое** в корень с заменой, Summary `NJR R005 - Random debug prisoner factions and forensic fixes`, Commit to main → Push origin. Только при зелёном GitHub Actions скачать INSTALL ZIP, заменить старый мод, исключить дубль папок. Не запускать батник.
- Пока у пользователя нет ПК, делать ничего не нужно.
