# TASK 007 — Projectile Foundation

> **Status:** Approved design specification unless explicitly marked as tunable.
> **Game language:** English. Planning discussions may be in German, but all player-facing text, code naming, comments, and Claude implementation specs should be English.
## GOAL

Implement pooled visible projectile foundation.

## FILES TO READ

- `items/23_WEAPON_FRAMEWORK.md`
- `combat/40_COMBAT_RULES.md`
- `art/104_VFX_GAME_FEEL.md`

## REQUIREMENTS

Projectile speed/range/damage/knockback/stagger payloads come from weapon/attack data. Add simple pooling suitable for repeated shots.

## DO NOT IMPLEMENT

No hitscan conversion, no huge universal pooling framework.

## ACCEPTANCE CRITERIA

Projectiles travel visibly, expire by range/lifetime, apply damage exactly once per valid hit, and reuse pool objects.

## TESTS

Run relevant EditMode/PlayMode checks where practical.

## EXPECTED FILES CHANGED

Only files directly required by this task and its tests.
