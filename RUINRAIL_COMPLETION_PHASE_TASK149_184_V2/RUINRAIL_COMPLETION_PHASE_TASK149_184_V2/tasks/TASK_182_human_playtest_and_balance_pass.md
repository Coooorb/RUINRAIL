# TASK 182 — Human Playtest and Evidence-Based Balance Pass

> **Status:** Approved RUINRAIL completion/release task.
> **Execution mode:** Mandatory human playtest gate.
> **Game/code language:** English.

## PREREQUISITES

- TASK 181 live co-op gate PASS.
- Final art, animation, VFX, UI, font and audio are integrated.

## GOAL

Validate whether the finished game is readable, fun and balanced enough for V1, then make only evidence-backed numerical tuning changes.

## REQUIREMENTS

1. Human tester completes representative runs covering at least Pistol/AR, Shotgun/Melee, Bow/Sniper, Blaster and one real multiplayer run.
2. Include at least one multi-depth run sufficient to judge depth scaling and extraction risk/reward.
3. Record qualitative + measurable findings: TTK, ammo pressure, loot frequency, enemy density, telegraph readability, Boss difficulty, coin economy, XP pace, healing pressure, room pacing, depth scaling, Legendary impact, controller feel and UI/text readability with final font.
4. Accessibility/readability pass must include combat telegraphs against all three final biome tilesets/effects.
5. Only tune existing values when evidence identifies a concrete issue. Log before/after and rationale; no new systems/content.
6. Re-run targeted and full automated tests after tuning.
7. Produce `production/HUMAN_PLAYTEST_AND_BALANCE_REPORT.md` with blockers, non-blockers and sign-off.

## ACCEPTANCE CRITERIA

1. Human evidence exists; bots are not substituted.
2. No severe unresolved fun/readability/balance blocker.
3. Every tuning change is evidence-backed and regression-tested.
4. Human owner explicitly signs off the V1 feel/balance.

## REVIEW GATE

Stop `WAITING_HUMAN_REVIEW` until the owner signs off.
