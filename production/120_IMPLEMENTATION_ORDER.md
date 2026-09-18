# Implementation Order

> **Status:** Approved design specification unless explicitly marked as tunable.
> **Game language:** English. Planning discussions may be in German, but all player-facing text, code naming, comments, and Claude implementation specs should be English.
Implement in this order. Do not skip to content multiplication before the underlying loop is validated.

## Environment Preflight
Before Phase 0, use the pinned editor/package baseline in `ENVIRONMENT.md` and ensure the repo test harness can locate Unity. Model/toolchain upgrades are separate maintenance changes, not incidental implementation decisions.

## Phase 0 — Foundation
Project structure, assemblies, Bootstrap, Input System, pixel-perfect camera, grid/tile foundation, config/definition foundations, logging/validation basics.

## Phase 1 — Player Combat Prototype
Movement, 360 aim, dash, HP/damage/death, two weapon slots/switching, Pistol, Knife, projectile + melee, reload/ammo, one Grunt.

## Phase 2 — Weapon Framework
One reference weapon per 11 classes; validate shotgun, bow charge, blaster heat, rocket AoE, stagger/knockback.

## Phase 3 — Inventory and Item Instances
Slots, backpack, unique item instances, stacks, transfer, drop/pickup.

## Phase 4 — Rarity and Affixes
Common–Legendary, integer rolls, one reference legendary special.

## Phase 5 — Armor / Accessories / Consumables
Framework first, small reference subset, then catalog content.

## Phase 6 — Room System
Grid room prefabs, sockets, markers, metadata, validation. Build one biome first (Metro).

## Phase 7 — Dungeon Generator
Seeded graph, main path, branches, room selection, socket matching, validation.

## Phase 8 — Enemy Framework
Add normal archetypes one by one in unlock order.

## Phase 9 — Loot Loop
Chests, loot tables, coins, depth rarity, ammo-aware drops.

## Phase 10 — First Elite + First Boss
Tunnel Stalker + The Conductor.

## Phase 11 — Extraction Loop
Boss → reward → transit → return/deeper → depth scaling → death/full loss → XP retention. This establishes the first true vertical slice.

## Phase 12 — Base
Storage, loadout, trader, character station, workshop, transit, starter kit.

## Phase 13 — Persistence
Complete save/profile/at-risk transaction handling and exploit rules.

## Phase 14 — Multiplayer Foundation
Sessions/Relay/NGO, host/join code, player spawn/network movement.

## Phase 15 — Co-op Gameplay
Network combat, enemy authority, shared rooms/loot, coins, boss, transit vote, Downed/Dead/Revive/Spectator and scaling.

## Phase 16 — Remaining Content
Remaining biomes, elites, bosses, rooms, approved items/events, legendary catalog.
