# Dungeon Generator

> **Status:** Approved design specification unless explicitly marked as tunable.
> **Game language:** English. Planning discussions may be in German, but all player-facing text, code naming, comments, and Claude implementation specs should be English.
## Structure

Each depth generates one dungeon in one biome from hand-authored room pools.

### Room Count
Initial target:
- Depth 1–3: 9–10 rooms.
- Depth 4–8: 10–11 rooms.
- Depth 9+: 10–13 rooms.
- Hard max target: ~13; later difficulty should not come from 30-room floors.

### Main Path
The main path connects Start to Boss. Initial target length: 6–9 rooms including Start/Boss.

### Branches
- At least 1 branch.
- Usually max 3 branches.
- Branch length typically 1–3 rooms.
- Optional content such as merchant, loot, event, treasure, recovery/medical, or elite encounters is preferentially placed on branches.

### Required Rules
- Exactly 1 Start Room.
- Exactly 1 Boss Room.
- Boss at end of main path.
- Boss not adjacent to Start.
- Merchant not directly adjacent to Start or Boss.
- Elite encounter not directly after Start or directly before Boss.
- Every room reachable.
- No overlapping rooms.
- Compatible door sockets only.

## Duplicate Rooms
Prefer not to use the same room prefab twice in one dungeon. Repetition is allowed only when the room pool cannot satisfy the requested graph otherwise.

## Generation Failure
If validation fails, discard the layout and regenerate. Do not patch broken layouts with ad-hoc runtime hacks.
