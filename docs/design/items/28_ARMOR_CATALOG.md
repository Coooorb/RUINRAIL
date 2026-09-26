# Armor Catalog

> **Status:** Approved V1 design specification.
> **Game language:** English.

The MVP has exactly **9 armor families**. Hazmat Suit is explicitly not part of V1.

## Base Armor Values

| Armor | Max HP | General DR | Additional base property |
|---|---:|---:|---|
| **Scrap Vest** | +20 | +4% | Balanced. |
| **Scout Rig** | +12 | +2% | +5% Movement Speed. |
| **Riot Armor** | +18 | +7% | Defensive armor. |
| **Heavy Plate** | +35 | +9% | -4% Movement Speed. |
| **Blast Suit** | +22 | +5% | 30% specialized Explosion Damage reduction. |
| **Medic Harness** | +18 | +3% | +20% Healing Received. |
| **Combat Harness** | +16 | +3% | +4% Weapon Damage. |
| **Reinforced Exo-Rig** | +30 | +6% | +25% Stagger Resistance. |
| **Runner Suit** | +10 | +2% | +8% Movement Speed. |

All values participate in the appropriate global caps. Blast Suit's specialized explosion reduction is applied separately/multiplicatively after general Damage Reduction.

## Legendary Passives

Legendary Armor has the same armor-family base identity, 3 random Affixes, and the fixed passive below.

| Armor | Passive | Final V1 effect |
|---|---|---|
| Scrap Vest | **Patchwork** | After clearing a Combat Room, restore HP equal to **6% of Max HP**. |
| Scout Rig | **Momentum** | After Dash, gain **+12% Movement Speed for 1 second**. |
| Riot Armor | **Anchored** | Fully negate one incoming stagger effect every **8 seconds**. Damage still applies normally. |
| Heavy Plate | **Last Stand** | While below **25% HP**, gain **+15% General Damage Reduction**, still subject to the temporary DR cap. |
| Blast Suit | **Shock Absorber** | Ignore knockback caused by explosions. Explosion damage still applies with normal reductions. |
| Medic Harness | **Emergency Care** | The first healing Consumable used in each Combat Room heals **25% more**. |
| Combat Harness | **Adrenaline** | Kill an enemy: **+10% Movement Speed for 3 seconds**. Internal cooldown: **5 seconds**. |
| Reinforced Exo-Rig | **Exo Lock** | Taking a single hit of **20+ final damage** grants **+30% Stagger and Knockback Resistance for 5 seconds**. Internal cooldown: **8 seconds**. |
| Runner Suit | **Second Wind** | The first time HP falls below **30%** in a Combat Room, immediately reset Dash cooldown. Once per Combat Room. |
