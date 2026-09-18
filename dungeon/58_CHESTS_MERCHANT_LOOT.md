# Chests, Dungeon Merchant, and Loot

> **Status:** Approved design specification unless explicitly marked as tunable.
> **Game language:** English. Planning discussions may be in German, but all player-facing text, code naming, comments, and Claude implementation specs should be English.
## Chest Types

1. **Supply Chest** — common; coins, ammo, consumables, small equipment chance.
2. **Equipment Chest** — guarantees at least one weapon/armor/accessory.
3. **Treasure Chest** — rare; guaranteed equipment and improved rarity table.
4. **Boss Cache** — after boss; guaranteed equipment + coins + additional item chance, using a strongly improved table.

Avoid a large hierarchy of colored chest tiers.

## Dungeon Merchant
Safe room. Uses Carried Coins.

Initial offer layout target: 5 slots:
- 2 Equipment.
- 1 Consumable.
- 1 Ammo.
- 1 Random category.

No in-room refresh button. Offers scale in quality with depth. Player may sell dungeon-held items for Carried Coins.

## Shared Co-op Loot
Equipment is a single shared pickup. Coins found as world currency are distributed evenly across current players/party bookkeeping. Loot is not duplicated per player.

## Loot Categories
Only direct gameplay value:
- Weapons.
- Armor.
- Accessories.
- Consumables.
- Ammo.
- Coins.

No junk/crafting-material loot.
