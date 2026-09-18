# TASK 099 — Network Loot, Coins, Chests and Events

> **Status:** Approved implementation task for the RUINRAIL V1/MVP roadmap.
> **Execution mode:** May be run individually or by the approved Autonomous Task Runner.
> **Game/code language:** English.

## PREREQUISITES

- TASK 098 must be complete or independently audited as already satisfied.
- Preserve the confirmed implementation state from all earlier completed tasks.
- If an earlier dependency is missing or broken, stop and report the dependency failure instead of inventing a substitute architecture.

## GOAL

Network-authorize shared world loot, pickups, chest/event resolution, Merchant purchases and party Coin distribution.

## FILES TO READ

Read this task, `CLAUDE.md`, `technical/117_CODING_RULES_FOR_CLAUDE.md`, and the following direct specifications before editing code:

- `multiplayer/82_NETWORK_AUTHORITY.md`
- `items/32_LOOT_PICKUPS_AND_ITEM_TRANSFER.md`
- `dungeon/58_CHESTS_MERCHANT_LOOT.md`
- `dungeon/57_EVENTS.md`

Do not bulk-read the entire GDD. Read one additional specification only when a concrete direct dependency requires it, and record why in the completion report.

## ASSUMED EXISTING STATE

- Inspect the current repository before coding. Reuse the systems proven by earlier tasks instead of recreating them.
- Existing automated tests are regression contracts. Preserve them unless an approved specification intentionally changes the behavior.
- Use ScriptableObjects for shared immutable definitions/balance data and runtime objects/instances for mutable per-run/per-player state, following the data-architecture specs.
- Keep host-authoritative/network-ready boundaries where the existing architecture already provides them, but do not implement networking before its assigned tasks.

## REQUIREMENTS

1. Host owns world pickup/item instance IDs and arbitrates simultaneous pickup/drop attempts; exactly one destination can win.
2. Chest/event/merchant RNG/state/payment/reward executes once on host and replicates resolved state/results.
3. Coin pickups distribute evenly to party through one authoritative result; no client independently credits Coins.
4. Ground items remain shared and do not despawn within depth; leaving depth clears unresolved world pickups consistently on all peers.
5. Use transaction IDs/idempotency for purchases/payments/rewards.

## DO NOT IMPLEMENT

- Do not implement systems assigned to later tasks early unless this task explicitly requires a narrow integration seam.
- Do not introduce PvP, classes, stamina, durability, crafting, Gear Score, critical hits, weak spots, extra currencies, or unapproved ammo categories.
- Do not silently rebalance approved V1 values. Keep approved constants data-driven where the architecture calls for data.
- Do not replace working implementations from earlier tasks with parallel/duplicate systems. Extend the confirmed repository state.
- Do not perform unrelated refactors, package upgrades, Unity-version changes, or architectural rewrites.

## ACCEPTANCE CRITERIA

1. Race two clients for one item: exactly one obtains it, no duplicate/loss beyond defined outcome.
2. Chest/event opens/resolves once for all clients.
3. Coin total is conserved and replicated exactly.
4. Rejoin/state sync does not regenerate resolved rewards.

## TESTS

- The Unity project must compile after the task.
- Add focused automated tests for the behavior introduced by this task and for its important edge cases.
- Run the repository test harness after implementation:
  `UNITY_PATH="/c/Program Files/Unity/Hub/Editor/6000.3.24f1/Editor/Unity.exe" bash scripts/run-unity-tests.sh All`
- A zero-test run is a failure. Never report PASS for tests that did not actually run.
- Existing relevant EditMode and PlayMode suites must remain green.
- If Unity or an external service is unavailable, report the affected check as `NOT RUN` with the exact reason; never fabricate a result.

## TASK-SPECIFIC NOTES

- Any item/coin duplication under network races is a hard blocker.

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

In normal single-task mode, stop before TASK 100. Under `production/130_AUTONOMOUS_TASK_RUNNER.md`, write the checkpoint to the autonomous run log and continue only if the runner's gate rules permit it.
