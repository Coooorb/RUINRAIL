# Open Decisions — Core V1 Design Closed

> **Status:** Project control document.
> **Game language:** English.

The previously listed V1 design decisions have now been finalized and distributed to their owning specification files.

## Current State

There are **no intentionally open core V1 game-design decisions** from the former open-decision list.

Finalized items now include:
- Project title and core world terminology.
- Concrete V1 weapon catalog and Legendary weapon specials.
- Armor base values and Legendary passives.
- Accessory Intrinsics and all 16 Legendary accessory passives.
- Consumable rarities, stacks, effects, durations, healing, buffs, and grenade values.
- Normal Enemy, Elite, and Boss baseline stats.
- XP formula and Level 61 cap.
- Global stackable-stat caps.
- Ammo stack limits.
- Storage upgrade capacities and prices.
- Core economy prices and Base Trader offer/quality curve.
- Final MVP room-prefab production count.
- Music-track scope and audio coverage rule.
- Display-name validation/moderation behavior.
- Default keyboard/mouse and controller mappings.

## Do Not Invent

A closed V1 design does **not** authorize Claude to invent additional systems or rebalance approved values during unrelated implementation work.

If implementation exposes a missing low-level parameter that is not specified anywhere and materially affects gameplay, Claude must:
1. expose it through configuration,
2. use a clearly labeled temporary implementation value only when necessary to continue,
3. report that value in the task summary,
4. not present it as a new permanent design rule.

Ordinary implementation details that do not alter gameplay design (private helper naming, internal collection choice, editor layout, etc.) do not require a new game-design decision.
