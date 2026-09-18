# External Review Patch — 2026-09-14

This maintenance patch addresses workflow gaps identified during an external review. It does not change RUINRAIL game design.

## Added
- Root `CLAUDE.md` for Claude Code automatic project memory/instructions.
- Root `ENVIRONMENT.md` with a pinned Unity 6.3 LTS editor and direct package baseline.
- PowerShell and Bash Unity Test Framework batchmode harness scripts.
- Model-routing guidance that is intentionally separate from game design.

## Updated
- Claude start protocol now checks environment pins.
- Coding rules forbid silent toolchain/package upgrades.
- Testing strategy provides executable commands and explicit `NOT RUN` semantics.
- Task protocol and prompt template are Claude-Code-aware and model-neutral.
- TASK 001 now pins packages and adds a bootstrap PlayMode smoke test.

## Design Impact
None. Core game rules, balance baselines, content catalogs, multiplayer rules, and scope are unchanged.

## Follow-up Harness Patch

A second external review found a real false-positive case: Unity can exit successfully while discovering zero tests. The test harness has been hardened accordingly.

Changes:

- Both Bash and PowerShell harnesses now fail when `total=0`.
- Both fail when tests are discovered but zero tests pass.
- Both inspect reported failure counts in addition to Unity's process exit code.
- Bash now captures Unity's exit code and prints an explicit diagnostic instead of relying on `set -e`.
- `All` now attempts both EditMode and PlayMode and aggregates the final result instead of fail-fast behavior.
- TASK 001 explicitly requires the PlayMode smoke test to be discovered, not merely written.
- Each of the seven proposed direct package version numbers was confirmed in Unity's official Unity 6000.0 package documentation on 2026-09-14 as released/available; actual project-level Package Manager resolution of the exact combination under Unity `6000.3.24f1` was **NOT RUN** in the documentation workspace.

Validation performed in the documentation workspace:

- Bash syntax check: PASS.
- Bash harness simulated with a fake Unity executable where EditMode reports zero tests and PlayMode reports one passing test: PASS (the harness correctly ran both suites and returned overall failure).
- Real Unity tests: NOT RUN — Unity Editor is not installed in this environment.
- PowerShell harness execution: NOT RUN — `pwsh` is not installed in this environment.

## Third Review Patch — Verification Semantics and Headless EditMode

A third external review identified two workflow/documentation issues.

Changes:

- EditMode test runs now add Unity `-nographics` for CI/SSH/headless reliability.
- PlayMode intentionally omits `-nographics`.
- The Bash XML parser now normalizes line breaks before reading `<test-run>` attributes.
- `ENVIRONMENT.md` no longer implies that Package Manager resolution occurred in the documentation workspace. It distinguishes a documentation/version cross-check from actual project resolution.
- TASK 001 now explicitly requires Unity Package Manager to resolve the exact approved pins in the real Unity project before those pins may be treated as operationally verified.

Verification performed in the documentation workspace:

- Bash syntax check: PASS.
- Bash fake-Unity regression suite: PASS for zero-tests/failures/stale-XML/process-error behavior and EditMode-only `-nographics` argument behavior.
- Real Unity Package Manager resolution: **NOT RUN — Unity Editor is not installed in this environment.**
- Real Unity tests: **NOT RUN — Unity Editor is not installed in this environment.**
- PowerShell harness execution: **NOT RUN — `pwsh` is not installed in this environment.**
