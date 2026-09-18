# Save and Persistence

> **Status:** Approved design specification unless explicitly marked as tunable.
> **Game language:** English. Planning discussions may be in German, but all player-facing text, code naming, comments, and Claude implementation specs should be English.
## Permanent vs Expedition State

Permanent profile and current expedition state are separate.

Permanent: display name, XP, level, skill points/investment, banked coins, storage/capacity, base upgrades, safe loadout/settings references as appropriate.

Expedition: equipped at-risk items, backpack, ammo, active consumable, carried coins, current HP/life state, run seed/depth/runtime state as required.

## Save Transactions
At expedition start, carried loadout becomes explicitly at-risk and is no longer treated as safely stored. On successful extraction, at-risk gear + found loot are committed back to safe ownership and carried coins become banked coins. On failure/death, at-risk carried assets are destroyed for the relevant player; XP remains.

## Anti-Exploit Rule
Quitting/closing during an active solo expedition cannot restore the pre-run gear snapshot. MVP does not support restarting and resuming a solo run after application restart.

## Save Versioning
Every save has a version number and migration path. Do this from the beginning.

## Autosave Points
Important transactions: storage/loadout changes, trader purchase/sale, skill spending/respec, base upgrade, expedition start, successful extraction, expedition failure, return to base.
