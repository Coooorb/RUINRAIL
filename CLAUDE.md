# RUINRAIL — Claude Operating Rules

RUINRAIL is a top-down 2D pixel-art PvE extraction roguelite for 1–3 players, built in Unity `6000.3.24f1` (URP 2D).
It is a working release candidate. Protect it.

**Principle:** know only what the current step needs, but close that step completely.
Expanded process, examples and rationale: `docs/DEVELOPMENT_WORKFLOW.md`. Doc index: `docs/README.md`.

## 1. Startup (every task)
1. Classify the task mode (§2). If the prompt gives one, use it; escalate if the work proves riskier (§2).
2. Read `docs/CURRENT_STATE.md` only when project-level context is needed.
3. Load only the relevant skill(s) from `.claude/skills/` (§8).
4. Search first (`rg`, filename, symbol, callers), then read only the needed ranges. Never reread an unchanged large file.
5. Open design (`docs/design/`) or technical (`docs/technical/`) specs only when the task depends on them.
6. Never read `production/archive/` or git history by default. Historical reports are not startup context.

## 2. Task modes
| Mode | Typical work | Effort | Plan | Verification |
|---|---|---|---|---|
| FAST | docs, text, tiny UI/data fix, obvious local bug | LOW | none | cheapest meaningful check |
| STANDARD | ordinary gameplay/UI/item bug, small local feature | MEDIUM | 5-line (below) | targeted tests + runtime consequence (+ screenshot if visual) |
| HIGH_RISK | persistence, save migration, networking/authority, transactions, economy ownership, composition root, shared co-op state | HIGH | compact explicit plan before editing | targeted first, then broader relevant suite, real runtime/peer proof if risk demands |
| RELEASE | release validation, release blocker, milestone audit, explicit production verification | HIGH | detailed allowed | `ruinrail-release` skill |

- STANDARD plan: `Root cause / Files / Change / Verification / Risk`. Keep it in your head or in `.claude/work/current-task.md`.
- **Escalate** before continuing if a FAST/STANDARD task turns out to touch persistence, network authority, transactions,
  the composition root or a migration. Task risk decides the mode — repository size never does.
- MAX effort only for a blocker still unresolved after a serious HIGH attempt.
- Context budget: FAST a handful of files; STANDARD a small group found by search, expand only on discovered dependency;
  HIGH_RISK broader but still search-driven; RELEASE broad state, no duplicate historical reading. "Maybe useful" is not a reason to read.

## 3. Development loop
UNDERSTAND → PLAN → IMPLEMENT one coherent step → RUN → TEST → LOOK → CRITIQUE → FIX → VERIFY → **STOP**.
- One task = one coherent improvement (one bug, one interaction, one seam, one visual issue, one vertical slice).
- New features: the smallest complete playable slice end-to-end (incl. save/load, UI, co-op if in scope) before breadth.
- Before creating a helper/service/test/script/doc, search for an existing one. Reuse it.

## 4. Testing ladder (cheapest useful proof first)
1 compile/static → 2 targeted EditMode → 3 targeted PlayMode → 4 relevant validator → 5 runtime proof → 6 broader suite → 7 build → 8 smoke.
- Targeted: `./scripts/run-unity-tests.sh EditMode "<Fixture;Fixture.Test>"` (PowerShell: `-TestFilter`). Writes `*-targeted-results.xml`.
- Full suites, builds and smoke only when risk, discovered broad impact, or the user requires it. Never by habit; never rerun unchanged expensive tests.
- A test PASSES only when the harness ran and said PASS. Zero tests = FAIL. Unity missing → `NOT RUN — Unity executable unavailable`.
- Known harness quirks live in memory (e.g. an open Editor locks batch runs — wait, don't kill it).

## 5. Definition of done
Not done because code exists, compiles, or a unit test exists. Done when the evidence the mode requires shows:
the **shipping runtime actually composes and reaches** the change; intended behaviour works; relevant tests pass; no new
console/runtime errors; visual work was **looked at** (screenshot); feel-sensitive work was **observed at runtime**.
Guard explicitly against "system exists and tests pass, but the shipping runtime never composes it."

## 6. Stop rule
When the success criteria pass: STOP. No unrelated inspection, nearby refactors, speculative improvements, extra reports,
unrequested test escalation, or "while I'm here" work.

## 7. Documentation rule
- **No new Markdown by default.** Git is the implementation history. Never create `TASK_*.md`, `PROMPT_*.md`, `*_REPORT.md`.
- Durable change → update its one canonical home (routing tree in `docs/DEVELOPMENT_WORKFLOW.md` §Docs):
  design → `docs/design/`, contract → `docs/technical/`, decision → `docs/DECISIONS.md`, project truth → `docs/CURRENT_STATE.md`,
  release truth → `production/CURRENT_RELEASE_STATUS.md`.
- Implementation that merely matches existing design normally needs **no** doc change.
- A new permanent doc only for a genuinely new durable domain, and it must be indexed in `docs/README.md`.
- Task-local plans, notes, probes, screenshots: `.claude/work/` (gitignored; overwrite, don't accumulate; delete probes when done).
- Evidence = tests/validators. Do not copy it into CSVs/matrices/reports for FAST/STANDARD work.
- After moving/removing docs: `python3 scripts/check-docs.py`.

## 8. Skills (load selectively, never all)
`ruinrail-gameplay`, `ruinrail-ui-visual`, `ruinrail-networking`, `ruinrail-persistence`, `ruinrail-qa`, `ruinrail-release`.
Tiny text change: none (or UI). Gameplay bug: gameplay + qa. Network bug: networking + qa. Save bug: persistence + qa.
Release: release + only the domain skills actually needed.

## 9. Subagents
FAST/STANDARD: none. HIGH_RISK: at most one focused reviewer (authority/lifecycle/race/edge cases) when genuinely useful.
RELEASE: several only for independent parallel work. Never several agents reading the same code.

## 10. Background processes
One runner/waiter per suite. No duplicate pollers. Ignore stale completion notifications for work already handled.
Stop background tasks cleanly. Parallelize only independent work.

## 11. Game and code rules (non-negotiable)
- Do not invent systems or change approved rules/balance without an explicit design update; report spec conflicts instead.
  Excluded forever-unless-redesigned: PvP, classes, abilities, crits, weak spots, stamina, durability, crafting materials, extra currencies.
- Missing gameplay-relevant parameter: expose it as config, label any temporary value, report it — never present it as design.
- Data-driven balance (definitions/configs), stable definition IDs, static definition / runtime state / save data kept separate.
- No monolithic managers, no second implementation of an existing system, no unrelated refactors, no speculative frameworks.
- Host authority for shared gameplay state. Approved grid/room system only.
- Toolchain pins in `docs/ENVIRONMENT.md` — never silently change Unity or package versions.
- Be conservative with `Assets/` serialized data, `.meta`, scenes, prefabs, ScriptableObjects, `Resources/`, asmdefs, `Packages/`, `ProjectSettings/`.

## 12. Worktree safety
The working tree is authoritative and may hold uncommitted work. Never `git checkout/restore/reset/clean/stash` or any
broad revert; reverse only your own edits. Commit only when asked.

## 13. Environment truth
Report exactly, never fake: `Windows x64: NOT RUN — module unavailable` (do not install it);
`Live UGS Sessions/Relay: NOT RUN — project/service configuration unavailable`.

## 14. Final response
Root cause · what changed · what was tested (exact command → result) · remaining blocker, if any. No grep narration,
no chronological logs, no restated task, no summaries of unchanged systems.
