# Dungeon and Room Validation

> **Status:** Approved design specification unless explicitly marked as tunable.
> **Game language:** English. Planning discussions may be in German, but all player-facing text, code naming, comments, and Claude implementation specs should be English.
## Room Validation Tool

Provide an editor action such as `Validate Room` that checks:
- Valid grid dimensions.
- Required sockets/markers.
- Door positions legal and reachable.
- No enemy spawn inside walls/blocked cells.
- No overlapping reserved props.
- Boss room contains BossSpawn.
- Merchant room contains MerchantSpawn.
- Required player/start markers.

Error output should include the room and grid coordinate when possible.

## Generated Dungeon Validation

Before gameplay begins:
- Start exists.
- Boss exists.
- Boss reachable from Start.
- Every generated room reachable.
- No disconnected branches.
- No room overlap.
- Door sockets match.
- Main path satisfies min length.
- Placement rules satisfied.

Invalid generation is discarded and rerolled from the deterministic/random generation service.
