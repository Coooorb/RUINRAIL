# TASK 081 — Room Runtime — Loot, Event, Merchant and Boss Integration

> **Status:** Approved implementation task for the RUINRAIL V1/MVP roadmap.
> **Execution mode:** May be run individually or by the approved Autonomous Task Runner.
> **Game/code language:** English.

## PREREQUISITES

- TASK 080 must be complete or independently audited as already satisfied.
- Preserve the confirmed implementation state from all earlier completed tasks.
- If an earlier dependency is missing or broken, stop and report the dependency failure instead of inventing a substitute architecture.

## GOAL

Integrate non-combat room categories and Boss lifecycle with their authored room runtime contracts.

## FILES TO READ

Read this task, `CLAUDE.md`, `technical/117_CODING_RULES_FOR_CLAUDE.md`, and the following direct specifications before editing code:

- `dungeon/55_ROOM_TYPES.md`
- `dungeon/57_EVENTS.md`
- `dungeon/58_CHESTS_MERCHANT_LOOT.md`
- `combat/46_BOSSES.md`

Do not bulk-read the entire GDD. Read one additional specification only when a concrete direct dependency requires it, and record why in the completion report.

## ASSUMED EXISTING STATE

- Inspect the current repository before coding. Reuse the systems proven by earlier tasks instead of recreating them.
- Existing automated tests are regression contracts. Preserve them unless an approved specification intentionally changes the behavior.
- Use ScriptableObjects for shared immutable definitions/balance data and runtime objects/instances for mutable per-run/per-player state, following the data-architecture specs.
- Keep host-authoritative/network-ready boundaries where the existing architecture already provides them, but do not implement networking before its assigned tasks.

## REQUIREMENTS

1. Loot/Treasure rooms expose their intended one-time loot/chest interactions without unnecessary combat lock unless authored.
2. Merchant room binds one stable per-depth merchant offer set; Event room binds one of the authored event instances; Recovery/Medical room binds Medical Station behavior.
3. Boss room locks appropriately, starts boss encounter once, unlocks/opens Boss Cache/transit hooks on boss defeat.
4. Room runtime uses metadata/category composition instead of scene-name switches.
5. Revisiting resolved special rooms cannot regenerate offers/events/rewards.

## DO NOT IMPLEMENT

- Do not implement systems assigned to later tasks early unless this task explicitly requires a narrow integration seam.
- Do not introduce PvP, classes, stamina, durability, crafting, Gear Score, critical hits, weak spots, extra currencies, or unapproved ammo categories.
- Do not silently rebalance approved V1 values. Keep approved constants data-driven where the architecture calls for data.
- Do not replace working implementations from earlier tasks with parallel/duplicate systems. Extend the confirmed repository state.
- Do not perform unrelated refactors, package upgrades, Unity-version changes, or architectural rewrites.

## ACCEPTANCE CRITERIA

1. Each room category instantiates/activates the correct runtime behavior exactly once.
2. Boss defeat triggers cache/transit downstream hooks once.
3. Revisits preserve resolved states.
4. No category behavior is keyed by prefab name strings.

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

In normal single-task mode, stop before TASK 082. Under `production/130_AUTONOMOUS_TASK_RUNNER.md`, write the checkpoint to the autonomous run log and continue only if the runner's gate rules permit it.
