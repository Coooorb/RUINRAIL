# Claude — Start Here: RUINRAIL

> **Status:** Project control document.
> **Game language:** English.

This project is intentionally split into many small specifications. Do not ingest every file for every task.

## Before Any Coding Task
0. Confirm the repository toolchain against `ENVIRONMENT.md`; do not silently change pinned versions.
1. Read `technical/117_CODING_RULES_FOR_CLAUDE.md`.
2. Read the assigned `tasks/TASK_XXX_*.md` file.
3. Read only the specs listed by that task under **FILES TO READ**, plus a direct dependency only if genuinely needed.
4. Implement only the task scope.
5. Do not invent features to fill gaps. Check `06_OPEN_DECISIONS.md`; if a missing choice matters to the task, report it rather than silently deciding.

## Canonical Priority
When instructions disagree, use this priority unless the user explicitly updates the design:
1. Latest explicit user instruction/task.
2. Approved design specification.
3. Technical architecture specification.
4. Task acceptance criteria.
5. Existing implementation details.

A coding convenience must never silently override an approved game rule.

## After the Task
Report changed files, implementation summary, exact test commands actually run/results, known limitations, and any design conflict/ambiguity. Use the repo test harness in `scripts/` whenever tests are in scope.
