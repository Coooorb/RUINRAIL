# TASK 006 — Health Damage

> **Status:** Approved design specification unless explicitly marked as tunable.
> **Game language:** English. Planning discussions may be in German, but all player-facing text, code naming, comments, and Claude implementation specs should be English.
## GOAL

Implement reusable health/damage foundation and solo death.

## FILES TO READ

- `combat/40_COMBAT_RULES.md`
- `combat/41_DAMAGE_HEALING_STATUS.md`
- `technical/117_CODING_RULES_FOR_CLAUDE.md`

## REQUIREMENTS

Reusable HealthComponent/damage request path, healing, death event. Player solo life state can react to death.

## DO NOT IMPLEMENT

No co-op Downed system, no crits, no weak spots.

## ACCEPTANCE CRITERIA

Damage/heal/death work in test scene and no attack script directly owns unrelated health logic.

## TESTS

Run relevant EditMode/PlayMode checks where practical.

## EXPECTED FILES CHANGED

Only files directly required by this task and its tests.
