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

**Secure Relay (57.7):** the one mid-expedition write to Storage. The relay moves one carried unit into Storage (its at-risk flag cleared there, `Storage.TryAddSecured`) and saves immediately (`secure_relay`); the open expedition marker is untouched, so failure, wipe or an abandoned run still resolve exactly as before for everything else, and the secured item is never part of the loss. A failed write keeps the autosave dirty and retries at the next safe point.

**Secure Relay escrow (co-op member):** a member moves its unit out of the run into `SaveSlot.RelayEscrow` and saves before asking the host, so the unit exists exactly once, on disk, while the verdict travels. Each escrow carries its state (`Pending` / `Accepted` / `Refused`), saved as soon as a verdict arrives. Only `Accepted` enters Storage (the host's acceptance, or its replay of a recorded acceptance after a reconnect; retried at the Shelter if Storage cannot take it). `Refused` returns the unit to the live run. An unresolved escrow is never secured: at the start of the Return/failure transaction (`ExpeditionService.Ending`) `BaseSession` puts it back into the carried inventory so the transaction treats it like any carried item, and on open after a process death it is lost with the abandoned run. Older saves have no escrow; the field is additive, so `CurrentVersion` is unchanged.
