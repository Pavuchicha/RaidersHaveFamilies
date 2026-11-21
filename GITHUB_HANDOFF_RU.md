# Raiders Have Families — передача v0.10.0

Этот ZIP содержит **все исходники**. Он заменяет в GitHub Desktop версии 0.7–0.9.2; пользователь ранее подтвердил тест в RimWorld только на v0.6.1.

1. Pull origin, Repository → Show in Explorer.
2. Из архива распаковать **содержимое в корень репозитория** с заменой файлов (корень содержит `.github`, `RaidersHaveFamilies`, `tests`).
3. Summary: `RHF v0.10.0 - Death Reports, Cannibalism and Coalition Reprisals`.
4. Commit to main → Push origin → GitHub Actions.
5. При зелёной сборке скачать artifact `RaidersHaveFamilies-RimWorld-1.7-v0.10.0-REPRISALS-TEST`, затем установить ZIP с папкой мода и DLL. Работать на копии сохранения.

**Статус:** локально нет .NET 10, поэтому DLL и игровой TPS не проверены. Ключевое новое описание: `RHF_100_REVIEW_RU.md`. Дальнейшую работу вести из полного ZIP v0.10.0, не с отдельного старого патча.
