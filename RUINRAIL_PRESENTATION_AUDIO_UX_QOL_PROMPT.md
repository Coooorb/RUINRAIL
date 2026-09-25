# RUINRAIL — PRESENTATION / AUDIO / UX-QOL CONSOLIDATION PASS

## ROLE

You are performing one consolidated presentation / audio / UX-QOL implementation pass on the current RUINRAIL Unity repository.

This pass intentionally combines the remaining high-value non-structural polish findings from the completed full-game review so they do not become many separate micro-tasks.

The pass covers:
1. World substrate / black-void presentation
2. Audio mix authoring and repetition control
3. Shelter Trader presentation parity
4. Status-effect display
5. Base pickup attraction QoL
6. Grenade quick-use QoL
7. Aim-assist setting / toggle
8. Ammo-economy reminder prompt
9. Help / Codex page
10. HUD / bossbar / text readability polish
11. Trader text truncation
12. Door / damage-number / body-text contrast
13. Light prop-dressing variety polish

This is an IMPLEMENTATION + VALIDATION task.

Do not turn this into a redesign of accepted art, combat, balance, progression, rooms, bosses, biomes, co-op, or economy.

Work autonomously.
Do not pause for approval.
Do not ask intermediate questions.
Do not stop after static analysis.
Inspect the current repository before changing anything.
Use runtime evidence.

---

# REPOSITORY SAFETY — CRITICAL

The current working tree contains intentional uncommitted work and is authoritative.

NEVER use:
- `git checkout`
- `git restore`
- `git reset`
- `git clean`
- `git stash`
- destructive cleanup
- broad file reverts
- any command that discards unrelated uncommitted work

Do not “clean up” files outside this task.

If you need to undo your own edit, restore it manually from content actually inspected during this task.

---

# IMPORTANT CURRENT PROJECT STATE

The following systems are already accepted or recently fixed and are FROZEN unless a literal regression is discovered:

- graphical in-run inventory
- player characters
- enemy character art
- weapon art
- VFX
- current Main Menu identity
- Dungeon Merchant functionality/presentation
- current boss logic from the Run Variety / Depth Retention / Boss pass
- boss seeded attack selection
- boss anti-kite reposition behavior
- elite frequency tuning
- depth-gated rooms
- biome gameplay weighting / hazard distinctions
- deepest-depth persistence / UI
- post-D30 reward continuation
- D1 ammo fine-tuning
- Field Knife balance
- blaster fine-tuning
- weapon stat consumers / AffixRegistry / WeaponStatMath
- StatConsumerIntegrityValidator
- D1–D30 difficulty scaling
- post-D30 difficulty scaling
- run-start and depth-arrival full-HP rules
- encounter containment
- save/persistence architecture
- non-combat room interactions
- progression attribute pipeline
- current combat aim/hit/collision behavior
- death / extraction transactions
- backpack reorder
- current minimap
- current graphical HUD structure

Do NOT reopen these design decisions.

---

# PRIMARY OBJECTIVE

Make RUINRAIL feel materially more finished and easier to use without changing its core combat/economy design.

By the end of this pass:

1. Small rooms no longer read as isolated bright islands in a featureless black void.
2. The audio pipeline has a deliberate mix rather than default-volume/default-pitch content.
3. Frequently repeated sounds are less fatiguing.
4. Shelter Trader presentation is clear and item-readable.
5. Timed player buffs/debuffs are visible and understandable.
6. Pickups feel less interaction-heavy at very short range.
7. Grenades have a practical quick-use path.
8. Aim assist can be disabled by the player.
9. Low-ammo teaching exists without becoming spammy.
10. Help/Codex covers the important persistent game rules.
11. Remaining HUD/readability/truncation issues are cleaned up.
12. Existing accepted gameplay and art remain intact.

---

# PHASE 1 — CURRENT-STATE REVERIFICATION

Before editing, inspect the CURRENT repository and capture the actual state.

Create:
`TestResults/PresentationAudioUxQol/current_state_before.csv`

Re-verify at minimum:
- how the dungeon camera renders outside room bounds
- whether any substrate/backdrop/tunnel-underlay already exists
- current room extents vs viewport at 640×360
- current audio buses
- current music / ambience / SFX event gain values
- pitch range values
- min-interval / repetition throttles
- max-instance settings
- clip counts / variation counts for repeated events
- loop durations
- Shelter Trader current UI
- Dungeon Merchant UI primitives available for reuse
- active timed player buffs/debuffs
- current status-effect data model
- current HUD capacity / safe areas
- pickup interaction path and current base attraction radius
- Magnetic Coil effect
- grenade inventory/use path
- current input map and available actions
- aim-assist config and Settings system
- current tutorial/onboarding prompts
- existing Help/Codex support, if any
- top HUD crowding
- boss bar surroundings / black rectangle issue
- text contrast in Shelter/Main Menu secondary text
- damage-number readability
- door visual readability
- trader row truncation
- prop dressing / decoration variation logic

If an old review finding is already fixed, do not reimplement it. Document it as stale.

---

# PHASE 2 — WORLD SUBSTRATE / BLACK-VOID FIX

The goal is NOT to hide rooms with camera tricks alone unless that is clearly the cleanest existing architecture.

Preferred solution:
A dark world substrate / backdrop layer beneath the dungeon layout that visually belongs to the biome and fills visible space outside room floor bounds.

Requirements:
- preserve current room geometry
- preserve current camera behavior unless a tiny clamp adjustment is needed
- preserve door/socket logic
- preserve minimap
- preserve collision
- preserve navigation
- preserve encounter bounds
- preserve biome art
- do not redesign room floor tiles
- do not obscure gameplay silhouettes
- do not create fake traversable floor

The backdrop must read as NON-WALKABLE / environmental underlay.

Preferred visual direction:
- dark
- low contrast
- industrial / subterranean
- subtle texture
- biome-aware
- no high-frequency noise
- no giant repeating checker pattern

Examples by biome, derived from existing art:
- Ruined Metro: deep tunnel bed / rail darkness / old concrete or track-bed texture
- Rustworks: dark industrial pit / soot / machinery substrate
- Overgrown Labs: dark service floor / structural underlayer / organic overgrowth shadow layer

Technical rules:
- render below gameplay floor
- no collider unless explicitly required
- no pathing impact
- no room-sealing impact
- no tile-occupancy impact
- deterministic for layout size

Create:
`TestResults/PresentationAudioUxQol/world_substrate_matrix.csv`

For all 3 biomes capture:
- small room
- medium room
- large room
- layout-edge room
- room cluster with inter-room gaps

Confirm:
- no raw black void dominates frame
- substrate is quieter than gameplay floor
- no traversal ambiguity
- no sorting regression
- no minimap impact

---

# PHASE 3 — AUDIO MIX AUTHORING

The runtime audio architecture already exists and was previously verified. Do not rebuild it.

Create:
`TestResults/PresentationAudioUxQol/audio_mix_before.csv`

For every audio event capture:
- event id
- category/bus
- current gain
- pitch min/max
- min interval
- max instances
- clip count
- loop/non-loop
- loop duration where applicable
- positional/global
- high-frequency/repetitive YES/NO
- runtime trigger path

Author a restrained category-based mix for:
- UI
- player weapons
- enemy weapons
- impacts
- enemy vocals
- player feedback
- pickups/interactions
- doors/room transitions
- ambience
- music
- boss music
- critical warning / low HP
- stingers

Do not leave everything at 1.0.
Do not create extreme gain differences.

For frequently repeated events, add modest variation where supported:
- small pitch variation
- sensible min interval
- max-instance limits
- clip variation if compatible clips already exist

Do not pitch music or ambience.
Do not distort signature weapon sounds.

If repeated SFX have only one clip and no alternative exists, do not fabricate a large external audio library. Use conservative pitch/interval variation and document remaining repetition.

Music/ambience:
- improve loop continuity if needed
- preserve current state machine
- do not rewrite soundtrack
- if longer authored music requires new external composition, report it honestly as external/content work

Preserve:
- persistent AudioListenerRig
- 2D relative attenuation/panning
- expedition music state
- output-device diagnostics/recovery
- settings volume persistence

Create:
`TestResults/PresentationAudioUxQol/audio_mix_after.csv`

Runtime evidence should cover:
- Shelter
- normal dungeon
- combat
- boss
- merchant/UI
- low HP

Verify:
- no duplicate listener
- no all-events-at-full-gain state
- repeated SFX variation occurs
- settings volumes still work
- device recovery still works
- no obvious clipping where measurable

---

# PHASE 4 — SHELTER TRADER PRESENTATION PARITY

Bring Shelter Trader to comparable clarity with the accepted Dungeon Merchant.

Prefer reuse of existing MerchantView primitives/styles.

Required:
- item icon
- item name
- rarity
- category
- price
- owned/equipped state where relevant
- details pane
- useful stat rows
- affix lines
- comparison to equipped item where data exists
- current buy/sell affordance
- mouse
- keyboard
- controller

Fix identical-name / different-price ambiguity.
Fix truncation.
Do not alter economy values or transaction authority.

Create:
`TestResults/PresentationAudioUxQol/shelter_trader_matrix.csv`

Verify:
- 72 item icons still bound
- no missing glyphs
- no overlap/truncation at 640×360
- pointer/controller navigation
- comparison works
- transactions unchanged

---

# PHASE 5 — STATUS-EFFECT DISPLAY

Re-verify actual runtime timed effects first.

Display ONLY effects that actually exist at runtime.

Use compact icon-based status chips.

Each active timed effect should show:
- icon
- remaining duration or radial fill
- stacks only if real
- positive/negative distinction
- tooltip/details on hover/focus if practical

Do not clutter the HUD.

Bind directly to authoritative runtime state.
No duplicate timer logic.
No stale UI after expiration.

Preserve:
- minimap
- biome label
- enemy remaining chip
- boss bar
- low-HP vignette
- dash icon
- weapon HUD
- consumable HUD

Create:
`TestResults/PresentationAudioUxQol/status_effect_matrix.csv`

For every timed status verify:
- activation
- icon
- duration
- expiry
- UI clear
- scene/save behavior
- controller tooltip path if applicable

---

# PHASE 6 — BASE PICKUP ATTRACTION

Reduce per-pile friction slightly.

Do NOT remove the Interact system entirely.
Do NOT vacuum loot across the room.

Add a SMALL baseline attraction radius for appropriate lightweight pickups:
- coins
- ammo
- small standard pickups if current architecture treats them similarly

Use existing PickupAttractor.

Target feel:
“walk over / very near it and it comes to you”

not:
“loot flies from several tiles away”.

Magnetic Coil must remain meaningfully stronger.
If it currently adds +3 tiles, preserve that additive identity.

Do not auto-attract:
- chests
- merchants
- event objects
- doors
- weapon caches
- large choice interactables

Create:
`TestResults/PresentationAudioUxQol/pickup_attraction_matrix.csv`

Verify:
- base pickup
- Magnetic Coil pickup
- no through-wall attraction
- no room-boundary pull
- no capacity bypass
- no duplication
- no merchant/event confusion

---

# PHASE 7 — GRENADE QUICK-USE

Add a quick-use path WITHOUT redesigning inventory.

Preferred:
A dedicated input action `QuickGrenade` or repository-consistent equivalent.

It should:
- find the first usable grenade according to existing inventory/consumable rules
- use the existing authoritative consumable service
- decrement exactly once
- respect cooldown / targeting / throw rules
- do nothing safely if none exists
- work with mouse/keyboard and controller
- be rebindable

Do not create a second consumable system.
Do not duplicate grenade logic.

Block grenade use while:
- inventory open
- merchant open
- pause open
- event/choice modal open
- any gameplay input gate is active

Create:
`TestResults/PresentationAudioUxQol/grenade_quick_use_matrix.csv`

Verify:
- correct grenade used
- correct decrement
- no double-use
- no UI bypass
- controller
- rebinding
- save/load unaffected

---

# PHASE 8 — AIM-ASSIST SETTING

Do NOT change current default strength.

Add:
`Aim Assist: On/Off`

Default:
ON

Persist using the existing settings document.

The setting must affect the real ShotSolver / aim-assist path.

When OFF:
- direct aim remains valid
- no target bend
- no proximity assist
- no hidden duplicate assist unless explicitly designed elsewhere

When ON:
- current angles/behavior remain unchanged

Create:
`TestResults/PresentationAudioUxQol/aim_assist_setting_matrix.csv`

Verify:
- default ON
- OFF persists
- ON persists
- live toggle changes real shot direction
- mouse/keyboard/controller Settings UI
- no profile/save coupling mistake

---

# PHASE 9 — LOW-AMMO TEACHING PROMPT

Add one contextual teaching prompt for a new player hitting meaningful low ammo.

Intent example:
`LOW AMMO — USE BOTH WEAPONS TO CONSERVE ROUNDS`

Use concise repository-consistent wording.

Requirements:
- one-time per profile or existing tutorial persistence convention
- trigger at an appropriate reserve threshold
- no spam
- no UI lock
- no ammo change
- do not imply melee is mandatory
- prefer an earlier useful trigger rather than first appearing only in boss panic

Use existing contextual prompt system.

Create:
`TestResults/PresentationAudioUxQol/ammo_prompt_matrix.csv`

Verify:
- triggers once
- persists correctly
- does not overlap badly with other prompts

---

# PHASE 10 — HELP / CODEX PAGE

Create a compact Help/Codex page using the existing UI system.

Do NOT build a giant lore encyclopedia.

Minimum sections:

## Core Run Rules
- Shelter → Expedition → Boss → Transit
- RETURN vs DESCEND
- what extraction secures
- what death/wipe loses
- XP / level / skill persistence

## Loadout
- Primary / Secondary
- Armor
- Accessory
- Active Consumable
- 8 backpack slots

## Ammo
- ammo types
- ammo caps
- use both weapons / resource management
- blaster heat
- melee ammo-free

## Rarity / Affixes
- rarity ladder
- affixes change stats
- Legendary mechanics

## Combat
- dash
- reload
- aim-assist setting
- enemy room locks
- elites / bosses

## Co-op / Downed
Document ONLY features actually implemented in the current shipped build.
Do not claim live co-op if it is not real yet.

## Controls
Point to rebindable controls/settings.

## Accessibility / Settings
Mention only settings that actually exist.

Access from Main Menu or Settings/Help, and Pause if clean.

Use accepted UiFont / UiSkin.

Create:
`TestResults/PresentationAudioUxQol/codex_matrix.csv`

Verify:
- navigation
- scrolling
- controller focus
- no clipping
- all factual statements match runtime

---

# PHASE 11 — HUD / BOSSBAR / READABILITY POLISH

Re-verify old findings first. Only fix issues that still exist.

## A. HUD top-band crowding
If still crowded:
- tighten spacing
- preserve minimap + biome readability
- preserve enemy-remaining chip scoping

## B. Boss-bar black rectangle
If still present:
- identify exact source
- fix layout/sprite/9-slice/root cause
- do not cover it with another panel

## C. Shelter / Main Menu secondary text contrast
Improve only if still weak.
Do not change Main Menu identity.

## D. Damage numbers
Improve readability if still disappearing against combat backgrounds.
Preserve pixel-art feel.

## E. Doors
Improve open/locked readability only if still ambiguous.
Do not redesign all door art.

## F. Trader truncation
Ensure names/prices remain legible.

Create:
`TestResults/PresentationAudioUxQol/readability_matrix.csv`

Validate at:
- 640×360
- 16:9
- at least one supported non-16:9 window

---

# PHASE 12 — LIGHT PROP-DRESSING VARIETY

This is NOT a room-art redesign.

If current prop placement is still overly repetitive, improve variation using current prop assets.

Allowed:
- deterministic placement variety
- rotation/flip where supported
- modest density variation
- biome-aware prop pools
- avoid identical repeated local clusters

Do NOT:
- obstruct doors
- obstruct combat
- cover pickups
- block navigation
- reduce telegraph readability
- alter room geometry

Use deterministic seed-driven variation.

Create:
`TestResults/PresentationAudioUxQol/prop_dressing_matrix.csv`

Record:
- pattern count
- blocked-door count
- blocked-pickup count
- navigation regressions
- biome pool

---

# PHASE 13 — SETTINGS INTEGRATION

Ensure all affected settings integrate into the existing SettingsPanel architecture.

Do not create another settings framework.

Verify:
- Master Volume
- Music Volume
- SFX Volume
- Aim Assist
- existing video/display settings
- input rebinding
- any existing accessibility toggles

Preserve fixed category/submenu behavior.

Older settings documents missing a key must get safe defaults rather than resetting volume/settings.

---

# PHASE 14 — VALIDATORS / CONTRACTS

Do not weaken validators to get green.

Add or extend honest production validation.

Suggested:
`PresentationAudioUxQolValidator`

Validate at minimum:
- substrate exists for all 3 biomes
- substrate is presentation/non-traversable
- no duplicate AudioListener
- release audio events have intentional gain/pitch policy
- high-frequency events have repetition policy
- Shelter Trader required presentation exists
- runtime timed statuses have display metadata where appropriate
- aim-assist setting maps to runtime
- Help/Codex required sections exist
- no placeholder fonts
- no required-glyph omissions
- pickup base radius stays within safe declared bound
- Magnetic Coil remains strictly stronger
- QuickGrenade action exists and is input-gated
- recent run-variety/depth/boss contracts still pass

A deliberately broken fixture should fail any new validator.

---

# PHASE 15 — REGRESSION FREEZE CHECK

Create:
`TestResults/PresentationAudioUxQol/frozen_systems_check.csv`

Explicitly verify unchanged:
- D1 ammo tuning
- Field Knife
- blaster tuning
- all other weapon base balance
- D1–D30 difficulty scaling
- post-D30 difficulty scaling
- post-D30 reward curve
- deepest-depth persistence
- boss seeded attack selection
- boss anti-kite fallback
- elite frequency
- room depth gating
- biome gameplay identity
- ammo caps
- starter kit
- run-start HP
- depth-arrival HP
- PlayerStats
- AffixRegistry
- WeaponStatMath
- StatConsumerIntegrity
- graphical inventory
- Dungeon Merchant behavior
- non-combat room behavior
- progression
- save/persistence
- death / extraction
- EncounterBounds
- room locks
- minimap logic
- character art
- weapon art
- VFX
- multiplayer/network architecture

Any unexpected drift is a failure.

---

# PHASE 16 — TESTS

Run at minimum:
- full EditMode
- full PlayMode
- production validators
- FinalProductionValidator
- ContentCountValidator/current equivalent
- StatConsumerIntegrityValidator
- save/migration validators
- Settings tests
- UI/navigation tests
- audio runtime tests
- inventory/merchant tests
- combat smoke
- generation smoke
- run-variety/depth/boss regression tests

Known pre-existing seed/clock-sensitive flakes must be reported honestly.
Do not hide them.
Do not broaden this task into a general test-infrastructure rewrite unless one of your own new tests is incorrect.

---

# PHASE 17 — BUILT-PLAYER PROOF

Build the actually available target.

If Windows x64 module is unavailable:
`Windows x64: NOT RUN — module unavailable`

Do not install it automatically.

A macOS non-development build is acceptable on the current machine.

Built-player proof should cover where practical:
1. Main Menu
2. Help/Codex
3. Aim Assist OFF in Settings, then actual run with no aim bending
4. Aim Assist ON restores previous behavior
5. Shelter Trader presentation
6. enter dungeon
7. world substrate visible in small room
8. base pickup attraction
9. Magnetic Coil extended attraction
10. grenade quick-use
11. status-effect icon appears/expires
12. low-ammo prompt triggers once
13. boss HUD/readability
14. representative audio in Shelter/combat/boss
15. settings persistence after reload
16. no regressions in D1 ammo / Field Knife / blaster tuning

Capture screenshots for visual proof.
Capture runtime audio diagnostics for audio proof.

---

# PHASE 18 — OWNER PLAYTEST CHECKLIST

Create:
`TestResults/PresentationAudioUxQol/HUMAN_PLAYTEST_CHECKLIST.md`

Use approximately 15–20 diagnostic checks:
- Does the dungeon feel like one coherent underground space rather than rooms floating in black?
- Is the substrate visible without distracting from walkable floor?
- Are combat sounds less fatiguing after several minutes?
- Are player shots / enemy shots / hits / pickups easy to distinguish?
- Does music sit under combat rather than fight it?
- Does Shelter Trader now feel as understandable as Dungeon Merchant?
- Can you see active timed buffs without HUD clutter?
- Does pickup attraction remove annoying micro-interactions without vacuuming loot from far away?
- Does Magnetic Coil still feel meaningfully stronger?
- Is grenade quick-use natural?
- Does Aim Assist OFF actually feel fully manual?
- Does Aim Assist ON feel unchanged?
- Does the low-ammo prompt teach without nagging?
- Is the Codex useful without feeling like homework?
- Is the boss HUD clean?
- Are damage numbers readable?
- Are open/locked doors obvious?
- Did any prop dressing reduce combat readability?
- Did any accepted art/system feel accidentally changed?

Do not ask generic “is it fun?” questions.

---

# PHASE 19 — REQUIRED REPORT

Create:
`production/PRESENTATION_AUDIO_UX_QOL_REPORT.md`

Required structure:

# RUINRAIL — PRESENTATION / AUDIO / UX-QOL REPORT

## 1. Executive Summary
## 2. Current-State Reverification
## 3. Frozen Owner-Approved Systems
## 4. World Substrate / Black-Void Fix
## 5. Audio Mix Before
## 6. Audio Mix Changes
## 7. Repetition / Variation Policy
## 8. Music / Ambience Loop Handling
## 9. Shelter Trader Presentation
## 10. Status-Effect Display
## 11. Pickup Attraction
## 12. Grenade Quick-Use
## 13. Aim-Assist Setting
## 14. Low-Ammo Prompt
## 15. Help / Codex
## 16. HUD / Bossbar / Readability
## 17. Prop Dressing
## 18. Validator / Contract Changes
## 19. Settings Compatibility
## 20. Frozen-System Verification
## 21. Automated Tests
## 22. Built-Player Evidence
## 23. Human Playtest Checklist
## 24. Known Pre-Existing Flakes
## 25. Files Changed
## 26. Deferred / External Content Needs
## 27. Final Status

Clearly distinguish:
- implemented
- already fixed / stale review finding
- deliberately deferred
- external asset/content dependency
- blocked

---

# REQUIRED ARTIFACTS

Create:
- `production/PRESENTATION_AUDIO_UX_QOL_REPORT.md`
- `TestResults/PresentationAudioUxQol/current_state_before.csv`
- `TestResults/PresentationAudioUxQol/world_substrate_matrix.csv`
- `TestResults/PresentationAudioUxQol/audio_mix_before.csv`
- `TestResults/PresentationAudioUxQol/audio_mix_after.csv`
- `TestResults/PresentationAudioUxQol/shelter_trader_matrix.csv`
- `TestResults/PresentationAudioUxQol/status_effect_matrix.csv`
- `TestResults/PresentationAudioUxQol/pickup_attraction_matrix.csv`
- `TestResults/PresentationAudioUxQol/grenade_quick_use_matrix.csv`
- `TestResults/PresentationAudioUxQol/aim_assist_setting_matrix.csv`
- `TestResults/PresentationAudioUxQol/ammo_prompt_matrix.csv`
- `TestResults/PresentationAudioUxQol/codex_matrix.csv`
- `TestResults/PresentationAudioUxQol/readability_matrix.csv`
- `TestResults/PresentationAudioUxQol/prop_dressing_matrix.csv`
- `TestResults/PresentationAudioUxQol/frozen_systems_check.csv`
- `TestResults/PresentationAudioUxQol/HUMAN_PLAYTEST_CHECKLIST.md`
- relevant screenshots / runtime audio diagnostics / logs

---

# NON-GOALS

Do NOT in this pass:
- rebalance weapons
- change Field Knife
- change blaster tuning
- change D1 ammo tuning
- change ammo caps
- change enemy HP/damage scaling
- change boss HP/damage
- change post-D30 reward curve
- change boss attack selection
- change boss anti-kite logic
- change elite frequency
- change room depth gating
- change biome combat identity
- add new enemies
- add new bosses
- add new rooms
- redesign character art
- redesign weapon art
- redesign VFX
- redesign graphical inventory
- redesign Main Menu
- redesign Dungeon Merchant
- redesign progression
- alter run/depth health rules
- add enemy kill drops
- add start-at-depth
- add checkpoints
- restructure multiplayer
- connect live UGS
- broadly refactor `ExpeditionScene`
- rewrite audio architecture
- create a new soundtrack from scratch

These belong to later work.

---

# FINAL SUCCESS CONDITIONS

This task is COMPLETE only if:

1. Old review findings were reverified against the current tree.
2. All three biomes have a coherent non-walkable substrate/backdrop solution.
3. Small-room black-void presentation is materially improved.
4. Audio events have an authored category-based mix.
5. Frequent repeated SFX have explicit variation/throttle policy.
6. Existing audio runtime/device/settings behavior remains correct.
7. Shelter Trader presentation is clear and comparable to Dungeon Merchant quality.
8. Runtime timed effects have honest visible HUD feedback.
9. Base pickup attraction is small and Magnetic Coil remains meaningfully stronger.
10. Grenade quick-use works through the existing authoritative consumable path.
11. Aim Assist can be disabled and defaults to ON.
12. Low-ammo teaching is contextual and non-spammy.
13. Help/Codex documents only current real game rules.
14. Remaining HUD/bossbar/readability/truncation issues that still exist are fixed.
15. Prop-dressing variety improves without affecting gameplay.
16. Settings remain backward compatible.
17. Validators enforce the new contracts honestly.
18. Frozen systems remain unchanged.
19. Full relevant test/validator gates are reported truthfully.
20. Available-platform non-development build succeeds.
21. Built-player/runtime proof exists.
22. Human playtest checklist exists.
23. No unrelated redesign occurred.

If complete, print exactly:

`PRESENTATION_AUDIO_UX_QOL_COMPLETE`

and:

`production/PRESENTATION_AUDIO_UX_QOL_REPORT.md`

If a repository-local requirement remains unresolved, print:

`PRESENTATION_AUDIO_UX_QOL_INCOMPLETE`

and list the exact blockers.

External content needs or unavailable platform modules must be reported truthfully and must not be disguised as completed work.
