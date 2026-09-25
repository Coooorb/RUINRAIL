# RUINRAIL — FINAL PRODUCTION / RELEASE AUDIT & FIX PASS

## ROLE

You are performing the final production/release audit and fix pass for RUINRAIL.

This is the final consolidation task after the completed gameplay-truth, balance, depth/retention, presentation/audio/UX and co-op runtime-composition work.

This task has two responsibilities:

1. Verify the CURRENT repository and built player as a release candidate.
2. Fix any repository-local defects found during that verification without broadening scope into new features or redesign.

This is NOT another design pass.
This is NOT a content-expansion pass.
This is NOT a speculative refactor pass.

The standard is:

> If RUINRAIL is claimed to be ready, every critical player-facing loop and production gate must be exercised end-to-end in the current tree and in an available non-development build, with known flaky gates either made deterministic or explicitly removed as release gates for a principled reason.

Work autonomously.
Do not pause for approval.
Do not ask intermediate questions.
Do not stop after analysis.
Do not stop at “tests are green” if the built-player evidence is incomplete.
Inspect the current working tree and all current production reports before making changes.

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

Do not clean unrelated generated artefacts merely for aesthetics.

If you need to undo your own local edit, restore it manually from content actually inspected during this task.

---

# CURRENT PRODUCT DECISIONS / FROZEN STATE

The following are accepted unless a literal release-blocking regression is found:

## Core gameplay
- D1 ammo fine-tuning
- Field Knife balance
- blaster fine-tuning
- all other current weapon balance
- all ammo caps
- D1–D30 difficulty scaling
- current post-D30 difficulty scaling
- current post-D30 reward continuation
- current boss HP/damage values
- boss seeded attack selection
- boss anti-kite reposition logic
- elite frequency
- room depth gating
- biome gameplay identity
- run-start HP rules
- depth-arrival HP rules
- death / extraction rules
- shared/approved co-op scaling values
- current starter kit

## Progression / items
- PlayerStats aggregation
- AffixRegistry
- WeaponStatMath
- StatConsumerIntegrity
- progression attribute effects
- save/persistence architecture
- item affix behavior
- current loot economy
- current merchant pricing

## Presentation / UX
- player/character art
- weapon art
- VFX
- graphical inventory
- Main Menu identity
- Dungeon Merchant
- Shelter Trader
- HUD structure
- minimap
- world substrate
- current audio mix
- music-loop correction
- status-effect display
- pickup attraction
- grenade quick-use
- aim-assist default/strength
- low-ammo teaching prompt
- Help/Codex
- prop dressing

## Multiplayer
- co-op remains V1
- 1–3 players
- PvE only
- no PvP
- no matchmaking
- no dedicated servers
- no host migration unless already approved/implemented
- current host-authority model
- current loot/economy ownership semantics
- current reconnect semantics
- current Transit voting semantics
- completed runtime composition from MD3/MD3-completion

Do not rebalance or redesign these because a synthetic metric looks imperfect.

---

# PRIMARY OBJECTIVE

By the end of this pass, RUINRAIL must have one trustworthy release-candidate status with:

1. no known repository-local critical gameplay blocker
2. no known dead runtime seam for a shipping player-facing system
3. deterministic production validation
4. deterministic or acceptably bounded smoke gates
5. full Solo end-to-end built-player proof
6. full Duo end-to-end real-peer proof
7. Trio runtime proof
8. clean save/load/migration behavior
9. clean fresh-profile and returning-profile behavior
10. stable depth transitions
11. clean death/return/extraction paths
12. settings/input/controller coverage
13. release build success on available platform
14. truthful status for unavailable external services/platforms
15. consistent production documentation

---

# PHASE 1 — RECONCILE THE CURRENT PRODUCTION STATE

Read the current repository and current reports before changing anything.

At minimum inspect:
- latest final MVP/completion report
- stat consumer report
- fresh-run combat/economy report
- D1 ammo/blaster fine-tuning report
- run variety/depth/retention/boss report
- presentation/audio/UX-QoL report
- co-op runtime composition report
- co-op runtime composition completion report
- latest test results
- latest validator outputs
- latest built-player smoke results
- latest build logs
- current open-decisions/scope docs
- current multiplayer docs
- current release target docs

Create:
`TestResults/FinalReleaseAudit/current_state_reconciliation.csv`

Columns:
- Area
- LatestClaim
- CurrentObservedState
- Consistent YES/NO
- StaleDoc YES/NO
- RequiredAction
- Evidence

The purpose is to identify stale reports and contradictory status before declaring release readiness.

---

# PHASE 2 — FINAL DEAD-SEAM / COMPOSITION AUDIT

The project has repeatedly found the same bug class:

> system exists, tests exist, but no shipping runtime caller composes it.

Perform one final targeted release-path audit for every player-facing subsystem.

This is NOT a broad code-style scan.

For each shipping subsystem record:
- provider / service / adapter exists
- composition caller exists
- runtime consumer exists
- release path reaches it
- PlayMode consequence test exists
- built-player consequence exists where practical

At minimum cover:
- PlayerStats / SkillStatSource
- AffixRegistry
- Ammo capacity provider
- weapon stat consumers
- knockback/stagger
- grenades / GrenadeLauncher
- pickup attraction
- aim assist setting
- status effects
- deepest-depth persistence
- post-D30 reward multiplier
- boss attack selection
- boss anti-kite behavior
- biome weighting
- room depth gating
- world substrate
- audio service / event assets
- music state
- Shelter Trader
- Help/Codex
- PlayerPresenceService
- player network entity factory
- NetworkDungeonSync
- EnemyNetSync
- LootAuthority
- PartyLifeRoster
- PartyReviveAuthority
- reconnect
- Transit vote
- shared non-combat/event authority
- save/extraction transaction path

Create:
`TestResults/FinalReleaseAudit/runtime_composition_matrix.csv`

Any player-facing shipping feature with no real runtime caller is a blocker.

---

# PHASE 3 — FRESH PROFILE END-TO-END

Use a brand-new profile with no prior progress.

Prove:
1. boot
2. Main Menu
3. create profile
4. Shelter
5. starter kit
6. Character Station available
7. Storage works
8. Shelter Trader works
9. Multiplayer Terminal does not break Solo
10. enter Solo expedition
11. D1 generation
12. combat
13. ammo economy
14. inventory
15. weapon switching
16. grenade quick-use if grenade acquired
17. timed status display
18. pickup attraction
19. Merchant
20. non-combat room
21. elite if encountered / deterministic fixture otherwise
22. boss
23. Transit
24. RETURN
25. save
26. return to Shelter
27. banked state correct
28. relaunch
29. profile restored

Create:
`TestResults/FinalReleaseAudit/fresh_profile_end_to_end.csv`

No debug-only setup may be required for the core path.

---

# PHASE 4 — RETURNING PROFILE / PROGRESSION END-TO-END

Use a non-fresh profile containing:
- XP
- levels
- skill points
- upgraded attributes
- banked coins
- stored items
- equipped gear
- deepest depth record
- tutorial persistence
- settings persistence

Verify:
- profile loads
- all six attributes produce their intended runtime effect
- level/cap rules correct
- Character Station previews correct
- equipped item affixes apply
- storage round-trip
- banked coins correct
- deepest depth monotonic
- no duplicated items
- no reset of settings/tutorial state
- no migration churn on every boot

Create:
`TestResults/FinalReleaseAudit/returning_profile_end_to_end.csv`

---

# PHASE 5 — SAVE / LOAD / MIGRATION / CORRUPTION SAFETY

Run the strongest persistence coverage.

At minimum:
- fresh save
- returning save
- older supported save versions
- migration chain
- atomic temp/current/backup recovery
- malformed save quarantine
- duplicate item-id rejection/recovery
- negative invalid value rejection
- interrupted save simulation where supported
- save after RETURN
- save after death
- save after progression purchase
- save after Shelter transaction
- save after co-op extraction
- reconnect/session state not written as permanent profile state unless explicitly designed

Create:
`TestResults/FinalReleaseAudit/save_integrity_matrix.csv`

No silent inventory loss.
No silent profile wipe.
No duplicated grants.

---

# PHASE 6 — SOLO FULL-RUN BUILT-PLAYER PROOF

Run an actual non-development built player.

Use deterministic seeds where helpful.

Prove in the built player:
- boot
- menu
- profile
- Shelter
- expedition
- D1 combat
- inventory
- ammo
- Merchant
- event/non-combat room
- boss
- Transit
- at least one DESCEND
- next depth
- RETURN
- save/reload
- one death/wipe path
- one settings change
- controller or controller-simulated navigation path if hardware is unavailable
- no exception
- no missing reference
- no missing script
- no softlock

Create:
`TestResults/FinalReleaseAudit/solo_built_player_proof.csv`

---

# PHASE 7 — CO-OP FINAL END-TO-END PROOF

Use the completed MD3 runtime composition.

Do NOT accept pure mocks.

## Duo
A real host/client pair must prove:
- Host
- Join
- ready/start
- same dungeon
- local ownership correct
- remote visuals correct
- combat
- enemy authority
- loot race
- Merchant/event request
- Downed
- Revive
- boss
- Transit vote
- D1 → D2
- state preservation
- RETURN
- extraction/save
- clean session exit

## Trio
Prove:
- 3 identities
- 3 player entities
- same dungeon
- authored trio scaling
- combat
- shared encounter
- loot race
- revive
- vote
- one depth transition
- no duplicate local presentation
- no missing party member

Create:
`TestResults/FinalReleaseAudit/coop_end_to_end.csv`

If local real-peer proof no longer passes, release status is blocked.

---

# PHASE 8 — CONTROLLER / INPUT / SETTINGS AUDIT

Verify all current input actions and settings.

At minimum:

## Input
- move
- aim
- fire
- reload
- weapon switch
- dash
- interact
- active consumable
- Quick Grenade
- inventory
- pause
- navigation/back/confirm
- any current map-specific action

## Rebinding
- keyboard/mouse
- gamepad
- conflict detection
- reset defaults
- persistence

## Settings
- Master
- Music
- SFX
- Ambience
- mute
- resolution
- display mode
- VSync
- frame rate limit
- screen shake
- shake intensity
- damage numbers
- hit flash
- tutorial prompts
- Aim Assist
- any current accessibility settings

Create:
`TestResults/FinalReleaseAudit/input_settings_matrix.csv`

No action may exist without a label/glyph/rebind path unless deliberately non-rebindable and documented.

---

# PHASE 9 — UI / NAVIGATION / REFERENCE-RESOLUTION AUDIT

Verify all shipping screens at the 640×360 reference.

At minimum:
- Main Menu
- profile creation
- Shelter
- Character Station
- Storage
- Shelter Trader
- Multiplayer Terminal
- Settings
- Controls/Rebinding
- Help/Codex
- in-run HUD
- inventory
- Dungeon Merchant
- Weapon Cache
- event choices
- pause
- Run Lost
- Return/extraction summary
- Transit voting
- co-op party HUD

Check:
- no clipping
- no overlapping controls
- no text outside panels
- no inaccessible focus target
- no focus trap
- mouse/keyboard/controller parity
- no stale data after reopening
- no incorrect rarity/glyph/icon
- no accidental fallback font
- no black rectangle/layout artefact
- readable status/boss/depth info

Also validate at:
- 16:9 scale-up
- one supported non-16:9 shape

Create:
`TestResults/FinalReleaseAudit/ui_navigation_matrix.csv`

---

# PHASE 10 — AUDIO / PRESENTATION FINAL AUDIT

Verify current Presentation/Audio pass still holds.

At minimum:
- no raw black void in small rooms
- substrate below gameplay floor
- no substrate collider
- no prop obstruction
- one AudioListener
- category mix present
- high-frequency SFX variation/throttle present
- music loop alignment
- ambience continuity
- Shelter audio
- exploration audio
- combat audio
- boss audio
- Merchant/UI audio
- output-device recovery
- volume persistence

Create:
`TestResults/FinalReleaseAudit/presentation_audio_matrix.csv`

Do not start a new audio-content production pass.

---

# PHASE 11 — COMBAT / BALANCE REGRESSION AUDIT

This is a regression check, not a rebalance pass.

Verify:
- all 33 weapon definitions
- weapon stat consumers
- fire rate
- magazine
- projectile speed
- projectile range
- shotgun spread
- melee attack timing
- blaster heat
- bow charge
- knockback/stagger
- reload
- auto reload
- pointblank collision
- aim assist ON/OFF
- remote weapon presentation in co-op
- starter P9
- Field Knife
- D1 ammo tuning
- current blaster tuning

Create:
`TestResults/FinalReleaseAudit/combat_regression_matrix.csv`

Any unexpected changed value is a defect unless directly required to fix a release blocker.

---

# PHASE 12 — DEPTH / ECONOMY / RETENTION REGRESSION AUDIT

Verify the accepted current contract:
- D1–D30 difficulty unchanged
- current D31+ difficulty unchanged
- post-D30 reward continuation starts only after D30
- deepest-depth persistence
- elite frequency
- room depth gating
- biome identity weighting
- boss seeded selection
- boss anti-kite
- current Merchant prices
- current coin rules
- current XP rules
- current rarity rules
- current RETURN/DESCEND behavior
- current depth-arrival heal

Create:
`TestResults/FinalReleaseAudit/depth_economy_regression_matrix.csv`

No new balance tuning.

---

# PHASE 13 — NON-COMBAT / EVENT REGRESSION AUDIT

Exercise every current non-combat/event type in Solo and, where shared, in co-op.

At minimum current systems such as:
- Merchant
- Medical Station
- Broken Machine
- Locked Vault
- Weapon Cache
- Treasure/Loot room
- Transit
- any other shipping event type

Verify:
- prompt
- interaction
- choice
- cost
- grant
- one-time resolution
- save state
- network authority where applicable
- no duplicate reward
- no blocked progression
- no focus/UI deadlock

Create:
`TestResults/FinalReleaseAudit/noncombat_regression_matrix.csv`

---

# PHASE 14 — DEATH / EXTRACTION / LOSS-RULE AUDIT

Prove:

## Solo death
- carried risk state lost as designed
- persistent progression retained
- Run Lost UI correct
- save correct

## Solo RETURN
- carried loot/coins secure correctly
- summary correct
- save correct

## Co-op partial death
- downed/revive semantics
- dead player semantics
- surviving party continues where designed

## Co-op wipe
- run ends once
- no duplicate loss transaction

## Co-op RETURN
- extraction commits once
- all peers transition coherently
- persistence correct

Create:
`TestResults/FinalReleaseAudit/death_extraction_matrix.csv`

---

# PHASE 15 — PERFORMANCE / LEAK / LONG-RUN AUDIT

Repeat production performance checks after all recent systems.

Measure:
- dungeon generation time
- depth rebuild time
- Mono/managed heap growth
- GameObject counts
- pooled object growth
- network object growth
- listener count
- camera count
- event subscription growth
- save time
- load time
- UI open/close churn
- 10+ depth transitions where practical
- Solo
- Duo
- Trio representative samples

Create:
`TestResults/FinalReleaseAudit/performance_longrun.csv`

Do not optimize healthy systems just to change numbers.

Fix only meaningful regressions/leaks.

---

# PHASE 16 — SMOKE FLAKINESS ELIMINATION

This is a major objective of the final pass.

The built-player smoke has historically shown seed/timing sensitivity.

Known examples from prior passes include:
- direct crosshair-on-enemy projectile check
- containment overshoot timing
- non-combat room absence under a seed
- other seed-dependent ordered-stage assumptions

Do NOT merely rerun until green and call that a release gate.

For every flaky stage:
1. reproduce across many runs
2. determine whether it is:
   - real product nondeterminism
   - test seed nondeterminism
   - timing/physics settling nondeterminism
   - ordered-stage coupling
   - environment-only issue
3. fix the root cause where repository-local
4. redesign the TEST if it asserts a seed-dependent precondition that is not guaranteed
5. preserve strict product invariants
6. do not widen frozen gameplay tolerances merely to make the test pass

## Required target

A deterministic release smoke must pass at least:
- 10 consecutive Solo runs on the same deterministic release-smoke seed/setup
- without manual retries
- without changing repository state between runs

If the smoke supports Duo:
- 5 consecutive Duo release-smoke runs on the same deterministic setup

If a full ordered mega-smoke remains structurally unsuitable, split it into deterministic independent release smoke scenarios rather than retaining a known 1-in-3 gate.

Do not delete coverage.

Create:
`TestResults/FinalReleaseAudit/smoke_stability.csv`

Columns:
- Scenario
- Runs
- Passes
- Failures
- Seed
- RootCause
- Fix
- FinalDeterministic YES/NO

This phase must materially improve trust in the release gate.

---

# PHASE 17 — RELEASE-SMOKE STRUCTURE

Audit the current `SmokeRunner`.

If its ordered monolithic structure causes unrelated stages to mask later coverage, perform the SMALLEST test-harness-only refactor needed for reliable release validation.

Allowed:
- independent deterministic smoke scenarios
- per-stage explicit seed
- stage precondition setup
- fresh save dir per run
- explicit logs/result files
- scenario-level timeouts
- aggregation report

Not allowed:
- gameplay redesign
- loosening product assertions
- hiding failures
- “retry until pass” as the official result

Create:
`TestResults/FinalReleaseAudit/release_smoke_manifest.csv`

Each shipping critical path should map to at least one deterministic scenario.

---

# PHASE 18 — BUILD PIPELINE / RELEASE TARGET AUDIT

Inspect the actual release build path.

Verify:
- Unity version remains `6000.3.24f1`
- approved scenes in correct order
- release build uses non-development options
- no missing script/reference warnings
- no accidental test scene
- content generation step succeeds
- audio/art generation only touches intended assets
- network prefabs registered in release build
- no debug-only service substituted
- no in-memory fake networking in release online path
- settings defaults correct
- version/build metadata available if project supports it

## Platform

Windows x64 is the declared release target.

If Windows module is unavailable on the current macOS machine, report exactly:

`Windows x64: NOT RUN — module unavailable`

Do not install automatically.

Use macOS Standalone as local built-player verification substitute.

Create:
`TestResults/FinalReleaseAudit/build_pipeline_matrix.csv`

---

# PHASE 19 — LIVE SERVICES STATUS

Live UGS Sessions/Relay is an external configuration dependency.

If configured:
- run real host/join-code integration
- preserve logs

If not configured, report exactly:

`Live UGS Sessions/Relay: NOT RUN — project/service configuration unavailable`

Do not fake success.
Do not install credentials.
Do not call it a repository-local failure if the local real network runtime is complete.

Also verify that the game reports an understandable error when live services are unavailable.

Create:
`TestResults/FinalReleaseAudit/external_services_status.csv`

---

# PHASE 20 — PRODUCTION DOCUMENTATION CONSISTENCY

The repository has accumulated many production reports.

Do not delete historical reports.

Identify stale CURRENT-status documents that contradict the actual tree.

Examples may include:
- old audio asset audit claiming missing audio
- old final MVP report generated before later passes
- old co-op status claiming solo-only
- open-decisions docs that no longer match current decisions

Update only documents intended to represent CURRENT status.

Historical task reports should remain historical.

Create:
`TestResults/FinalReleaseAudit/documentation_consistency.csv`

Columns:
- Document
- IntendedAsCurrent YES/NO
- Stale YES/NO
- Contradiction
- Action

No status file should claim both “co-op not connected” and “co-op complete” as current truth.

---

# PHASE 21 — FINAL VALIDATOR

Add or extend one final validator:

`FinalReleaseCandidateValidator`
or repository-consistent equivalent.

It should aggregate/protect critical release contracts without duplicating every test.

At minimum fail on:
- missing required scenes
- wrong content counts
- missing runtime consumer/composition for approved player-facing systems
- broken save version/migration
- invalid starter kit
- missing player prefab registration
- missing enemy network registration
- co-op runtime composition absent
- more than one AudioListener composition site
- placeholder UI font
- missing required UI screen
- invalid settings action/glyph count
- broken world substrate
- invalid audio event policy
- missing boss/room/biome contracts
- missing deepest-depth field
- D1 ammo drift
- Field Knife drift
- blaster drift
- frozen scaling drift
- smoke manifest missing critical scenarios
- stale release-current status file marker if such a mechanism exists

A deliberately broken fixture must fail.

---

# PHASE 22 — FINAL FROZEN-VALUE SNAPSHOT

Create:
`TestResults/FinalReleaseAudit/frozen_release_snapshot.csv`

Record the final shipping values/contracts for future comparison.

At minimum:
- player base HP
- dash values
- ammo caps
- starter kit
- D1 Supply Light range
- all 33 weapon core fingerprints
- Field Knife
- 3 blaster heat values
- boss HP fingerprints
- depth scaling samples
- post-D30 reward formula/cap
- elite frequency samples
- room-gating count
- biome weighting fingerprint
- max level
- attribute caps
- backpack size
- player count max
- co-op scaling values
- revive/bleedout values
- pickup attraction base
- Magnetic Coil bonus
- aim assist angles
- audio event count
- room count
- item count
- weapon count
- enemy/elite/boss counts

This is not a new balance spec.
It is a final regression snapshot.

---

# PHASE 23 — FULL AUTOMATED GATES

Run all current suites after the last source/data change.

At minimum:
- full PlayMode
- full EditMode
- FinalProductionValidator
- ContentCountValidator
- StatConsumerIntegrityValidator
- RunVarietyDepthRetentionValidator
- PresentationAudioUxQolValidator
- Co-op runtime validator
- final release candidate validator
- save/migration tests
- inventory tests
- merchant tests
- settings tests
- UI navigation tests
- combat tests
- audio tests
- dungeon generation tests
- boss tests
- economy tests
- non-combat tests
- co-op authority tests
- reconnect tests
- revive tests
- transit tests
- network sync tests
- solo regression tests
- final deterministic smoke suites

Report skipped/environment-gated tests explicitly.

No failed test may be hidden by rerun-only reporting.

---

# PHASE 24 — FINAL BUILT-PLAYER RELEASE PROOF

After all fixes/tests:

Build a fresh non-development player.

Run:

## A. Solo Release Scenario
- fresh save dir
- deterministic seed
- D1
- boss
- descend
- D2
- return
- save/reload

## B. Solo Death Scenario
- fresh/known save
- deliberate death
- loss/persistence check

## C. Returning Profile Scenario
- progress
- attribute effects
- storage
- trader
- re-enter run

## D. Duo Release Scenario
- real host/client
- combat
- loot
- revive
- boss
- vote
- descend
- return

## E. Trio Release Scenario
- real 3-peer
- composition
- scaling
- combat
- vote
- transition

All scenario result files must be archived under:
`TestResults/FinalReleaseAudit/built_player/`

---

# PHASE 25 — OWNER MANUAL RELEASE CHECKLIST

Create:
`TestResults/FinalReleaseAudit/OWNER_RELEASE_CHECKLIST.md`

This should be concise enough to actually use.

Approximately 30–40 checks, grouped into:
- Boot/Profile
- Shelter
- Combat
- Loot/Inventory
- Boss/Transit
- Death/Extraction
- Presentation/Audio
- Settings/Controller
- Duo
- Trio
- Long-run feel

Questions should be observational, not generic.

Examples:
- Does the first D1 run feel slightly ammo-constrained without feeling starved?
- Does the Field Knife still feel like a useful secondary rather than a forced primary?
- Do Blasters feel smoother but still heat-limited?
- Do rooms no longer float in raw black?
- After 5–10 minutes, is the mix less fatiguing?
- Does opening Inventory in co-op leave the world running?
- Does a client see the same enemy/door/loot outcome as the host?
- Does revive feel readable and fair?
- Does Transit make every player’s vote state obvious?
- After D1→D2, does each peer still have the correct inventory/ammo/equipment?
- Does RETURN secure exactly what you expect?
- Does anything previously accepted feel accidentally changed?

---

# PHASE 26 — FINAL REPORT

Create:
`production/FINAL_RELEASE_CANDIDATE_AUDIT.md`

Required structure:

# RUINRAIL — FINAL RELEASE CANDIDATE AUDIT

## 1. Final Status
## 2. Reviewed Repository State
## 3. Current-Report Reconciliation
## 4. Runtime Composition / Dead-Seam Audit
## 5. Fresh Profile End-to-End
## 6. Returning Profile / Progression
## 7. Save / Migration / Recovery
## 8. Solo Built-Player Proof
## 9. Duo Co-op Proof
## 10. Trio Co-op Proof
## 11. Controller / Input / Settings
## 12. UI / Navigation / Resolution
## 13. Presentation / Audio
## 14. Combat Regression
## 15. Depth / Economy Regression
## 16. Non-Combat / Events
## 17. Death / Extraction
## 18. Performance / Leak / Long-Run
## 19. Smoke Flakiness Before
## 20. Smoke Stabilization
## 21. Final Smoke Stability
## 22. Build Pipeline
## 23. External Services
## 24. Documentation Consistency
## 25. Final Validator
## 26. Frozen Release Snapshot
## 27. Automated Test Results
## 28. Built-Player Scenarios
## 29. Owner Manual Checklist
## 30. Known Non-Blocking External Limitations
## 31. Known Repository-Local Issues
## 32. Files Changed
## 33. Final Release Status

The report must clearly distinguish:
- PASS
- FIXED DURING AUDIT
- NOT RUN — ENVIRONMENT
- EXTERNAL CONTENT LIMITATION
- NON-BLOCKING KNOWN ISSUE
- RELEASE BLOCKER

Do not label something PASS if it was only inferred.

---

# REQUIRED ARTIFACTS

Create at minimum:
- `production/FINAL_RELEASE_CANDIDATE_AUDIT.md`
- `TestResults/FinalReleaseAudit/current_state_reconciliation.csv`
- `TestResults/FinalReleaseAudit/runtime_composition_matrix.csv`
- `TestResults/FinalReleaseAudit/fresh_profile_end_to_end.csv`
- `TestResults/FinalReleaseAudit/returning_profile_end_to_end.csv`
- `TestResults/FinalReleaseAudit/save_integrity_matrix.csv`
- `TestResults/FinalReleaseAudit/solo_built_player_proof.csv`
- `TestResults/FinalReleaseAudit/coop_end_to_end.csv`
- `TestResults/FinalReleaseAudit/input_settings_matrix.csv`
- `TestResults/FinalReleaseAudit/ui_navigation_matrix.csv`
- `TestResults/FinalReleaseAudit/presentation_audio_matrix.csv`
- `TestResults/FinalReleaseAudit/combat_regression_matrix.csv`
- `TestResults/FinalReleaseAudit/depth_economy_regression_matrix.csv`
- `TestResults/FinalReleaseAudit/noncombat_regression_matrix.csv`
- `TestResults/FinalReleaseAudit/death_extraction_matrix.csv`
- `TestResults/FinalReleaseAudit/performance_longrun.csv`
- `TestResults/FinalReleaseAudit/smoke_stability.csv`
- `TestResults/FinalReleaseAudit/release_smoke_manifest.csv`
- `TestResults/FinalReleaseAudit/build_pipeline_matrix.csv`
- `TestResults/FinalReleaseAudit/external_services_status.csv`
- `TestResults/FinalReleaseAudit/documentation_consistency.csv`
- `TestResults/FinalReleaseAudit/frozen_release_snapshot.csv`
- `TestResults/FinalReleaseAudit/OWNER_RELEASE_CHECKLIST.md`
- `TestResults/FinalReleaseAudit/built_player/` scenario logs/results/captures

---

# NON-GOALS

Do NOT use this final pass to:
- add new weapons
- add new enemies
- add new bosses
- add new rooms
- add new biomes
- add new progression trees
- add start-at-depth
- add PvP
- add matchmaking
- add dedicated servers
- add host migration
- add voice chat
- redesign character art
- redesign weapon art
- redesign VFX
- rewrite inventory
- rebalance D1–D30
- rebalance Field Knife
- rebalance Blasters
- rebalance bosses
- rebalance ammo caps
- redesign co-op economy
- create a new soundtrack
- perform general architecture beautification
- rewrite save architecture
- rewrite networking architecture
- fix external Unity Services configuration automatically
- install missing Windows build support automatically

Only fix concrete release defects.

---

# RELEASE-BLOCKER POLICY

A repository-local issue is a RELEASE BLOCKER if it can cause any of:
- crash
- softlock
- corrupted save
- lost/duplicated permanent progression
- duplicated/lost extraction transaction
- broken fresh-profile path
- broken returning-profile path
- broken Solo core loop
- broken Duo core loop
- broken required Trio composition
- player-facing shipping system with no runtime caller
- client authority exploit affecting items/coins/combat/progression
- deterministic generation divergence between peers
- broken depth transition
- broken boss completion
- broken death/wipe/return
- uncontrolled memory/object growth
- release build failure
- nondeterministic official smoke gate that cannot be trusted

Non-blocking external limitations may include:
- Live UGS/Relay not configured in the current environment
- Windows x64 module unavailable on current macOS machine
- lack of newly authored external alternative SFX clips
- lack of longer newly composed music
- optional hand-authored replacement substrate art

These must be reported truthfully.

---

# FINAL SUCCESS CONDITIONS

This task is COMPLETE only if:

1. Current status documents are reconciled.
2. No shipping player-facing system has a dead runtime composition seam.
3. Fresh-profile end-to-end passes.
4. Returning-profile/progression end-to-end passes.
5. Save/migration/recovery passes.
6. Solo built-player core loop passes.
7. Duo real-peer full loop passes.
8. Trio real-peer runtime proof passes.
9. Controller/input/settings coverage passes.
10. UI/navigation/reference-resolution coverage passes.
11. Presentation/audio regression coverage passes.
12. Combat frozen-value regression passes.
13. Depth/economy frozen-value regression passes.
14. All non-combat/event types work.
15. Death/extraction rules pass.
16. Long-run performance has no meaningful leak/regression.
17. Official release smoke is deterministic enough to be trusted.
18. Solo smoke passes at least 10 consecutive deterministic runs without manual retries.
19. Duo release smoke passes repeated deterministic runs if the local harness supports it.
20. No tolerance was loosened solely to make a flaky test green.
21. Release build succeeds on an available platform.
22. Windows status is reported truthfully.
23. Live UGS/Relay status is reported truthfully.
24. Final production docs do not contradict the current tree.
25. Final release validator passes.
26. Full EditMode passes except explicit/environment-gated skips.
27. Full PlayMode passes.
28. All required built-player scenarios pass.
29. Owner release checklist exists.
30. No repository-local release blocker remains.

If ALL repository-local conditions are satisfied, print exactly:

`FINAL_RELEASE_CANDIDATE_COMPLETE`

and:

`production/FINAL_RELEASE_CANDIDATE_AUDIT.md`

If any repository-local release blocker remains, print exactly:

`FINAL_RELEASE_CANDIDATE_BLOCKED`

and list the exact blocker(s).

Do not print COMPLETE if the official smoke still requires “rerun until green”.
Do not print COMPLETE if full Duo co-op no longer works.
Do not print COMPLETE if a known player-facing shipping feature is still test-only or uncomposed.
