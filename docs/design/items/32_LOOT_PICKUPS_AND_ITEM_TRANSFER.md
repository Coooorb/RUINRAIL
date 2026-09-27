# Loot Pickups and Item Transfer

> **Status:** Approved design specification unless explicitly marked as tunable.
> **Game language:** English. Planning discussions may be in German, but all player-facing text, code naming, comments, and Claude implementation specs should be English.
## Ground Loot

Equipment exists as a single physical world pickup visible to all teammates. It requires an interaction to pick up. There is no instanced personal equipment loot.

Coins are shared evenly when found in co-op. Ammo and equipment remain world pickups/stacks as designed.

**Partial stack pickup** (2026-09-26): a stack pickup (ammo) takes whatever fits — the room left in the collector's matching stacks up to their normal cap, plus free slots — even when no slot is free; the rest stays on the ground, where it lay, with its exact remaining quantity. Nothing fits: the pickup is untouched. Whole fit: unchanged (the collector's pickup bonus applies to whole pickups, clipped to the room). Auto-pickup pulls only stacks that fit whole and takes a partial fit in place; manual pickup, auto-pickup and the co-op host arbiter share this one transaction.

## Dropping

Players may drop backpack items and equipped gear into the world, allowing simple teammate handoffs. Do not build a separate trade window for MVP.

A dropped auto-pickup stack (ammo) is held back from the dropper's own pickup attraction until the dropper has once been out of attraction reach of it — it stays where it fell instead of flying straight back; walking back over it later collects it normally, a deliberate interaction always works, and every other player collects it at once (2026-09-26). Naturally spawned pickups are unaffected.

In co-op, an attracted pickup is pulled toward one player at a time: the first player whose attraction (or Room Sweep) takes it keeps it until it is collected or stops being takeable; a player who can no longer pull it (Downed/Dead) releases it to the others. Overlapping reaches no longer pull a pile toward two players at once, which left it stuck between them (2026-09-26).

Dropped equipment does not time-despawn during the active depth. Anything left behind is removed when the party leaves the depth.

## Ownership Integrity

An equipment instance may exist in exactly one location at a time: storage, equipped slot, backpack, ground, merchant, etc. All movement must use a central validated transfer path to prevent duplication.
