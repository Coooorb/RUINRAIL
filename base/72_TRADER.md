# Base Trader

> **Status:** Approved V1 design specification.
> **Game language:** English.

The Base Trader buys and sells items using **Banked Coins**.

## Trader Levels

| Trader level | Offers per refresh | Upgrade cost |
|---|---:|---:|
| 1 | **4** | — |
| 2 | **5** | **2,500 Banked Coins** |
| 3 | **6** | **7,000 Banked Coins** |

Trader inventory refreshes after **every ended Expedition**, whether the Expedition ended in successful extraction or failure. There are no real-time wait timers and no manual paid reroll for V1.

## Offer Quality

| Trader level | Common | Uncommon | Rare | Epic | Legendary |
|---:|---:|---:|---:|---:|---:|
| 1 | 50% | 35% | 13% | 1.9% | 0.1% |
| 2 | 35% | 40% | 20% | 4.7% | 0.3% |
| 3 | 25% | 38% | 28% | 8.4% | 0.6% |

The category mix may include Weapons, Armor, Accessories, Consumables, and Ammo. Legendary equipment remains rare even at Trader Level 3.

## Selling

Players may sell safe stored items for Banked Coins. V1 sale value is **35% of the item's current buy value**, rounded to the nearest 5 Coins. Affix roll quality does not alter sale value beyond the item's Rarity.

Base prices and Rarity multipliers are defined in `base/77_ECONOMY.md`.
