# V1 Economy Baselines

> **Status:** Approved V1 balance specification.
> **Game language:** English.

RUINRAIL uses one currency: **Coins**. During an Expedition they are Carried Coins; after successful extraction they become Banked Coins.

## Rarity Price Multipliers

| Rarity | Buy-value multiplier |
|---|---:|
| Common | ×1.00 |
| Uncommon | ×1.35 |
| Rare | ×1.90 |
| Epic | ×3.00 |
| Legendary | ×5.00 |

Equipment buy value = authored Base Price × Rarity multiplier, rounded to the nearest 5 Coins.

Sell value = **35% of current buy value**, rounded to the nearest 5 Coins.

Affix roll quality does not further change price.

## Weapon Base Prices

| Class | Base Price |
|---|---:|
| Pistol | 250 |
| SMG | 350 |
| Assault Rifle | 450 |
| Battle Rifle | 500 |
| Shotgun | 450 |
| Sniper | 600 |
| Bow | 400 |
| Rocket Launcher | 700 |
| Blaster | 650 |
| Knife | 250 |
| Spear | 350 |

## Armor Base Prices

| Armor | Base Price |
|---|---:|
| Scrap Vest | 300 |
| Scout Rig | 350 |
| Riot Armor | 450 |
| Heavy Plate | 650 |
| Blast Suit | 500 |
| Medic Harness | 450 |
| Combat Harness | 400 |
| Reinforced Exo-Rig | 600 |
| Runner Suit | 350 |

## Accessory Base Price

All 16 Accessory families use a V1 base price of **300 Coins** before Rarity multiplier.

## Consumable Base Prices

| Consumable | Price |
|---|---:|
| Bandage | 60 |
| Frag Grenade | 80 |
| Medkit | 150 |
| Combat Stim | 140 |
| Smoke Grenade | 110 |
| Shock Grenade | 220 |
| Damage Stim | 250 |
| Armor Injector | 250 |
| Incendiary Grenade | 220 |
| Defibrillator | 1,500 |

## Ammo Trader Bundles

| Bundle | Price |
|---|---:|
| Light Ammo ×60 | 60 |
| Medium Ammo ×40 | 70 |
| Heavy Ammo ×20 | 90 |
| Shells ×12 | 80 |

> **Design update (2026-09-19, ECONOMY/CONTAINMENT/DEATH/PROJECTILES runtime fix pass, explicit owner instruction):** a bundle price is a price for the whole bundle, never a per-round value. **Ammo resale = 15 % of the equivalent current Merchant purchase value for the exact quantity sold (bundle price × quantity ÷ bundle units), rounded down** — no rarity multiplier, no 5-coin step rounding, and a payout of 0 for tiny quantities is legitimate (no minimum 1 coin). Applies to the Dungeon Merchant and the Shelter Trader. The percent is data on `EconomyConfig` (`AmmoSellPercentOfPurchaseValue`). Before this rule the 35 % item formula was applied to the bundle price *per round* (a 180-round Light stack sold for 3,600 Coins); a full Light bundle now sells for 9, a full 180 stack for 27.

## Dungeon Event Prices

All event prices use Carried Coins.

- Locked Vault: `250 + 25 × (Depth - 1)`, capped at **1,000**.
- Broken Machine: `100 + 10 × (Depth - 1)`, capped at **400**.
- Medical Station heal: `150 + 15 × (Depth - 1)`, capped at **600**.
- Medical Station Dead-teammate revive: `500 + 30 × (Depth - 1)`, capped at **1,500**.

## Base Upgrade Prices

- Skill Respec: **2,500 Banked Coins**.
- Storage: see `base/71_STORAGE.md`.
- Trader: see `base/72_TRADER.md`.
