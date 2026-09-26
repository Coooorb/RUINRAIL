# RUINRAIL Documentation Index

Read what the task needs, nothing more. Every durable fact has one home; if you are about to write it somewhere else,
update the home instead.

## Start here
- [`../CLAUDE.md`](../CLAUDE.md) — how Claude works on this repo (modes, loop, testing, doc rules).
- [`CURRENT_STATE.md`](CURRENT_STATE.md) — what is true about the project right now (2-minute read).

## Development
- [`DEVELOPMENT_WORKFLOW.md`](DEVELOPMENT_WORKFLOW.md) — expanded process, doc routing, prompt format, examples.
- [`DECISIONS.md`](DECISIONS.md) — durable accepted decisions and deliberate deferrals.
- [`ENVIRONMENT.md`](ENVIRONMENT.md) — pinned Unity/package toolchain and test harness.
- [`../scripts/README.md`](../scripts/README.md) — test harness, smoke and proof scripts.
- `../.claude/skills/` — per-domain working rules, loaded selectively.

## Design — intended player-facing behaviour (`design/`)
Code comments cite specs by short path (e.g. `dungeon/56_BIOMES.md`, `items/24`); they resolve under `docs/design/`
(and `technical/…` under `docs/technical/`).
- Project: [`01_PROJECT_VISION`](design/01_PROJECT_VISION.md), [`02_CORE_PILLARS`](design/02_CORE_PILLARS.md),
  [`03_CORE_GAME_LOOP`](design/03_CORE_GAME_LOOP.md), [`04_SCOPE_AND_NON_GOALS`](design/04_SCOPE_AND_NON_GOALS.md),
  [`05_TERMINOLOGY`](design/05_TERMINOLOGY.md), [`07_TUNABLE_VALUES`](design/07_TUNABLE_VALUES.md),
  [`08_REFERENCE_BOUNDARIES`](design/08_REFERENCE_BOUNDARIES.md), [`122_MVP_SCOPE`](design/122_MVP_SCOPE.md),
  [`126_FINAL_MVP_CONTENT_COUNTS`](design/126_FINAL_MVP_CONTENT_COUNTS.md)
- [`player/`](design/player/) 10–16 — profile, controller, stats, leveling, dash, life states, stat caps
- [`items/`](design/items/) 20–34 — inventory, item model, rarity/affixes, weapons, ammo, armor, accessories, consumables, loot transfer
- [`combat/`](design/combat/) 40–47 — combat rules, damage/status, stagger/knockback, enemies, elites, bosses, encounter budgets
- [`dungeon/`](design/dungeon/) 50–60 — grid/tiles, rooms, generator, validation, biomes, events, chests/merchant, depth scaling, extraction/transit
- [`base/`](design/base/) 70–77 — shelter, storage, trader, character station, workshop, starter kit, expedition summary, economy
- [`multiplayer/`](design/multiplayer/) 80–86 — architecture, sessions, authority, co-op scaling, downed/revive, disconnects, transit voting
- [`ui/`](design/ui/) 90–95 — UI/UX overview (controls), HUD, inventory UI, tooltips, base UI, onboarding
- [`art/`](design/art/) 100–107 — art direction, pixel grid, camera/sorting/lighting, animation, VFX/feel, audio/music, art bible,
  art production contract; `FINAL_ART_PRODUCTION_SPEC.md` (+ manifest CSV)

## Technical — contracts and architecture (`technical/`)
[`110`](technical/110_UNITY_PROJECT_STRUCTURE.md) project structure · [`111`](technical/111_DATA_ARCHITECTURE.md) data ·
[`112`](technical/112_SCRIPTABLE_OBJECTS.md) ScriptableObjects · [`113`](technical/113_SAVE_PERSISTENCE.md) save/persistence ·
[`114`](technical/114_RNG_DETERMINISM.md) RNG · [`115`](technical/115_EDITOR_VALIDATION_TOOLS.md) validators ·
[`116`](technical/116_INPUT_SYSTEM.md) input (read by the release validator) · [`118`](technical/118_TESTING_STRATEGY.md) testing ·
[`119`](technical/119_DEPENDENCY_RULES.md) dependencies · [`134`](technical/134_FINAL_PRODUCTION_ASSET_MANIFEST_RULES.md) asset manifest rules ·
[`136`](technical/136_ASSET_REPLACEMENT_WORKFLOW.md) asset replacement workflow

## Production
- [`../production/CURRENT_RELEASE_STATUS.md`](../production/CURRENT_RELEASE_STATUS.md) — **the only current release status.**
- `../production/FINAL_RELEASE_FROZEN_BASELINE.csv`, `asset_provenance.json`, `VISUAL_SLICE_APPROVAL.md`,
  `137_PROTOTYPE_VALUE_DISPOSITIONS.md` — machine/human baselines read by validators and tests. Do not move.

## Historical — do not read by default
- `../production/archive/` — superseded status docs, pass reports, run logs, one-off audit matrix scripts.
- Git history — task prompts, task files and every implementation step.
