# Ruined Metro room-set validation summary

## Pool

| ID | Type | Size | Doors | Elite | Weight |
|---|---|---|---|---|---:|
| metro_boss_01 | Boss | Boss | South | False | 1,0 |
| metro_boss_02 | Boss | Boss | West | False | 1,0 |
| metro_combat_large_01 | Combat | Large | North/South/East/West | True | 1,0 |
| metro_combat_large_02 | Combat | Large | North/East/West | True | 1,0 |
| metro_combat_medium_01 | Combat | Medium | North/South/East/West | True | 1,0 |
| metro_combat_medium_02 | Combat | Medium | North/South/East/West | False | 1,0 |
| metro_combat_medium_03 | Combat | Medium | North/South/East/West | False | 1,0 |
| metro_combat_medium_04 | Combat | Medium | North/South/East/West | True | 1,0 |
| metro_combat_small_01 | Combat | Small | North/South/West | False | 1,0 |
| metro_combat_small_02 | Combat | Small | North/South/East/West | False | 1,0 |
| metro_combat_small_03 | Combat | Small | North/East/West | False | 1,0 |
| metro_combat_small_04 | Combat | Small | South/East/West | False | 1,0 |
| metro_combat_small_05 | Combat | Small | North/South/East/West | False | 1,0 |
| metro_event_01 | Event | Small | North/East/West | False | 1,0 |
| metro_event_02 | Event | Small | North/South/East/West | False | 1,0 |
| metro_loot_01 | Loot | Small | North/South/East/West | False | 1,0 |
| metro_medical_01 | MedicalRecovery | Small | North/South/East/West | False | 1,0 |
| metro_merchant_01 | Merchant | Small | North/South/East/West | False | 1,0 |
| metro_start_01 | Start | Small | North/South/East/West | False | 1,0 |
| metro_start_02 | Start | Small | North/South/East/West | False | 1,0 |
| metro_treasure_01 | Treasure | Small | North/South/East | False | 0,5 |

## Seeded generation (graph -> assembly -> validation, 53 discard-and-regenerate on failure)

- Seeds: 40 x depths 1,2,5,10,25 = 200 dungeons generated and layout-validated: PASS
- Regenerated rounds: 1 of 200 (seed 14 depth 25: 2 rounds)
- Largest dungeon: 13 rooms; rooms reused within one dungeon: max 2, average 0,32
- Boss arenas only on the Boss node, Start rooms only on the Start node, utility rooms only in their category slots: PASS

Result: PASS (21 rooms, 0 rejected)
