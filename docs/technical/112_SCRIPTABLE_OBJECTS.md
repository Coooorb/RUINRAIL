# ScriptableObject Definitions

> **Status:** Approved design specification unless explicitly marked as tunable.
> **Game language:** English. Planning discussions may be in German, but all player-facing text, code naming, comments, and Claude implementation specs should be English.
Use ScriptableObjects for authored definitions and configuration, not mutable player saves.

Recommended definition/config types:
- WeaponDefinition.
- ArmorDefinition.
- AccessoryDefinition.
- ConsumableDefinition.
- AffixDefinition.
- EnemyDefinition.
- EliteDefinition.
- BossDefinition.
- RoomDefinition.
- LootTableDefinition.
- PlayerBalanceConfig.
- DepthScalingConfig.
- CoopScalingConfig.
- LootScalingConfig.

Definitions have stable string/ID keys. Save data resolves IDs through registries/databases rather than file paths.

Balance values should live in definitions/configs instead of magic numbers buried in behaviour scripts.
