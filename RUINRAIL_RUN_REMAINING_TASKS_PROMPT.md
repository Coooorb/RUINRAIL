# RUINRAIL — Master Prompt for Autonomous Completion

Use this from the repository root in Claude Code after TASK 001–010 are already completed.

---

Continue development of RUINRAIL from the current repository state and execute **all remaining approved implementation tasks TASK 011 through TASK 148 automatically, strictly in numeric order**.

Do not ask me for permission between successful tasks. Do not batch multiple code-modifying tasks together. Treat every task as its own transaction/checkpoint with full compile, test and acceptance-criteria verification before the next task begins.

Before starting TASK 011:
1. Read `CLAUDE.md`.
2. Read `CLAUDE_START_HERE.md`.
3. Read `production/128_CURRENT_IMPLEMENTATION_STATE_AFTER_TASK_010.md`.
4. Read `production/129_REMAINING_TASK_ROADMAP.md`.
5. Read and obey `production/130_AUTONOMOUS_TASK_RUNNER.md`.
6. Read and obey `technical/117_CODING_RULES_FOR_CLAUDE.md`.
7. Inspect the current repository and run the existing test harness once to confirm the TASK 001–010 handoff is still green. Do not rebuild those systems from scratch.

Then begin `TASK 011` and follow this loop for every task through `TASK 148`:

- Read the current task file and only its listed direct specifications plus a truly necessary direct dependency.
- Inspect existing code/tests before editing.
- Implement only that task's scope and obey its DO NOT IMPLEMENT section.
- Reuse existing foundations; never create a parallel replacement because the existing code is unfamiliar.
- Compile.
- Run the exact tests/validators required by the task, including the RUINRAIL EditMode/PlayMode harness.
- A zero-test run is failure. Never call NOT RUN a PASS.
- Fix in-scope failures, rerun, and re-check every Acceptance Criterion.
- Append the required checkpoint to `production/AUTONOMOUS_RUN_LOG.md`.
- If status is PASS, immediately continue with the next numeric task without asking me.
- If the current task is a hard blocker under `production/130_AUTONOMOUS_TASK_RUNNER.md`, STOP at that task and give me the exact blocker, failed tests/errors, files changed, and safest next action. Do not skip it.
- If a presentation task is blocked only by genuinely missing external art/animation/audio files, complete the engineering portion, mark `BLOCKED_EXTERNAL_ASSET`, log every missing asset role, and continue independent engineering as permitted by the runner. Never pretend placeholders are final assets.
- If a live Sessions/Relay check cannot run because the external environment is unavailable, mark only that check NOT RUN and follow the runner's dependency-safety rule.

Critical invariants for the entire run:
- Never invent or rebalance game design. The GDD is authoritative.
- No PvP, classes, stamina, durability, crafting, Gear Score, crits, weak spots, extra currencies or unapproved ammo types.
- No dedicated server, host migration or matchmaking in V1.
- Do not silently upgrade Unity/packages.
- Keep items/Coins/save/network transactions atomic and idempotent. Stop on unresolved duplication/loss exploits.
- Keep host authority for network gameplay and persistent expedition outcomes.
- Keep seeded dungeon/enemy/loot RNG deterministic with separated streams.
- Keep Primary/Secondary weapon slots class-agnostic and only the active weapon consumes attack/reload/special input.
- Preserve the confirmed 0.18s Dash duration, 1.25s cooldown and 0.10s default dash iFrame baseline unless an authoritative GDD revision explicitly changes them.
- Friendly fire between players is OFF; environmental hazards can still hurt players.
- Do not implement post-MVP features to solve a V1 problem.

Run tasks 011 → 148 now. Do not stop for routine status updates or approvals between successful tasks.

At the end, provide one consolidated final response containing:
1. Final task reached.
2. PASS/BLOCKED/NOT RUN summary for all tasks.
3. Exact final test/validator/build results.
4. Content-count validation results.
5. Save/network/exploit-hardening status.
6. Any external art/audio/service blockers.
7. Path to `production/FINAL_MVP_COMPLETION_REPORT.md`.

Do not call the game complete unless TASK 148's evidence-based final gate permits the exact status `COMPLETE`.
