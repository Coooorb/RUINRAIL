# Claude Coding Prompt Template

> **Status:** Project control document.
> **Game language:** English.

Use this template when handing a task to Claude Code. Model selection is an execution/cost decision, not a game-design rule; see `production/127_MODEL_ROUTING_GUIDE.md`.

```text
You are implementing TASK_XXX for this Unity project.

FIRST READ:
- technical/117_CODING_RULES_FOR_CLAUDE.md
- tasks/TASK_XXX_....md
- [only the task's listed spec files]

Rules:
- Do not implement anything in DO NOT IMPLEMENT.
- Do not invent game systems or permanent design choices.
- Preserve the existing architecture unless the task explicitly requires a scoped change.
- If the task conflicts with an approved specification, report the conflict instead of deciding yourself.
- Keep all balance values configurable where the specs mark them tunable.
- Run the task's relevant tests through the approved repo test harness when possible. Never claim PASS for a test that did not run.

At the end, report:
1. Files changed/created.
2. What was implemented.
3. Tests run and exact result.
4. Known limitations/TODOs that are actually in scope.
5. Any spec ambiguity or conflict.
```

When using Claude Code from the repository root, `CLAUDE.md` is the automatic entry point. For another coding interface that does not auto-load it, provide `CLAUDE_START_HERE.md` explicitly.
