# Final Production Asset Manifest Rules

> **Status:** Control document for TASK 149–184.

The repository's current validators/audits are the source of truth for exact role IDs. TASK 149 must generate a concrete checked-in manifest from current code/assets. Never maintain a second manually drifting count list when a validator can enumerate the roles.

## Required Manifest Categories

At minimum enumerate:

- Player source sprites and animation clip roles.
- 9 normal enemy source sprites and animation clip roles.
- 6 Elite source sprites and animation clip roles.
- 6 Boss source sprites and animation clip roles.
- 33 weapon world/equipped sprite roles.
- Armor/accessory/consumable/item/coin/ammo/world-pickup icon/sprite roles actually used by the UI/runtime.
- Ruined Metro, Rustworks and Overgrown Labs tiles/props/decals/foreground/environment VFX roles.
- Safehouse/base/transit presentation roles.
- UI frames, buttons, focus states, inventory slots, HUD glyphs, controller/keyboard glyphs, rarity frames and required icon roles.
- Pixel font role(s) and licensing/source metadata.
- Combat/environment/status/loot VFX roles.
- Current gameplay SFX event roles (the TASK-148 code manifest reported 53 unique audio-event IDs; TASK 149 must confirm the current number instead of hard-coding it forever).
- 11 music roles.
- 6 stinger roles.
- 3 ambience roles.
- Network Player Prefab and any presentation components required by that prefab.

## Per-Asset Record

Each manifest entry should carry, where applicable:

- Stable role ID / key.
- Intended runtime owner/system.
- Asset type.
- Source file path.
- Imported/generated runtime path.
- Pixel dimensions / sheet layout for sprite content.
- Expected PPU or UI usage.
- Required directions/states/frames.
- Current status: `PLACEHOLDER`, `MISSING`, `CANDIDATE`, `APPROVED`, `INTEGRATED`, `REJECTED`.
- Originality/source/license note.
- Human approval status when visual/audio judgment is required.

## Asset Source Rule

Do not rip, trace, recolor or ship copyrighted assets from reference games. External/generated/source art must be original RUINRAIL content and reviewed against the Art Bible.

## TASK-148 Baseline Reconciliation

TASK 149 must reconcile its generated manifest against `production/archive/135_TASK148_GAP_ANALYSIS_ADDENDUM.md`, including the baseline of 22×48 animation clip roles, 33 weapon sprite roles, three biome packages, final world-object/VFX/UI/font roles, 53 SFX IDs, 11 music roles, 6 stingers and 3 ambience loops. Any count delta must identify the exact code/data source that caused the delta.

A role may be `APPROVED` only after the required human gate where aesthetic/audio judgment is involved. Automated validators may prove import correctness/integration, but cannot self-approve visual or audio quality.
