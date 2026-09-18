# TASK 004 — Player Aiming

> **Status:** Approved design specification unless explicitly marked as tunable.
> **Game language:** English. Planning discussions may be in German, but all player-facing text, code naming, comments, and Claude implementation specs should be English.
## GOAL

Implement 360-degree aim with 8-direction body-facing output.

## FILES TO READ

- `player/11_PLAYER_CONTROLLER.md`
- `art/102_CAMERA_SORTING_LIGHTING.md`
- `technical/116_INPUT_SYSTEM.md`

## REQUIREMENTS

Mouse/right-stick aim vector, weapon pivot direction, expose 8-direction body-facing state.

## DO NOT IMPLEMENT

No weapon firing yet.

## ACCEPTANCE CRITERIA

Aim direction is independent from movement and supports 360 degrees while body facing resolves to 8 directions.

## TESTS

Run relevant EditMode/PlayMode checks where practical.

## EXPECTED FILES CHANGED

Only files directly required by this task and its tests.
