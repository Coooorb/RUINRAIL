# TASK 057 — Regular Weapon Catalog Completion

> **Status:** Approved implementation task for the RUINRAIL V1/MVP roadmap.
> **Execution mode:** May be run individually or by the approved Autonomous Task Runner.
> **Game/code language:** English.

## PREREQUISITES

- TASK 056 must be complete or independently audited as already satisfied.
- Preserve the confirmed implementation state from all earlier completed tasks.
- If an earlier dependency is missing or broken, stop and report the dependency failure instead of inventing a substitute architecture.

## GOAL

Author and validate every remaining non-Legendary V1 weapon definition so all 11 classes have two normal weapons.

## FILES TO READ

Read this task, `CLAUDE.md`, `technical/117_CODING_RULES_FOR_CLAUDE.md`, and the following direct specifications before editing code:

- `items/33_WEAPON_CATALOG.md`
- `items/24_WEAPON_CLASSES.md`
- `items/23_WEAPON_FRAMEWORK.md`
- `items/26_AMMO_SYSTEM.md`

Do not bulk-read the entire GDD. Read one additional specification only when a concrete direct dependency requires it, and record why in the completion report.

## ASSUMED EXISTING STATE

- Inspect the current repository before coding. Reuse the systems proven by earlier tasks instead of recreating them.
- Existing automated tests are regression contracts. Preserve them unless an approved specification intentionally changes the behavior.
- Use ScriptableObjects for shared immutable definitions/balance data and runtime objects/instances for mutable per-run/per-player state, following the data-architecture specs.
- Keep host-authoritative/network-ready boundaries where the existing architecture already provides them, but do not implement networking before its assigned tasks.

## REQUIREMENTS

1. Author every remaining normal weapon from items/33 exactly: Kestrel-12, Wasp-45, Marauder A2, Hound BR, Scatter-8, Needle Rifle, Compound Bow, Twin-Tube Launcher, Arc Blaster B4, Ripper Knife, Guard Lance, plus any catalog normal definition not already authored.
2. Use existing reusable behavior/data compositions for projectile, shotgun, heat, bow charge, rocket/explosion and melee; do not create per-weapon scripts for mere stat differences.
3. Validate each definition has stable ID, class, exact damage/rate/mag/reload/speed/range/resource fields appropriate to its class.
4. Ensure all normal weapons participate in loot/economy/rarity pools and can occupy either weapon slot.

## DO NOT IMPLEMENT

- Do not implement systems assigned to later tasks early unless this task explicitly requires a narrow integration seam.
- Do not introduce PvP, classes, stamina, durability, crafting, Gear Score, critical hits, weak spots, extra currencies, or unapproved ammo categories.
- Do not silently rebalance approved V1 values. Keep approved constants data-driven where the architecture calls for data.
- Do not replace working implementations from earlier tasks with parallel/duplicate systems. Extend the confirmed repository state.
- Do not perform unrelated refactors, package upgrades, Unity-version changes, or architectural rewrites.

## ACCEPTANCE CRITERIA

1. Exactly two non-Legendary definitions exist for each of 11 weapon classes = 22 normal weapons total.
2. Every authored numeric value matches items/33.
3. No duplicate stable IDs and no unsupported ammo categories.
4. Data-only variants do not introduce duplicate attack implementations.

## TESTS

- The Unity project must compile after the task.
- Create a catalog-wide EditMode validation test that enumerates definitions and compares required count/class distribution.
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

In normal single-task mode, stop before TASK 058. Under `production/130_AUTONOMOUS_TASK_RUNNER.md`, write the checkpoint to the autonomous run log and continue only if the runner's gate rules permit it.
