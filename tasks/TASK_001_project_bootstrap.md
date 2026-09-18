# TASK 001 — Project Bootstrap

> **Status:** Approved design specification unless explicitly marked as tunable.
> **Game language:** English. Planning discussions may be in German, but all player-facing text, code naming, comments, and Claude implementation specs should be English.
## GOAL

Create the initial Unity project folder/assembly/scene skeleton without gameplay systems.

## FILES TO READ

- `ENVIRONMENT.md`
- `technical/110_UNITY_PROJECT_STRUCTURE.md`
- `technical/117_CODING_RULES_FOR_CLAUDE.md`

## REQUIREMENTS

Create the approved folders, Bootstrap/MainMenu/Base/Dungeon scene placeholders, practical assembly definitions, and a minimal AppRoot/bootstrap path. Pin the direct packages from `ENVIRONMENT.md` in `Packages/manifest.json` without silently substituting other versions, then verify that Unity Package Manager actually resolves those exact pins in the real project. Create a minimal PlayMode bootstrap smoke test proving Bootstrap reaches MainMenu.

## DO NOT IMPLEMENT

No player movement, inventory, dungeon generator, networking, or content systems.

## ACCEPTANCE CRITERIA

Unity Package Manager resolves the exact approved direct pins without silent substitution; project enters MainMenu from Bootstrap without errors; assemblies compile; the PlayMode smoke test is actually discovered and passes; no monolithic manager is introduced.

## TESTS

Run `./scripts/run-unity-tests.ps1 -TestPlatform EditMode` and `./scripts/run-unity-tests.ps1 -TestPlatform PlayMode` on Windows, or the equivalent Bash harness. A zero-test result is a failure and must be fixed before claiming PASS. If Unity is unavailable, report both as `NOT RUN` with the exact reason.

## EXPECTED FILES CHANGED

Only files directly required by this task and its tests.
