# Review-Gated Completion Runner — TASK 149 through TASK 184 (Revision 2)

## Start State

Read in this order:

1. `production/131_CURRENT_PRODUCTION_STATE_AFTER_TASK_148.md`
2. `production/135_TASK148_GAP_ANALYSIS_ADDENDUM.md`
3. `production/132_COMPLETION_ROADMAP_TASK149_184.md`
4. `art/106_RUINRAIL_FINAL_ART_BIBLE.md`
5. `production/134_FINAL_PRODUCTION_ASSET_MANIFEST_RULES.md`

Treat TASK 001–148 as the proven baseline unless fresh evidence finds a regression.

## Sequential Rule

Execute exactly TASK 149 → TASK 184 in numeric order. One task at a time. Never silently skip a blocker or substitute a mock for a required live/human/hardware check.

For every task:

1. Read the task immediately before work.
2. Inspect only direct dependencies plus current repository evidence.
3. Implement only in-scope work.
4. Run the task's required tests/validators/build checks.
5. Fix in-scope failures and rerun.
6. Re-read acceptance criteria and `DO NOT IMPLEMENT`.
7. Append immutable checkpoint to `production/COMPLETION_RUN_LOG.md`.
8. Continue only if gate rules permit.

## Checkpoint Status

Use exactly one:

`PASS | FAIL_BLOCKER | NOT_RUN_BLOCKER | WAITING_HUMAN_REVIEW | BLOCKED_EXTERNAL_ASSET | BLOCKED_EXTERNAL_SERVICE | BLOCKED_EXTERNAL_HARDWARE`

Record changed paths, manifest deltas, exact tests/results, acceptance status, required human/external action, and known limitations.

## Human Review Stops

STOP after TASK 152, TASK 165 and TASK 176 for visual/audio approval.

TASK 179 is a design-signoff task: any unresolved PROTOTYPE disposition => `WAITING_HUMAN_REVIEW`.

TASK 182 is the mandatory human fun/readability/balance gate and cannot be replaced by automation.

## External Content Rule

TASK 153–175 require real production art/audio. If content is missing, stop `BLOCKED_EXTERNAL_ASSET` with the exact manifest roles. Do not generate fake placeholders and call them final, and never weaken validators.

## Live Service Rule

TASK 180 may require user-authorized UGS project linking. Never store credentials.
TASK 181 requires real Sessions/Relay host/join on two built clients. Fake transport/local unit tests do not satisfy it.

## Hardware Rule

TASK 183 requires profiling on intended target hardware. If unavailable, stop `BLOCKED_EXTERNAL_HARDWARE`.

## Terminal Rule

TASK 184 is terminal. It may say `RELEASE_COMPLETE` only with fresh proof for assets, tests, design-signoff, live multiplayer, human playtest, hardware profiling and release build/smoke. Never create TASK185 automatically.
