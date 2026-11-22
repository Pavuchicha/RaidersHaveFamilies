# RHF S2.1 — Harmony initialization & focused tactical hotfix

**Source baseline (not S2):** GitHub `main`, commit `efbe0bc4c4f095d4316bf69e0b7f9edb2b97cf20` (v0.13.3 S1).  This overlay includes **all S2** tactical changes plus **S2.1**, so do NOT install S2 separately.

**Trigger:** live test screenshots 01/05/16 and audit A-P0-004: Harmony failed `Undefined target method ... RHFControlledFreedExitJobV131::Prefix` on RimWorld 1.7.5014. Thus key game hooks may not have been installed.  This release changes patch bootstrap; it is NOT proof that scenario 05 or 16 now works in the game.

## S2.1 code changes relative to S2

1. `RHFExitJobGuardV131.cs` removes attribute-driven Harmony target resolution and uses an OPTIONAL manual installer that only hooks a matching one-`Pawn`/`Job` `TryGiveJob` declared in relevant `ExitMap` types; skips absent types without crashing the rest of the mod. It warns when no matching hook was found. That means the exit-job loop is **not guaranteed fixed** where the method is absent; test and investigate separately.
2. `RHFPatches.cs` installs Harmony patch CLASSES independently (`CreateClassProcessor(type).Patch()`), logs failures with class names and totals, installs exit hook and WCE2 integrations separately. One obsolete hook must not disable all unrelated patches. A failure in a mandatory patch remains visible and must be addressed.
3. `RHFRescueFireDisciplineV136.cs` idle self-defense may engage actual player-owned colonists, mechanoids, animals, and defensively firing turrets within local weapon range. Never generic trash buildings; still checks the protected operation roster and approximate physical firing lane. Recheck target every 240 ticks.
4. `RHFObjectiveRouteV128.cs` checks occupied lane, route safety and known turret exposure for displaced side-lane targets.
5. `RHFCombatPlannerV125.cs` avoids overwriting `Wait_Combat`/`AttackStatic` while prone as well as named CE suppress/seek-cover/reload jobs.
6. `RHFOperations.cs` accumulates same-faction drop snapshots (`UnionWith`) instead of resetting each time.
7. **New user rule (partially implemented):** `RHFOperations.cs` disallows `TryPod` for ground-arriving rescue parties and requires at least one actual **successful drop rescue** before `BuildAndLaunch`. `RHFDebugActions.cs` switches debug 04 (FieldPod) to industrial drop rescue, as already used by debug 16. This is ONLY the eligibility gate. Actual cargo kits per VIP, fuel, launchable machinery, landing/roof safety, and full strategic approach model are specified separately for S3; they are NOT implemented here.
8. Tests: old literal-string tests updated to reflect the newer implementation; `rhf_137_harmony_hotfix_contracts.py` and `rhf_138_air_only_pod_gate.py` with negative mutations. Old test 136 expanded for turret and mech ownership.

## Validation and limits

- **T0/T1:** 37 Python CI tests PASS (including negative mutations), XML parses. This is NOT a C# compilation or gameplay proof.
- **T2 (GitHub Actions):** NOT RUN for S2.1.
- **T3 (RimWorld 1.7.5014, CE):** NOT RUN for S2.1.
- **Severity P0:** the obsolete `ExitMapBest` target may still not be discoverable; now it only logs a warning. If `started 10 jobs in one tick` returns, implement another safe, correctly bound job interception without global ThinkTree breakage.
- **Severity P0:** after starting RimWorld inspect `[RHF][HarmonyS21]` and ANY `FAILED class`. One failure cannot stop all other installs, but a failed mandatory class (SpawnSetup, hostility, letters, Pawn.ExitMap) still invalidates the test.
- **Open P1+:** genuine all-route tactical planner, formation/fireteam geometry, real physical drop-kit inventory, fuel, full native launch and multiple VIP support, roof-safe drop site/build site, recovery from barricade deadlock. See companion S3 doctrine.

## Developer validation order

1. Agent A independently reviews S2.1 source changes and C# API compatibility, especially Harmony 3 `CreateClassProcessor`, reflection and the optional exit guard.
2. If approved, overlay ZIP into GitHub Desktop (on exact S1 baseline), commit/push, wait for green CI compilation, download artifact `RaidersHaveFamilies-RimWorld-1.7-v0.13.3-S2.1-HARMONY-RECOVERY-TEST`.
3. Start a **fresh pre-raid** save; collect startup Harmony logs and diagnostic 33; test 01 first, then 05, then 16, then 04 (now airdrop by design). Check objective ownership, one letter, no prisoner hostility, no uncontrolled assault, no job loops.
4. DO NOT claim 05/16 fixed until tested. On every failed action, send new `Player.log` and diagnostic 33.

## GitHub Desktop Summary

`RHF v0.13.3 S2.1 - Isolate Harmony hooks and restore rescue combat`
