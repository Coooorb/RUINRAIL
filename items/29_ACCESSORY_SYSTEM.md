# Accessory System

> **Status:** Approved design specification unless explicitly marked as tunable.
> **Game language:** English. Planning discussions may be in German, but all player-facing text, code naming, comments, and Claude implementation specs should be English.
Accessories are the main utility/build-shaping equipment slot.

## Intrinsic Effects

Every accessory has a fixed intrinsic effect even at Common rarity. Rarity then adds normal affixes:
- Common: intrinsic + 0 affixes.
- Uncommon: intrinsic + 1.
- Rare: intrinsic + 2.
- Epic: intrinsic + 3.
- Legendary: intrinsic + 3 plus a fixed legendary passive.

Legendary accessories do not use RMB.

## Philosophy

Accessories may improve movement, dash, projectile behaviour, reload/weapon handling, melee stats, healing, blaster heat/cooling, bow charge, ammo carrying, pickup range, knockback, or stagger. Use class-appropriate affix pools to avoid irrelevant rolls.

## Catalog References

Fixed Intrinsics: `items/30_ACCESSORY_CATALOG.md`.
Legendary passives: `items/34_LEGENDARY_ACCESSORY_PASSIVES.md`.
Global caps: `player/16_GLOBAL_STAT_CAPS.md`.
