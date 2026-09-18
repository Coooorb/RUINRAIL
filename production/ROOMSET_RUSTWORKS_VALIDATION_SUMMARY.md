# Rustworks room-set validation summary

## Pool

| ID | Type | Size | Doors | Elite | Weight |
|---|---|---|---|---|---:|
| rust_boss_01 | Boss | Boss | South | False | 1,0 |
| rust_boss_02 | Boss | Boss | West | False | 1,0 |
| rust_combat_large_01 | Combat | Large | North/South/East/West | True | 1,0 |
| rust_combat_large_02 | Combat | Large | North/East/West | True | 1,0 |
| rust_combat_medium_01 | Combat | Medium | North/South/East/West | True | 1,0 |
| rust_combat_medium_02 | Combat | Medium | North/South/East/West | False | 1,0 |
| rust_combat_medium_03 | Combat | Medium | North/South/East/West | False | 1,0 |
| rust_combat_medium_04 | Combat | Medium | North/South/East/West | True | 1,0 |
| rust_combat_small_01 | Combat | Small | North/South/East/West | False | 1,0 |
| rust_combat_small_02 | Combat | Small | North/South/East/West | False | 1,0 |
| rust_combat_small_03 | Combat | Small | North/East/West | False | 1,0 |
| rust_combat_small_04 | Combat | Small | South/East/West | False | 1,0 |
| rust_combat_small_05 | Combat | Small | North/South/East/West | False | 1,0 |
| rust_event_01 | Event | Small | North/East/West | False | 1,0 |
| rust_event_02 | Event | Small | North/South/East/West | False | 1,0 |
| rust_loot_01 | Loot | Small | North/South/East/West | False | 1,0 |
| rust_medical_01 | MedicalRecovery | Small | North/South/East/West | False | 1,0 |
| rust_merchant_01 | Merchant | Small | North/South/East/West | False | 1,0 |
| rust_start_01 | Start | Small | North/South/East/West | False | 1,0 |
| rust_start_02 | Start | Small | North/South/East/West | False | 1,0 |
| rust_treasure_01 | Treasure | Small | North/South/East | False | 0,5 |

## Seeded generation (graph -> assembly -> validation, 53 discard-and-regenerate on failure)

- Seeds: 40 x depths 1,2,5,10,25 = 200 dungeons generated and layout-validated: PASS
- Regenerated rounds: 1 of 200 (seed 15 depth 5: 2 rounds)
- Largest dungeon: 13 rooms; rooms reused within one dungeon: max 2, average 0,28
- Boss arenas only on the Boss node, Start rooms only on the Start node, utility rooms only in their category slots: PASS

Result: PASS (21 rooms, 0 rejected)

## Visual placeholder debt (tracked separately from validation)

- Every Rustworks prefab uses the engineering placeholder tiles (Placeholder_Floor / Placeholder_FloorDetail / Placeholder_Wall / Placeholder_Obstacle / Placeholder_Hazard). Geometry, sockets, markers and hazards are final; the visual layer is `BLOCKED_EXTERNAL_ASSET`.
- Missing external art roles: Rustworks tileset (floor/wall/detail), machinery/press/pipe/conveyor props, furnace grate hazard art, explosive environmental props, boss arena dressing (foundry floor, scrap arena).
- Re-baking through `RuinRail/Rooms/Rebuild Rustworks Rooms` keeps asset GUIDs stable, so swapping the placeholder tiles for final art will not touch any reference.
