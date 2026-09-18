# TASK 008 — Pistol Reference

> **Status:** Approved design specification unless explicitly marked as tunable.
> **Game language:** English. Planning discussions may be in German, but all player-facing text, code naming, comments, and Claude implementation specs should be English.
## GOAL

Implement the first reference Pistol weapon.

## FILES TO READ

- `items/23_WEAPON_FRAMEWORK.md`
- `items/24_WEAPON_CLASSES.md`
- `items/26_AMMO_SYSTEM.md`

## REQUIREMENTS

Basic projectile fire, small random damage range, magazine, reserve Light Ammo, reload.

## DO NOT IMPLEMENT

No rarity/affixes, legendary special, inventory system, crits.

## ACCEPTANCE CRITERIA

Pistol can fire/reload from configured stats and correctly consumes Light Ammo.

## TESTS

Run relevant EditMode/PlayMode checks where practical.

## EXPECTED FILES CHANGED

Only files directly required by this task and its tests.
