# Room Metadata and Tags

> **Status:** Approved design specification unless explicitly marked as tunable.
> **Game language:** English. Planning discussions may be in German, but all player-facing text, code naming, comments, and Claude implementation specs should be English.
Every room definition should expose stable metadata such as:
- Stable ID.
- Biome.
- Room Type.
- Size class / tile dimensions.
- Difficulty rating/weight.
- MinDepth.
- MaxDepth or unlimited.
- Supported door directions/sockets.
- SupportsElite flag.
- Selection weight.
- References to prefab/validation data.

Example concept:
```text
ID: rustworks_combat_medium_04
Biome: Rustworks
RoomType: Combat
Size: Medium
Difficulty: 2
MinDepth: 1
SupportsElite: true
Weight: 1.0
```

Room geometry and encounter contents are separate. A room must not always contain the same enemy composition.
