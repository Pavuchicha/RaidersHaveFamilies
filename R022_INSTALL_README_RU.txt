NJR R022 — CI regression test hotfix (2026-11-23)

ВНИМАНИЕ: Это исправление теста CI, НЕ исправление поведения игровых рейдеров.

1. Распаковать NJR_R022_GITHUB_DESKTOP_TEST_FIX.zip В КОРЕНЬ репозитория RaidersHaveFamilies с заменой.
2. В GitHub Desktop: Summary = R022: Update rescue route CI test for leaderless R021
3. Commit to main, Push origin.
4. Убедиться, что GitHub Actions прошёл этап статических тестов и компиляции.

Причина исходного сбоя: тест требовал удалённый в R021 вызов ChooseReachableApproach(lead,captive,map).
Новый код использует ResolveShared() и запасной ChooseReachableApproach(sample,captive,map).

Локально: 81/81 проверок из шага статических тестов прошли. Компиляция DLL не проверена.
Полный отчёт: docs/NJR_R022_CI_TEST_UPDATE_RU.md
