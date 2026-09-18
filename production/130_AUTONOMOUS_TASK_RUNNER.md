# Autonomous Task Runner — TASK 011 through TASK 148

> **Status:** Approved development-workflow protocol. It does not change game design.
> **Purpose:** Allow Claude Code to execute all remaining implementation tasks sequentially without requesting permission after each successful task while preserving the same quality gates as supervised one-task execution.

## Start State

TASK 001 through TASK 010 are treated as completed only if the current repository still passes their existing regression tests. Read `production/128_CURRENT_IMPLEMENTATION_STATE_AFTER_TASK_010.md` before TASK 011. Never recreate these systems from scratch.

## Non-Negotiable Sequential Rule

Execute exactly one task at a time in numeric order: TASK 011, then 012, continuing through TASK 148. Never implement two code-modifying tasks in parallel. Never skip a failed mandatory task merely to make progress. A later task may begin only after the current task's gate is resolved according to the rules below.

For every task:
1. Read `CLAUDE.md`, the current task file, `technical/117_CODING_RULES_FOR_CLAUDE.md`, and only the task's `FILES TO READ` plus a truly direct dependency if needed.
2. Inspect the current repository implementation and existing tests before editing.
3. Implement only the current task. Prefer extending existing foundations over parallel systems.
4. Compile and run the tests/validators required by the task. Run the repository `All` harness unless the task explicitly adds another check.
5. Fix in-scope failures and rerun until PASS or until a real blocker is established.
6. Re-read Acceptance Criteria and DO NOT IMPLEMENT before closing the task.
7. Append a checkpoint to `production/AUTONOMOUS_RUN_LOG.md`.
8. Continue automatically to the next task only when the gate permits it. Do not ask the user for permission between successful tasks.

## Checkpoint Log Format

Append one section per task:

```markdown
## TASK NNN — Name
Status: PASS | FAIL_BLOCKER | NOT_RUN_BLOCKER | BLOCKED_EXTERNAL_ASSET
Changed: <files or concise groups>
Tests: <exact commands and exact discovered/passed/failed/not-run results>
Acceptance: <all pass, or list failures>
Notes: <prototype tunables, direct-dependency reads, limitations>
```

Never overwrite prior checkpoint history.

## Gate Rules

### PASS → Continue
Continue automatically only when:
- project compiles;
- required local automated tests actually ran with >0 discovered tests and zero failures;
- current task Acceptance Criteria are satisfied;
- no known dependency-corrupting defect remains.

### Hard Blocker → STOP
Stop the entire autonomous run at the current task and report the blocker when any of these remain unresolved after reasonable in-scope fixes:
- compile/build failure;
- Package Manager/resolver conflict requiring an unapproved version change;
- contradictory design specs that materially affect implementation and cannot be resolved by the documented priority rules;
- deterministic dungeon/network state divergence;
- item/Coin/XP duplication or unintended permanent-loss exploit;
- save migration/atomic-write defect that risks destroying a valid save;
- host-authority violation or network race that permits invalid persistent outcomes;
- mandatory local tests cannot run and later tasks would rely on unverified behavior.

Do not skip the blocking task. Do not mark it PASS. Do not silently weaken tests or acceptance criteria.

### External Art/Audio Asset Blocker → Continue Independent Engineering
For presentation tasks (especially 138, 140, 141), source art/animation/audio may not exist in the repository. In that case:
- complete the code/data integration and placeholder-safe runtime behavior that can be completed truthfully;
- record exactly which required external asset roles are missing as `BLOCKED_EXTERNAL_ASSET`;
- continue with later engineering tasks that do not depend on the missing file itself;
- TASK 148 must remain `BLOCKED_EXTERNAL_ASSET` or `INCOMPLETE` until mandatory final content is actually supplied.

Never generate fake test PASS results and never claim placeholder silence/art is final production content.

### External Online Service Unavailable
If Relay/Sessions live-service verification cannot run because credentials/network/service environment are unavailable, mark that specific check `NOT RUN` with the exact reason. Continue only when local deterministic/service-adapter tests establish a safe dependency for later work. Final release/completion reports must preserve the NOT RUN status until a real smoke test is performed.

## Already-Satisfied Work

If the current repository already satisfies all requirements of a task because an earlier task legitimately implemented a narrow prerequisite:
- do not rewrite working code merely to create a diff;
- audit the current implementation against the current task;
- add missing focused tests/validation if necessary;
- run the required suites;
- log PASS with `No production change required` when justified.

## Quality Rules for Long Autonomous Runs

- Do not rely on memory of old task text. Re-read the current task immediately before implementation and again before closure.
- Keep diffs task-sized even though the overall run is large.
- Never defer a failing regression with comments such as “fix later” when later tasks depend on it.
- Preserve approved V1 numbers. Prototype/tunable values must be named as such in the log.
- Keep RNG streams deterministic and separated by system purpose.
- Keep item/save/network transactions idempotent and ownership-safe.
- No monolithic GameManager, no global singleton accumulation, no concrete weapon/enemy ID switch architecture.
- Prefer data-driven definitions + composable behavior + narrow interfaces/events.
- Do not edit GDD design values to make implementation/tests easier.

## Periodic Regression Gates

Every individual task already runs the relevant/full test harness. In addition, treat TASK 082, 090, 108, 118, 128, 130, 145, 146, 147 and 148 as explicit phase gates. At a phase gate, run all production validators plus full EditMode/PlayMode tests and preserve a report artifact.

## End Condition

The autonomous sequence ends only when:
- TASK 148 has produced `production/FINAL_MVP_COMPLETION_REPORT.md`; or
- a hard blocker forced a stop.

TASK 148 is forbidden from declaring `COMPLETE` merely because all code tasks were attempted. It must audit actual mandatory V1 content, tests, release build, online-service verification status, and required external presentation/audio assets.
