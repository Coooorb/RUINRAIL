# Legendary Accessory Passives

> **Status:** Approved V1 design specification.
> **Game language:** English.

A Legendary Accessory keeps its family Intrinsic, rolls 3 normal positive Affixes, and gains exactly one fixed passive from this catalog. Legendary Accessories never use RMB/LT; active Special attacks belong only to Legendary Weapons.

| Accessory | Passive | Final V1 effect |
|---|---|---|
| **Runner's Watch** | **Momentum** | After Dash, gain **+10% Movement Speed for 3s**. |
| **Field Scope** | **Steady Aim** | After **1s with no character movement**, Projectile Weapons deal **+12% Damage**. Aim rotation is allowed. Effect ends immediately when movement resumes. |
| **Combat Bracelet** | **Flow State** | Melee kill grants **+15% Melee Attack Speed for 4s**. Additional melee kills refresh the duration. |
| **Quickdraw Holster** | **Hot Swap** | Weapon Swap grants the newly active weapon **+15% Fire/Attack Speed for 3s**. Internal cooldown: **6s**. |
| **Loader's Glove** | **Fresh Mag** | After completing a reload, the next **3 weapon attacks** deal **+15% Damage**. For Shotguns this counts shots, not individual pellets. |
| **Cooling Module** | **Cold Start** | If a Blaster cools from **50+ Heat** all the way to 0, its next **5 shots generate 50% less Heat**. |
| **Heat Sink** | **Emergency Vent** | When a Blaster Overheats, emit a **2.5-tile Energy Pulse** dealing **30–40 Damage** to enemies. Internal cooldown: **8s**. |
| **Archer's Ring** | **Perfect Draw** | A fully drawn Bow shot penetrates the **first enemy hit** without losing damage. |
| **Dash Capacitor** | **Discharge** | Dash endpoint emits a small Shockwave with **high Knockback and Stagger**. No direct damage. Internal cooldown: **6s**. |
| **Rangefinder** | **Long Shot** | A projectile that travels at least **7 tiles** before hitting deals **+15% Damage**. Use actual travel distance from projectile spawn to hit. |
| **Trauma Pendant** | **Second Pulse** | Healing Consumables additionally restore **15% of their normal healing amount over 5s**. |
| **Ammo Pouch** | **Scavenger's Reserve** | Ammo pickups grant **25% more ammo**, rounded to the nearest whole ammo unit. |
| **Magnetic Coil** | **Room Sweep** | On Combat Room clear, all remaining **Coins and Ammo pickups** in that room are pulled to the wearer. Equipment and Consumables are not auto-collected. |
| **Stabilizer** | **Lock-In** | After **1s of continuous firing**, Spread is reduced by an additional **25%** until firing stops. |
| **Impact Module** | **Wallbreaker** | A normal Enemy or Elite knocked into a wall takes **15–20 bonus Damage** and high Stagger. Per-target cooldown: **2s**. Bosses cannot trigger this effect. |
| **Shock Charm** | **Arc Stagger** | When the wearer Staggers an enemy, emit a **2.5-tile Shockwave** that applies high Stagger to nearby enemies. Internal cooldown: **6s**. |

All bonuses remain subject to applicable global caps in `player/16_GLOBAL_STAT_CAPS.md`.
