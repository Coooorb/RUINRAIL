# TASK 011 — Ammo and Reload Generalization

> **Status:** Approved implementation task for the RUINRAIL V1/MVP roadmap.
> **Execution mode:** May be run individually or by the approved Autonomous Task Runner.
> **Game/code language:** English.

## PREREQUISITES

- TASK 010 must be complete or independently audited as already satisfied.
- Preserve the confirmed implementation state from all earlier completed tasks.
- If an earlier dependency is missing or broken, stop and report the dependency failure instead of inventing a substitute architecture.

## GOAL

Generalize the existing P9 Ranger magazine/reserve implementation into the approved four-ammo V1 foundation without building inventory early.

## FILES TO READ

Read this task, `CLAUDE.md`, `technical/117_CODING_RULES_FOR_CLAUDE.md`, and the following direct specifications before editing code:

- `items/26_AMMO_SYSTEM.md`
- `items/23_WEAPON_FRAMEWORK.md`
- `items/33_WEAPON_CATALOG.md`

Do not bulk-read the entire GDD. Read one additional specification only when a concrete direct dependency requires it, and record why in the completion report.

## ASSUMED EXISTING STATE

- Inspect the current repository before coding. Reuse the systems proven by earlier tasks instead of recreating them.
- Existing automated tests are regression contracts. Preserve them unless an approved specification intentionally changes the behavior.
- Use ScriptableObjects for shared immutable definitions/balance data and runtime objects/instances for mutable per-run/per-player state, following the data-architecture specs.
- Keep host-authoritative/network-ready boundaries where the existing architecture already provides them, but do not implement networking before its assigned tasks.

## REQUIREMENTS

1. Exactly four normal ammo categories exist: Light, Medium, Heavy, Shells. Bow, Blaster, Knife and Spear must not consume these normal ammo reserves.
2. Reuse/generalize the TASK 008 magazine + reserve behavior so every future ammo weapon can specify ammo type, magazine capacity, reload time and per-shot ammo cost.
3. Normal firearm shots consume their authored ammo cost; Rocket Launcher compatibility must support the approved default cost of 4 Heavy Ammo per shot without inventing a fifth Rocket ammo type.
4. Reload transfers only available reserve ammo, never overfills a magazine, never creates ammo, and preserves the TASK 010 rule that unequipping cancels reload before transfer.
5. Expose the approved backpack stack-limit data (Light 180, Medium 120, Heavy 60, Shells 40) as definitions/configuration without implementing backpack storage yet.

## DO NOT IMPLEMENT

- Do not implement systems assigned to later tasks early unless this task explicitly requires a narrow integration seam.
- Do not introduce PvP, classes, stamina, durability, crafting, Gear Score, critical hits, weak spots, extra currencies, or unapproved ammo categories.
- Do not silently rebalance approved V1 values. Keep approved constants data-driven where the architecture calls for data.
- Do not replace working implementations from earlier tasks with parallel/duplicate systems. Extend the confirmed repository state.
- Do not perform unrelated refactors, package upgrades, Unity-version changes, or architectural rewrites.

## ACCEPTANCE CRITERIA

1. All four ammo types are represented once and only once; no extra normal ammo type exists.
2. Reload/shot consumption is reusable across weapon definitions and cannot make reserve or magazine counts negative.
3. Per-shot ammo cost supports 4 Heavy for future Rocket Launcher shots.
4. Existing P9 Ranger behavior and weapon-switch reload cancellation remain green.
5. Stack-limit definitions equal 180/120/60/40 and are not yet treated as a full inventory.

## TESTS

- The Unity project must compile after the task.
- Test partial reloads, empty reserve, full magazine, per-shot costs >1, and all four ammo categories.
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

In normal single-task mode, stop before TASK 012. Under `production/130_AUTONOMOUS_TASK_RUNNER.md`, write the checkpoint to the autonomous run log and continue only if the runner's gate rules permit it.
