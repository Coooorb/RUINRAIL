# TASK 025 — Dungeon Assembler

> **Status:** Approved implementation task for the RUINRAIL V1/MVP roadmap.
> **Execution mode:** May be run individually or by the approved Autonomous Task Runner.
> **Game/code language:** English.

## PREREQUISITES

- TASK 024 must be complete or independently audited as already satisfied.
- Preserve the confirmed implementation state from all earlier completed tasks.
- If an earlier dependency is missing or broken, stop and report the dependency failure instead of inventing a substitute architecture.

## GOAL

Map a valid seeded dungeon graph to validated handmade room prefabs through socket matching and collision-free grid placement.

## FILES TO READ

Read this task, `CLAUDE.md`, `technical/117_CODING_RULES_FOR_CLAUDE.md`, and the following direct specifications before editing code:

- `dungeon/53_DUNGEON_GENERATOR.md`
- `dungeon/54_DUNGEON_VALIDATION.md`
- `dungeon/51_ROOM_AUTHORING.md`
- `dungeon/56_BIOMES.md`
- `technical/114_RNG_DETERMINISM.md`

Do not bulk-read the entire GDD. Read one additional specification only when a concrete direct dependency requires it, and record why in the completion report.

## ASSUMED EXISTING STATE

- Inspect the current repository before coding. Reuse the systems proven by earlier tasks instead of recreating them.
- Existing automated tests are regression contracts. Preserve them unless an approved specification intentionally changes the behavior.
- Use ScriptableObjects for shared immutable definitions/balance data and runtime objects/instances for mutable per-run/per-player state, following the data-architecture specs.
- Keep host-authoritative/network-ready boundaries where the existing architecture already provides them, but do not implement networking before its assigned tasks.

## REQUIREMENTS

1. Choose only validated room prefabs matching requested biome/category and socket needs.
2. Place rooms on an integer layout grid by matching opposite cardinal sockets, with no room-bounds overlap or unintended connections.
3. Avoid duplicate use of the same room prefab within one dungeon when the eligible pool is sufficient; allow controlled reuse only when necessary and report/flag the condition.
4. Keep prefab selection deterministic under the assembly RNG stream and separate from graph RNG.
5. Validate final assembled connectivity from Start to every room/Boss and expose a clear assembly result/failure diagnostic.

## DO NOT IMPLEMENT

- Do not implement systems assigned to later tasks early unless this task explicitly requires a narrow integration seam.
- Do not introduce PvP, classes, stamina, durability, crafting, Gear Score, critical hits, weak spots, extra currencies, or unapproved ammo categories.
- Do not silently rebalance approved V1 values. Keep approved constants data-driven where the architecture calls for data.
- Do not replace working implementations from earlier tasks with parallel/duplicate systems. Extend the confirmed repository state.
- Do not perform unrelated refactors, package upgrades, Unity-version changes, or architectural rewrites.

## ACCEPTANCE CRITERIA

1. Given fixed graph/pool/seed, the same room IDs and placements are produced.
2. No placed room bounds overlap and every graph edge corresponds to a compatible connected socket pair.
3. All assembled rooms are reachable from Start and Boss is reachable.
4. Invalid/insufficient pools fail clearly rather than producing corrupted layouts.
5. Runtime does not procedurally create room geometry.

## TESTS

- The Unity project must compile after the task.
- Use synthetic room fixtures for exhaustive socket/overlap cases plus at least one real authored room-set smoke test.
- Run the repository test harness after implementation:
  `UNITY_PATH="/c/Program Files/Unity/Hub/Editor/6000.3.24f1/Editor/Unity.exe" bash scripts/run-unity-tests.sh All`
- A zero-test run is a failure. Never report PASS for tests that did not actually run.
- Existing relevant EditMode and PlayMode suites must remain green.
- If Unity or an external service is unavailable, report the affected check as `NOT RUN` with the exact reason; never fabricate a result.

## TASK-SPECIFIC NOTES

- This is a high-reasoning assembly task; prefer explicit search/backtracking with bounded failure diagnostics over brittle greedy placement if needed.

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

In normal single-task mode, stop before TASK 026. Under `production/130_AUTONOMOUS_TASK_RUNNER.md`, write the checkpoint to the autonomous run log and continue only if the runner's gate rules permit it.
