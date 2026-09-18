# Dungeon Events

> **Status:** Approved design specification unless explicitly marked as tunable.
> **Game language:** English. Planning discussions may be in German, but all player-facing text, code naming, comments, and Claude implementation specs should be English.
The MVP has **6 approved event types**.

## 1. Cursed Chest
Player chooses to open. Doors lock, a harder encounter spawns, and success awards high-quality loot.

## 2. Locked Vault
Pay Carried Coins to open a vault with guaranteed good loot. Cost follows the exact formula in `base/77_ECONOMY.md`.

## 3. Broken Machine
Pay Carried Coins using the exact formula in `base/77_ECONOMY.md` to attempt repair. Result can yield an item/ammo/consumable or simply fail. Do not add punitive hidden damage; this is a small gambling event.

## 4. Supply Signal
Activate and survive a ~30-second wave encounter (tunable). Success drops a Supply Chest/reward.

## 5. Medical Station
Pay Carried Coins for healing, or in co-op revive a fully Dead teammate. Prices follow `base/77_ECONOMY.md`. This replaces the need for a second separate recovery-room system.

## 6. Weapon Cache
Choose exactly one weapon from three random presented weapons. Once used, the event is consumed for the whole party.

## Co-op
The player triggering a paid event spends their own Carried Coins. Rewards are spawned into the shared world unless the event explicitly represents a single-choice selection.
