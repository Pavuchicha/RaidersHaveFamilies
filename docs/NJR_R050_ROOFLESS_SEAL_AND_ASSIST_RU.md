# NJR R050 — roofless fortification and squad prisoner defense

## Runtime evidence (R049)
- 7 planning attempts at ticks 40133–43913: 16–18 candidates, viable=0, noBenefit=candidates, seeds=0, exterior=1292.
- Pod at (122,152), launcher (121,152). End-of-session map shows constructed roof removed at (123,152) and (124,150); both roofed at start. The R049 graph rejected roofless boarding seeds and roofless wall candidates. The old geometry fixture did not represent roof clearance.
- Shoat (19434), unrelated colony prisoner, attempted a melee cast at another rescuer at 39886; first recorded damage to Skulk (36819) at 39962; Skulk returned a hit at 39963. Repeated damage until 47448; R047 stored the hit per victim, not per squad.
- VIP successfully launched; no change to pod construction/launch or ingress routing.

## Changes
- Trace exposed exterior from the local boundary along unroofed non-door cells. Roofless cells inside structural enclosure no longer count as automatically outside. Seed from physical boarding neighbor; prospective loose chunk seed only if no standable seed exists. Native pocket, builder and logistics checks remain mandatory. Candidate cells may be roofless; they must yield real geometry benefit. Retry selection after 150 ticks rather than 600. No decorative fallback or artificial wall spawning.
- Incoming prisoner damage authorizes at most two local melee responders, including victim and nearest eligible helper; protected VIPs excluded. Helpers must be within 6 cells and have visibility/reachability; contact expires after 360 ticks absent renewed damage. Preserve construction and supply workers as helpers; a personally attacked worker may defend itself. Reject unprovoked prisoner combat both in job guard and automatic verb start. No global faction changes.
- New trace fixture uses final recorded map (not live tick-by-tick roof changes), first damage and logs. Both possible launcher footprint origins checked. Existing tests changed only where new behavior supersedes older assumptions or revision string.
- CI Python contracts/model scripts: 107/107 passed (in-process). No RimWorld 1.7 assemblies/dotnet available here; DLL build and CE/native construction, actual assistance timing still need GitHub CI and game playtest. The post-clearance map is a model, not a proof of native job completion.
