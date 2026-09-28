# Dungeon Events

> **Status:** Approved design specification unless explicitly marked as tunable.
> **Game language:** English. Planning discussions may be in German, but all player-facing text, code naming, comments, and Claude implementation specs should be English.
The MVP has **6 approved event types** plus the **Secure Relay** (§7, design update 2026-09-26). Event rooms draw from the five non-medical events and the Secure Relay with equal weight (§7 Rarity); the Medical Station lives in Medical rooms.

## 1. Cursed Chest
Player chooses to open. Doors lock, a harder encounter spawns, and success awards high-quality loot.

## 2. Locked Vault
Pay Carried Coins to open a vault with guaranteed good loot. Cost follows the exact formula in `base/77_ECONOMY.md`.

## 3. Broken Machine
Pay Carried Coins using the exact formula in `base/77_ECONOMY.md` to attempt repair. Result can yield an item/ammo/consumable or simply fail. Do not add punitive hidden damage; this is a small gambling event.

## 4. Supply Signal
Activate and survive a ~30-second wave encounter (tunable). Doors lock on activation and open when the survival resolves (the Cursed Chest's lockdown). Success drops a Supply Chest/reward.

## 5. Medical Station
Pay Carried Coins for healing, or in co-op revive a fully Dead teammate. Prices follow `base/77_ECONOMY.md`. This replaces the need for a second separate recovery-room system.

## 6. Weapon Cache
Choose exactly one weapon from three random presented weapons. Once used, the event is consumed for the whole party.

## 7. Secure Relay (design update 2026-09-26, explicit owner request)
A rare, non-combat terminal in an Event room. Each player may use it **once** to send exactly **one** carried item from the current run permanently into **their own Shelter Storage**.
- **Rarity:** an Event room without an authored `event:<kind>` tag picks its event with one seeded, equal-weight draw over six events — Cursed Chest, Locked Vault, Broken Machine, Supply Signal, Weapon Cache and the Secure Relay (1 in 6 each; design update 2026-09-28, replacing the earlier separate 8% relay roll). The Medical Station is not in this pool (Medical rooms only). Never guaranteed; an authored `event:secure_relay` tag pins it.
- **Eligible:** one Weapon, Armor or Accessory, or **one unit** of a Consumable stack — from the worn slots or the backpack. **Never:** Coins (not items), Ammo, Starter Kit gear (unsellable, 75).
- **Transfer:** one transaction — the unit leaves the run and lands in Storage no longer at risk, or nothing changes (Storage full or any refusal: nothing removed, no use spent). The save is written at once (113); the secured item stays safe through a later death, wipe or abandoned run. It cannot come back into the current run (Storage is only reachable at the Shelter).
- **Per player:** every member has an independent single use, keyed by participant id (a reconnect cannot reset it). The relay never resolves for the party; a member who used it sees the terminal's ITEM SECURED state (screen, world sprite, prompt `— ITEM SECURED`).
- **Screen:** worn slots + backpack as inventory slots (refused items shaded with a red tab and a reason), item details, SECURE → CONFIRM, LEAVE. Mouse, keyboard and controller.
- **Co-op authority:** the member first moves the unit out of its run into a saved escrow, then sends a request (transaction id + instance id); the host checks the item is in that member's own carried containers, is eligible and that the member has not used this relay, takes its copy of the unit and records the use and the grant (per participant and transaction). Accepted → the member's escrow goes into its Storage; an explicit refusal → back into its run. Only the host's acceptance secures: a verdict lost with the link is recovered by re-sending the same transaction after the reconnect (the host replays its recorded verdict, never decides again); a request the host never decided stays **unresolved** (the escrow's persisted state is Pending, distinct from Accepted) and is never secured — if the run ends first its unit rejoins the run before the Return/failure transaction (home with an extraction, lost with a death/wipe), and if the process dies mid-run it is lost with the abandoned run. The host decides nothing after its run has ended, so every acceptance reaches a connected member ahead of the run-end message. Replays of a transaction id return the stored verdict. Inherent limit: a member whose link drops right after the host's acceptance and who never reconnects before the run ends cannot learn it; that unit follows its failed run.

## Co-op
The player triggering a paid event spends their own Carried Coins. Rewards are spawned into the shared world unless the event explicitly represents a single-choice selection.

## Interaction Contract (implementation note 2026-09-20)
- Every event object draws its final art, answers the Interact prompt while it is still open, and the prompt states the cost and — when the press would be refused — why (`REPAIR BROKEN MACHINE (100 COINS) — NEED 63 MORE COINS`, `USE MEDICAL STATION (150 COINS) — HP FULL`, `— USED`). A refused press announces its reason on the HUD notice and changes nothing.
- A successful press executes the outcome exactly once and announces it (reward names, failed repair with the coins spent, started wave, purchased heal); the object dims to its used state, offers no prompt and cannot repeat; a revisit restores the used state. Every event room can be exited afterwards.
- Broken Machine (57.3) is the paid seeded repair as specified: the coins are spent whether the machine yields an item / ammo / consumable or simply fails; there is no option menu because the design defines none.
- The transit car in the Boss Room offers `BOARD TRANSIT` once the decision is open; boarding restates the open choice.
