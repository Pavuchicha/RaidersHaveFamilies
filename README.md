# Raiders Have Families — 0.2.0 EXPERIMENTAL

**RimWorld 1.7 | .NET 10 | Harmony 3 | optional Ideology and War Crimes Expanded 2**

**NOT a fully tested release.** GitHub Actions compiles the DLL from source. The authoring environment has no game executable, .NET SDK or NuGet access. Static checks pass; compile and in-game runtime must still be confirmed.

## Implemented in this preview

- Harmony post-spawn event, groups non-player humanlike pawn arrivals from raids, visitors, traders, and modded factions. Does not patch combat or the storyteller tick.
- Direct partner/fiancé/spouse/ex relations among newcomers where plausible. Parent-child and shared parents create game-derived sibling/half-sibling and further family ties. Creates a bounded number of off-map human relatives; exotic-race off-map generation is intentionally disabled pending race-specific integration.
- Per-visit budgets: 144 newcomer processing limit, 32 pair probes per pawn, 8 off-map pawn generation attempts maximum per arrival, maximum 512 active generated world relatives per save. Default values are adjustable and lower. World pawns created by this mod are pruned when they have no live relevant playable relatives; other mods' pawns are never deleted.
- Ransom menu added to a powered Comms Console's right-click interaction menu. Choose colony prisoner, demand silver or offer free release for goodwill. Payment follows the actual release and safe map exit. Return value depends on rank, missing parts, health and improved replacement body parts, including prosthetics from external mods.
- Faction-importance heuristic: regular member, notable member, officer, high official, actual faction leader. More important captives can trigger automatic ransom offers; rejecting them or disclosing serious abuses can schedule one amplified **vanilla** reprisal raid.
- Optional War Crimes Expanded 2 integration: patches only surgery workers attached to WCE2 recipes, recording deliberate operations by player doctors on prisoners; effects appear on disclosure, not at operation time. Records severity and current pain as a capped score. Ideology's pro-torture precepts and Bloodlust trait may give public cruelty memories with escalating but bounded effects.
- Detects slave state / changed ideology of tracked prisoners during rare maintenance. Concealed operation consequences are applied if a negotiated prisoner returns without prior disclosure; battle wounds do not automatically count as abuse.

## Explicit limitations (NOT implemented)

- Independent prison-breach AI, silent rescue, escape corridor / tactical demolition, drop-pod construction, roof removal, multi-faction coalition rescue and mercenary sponsorship.
- Hostage exchange by a dedicated visiting escort: currently the original prisoner Release option must be used, including for VIPs. Exiting alive completes the negotiated transfer.
- Custom hostile-settlement prisoner-rescue quests, talk-to-follow companion AI and escort-to-map-edge quest completion.
- Full event-level integration with all WCE2 operation outcomes, execution, body consumption, prisoner sale and religious conversion. WCE surgery hooks are experimental; missing/failed operations may not be distinguished reliably until the exact updated mod source is reviewed.
- Same-tile world-wide search for relatives in every modded race and faction hierarchy beyond the role heuristic. There is no guarantee of zero Harmony patch conflicts with other comms or ransom mods.

## GitHub build and install

Upload the **contents** of this archive to repository root, replacing previous files. Actions → latest `Build Raiders Have Families 0.2.0` → Artifacts → `RaidersHaveFamilies-RimWorld-1.7-v0.2.0-EXPERIMENTAL`. Install the **inside** `RaidersHaveFamilies` folder into RimWorld Mods. Remove the old 0.1.x folder and back up your save.

If `dotnet build` fails, read the error in the workflow log. Do not install an old 0.1.x DLL with 0.2 files.

## Performance and compatibility contract

- No per-tick pawn scans. On uneventful ticks, only scheduler comparisons and fast guards.
- Group relations processed once 120 ticks after spawned pawns enter a map, with explicit hard bounds.
- Only the colony prisoner list is checked once every 30,000 ticks. No WCE hediff polling in combat or pawn ticks.
- No direct patches to CE, prosthetic defs, xenotypes or modded faction defs. WCE2 compatibility is late-bound and optional.
- The active-kin cap prevents indefinite world pawn growth. Normal world pawn GC remains the game's responsibility.
- No Multiplayer synchronization integration is provided. Test separately alongside HostageTaker, Prisoner Diplomacy and other ransom mods to avoid overlapping offers.

**Performance is an architectural expectation, not a measured FPS/TPS claim.** Profile large raids and long saves with RimWorld's 1.7 Frame Profiler or Dubs Performance Analyzer.
