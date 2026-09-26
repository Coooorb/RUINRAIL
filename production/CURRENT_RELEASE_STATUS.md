# RUINRAIL — Current Release Status

> The **only** current human-readable release/production status. Update it in place after a RELEASE-mode pass; move
> superseded detail to `archive/`. `FinalReleaseCandidateValidator` requires this file and requires every archived
> status doc it lists to carry a `SUPERSEDED` banner pointing here. Last updated 2026-09-25.

## State
**FINAL_RELEASE_CANDIDATE_COMPLETE** (repository-local). No repository-local release blocker. Branch
`release-candidate-2026-09-25`, HEAD `31ea9b1`. Unity `6000.3.24f1`, pins unchanged.

## Baselines (latest recorded full runs, 2026-09-25)
| Gate | Result |
|---|---|
| PlayMode (full) | PASS — 846/846, 0 failed, 0 skipped |
| EditMode (full) | PASS — 978/980, 0 failed, 2 skipped by design (manual calibration; live UGS check) |
| FinalReleaseCandidateValidator | PASS — 55/55 rules; deliberately broken fixture fails every rule family |
| Frozen balance snapshot | equals `FINAL_RELEASE_FROZEN_BASELINE.csv` |
| Release smoke (`scripts/run-release-smoke.sh`, pinned seeds) | PASS — 40/40 at RC audit (fresh/returning/death/cache 10/10 each); 1/1 each on the final cleanup build |
| Duo co-op smoke (real host + client, seed 46) | PASS — 5/5 at RC audit; 2/2 on the final cleanup build |
| Trio co-op (three real processes) | PASS — 2/2 |
| Long run D1→D13 (built player) | PASS — 3/3 |
| macOS non-development build | PASS — Succeeded, 0 errors, 176.1 MB |
| Windows x64 | `Windows x64: NOT RUN — module unavailable` |
| Live UGS Sessions/Relay | `Live UGS Sessions/Relay: NOT RUN — project/service configuration unavailable` |

## Known non-blocking issues
- Pack pile-up transient (≤~0.4 tiles inside a collider for one physics step; bodies never leave the room).
- Mono heap drift ≈0.09 MB per depth in long runs (no object growth).
- Clock-seeded smoke layouts can miss the direct-hit reference placement (not a release gate; official smoke is seed-pinned).
- Player knockback/stagger and the Defibrillator became live in the RC audit — owner feel check pending.

## Outstanding external / human gates
- Windows x64 build + smoke on a machine with the Windows build module.
- Live UGS Sessions/Relay with a linked Unity project.
- Windows device profiling; owner manual playtest checklist.
- Visual slice approval record `VISUAL_SLICE_APPROVAL.md` still reads `Verdict: NOT_APPROVED` (human decision; no tool may set it).

## Detail (historical, read only when needed)
- `archive/FINAL_RELEASE_CANDIDATE_AUDIT.md` — full RC audit (33 sections, evidence map).
- `archive/FINAL_RELEASE_CLEANUP_REPORT.md` — co-op reactive passives, rebinding contract, final baselines.
- Evidence folders under `TestResults/FinalReleaseAudit/`, `TestResults/FinalReleaseCleanup/` (local, gitignored).
