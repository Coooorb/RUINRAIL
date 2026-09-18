# Room Authoring

> **Status:** Approved V1 design specification.
> **Game language:** English.

Rooms are hand-authored on the grid and later assembled procedurally. Runtime does **not** generate walls tile-by-tile.

## V1 Room Size Classes

- Small: **16×12 tiles**.
- Medium: **24×16 tiles**.
- Large: **32×20 tiles**.
- Boss: **36×24 tiles**.

Room Prefabs declare tile width/height and stay inside those bounds. Do not solve asset/layout mismatches with arbitrary Transform scaling.

## Room Prefab Structure

Suggested root:
- Grid / Tilemaps.
- DoorSockets.
- EnemySpawnPoints.
- LootSpawnPoints.
- EventSpawnPoints.
- PlayerSpawnPoints as appropriate.
- Boss/Merchant-specific markers where applicable.
- RoomController / metadata reference.

## Door Sockets

Connections are discrete North/South/East/West sockets on exact grid coordinates. Dungeon assembly connects compatible sockets; it never approximates doorway alignment.

## Logic Markers

Use authoring markers for Walkable, Blocked, EnemySpawn, PlayerSpawn, LootSpawn, ChestSpawn, TrapSpawn, DoorSocket, InteractableSpawn, Reserved. The generator decides what spawns at legal markers; it does not invent arbitrary valid positions.

## Final MVP Room Count

Exact production counts are defined in `production/126_FINAL_MVP_CONTENT_COUNTS.md`.
