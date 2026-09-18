---
name: final-audit-report-regenerated-on-mac
description: FinalMvpAuditTests rewrites production/FINAL_MVP_COMPLETION_REPORT.md every EditMode run; on macOS/fresh clones the build+smoke lines become NOT RUN — do not commit that regenerated report
metadata:
  type: project
---

`FinalMvpAudit` (EditMode) writes `production/FINAL_MVP_COMPLETION_REPORT.md`, which is git-tracked. Since 2026-09-18 the
"Clean non-development release build (Windows x64)" and "Built-player smoke" lines are `NOT RUN` when
`TestResults/build_report.md` / `TestResults/smoke_result.json` are absent (fresh clone, macOS), and `FAIL` only when
present but failed. `TestResults/` is gitignored, so on the MacBook every EditMode run leaves the report modified with
NOT RUN lines.

**Why:** the Windows x64 release build cannot be produced on the Mac; before the change the audit test failed on every
non-Windows clone (813/815) although all real tests were green.

**How to apply:** after running tests on macOS, restore the report before committing
(`git checkout -- production/FINAL_MVP_COMPLETION_REPORT.md`); only commit a report produced on Windows with the
build artifacts present. See [[memory-lives-in-repo]] for the two-machine workflow.
