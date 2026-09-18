# TASK 003 — Player Movement

> **Status:** Approved design specification unless explicitly marked as tunable.
> **Game language:** English. Planning discussions may be in German, but all player-facing text, code naming, comments, and Claude implementation specs should be English.
## GOAL

Implement responsive top-down movement in a test scene.

## FILES TO READ

- `player/11_PLAYER_CONTROLLER.md`
- `player/14_DASH_AND_MOVEMENT.md`
- `art/101_PIXEL_GRID_AND_SCALE.md`
- `technical/117_CODING_RULES_FOR_CLAUDE.md`

## REQUIREMENTS

Independent movement foundation; movement speed comes from configuration/stat source rather than magic numbers.

## DO NOT IMPLEMENT

No dash, shooting, networking, stamina, or animation polish.

## ACCEPTANCE CRITERIA

Player moves smoothly in all directions and obeys collision in a simple test room.

## TESTS

Run relevant EditMode/PlayMode checks where practical.

## EXPECTED FILES CHANGED

Only files directly required by this task and its tests.
