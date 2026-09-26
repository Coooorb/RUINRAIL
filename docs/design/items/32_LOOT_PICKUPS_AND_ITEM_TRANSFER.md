# Loot Pickups and Item Transfer

> **Status:** Approved design specification unless explicitly marked as tunable.
> **Game language:** English. Planning discussions may be in German, but all player-facing text, code naming, comments, and Claude implementation specs should be English.
## Ground Loot

Equipment exists as a single physical world pickup visible to all teammates. It requires an interaction to pick up. There is no instanced personal equipment loot.

Coins are shared evenly when found in co-op. Ammo and equipment remain world pickups/stacks as designed.

## Dropping

Players may drop backpack items and equipped gear into the world, allowing simple teammate handoffs. Do not build a separate trade window for MVP.

Dropped equipment does not time-despawn during the active depth. Anything left behind is removed when the party leaves the depth.

## Ownership Integrity

An equipment instance may exist in exactly one location at a time: storage, equipped slot, backpack, ground, merchant, etc. All movement must use a central validated transfer path to prevent duplication.
