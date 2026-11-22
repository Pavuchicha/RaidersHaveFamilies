# NJR — Not Just Raiders

**RimWorld 1.7 / experimental v0.14.0-beta2** · Formerly *Raiders Have Families*.

Raids have consequences. Enemy pawns may have relatives and loyalty ties; prisoner treatment can affect relations, ransom demands and faction responses. The mod also experiments with hostage rescue, coalition assaults, retaliation, social rank, combat doctrine, and physical VIP transport-pod extraction. **No claim that all 18 tactical scenarios are production-ready**: they require gameplay validation.

## Installation

GitHub Actions on this repository compile the project using `.NET 10`, `Krafs.Rimworld.Ref 1.7.5014-unstable`, and `Lib.Harmony 3.0.0-rc.7`, then package the installable mod. After a **successful** workflow run, install only the `RaidersHaveFamilies` folder from the install ZIP inside the RimWorld `Mods` directory. Remove the earlier copy so only one installation remains. Back up existing saves before testing beta builds.

## Save compatibility

The display name is **NJR — Not Just Raiders**. Internal identifiers deliberately **remain unchanged**: `packageId=pavuk.raidshavefamilies`, C# namespace, assembly name `RaidersHaveFamilies.dll`, DefNames, Scribe keys, and on-disk installation folder. This is intentional to preserve old mod lists and save references. Do not install it alongside the original: it is the **same mod**, renamed.

## Features and current evidence

- Family ties, optional kin generation, importance/rank 0–8, prisoner-family contact, ransom and diplomacy.
- Captivity consequences, actual Ideology conversion disclosure, retaliation and allied/mercenary faction responses.
- 18 developer-mode scenarios (rescue, punishment, combinations, animals, coalition, siege and VIP flight). The individual scenarios are **test harnesses**, not proof that every outcome works.
- Field evacuation uses physical building plans, materials, transport and world travel; save/load and long-route relay behavior still need in-game testing.
- Optional integration with WCE2 / Combat Extended is not claimed fully verified.

## Verification

`python3 tests/njr_200_integrity_audit.py` validates packaging, translations, the 18 debug scenarios and compatibility invariants. CI runs the historical regression suite, restores RimWorld references and compiles the DLL. Static tests cannot replace gameplay; use the in-game developer actions and collect `Player.log` for failures. Audit findings and coverage are in `docs/NJR_FULL_AUDIT_AND_RELEASE_RU.md`.

Version v0.14.0-beta2 describes a rebrand/consolidation based on S4.1-A/P, **not a declaration of 18 proven tactical AIs**. See [`docs/LEGACY_README_PRE_NJR.md`](docs/LEGACY_README_PRE_NJR.md) for historical release notes.
