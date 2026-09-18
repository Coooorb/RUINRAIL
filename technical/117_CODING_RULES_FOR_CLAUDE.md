# Coding Rules for Claude

> **Status:** Approved design specification unless explicitly marked as tunable.
> **Game language:** English. Planning discussions may be in German, but all player-facing text, code naming, comments, and Claude implementation specs should be English.
## Design Discipline

1. Do not invent new game systems.
2. Do not change approved game rules without an explicit design update.
3. If the task conflicts with an approved spec, report the conflict and stop that conflicting portion.
4. Do not add crits, weak spots, stamina, durability, crafting materials, extra currencies, classes, character abilities, PvP, or other excluded systems.

## Architecture Discipline

5. No monolithic `GameManager` or `PlayerController` containing unrelated responsibilities.
6. Do not create a second implementation of an existing system.
7. Do not silently refactor unrelated working systems while completing a narrow task.
8. Keep static definitions, runtime state, and save data separate.
9. Use stable definition IDs; do not save asset paths/display names as identity.
10. Do not hardcode balance values that belong in definitions/configs.
11. Use the approved grid/tile/room system; do not freehand dungeon geometry.
12. Networking uses host authority for shared gameplay state.
13. Follow `ENVIRONMENT.md`. Never silently change the Unity Editor or package versions to make an implementation easier.

## Task Discipline

14. Read only the files listed by the task plus direct dependencies if needed.
15. Implement only the requested scope.
16. Respect every `DO NOT IMPLEMENT` section in task files.
17. At completion, report changed files, exact test commands performed, exact results, known issues, and any spec ambiguity/conflict.
18. Do not claim a test passed unless it was actually run. `NOT RUN` is valid; an invented PASS is not.
19. When tests are relevant, use the approved repo harness in `scripts/run-unity-tests.ps1` or `scripts/run-unity-tests.sh` unless the task explicitly specifies another command.
20. Zero discovered tests are a test failure, not a pass. Do not treat Unity exit code 0 alone as proof that tests ran.
21. Prefer small composable behaviours over giant type-switch/if chains.

## Simplicity

22. Do not build generalized frameworks “just in case” when the approved feature set is small.
23. Solve the current approved requirement cleanly and extensibly enough for known planned content.
