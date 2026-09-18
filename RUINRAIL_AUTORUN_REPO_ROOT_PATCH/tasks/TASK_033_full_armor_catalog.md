# TASK 033 — Full Armor Catalog and Legendary Passives

> **Status:** Approved implementation task for the RUINRAIL V1/MVP roadmap.
> **Execution mode:** May be run individually or by the approved Autonomous Task Runner.
> **Game/code language:** English.

## PREREQUISITES

- TASK 032 must be complete or independently audited as already satisfied.
- Preserve the confirmed implementation state from all earlier completed tasks.
- If an earlier dependency is missing or broken, stop and report the dependency failure instead of inventing a substitute architecture.

## GOAL

Author all 9 V1 armor families and their fixed Legendary passives with tests for exact values and room/cooldown semantics.

## FILES TO READ

Read this task, `CLAUDE.md`, `technical/117_CODING_RULES_FOR_CLAUDE.md`, and the following direct specifications before editing code:

- `items/27_ARMOR_SYSTEM.md`
- `items/28_ARMOR_CATALOG.md`
- `player/16_GLOBAL_STAT_CAPS.md`
- `combat/41_DAMAGE_HEALING_STATUS.md`

Do not bulk-read the entire GDD. Read one additional specification only when a concrete direct dependency requires it, and record why in the completion report.

## ASSUMED EXISTING STATE

- Inspect the current repository before coding. Reuse the systems proven by earlier tasks instead of recreating them.
- Existing automated tests are regression contracts. Preserve them unless an approved specification intentionally changes the behavior.
- Use ScriptableObjects for shared immutable definitions/balance data and runtime objects/instances for mutable per-run/per-player state, following the data-architecture specs.
- Keep host-authoritative/network-ready boundaries where the existing architecture already provides them, but do not implement networking before its assigned tasks.

## REQUIREMENTS

1. Author exactly: Scrap Vest, Scout Rig, Riot Armor, Heavy Plate, Blast Suit, Medic Harness, Combat Harness, Reinforced Exo-Rig, Runner Suit with the catalog base values.
2. Implement all nine Legendary passives exactly: Patchwork 6% maxHP after combat clear; Momentum +12% move 1s after dash; Anchored negate one stagger/8s; Last Stand below25% +15% DR; Shock Absorber ignore explosion knockback; Emergency Care first healing consumable/room +25%; Adrenaline kill +10% move3s ICD5; Exo Lock hit>=20 final damage gives +30% stagger/knockback resist5s CD8; Second Wind first below30%/room resets dash cooldown.
3. Legendary armor uses 3 normal affixes plus its fixed passive and never gets an RMB special.
4. Use room lifecycle/events and central stat aggregation; do not poll global scene state.

## DO NOT IMPLEMENT

- Do not implement systems assigned to later tasks early unless this task explicitly requires a narrow integration seam.
- Do not introduce PvP, classes, stamina, durability, crafting, Gear Score, critical hits, weak spots, extra currencies, or unapproved ammo categories.
- Do not silently rebalance approved V1 values. Keep approved constants data-driven where the architecture calls for data.
- Do not replace working implementations from earlier tasks with parallel/duplicate systems. Extend the confirmed repository state.
- Do not perform unrelated refactors, package upgrades, Unity-version changes, or architectural rewrites.

## ACCEPTANCE CRITERIA

1. Catalog contains exactly 9 armor families and no Hazmat Suit.
2. Every base value and Legendary passive magnitude/duration/cooldown matches the catalog.
3. Per-room/ICD passives trigger no more often than specified and reset at correct room boundaries.
4. All modifiers respect global caps and specialized explosion reduction remains separate.

## TESTS

- The Unity project must compile after the task.
- Add focused automated tests for the behavior introduced by this task and for its important edge cases.
- Run the repository test harness after implementation:
  `UNITY_PATH="/c/Program Files/Unity/Hub/Editor/6000.3.24f1/Editor/Unity.exe" bash scripts/run-unity-tests.sh All`
- A zero-test run is a failure. Never report PASS for tests that did not actually run.
- Existing relevant EditMode and PlayMode suites must remain green.
- If Unity or an external service is unavailable, report the affected check as `NOT RUN` with the exact reason; never fabricate a result.

## TASK-SPECIFIC NOTES

- No additional task-specific notes.

## EXPECTED FILES CHANGED

Only production/data/editor/test files directly required by this task. Keep the diff narrow and reviewable.

## COMPLETION REPORT

At task end report:
1. Files created/changed.
2. What was implemented and how it integrates with existing systems.
3. Exact approved/tunable values used, including any explicitly temporary prototype defaults.
4. Exact test commands actually run and exact PASS/FAIL/NOT RUN results.
5. Acceptance-criteria status item by item.
6. Known limitations, unresolved specification conflicts, external-asset blockers, or follow-up dependencies.

In normal single-task mode, stop before TASK 034. Under `production/130_AUTONOMOUS_TASK_RUNNER.md`, write the checkpoint to the autonomous run log and continue only if the runner's gate rules permit it.
