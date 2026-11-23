## R020 (2026-11-23) — stabilize rescue command and structural-attack authority (SOURCE ONLY)

After field report of R019 squad attacking unrelated walls, the objective commander is held stable per captive, an A* CPU-budget abort can no longer authorize a demolition fallback, intact agreed breaches retain their plan while the breacher works, and stale vanilla structure-attack jobs are interrupted on mission claim. Only source/model checks have passed; C# compilation and real game behavior are UNVERIFIED. Pod construction #16 is unchanged. Details: `docs/NJR_R020_COMMAND_AUTHORITY_AND_BREACH_REGRESSION_RU.md`.

---

## R017 (2026-11-23) — minimum mission force + coordinated door-first breaching (SOURCE ONLY)

An actual game report showed rescue #01 arriving as **one raider** and breaking unnecessary walls. Root causes: raw storyteller points and equipment costs without a minimum native combat roster; acceptance of any single generated pawn; individual troops independently electing different break targets; route policy that preferred a wall over even a safe door. R017 commits a minimum-sized mission budget before native generation (rescue 5, diversion 3, vengeance 5), fills understrength native pawn lists **before real EdgeWalkIn/CenterDrop arrival and Lord creation**, preserves verified ground spawning, and makes safe doors/doorways take priority over non-essential wall demolition. See `docs/NJR_R017_RESCUE_FORCE_AND_BREACH_ROOT_CAUSE_RU.md`.

**STATIC TESTS ONLY. No .NET build or in-game #01/#16 gameplay pass has been established.** Extremely expensive modded combat kinds can still hit the 15,000-point ceiling; the game logs this explicitly. VIP fortification/build AI remains unverified.

---

## R016 (2026-11-23) — REAL GROUND RESCUE ENTRY HOTFIX (SOURCE ONLY)

The recurring cave/lake rescue #01 screenshot exposed three flaws: an exterior approach scan could accept unrelated terrain anywhere on the map; vanilla `EdgeWalkIn` scattering ignored roof/fog/water after NJR validated only the center; and initially blocked captives left some rescuers without a forward approach order. R016 checks objective-near connectivity and filters *every actual spawned pawn cell* with mission-scoped Harmony hooks. The center is protected against native re-resolution. See `docs/NJR_R016_GROUND_RESCUE_SPAWN_ROOT_CAUSE_AND_RETEST_RU.md`.

**No DLL compilation or RimWorld gameplay retest has been performed in this environment.** Scenario #01 and VIP #16 are not declared game-tested.

---

## R015 (2026-11-23) — tactical route execution / SOURCE ONLY

The offense JobGiver now requests the next adjacent cell of the individual fighter's own safe-first A* route instead of handing a distant Goto to the vanilla pathfinder. Guard regrouping no longer falls back to an own-position idle order; open doors are excluded from physical breaching. See `docs/NJR_R015_FINAL_HOME_AI_AND_RELEASE_RU.md` and `NJR_R015_HOME_TEST_CARD_RU.md`. Compiling a DLL and checking real JobDrivers is still REQUIRED.

---

## Development R014 (2026-11-23, SOURCE ONLY)

Faction-scoped saved intelligence for physically observed turrets, no live targets assigned through walls to soldiers without line of sight, urgent refresh of destroyed/captured objectives, and incapable-of-violence sapper rejection. Source audit: `docs/NJR_R014_MISSION_INTELLIGENCE_AI_REVIEW_RU.md`. 75/75 supplemental Python checks; DLL, Combat Extended, save/load and RimWorld scenarios 01/16 **not tested**.

---

# NJR R010 — unrestricted tactical planning (source-only)

R010 removes artificial in-combat actor, threat, target, path-expansion, scan, support and order-count cutoffs (even at the cost of combat performance). Idle-state throttles, raid-point budgets, physical constraints and mission termination remain. See `docs/NJR_R010_UNBOUNDED_COMBAT_MANUAL_REVIEW_RU.md` for the manual re-review and limitations. No C# DLL/game/CE validation yet; game-tested #1/#16 remain open.

---

# NJR — Not Just Raiders

**RimWorld 1.7 / experimental v0.14.0-beta2** · Formerly *Raiders Have Families*.

Raids have consequences. Enemy pawns may have relatives and loyalty ties; prisoner treatment can affect relations, ransom demands and faction responses. The mod also experiments with hostage rescue, coalition assaults, retaliation, social rank, combat doctrine, and physical VIP transport-pod extraction. **No claim that all 18 tactical scenarios are production-ready**: they require gameplay validation.

## Development update R009 (2026-11-23, community combat AI source research; source-only)

Reviewed *actual C# source* from CAI-5000, Smarter Raider AI, Search and Destroy, and Better Grenade Handling. Scoped NJR mission behavior remains independent from their global vanilla Harmony pathfinding/avoid-grid patches. R009 removes stale offensive turret enumeration limits, compares known turret exposure behind doors against a reachable wall breach, and invalidates rescue corridor cache on map turret-layout changes (sampled every 120 active-mission ticks). No foreign code has been copied. **No .NET compile or gameplay PASS**, and full multiroom attack routing and scenarios #01/#16 remain unverified. Full investigation in `docs/NJR_R009_COMMUNITY_AI_CODE_REVIEW_RU.md`.

## Development update R008 (2026-11-23, MANUAL SOURCE REVIEW — source-only)

A targeted manual control-flow and RimWorld 1.7 API review found six concrete defects/fragilities and corrected them: rescue route caches are now bound to their actual leader and moving captive; reserve rescue incidents cannot fall back to unverified random map edges; weapon replacement is no longer rolled back because deleting OLD gear failed; group ransom IDs are not reused after loading a partially migrated save; orbital verbs are accessed through the typed `CompApparelVerbOwner` interface; and tactical route scoring no longer drops the 41st or later turret from its threat model. Original Scribe keys, packageId and tactical doctrine remain unchanged.

**This does NOT certify every character of the full source, DLL compilation, or success in RimWorld.** The unresolved standalone ransom payment-loss path and critical scenarios 01/16 are documented explicitly in `docs/NJR_R008_MANUAL_FORENSIC_REVIEW_RU.md`. Python smoke checks are secondary compatibility checks, never a substitute for code review and real gameplay.

## Development update R007 (2026-11-23, mod compatibility + idle optimization; source-only)

Transient hostage lookup now uses a non-serialized `Pawn` dictionary with immediate invalidation at every tracked removal/detach; queued arrivals use a hash set instead of repeated linear duplicate scans. Modded faction combat-animal candidates are cached per immutable `FactionDef`, and intermediate affordability allocations are reused. Optional armor/orbital-belt equipment is skipped when it conflicts with faction-/mod-issued apparel, with bounded failure isolation and actual item-value billing. The previous R006 contracts, raid AI, CE ammo preservation and serialized keys are retained unchanged.

**70/70 static/model tests PASS. No .NET compile, CE/HAR/VE in-game validation, FPS/TPS benchmarks or 01/16 gameplay PASS claimed.** See `docs/NJR_R007_COMPATIBILITY_AND_IDLE_OPTIMIZATION_RU.md`.

## Development update R006 (2026-11-23, GLOBAL SOURCE AUDIT — not a runtime release)

The global audit identified and fixed a **group ransom state loss**: a member who physically left the map had their `Pawn` pointer cleared (correct), but a later diplomatic cleanup deleted the remaining unpaid contract record (incorrect). Group participant debts now persist without holding world pawns until payment or explicit cancellation. Disabling *new* negotiations no longer freezes already pending recruitment disclosure or retaliation. The ordinary raid incident no longer adds a free 300-point minimum **after** paying animal, gear and construction costs. Dev scenario 08 is now a technology-composition test, not a separate animal-only raid category.

**69/69 Python static/model tests PASS; 13 XML parse; all 18 dev scenario commands registered.** This does not prove C# compilation, RimWorld runtime, CE, long-save compatibility, FPS/TPS or success for rescue 01 and VIP 16. See `docs/NJR_R006_GLOBAL_FUNCTIONAL_AUDIT_RU.md`.

## Development update R005 (2026-11-23, source-only; independent audit)

The dev-mode hostage spawn now draws a **random eligible hostile faction** per invocation, avoiding the immediately preceding selection when two or more choices exist. A manually pinned faction remains an explicit opt-in test override and is now named unmistakably in the developer menu and spawn notice; a one-faction world cannot produce variety. Modded faction pawn kinds declared only through `pawnGroupMakers.options` are supported. A low-tech raid's drop restriction is applied before native VIP pod supply budgeting, including the backup incident. See `docs/NJR_R005_FORENSIC_SELF_AUDIT_RU.md`.

**68/68 Python tests PASS; C# compilation, RimWorld gameplay, CE and Save/Load remain UNVERIFIED.** Previous real gameplay failures in scenarios 01/16 are not declared solved.

## Development update R004 (2026-11-23, source-only)

Raid forces now use **technology-aware, paid equipment/animal composition** rather than a hard-coded "animal raid" or universal pod landing. Low-tech own-faction pod raids are blocked, while compatible high-tech mercenaries can arrive separately. Modern factions can receive varied role-based kits including uranium melee equipment, miniguns and expensive heavy weapons at high raid budgets; modded weapons respect PawnKind tags, and CE ammo-bearing guns are not blindly replaced. An experimental real orbital-targeter Verb is included but **not gameplay-verified**. Rescue routing searches for a fully turret-safe path first (even via walls) and only then minimizes exposure. Clan/medieval/industrial/outlaw offices now differ. Ordinary departing prisoner records are removed by event, with sparse cleanup and 90k/150k strategic review ticks. See `docs/NJR_R004_REQUIREMENTS_AND_DESIGN_RU.md` and `docs/NJR_R004_CHANGELOG_AND_OPEN_RU.md`.

**67/67 Python source contracts/reference models PASS; .NET/C# compilation, in-game 18 scenarios, orbital Verb, CE and save/load UNVERIFIED.** Previous observed field failures #01 and #16 remain open.

## Development update R003 (2026-11-23, source-only)

Improved the shared offensive planner for retaliation, diversions, coalitions and post-rescue assaults (scenarios 07–15, 17–18). It now selects genuinely observed threats, prefers physically reachable doors to unnecessary wall damage, scores limited formation slots against visible turrets, does not attack released captives, and treats a sought defector's last observed tile differently from live omniscient tracking. A developer-only diagnostic action 38 shows offensive Lords, roles, jobs and observations. Vanilla siege and animal Lords are intentionally not commandeered.

**This is a source-only candidate.** 66/66 Python contracts/reference models passed; C# build, gameplay, CE and save/load NOT tested. Known field issues 01 and 16 remain open, as does unbudgeted native animal support in scenario 08. Full details in `docs/NJR_R003_CHANGELOG_RU.md`.

## Development update R002 (2026-11-23, source-only)

Group ransom negotiations now support choosing 2–12 prisoners from up to 24 eligible captives (8 options per page). Successful physical release of every selected prisoner triggers one combined payment. An inaccessible drop site leaves the payment pending; the player may request delivery later through the original faction's Comms Console from the same colony. Once native drop-pod creation has begun, an uncertain partial failure blocks automatic retry to prevent duplicating silver. A new developer diagnostic lists group contracts and payout states.

**This R002 source overlay is experimental, not a compiled/game-tested build.** Its standalone GitHub Desktop ZIP includes all pending R001+R002 changes. See `docs/NJR_R002_CHANGELOG_RU.md` and the retained requirement/audit documents. Rescue scenarios 01 and 16 remain live-playtest blockers.

## Installation

GitHub Actions on this repository compile the project using `.NET 10`, `Krafs.Rimworld.Ref 1.7.5014-unstable`, and `Lib.Harmony 3.0.0-rc.7`, then package the installable mod. After a **successful** workflow run, install only the `NJR-NotJustRaiders` folder from the install ZIP inside the RimWorld `Mods` directory. Remove the earlier copy so only one installation remains. Back up existing saves before testing beta builds.

## Save compatibility

The display name is **NJR — Not Just Raiders**. Internal identifiers deliberately **remain unchanged**: `packageId=pavuk.raidshavefamilies`, C# namespace, assembly name `RaidersHaveFamilies.dll`, DefNames and Scribe keys. The *installed folder name* is now `NJR-NotJustRaiders`; RimWorld uses the unchanged packageId for save compatibility. Delete the old `RaidersHaveFamilies` folder when installing the new build. Do not install it alongside the original: it is the **same mod**, renamed.

## Features and current evidence

- Family ties, optional kin generation, importance/rank 0–8, prisoner-family contact, ransom and diplomacy.
- Captivity consequences, actual Ideology conversion disclosure, retaliation and allied/mercenary faction responses.
- 18 developer-mode scenarios (rescue, punishment, combinations, animals, coalition, siege and VIP flight). The individual scenarios are **test harnesses**, not proof that every outcome works.
- Field evacuation uses physical building plans, materials, transport and world travel; save/load and long-route relay behavior still need in-game testing.
- Optional integration with WCE2 / Combat Extended is not claimed fully verified.

## Verification

`python3 tests/njr_200_integrity_audit.py` validates packaging, translations, the 18 debug scenarios and compatibility invariants. CI runs the historical regression suite, restores RimWorld references and compiles the DLL. Static tests cannot replace gameplay; use the in-game developer actions and collect `Player.log` for failures. Audit findings and coverage are in `docs/NJR_FULL_AUDIT_AND_RELEASE_RU.md`.

Version v0.14.0-beta2 describes a rebrand/consolidation based on S4.1-A/P, **not a declaration of 18 proven tactical AIs**. See [`docs/LEGACY_README_PRE_NJR.md`](docs/LEGACY_README_PRE_NJR.md) for historical release notes.

## Unreleased development snapshot · 2026-11-23 · R001

The full source archive (not an installable DLL) also contains experimental
**group ransom negotiations for 2–12 prisoners**, bounded reuse of existing
world pawns for family relationships, and privacy of unreported Ideology
conversion. These changes are **source-only and have not been compiled or
played**. The release workflow remains labelled v0.14.0-beta2 until a verified
CI migration; this label must not be mistaken for the new snapshot version.
See `docs/NJR_DEVELOPMENT_MASTER_R001_RU.md`,
`docs/NJR_R001_CHANGELOG_RU.md`, and
`docs/NJR_OPEN_DEFECTS_AND_TEST_MATRIX_R001_RU.md`.

### R008 — ручная ревизия (после R007)

Кодовые дефекты и API-сверка изложены в `docs/NJR_R008_MANUAL_FORENSIC_REVIEW_RU.md`: исправлены кэш маршрута, безопасный вход резерва, транзакция оружия, ID договора, типизированный орбитальный Verb и скрытые ограничения обнаружения турелей в нескольких планировщиках, а также бесплатный минимум тестового запасного рейда. Это не является посимвольным сертификатом проекта или игровым PASS.

### R011 — боевой контакт и реальная эвакуация (source-only)

Новые физические контрприказы против турелей и пешек игрока на пути: `NJRCombatContactS44`. Строитель VIP-капсулы сохраняет свою роль во время контакта; освобождённых сопровождают общей колонной; арьергард назначается только после наблюдаемого движения преследователей к колонне. Если безопасного выхода нет, сначала допускается опасный физически проходимый край, затем пробуется настоящий пролом или доступный промежуточный маршрут. Исправлен скрытый 55-клеточный потолок дальности оружия. Полная ручная трассировка: `docs/NJR_R011_FRONTLINE_CONTACT_PURSUIT_MANUAL_AUDIT_RU.md`. Не является игровым PASS или подтверждённой сборкой.

### R012 — системная матрица боевых комбинаций (SOURCE-ONLY)

Составлена ручная матрица 64 классов ситуаций с прослеживанием `Lord/ThinkTree/JobDriver` и явными остаточными рисками (`docs/NJR_R012_COMBAT_COMBINATIONS_MANUAL_AUDIT_RU.md`). Добавлен полный `NJROffenseRouteS45`: маршрут для наступления выбирается не по факту существования любого пути, а safe-first среди настоящих дверей/стен и наблюдаемых турелей; если безопасного нет, рассматривается минимальный физический риск. Исправлен тупик спасательного `RiskFallback`, индивидуальные пути отставших, blast-radius safety для штатных взрывных боеприпасов, защита инженера VIP, ближний fallback стрелка-проломщика, поддержка модовых стен при фазе 3. Исправлен реальный compile-blocker необъявленного `shown` в debug-диагностике наступления. **НЕ game-pass, DLL/CE/проверки #1/#16 ещё нужны.** Текущее досье: `docs/NJR_DEVELOPMENT_MASTER_R012_RU.md`.


## R013 — ручная ревизия по community AI

Сохранён **source-only** отчёт `docs/NJR_R013_AI_MANUAL_REVIEW_RU.md`: VIP-носильщики продолжают разгрузку под настоящей охраной; ближайшая наблюдаемая турель выбирается по тактической близости, а не по порядку строительства; сопоставляются все наблюдатели; открытые вражеские двери не планируются на разрушение; арьергард ориентируется на относительное движение обеих сторон; фазе VIP доступны модовые стены. Сборка DLL и живые проверки #01/#16 остаются открытыми.
