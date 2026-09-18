# TASK 010 — Dual Weapon Switching

> **Status:** Approved design specification unless explicitly marked as tunable.
> **Game language:** English. Planning discussions may be in German, but all player-facing text, code naming, comments, and Claude implementation specs should be English.
## GOAL

Implement Primary/Secondary weapon slots and switching.

## FILES TO READ

- `items/20_INVENTORY_SYSTEM.md`
- `player/11_PLAYER_CONTROLLER.md`

## REQUIREMENTS

Two equivalent weapon slots; direct Weapon1/Weapon2 and swap action; switching state exposes active weapon.

## DO NOT IMPLEMENT

No backpack/storage UI yet.

## ACCEPTANCE CRITERIA

Any two reference weapons can occupy the slots and switch reliably without class restrictions.

## TESTS

Run relevant EditMode/PlayMode checks where practical.

## EXPECTED FILES CHANGED

Only files directly required by this task and its tests.
