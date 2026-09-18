# TASK 149 — Post-TASK-148 Completion Gap Audit

> **Status:** Approved RUINRAIL completion/release task.
> **Execution mode:** Review-gated completion runner or supervised single-task execution.
> **Game/code language:** English.

## PREREQUISITES

- TASK 148 must be complete, or the runner must have an explicit approved gate record allowing continuation.
- Read `production/131_CURRENT_PRODUCTION_STATE_AFTER_TASK_148.md` and preserve the proven TASK-001–148 engineering state.
- If a required prior human/external gate is not approved, stop. Do not self-approve it.

## GOAL

Audit the actual final repository/build after TASK 148 and convert every remaining presentation, live-service, profiling and human-validation blocker into a machine-readable/checkable completion manifest. Do not implement bulk assets yet.

## FILES TO READ

Read this task, `CLAUDE.md`, `technical/117_CODING_RULES_FOR_CLAUDE.md`, and only the following direct specifications before editing:

- `production/131_CURRENT_PRODUCTION_STATE_AFTER_TASK_148.md`
- `production/132_COMPLETION_ROADMAP_TASK149_184.md`
- `production/134_FINAL_PRODUCTION_ASSET_MANIFEST_RULES.md`
- `production/FINAL_MVP_COMPLETION_REPORT.md if present`
- `production/135_TASK148_GAP_ANALYSIS_ADDENDUM.md`
- `art/103_ANIMATION_RULES.md`
- `art/105_AUDIO_MUSIC.md`

Read one additional file only when a concrete direct dependency requires it, and record why in the completion report. Do not bulk-read/rewrite the GDD.

## ASSUMED EXISTING STATE

- TASK 148 proved the engineering MVP with full local automated coverage/build smoke and left presentation/live-service/human-validation blockers.
- Existing IDs, ScriptableObject definitions, scene flow, networking authority, save/transaction semantics, room logic and combat balance are regression contracts.
- Reuse the existing animation/VFX/audio/network/UI foundations from TASK 131–148 rather than creating duplicates.
- Use `art/106_RUINRAIL_FINAL_ART_BIBLE.md` as the final production visual reference.

## REQUIREMENTS

1. Inspect current validators, ScriptableObjects, prefabs, scenes and production assets rather than trusting the old report blindly.
2. Generate/update a checked-in final asset manifest enumerating exact current roles and statuses.
3. Identify every placeholder or missing sprite, tile, prop, UI, font, animation, VFX and audio role.
4. Confirm whether the TASK-148 counts (22 animation sets × 48 clip roles, 33 weapon sprites, 53 audio-event roles, 11 music, 6 stingers, 3 ambience) still match current code; report deltas explicitly.
5. List live UGS blockers, Network Player Prefab status, live test prerequisites and hardware profiling targets.
6. Enumerate all current `PROTOTYPE` markers from source/assets and reconcile the count against the TASK-148 baseline of 35; classify each by owner/system without changing its value.
7. Locate every deprecated `Physics2D.*NonAlloc` use and reconcile against the TASK-148 baseline of 12 calls / 7 files.
8. Confirm whether loading/transition/error presentation exists, whether release boot still wires fake multiplayer adapters, and whether a final network player prefab is registered.
9. Record intended release platform scope; do not imply unbuilt platforms are supported.
10. Do not change gameplay balance or content definitions in this audit.

## DO NOT IMPLEMENT

- Do not add new gameplay systems, weapon classes, enemies, Bosses, currencies, crafting, PvP, classes, stamina, durability, Gear Score, critical hits, weak spots or ammo categories.
- Do not replace proven TASK-001–148 architecture with parallel systems merely to integrate content.
- Do not silently rebalance approved values outside TASK 179.
- Do not rip, trace, recolor or ship copyrighted assets from reference games.
- Do not mark placeholder/fallback/silent content as final production content.
- Do not weaken tests/validators to make a missing asset or service check pass.
- Do not commit credentials, secrets or service tokens.

## ACCEPTANCE CRITERIA

1. A reviewer can identify every remaining release blocker from one manifest/report.
2. No known placeholder/missing final content role is silently omitted.
3. Counts are generated/verified from the current repository where possible.
4. All existing regression suites remain green.
5. The manifest/report explicitly covers art, audio, live multiplayer, deprecated API cleanup, prototype-value sign-off, loading/error presentation, player-build profiling and human validation.

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
In single-task mode, stop before TASK 150. Under the review-gated runner, continue only when the runner and any human/external gate permit it.
