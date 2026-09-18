# Inventory and Equipment Slots

> **Status:** Approved design specification unless explicitly marked as tunable.
> **Game language:** English. Planning discussions may be in German, but all player-facing text, code naming, comments, and Claude implementation specs should be English.

## Non-Negotiable Rules

- One item equals one slot for equipment.
- No grid-based item sizes.
- No dedicated trading UI; players can drop items for teammates.
## Equipped Slots

Every player has:
- Primary Weapon.
- Secondary Weapon.
- Armor.
- Accessory.
- Active Consumable.

Primary and Secondary are identical weapon slots. They do not restrict weapon class. Any two weapons may be carried, including two melee weapons or two rocket launchers.

## Backpack

Exactly **8 backpack slots**.

Rules:
- Every equipment item occupies exactly 1 slot.
- An additional weapon in the backpack occupies 1 slot.
- Ammo occupies backpack slots and stacks by ammo type.
- Consumables occupy backpack slots and stack with identical consumables.
- Different consumables require different stacks/slots.
- Coins do not occupy backpack space.
- Equipped items do not consume backpack slots.

## Consumable Slot

Only one consumable stack is active/equipped at a time. Additional consumables stay in the backpack. The player may swap which stack is active through inventory management.

## Mid-Run Equipment Changes

Equipment may be swapped during a dungeon by opening the inventory. In solo the inventory pauses the game; in online co-op it does not pause the world.
