# Weapon Framework

> **Status:** Approved design specification unless explicitly marked as tunable.
> **Game language:** English. Planning discussions may be in German, but all player-facing text, code naming, comments, and Claude implementation specs should be English.
## Shared Ranged Weapon Stats

- Damage Min / Damage Max.
- Fire Rate.
- Magazine Size where applicable.
- Reload Time where applicable.
- Projectile Speed.
- Spread.
- Range.
- Knockback.
- Stagger Power.
- Ammo Type where applicable.

Damage is a small random integer/whole-value range such as 7–9. There are no critical hits or weak spots.

## Shared Melee Weapon Stats

- Damage Min / Damage Max.
- Attack Speed.
- Attack Range.
- Attack Arc / shape.
- Wind-up / recovery as needed.
- Knockback.
- Stagger Power.

Melee uses no stamina and no ammo.

## Projectile Philosophy

Ranged attacks use visible projectiles rather than hitscan as the default. Very fast weapons may use extremely fast projectiles, but enemy and player combat should remain visually readable and dodgeable where appropriate.

Normal guns do not require a complicated distance-damage falloff system. Range/spread handles practical effective distance.

## Durability

No weapon durability.

## Architecture

Do not implement one giant weapon class with weapon-type conditionals. Use shared weapon definitions plus reusable attack behaviours such as projectile, shotgun, bow, blaster, melee, and rocket/explosion behaviour.
