# RHF S4.1-A/I — native-only animal raid ownership and flank control

**REVIEW ONLY / GitHub not updated.**

The defect in S4.1-A/H was an incident ownership gap: `ObserveSpawn`
returned immediately for every non-human pawn. Thus `RHFNativeDoctrineS41`
could observe a beast Lord only through a registered human in the *same* Lord.
A separate animal-only Lord never entered the tactical state at all.

## Corrected ownership

- `RHFOperation.NativeAnimalsS41` is a separate saveable `Pawn` reference list.
  Animals are **never inserted into `Squad` or `Diversion`**, which avoids
  accidental human rescue commands.
- `ObserveSpawn` admits an animal **only if `activeOperation` and `activeParty`
  are set synchronously by the actual RHF incident**, the faction matches and
  the raid is not a rescue. No faction-wide recruitment between incidents.
- A synchronous before/after ID delta recovers animals whose `SpawnSetup`
  was omitted by a mod. It does not search for coincident faction animals
  outside the actual `TryExecuteRaid` call.
- An animal-only `Lord` is now observed through the persisted animal list;
  its native Lord, duty, attack behavior and handlers remain untouched.
- Friendly-fire evaluation treats registered native animals as allies even
  when RimWorld factions nominally remain hostile to each other.

## Smart support

At most once every 360 ticks, the observer records the nearest physically
visible hostile pawn within a bounded shortlist, retaining the last observed
cell for at most 900 ticks. Supporting human infantry use a bounded 12-cell
reachable flank search, aligned with the herd's actual front and its observed
approach direction. There is no magical tracking through closed walls, no
blanket claim of unrelated animal Lords and no global `TryTakeOrderedJob`.

## Known limitations

- **Late drop-pod animals** that appear only after the incident's synchronous
  invocation cannot safely be inferred from faction alone; the mod refuses
  to claim them without a stronger spawned-pod identity link. This requires
  a future identity-preserving transport arrival registration hook.
- **The animals themselves still use the vanilla Lord/ThinkTree.** This patch
  improves factual registration and tactical coordination, not custom animal
  pathfinding, target selection or retreat. Those require a separate safely
  owned animal Lord.
- Nothing in this candidate has passed RimWorld 1.7 compilation or actual CE
  battle tests. Static tests do not prove functionality in-game.
