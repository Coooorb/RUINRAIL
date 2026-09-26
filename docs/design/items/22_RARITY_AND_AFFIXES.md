# Rarity and Affix System

> **Status:** Approved design specification unless explicitly marked as tunable.
> **Game language:** English. Planning discussions may be in German, but all player-facing text, code naming, comments, and Claude implementation specs should be English.

## Non-Negotiable Rules

- No crit affixes.
- No weak-spot affixes.
- No decimal affix rolls.
## Equipment Rarities

| Rarity | Random affixes | Extra mechanic |
|---|---:|---|
| Common | 0 | none |
| Uncommon | 1 | none |
| Rare | 2 | none |
| Epic | 3 | none |
| Legendary | 3 | weapon special or armor/accessory legendary passive |

Legendary affix quality is not numerically above Epic by default. Legendary value comes from its unique fixed mechanic.

## Integer Rolls Only

Affix values are always whole integers. Example Damage affix range `+6%` to `+10%` can only roll 6, 7, 8, 9, or 10. No decimal roll values.

Example initial ranges (tunable):
- Damage: +6–10%.
- Fire Rate: +5–9%.
- Reload Speed: +8–14%.
- Magazine Size: +10–20%.
- Projectile Speed: +8–15%.
- Range: +8–15%.
- Knockback: +10–20%.
- Stagger Power: +8–16%.

Item/class-specific affix pools prevent nonsense combinations. Blasters can roll heat/cooling affixes, bows can roll charge-related affixes, melee can roll melee-specific stats.

## Positive Affixes

Random affixes are positive only. Deliberate negatives may exist only on specifically hand-designed items where a trade-off is part of the item's identity.

## Consumables

Consumables have a fixed rarity used only as a drop-frequency/category label. They have no random rarity versions, affixes, or rolls.
