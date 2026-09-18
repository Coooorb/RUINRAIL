# TASK 024 — Seeded Dungeon Graph

> **Status:** Approved implementation task for the RUINRAIL V1/MVP roadmap.
> **Execution mode:** May be run individually or by the approved Autonomous Task Runner.
> **Game/code language:** English.

## PREREQUISITES

- TASK 023 must be complete or independently audited as already satisfied.
- Preserve the confirmed implementation state from all earlier completed tasks.
- If an earlier dependency is missing or broken, stop and report the dependency failure instead of inventing a substitute architecture.

## GOAL

Generate the deterministic abstract room graph for one depth before any prefabs are placed.

## FILES TO READ

Read this task, `CLAUDE.md`, `technical/117_CODING_RULES_FOR_CLAUDE.md`, and the following direct specifications before editing code:

- `dungeon/53_DUNGEON_GENERATOR.md`
- `dungeon/55_ROOM_TYPES.md`
- `dungeon/59_DEPTH_SCALING.md`
- `technical/114_RNG_DETERMINISM.md`

Do not bulk-read the entire GDD. Read one additional specification only when a concrete direct dependency requires it, and record why in the completion report.

## ASSUMED EXISTING STATE

- Inspect the current repository before coding. Reuse the systems proven by earlier tasks instead of recreating them.
- Existing automated tests are regression contracts. Preserve them unless an approved specification intentionally changes the behavior.
- Use ScriptableObjects for shared immutable definitions/balance data and runtime objects/instances for mutable per-run/per-player state, following the data-architecture specs.
- Keep host-authoritative/network-ready boundaries where the existing architecture already provides them, but do not implement networking before its assigned tasks.

## REQUIREMENTS

1. Generate 9–13-room layouts using depth targets: D1–3 9–10, D4–8 10–11, D9+ 10–13.
2. Generate one main path of 6–9 rooms including exactly one Start and one terminal Boss, plus 1–3 branches each 1–3 rooms as allowed by the design rules.
3. Place required/special room categories according to dungeon rules without requiring geometry knowledge.
4. Use a passed expedition/depth seed and dedicated RNG stream so identical seed + inputs produce identical graph results.
5. Avoid immediate/spec-forbidden category arrangements and fail with a diagnostic rather than silently violating constraints.

## DO NOT IMPLEMENT

- Do not implement systems assigned to later tasks early unless this task explicitly requires a narrow integration seam.
- Do not introduce PvP, classes, stamina, durability, crafting, Gear Score, critical hits, weak spots, extra currencies, or unapproved ammo categories.
- Do not silently rebalance approved V1 values. Keep approved constants data-driven where the architecture calls for data.
- Do not replace working implementations from earlier tasks with parallel/duplicate systems. Extend the confirmed repository state.
- Do not perform unrelated refactors, package upgrades, Unity-version changes, or architectural rewrites.

## ACCEPTANCE CRITERIA

1. Identical seeds produce structurally identical graphs; different test seeds produce valid variation.
2. Room count, main-path length, Start/Boss uniqueness and branch-count/length invariants always hold across broad seeded tests.
3. Graph generation does not instantiate room prefabs or use UnityEngine.Random directly.
4. Failure is explicit when constraints cannot be satisfied.

## TESTS

- The Unity project must compile after the task.
- Property-style tests should sample many fixed seeds at representative depths.
- Run the repository test harness after implementation:
  `UNITY_PATH="/c/Program Files/Unity/Hub/Editor/6000.3.24f1/Editor/Unity.exe" bash scripts/run-unity-tests.sh All`
- A zero-test run is a failure. Never report PASS for tests that did not actually run.
- Existing relevant EditMode and PlayMode suites must remain green.
- If Unity or an external service is unavailable, report the affected check as `NOT RUN` with the exact reason; never fabricate a result.

## TASK-SPECIFIC NOTES

- This is a high-reasoning algorithm task. Preserve deterministic RNG stream boundaries; do not mix loot/enemy rolls into graph RNG.

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

In normal single-task mode, stop before TASK 025. Under `production/130_AUTONOMOUS_TASK_RUNNER.md`, write the checkpoint to the autonomous run log and continue only if the runner's gate rules permit it.
