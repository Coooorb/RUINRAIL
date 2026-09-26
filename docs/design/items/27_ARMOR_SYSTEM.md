# Armor System

> **Status:** Approved design specification unless explicitly marked as tunable.
> **Game language:** English. Planning discussions may be in German, but all player-facing text, code naming, comments, and Claude implementation specs should be English.
Armor is a single equipment slot. There are no separate helmets, chest pieces, leggings, or boots.

## Core Armor Identity

Armor primarily affects survivability through base properties such as Max HP and Damage Reduction. Some armor families deliberately emphasize mobility, healing, blast resistance, or stagger resistance.

## Rarity

- Common: base armor only.
- Uncommon: +1 random positive affix.
- Rare: +2.
- Epic: +3.
- Legendary: +3 affixes at Epic-like quality plus one fixed hand-designed legendary passive.

Legendary armor never receives an RMB action. RMB special attacks belong only to legendary weapons.

## Potential Armor Affixes

Approved V1 integer Affix categories use data-driven ranges; the common starting ranges include:
- Max HP +6–10%.
- Damage Reduction +2–5%.
- Movement Speed +5–9%.
- Healing Received +8–14%.
- Dash Cooldown Reduction +6–10%.
- Knockback Resistance +8–15%.
- Stagger Resistance +8–15%.

Random affixes remain positive. A specific armor family may carry a deliberate base trade-off such as a small movement penalty if explicitly authored.

## Caps and Catalog

Base Armor values and Legendary passives are in `items/28_ARMOR_CATALOG.md`. Combined player stats obey `player/16_GLOBAL_STAT_CAPS.md`.
