# Item Data Model

> **Status:** Approved design specification unless explicitly marked as tunable.
> **Game language:** English. Planning discussions may be in German, but all player-facing text, code naming, comments, and Claude implementation specs should be English.
## Definition vs. Instance

Static item definitions and concrete item instances are separate.

### Definition
Examples: `weapon_ar17`, `armor_riot`, `accessory_runner_watch`, `consumable_medkit`.

Definitions contain static properties such as display name, base weapon stats, fixed accessory intrinsic, allowed affix pool, icon, prefab/sprite references, fixed consumable rarity, and legendary special/passive references.

### Equipment Instance
A concrete dropped/persisted weapon, armor, or accessory stores:
- Unique Item Instance ID (GUID or equivalent stable unique ID).
- Definition ID.
- Rarity.
- Affix IDs and integer roll values.

A legendary special/passive belongs to the definition, not the instance. Do not duplicate that data into every saved item.

### Stack Item
Consumables and ammo do not need affix instances. Store definition/type plus quantity.

## Stable IDs

Save data references stable definition IDs, never asset paths or display names. Moving/renaming a Unity asset must not break saves.
