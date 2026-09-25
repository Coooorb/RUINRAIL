# Inventory UI

> **Status:** Approved design specification unless explicitly marked as tunable.
> **Game language:** English. Planning discussions may be in German, but all player-facing text, code naming, comments, and Claude implementation specs should be English.
`Inventory` shows equipped slots and 8 backpack slots.

### Equipment Panel
- Primary Weapon.
- Secondary Weapon.
- Armor.
- Accessory.
- Active Consumable.

### Backpack
Two rows of 4 or another equally clear 8-slot layout.

Supported actions:
- Equip/Swap.
- Move.
- Drop.
- Set active consumable.

Solo inventory pauses gameplay. Co-op inventory does not pause the shared game.

When backpack is full, show `BACKPACK FULL`. Do not automatically open a huge replacement workflow. Swapping can be done through inventory.

### Backpack slot order (implementation note 2026-09-19)

Backpack slots are the player's own order. Moving between two backpack slots is a real slot operation: left click on an occupied slot picks it up, a click on an empty slot moves it to exactly that slot, a click on an occupied slot swaps the two, clicking the source again cancels; drag-and-drop does the same (empty target = move, occupied target = swap, same-definition stack = merge up to the stack limit with the remainder left in the source slot, invalid drop = nothing). Keyboard/controller select → confirm uses the same operation. The order is never compacted or re-sorted and survives close/reopen, other moves and save/load. Root cause fixed: the view model treated backpack → backpack as a no-op that reported success.
