# TASK 148 — Final MVP Completion Gate

> **Status:** Approved implementation task for the RUINRAIL V1/MVP roadmap.
> **Execution mode:** May be run individually or by the approved Autonomous Task Runner.
> **Game/code language:** English.

## PREREQUISITES

- TASK 147 must be complete or independently audited as already satisfied.
- Preserve the confirmed implementation state from all earlier completed tasks.
- If an earlier dependency is missing or broken, stop and report the dependency failure instead of inventing a substitute architecture.

## GOAL

Perform the definitive specification-vs-repository audit and declare COMPLETE only when every mandatory V1 requirement, engineering gate and required external content role is satisfied.

## FILES TO READ

Read this task, `CLAUDE.md`, `technical/117_CODING_RULES_FOR_CLAUDE.md`, and the following direct specifications before editing code:

- `production/122_MVP_SCOPE.md`
- `production/126_FINAL_MVP_CONTENT_COUNTS.md`
- `04_SCOPE_AND_NON_GOALS.md`
- `production/123_POST_MVP.md`
- `art/105_AUDIO_MUSIC.md`

Do not bulk-read the entire GDD. Read one additional specification only when a concrete direct dependency requires it, and record why in the completion report.

## ASSUMED EXISTING STATE

- Inspect the current repository before coding. Reuse the systems proven by earlier tasks instead of recreating them.
- Existing automated tests are regression contracts. Preserve them unless an approved specification intentionally changes the behavior.
- Use ScriptableObjects for shared immutable definitions/balance data and runtime objects/instances for mutable per-run/per-player state, following the data-architecture specs.
- Keep host-authoritative/network-ready boundaries where the existing architecture already provides them, but do not implement networking before its assigned tasks.

## REQUIREMENTS

1. Audit every V1/MVP requirement and fixed content count against the actual repository, not prior completion claims.
2. Run final full test harness, production validators and clean release-build validation again or reference same-run immutable artifacts where runner guarantees they are current.
3. Confirm all 33 weapons, 9 armor,16 accessories,10 consumables,9 normal enemies,6 Elites,6 Bosses,63 rooms/3 biomes, endless depth, Base, save, solo/duo/trio networking/life states/voting, UI/tutorial and audio roles are present.
4. Confirm post-MVP features remain absent unless harmless tooling only: PvP, dedicated servers, host migration, matchmaking, classes, crafting, etc.
5. Create `production/FINAL_MVP_COMPLETION_REPORT.md` with status `COMPLETE`, `INCOMPLETE`, or `BLOCKED_EXTERNAL_ASSET`. Never declare COMPLETE while mandatory final art/animation/audio content or a required test/build/service verification is genuinely missing.

## DO NOT IMPLEMENT

- Do not implement systems assigned to later tasks early unless this task explicitly requires a narrow integration seam.
- Do not introduce PvP, classes, stamina, durability, crafting, Gear Score, critical hits, weak spots, extra currencies, or unapproved ammo categories.
- Do not silently rebalance approved V1 values. Keep approved constants data-driven where the architecture calls for data.
- Do not replace working implementations from earlier tasks with parallel/duplicate systems. Extend the confirmed repository state.
- Do not perform unrelated refactors, package upgrades, Unity-version changes, or architectural rewrites.

## ACCEPTANCE CRITERIA

1. Final report maps every MVP requirement/count to concrete implementation/data/test evidence.
2. All local automated tests/validators/release build pass with truthful counts.
3. No unresolved critical duplication/loss/authority/save defect exists.
4. All mandatory external content roles (including exact 11 music roles and required stingers/SFX coverage) are present for COMPLETE; otherwise status remains blocked/incomplete.
5. No prohibited post-MVP scope was required to reach the result.

## TESTS

- The Unity project must compile after the task.
- Add focused automated tests for the behavior introduced by this task and for its important edge cases.
- Run the repository test harness after implementation:
  `UNITY_PATH="/c/Program Files/Unity/Hub/Editor/6000.3.24f1/Editor/Unity.exe" bash scripts/run-unity-tests.sh All`
- A zero-test run is a failure. Never report PASS for tests that did not actually run.
- Existing relevant EditMode and PlayMode suites must remain green.
- If Unity or an external service is unavailable, report the affected check as `NOT RUN` with the exact reason; never fabricate a result.

## TASK-SPECIFIC NOTES

- This is the terminal task. Do not create TASK 149 automatically. The final report must be evidence-based and must not lower the acceptance bar to force a green status.

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

In normal single-task mode, stop before TASK 149. Under `production/130_AUTONOMOUS_TASK_RUNNER.md`, write the checkpoint to the autonomous run log and continue only if the runner's gate rules permit it.
