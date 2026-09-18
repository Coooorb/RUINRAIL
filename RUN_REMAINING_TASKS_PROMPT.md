# RUINRAIL — Master Prompt for Autonomous Completion (Repo-Root Patch Version)

Use this from the Unity repository root after TASK 001–010 are completed and after `RUINRAIL_AUTORUN_REPO_ROOT_PATCH.zip` has been extracted/merged into that same repository root.

---

Continue development of RUINRAIL from the current repository state and execute **all remaining approved implementation tasks TASK 011 through TASK 148 automatically, strictly in numeric order**.

Do not ask me for permission between successful tasks. Do not batch multiple code-modifying tasks together. Treat every task as its own transaction/checkpoint with full compile, test and acceptance-criteria verification before the next task begins.

## Mandatory pre-flight before TASK 011

1. Confirm you are running from the repository root that contains `Assets/`, `CLAUDE.md`, `tasks/`, and `production/`.
2. Read `CLAUDE.md`.
3. Read `CLAUDE_START_HERE.md`.
4. Read `production/128_CURRENT_IMPLEMENTATION_STATE_AFTER_TASK_010.md`.
5. Read `production/129_REMAINING_TASK_ROADMAP.md`.
6. Read and obey `production/130_AUTONOMOUS_TASK_RUNNER.md`.
7. Read and obey `technical/117_CODING_RULES_FOR_CLAUDE.md`.
8. Verify that **every task file TASK 011 through TASK 148 exists with no missing number**. There must be exactly **138 remaining numbered task specifications** in that numeric range. Do not start implementation if any numbered task specification in the range is missing.
9. Ensure `production/AUTONOMOUS_RUN_LOG.md` exists. If it is missing but the runner protocol and all task specifications are present, create it as an append-only execution log; a missing log file by itself is **not** a design or implementation blocker.
10. Inspect the current repository and run the existing test harness once to confirm the TASK 001–010 handoff is still green. Do not rebuild those systems from scratch.

If steps 4–8 fail because planning/task specification files are genuinely missing, STOP at pre-flight and report exactly what is missing. Do not invent replacement specifications.

If all pre-flight checks pass, begin `TASK 011` immediately.

## Per-task loop — TASK 011 through TASK 148

For each task, in strict numeric order:

- Read the current task file and only its listed direct specifications plus a truly necessary direct dependency.
- Inspect existing code/tests before editing.
- Implement only that task's scope and obey its `DO NOT IMPLEMENT` section.
- Reuse existing foundations; never create a parallel replacement because the existing code is unfamiliar.
- Compile the project.
- Run the exact tests/validators required by the task, including the RUINRAIL EditMode/PlayMode harness unless the task explicitly says otherwise.
- A zero-test run is a failure. Never call `NOT RUN` a `PASS`.
- Fix in-scope failures, rerun the affected tests, and re-check every Acceptance Criterion.
- Append the required checkpoint to `production/AUTONOMOUS_RUN_LOG.md`.
- If status is `PASS`, immediately continue with the next numeric task without asking me.
- If the current task is a hard blocker under `production/130_AUTONOMOUS_TASK_RUNNER.md`, STOP at that task and give me the exact blocker, failed tests/errors, files changed, and safest next action. Do not skip it.
- If a presentation task is blocked only by genuinely missing external art/animation/audio files, complete the engineering portion, mark `BLOCKED_EXTERNAL_ASSET`, log every missing asset role, and continue independent engineering as permitted by the runner. Never pretend placeholders are final assets.
- If a live Sessions/Relay check cannot run because the external environment is unavailable, mark only that check `NOT RUN` and follow the runner's dependency-safety rules.

## Critical invariants for the entire run

- Never invent or rebalance game design. The GDD and approved task specifications are authoritative.
- No PvP.
- No character classes.
- No stamina.
- No durability.
- No crafting.
- No Gear Score.
- No critical hits.
- No weak spots.
- No extra currencies.
- No unapproved ammo types.
- No dedicated server for V1.
- No host migration for V1.
- No public matchmaking for V1.
- Do not silently upgrade Unity or package versions.
- Keep item, Coin, save, extraction, merchant, storage and network transactions atomic/idempotent where applicable.
- Stop on any unresolved item/Coin/XP duplication exploit or unintended persistent item-loss defect.
- Keep the host authoritative for network gameplay and persistent expedition outcomes.
- Keep seeded dungeon, encounter, loot and related RNG deterministic with separated RNG streams.
- Keep Primary and Secondary weapon slots class-agnostic.
- Only the active weapon may consume attack, reload or special input.
- Preserve the confirmed **0.18 second Dash duration**, **1.25 second Dash cooldown**, and **0.10 second default Dash iFrame baseline** unless an authoritative GDD revision explicitly changes them.
- Friendly fire between players is OFF.
- Environmental hazards can still hurt players.
- Do not implement post-MVP systems merely to solve a V1 task.
- Do not create monolithic manager classes or accumulate unrelated global singletons.
- Prefer data-driven definitions, composable gameplay behaviours, narrow interfaces/events/services.
- Do not edit GDD design values merely to make implementation/tests easier.
- Do not weaken/remove/rewrite a failing test merely to make the build green unless the test is demonstrably inconsistent with the approved specification.

## Quality sequence for every task

Current task spec
→ inspect current implementation
→ implement only current scope
→ compile
→ focused tests
→ full required test harness
→ fix failures
→ rerun
→ verify Acceptance Criteria
→ append autonomous checkpoint
→ continue only when the gate permits it

If a task is already fully satisfied because an earlier implementation legitimately covered it:

- do not rewrite working code just to create a diff;
- audit the implementation against the current task;
- add missing validation/tests if necessary;
- run the required tests;
- mark the task `PASS` with `No production change required` when justified;
- continue normally.

Treat TASK 082, 090, 108, 118, 128, 130, 145, 146, 147 and 148 as especially strict phase gates as defined by the runner/task specs.

## End condition

Run TASK 011 → TASK 148 now. Do not stop for routine progress approvals between successful tasks.

The autonomous run ends only when:

- TASK 148 has been completed and produced `production/FINAL_MVP_COMPLETION_REPORT.md`, or
- a hard blocker requires the run to stop.

At the end, provide one consolidated final response containing:

1. Final task reached.
2. PASS / BLOCKED / NOT RUN summary for all tasks.
3. Exact final EditMode/PlayMode test results.
4. Production-validator results.
5. Release-build result.
6. Final content-count validation.
7. Save/network/exploit-hardening status.
8. Any remaining external art/audio/service blockers.
9. Final MVP status.
10. Path to `production/FINAL_MVP_COMPLETION_REPORT.md`.

Do not call the game complete merely because every task was attempted. Do not create TASK 149 or expand into post-MVP scope.
