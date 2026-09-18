# Claude Task Protocol

> **Status:** Approved design specification unless explicitly marked as tunable.
> **Game language:** English. Planning discussions may be in German, but all player-facing text, code naming, comments, and Claude implementation specs should be English.
Every implementation request should be narrow.

## Task File Template

```text
GOAL

FILES TO READ

REQUIREMENTS

DO NOT IMPLEMENT

ACCEPTANCE CRITERIA

TESTS

EXPECTED FILES CHANGED
```

## Context Rule
Do not provide Claude every project MD for every task. Claude Code automatically receives root `CLAUDE.md`. The task should then read its own listed specs only. For non-Claude-Code workflows, provide `CLAUDE_START_HERE.md`, `technical/117_CODING_RULES_FOR_CLAUDE.md`, the task file, and only relevant dependencies.

## Completion Report
Claude should report:
- Files created/changed.
- What was implemented.
- Exact test command(s) actually run and exact results. If unavailable, explicitly report `NOT RUN` and why.
- Known limitations.
- Any ambiguity/conflict with the design specs.

## Conflict Rule
If a task instruction conflicts with approved documentation, Claude should not choose a new design. It should flag the conflict.

## Test Harness Rule
When a task has Unity tests in scope, use `scripts/run-unity-tests.ps1` or `scripts/run-unity-tests.sh` as defined in `technical/118_TESTING_STRATEGY.md`. Do not substitute an undocumented command unless the harness itself is the task being repaired.
