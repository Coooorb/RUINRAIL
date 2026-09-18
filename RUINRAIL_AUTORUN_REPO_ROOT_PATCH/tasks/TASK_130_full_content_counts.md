# TASK 130 — All 63 Rooms and Content Count Validation

> **Status:** Approved implementation task for the RUINRAIL V1/MVP roadmap.
> **Execution mode:** May be run individually or by the approved Autonomous Task Runner.
> **Game/code language:** English.

## PREREQUISITES

- TASK 129 must be complete or independently audited as already satisfied.
- Preserve the confirmed implementation state from all earlier completed tasks.
- If an earlier dependency is missing or broken, stop and report the dependency failure instead of inventing a substitute architecture.

## GOAL

Create a production validator that proves all fixed V1 content counts and IDs are present before UI/polish/release work.

## FILES TO READ

Read this task, `CLAUDE.md`, `technical/117_CODING_RULES_FOR_CLAUDE.md`, and the following direct specifications before editing code:

- `production/126_FINAL_MVP_CONTENT_COUNTS.md`
- `production/122_MVP_SCOPE.md`
- `items/33_WEAPON_CATALOG.md`
- `combat/44_NORMAL_ENEMIES.md`
- `combat/45_ELITES.md`
- `combat/46_BOSSES.md`

Do not bulk-read the entire GDD. Read one additional specification only when a concrete direct dependency requires it, and record why in the completion report.

## ASSUMED EXISTING STATE

- Inspect the current repository before coding. Reuse the systems proven by earlier tasks instead of recreating them.
- Existing automated tests are regression contracts. Preserve them unless an approved specification intentionally changes the behavior.
- Use ScriptableObjects for shared immutable definitions/balance data and runtime objects/instances for mutable per-run/per-player state, following the data-architecture specs.
- Keep host-authoritative/network-ready boundaries where the existing architecture already provides them, but do not implement networking before its assigned tasks.

## REQUIREMENTS

1. Validate exactly 63 room prefabs: 21 per biome and exact per-biome category distribution 2 Start/5 small combat/4 medium/2 large/1 merchant/2 event/1 loot/1 treasure/1 recovery/2 boss.
2. Validate exactly 33 weapons, 9 armor families, 16 accessory families, 10 consumables, 9 normal enemies, 6 Elites, 6 Bosses.
3. Validate stable definition/content IDs unique and all required runtime references/biome registrations resolve.
4. Validate each biome has exactly 2 Elites and 2 Bosses and no post-MVP content accidentally counted.
5. Emit a deterministic content-count report consumable by final gate/CI.

## DO NOT IMPLEMENT

- Do not implement systems assigned to later tasks early unless this task explicitly requires a narrow integration seam.
- Do not introduce PvP, classes, stamina, durability, crafting, Gear Score, critical hits, weak spots, extra currencies, or unapproved ammo categories.
- Do not silently rebalance approved V1 values. Keep approved constants data-driven where the architecture calls for data.
- Do not replace working implementations from earlier tasks with parallel/duplicate systems. Extend the confirmed repository state.
- Do not perform unrelated refactors, package upgrades, Unity-version changes, or architectural rewrites.

## ACCEPTANCE CRITERIA

1. All exact counts match production/126 and any missing/extra/duplicate content fails the validator.
2. Every content ID resolves and is unique.
3. Report lists exact counts by category/biome/class and PASS/FAIL.
4. Full automated test suite remains green.

## TESTS

- The Unity project must compile after the task.
- Add focused automated tests for the behavior introduced by this task and for its important edge cases.
- Run the repository test harness after implementation:
  `UNITY_PATH="/c/Program Files/Unity/Hub/Editor/6000.3.24f1/Editor/Unity.exe" bash scripts/run-unity-tests.sh All`
- A zero-test run is a failure. Never report PASS for tests that did not actually run.
- Existing relevant EditMode and PlayMode suites must remain green.
- If Unity or an external service is unavailable, report the affected check as `NOT RUN` with the exact reason; never fabricate a result.

## TASK-SPECIFIC NOTES

- This is a hard content-completeness checkpoint; do not hide missing definitions behind placeholder count entries.

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

In normal single-task mode, stop before TASK 131. Under `production/130_AUTONOMOUS_TASK_RUNNER.md`, write the checkpoint to the autonomous run log and continue only if the runner's gate rules permit it.
