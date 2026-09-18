# Install the RUINRAIL Autonomous-Run Patch

This ZIP is intentionally rooted at the Unity repository root.

Extract/merge its contents directly into the repository directory that already contains `Assets/`, `tasks/`, `production/`, and `CLAUDE.md`.

After extraction, the repository must contain at minimum:

- `production/128_CURRENT_IMPLEMENTATION_STATE_AFTER_TASK_010.md`
- `production/129_REMAINING_TASK_ROADMAP.md`
- `production/130_AUTONOMOUS_TASK_RUNNER.md`
- `production/AUTONOMOUS_RUN_LOG.md`
- `tasks/TASK_011_ammo_reload.md`
- every sequential task through `tasks/TASK_148_final_mvp_gate.md`
- `RUN_REMAINING_TASKS_PROMPT.md`

The patch intentionally replaces the older terse TASK 011–030 specifications with the expanded autonomous-safe versions and adds TASK 031–148.

Do not extract this patch into a nested documentation folder that Claude Code is not running from. Claude Code should be launched from the same repository root where `CLAUDE.md`, `Assets/`, `tasks/`, and `production/` are visible.
