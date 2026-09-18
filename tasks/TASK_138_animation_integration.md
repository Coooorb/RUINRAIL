# TASK 138 — Animation Integration

> **Status:** Approved implementation task for the RUINRAIL V1/MVP roadmap.
> **Execution mode:** May be run individually or by the approved Autonomous Task Runner.
> **Game/code language:** English.

## PREREQUISITES

- TASK 137 must be complete or independently audited as already satisfied.
- Preserve the confirmed implementation state from all earlier completed tasks.
- If an earlier dependency is missing or broken, stop and report the dependency failure instead of inventing a substitute architecture.

## GOAL

Integrate production/placeholder animation state machines for player 8-direction body, enemies, Elites, Bosses, weapons and interactions while gameplay timing remains authoritative.

## FILES TO READ

Read this task, `CLAUDE.md`, `technical/117_CODING_RULES_FOR_CLAUDE.md`, and the following direct specifications before editing code:

- `art/103_ANIMATION_RULES.md`
- `player/11_PLAYER_CONTROLLER.md`
- `combat/43_ENEMY_FRAMEWORK.md`
- `items/23_WEAPON_FRAMEWORK.md`

Do not bulk-read the entire GDD. Read one additional specification only when a concrete direct dependency requires it, and record why in the completion report.

## ASSUMED EXISTING STATE

- Inspect the current repository before coding. Reuse the systems proven by earlier tasks instead of recreating them.
- Existing automated tests are regression contracts. Preserve them unless an approved specification intentionally changes the behavior.
- Use ScriptableObjects for shared immutable definitions/balance data and runtime objects/instances for mutable per-run/per-player state, following the data-architecture specs.
- Keep host-authoritative/network-ready boundaries where the existing architecture already provides them, but do not implement networking before its assigned tasks.

## REQUIREMENTS

1. Player body uses 8-direction facing output while weapon visual/pivot retains independent 360° aim; animation must not quantize gameplay aim.
2. Use approved 8–12 fps pixel animation target and sorting conventions; movement/attack/dash/downed/dead states map from gameplay events/state.
3. Enemy/Elite/Boss telegraph/attack animations follow existing gameplay timing; do not move damage timings into animation-only magic events unless explicitly synchronized and tested.
4. Weapon fire/reload/melee/charge/overheat visuals react to existing state without owning ammo/damage logic.
5. If required final sprite sheets/clips are absent, wire robust placeholder/state architecture, mark missing assets `BLOCKED_EXTERNAL_ASSET`, and never claim final animation-content completion.

## DO NOT IMPLEMENT

- Do not implement systems assigned to later tasks early unless this task explicitly requires a narrow integration seam.
- Do not introduce PvP, classes, stamina, durability, crafting, Gear Score, critical hits, weak spots, extra currencies, or unapproved ammo categories.
- Do not silently rebalance approved V1 values. Keep approved constants data-driven where the architecture calls for data.
- Do not replace working implementations from earlier tasks with parallel/duplicate systems. Extend the confirmed repository state.
- Do not perform unrelated refactors, package upgrades, Unity-version changes, or architectural rewrites.

## ACCEPTANCE CRITERIA

1. Gameplay state drives animation without changing combat results/timing.
2. 8-way body + 360 weapon separation remains correct.
3. No missing-animation reference causes runtime exceptions; placeholder fallback is explicit.
4. External missing animation assets are listed for final gate.

## TESTS

- The Unity project must compile after the task.
- Add focused automated tests for the behavior introduced by this task and for its important edge cases.
- Run the repository test harness after implementation:
  `UNITY_PATH="/c/Program Files/Unity/Hub/Editor/6000.3.24f1/Editor/Unity.exe" bash scripts/run-unity-tests.sh All`
- A zero-test run is a failure. Never report PASS for tests that did not actually run.
- Existing relevant EditMode and PlayMode suites must remain green.
- If Unity or an external service is unavailable, report the affected check as `NOT RUN` with the exact reason; never fabricate a result.

## TASK-SPECIFIC NOTES

- External art/animation files cannot be manufactured by code-quality tasks. Missing required final assets may be BLOCKED_EXTERNAL_ASSET while later independent engineering continues.

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

In normal single-task mode, stop before TASK 139. Under `production/130_AUTONOMOUS_TASK_RUNNER.md`, write the checkpoint to the autonomous run log and continue only if the runner's gate rules permit it.
