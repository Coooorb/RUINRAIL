---
name: ruinrail-release
description: RUINRAIL RELEASE mode only — release validation, release blockers, milestone audits, explicitly requested production verification. Heavy: full suites, validator, build, deterministic smoke, Duo/Trio proofs.
---

# RUINRAIL release

Use only in RELEASE mode. Current truth: `production/CURRENT_RELEASE_STATUS.md` (update it in place at the end).

**Gate sequence** (stop and fix at the first real failure; no retries-until-green):
1. Full PlayMode, then full EditMode: `./scripts/run-unity-tests.sh PlayMode` → `EditMode` (EditMode's
   `FinalMvpAudit` reads the PlayMode XML; it writes `TestResults/final_mvp_completion.md`).
2. `FinalReleaseCandidateValidator` (runs inside `FinalReleaseCandidateValidatorTests`; report
   `TestResults/final_release_candidate.md`): all rules PASS, broken fixture fails every family, frozen snapshot equals
   `production/FINAL_RELEASE_FROZEN_BASELINE.csv`. Never edit the baseline to make it pass without an approved design change.
3. Non-development build of the available platform (macOS here: `ReleaseBuildTool`, report `TestResults/build_report_macos.md`).
4. Deterministic release smoke: `scripts/run-release-smoke.sh` (pinned seeds from `release_smoke_manifest.csv`, every
   scenario consecutive, no retries). Diagnose flakes with `run-smoke-loop.sh`; never widen tolerances.
5. Real-peer co-op: Duo `scripts/run-coop-release-smoke.sh`; Trio via `run-coop-expedition-proof.sh` with three processes.

**Wording (exact).** `Windows x64: NOT RUN — module unavailable` (never install the module automatically).
`Live UGS Sessions/Relay: NOT RUN — project/service configuration unavailable`.

**Blocker policy.** A release blocker = data loss/duplication, crash or soft-lock on a shipping path, a shipping system
not composed at runtime, authority breach, save incompatibility, failed gate above. Everything else is a
NON-BLOCKING KNOWN ISSUE, listed in `CURRENT_RELEASE_STATUS.md`.

**Evidence.** Harness output, validator report, build report and smoke `runs.csv` are the evidence. Summarize them in
`CURRENT_RELEASE_STATUS.md`; move the previous detailed audit to `production/archive/` with a SUPERSEDED banner
pointing to the current status (the validator checks the banners it lists). Parallel agents only for independent
proofs (e.g. Duo vs Trio).
