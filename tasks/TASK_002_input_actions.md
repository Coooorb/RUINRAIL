# TASK 002 — Input Actions

> **Status:** Approved design specification unless explicitly marked as tunable.
> **Game language:** English. Planning discussions may be in German, but all player-facing text, code naming, comments, and Claude implementation specs should be English.
## GOAL

Create the approved Unity Input System action map.

## FILES TO READ

- `technical/116_INPUT_SYSTEM.md`
- `player/11_PLAYER_CONTROLLER.md`
- `technical/117_CODING_RULES_FOR_CLAUDE.md`

## REQUIREMENTS

Implement Move, Aim, Fire, Special, Dash, Reload, Interact, Weapon1, Weapon2, WeaponSwap, Consumable, Inventory with keyboard/mouse and baseline controller bindings.

## DO NOT IMPLEMENT

Do not implement gameplay responses to the actions yet.

## ACCEPTANCE CRITERIA

Input asset exists, actions are named consistently, and callbacks/adapter can expose intent cleanly.

## TESTS

Run relevant EditMode/PlayMode checks where practical.

## EXPECTED FILES CHANGED

Only files directly required by this task and its tests.
