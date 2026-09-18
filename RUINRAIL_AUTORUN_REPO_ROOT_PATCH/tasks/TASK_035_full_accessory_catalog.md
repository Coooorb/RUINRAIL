# TASK 035 — Full Accessory Catalog and Legendary Passives

> **Status:** Approved implementation task for the RUINRAIL V1/MVP roadmap.
> **Execution mode:** May be run individually or by the approved Autonomous Task Runner.
> **Game/code language:** English.

## PREREQUISITES

- TASK 034 must be complete or independently audited as already satisfied.
- Preserve the confirmed implementation state from all earlier completed tasks.
- If an earlier dependency is missing or broken, stop and report the dependency failure instead of inventing a substitute architecture.

## GOAL

Author all 16 V1 accessories, exact intrinsics and exact fixed Legendary passives.

## FILES TO READ

Read this task, `CLAUDE.md`, `technical/117_CODING_RULES_FOR_CLAUDE.md`, and the following direct specifications before editing code:

- `items/30_ACCESSORY_CATALOG.md`
- `items/34_LEGENDARY_ACCESSORY_PASSIVES.md`
- `player/16_GLOBAL_STAT_CAPS.md`
- `combat/42_STAGGER_KNOCKBACK.md`

Do not bulk-read the entire GDD. Read one additional specification only when a concrete direct dependency requires it, and record why in the completion report.

## ASSUMED EXISTING STATE

- Inspect the current repository before coding. Reuse the systems proven by earlier tasks instead of recreating them.
- Existing automated tests are regression contracts. Preserve them unless an approved specification intentionally changes the behavior.
- Use ScriptableObjects for shared immutable definitions/balance data and runtime objects/instances for mutable per-run/per-player state, following the data-architecture specs.
- Keep host-authoritative/network-ready boundaries where the existing architecture already provides them, but do not implement networking before its assigned tasks.

## REQUIREMENTS

1. Author exactly 16 accessory families and their catalog intrinsics: Runner's Watch, Field Scope, Combat Bracelet, Quickdraw Holster, Loader's Glove, Cooling Module, Heat Sink, Archer's Ring, Dash Capacitor, Rangefinder, Trauma Pendant, Ammo Pouch, Magnetic Coil, Stabilizer, Impact Module, Shock Charm.
2. Implement all Legendary passives from items/34 exactly, including conditions, durations and internal cooldowns.
3. Ensure projectile-distance, continuous-fire, overheat, melee-kill, reload-complete, dash and stagger triggers subscribe to narrow events instead of concrete-type polling.
4. Ammo Pouch modifies stack capacity +25% without duplicating item quantities when equipped/removed; Magnetic Coil changes eligible pickup attraction only.

## DO NOT IMPLEMENT

- Do not implement systems assigned to later tasks early unless this task explicitly requires a narrow integration seam.
- Do not introduce PvP, classes, stamina, durability, crafting, Gear Score, critical hits, weak spots, extra currencies, or unapproved ammo categories.
- Do not silently rebalance approved V1 values. Keep approved constants data-driven where the architecture calls for data.
- Do not replace working implementations from earlier tasks with parallel/duplicate systems. Extend the confirmed repository state.
- Do not perform unrelated refactors, package upgrades, Unity-version changes, or architectural rewrites.

## ACCEPTANCE CRITERIA

1. Exactly 16 definitions exist and all intrinsic values match the approved table.
2. Each Legendary passive triggers exactly under its documented condition and respects cooldown/per-target rules.
3. Legendary accessories have intrinsic + 3 affixes + fixed passive.
4. Equip/unequip cannot duplicate consumables/ammo or leave stale effects.

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

In normal single-task mode, stop before TASK 036. Under `production/130_AUTONOMOUS_TASK_RUNNER.md`, write the checkpoint to the autonomous run log and continue only if the runner's gate rules permit it.
