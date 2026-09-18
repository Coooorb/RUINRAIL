# TASK 005 — Dash

> **Status:** Approved design specification unless explicitly marked as tunable.
> **Game language:** English. Planning discussions may be in German, but all player-facing text, code naming, comments, and Claude implementation specs should be English.
## GOAL

Implement the universal dash.

## FILES TO READ

- `player/14_DASH_AND_MOVEMENT.md`
- `technical/117_CODING_RULES_FOR_CLAUDE.md`

## REQUIREMENTS

Use configurable duration/cooldown and a short invulnerability window. No stamina or charges.

## DO NOT IMPLEMENT

Do not add character abilities or skill-tree logic.

## ACCEPTANCE CRITERIA

Dash triggers responsively, obeys cooldown, and invulnerability is isolated to configured dash window.

## TESTS

Run relevant EditMode/PlayMode checks where practical.

## EXPECTED FILES CHANGED

Only files directly required by this task and its tests.
