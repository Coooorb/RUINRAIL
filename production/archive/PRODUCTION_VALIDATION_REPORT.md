# RUINRAIL V1 final production validation (TASK 145)

Data/engineering result: **PASS** — 9/9 sections pass, 0 errors.
External assets (not data failures): **BLOCKED_EXTERNAL_ASSET** — 97 outstanding.

## Stable ids — PASS
- PASS 72 item ids unique, non-empty, stable.
- PASS 9 enemy ids unique, non-empty, stable.
- PASS 6 elite ids unique, non-empty, stable.
- PASS 6 boss ids unique, non-empty, stable.
- PASS 63 room ids unique, non-empty, stable.
- PASS 51 attack ids unique, non-empty, stable.
- PASS 3 hazard ids unique, non-empty, stable.
- PASS 15 affix ids unique, non-empty, stable.
- PASS 5 loot table ids unique, non-empty, stable.
- PASS 53 audio event ids unique, non-empty, stable.
- PASS 21 actor ids unique across enemies, Elites and Bosses.

## Serialized references — PASS
- PASS 5091 assets scanned: every serialized object reference resolves.

## Rooms and room pools — PASS
- PASS 64 rooms generator-ready (schema, sockets, reachability, markers).
- PASS Three biome room pools complete (21 rooms each).

## Loot tables, rarity tables, Legendary mechanics, prices — PASS
- PASS LootSourceCatalog: 4 sources and 3 rarity tables resolve.
- PASS 5 loot tables and 3 rarity tables have valid weights and non-empty rolls (where no error is listed).
- PASS Weapon catalog: 33 definitions, every Legendary has its registered special/passive, prices from the economy tables.

## Enemies, Elites, Bosses — PASS
- PASS 9 enemies, 6 Elites, 6 Bosses: stats and attack references valid.

## Content counts (production/126) — PASS
- PASS 52/52 count lines exact: 33 weapons / 9 armor / 16 accessories / 10 consumables / 9 enemies / 6 Elites / 6 Bosses / 63 rooms.

## Build scenes and boot flow — PASS
- PASS Build settings: Bootstrap, MainMenu, Base, Dungeon enabled in order and present.
- PASS Bootstrap scene carries AppRoot (loads MainMenu).

## Assembly hygiene (no Editor leaks into runtime) — PASS
- PASS 8 runtime assemblies reference no Editor assembly; no runtime script uses UnityEditor outside #if UNITY_EDITOR.

## Network prefabs — PASS
- PASS DefaultNetworkPrefabs.asset present with 0 entries, all resolving.

## Findings for later tasks (not data failures)
- Scene 'MainMenu' has no composition root yet (no MonoBehaviour): the boot flow (TASK 147) must compose the screen/run from the existing view models and services.
- Scene 'Base' has no composition root yet (no MonoBehaviour): the boot flow (TASK 147) must compose the screen/run from the existing view models and services.
- Scene 'Dungeon' has no composition root yet (no MonoBehaviour): the boot flow (TASK 147) must compose the screen/run from the existing view models and services.
- No network player prefab is registered yet: NgoPlayerEntityFactory takes the prefab at composition (TASK 147 boot flow); the live NGO/Relay path stays NOT RUN until it exists.

## External art / audio blockers (BLOCKED_EXTERNAL_ASSET)
- animation set 'player' (Player): 48/48 clips missing
- animation set 'bomber' (Enemy): 48/48 clips missing
- animation set 'brute' (Enemy): 48/48 clips missing
- animation set 'charger' (Enemy): 48/48 clips missing
- animation set 'grunt' (Enemy): 48/48 clips missing
- animation set 'shield_enemy' (Enemy): 48/48 clips missing
- animation set 'shooter' (Enemy): 48/48 clips missing
- animation set 'sniper_enemy' (Enemy): 48/48 clips missing
- animation set 'summoner' (Enemy): 48/48 clips missing
- animation set 'swarm' (Enemy): 48/48 clips missing
- animation set 'elite_crusher_unit' (Elite): 48/48 clips missing
- animation set 'elite_mutated_brute' (Elite): 48/48 clips missing
- animation set 'elite_prototype_x7' (Elite): 48/48 clips missing
- animation set 'elite_railguard' (Elite): 48/48 clips missing
- animation set 'elite_scrap_executioner' (Elite): 48/48 clips missing
- animation set 'elite_tunnel_stalker' (Elite): 48/48 clips missing
- animation set 'boss_aegis_core' (Boss): 48/48 clips missing
- animation set 'boss_scrap_king' (Boss): 48/48 clips missing
- animation set 'boss_subject_omega' (Boss): 48/48 clips missing
- animation set 'boss_the_conductor' (Boss): 48/48 clips missing
- animation set 'boss_the_foundry_titan' (Boss): 48/48 clips missing
- animation set 'boss_tunnel_maw' (Boss): 48/48 clips missing
- weapon sprites: 0/33
- sfx clip 'weapon.fire.pistol'
- sfx clip 'weapon.fire.smg'
- sfx clip 'weapon.fire.assault_rifle'
- sfx clip 'weapon.fire.battle_rifle'
- sfx clip 'weapon.fire.shotgun'
- sfx clip 'weapon.fire.sniper'
- sfx clip 'weapon.fire.blaster'
- sfx clip 'weapon.bow.draw'
- sfx clip 'weapon.bow.release'
- sfx clip 'weapon.melee.knife_slash'
- sfx clip 'weapon.melee.spear_thrust'
- sfx clip 'weapon.fire.rocket_launch'
- sfx clip 'weapon.rocket.explosion'
- sfx clip 'weapon.reload'
- sfx clip 'weapon.dry_fire'
- sfx clip 'weapon.blaster.heat_rising'
- sfx clip 'weapon.blaster.overheat_warning'
- sfx clip 'weapon.blaster.overheat'
- sfx clip 'weapon.blaster.vent'
- sfx clip 'weapon.legendary_special'
- sfx clip 'enemy.telegraph'
- sfx clip 'enemy.telegraph.elite'
- sfx clip 'enemy.telegraph.boss'
- sfx clip 'enemy.hit'
- sfx clip 'enemy.stagger'
- sfx clip 'enemy.death'
- sfx clip 'enemy.elite.spawn'
- sfx clip 'enemy.boss.phase'
- sfx clip 'player.hit'
- sfx clip 'player.dash'
- sfx clip 'player.downed'
- sfx clip 'player.revive.start'
- sfx clip 'player.revive.complete'
- sfx clip 'player.death'
- sfx clip 'player.consumable.use'
- sfx clip 'player.heal'
- sfx clip 'player.hazard'
- sfx clip 'loot.pickup.item'
- sfx clip 'loot.pickup.coins'
- sfx clip 'loot.drop.common'
- sfx clip 'loot.drop.uncommon'
- sfx clip 'loot.drop.rare'
- sfx clip 'loot.drop.epic'
- sfx clip 'loot.drop.legendary'
- sfx clip 'loot.chest.open'
- sfx clip 'world.door.open'
- sfx clip 'world.transit.depart'
- sfx clip 'world.merchant.open'
- sfx clip 'ui.navigate'
- sfx clip 'ui.confirm'
- sfx clip 'ui.cancel'
- sfx clip 'ui.failure'
- sfx clip 'ui.purchase'
- music track 'MainMenu'
- music track 'Shelter'
- music track 'RuinedMetroExploration'
- music track 'RuinedMetroCombat'
- music track 'RuinedMetroBoss'
- music track 'RustworksExploration'
- music track 'RustworksCombat'
- music track 'RustworksBoss'
- music track 'OvergrownLabsExploration'
- music track 'OvergrownLabsCombat'
- music track 'OvergrownLabsBoss'
- stinger 'LegendaryDrop'
- stinger 'EliteEncounter'
- stinger 'BossDefeated'
- stinger 'ExtractionSuccess'
- stinger 'ExpeditionFailed'
- stinger 'LevelUp'
- ambience loop 'RuinedMetro'
- ambience loop 'Rustworks'
- ambience loop 'OvergrownLabs'
- room tile sets, character/enemy/boss sprites, VFX sprites, UI frames/icons and the pixel font (placeholder tiles/flat sprites/built-in font in use)
