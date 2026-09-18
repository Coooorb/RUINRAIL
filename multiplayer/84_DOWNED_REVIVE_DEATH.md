# Downed, Revive, and Death

> **Status:** Approved design specification unless explicitly marked as tunable.
> **Game language:** English. Planning discussions may be in German, but all player-facing text, code naming, comments, and Claude implementation specs should be English.
## Downed

Co-op only. At 0 HP while at least one teammate is alive:
- Enter Downed.
- Initial bleedout timer: 20 s.
- Cannot attack, dash, use items, inventory, or normal interactions.
- Can crawl slowly.

## Standard Revive

A living teammate holds Interact near the Downed player.
- Initial revive time: 4 s.
- Moving away, attacking, dashing, or taking damage interrupts the revive.
- Successful revive returns the player at 30% Max HP.
- Initial revive protection: ~1.5 s.

No “three downs then instant death” counter.

## Dead

Bleedout reaches 0 → Dead. The player's carried equipment does **not** drop for teammates. This prevents bypassing full-loot-loss by salvaging a dead teammate's gear.

Dead players can return through:
1. Legendary Defibrillator consumable.
2. Medical Station event for Carried Coins.

Return at roughly 30% HP; exact values tunable.

## Team Wipe

If every party member is Downed/Dead and therefore nobody can perform a revive, fail the expedition immediately. Do not wait for all bleedout timers.

## Extraction

A player who is still Dead when the team Returns to Shelter loses all at-risk carried gear/loot/coins. Living players secure theirs normally.
