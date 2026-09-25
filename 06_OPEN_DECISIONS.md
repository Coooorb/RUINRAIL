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

Decisions finalized by the later passes (2026-09-19 → 2026-09-25), recorded here so this list matches the tree:
- Co-op V1: 1–3 players, PvE only, host-authoritative NGO over UnityTransport; the local proof joins by direct address, live UGS Sessions/Relay needs a linked project (external configuration). No PvP, matchmaking, dedicated servers or host migration.
- Post-Depth-30 reward continuation (bounded, flat through D30) and the persistent personal deepest-depth record (`dungeon/59`).
- Seeded boss attack selection and boss anti-kite repositioning; biome encounter weighting (weighting, never exclusion).
- D1 ammo / Blaster fine-tuning values and the Field Knife values as frozen in `production/FINAL_RELEASE_FROZEN_BASELINE.csv`.

## Do Not Invent

A closed V1 design does **not** authorize Claude to invent additional systems or rebalance approved values during unrelated implementation work.

If implementation exposes a missing low-level parameter that is not specified anywhere and materially affects gameplay, Claude must:
1. expose it through configuration,
2. use a clearly labeled temporary implementation value only when necessary to continue,
3. report that value in the task summary,
4. not present it as a new permanent design rule.

Ordinary implementation details that do not alter gameplay design (private helper naming, internal collection choice, editor layout, etc.) do not require a new game-design decision.
