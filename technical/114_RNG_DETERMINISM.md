# RNG and Determinism

> **Status:** Approved design specification unless explicitly marked as tunable.
> **Game language:** English. Planning discussions may be in German, but all player-facing text, code naming, comments, and Claude implementation specs should be English.
Each expedition has a Run Seed; each depth derives deterministic/random sub-seeds/streams for dungeon layout and relevant generated content.

Prefer a project-owned seeded RNG abstraction instead of uncontrolled `UnityEngine.Random` calls throughout gameplay code.

Suggested logical streams:
- Dungeon RNG.
- Encounter RNG.
- Loot RNG.

Seeded generation improves co-op authority and debugging. A reported `RunSeed + Depth` should allow developers to reproduce a layout/roll path where practical.

The host owns authoritative random generation in co-op.
