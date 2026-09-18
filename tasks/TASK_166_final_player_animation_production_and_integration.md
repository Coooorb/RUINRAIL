# TASK 166 — Final Player Animation Production and Integration

> **Status:** Approved RUINRAIL completion/release task.
> **Execution mode:** Review-gated completion runner or supervised single-task execution.
> **Game/code language:** English.

## PREREQUISITES

- TASK 165 must be complete, or the runner must have an explicit approved gate record allowing continuation.
- Read `production/131_CURRENT_PRODUCTION_STATE_AFTER_TASK_148.md` and preserve the proven TASK-001–148 engineering state.
- If a required prior human/external gate is not approved, stop. Do not self-approve it.

## GOAL

Populate the existing player animation architecture with final approved animation content for every manifest-required player state/direction.

## FILES TO READ

Read this task, `CLAUDE.md`, `technical/117_CODING_RULES_FOR_CLAUDE.md`, and only the following direct specifications before editing:

- `art/103_ANIMATION_RULES.md`
- `art/106_RUINRAIL_FINAL_ART_BIBLE.md`
- production asset manifest
- `existing SpriteAnimation/PlayerAnimationDriver implementation`

Read one additional file only when a concrete direct dependency requires it, and record why in the completion report. Do not bulk-read/rewrite the GDD.

## ASSUMED EXISTING STATE

- TASK 148 proved the engineering MVP with full local automated coverage/build smoke and left presentation/live-service/human-validation blockers.
- Existing IDs, ScriptableObject definitions, scene flow, networking authority, save/transaction semantics, room logic and combat balance are regression contracts.
- Reuse the existing animation/VFX/audio/network/UI foundations from TASK 131–148 rather than creating duplicates.
- Use `art/106_RUINRAIL_FINAL_ART_BIBLE.md` as the final production visual reference.

## REQUIREMENTS

1. Use the existing animation state architecture; do not replace gameplay state ownership.
2. Produce/integrate all required player clip roles reported by the current AnimationAssetAudit using approved source sheets.
3. Keep approximately 8–12 FPS visual cadence and frame-rate-independent movement/combat.
4. Preserve 8-way body facing and independent 360° weapon aim.
5. Melee visible arcs/reach must remain close to actual hitboxes; firearm recoil/weapon visuals remain separate where designed.

## DO NOT IMPLEMENT

- Do not add new gameplay systems, weapon classes, enemies, Bosses, currencies, crafting, PvP, classes, stamina, durability, Gear Score, critical hits, weak spots or ammo categories.
- Do not replace proven TASK-001–148 architecture with parallel systems merely to integrate content.
- Do not silently rebalance approved values outside TASK 179.
- Do not rip, trace, recolor or ship copyrighted assets from reference games.
- Do not mark placeholder/fallback/silent content as final production content.
- Do not weaken tests/validators to make a missing asset or service check pass.
- Do not commit credentials, secrets or service tokens.

## ACCEPTANCE CRITERIA

1. Player animation audit has zero missing mandatory roles.
2. Damage/movement/revive timings remain authoritative and unchanged.
3. No visual state gets stuck under rapid weapon swap/dash/downed transitions.
4. Tests/validators pass.

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
In single-task mode, stop before TASK 167. Under the review-gated runner, continue only when the runner and any human/external gate permit it.
