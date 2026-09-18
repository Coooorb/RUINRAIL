# TASK 095 — Network Weapons and Player Combat

> **Status:** Approved implementation task for the RUINRAIL V1/MVP roadmap.
> **Execution mode:** May be run individually or by the approved Autonomous Task Runner.
> **Game/code language:** English.

## PREREQUISITES

- TASK 094 must be complete or independently audited as already satisfied.
- Preserve the confirmed implementation state from all earlier completed tasks.
- If an earlier dependency is missing or broken, stop and report the dependency failure instead of inventing a substitute architecture.

## GOAL

Synchronize weapon selection, Fire/Reload/Special requests and authoritative projectile/melee outcomes.

## FILES TO READ

Read this task, `CLAUDE.md`, `technical/117_CODING_RULES_FOR_CLAUDE.md`, and the following direct specifications before editing code:

- `multiplayer/82_NETWORK_AUTHORITY.md`
- `items/23_WEAPON_FRAMEWORK.md`
- `combat/40_COMBAT_RULES.md`
- `items/25_LEGENDARY_WEAPON_SPECIALS.md`

Do not bulk-read the entire GDD. Read one additional specification only when a concrete direct dependency requires it, and record why in the completion report.

## ASSUMED EXISTING STATE

- Inspect the current repository before coding. Reuse the systems proven by earlier tasks instead of recreating them.
- Existing automated tests are regression contracts. Preserve them unless an approved specification intentionally changes the behavior.
- Use ScriptableObjects for shared immutable definitions/balance data and runtime objects/instances for mutable per-run/per-player state, following the data-architecture specs.
- Keep host-authoritative/network-ready boundaries where the existing architecture already provides them, but do not implement networking before its assigned tasks.

## REQUIREMENTS

1. Owner sends weapon intents; host validates active slot, cooldown, reload, ammo/heat/charge and executes authoritative attack state.
2. Projectile/melee/explosion damage is authored/resolved on host; clients render synchronized projectiles/effects/state without independently applying health damage.
3. Synchronize magazine/reserve/heat/charge/reload/active-slot state necessary for owner HUD and remote presentation.
4. Legendary Special request uses same active-weapon authority/cooldown and cannot be client-forced while inactive/on cooldown.
5. Friendly fire between players is OFF for weapon attacks; do not make hazards harmless.

## DO NOT IMPLEMENT

- Do not implement systems assigned to later tasks early unless this task explicitly requires a narrow integration seam.
- Do not introduce PvP, classes, stamina, durability, crafting, Gear Score, critical hits, weak spots, extra currencies, or unapproved ammo categories.
- Do not silently rebalance approved V1 values. Keep approved constants data-driven where the architecture calls for data.
- Do not replace working implementations from earlier tasks with parallel/duplicate systems. Extend the confirmed repository state.
- Do not perform unrelated refactors, package upgrades, Unity-version changes, or architectural rewrites.

## ACCEPTANCE CRITERIA

1. Duplicate/spoofed Fire RPC cannot create duplicate authoritative shots or ammo consumption.
2. Player weapon damage never reduces teammate HP under FF-off rules.
3. Ammo/reload/heat/charge and active slot converge between host/owner after actions.
4. Melee delayed hit cancellation on switch remains correct over network.

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

In normal single-task mode, stop before TASK 096. Under `production/130_AUTONOMOUS_TASK_RUNNER.md`, write the checkpoint to the autonomous run log and continue only if the runner's gate rules permit it.
