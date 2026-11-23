NJR R005 / 23.11.2026
Этот ZIP — накопительное обновление исходников R001–R005 для GitHub Desktop, а НЕ готовый игровой мод.
Repository -> Show in Explorer -> распаковать содержимое ZIP в КОРЕНЬ репозитория с заменой -> Summary:
NJR R005 - Random debug prisoner factions and forensic fixes
Commit to main -> Push origin. Дождаться GREEN GitHub Actions, затем скачать INSTALL артефакт.
В игре удалить старую папку RaidersHaveFamilies и заменить на NJR-NotJustRaiders; packageId не менять.
R005: истинный случайный выбор фракции (если >=2 — без немедленного повтора), отдельный manual pin,
модовые PawnGroupMaker PawnKinds, tech gate до финансирования pod, низкотехнологичный fallback только EdgeWalkIn.
68/68 статических тестов; настоящая сборка/игровые тесты не проводились.
