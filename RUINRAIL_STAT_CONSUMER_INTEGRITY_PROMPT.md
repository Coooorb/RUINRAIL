# RUINRAIL — STAT CONSUMER INTEGRITY PASS

## ROLE

You are performing a tightly scoped implementation and verification pass on the current RUINRAIL Unity repository.

This pass exists because the comprehensive game review found a recurring failure mode:

> Content definitions, affixes, accessory intrinsics, or adapters can be correctly authored and tested in isolation while still having no effective runtime consumer.

A previous example was `SkillStatSource`: the system existed and tests passed, but runtime composition never instantiated it, so progression attributes had no gameplay effect.

The full-game review found the same class of problem across several item/stat systems.

Your job is to make the CURRENTLY AUTHORED item/stat content truthful.

Do not redesign the game.
Do not add unrelated systems.
Do not rebalance the whole arsenal.
Do not change biome identity, depth scaling, boss AI, audio, room generation, multiplayer architecture, or presentation in this pass.

Work autonomously.
Do not pause for approval.
Do not ask intermediate questions.

---

# REPOSITORY SAFETY

The current working tree contains a large amount of intentional uncommitted work and is authoritative.

NEVER use:

- `git checkout`
- `git restore`
- `git reset`
- `git clean`
- `git stash`
- destructive repository cleanup
- broad file reverts

Do not discard unrelated work.

If you must undo your own edit, restore the exact previous content manually and prove the diff is scoped.

---

# PRIMARY OBJECTIVE

Ensure that every stat/effect currently presented to the player through:

- accessory intrinsics
- item affixes
- relevant weapon definitions
- relevant inventory capacity modifiers

is either:

1. actually consumed by a real runtime gameplay system,
2. explicitly removed from the player-facing V1 acquisition/roll pool,
3. or explicitly documented as intentionally deferred because implementing it requires a separate design decision.

No stat is allowed to remain silently advertised while doing nothing.

The pass must also add automated protection so this failure mode cannot quietly return.

---

# PHASE 1 — RE-VERIFY THE REVIEW FINDINGS

Do NOT blindly assume the comprehensive review is correct.
Re-verify every finding against the current working tree before editing.

The review reported that the following accessory intrinsics have no real runtime consumer:

- Projectile Range
- Melee Attack Speed
- Weapon Switch Speed
- Blaster Cooling Rate
- Blaster Heat Per Shot Reduction
- Bow Charge Speed
- Projectile Speed
- Ammo Stack Capacity
- Weapon Spread Reduction
- Knockback
- Stagger Power

The review also reported dead or partially dead affix coverage across ranged, blaster, bow, melee, and accessory pools.

It further reported that:

- all 33 weapon definitions currently have `Knockback = 0`
- all 33 weapon definitions currently have `StaggerPower = 0`
- player-side impact plumbing already exists
- enemy resistance curves already exist
- Shotgun/Rocket/heavy-melee docs imply stronger impact identity
- `SetAmmoCapacityBonusProvider` exists but is only wired in tests

Verify each statement directly from code/data before changing anything.

Create:

`TestResults/StatConsumerIntegrity/dead_content_before.txt`

with every verified dead player-facing effect before fixes.

---

# PHASE 2 — BUILD A STAT CONSUMER MATRIX

Create:

`TestResults/StatConsumerIntegrity/stat_consumer_matrix.csv`

Minimum columns:

- StatId
- SourceType
- SourceDefinition
- PlayerFacingLabel
- GrantedValue
- RuntimeConsumer
- RuntimeCodePath
- EffectiveAtRuntimeBefore
- Decision
- EffectiveAtRuntimeAfter
- VerificationMethod
- Notes

`Decision` must be exactly one of:

- `CONSUME`
- `REMOVE_FROM_V1_POOL`
- `DEFER_EXPLICITLY`

Enumerate every player-facing item stat that can currently be granted by:

- accessory intrinsic
- armor intrinsic
- weapon affix
- armor affix
- accessory affix
- progression
- temporary buff
- Legendary passive where relevant

No silently dead stat is allowed.

---

# PHASE 3 — IMPLEMENT SAFE, OBVIOUS CONSUMERS

For stats whose intended behavior is already clear from existing documentation and architecture, wire them into the actual runtime systems.

Do not invent new mechanics when the intended consumer is already evident.

## Fire Rate

For relevant ranged/blaster weapons:

- modify actual shot cadence / cooldown
- preserve definition fire rate as base
- use authoritative stats
- apply caps exactly once
- do not double-apply

Runtime proof must measure actual shots over deterministic time.

## Magazine Size

For magazine weapons:

- modify effective magazine capacity
- define deterministic rounding
- never create reserve ammo
- preserve valid current magazine state
- reload logic and HUD must use the same effective capacity

Do NOT change global ammo caps in this pass.

## Projectile Speed

For projectile weapons:

- modify actual projectile motion
- preserve visual/hit synchronization
- measure real travel over time

## Projectile Range

Modify the real authoritative range/lifetime/travel limit used by gameplay.
Do not only change UI values.

Runtime proof must measure real maximum reach.

## Melee Attack Speed

Modify actual attack cadence/recovery timing.
Preserve animation/event correctness and prevent duplicate hits.

## Blaster Cooling Rate

Modify actual heat recovery.
Measure heat recovery over deterministic time.

## Blaster Heat Per Shot Reduction

Modify actual heat added per shot.
Measure heat after deterministic firing or shots-to-overheat.

## Bow Charge Speed

Modify actual time to full charge.
Measure real charge timing.

## Ammo Stack Capacity

Wire the existing capacity bonus provider into the authoritative runtime inventory path.

Requirements:

- no duplicated inventory rules
- no generated/free ammo
- effective capacity changes correctly
- pickups/transfers/merchant/save-load/starter/UI use the same rule
- unequipping/removing the effect restores the baseline safely

This is a high-priority regression target because it matches the previous "adapter exists, no runtime caller" failure mode.

---

# PHASE 4 — WEAPON SWITCH SPEED: DO NOT INVENT A MECHANIC

Current context indicates weapon switching is instantaneous and there is no authoritative switch-duration mechanic.

If that is still true:

- classify `WeaponSwitchSpeed` as `DEFER_EXPLICITLY`
- ensure no V1 player-acquirable item falsely promises a switch-speed benefit
- do NOT create an arbitrary switch delay
- do NOT silently delete the StatId if existing data references it
- preserve the current Handling progression behavior that is actually implemented

Document the design gap clearly.

---

# PHASE 5 — WEAPON SPREAD: CONSERVATIVE DECISION

Do NOT invent a new recoil/spread model.

Inspect:

- weapon-class docs
- current weapon data
- existing spread support
- `WeaponSpreadReduction`

If existing approved docs clearly define baseline spread and the implementation supports it:

- restore only documented spread behavior
- make `WeaponSpreadReduction` consume the real spread

If docs do not provide enough information:

- classify spread reduction as `DEFER_EXPLICITLY`
- remove it from V1 acquisition/roll pools until a dedicated design pass defines it
- do not invent degrees

Create:

`TestResults/StatConsumerIntegrity/spread_decision.md`

with current data, current docs, decision, and reason.

---

# PHASE 6 — KNOCKBACK / STAGGER

Do NOT set arbitrary non-zero values on all weapons.

Inspect:

- `combat/42_STAGGER_KNOCKBACK.md`
- weapon class docs
- weapon definitions
- enemy resistance curves
- Legendary/passive dependencies
- existing impact pipeline/tests
- containment/collision systems

The review reported that design intent explicitly supports stronger knockback/stagger for:

- Shotguns
- Rockets
- heavy melee

Use existing approved docs as source of truth.

If exact values are documented, use them.

If exact values are not documented:

- choose conservative values only for classes clearly intended to have impact identity
- leave ordinary low-impact weapon classes at zero unless docs say otherwise
- document every assigned value
- preserve boss/elite resistance
- preserve EncounterBounds
- preserve wall collision and anti-escape behavior

Required runtime proof:

1. intended impact-heavy weapon affects a normal low-resistance enemy
2. high-resistance enemy reduces the effect
3. elite/boss resistance behaves correctly
4. impact-heavy classes differ meaningfully from ordinary weapons
5. impact-dependent passives can trigger through valid gameplay if they are V1-live
6. no enemy escapes EncounterBounds
7. no wall/void penetration regression

Create:

`TestResults/StatConsumerIntegrity/impact_matrix.csv`

with at least:

- Weapon
- Class
- BaseKnockbackBefore
- BaseStaggerBefore
- BaseKnockbackAfter
- BaseStaggerAfter
- IntendedRole
- NormalEnemyResult
- ResistantEnemyResult
- BossResult
- PassiveInteraction
- Status

---

# PHASE 7 — REMOVE FALSE PLAYER-FACING CONTENT

For any stat classified as `REMOVE_FROM_V1_POOL` or `DEFER_EXPLICITLY`, ensure the player cannot newly acquire an item that advertises a no-op.

Valid approaches include:

- remove affix from applicable active roll pool
- make an unresolved accessory unavailable from current V1 loot acquisition
- update acquisition validators
- retain definition data for future design work

Do NOT invent replacement bonuses merely to preserve counts.

Do NOT silently reroll existing save items.

Old save compatibility must be deterministic and non-destructive.

If a legacy item contains a deferred affix:

- load it safely
- do not corrupt/delete it
- do not falsely present it as working
- document the compatibility policy

---

# PHASE 8 — NO-DEAD-CONTENT VALIDATOR

Implement a production validator, e.g.:

`StatConsumerIntegrityValidator`

It must protect against this failure class.

At minimum validate:

1. every player-acquirable accessory intrinsic
2. every active affix pool entry
3. every armor intrinsic/stat
4. every currently grantable weapon-affix stat
5. every relevant progression stat
6. every runtime adapter/provider whose behavior requires composition wiring

A player-facing granted stat must resolve to:

- a real runtime consumer
- an explicit documented exception
- or non-player-acquirable/deferred content

Do not merely grep for a `StatId` name and call that proof.

Where practical, combine:

- static mapping
- runtime integration tests
- acquisition-pool validation

Add a regression test that deliberately disconnects a consumer/provider and proves the validator fails.

The validator should be capable of catching the same general bug class as:

- historical `SkillStatSource` missing runtime composition
- current/historical Ammo Capacity provider not wired

---

# PHASE 9 — UI / TOOLTIP TRUTHFULNESS

Audit item-facing UI only as needed to make displayed stats truthful:

- graphical inventory details
- merchant details
- Shelter/loadout item details
- item comparison views

Requirements:

- every displayed intrinsic/affix corresponds to a real effect or clearly handled legacy/deferred state
- new items cannot roll known no-op affixes
- effective numerical semantics are correct
- do not redesign accepted UI layouts

The graphical inventory and Dungeon Merchant UI are frozen except for necessary truthfulness fixes.

---

# PHASE 10 — TESTS

Add deterministic tests for every newly connected consumer.

## EditMode / unit coverage

At minimum:

- FireRate modifier
- MagazineSize modifier
- ProjectileSpeed modifier
- ProjectileRange modifier
- MeleeAttackSpeed modifier
- BlasterCoolingRate modifier
- BlasterHeatPerShot modifier
- BowChargeSpeed modifier
- AmmoStackCapacity modifier
- caps
- add/remove recomputation
- no double-application
- save/load where relevant

## PlayMode / runtime integration

For every consumed stat:

1. equip/acquire a real item
2. run through the real composition root
3. measure actual gameplay change
4. remove/unequip it
5. prove baseline returns

Property inspection alone is insufficient when gameplay can be measured.

Examples:

- shots over fixed time
- projectile distance/time
- bow charge duration
- melee attacks over fixed time
- blaster heat recovery
- effective magazine capacity
- effective ammo inventory capacity

## Affix generation

For each active pool:

- deterministic large sample
- every generated affix must map to a real V1 effect
- no deferred/removed affix may appear

## Accessories

Create:

`TestResults/StatConsumerIntegrity/accessory_runtime_matrix.csv`

One row per accessory family with actual equip → effect → unequip proof.

---

# PHASE 11 — NARROW BALANCE SAFETY CHECK

This is NOT the broad weapon-balance pass.

Activating previously dead effects can still change combat significantly, so measure before/after for representative weapons:

- Pistol
- SMG
- Assault Rifle
- Shotgun
- Sniper
- Rocket
- Bow
- Blaster
- Knife
- Spear

Measure where relevant:

- burst DPS
- sustained DPS
- effective magazine
- fire rate
- reload
- range
- projectile speed
- impact behavior

Test:

- baseline/no affix
- one relevant max affix
- representative Epic combination

Create:

`TestResults/StatConsumerIntegrity/weapon_effect_before_after.csv`

Do not fix unrelated weapon dominance in this pass unless activating the stat reveals an actual arithmetic/runtime bug.

---

# PHASE 12 — REGRESSION PROTECTION

Do not regress:

- graphical inventory
- Dungeon Merchant UI
- Main Menu
- HUD
- dash behavior
- run-start full HP
- depth-arrival full HP
- character progression
- player aim/hit fixes
- projectile visuals
- enemy collision
- EncounterBounds
- room locking
- loot/chests
- death screen
- backpack reorder
- item descriptions
- Settings
- non-combat rooms
- starter loadout
- ammo resale fix
- save/load/migrations
- seeded generation

Especially verify:

- magazine effects do not mint ammo
- ammo-capacity effects do not duplicate ammo
- fire-rate effects do not bypass reload
- projectile modifiers do not desync VFX/hits
- impact cannot break containment
- legacy saves remain valid

---

# PHASE 13 — BUILD / RUNTIME PROOF

Run:

- all relevant EditMode tests
- all PlayMode tests
- all production validators
- content validators
- item-description validator
- save/load validation
- combat validation
- new StatConsumerIntegrity validator

Use deterministic seeds for scoped proof.

The known clock-seeded `CombatAimCollisionProofTests` flake must not be used as the only combat proof.
If it flakes:

- report it separately
- rerun deterministic scoped tests
- do not hide it
- do not broaden this pass into unrelated test cleanup unless required for trustworthy verification

Build the platform actually available.

If Windows support is unavailable on the current Mac machine:

`Windows x64: NOT RUN — module unavailable`

Do not install modules automatically.
Do not falsely claim Windows verification.

Run built-player smoke proving at least:

1. fresh/clean profile
2. equip at least one previously dead accessory effect
3. enter run
4. measure effect in runtime
5. obtain/roll at least one previously dead affix
6. measure effect
7. prove impact if enabled
8. prove ammo-capacity behavior if enabled
9. save
10. reload
11. prove state remains valid

---

# REQUIRED OUTPUTS

Create:

- `production/STAT_CONSUMER_INTEGRITY_REPORT.md`
- `TestResults/StatConsumerIntegrity/stat_consumer_matrix.csv`
- `TestResults/StatConsumerIntegrity/dead_content_before.txt`
- `TestResults/StatConsumerIntegrity/accessory_runtime_matrix.csv`
- `TestResults/StatConsumerIntegrity/impact_matrix.csv`
- `TestResults/StatConsumerIntegrity/weapon_effect_before_after.csv`
- `TestResults/StatConsumerIntegrity/spread_decision.md`
- runtime logs/screenshots/evidence as appropriate

Report structure:

# RUINRAIL — STAT CONSUMER INTEGRITY REPORT

## 1. Executive Summary
## 2. Verified Root Cause Pattern
## 3. Dead Content Before
## 4. Per-Stat Decision Table
## 5. Accessory Intrinsic Results
## 6. Affix Pool Results
## 7. Weapon Impact / Knockback / Stagger
## 8. Ammo Capacity Wiring
## 9. Deferred Design Decisions
## 10. Tooltip / UI Truthfulness
## 11. Save Compatibility
## 12. Validator Design
## 13. Automated Tests
## 14. Runtime Measurements
## 15. Built-Player Proof
## 16. Regression Results
## 17. Remaining Open Questions
## 18. Files Changed
## 19. Final Status

Explicitly list every stat as:

- CONSUMED
- REMOVED_FROM_V1_POOL
- DEFERRED_EXPLICITLY

with reason.

Do not call a stat fixed without real runtime proof.

---

# NON-GOALS

Do NOT in this pass:

- change overall depth HP scaling
- redesign XP/coin depth rewards
- add deepest-depth progression
- add depth checkpoints
- redesign biomes
- add generic enemy kill drops
- broadly rebalance ammo
- redesign bosses
- solve boss kiting
- alter boss attack selection
- rebuild audio
- solve black-void presentation
- redesign characters
- redesign weapon art
- redesign VFX
- redesign graphical inventory
- restructure multiplayer
- connect UGS/Relay
- broadly refactor `ExpeditionScene`
- add weapons/enemies/rooms

These are later passes.

---

# FINAL SUCCESS CONDITIONS

This pass is COMPLETE only if:

1. Every currently player-facing stat-granting item/affix is functional, excluded from acquisition, or explicitly deferred.
2. No newly generated/acquired item advertises a known no-op.
3. Safe/obvious existing stats have real runtime consumers.
4. Ammo Stack Capacity is proven through the real runtime path if retained for V1.
5. Knockback/Stagger matches existing documented intent and is runtime-proven if enabled.
6. Weapon Switch Speed is NOT given an invented mechanic.
7. Spread is NOT arbitrarily invented.
8. A validator prevents silent dead-content regression.
9. Old saves remain valid and non-destructive.
10. Relevant tests/validators are green except honestly documented external/pre-existing cases.
11. Built-player proof exists on the actually available platform.
12. No unrelated accepted system is redesigned.

If all repository-local conditions pass, print exactly:

`STAT_CONSUMER_INTEGRITY_COMPLETE`

and:

`production/STAT_CONSUMER_INTEGRITY_REPORT.md`

If repository-local requirements remain unresolved, print:

`STAT_CONSUMER_INTEGRITY_INCOMPLETE`

and explain the exact blockers.

External unavailable services/platform modules are not repository-local failures but must be reported truthfully.
