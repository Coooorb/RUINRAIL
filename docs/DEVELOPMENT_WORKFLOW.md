# RUINRAIL — Development Workflow

`../CLAUDE.md` holds the binding rules in short form. This document explains them for humans and gives the routing
tree, prompt format and examples. It does not repeat project facts (`CURRENT_STATE.md`) or decisions (`DECISIONS.md`).

## Philosophy
Know only what the current step needs, but close that step completely. The old default — read everything, plan a huge
task, touch many systems, run everything, write a large report — is retired. Small, verified, finished steps are cheaper
and safer on a release-candidate codebase.

## Modes and effort
| Mode | Use for | Effort | Written plan | Default verification ceiling |
|---|---|---|---|---|
| FAST | docs, text, tiny UI/data fix, obvious local bug | LOW | none | compile + one targeted test or a read-back |
| STANDARD | ordinary gameplay/UI/item bug, small local feature | MEDIUM | 5 lines | targeted EditMode/PlayMode + runtime consequence + screenshot if visual |
| HIGH_RISK | persistence, migrations, networking/authority, transactions, economy ownership, composition root, shared co-op state | HIGH | compact, before editing | targeted → relevant broader suite → real runtime/peer proof when risk demands |
| RELEASE | release validation/blockers, milestone audit, explicit production verification | HIGH | detailed allowed | full suites, validator, build, deterministic smoke, Duo/Trio (skill `ruinrail-release`) |

MAX effort is reserved for a blocker still unresolved after a serious HIGH-effort attempt. Do not default to High/Max.

**Escalation.** A FAST/STANDARD task that turns out to touch persistence, network authority, transactions, the
composition root or a migration is re-classified before continuing. A local STANDARD task is not upgraded because the
repository is large — risk decides the mode.

## Context budget
- FAST: a handful of files. STANDARD: search first, then a small group of relevant files; expand only on a discovered dependency.
- HIGH_RISK: broader, still search-driven. RELEASE: broad state allowed, but no duplicate historical reading.
- Search before reading (`rg`, filenames, symbols, callers); read ranges, not whole large files; never reopen unchanged files.
- Archives, old reports and git history are never startup context. "Maybe useful" does not justify reading a large report.
- Before running expensive validation, confirm relevant code changed since the last run of it.

## The loop
UNDERSTAND → PLAN → IMPLEMENT → RUN → TEST → LOOK → CRITIQUE → FIX → VERIFY → STOP.
Not every step needs an artefact; LOOK and CRITIQUE mean actually inspecting the result (image, log, runtime state),
not re-reading your own diff.

### Planning
- FAST: none. STANDARD: `Root cause / Files / Change / Verification / Risk`. HIGH_RISK: compact explicit plan before editing.
- Plans are temporary: `.claude/work/current-task.md` (overwrite it). A persistent plan is justified only for a
  multi-step feature or milestone — then keep one entry in the workboard (see below), not a new file.

### Vertical slices
New features start with the smallest complete playable slice — e.g. ONE recipe, ONE station, ONE input, ONE output,
ONE UI flow, save/load — working end-to-end in the shipping runtime before any breadth. Likewise one enemy behaviour,
one polished weapon interaction, one event type, one multiplayer interaction.

## Testing ladder
1. compile / static check
2. targeted EditMode — `./scripts/run-unity-tests.sh EditMode "FixtureName"` (`;`-separated names or a regex)
3. targeted PlayMode — `./scripts/run-unity-tests.sh PlayMode "FixtureName.TestName"`
4. the relevant validator (most run inside an EditMode fixture: e.g. `FinalReleaseCandidateValidatorTests`)
5. runtime proof (PlayMode capture, built-player smoke scenario, co-op proof runner)
6. broader suite (`EditMode` / `PlayMode` without filter)
7. build
8. smoke (`run-release-smoke.sh`, `run-coop-release-smoke.sh`)

Escalate only when risk requires it, a lower rung reveals broad impact, or the user asks. Do not run both full suites by
habit, rebuild after every edit, or rerun unchanged expensive tests. Filtered runs write `*-targeted-results.xml`, so
they never clobber the full-suite XML that `FinalMvpAudit` reads. `TestResults/` is disposable scratch output.

## Visual feedback loop
Visual work is judged from pixels, not code. Existing capture infrastructure (reuse it, don't build new):
- `Assets/Game/Tests/PlayMode/UiScreenCapture.cs` — any live menu canvas at 640×360 → `TestResults/PolishPreview/`.
- `Assets/Game/Tests/PlayMode/LiveDungeonCapture.cs` — live dungeon frame through the real camera rig + HUD → `TestResults/RegressionProof/`.
- Built player: `-smoke [-savedir dir] [-screenshot file.png]` (SmokeRunner).

Loop: capture before → change → run the capturing test → open the PNG → critique (checklist in `ruinrail-ui-visual`)
→ fix → capture again → stop. Scratch captures go to `TestResults/VisualReview/` as overwriteable `before.png` /
`after.png` / `latest.png`; keep a permanent screenshot only for a meaningful baseline.

## Gameplay feel loop
change → run → observe (PlayMode run, logs, capture, runtime state) → critique → fix → verify. Applies to weapon feel,
knockback, hit response, telegraphs, room/boss readability, UI responsiveness. Tests establish correctness; runtime
observation establishes experience. Never loosen a gameplay tolerance to make a test pass.

## Documentation
**No new Markdown by default.** Before creating any `.md`, ask: does it need to outlive the task? Is it useful later? Is
there a canonical home? Can that home be updated instead? Is it genuinely a new durable domain? Only "yes, no home,
new domain" creates a file — and it gets indexed in `README.md`.

Routing when a task changes durable information:
```
existing game design?            → update the doc in docs/design/
existing technical contract?     → update the doc in docs/technical/
durable project decision?        → docs/DECISIONS.md
project-level current truth?     → docs/CURRENT_STATE.md (only if important enough)
release status?                  → production/CURRENT_RELEASE_STATUS.md
temporary implementation detail? → no permanent doc (git commit message)
temporary plan / notes / probes? → .claude/work/
new durable domain, no home?     → new canonical doc + index entry in docs/README.md
```
Implementation that merely matches the existing design normally changes no docs. Git is the implementation history;
docs hold current truth, design, contracts and decisions — never a task diary. Never create `TASK_*`, `PROMPT_*`,
`*_REPORT`, `*_FIX_REPORT`, `*_PASS_REPORT` files.

Check after touching docs: `python3 scripts/check-docs.py` (broken links, root clutter, task/prompt files, unindexed
docs, duplicate release status, unbannered archived status docs). No Unity needed.

## Evidence
Tests and validators are the evidence. FAST/STANDARD completion = code change + test change if appropriate + relevant
verification + concise response. Do not generate before/after/matrix/proof CSVs, checklists or long reports. HIGH_RISK
may add artefacts only when they materially improve safety or debugging; RELEASE may use detailed evidence.

## Temporary work — `.claude/work/`
Gitignored. For `current-task.md`, scratch notes, investigation output, one-off probes, temporary screenshots. Reuse and
overwrite; delete probes when done; never let it become an archive.

## Workboard
No project workboard exists; none is needed while work arrives as individual prompts. If a multi-step milestone starts,
create exactly one `docs/WORKBOARD.md` with `NOW / NEXT / DEFERRED`, remove finished items, and index it.

## Subagents
FAST/STANDARD: none. HIGH_RISK: at most one focused reviewer searching for authority/lifecycle/race/edge cases while the
main agent implements. RELEASE: several only when the work is independent and parallel (e.g. Duo proof vs. Trio proof).
Never several agents reading the same repository.

## Build and smoke policy
Build and smoke are RELEASE tools, or HIGH_RISK proof when a change is specifically build-sensitive (scenes/build
settings, player-only code paths, co-op process wiring). Moving docs or editing tests never justifies a build.

## Background processes
One runner/waiter per suite; no duplicate pollers; don't react to stale completion notices; stop background tasks
cleanly; parallelize only independent work. An open Unity Editor locks batch runs — wait, don't kill it.

## Definition of done and stop rule
Done = the mode's evidence shows the shipping runtime composes and reaches the change, behaviour works, relevant tests
pass, no new console errors, visuals were looked at, feel was observed. Then STOP: no unrelated inspection, nearby
refactors, speculative improvements, extra reports or unrequested test escalation.

## Prompt format for future tasks
```
Task:            one concrete objective
Mode:            FAST | STANDARD | HIGH_RISK | RELEASE
Success criteria: 3–8 measurable outcomes
Scope:           what may change
Do not:          specific non-goals
Optional:        task-specific verification or visual requirement
```
Do not repeat git safety, the testing ladder, Windows/UGS wording, project background, release philosophy or authority
rules in prompts — CLAUDE.md and the skills carry them.

### Example
```
Task: Fix remote held-weapon visual after weapon switching.
Mode: STANDARD
Success criteria:
- owner sees correct weapon
- host sees correct weapon
- remote peer sees correct weapon
- no duplicate visual
- reconnect restores current visual
- Solo unchanged
Scope: weapon visual replication only
Do not: rebalance weapons; redesign art; refactor unrelated networking
```

## Final response shape
Root cause · what changed · what was tested (command → result) · result · real remaining blocker. Nothing else.

## Evaluating the workflow
Pilot with one FAST, one STANDARD and later one HIGH_RISK task. Compare: files read, duration, artefacts created
(should be none beyond code/tests), verification cost (rungs used), and result quality. No telemetry tooling — read the
session transcript and `git status`.
