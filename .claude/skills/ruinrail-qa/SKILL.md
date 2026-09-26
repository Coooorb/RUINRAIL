---
name: ruinrail-qa
description: RUINRAIL testing and verification — choosing and running targeted tests, diagnosing failures, flakiness, harness vs product bugs. Load with the domain skill for any bug fix or feature.
---

# RUINRAIL QA

**Targeted first.** Find existing coverage (`rg -l <Symbol> Assets/Game/Tests`), extend it rather than adding a new
fixture. Run only what the change can affect:
`./scripts/run-unity-tests.sh EditMode "Fixture"` · `./scripts/run-unity-tests.sh PlayMode "Fixture.Test"`
(writes `TestResults/*-targeted-results.xml`). Escalate up the ladder in `CLAUDE.md` §4 only with a reason.
PlayMode before EditMode when both are needed (some EditMode audits read the PlayMode XML).

**Determinism.** Fixed seeds; wait on `Time.time` deadlines, not frame counts (batch PlayMode is uncapped). No clock
seeds or random preconditions in assertions. Isolate fixtures: clear scene roots after entering the Dungeon; keep
impact targets off the player collider.

**Diagnose, don't retry.** On failure, decide: product bug, test bug, or harness/environment problem — and say which.
Never rerun until green. Never weaken a gameplay tolerance or delete an assertion to pass. Known harness facts (in
memory): an open Editor locks batch runs (wait, don't kill); the first batch run after a C# edit may fail on Windows
Smart App Control.

**Consequence over existence.** A test proves the shipping behaviour (state change in the composed runtime), not that
a type or method exists.

**Reporting.** Exact command → PASS/FAIL line from the harness. Zero tests = FAIL. Unity unavailable →
`NOT RUN — Unity executable unavailable`. No CSV/matrix/report artefacts for normal tasks.
