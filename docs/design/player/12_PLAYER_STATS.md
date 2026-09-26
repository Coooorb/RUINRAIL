# Player Stats

> **Status:** Approved V1 design specification.
> **Game language:** English.

## Baseline

- Base Max HP: **100**.
- Movement Speed uses a **100% baseline**; exact Unity world-units/sec are tuned during feel testing without changing the percentage-based design model.
- No mana.
- No stamina.
- No permanent default shield.
- No crit chance or crit damage.

## Derived Sources

Final player stats combine:
1. Base stats.
2. Permanent skill-point bonuses.
3. Equipped armor base values.
4. Equipped accessory Intrinsic.
5. Equipment Affixes.
6. Temporary consumable/item/status effects where applicable.
7. Global caps from `player/16_GLOBAL_STAT_CAPS.md`.

Specialized damage-type reduction such as Blast Suit explosion reduction is not the same stat as general Damage Reduction. Apply the general DR cap first, then apply a specialized reduction multiplicatively to the remaining matching damage.
