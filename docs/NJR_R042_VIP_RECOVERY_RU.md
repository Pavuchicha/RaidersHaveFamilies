# NJR R042 — VIP recovery (2026-11-25)
Base R041. Scope: fortified VIP rescue #16, related exit recovery and mission-specific job gate.
## Evidence
Input `20261125_190714_302_89ae8f55.zip`: 18457 events, baseRevision R041. 
At 39216 the actual path (122,150) -> (118,152) was 7 cells, cost 16308, one steel wall (119,152), HP 300, zero recorded exposure. 
The recorded standable-cell grid admits a cardinal door-only path through (114,151), door HP 160, cost 656 without threat costs. The reason native threat-weighted search preferred a wall is NOT uniquely established. The old door diagnostic listed individual doors, not alternative complete corridors.
At 39846 VIP Hayhouse freed. At 39936 strict search checked 6914 pads and accepted zero; immediately triggered ground fallback. Stage 1 never began, so neither construction nor refuel was tested. R040 had physically launched a pod.
Shoat (non-target prisoner) initiated melee against engineer Шоён at 39942; engineer's subsequent AttackMelee from JobGiver_ReactToCloseMeleeThreat and wild boar's LordJob_AssaultColony attacks were recorded. Self-defense is not random unprovoked targeting. At 40026, 40116, 40656 the land fallback assigned additional access breaches.
## Changes
- VIP-only complete door-only comparison after a wall path, limited to local 32-cell goal radius. Prefer lower weighted cost or cheaper HP route with comparable threat and modest detour. Log both complete alternatives. VIP hazard cost uses current geometric LOS rather than blanket same-room exposure; LOS approximation cannot reproduce CE ballistics. Rescue-1 routing unchanged.
- Restore independent R040 direct site search and secondary R040 access search as fallback after strict R041 pad validation. Keep physical footprint and native construction checks. No instant ground fallback: limited retries every 450 ticks, 1800-tick search window, except ongoing real access. Log rejection categories (not independent counts for all predicates; score-pruned candidates may be skipped).
- VIP land fallback and withdrawal first attempt a complete door-only access route, exact authorization from shared controller; walls only after door-only search finds no edge. Other rescue operations retain legacy branch.
- Mission-specific StartJob gate handles recorded VIP rescue animals too, disallows unauthorized structural targets; non-target captive engagement attempts local disengagement if a safe cell exists. Permit immediate self-defense if no safe disengagement and captive actually attacking. Do not alter factions or suppress native CE.
- Guard cell selection excludes immediate vicinity of unrelated captives. Preserve R041 specialists, parallel fortify/refuel and actual transporter mechanics. Barriers, fuel work, building still require game confirmation.
## Verification
99/99 Python CI preflight source/model scripts passed (no .NET compiler/RimWorld runtime present). Updated legacy literal assertions for modified authorization, exposure signature, diagnostics revision. New fixture from actual recorded geometry and damage chronology; no exact CE replay. No DLL or installation ZIP produced. R042 is a candidate, not accepted gameplay.
