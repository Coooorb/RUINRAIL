# Scope and Non-Goals

> **Status:** Approved design specification unless explicitly marked as tunable.
> **Game language:** English. Planning discussions may be in German, but all player-facing text, code naming, comments, and Claude implementation specs should be English.

## Non-Negotiable Rules

- Claude must not invent excluded systems as “future-proofing.”
- Keep the MVP focused on the approved loop.
## Explicitly Out of Scope for the Initial MVP

- PvP.
- Dedicated servers.
- Host migration.
- Public matchmaking.
- Character classes.
- Character active abilities or ultimates.
- Skill trees beyond the six permanent fundamental attributes.
- Critical hits or critical-hit stats.
- Weak spots/headshots.
- Stamina.
- Weapon durability or repairs.
- Weapon attachment/modding systems.
- Crafting materials and crafting recipes.
- Multiple currencies.
- Open world.
- Fully procedural room geometry.
- Hunger, thirst, survival needs.
- Pets or combat companions.
- Auction house or dedicated trade UI.
- Equipment set bonuses, sockets, gems.
- Base-building placement.
- Story campaign/dialogue-tree/quest infrastructure.
- Daily quests, battle pass, or monetization systems.

## Scope Philosophy

Use one system to serve multiple content pieces. Examples: one enemy AI foundation with reusable attack behaviours, one room format across all biomes, one item-instance model across weapons/armor/accessories, and one currency (Coins).
