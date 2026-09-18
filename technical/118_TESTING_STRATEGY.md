# Testing Strategy

> **Status:** Approved design specification unless explicitly marked as tunable.
> **Game language:** English. Planning discussions may be in German, but all player-facing text, code naming, comments, and Claude implementation specs should be English.
Prioritize automated tests for rule-heavy deterministic logic, not every visual animation.

## Canonical Commands

Use the repo harness rather than inventing ad-hoc Unity command lines:

```powershell
./scripts/run-unity-tests.ps1 -TestPlatform EditMode
./scripts/run-unity-tests.ps1 -TestPlatform PlayMode
./scripts/run-unity-tests.ps1 -TestPlatform All
```

```bash
./scripts/run-unity-tests.sh EditMode
./scripts/run-unity-tests.sh PlayMode
./scripts/run-unity-tests.sh All
```

The scripts call Unity in batch mode with `-runTests`, write XML/log output to `TestResults/`, and propagate failures through a non-zero exit code. The Unity executable/version is governed by `ENVIRONMENT.md`.

A suite is only a **PASS** when all of the following are true:

- Unity exits successfully.
- The result XML exists and can be read.
- At least one test is discovered (`total > 0`).
- At least one test passes (`passed > 0`).
- No test failures are reported.

A Unity exit code of 0 with `total="0"` is **FAIL**, not PASS. This normally indicates missing test assembly references or failed test discovery.

`All` attempts both EditMode and PlayMode even if one suite fails, then returns a failing exit code if either suite failed.

If Unity is unavailable, the completion report must say the relevant test was **NOT RUN** and give the reason. Never convert an unavailable harness into a claimed pass.

## Required / Valuable Automated Tests

- Affix roll always integer and within inclusive range.
- Rarity generates correct affix count.
- Item cannot exist in two inventory/storage locations simultaneously.
- Full backpack rejects normal pickup safely.
- Consumable/ammo stack logic respects limits.
- Dungeon always contains reachable Start/Boss and no disconnected rooms.
- Dungeon rejects overlap/socket mismatch.
- Skill attributes cannot exceed 10 points.
- XP remains after expedition failure.
- Failed expedition destroys at-risk inventory.
- Successful extraction secures loot and banks carried coins.
- Solo Defibrillator drop filtering.
- Co-op transit requires unanimous living-player Continue.

## Test Type Guidance

- Prefer **EditMode** tests for deterministic data/rule logic: affixes, inventory transfer, loot tables, graph validation, scaling formulas, save serialization.
- Use **PlayMode** tests when GameObjects, scenes, MonoBehaviour lifecycle, physics, room activation, or runtime interaction is required.
- Use Multiplayer Play Mode/editor tooling for repeatable 2–3 player scenarios once networking enters scope.
- Do not write brittle frame-timing tests when a deterministic lower-level test can prove the rule.

## Task Acceptance

A task is not automatically blocked because Unity cannot run on the current machine. It may be completed with tests marked `NOT RUN`, but the report must distinguish:

1. tests written,
2. tests actually executed,
3. exact results, and
4. any manual verification still required.

## Headless Behavior

- **EditMode:** the harness adds Unity `-nographics` for CI/SSH/headless reliability.
- **PlayMode:** the harness intentionally omits `-nographics` because PlayMode tests may depend on a graphics device or render loop.
- `All` runs both suites with their respective behavior and aggregates the result.
