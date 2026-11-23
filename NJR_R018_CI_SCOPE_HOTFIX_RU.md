# NJR R018 — исправление компиляции R017 (23.11.2026)

## Подтверждённая ошибка
GitHub Actions run 45244204215, commit 77deeed23bea5842852cfa5c756c83878754e1a1:
`RaidersHaveFamilies/Source/RHFOperations.cs(948,25): error CS0103: The name 'parms' does not exist in the current context`.
Шаг 7 Compile mod DLL завершился неудачно; предыдущие проверки и restore прошли.

## Патч
- `RHFOperations.cs`: сохранён выбранный и верифицированный центр наземного появления в `rescueReinforcementEntry` до границы `try`;
  вызов `CompleteGroundRescue()` теперь получает эту доступную в нужной области видимости переменную.
- `tests/njr_223_force_strength_coordinated_breach_r017.py`: исправлен контрактный тест, который ошибочно требовал прежний, некомпилируемый вызов.
- Никаких изменений баланса, условий миссий или стратегии AI в этом патче нет.

## Проверки
Локально успешно: tests/njr_223_force_strength_coordinated_breach_r017.py,
tests/njr_222_ground_rescue_actual_arrival_r016.py,
tests/njr_221_route_execution_r015.py,
tests/rhf_151_s41_rescue_single_writer.py.
Компиляции C# и игрового теста НЕ БЫЛО; после коммита дождаться зелёной сборки GitHub Actions.
Полный локальный пакет тестов при попытке выполнения превысил временной лимит, утверждать PASS нельзя.

## Интеграция
Доступ GitHub API на чтение есть, на запись — нет (403 Resource not accessible by integration).
Патч распаковать в КОРЕНЬ уже существующего локального репозитория Pavuchicha/RaidersHaveFamilies, подтвердить замену файлов.
GitHub Desktop: Summary — `R018: Fix rescue reinforcement compile scope CS0103`, Commit to main, Push origin.

## Следующий шаг
Подтвердить успешную сборку 1.7 DLL и затем повторить ground rescue #1 (5 бойцов, точка входа, маршруты, стены);
VIP pod rescue #16 пока остаётся НЕПОДТВЕРЖДЁННЫМ.
