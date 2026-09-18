# TASK 184 — Final Release Completion Audit

> **Status:** Terminal RUINRAIL V1 release task.
> **Execution mode:** Review-gated terminal audit.
> **Game/code language:** English.

## PREREQUISITES

- TASK 183 PASS.
- All prior human/service/hardware gates approved with evidence.

## GOAL

Perform a fresh terminal release audit and declare RUINRAIL V1 complete only when engineering, production content, live services, design sign-off, human playtest and release-build evidence all agree.

## REQUIREMENTS

1. Re-run complete EditMode and PlayMode suites; record discovered/passed/failed/skipped.
2. Re-run content-count, production, presentation, animation, asset/audio and release validators.
3. Verify approved gameplay content counts remain exact.
4. Verify final asset manifest has zero mandatory `MISSING`/`PLACEHOLDER` roles: characters/animations, 33 weapons, three biomes, world objects, VFX, UI/icons/glyphs/font, 53-current SFX roles, 11 music, 6 stingers, 3 ambience, subject to TASK149 current-count reconciliation.
5. Verify all release-owned `PROTOTYPE` markers were dispositioned by TASK179 and zero unresolved design-placeholder marker remains.
6. Verify project-owned deprecated Physics2D NonAlloc usage is gone and release warnings are classified.
7. Verify loading/transition/error presentation exists in the final build.
8. Verify TASK181 live Sessions/Relay evidence applies to the same candidate code/config.
9. Verify TASK182 human playtest/balance sign-off and TASK183 target-hardware profile evidence.
10. Build a fresh Windows x64 non-development release, run built-player smoke and inspect logs for exceptions/missing refs.
11. Re-audit save/item/coin/XP transaction integrity, host authority and deterministic generation via existing hardening suites.
12. State exact supported V1 platform(s); do not infer support for platforms not built/tested.
13. Write `production/FINAL_RELEASE_COMPLETION_REPORT.md` with terminal status exactly one of: `RELEASE_COMPLETE`, `INCOMPLETE`, `BLOCKED_EXTERNAL_DEPENDENCY`.
14. Do not create TASK185 automatically.

## ACCEPTANCE CRITERIA

- All mandatory fresh checks PASS.
- Zero mandatory final asset placeholder/missing roles.
- Live multiplayer actually verified.
- Human playtest and target-device profile approved.
- Zero unresolved prototype/design placeholder and zero unresolved release-blocking warning/error.
- Fresh release build/smoke/log audit PASS.
- Final report is reproducible and truthful.
