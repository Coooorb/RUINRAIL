# RUINRAIL — FINAL AUTONOMOUS COMPLETION PASS V2
## Use with FINAL_ART_PRODUCTION_SPEC.md
## Start from the current repository state after TASK 184

This is the final autonomous production pass.

The user explicitly waives intermediate visual/audio approval prompts. Do NOT stop to ask whether you should continue. Do NOT pause at the old TASK 152/165/176 approval gates.

You must, however, distinguish objectively between:
- PLACEHOLDER
- PIPELINE_FALLBACK
- FINAL_ART

A non-null PNG is not automatically final art.

Read `FINAL_ART_PRODUCTION_SPEC.md` in full before producing or accepting any release visual. That document is the binding visual source of truth.

Only terminally stop for a TRUE external dependency that cannot be completed in the repository/environment, such as unavailable UGS account linking/credentials, or human-only subjective playtest evidence. Finish all independent work first.

---

# A. PRE-FLIGHT

Read the latest:
- CLAUDE.md
- CLAUDE_START_HERE.md
- technical/117_CODING_RULES_FOR_CLAUDE.md
- technical/118_TESTING_STRATEGY.md
- production/FINAL_MVP_COMPLETION_REPORT.md
- production/FINAL_RELEASE_COMPLETION_REPORT.md
- production/COMPLETION_RUN_LOG.md
- production/135_TASK148_GAP_ANALYSIS_ADDENDUM.md
- art/106_RUINRAIL_FINAL_ART_BIBLE.md
- FINAL_ART_PRODUCTION_SPEC.md
- current art/audio manifests and validators

Repository truth wins over older assumptions.

Create/update:
`production/FINAL_AUTONOMOUS_COMPLETION_GAP_AUDIT.md`

Verify current gap counts before changing files.

---

# B. SCOPE FREEZE

Do not add new gameplay systems or approved-content-count expansions.

Do not add:
PvP, classes, stamina, durability, crafting, gear score, crits, weak spots, extra currencies, matchmaking, dedicated servers, host migration, achievements, leaderboards or extra V1 weapons/enemies/rooms/bosses.

Preserve stable IDs, save compatibility and deterministic generation.

---

# C. CLOSE THE ONE KNOWN CODE SEAM

Check `ItemDefinition`.

If it still has no serialized icon Sprite/reference field:
- add the narrowest compatible icon field;
- preserve existing IDs/data;
- wire UI/item bindings;
- add targeted tests;
- run impacted EditMode/PlayMode tests.

If already fixed, verify and continue.

---

# D. ART PRODUCTION — MUST FOLLOW FINAL_ART_PRODUCTION_SPEC.md

Produce and integrate all release visual roles.

## D1. Characters
Complete final source sheets/frame sets for:
- player
- 9 normal enemies
- 6 Elites
- 6 Bosses

Use the exact design profiles in `FINAL_ART_PRODUCTION_SPEC.md`.

Build/bind the existing 1,056 clip roles from directional source sheets through the current pipeline.

Do not create redundant drawings where clips can be derived.
Do not alter gameplay timing to fit animation.
Animation must fit gameplay timing.

## D2. Weapons
Produce all 33 final +X weapon sprites using the named design profiles in the art spec.

Validate:
- pivot
- grip
- muzzle
- orientation
- class readability
- Legendary differentiation
- bow/blaster/reload states required by current runtime

## D3. Icons
Produce and bind exactly:
- 33 weapon
- 9 armor
- 16 accessory
- 10 consumable
- 4 ammo
= 72 final item icons.

## D4. Biomes
Complete:
- Ruined Metro
- Rustworks
- Overgrown Labs

Each must implement the exact tile, dressing, material, lighting and palette rules in the art spec.

Replace every release-bound placeholder tile reference in all 63 rooms.
Target zero.

Do not alter room logic/layout.

## D5. Shelter
Complete final Shelter/base art and every station:
- Storage
- Loadout
- Trader
- Character
- Workshop
- Multiplayer Terminal
- Expedition Transit

## D6. World objects
Complete all manifest roles including:
- Supply Chest
- item pickup
- coin pickup
- Dungeon Merchant
- Medical Station
- Transit Car
- doors
- Locked Vault
- Cursed Chest
- Broken Machine
- Supply Signal
- Medical Station event representation
- Weapon Cache
- any additional current manifest-required world object

## D7. UI
Fully skin every release screen using the exact UI rules in the art spec.

Replace release dependency on `LegacyRuntime.ttf`.

Provide final keyboard/mouse and controller glyphs.

Run text-fit using the real final font and fix overflow.

## D8. VFX
Replace every white-square/prototype VFX with final pixel VFX according to the art spec.

Telegraphs must remain mechanically truthful and readable in all three biome lighting profiles.

## D9. Lighting
Author final lighting for all three biomes according to the art spec.

---

# E. ART VALIDATION

Do not accept art solely because files exist.

Where feasible, extend manifests/validators to record or verify final-art provenance.

Require:
- 22/22 final character families
- 1,056/1,056 animation roles resolved
- 33/33 final weapon sprites
- 72/72 icons bound
- 3/3 complete biome packages
- all 63 rooms free of release placeholders
- complete world-object visuals
- complete Shelter visuals
- complete UI/font/glyphs
- 13/13 VFX/telegraph roles or exact current manifest count
- 3/3 final biome lighting looks
- 0 invisible player/enemy/weapon render paths
- 0 release-bound white-square VFX
- 0 release-bound flat-color debug tiles
- 0 release dependency on LegacyRuntime.ttf

If screenshot/render capture tooling exists, capture the representative scenes listed in the art spec and inspect them.

If the coding environment cannot visually inspect output, do not fabricate a human aesthetic judgment. Structural compliance may pass while visual inspection remains a clearly stated external review item.

If locally/programmatically generated art does NOT satisfy the final-art spec, keep it as PIPELINE_FALLBACK and report the role incomplete. Do not promote bad placeholder art just to satisfy counts.

---

# F. AUDIO

Complete all:
- 53 SFX
- 11 music tracks
- 6 stingers
- 3 ambience loops

Original/licensable audio only.

If dedicated audio generation is unavailable, procedural/local generation is allowed only if the result is suitable for release and not merely a test tone.

Perform practical mix/routing checks:
- no clipping
- combat-critical cues audible
- UI subordinate
- ambience subordinate
- boss cues distinct
- music transitions clean
- no required event maps to null

---

# G. LIVE MULTIPLAYER

If the Unity project is linked to a valid UGS project and credentials/services are available:
- run real Sessions/Relay integration;
- create two release clients;
- host;
- obtain join code;
- join second client;
- verify spawn, movement, combat, health, enemies, loot, downed/revive, boss flow, transit voting, return, disconnect/reconnect.

If UGS account/project linking is unavailable:
- do not fake a pass;
- do not restore fake online behavior;
- finish everything else;
- report the minimal remaining user action only at terminal end.

---

# H. FINAL-CONTENT PERFORMANCE

Run performance with FINAL content:
- Windows release player
- >=18 active enemies
- realistic projectiles
- VFX
- audio
- loot
- UI
- final biome rendering
- trio-equivalent load where applicable

Record:
- average frame time
- p95
- p99
- worst frame
- GC/allocations
- memory trend
- object count trend

Fix meaningful regressions and rerun.

---

# I. AUTOMATED SOAK / PLAYABILITY

Run repeated end-to-end scenarios:
- menu -> Shelter -> dungeon -> return
- multiple depths
- all three biomes
- multiple weapon classes
- melee/ranged
- consumables
- events
- merchant
- Elites
- Bosses
- save/reload
- wipe/failure
- downed/revive where harness supports it
- inventory/storage/economy transactions
- long soak
- text-fit
- controller/input maps where automatable
- objective telegraph contrast checks

Do not fabricate human feel judgment.

---

# J. FEEL-SENSITIVE VALUES

Re-evaluate:
- camera follow sharpness
- music crossfade
- downed crawl speed
- enemy strike-hold
- remaining TUNE_FINAL/PROTOTYPE values

Change only with objective evidence.
Otherwise keep current values and document them as retained defaults pending human feel feedback.

---

# K. FULL REGRESSION

Run strict EditMode and PlayMode suites.

PASS requires:
- Unity/test process exit 0
- XML exists
- total > 0
- passed > 0
- failed = 0

Also run:
- content-count validation
- production validation
- presentation validation
- art validation
- animation validation
- audio validation
- save hardening
- exploit hardening
- network validation
- build validation

Fix failures and continue.

---

# L. RELEASE BUILD

Produce clean Windows x64 NON-DEVELOPMENT build.

Run built-player smoke:
Main Menu -> Shelter -> Expedition -> combat/loot -> return -> save -> reload.

Require:
- no exceptions
- no missing scripts
- no missing references
- player visible
- enemies visible
- weapons visible
- final room visuals
- final UI
- real audio playback
- save consistency

---

# M. DOCUMENTATION

Append continuously:
`production/FINAL_AUTONOMOUS_COMPLETION_LOG.md`

At terminal end create:
`production/FINAL_SHIPPABLE_V1_REPORT.md`

Include exact:
- asset counts
- final vs fallback vs placeholder counts
- placeholder references before/after
- animation counts
- icon counts
- biome counts
- VFX counts
- audio counts
- exact test results
- validators
- build result
- smoke result
- final-content performance
- live Relay result
- visual-inspection status
- human-playtest status
- true remaining blockers

---

# N. TERMINAL POLICY

Allowed statuses:

## RELEASE_COMPLETE
Only when all mandatory V1 engineering/content/audio/network/release requirements have real evidence and there is no mandatory blocker.

## BLOCKED_EXTERNAL_DEPENDENCY
Use only when every repository-local task is complete and a true external requirement remains, for example:
- UGS linking/credentials
- required external visual inspection the coding environment cannot truthfully perform
- human subjective playtest

## INCOMPLETE
Use when repository-local work remains.

Do not create TASK185 just to defer work.

Do not ask for intermediate approval.
Do not pause at old review gates.
Do not claim fallback art is final unless it passes the full art spec.
Do not fabricate evidence.

BEGIN NOW.
