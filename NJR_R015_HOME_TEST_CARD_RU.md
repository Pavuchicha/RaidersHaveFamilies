# NJR R015 — при возвращении к компьютеру

**Файл для GitHub Desktop:** `NJR_GITHUB_DESKTOP_REPLACE_R015.zip` — накопительный исходный overlay (R001–R015), НЕ игровой мод.

1. Открыть **GitHub Desktop**, выбрать репозиторий `RaidersHaveFamilies`, сделать **Fetch/Pull origin**; убедиться, что незакоммиченных изменений нет (или сохранить их отдельно).
2. **Repository → Show in Explorer**. В корень репозитория распаковать **содержимое** ZIP R015 с заменой. Должны появиться папки `.github`, `RaidersHaveFamilies`, `tests`.
3. В GitHub Desktop Summary: `NJR R015 - Safe route execution, actual breacher support, open-door fix`. Затем **Commit to main → Push origin**.
4. GitHub → **Actions**: дождаться завершения новой сборки. Если **красная**, это ошибка, пришли скрин ошибок компилятора. Если **зелёная**, скачать **Artifacts → NJR-NotJustRaiders-RimWorld-1.7-v0.15.0-beta1**.
5. Распаковать вложенный установочный `NJR_NotJustRaiders_v0.15.0-beta1_RimWorld_1.7_INSTALL.zip`: содержимое `NJR-NotJustRaiders/` копировать в `RimWorld/Mods/` **вместо** предыдущего NJR. Проверь, чтобы `NJR-NotJustRaiders/Assemblies/RaidersHaveFamilies.dll` физически существовал. Удалить старые дубли мода. Harmony — обязательная зависимость.
6. Сохранение **скопировать**. В игре запустить NJR debug `01 — Спасение: пролом и побег`, затем `16 — VIP: десант, баррикады, полевая капсула`. При сбое прислать `Player.log`, скрин карты и точный тест.

**Нельзя:** копировать `NJR_DEVELOPMENT_FULL_R015_2026-11-23.zip` в GitHub или в `Mods/`. Это архив истории для следующего разработчика, а не обновление.

**Предел достоверности:** Python-тесты не проверяют RimWorld. Только зелёный GitHub Actions подтвердит компиляцию, а сами игровые испытания подтвердят поведение.
