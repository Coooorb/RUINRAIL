# Base UI

> **Status:** Approved design specification unless explicitly marked as tunable.
> **Game language:** English. Planning discussions may be in German, but all player-facing text, code naming, comments, and Claude implementation specs should be English.
Approved station screens:
- Storage: item grid + filters/sort, shown as the **stash** — the survivor's worn slots and backpack beside the paged Storage grid; click / confirm / drag moves the chosen item across (a Storage item dropped on a worn slot is equipped), STORE WHOLE BACKPACK stores the bag in one action, and a refused move shows its rule (Storage full, backpack full). After a successful return with loot, the STORAGE tab and the survivor card point at the stash until it is opened.
- Loadout: equipped slots + backpack + storage source.
- Trader: Buy / Sell.
- Character: Level, XP, Skill Points, six attributes, Respec.
- Workshop: approved base upgrades.
- Multiplayer: Solo / Host / Join, join code, Ready states.
- Transit: Start Expedition; coins for the run — `-step` / amount / `+step`, NONE / ALL, with banked now, taking and what stays banked beside it (77).

## Main Menu
Initial menu:
- PLAY.
- SETTINGS.
- QUIT.

`PLAY` enters/loads the base; multiplayer organization happens inside the base rather than a sprawling main-menu flow.
