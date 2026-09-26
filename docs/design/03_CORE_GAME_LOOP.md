# Core Game Loop

> **Status:** Approved design specification unless explicitly marked as tunable.
> **Game language:** English. Planning discussions may be in German, but all player-facing text, code naming, comments, and Claude implementation specs should be English.
## Meta Loop

1. Enter the safe base.
2. Manage storage and loadout.
3. Spend banked coins on useful services/upgrades.
4. Spend permanent skill points.
5. Choose solo, host co-op, or join co-op.
6. Start an expedition.
7. On return, secure and organize extracted loot, then repeat.

## Expedition Loop

1. Enter Depth 1.
2. Clear rooms containing enemies, traps, events, loot, merchants, and occasional elite mini-boss encounters.
3. Find weapons, armor, accessories, consumables, ammo, and coins.
4. Manage limited backpack space.
5. Reach and defeat the biome boss.
6. Collect boss rewards.
7. Board the underground transit.
8. Choose:
   - **Return to Shelter** — all surviving players' carried loot is secured.
   - **Descend Deeper** — enter the next, harder depth with everything still at risk.
9. Repeat indefinitely until extraction or expedition failure.

## Failure

In solo, reaching 0 HP immediately fails the expedition. In co-op, a player can become downed and later dead; the expedition fails when no player remains alive and capable of reviving another player. XP remains permanent even on failure.
