# Editor Validation Tools

> **Status:** Approved design specification unless explicitly marked as tunable.
> **Game language:** English. Planning discussions may be in German, but all player-facing text, code naming, comments, and Claude implementation specs should be English.
Build small editor tools early.

## Room Validator
Checks grid dimensions, door sockets, reachability, spawn markers, blocked/overlapping positions, room-type requirements.

## Item Validator
Checks stable/duplicate IDs, missing icon/prefab/definition data, invalid affix ranges, legendary definition missing required special/passive.

## Loot Table Validator
Checks invalid/negative weights, missing references, impossible empty tables.

## Enemy/Boss Validator
Checks stable ID, missing prefab, invalid base stats/attack references.

Errors should be actionable and point to exact assets/coordinates where possible.
