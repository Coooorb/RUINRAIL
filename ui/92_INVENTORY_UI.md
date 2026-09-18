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
