# TASK 111 — Rustworks Rooms — Utility Rooms

> **Status:** Approved implementation task for the RUINRAIL V1/MVP roadmap.
> **Execution mode:** May be run individually or by the approved Autonomous Task Runner.
> **Game/code language:** English.

## PREREQUISITES

- TASK 110 must be complete or independently audited as already satisfied.
- Preserve the confirmed implementation state from all earlier completed tasks.
- If an earlier dependency is missing or broken, stop and report the dependency failure instead of inventing a substitute architecture.

## GOAL

Author the Rustworks utility room-prefab subset using the validated room pipeline.

## FILES TO READ

Read this task, `CLAUDE.md`, `technical/117_CODING_RULES_FOR_CLAUDE.md`, and the following direct specifications before editing code:

- `dungeon/51_ROOM_AUTHORING.md`
- `dungeon/52_ROOM_METADATA_AND_TAGS.md`
- `dungeon/54_DUNGEON_VALIDATION.md`
- `dungeon/56_BIOMES.md`
- `art/100_ART_DIRECTION.md`

Do not bulk-read the entire GDD. Read one additional specification only when a concrete direct dependency requires it, and record why in the completion report.

## ASSUMED EXISTING STATE

- Inspect the current repository before coding. Reuse the systems proven by earlier tasks instead of recreating them.
- Existing automated tests are regression contracts. Preserve them unless an approved specification intentionally changes the behavior.
- Use ScriptableObjects for shared immutable definitions/balance data and runtime objects/instances for mutable per-run/per-player state, following the data-architecture specs.
- Keep host-authoritative/network-ready boundaries where the existing architecture already provides them, but do not implement networking before its assigned tasks.

## REQUIREMENTS

1. Create exactly 1 Merchant + 2 Event + 1 Loot + 1 Treasure + 1 Recovery/Medical; every prefab is tagged Rustworks, has a unique stable room ID and correct category.
2. Use handmade required Tilemap layers, target room dimensions, cardinal sockets, reachable layout and validated logic markers.
3. Apply the gameplay/readability identity of Rustworks from the biome/art specs through geometry/hazards/obstacles without introducing biome-exclusive player mechanics not specified.
4. Use final art when available; otherwise keep engineering-valid placeholder geometry and record external art blocker instead of pretending it is final.
5. Run Room Validator on every new prefab.

## DO NOT IMPLEMENT

- Do not implement systems assigned to later tasks early unless this task explicitly requires a narrow integration seam.
- Do not introduce PvP, classes, stamina, durability, crafting, Gear Score, critical hits, weak spots, extra currencies, or unapproved ammo categories.
- Do not silently rebalance approved V1 values. Keep approved constants data-driven where the architecture calls for data.
- Do not replace working implementations from earlier tasks with parallel/duplicate systems. Extend the confirmed repository state.
- Do not perform unrelated refactors, package upgrades, Unity-version changes, or architectural rewrites.

## ACCEPTANCE CRITERIA

1. Exactly 1 Merchant + 2 Event + 1 Loot + 1 Treasure + 1 Recovery/Medical validated Rustworks rooms exist in this subset.
2. Every room has correct biome/category/ID/layers/sockets/reachability/spawns.
3. All validators/tests pass and runtime geometry remains authored, not procedural.

## TESTS

- The Unity project must compile after the task.
- Add focused automated tests for the behavior introduced by this task and for its important edge cases.
- Run the repository test harness after implementation:
  `UNITY_PATH="/c/Program Files/Unity/Hub/Editor/6000.3.24f1/Editor/Unity.exe" bash scripts/run-unity-tests.sh All`
- A zero-test run is a failure. Never report PASS for tests that did not actually run.
- Existing relevant EditMode and PlayMode suites must remain green.
- If Unity or an external service is unavailable, report the affected check as `NOT RUN` with the exact reason; never fabricate a result.

## TASK-SPECIFIC NOTES

- Missing final visual assets may be BLOCKED_EXTERNAL_ASSET for presentation, but geometry/logic/validation must still be complete.

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

In normal single-task mode, stop before TASK 112. Under `production/130_AUTONOMOUS_TASK_RUNNER.md`, write the checkpoint to the autonomous run log and continue only if the runner's gate rules permit it.
