# Install RUINRAIL Completion Phase Revision 2

1. Extract this package directly into the RUINRAIL Unity repository root (the folder containing `Assets`, `Packages`, `ProjectSettings`, existing `tasks`, `production`, `technical`, and `CLAUDE.md`).
2. Merge folders. Do not place this package one directory deeper.
3. This package adds/replaces completion-phase docs/tasks only. It does not replace TASK001–148 implementation.
4. Use `RUN_TASK_149_TO_184_REVIEW_GATED.md` as the Claude Code start prompt.
5. The runner intentionally stops for human review/external assets/services/hardware. Do not tell Claude to bypass those stops.

Revision 2 extends the prior 149–180 draft to TASK184 so the concrete TASK148 gaps each have a dedicated release task: deprecated Physics2D migration, loading/error UX, 35 PROTOTYPE-value sign-off, real UGS composition, two-client Relay, human balance, target-hardware profiling and terminal release audit.
