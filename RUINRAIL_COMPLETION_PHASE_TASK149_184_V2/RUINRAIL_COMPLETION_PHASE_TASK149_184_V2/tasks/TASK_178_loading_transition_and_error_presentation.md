# TASK 178 — Loading, Transition and Recoverable Error Presentation

> **Status:** Approved RUINRAIL completion/release task.
> **Execution mode:** Review-gated completion runner or supervised single-task execution.
> **Game/code language:** English.

## PREREQUISITES

- TASK 177 PASS.
- Final UI/art language from TASK 163 and visual gate TASK 165 approved.

## GOAL

Replace abrupt/silent scene transitions and raw failure states with minimal production-ready RUINRAIL loading/transition/error presentation while preserving the existing synchronous scene architecture unless a measured defect requires otherwise.

## REQUIREMENTS

1. Add a production loading/transition overlay for boot/menu → Shelter, Shelter → expedition, depth transition/Transit, expedition → Shelter and any other existing scene transition path.
2. Use final RUINRAIL UI art/font; no grey placeholder panels or legacy runtime font.
3. Provide clear recoverable error UI for live-service connection/join failures, save/load failure surfaced by existing diagnostics, and scene/boot failure paths that can be handled safely.
4. Do not fake progress percentages when no real progress metric exists; use an indeterminate treatment instead.
5. Prevent double-submit/input leakage during transition.
6. Preserve save/transaction semantics and do not swallow exceptions that existing diagnostics need to record.
7. Add automated coverage for transition state/input gating and error-view-model behavior where practical.

## ACCEPTANCE CRITERIA

1. No normal scene transition presents an unexplained blank/raw frame in the release build.
2. Recoverable service/save errors are human-readable and return the player to a safe action path.
3. Final font and approved UI style are used.
4. Regression suite remains green.
