# Extraction and Transit

> **Status:** Approved design specification unless explicitly marked as tunable.
> **Game language:** English. Planning discussions may be in German, but all player-facing text, code naming, comments, and Claude implementation specs should be English.
## Theme

There is no fantasy portal. The game uses an old underground armored metro/industrial transit system connecting the shelter and deeper sectors.

## Post-Boss Flow

1. Boss dies.
2. Boss reward/cache spawns.
3. Room becomes safe.
4. Transit arrives/activates.
5. Players board.
6. Choice appears:
   - **RETURN TO SHELTER** — secure surviving players' carried items and convert carried coins to banked coins.
   - **DESCEND TO DEPTH N+1** — keep all gear at risk and enter a newly generated harder dungeon.

Depth is theoretically infinite.

## Team Rule
The party stays together. No individual extraction and no split-depth parties.

## New Depth Heal (implementation note 2026-09-20)
When the party chooses **DESCEND** and the next depth has been generated, every living expedition participant begins that depth at their own **effective** maximum HP (`PlayerStats.MaxHealth` over the current loadout: base + armor / accessory / affix modifiers — 120/120 with a Scrap Vest, never a hard-coded base). The heal runs exactly once per successful depth transition, through the ordinary host-authoritative heal path; it never resurrects a Dead or Downed participant, and it is never triggered by entering or revisiting a room, opening or reopening the Transit decision, voting, rebuilding inside the same depth, equipment changes or menus. Returning to the Shelter does not use it.
