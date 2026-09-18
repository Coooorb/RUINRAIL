# Leveling and Skill Points

> **Status:** Approved V1 design specification.
> **Game language:** English.

## Permanent Progression

XP is permanent. XP earned during an expedition is retained even if the expedition later fails. Every level gained awards exactly **1 Skill Point**.

Skill points may only be spent in The Shelter, preventing menu downtime during active co-op combat.

## Attributes

| Attribute | Bonus per point | Max points | Max bonus |
|---|---:|---:|---:|
| Vitality | +2 Max HP | 10 | +20 HP |
| Power | +1% weapon damage | 10 | +10% |
| Mobility | +1% movement speed | 10 | +10% |
| Recovery | +2% healing effectiveness | 10 | +20% |
| Handling | +1% reload speed and weapon switch speed | 10 | +10% each |
| Resilience | +2% knockback and stagger resistance | 10 | +20% each |

## Level Cap

**Level 61.** The player starts at Level 1 and can earn 60 points across Levels 2–61, enough to eventually max all six attributes.

This is intentionally not a build-defining skill tree. Gear remains the main source of build identity.

## XP Formula

For current Level `L`, the XP required to reach `L + 1` is:

`XPToNextLevel = 250 + 50 × (L - 1) + 5 × (L - 1)^2`

Whole-number XP only.

Reference values:

| Current Level | XP to next level |
|---:|---:|
| 1 | 250 |
| 10 | 1,105 |
| 30 | 5,905 |
| 60 | 20,605 |

Total XP required from Level 1 to Level 61: **454,550 XP**.

At Level 61, XP progression stops for V1. No prestige system is part of the MVP.

## Respec

The Character Station resets all allocated Skill Points for **2,500 Banked Coins**. Level and XP are unchanged.
