# Grid and Tile System

> **Status:** Approved design specification unless explicitly marked as tunable.
> **Game language:** English. Planning discussions may be in German, but all player-facing text, code naming, comments, and Claude implementation specs should be English.

## Non-Negotiable Rules

- No arbitrary environment Transform placement as a substitute for grid/tile authoring.
- Do not disable pixel/grid rules to “fix” alignment.
## Hard Rule

Environment placement is grid-based. Claude must not freehand walls, doors, room boundaries, or gameplay props at arbitrary transform positions.

## Base Grid

- Tile size: 32×32 pixels.
- 1 tile = 1×1 logical grid unit.
- Pixels Per Unit target: 32.
- Gameplay sprites should normally use Transform Scale `(1,1,1)`.

## Tilemap Layers

Recommended fixed layers:
- Floor.
- Floor Detail.
- Walls.
- Obstacles.
- Hazards.
- Above Player.
- Logic/Navigation (invisible or authoring-only).

## Collision Semantics

- Floor: no collision.
- Walls: collision.
- Obstacles: collision.
- Hazards: trigger/damage logic.
- Decoration: no gameplay collision unless explicitly authored.

## Props

Gameplay props reserve integer tile footprints, e.g. 1×1 barrel, 2×2 crate, 2×3 machine. Placement tooling must prevent overlap with reserved/blocked tiles.
