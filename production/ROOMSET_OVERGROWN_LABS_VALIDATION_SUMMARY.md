# Overgrown Labs room-set validation summary

## Pool

| ID | Type | Size | Doors | Elite | Weight |
|---|---|---|---|---|---:|
| labs_boss_01 | Boss | Boss | South | False | 1,0 |
| labs_boss_02 | Boss | Boss | West | False | 1,0 |
| labs_combat_large_01 | Combat | Large | North/South/East/West | True | 1,0 |
| labs_combat_large_02 | Combat | Large | North/East/West | True | 1,0 |
| labs_combat_medium_01 | Combat | Medium | North/South/East/West | True | 1,0 |
| labs_combat_medium_02 | Combat | Medium | North/South/East/West | False | 1,0 |
| labs_combat_medium_03 | Combat | Medium | North/South/East/West | False | 1,0 |
| labs_combat_medium_04 | Combat | Medium | North/South/East/West | True | 1,0 |
| labs_combat_small_01 | Combat | Small | North/South/East/West | False | 1,0 |
| labs_combat_small_02 | Combat | Small | North/South/East/West | False | 1,0 |
| labs_combat_small_03 | Combat | Small | North/East/West | False | 1,0 |
| labs_combat_small_04 | Combat | Small | South/East/West | False | 1,0 |
| labs_combat_small_05 | Combat | Small | North/South/East/West | False | 1,0 |
| labs_event_01 | Event | Small | North/East/West | False | 1,0 |
| labs_event_02 | Event | Small | North/South/East/West | False | 1,0 |
| labs_loot_01 | Loot | Small | North/South/East/West | False | 1,0 |
| labs_medical_01 | MedicalRecovery | Small | North/South/East/West | False | 1,0 |
| labs_merchant_01 | Merchant | Small | North/South/East/West | False | 1,0 |
| labs_start_01 | Start | Small | North/South/East/West | False | 1,0 |
| labs_start_02 | Start | Small | North/South/East/West | False | 1,0 |
| labs_treasure_01 | Treasure | Small | North/South/East | False | 0,5 |

## Seeded generation (graph -> assembly -> validation, 53 discard-and-regenerate on failure)

- Seeds: 40 x depths 1,2,5,10,25 = 200 dungeons generated and layout-validated: PASS
- Regenerated rounds: 0 of 200 ()
- Largest dungeon: 13 rooms; rooms reused within one dungeon: max 2, average 0,31
- Boss arenas only on the Boss node, Start rooms only on the Start node, utility rooms only in their category slots: PASS

Result: PASS (21 rooms, 0 rejected)

## Visual placeholder debt (tracked separately from validation)

- Every Overgrown Labs prefab uses the engineering placeholder tiles. Geometry, sockets, markers and hazards are final; the visual layer is `BLOCKED_EXTERNAL_ASSET`.
- Missing external art roles: Labs tileset (clean-tech floor/wall/glass), terminal / bio-tank / root / vine props, acid pool hazard art, failed-experiment dressing, boss arena dressing (biomass pit, security core).
- Re-baking through `RuinRail/Rooms/Rebuild Overgrown Labs Rooms` keeps asset GUIDs stable.
