# TASK 150 — Final Art Bible and Reference Boundaries

> **Status:** Approved RUINRAIL completion/release task.
> **Execution mode:** Review-gated completion runner or supervised single-task execution.
> **Game/code language:** English.

## PREREQUISITES

- TASK 149 must be complete, or the runner must have an explicit approved gate record allowing continuation.
- Read `production/131_CURRENT_PRODUCTION_STATE_AFTER_TASK_148.md` and preserve the proven TASK-001–148 engineering state.
- If a required prior human/external gate is not approved, stop. Do not self-approve it.

## GOAL

Adopt the approved RUINRAIL final visual language as a repository production contract without changing gameplay.

## FILES TO READ

Read this task, `CLAUDE.md`, `technical/117_CODING_RULES_FOR_CLAUDE.md`, and only the following direct specifications before editing:

- `art/100_ART_DIRECTION.md`
- `art/101_PIXEL_GRID_AND_SCALE.md`
- `art/102_CAMERA_SORTING_LIGHTING.md`
- `art/103_ANIMATION_RULES.md`
- `art/104_VFX_GAME_FEEL.md`
- `art/106_RUINRAIL_FINAL_ART_BIBLE.md`
- `production/134_FINAL_PRODUCTION_ASSET_MANIFEST_RULES.md`

Read one additional file only when a concrete direct dependency requires it, and record why in the completion report. Do not bulk-read/rewrite the GDD.

## ASSUMED EXISTING STATE

- TASK 148 proved the engineering MVP with full local automated coverage/build smoke and left presentation/live-service/human-validation blockers.
- Existing IDs, ScriptableObject definitions, scene flow, networking authority, save/transaction semantics, room logic and combat balance are regression contracts.
- Reuse the existing animation/VFX/audio/network/UI foundations from TASK 131–148 rather than creating duplicates.
- Use `art/106_RUINRAIL_FINAL_ART_BIBLE.md` as the final production visual reference.

## REQUIREMENTS

1. Reconcile existing art documentation with `art/106_RUINRAIL_FINAL_ART_BIBLE.md`; if there is a contradiction, preserve approved numeric/technical rules and report the conflict.
2. Document practical import/preview examples for player, enemy, weapon, environment and UI categories without copying reference-game assets.
3. Record the reference hierarchy: Soul Knight for sprite construction/readability; Zero Sievert for gritty pixel-world character; Fallout 4 strongly for atmosphere; ARC Raiders moderately for industrial sci-fi accent.
4. Add explicit originality/reference-boundary checks to the production asset review workflow.
5. Do not generate or bulk-integrate final assets in this task.

## DO NOT IMPLEMENT

- Do not add new gameplay systems, weapon classes, enemies, Bosses, currencies, crafting, PvP, classes, stamina, durability, Gear Score, critical hits, weak spots or ammo categories.
- Do not replace proven TASK-001–148 architecture with parallel systems merely to integrate content.
- Do not silently rebalance approved values outside TASK 179.
- Do not rip, trace, recolor or ship copyrighted assets from reference games.
- Do not mark placeholder/fallback/silent content as final production content.
- Do not weaken tests/validators to make a missing asset or service check pass.
- Do not commit credentials, secrets or service tokens.

## ACCEPTANCE CRITERIA

1. Art direction is unambiguous enough that separate asset batches can be reviewed against the same rules.
2. Existing grid/scale/camera/animation contracts remain unchanged.
3. Originality boundaries are explicit.
4. No gameplay code change is required unless a validator/document path needs a narrow update.

## TESTS / VALIDATION

- Unity must compile after any repository change.
- Add focused automated/editor validation only where it protects the production integration introduced by this task; do not add meaningless count-only tests when an existing validator already proves the same thing.
- Run the repository harness after implementation when code/assets changed:
  `UNITY_PATH="/c/Program Files/Unity/Hub/Editor/6000.3.24f1/Editor/Unity.exe" bash scripts/run-unity-tests.sh All`
- A zero-test run is a failure. Record exact discovered/passed/failed/skipped counts.
- Run the task-specific production/presentation/asset/audio/build/live-service checks described above in addition to the harness.
- If a human, account, service, hardware or external asset prerequisite prevents a mandatory check, report the exact check as `NOT RUN`/`BLOCKED_EXTERNAL_*`; never fabricate PASS.

## EXPECTED FILES CHANGED

Keep changes restricted to the current task's production assets, import metadata, narrow integration code/editor validation, tests and completion report. Follow the existing repository folder structure rather than creating a parallel architecture.

## COMPLETION REPORT

At task end report:
1. Files/assets created, supplied, imported or changed.
2. Manifest roles moved between `MISSING` / `PLACEHOLDER` / `CANDIDATE` / `APPROVED` / `INTEGRATED`.
3. What was integrated and how existing TASK-001–148 systems were preserved.
4. Exact test/validator/build/service commands or actions and exact results.
5. Acceptance criteria item-by-item status.
6. Human/external approvals still required.
7. Known limitations or blockers.

Append the checkpoint to `production/COMPLETION_RUN_LOG.md`.
In single-task mode, stop before TASK 151. Under the review-gated runner, continue only when the runner and any human/external gate permit it.
