# Core Design Pillars

> **Status:** Approved design specification unless explicitly marked as tunable.
> **Game language:** English. Planning discussions may be in German, but all player-facing text, code naming, comments, and Claude implementation specs should be English.
## 1. Risk vs. Reward
After every defeated boss, the team decides whether to extract and secure all carried value or descend to a harder depth. Death before extraction destroys all at-risk gear, backpack contents, ammo, consumables, and carried coins.

## 2. Loot Excitement
Equipment must be interesting to compare. Random whole-number affix rolls create better and worse versions of the same item. Legendary equipment earns its rarity through a unique mechanic, not merely larger numeric stats.

## 3. Fast, Readable Top-Down Combat
360-degree aiming, projectile readability, telegraphed attacks, dodge/dash movement, clear enemy roles, and strong hit feedback matter more than simulation complexity.

## 4. Persistent Safe Base
The base is a compact preparation hub. Successfully extracted items, banked coins, XP, level, and spent skill points remain safe between runs.

## Design Filter
A new feature should clearly strengthen at least one pillar without damaging the others. If it mainly adds management complexity without improving the core expedition loop, it should be cut or postponed.
