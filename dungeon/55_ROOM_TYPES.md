# Room Types

> **Status:** Approved design specification unless explicitly marked as tunable.
> **Game language:** English. Planning discussions may be in German, but all player-facing text, code naming, comments, and Claude implementation specs should be English.
Approved room/encounter categories:

- **Start Room:** exactly one, safe spawn, no immediate enemies.
- **Combat Room:** doors lock, encounter runs, doors unlock when enemies are defeated.
- **Loot Room:** guaranteed loot, little or no combat.
- **Treasure Room:** rarer, better loot table.
- **Merchant Room:** safe merchant.
- **Event Room:** one approved event.
- **Medical/Recovery Event Room:** represented through the Medical Station event rather than a separate complex subsystem.
- **Elite Encounter:** a compatible combat room running one elite mini-boss setup; “Elite” is not a generic room architecture.
- **Boss Room:** exactly one, end of main path, activates transit after boss defeat.

Initial per-dungeon target ranges:
- Combat: 4–8.
- Merchant: 0–1.
- Event: 0–2.
- Loot: 0–2.
- Treasure: 0–1.
- Medical/Recovery: 0–1.
- Elite: 0–1 early, up to 0–2 later.
