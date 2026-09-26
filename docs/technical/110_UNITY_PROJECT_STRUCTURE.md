# Unity Project Structure

> **Status:** Approved design specification unless explicitly marked as tunable.
> **Game language:** English. Planning discussions may be in German, but all player-facing text, code naming, comments, and Claude implementation specs should be English.
Recommended structure under `Assets/Game/`:

```text
Art/
Audio/
Prefabs/
Scenes/
ScriptableObjects/
Scripts/
Settings/
Tests/
```

Scripts should be divided by responsibility: Core, Player, Combat, Items, Inventory, Enemies, Dungeon, Loot, Base, Progression, Multiplayer, Persistence, UI, Audio, Utilities.

Scenes:
- Bootstrap.
- MainMenu.
- Base.
- Dungeon.

Do not create a separate gameplay architecture/scene per biome; use one Dungeon scene/system with biome content sets.

## Assembly Definitions
Initial sensible split: Game.Core, Game.Gameplay, Game.Dungeon, Game.Persistence, Game.Networking, Game.UI, Game.Editor, Game.Tests. Keep the count practical.

## Repository-Root Operational Files

Keep these at repository root, outside `Assets/`:

- `CLAUDE.md` — Claude Code operating rules (automatic entry).
- `docs/` — design, technical contracts, current state, decisions, workflow (`docs/ENVIRONMENT.md` pins the toolchain).
- `production/` — current release status and validator baselines; `production/archive/` is history.
- `.claude/` — skills (tracked), memory (tracked), `work/` (temporary, gitignored).
- `scripts/` — command-line test harness and developer utilities.
- `TestResults/` — generated test output; normally gitignored.

Do not place these workflow files inside `Assets/`.
