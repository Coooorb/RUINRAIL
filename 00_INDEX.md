# RUINRAIL — Project Documentation Index

> **Status:** Approved design specification unless explicitly marked as tunable.
> **Game language:** English. Planning discussions may be in German, but all player-facing text, code naming, comments, and Claude implementation specs should be English.
This documentation describes **RUINRAIL**, a top-down 2D pixel-art PvE extraction roguelite for 1–3 players.

## How Claude Should Use These Docs

When this pack is placed at repository root, Claude Code should enter through `CLAUDE.md`. `ENVIRONMENT.md` is the pinned operational baseline.

Do **not** load the entire documentation set into every coding task. Use `production/124_CLAUDE_TASK_PROTOCOL.md` to select only the relevant specs. If implementation instructions conflict with an approved design file, report the conflict instead of silently inventing a new rule.

## Design Map

### Repository / Environment
- `CLAUDE.md` — automatic Claude Code project instructions.
- `CLAUDE_START_HERE.md` — task/context protocol.
- `ENVIRONMENT.md` — pinned Unity and package toolchain.
- `scripts/` — Unity test harness, release smoke (`run-release-smoke.sh`, `run-coop-release-smoke.sh`, `release_smoke_manifest.csv`).

### Current Release Status
- `production/FINAL_RELEASE_CANDIDATE_AUDIT.md` — **the current release-candidate status** (built-player scenarios, co-op proofs, smoke stability, NOT RUN items). Reports it names as superseded carry a banner and are historical.
- `production/FINAL_RELEASE_FROZEN_BASELINE.csv` — the frozen shipping values the release gate compares against.

### Project
- `01_PROJECT_VISION.md` — game identity and elevator pitch.
- `02_CORE_PILLARS.md` — design pillars used to judge new ideas.
- `03_CORE_GAME_LOOP.md` — base, expedition, boss, extraction, deeper-depth loop.
- `04_SCOPE_AND_NON_GOALS.md` — explicit scope limits.
- `05_TERMINOLOGY.md` — canonical terms.

### Player
- `player/10_PLAYER_PROFILE.md`
- `player/11_PLAYER_CONTROLLER.md`
- `player/12_PLAYER_STATS.md`
- `player/13_LEVELING_AND_SKILL_POINTS.md`
- `player/14_DASH_AND_MOVEMENT.md`
- `player/15_PLAYER_LIFE_STATES.md`
- `player/16_GLOBAL_STAT_CAPS.md`

### Items and Equipment
- `items/20_INVENTORY_SYSTEM.md`
- `items/21_ITEM_DATA_MODEL.md`
- `items/22_RARITY_AND_AFFIXES.md`
- `items/23_WEAPON_FRAMEWORK.md`
- `items/24_WEAPON_CLASSES.md`
- `items/25_LEGENDARY_WEAPON_SPECIALS.md`
- `items/26_AMMO_SYSTEM.md`
- `items/27_ARMOR_SYSTEM.md`
- `items/28_ARMOR_CATALOG.md`
- `items/29_ACCESSORY_SYSTEM.md`
- `items/30_ACCESSORY_CATALOG.md`
- `items/31_CONSUMABLES.md`
- `items/32_LOOT_PICKUPS_AND_ITEM_TRANSFER.md`
- `items/33_WEAPON_CATALOG.md`
- `items/34_LEGENDARY_ACCESSORY_PASSIVES.md`

### Combat and Enemies
- `combat/40_COMBAT_RULES.md`
- `combat/41_DAMAGE_HEALING_STATUS.md`
- `combat/42_STAGGER_KNOCKBACK.md`
- `combat/43_ENEMY_FRAMEWORK.md`
- `combat/44_NORMAL_ENEMIES.md`
- `combat/45_ELITES.md`
- `combat/46_BOSSES.md`
- `combat/47_ENCOUNTER_BUDGETS.md`

### Dungeon
- `dungeon/50_GRID_TILE_SYSTEM.md`
- `dungeon/51_ROOM_AUTHORING.md`
- `dungeon/52_ROOM_METADATA_AND_TAGS.md`
- `dungeon/53_DUNGEON_GENERATOR.md`
- `dungeon/54_DUNGEON_VALIDATION.md`
- `dungeon/55_ROOM_TYPES.md`
- `dungeon/56_BIOMES.md`
- `dungeon/57_EVENTS.md`
- `dungeon/58_CHESTS_MERCHANT_LOOT.md`
- `dungeon/59_DEPTH_SCALING.md`
- `dungeon/60_EXTRACTION_TRANSIT.md`

### Base
- `base/70_BASE_OVERVIEW.md`
- `base/71_STORAGE.md`
- `base/72_TRADER.md`
- `base/73_CHARACTER_STATION.md`
- `base/74_WORKSHOP.md`
- `base/75_STARTER_KIT.md`
- `base/76_EXPEDITION_SUMMARY.md`
- `base/77_ECONOMY.md`

### Multiplayer
- `multiplayer/80_MULTIPLAYER_ARCHITECTURE.md`
- `multiplayer/81_SESSIONS_JOIN_CODES.md`
- `multiplayer/82_NETWORK_AUTHORITY.md`
- `multiplayer/83_COOP_SCALING.md`
- `multiplayer/84_DOWNED_REVIVE_DEATH.md`
- `multiplayer/85_DISCONNECTS_SPECTATOR.md`
- `multiplayer/86_TRANSIT_VOTING.md`

### UI / UX
- `ui/90_UI_UX_OVERVIEW.md`
- `ui/91_DUNGEON_HUD.md`
- `ui/92_INVENTORY_UI.md`
- `ui/93_LOOT_TOOLTIPS.md`
- `ui/94_BASE_UI.md`
- `ui/95_ONBOARDING_TUTORIAL.md`

### Art / Audio
- `art/100_ART_DIRECTION.md`
- `art/101_PIXEL_GRID_AND_SCALE.md`
- `art/102_CAMERA_SORTING_LIGHTING.md`
- `art/103_ANIMATION_RULES.md`
- `art/104_VFX_GAME_FEEL.md`
- `art/105_AUDIO_MUSIC.md`
- `art/106_RUINRAIL_FINAL_ART_BIBLE.md`
- `art/107_ART_PRODUCTION_CONTRACT.md`

### Technical
- `technical/110_UNITY_PROJECT_STRUCTURE.md`
- `technical/111_DATA_ARCHITECTURE.md`
- `technical/112_SCRIPTABLE_OBJECTS.md`
- `technical/113_SAVE_PERSISTENCE.md`
- `technical/114_RNG_DETERMINISM.md`
- `technical/115_EDITOR_VALIDATION_TOOLS.md`
- `technical/116_INPUT_SYSTEM.md`
- `technical/117_CODING_RULES_FOR_CLAUDE.md`
- `technical/118_TESTING_STRATEGY.md`

### Production
- `production/120_IMPLEMENTATION_ORDER.md`
- `production/121_VERTICAL_SLICE.md`
- `production/122_MVP_SCOPE.md`
- `production/123_POST_MVP.md`
- `production/124_CLAUDE_TASK_PROTOCOL.md`
- `production/125_PROMPT_TEMPLATE.md`
- `production/126_FINAL_MVP_CONTENT_COUNTS.md`
- `production/127_MODEL_ROUTING_GUIDE.md`
- `production/131_CURRENT_PRODUCTION_STATE_AFTER_TASK_148.md` — historical (superseded; see Current Release Status)
- `production/132_COMPLETION_ROADMAP_TASK149_184.md`
- `production/133_REVIEW_GATED_COMPLETION_RUNNER.md`
- `production/134_FINAL_PRODUCTION_ASSET_MANIFEST_RULES.md`
- `production/135_TASK148_GAP_ANALYSIS_ADDENDUM.md`
- `production/136_ASSET_REPLACEMENT_WORKFLOW.md`

### Tasks
The `tasks/` folder contains small implementation prompts. Each task deliberately has a narrow scope and explicit acceptance criteria.
