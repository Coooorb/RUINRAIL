# TASK 009 — Knife Reference

> **Status:** Approved design specification unless explicitly marked as tunable.
> **Game language:** English. Planning discussions may be in German, but all player-facing text, code naming, comments, and Claude implementation specs should be English.
## GOAL

Implement reference Knife melee behaviour.

## FILES TO READ

- `items/23_WEAPON_FRAMEWORK.md`
- `items/24_WEAPON_CLASSES.md`
- `combat/42_STAGGER_KNOCKBACK.md`

## REQUIREMENTS

Fast short-range attack with configurable wind-up/recovery/range/damage; no stamina.

## DO NOT IMPLEMENT

No Spear yet; no combo tree.

## ACCEPTANCE CRITERIA

Knife hitbox matches visual/test arc and cannot double-hit unintentionally per swing.

## TESTS

Run relevant EditMode/PlayMode checks where practical.

## EXPECTED FILES CHANGED

Only files directly required by this task and its tests.
