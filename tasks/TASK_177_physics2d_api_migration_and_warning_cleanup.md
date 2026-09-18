# TASK 177 — Physics2D API Migration and Release-Warning Cleanup

> **Status:** Approved RUINRAIL completion/release task.
> **Execution mode:** Review-gated completion runner or supervised single-task execution.
> **Game/code language:** English.

## PREREQUISITES

- TASK 176 must be complete and its human presentation/audio gate approved.
- Read the TASK 149 gap report and `production/135_TASK148_GAP_ANALYSIS_ADDENDUM.md`.

## GOAL

Remove the known deprecated Physics2D API usage and any directly related release warnings without changing gameplay behavior.

## REQUIREMENTS

1. Re-scan the repository for obsolete/deprecated Physics2D calls; reconcile against the TASK-148 baseline of 12 `Physics2D.*NonAlloc` calls across 7 files.
2. Migrate each call to the supported Unity 6.3-compatible `ContactFilter2D`/current overload or equivalent supported API.
3. Preserve layer masks, trigger behavior, hit ordering assumptions, buffer semantics and allocation characteristics where gameplay depends on them.
4. Add/update focused regression tests for any migrated combat, pickup, melee, hazard or overlap behavior that was not already adequately covered.
5. Produce a fresh non-development Windows build and record warnings. Any remaining warning must be classified as owned/fixable, external/package, or accepted with reason.
6. Do not bundle unrelated refactors.

## DO NOT IMPLEMENT

- No new gameplay systems/content.
- No balance changes.
- No architectural rewrite of proven collision/combat systems.

## ACCEPTANCE CRITERIA

1. Zero project-owned deprecated Physics2D NonAlloc calls remain.
2. Existing collision/combat semantics remain green under targeted and full tests.
3. Fresh release build succeeds.
4. Project-owned CS0618 warnings addressed by this task are gone.

## TESTS / VALIDATION

Run targeted affected tests, then:
`UNITY_PATH="/c/Program Files/Unity/Hub/Editor/6000.3.24f1/Editor/Unity.exe" bash scripts/run-unity-tests.sh All`

Append exact before/after scan counts, tests and build warnings to `production/COMPLETION_RUN_LOG.md`.
