# Data Architecture

> **Status:** Approved design specification unless explicitly marked as tunable.
> **Game language:** English. Planning discussions may be in German, but all player-facing text, code naming, comments, and Claude implementation specs should be English.
Separate three kinds of data.

## Static Game Data
ScriptableObject definitions: weapons, armor, accessories, consumables, enemies, bosses, rooms, loot tables, balance/scaling configs.

## Runtime Data
Current HP, active inventory state, current dungeon graph/rooms, active enemies, ground loot, room state, current expedition state.

## Persistent Data
Display name, XP, level, skills, banked coins, storage, safe loadout, base upgrades, save version.

Never treat mutable ScriptableObject assets as the player's runtime/save state.
