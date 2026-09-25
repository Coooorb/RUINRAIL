# RUINRAIL — RUN VARIETY / DEPTH RETENTION / BOSS PASS

## ROLE

You are performing one consolidated gameplay pass on the current RUINRAIL Unity repository.

This pass intentionally combines several closely related findings from the completed full-game review so they do not become many tiny tasks:

1. Endless-depth achievement / retention
2. Post-Depth-30 reward continuation
3. Boss attack variety
4. Boss anti-kite fallback behavior
5. Elite visibility / encounter frequency
6. Depth-gated room variety
7. Stronger biome gameplay identity using existing content

This is an IMPLEMENTATION + VALIDATION task, not a new broad design review.

Work autonomously.
Do not pause for approval.
Do not ask intermediate questions.
Do not stop after static analysis.
Inspect the current repository before changing anything.
Use deterministic runtime evidence.
Keep the pass tightly within the scope below.

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

If you need to undo your own local edit, restore it manually from content you actually inspected during this task.

---

# IMPORTANT CURRENT PROJECT STATE

Several earlier review findings are now already resolved.

Do NOT reopen them:

- dead accessory / affix / weapon stats are fixed
- `WeaponStatMath` is live
- `AffixRegistry` is live
- `StatConsumerIntegrityValidator` exists
- Ammo Stack Capacity runtime composition is live
- Knockback / Stagger identity is live where intended
- D1 ammo was already fine-tuned
- Supply Chest Light ammo was already adjusted from 20–40 to 26–46
- Field Knife is owner-approved and must remain unchanged
- blasters already received the approved mild heat-flow buff
- graphical inventory is accepted
- characters, weapons and VFX are accepted
- current Main Menu is accepted
- current Dungeon Merchant is accepted
- player progression attribute effects are fixed
- run-start / depth-arrival full-HP rules are correct and frozen
- encounter containment is correct and frozen
- save/persistence architecture is correct and frozen
- current combat aim / collision behavior is not part of this pass

The previous fresh-run balance validation also found that regular weapons no longer have a meaningful strict-dominance problem after the stat fixes.

Do NOT start another broad weapon rebalance.

---

# AUTHORITATIVE OWNER DECISIONS

These override purely theoretical conclusions.

## A. Depth 1–30 difficulty scaling is accepted

Do NOT change enemy or boss HP / damage scaling from Depth 1 through Depth 30.

Those values were deliberately established earlier and are currently accepted.

## B. Post-Depth-30 difficulty is NOT the first lever

Depth 31+ must be measured because the previous review found that reward growth flattens while risk continues to rise.

However:

- do not immediately nerf post-D30 enemy/boss difficulty
- prefer fixing reward / retention incentives first
- only report post-D30 TTK / risk behavior
- do not alter D31+ enemy/boss HP/damage scaling in this pass unless a literal bug is found

Any future difficulty-curve redesign after D30 should remain a separate owner decision.

## C. No start-at-deeper-depth feature yet

Record and display the deepest depth reached.

DO NOT implement:
- choose starting depth
- checkpoints that skip early depths
- paid depth skips
- “continue from deepest depth”

Those have extraction/economy consequences and remain a separate design decision.

## D. Boss HP / weapon balance stay frozen

This pass may alter boss ATTACK SELECTION and NO-VALID-ATTACK MOVEMENT/FALLBACK behavior.

It may NOT alter:
- boss HP
- boss damage numbers
- weapon damage
- weapon fire rate
- ammo caps
- starter ammo
- melee damage
- blaster values
- the recent D1 ammo change

---

# PRIMARY OBJECTIVES

By the end of this task:

1. The game permanently records the player's deepest successfully reached depth.
2. That achievement is clearly visible in appropriate existing UI.
3. Depth 31+ has a continuing, bounded reward reason to descend even after the existing rarity curve stops materially improving.
4. D1–D30 combat difficulty scaling remains unchanged.
5. Boss attack choice is no longer a predictable “first valid attack in list” pattern.
6. Bosses do not become harmless when the player sits outside all authored attack bands.
7. Elites are visible often enough to justify the six authored elite variants, without becoming constant.
8. Some existing rooms become meaningfully depth-gated, creating visible progression without adding new rooms.
9. The three existing biomes gain measurable gameplay identity using current content and current art, without a total content redesign.
10. Existing accepted systems remain stable.

---

# PHASE 1 — RE-VERIFY CURRENT STATE BEFORE EDITING

Do not blindly trust the old review.

Inspect the current repository and create:

`TestResults/RunVarietyDepthRetention/current_state_before.csv`

Capture at minimum:

- depth scaling values at D1 / D5 / D10 / D20 / D30 / D40 / D50 / D75 / D100
- enemy HP and damage multipliers
- boss HP and damage multipliers
- current XP depth scaling
- current coin depth scaling
- current rarity-by-depth bands
- current reward caps
- current boss-cache behavior
- current merchant price scaling
- current event costs
- current elite-frequency rules
- actual elite appearance rate over deterministic generated depths
- all 63 room `_minDepth` / `_maxDepth`
- room selection weights
- encounter pools / weighting by biome
- hazard parameters by biome
- current boss attack-selection code path
- current boss attack distance bands
- boss move speeds
- boss arena sizes
- current profile schema / migration version
- whether any deepest-depth field already exists

If the old review is stale, use the current implementation as authoritative and document the difference.

---

# PHASE 2 — DEEPEST DEPTH RECORD

Implement a persistent personal-best depth record.

## Semantics

Use an explicit field such as:

`DeepestDepthReached`

or an equivalent repository-consistent name.

It must be:

- monotonic
- profile-persistent
- save-version compatible
- safe for existing saves
- deterministic
- not reset on death
- not reset on extraction
- not reset on a new expedition
- not reduced if an old/shallower run is played later

## When it updates

Update the record only when the player has successfully ENTERED / ARRIVED at that depth in a real expedition.

Examples:

- starting Depth 1 may establish 1
- defeating the D1 boss alone must not prematurely record D2
- choosing DESCEND and successfully building/entering D2 may record 2
- a failed transition must not record a depth never entered

Use the current authoritative depth-transition seam.

Do not create an exploit where replaying room transitions increments the record.

## Existing saves

Old saves must migrate safely.

Expected migration behavior:

- missing field defaults safely
- no profile corruption
- no item loss
- no XP / skill / coin reset
- no duplicate grant
- record begins from the first verified depth entered after migration unless reliable historical depth data already exists

Do not fabricate historical personal-best data.

---

# PHASE 3 — DEEPEST DEPTH PRESENTATION

Display the record in existing presentation surfaces without redesigning them.

Required:

## A. Shelter / profile-facing readout
Show a compact line/card such as:

`DEEPEST DEPTH: 17`

Use the existing RUINRAIL UI language/style.

Do not create a large new screen.

## B. Expedition result / run-lost / return summary
Show:

- depth reached this expedition
- personal best
- a clear `NEW PERSONAL BEST` treatment only when actually surpassed

Do not create a fake celebratory modal.

## C. Main Menu profile area
If the current Main Menu profile panel has a natural place for it, show the record there.

Do not damage the accepted Main Menu layout merely to force another label.

If no clean slot exists, document why Shelter + run summary are sufficient.

## D. Transit
If it can be added cleanly without crowding, show current depth and personal best as passive context.

Do NOT add a “start at best depth” button.

---

# PHASE 4 — POST-D30 REWARD CONTINUATION

The previous review found that the rarity curve materially stops improving around Depth 30 while enemy scaling continues.

The owner does NOT want D1–D30 difficulty scaling changed.

The purpose here is to give D31+ a continuing reason to descend without creating runaway inflation.

## First measure the current curve

Create:

`TestResults/RunVarietyDepthRetention/reward_curve_before.csv`

For D1 / D5 / D10 / D20 / D30 / D40 / D50 / D75 / D100 record:

- enemy HP multiplier
- enemy damage multiplier
- expected XP from representative depth
- expected coins from representative depth
- expected loot rarity distribution
- expected boss-cache value
- expected total carried value
- expected value secured by RETURN
- expected value at risk by DESCEND

Use deterministic simulation where possible.

## Reward-side design guardrails

Do NOT add a new rarity tier.

Do NOT increase the maximum power ceiling of item affixes.

Do NOT flood the backpack with extra items.

Prefer using existing reward types.

Preferred levers, in order:

1. modest post-D30 XP scaling
2. modest post-D30 coin scaling
3. bounded post-D30 improvement to high-rarity chance / reward quality if the current loot table supports it cleanly
4. modest boss-cache value continuation

Avoid adding four new reward systems.

## Required behavior

- D1–D30 reward behavior should remain effectively unchanged unless a literal bug is found
- D31+ expected reward must not become flat while risk continues to rise
- reward growth must use diminishing returns / a cap
- D100 must not produce absurd coin/XP inflation
- D31+ should feel “still worth pushing” rather than “farm D1 forever”
- extraction must still matter because value remains at risk

## Calibration

Use the smallest change that produces a clear monotonic post-D30 incentive.

Do not choose values from intuition alone.

Run a calibration sweep and compare at least three candidate curves.

Create:

`TestResults/RunVarietyDepthRetention/post30_reward_calibration.csv`

For each candidate include:

- D30 value
- D40 value
- D50 value
- D75 value
- D100 value
- multiplier vs D30
- expected inflation impact
- whether D1–D30 changed
- whether cap/diminishing-return behavior holds

Choose the most conservative curve that solves the flat-reward problem.

If the current architecture cannot support a clean bounded post-D30 curve without a broad economy rewrite, do NOT fake it.
Implement the deepest-depth system and other phases, mark this subphase incomplete in the report, and explain the smallest follow-up architecture needed.

---

# PHASE 5 — RETURN VS DESCEND INFORMATION

Do NOT automate the player's decision.

Do NOT show “recommended” or “best choice”.

Improve information only if current UI lacks it.

The post-boss Transit decision may display concise factual context:

- current depth
- personal-best depth
- current carried coins/value if already available
- next depth number
- any explicit post-D30 reward bonus if one is implemented

Do not add prediction language.

Do not remove the current full-heal-on-descend rule.

Do not change extraction loss rules.

---

# PHASE 6 — BOSS ATTACK SELECTION VARIETY

The previous review reported that boss attack selection returns the first ready attack whose distance band matches.

Re-verify this.

If still true, replace it with seeded selection among ALL valid ready in-band attacks.

## Requirements

- deterministic for the same run/depth/boss seed
- network/replay-friendly
- use the existing named deterministic RNG streams
- do not use `UnityEngine.Random`
- do not use wall-clock time
- preserve cooldowns
- preserve phases
- preserve attack distance bands
- preserve attack damage
- preserve telegraph timings unless required by a literal bug
- preserve attack definitions

## Selection behavior

Preferred:

1. collect all ready attacks valid for current phase and range
2. apply authored weights if they already exist
3. otherwise use equal weighting rather than list-order priority
4. draw from deterministic encounter/boss RNG
5. avoid pathological immediate repetition if a repository-supported no-repeat mechanism already exists

Do NOT invent complex adaptive AI.

Create:

`TestResults/RunVarietyDepthRetention/boss_attack_distribution.csv`

For all six bosses, across deterministic simulations, record:

- attack id
- valid opportunities
- selections
- selection percentage
- immediate-repeat rate
- phase
- range band
- seed coverage

No valid authored attack should remain effectively unreachable because it appears later in a list.

---

# PHASE 7 — BOSS NO-VALID-ATTACK / ANTI-KITE FALLBACK

The previous review found that bosses can have no valid attack when the player sits outside all attack bands, causing plain chase behavior.

Re-verify all six bosses.

Do NOT solve this by:
- increasing boss HP
- increasing boss damage
- globally increasing boss speed
- shrinking arenas
- nerfing player movement
- nerfing Snipers
- changing aim assist

## Preferred behavior

When no authored attack is currently valid:

- use an existing reposition / gap-close / dash / movement behavior if the boss already owns one
- or add a small generic non-damaging `RepositionToEngagementRange` fallback
- preserve collision / EncounterBounds
- never teleport through walls
- never leave the arena
- do not create unavoidable damage
- once in a valid band, return to normal attack selection

If a boss already has an authored long-range tool that is simply never selected because of selection logic, fixing Phase 6 may be sufficient.

Do not add new attacks unless no existing clean fallback can solve the actual gap.

Create:

`TestResults/RunVarietyDepthRetention/boss_kite_matrix.csv`

For each boss record:

- arena dimensions
- player max practical engagement range
- boss max authored attack range
- previous no-valid zone
- fallback used
- time spent in no-valid state before/after
- bounds violations
- wall penetration
- unavoidable-hit regressions

---

# PHASE 8 — ELITE VISIBILITY / FREQUENCY

The game contains six authored elites.

The old review reported very low dungeon-level elite frequency.

First measure actual current frequency over deterministic generation.

Minimum:

- 1,000 generated depths total
- D1 / D5 / D10 / D20 / D30 / D50 represented
- all three biomes represented

Create:

`TestResults/RunVarietyDepthRetention/elite_frequency_before.csv`

Record:

- depth
- biome
- elite eligible rooms
- elite spawned YES/NO
- elite type
- total elites per expedition segment

## Adjustment rule

Do not blindly increase the rate.

If current data shows elites already appear often enough to be a meaningful recurring encounter, leave frequency unchanged and document that the old review is stale.

If the median multi-depth expedition encounters elites so rarely that most players can go several depths without seeing any, increase frequency conservatively.

Target behavior:

- elites remain special
- they are not in every depth
- a player pushing several depths should regularly encounter the authored elite system
- D1 should not suddenly become elite-heavy

Use the smallest data change that achieves that.

Do not change elite HP/damage/rewards in this phase unless a literal bug is found.

Create:

`TestResults/RunVarietyDepthRetention/elite_frequency_after.csv`

---

# PHASE 9 — DEPTH-GATED EXISTING ROOMS

All room definitions were previously available from Depth 1.

Add visible progression using EXISTING rooms only.

Do NOT create new rooms in this pass.

## Rules

- keep Start rooms available at D1
- keep required boss rooms available
- keep Merchant / Medical / Loot / Treasure availability viable
- do not create a generation dead-end
- every biome must remain generatable at D1
- every biome must remain generatable at D5 / D10 / D20 / D30
- room graph validation must remain deterministic

## Scope

Gate only a SMALL subset of existing rooms.

Prefer rooms whose:
- name
- layout
- hazard density
- encounter capacity
- event identity
- or existing difficulty value

already makes them read as stronger / deeper content.

Suggested depth bands to consider:

- D1 baseline
- D5+
- D10+
- D20+

Do not gate half the room catalog.

Do not introduce D31-only rooms yet unless an existing room clearly supports that role.

## Validation

Create:

`TestResults/RunVarietyDepthRetention/room_depth_gating.csv`

Columns:

- RoomId
- Biome
- RoomType
- PreviousMinDepth
- NewMinDepth
- Why
- D1Eligible
- D5Eligible
- D10Eligible
- D20Eligible
- D30Eligible

Run broad seed generation.

Minimum:
- 100 valid generated depths per biome at D1
- 100 per biome at D5
- 100 per biome at D10
- 100 per biome at D20
- 100 per biome at D30

No generation failures.

---

# PHASE 10 — BIOME GAMEPLAY IDENTITY

The previous review reported that removing the art makes the three biomes mechanically very similar.

Improve identity using current content.

Do NOT:
- redesign accepted art
- add a new enemy roster
- create new bosses
- create dozens of new rooms
- change the 63-room total
- broadly change weapon balance

## Source of truth

Before inventing any biome behavior, inspect the existing biome design docs, room names, hazards, boss themes and encounter tags.

Derive mechanical differences from EXISTING authored identity.

Do not impose generic stereotypes if the repository does not support them.

## Preferred levers

Use the smallest number of existing systems that produce meaningful identity:

### A. Encounter weighting
Different biomes may weight existing enemy archetypes differently.

Do not make archetypes exclusive unless existing design docs say so.

### B. Elite weighting
If current elite themes already map naturally to biomes, use that.
Otherwise do not force it.

### C. Hazard behavior
The previous review found the three hazards numerically identical.

If design docs support distinct roles, give them modest mechanical differences using existing hazard parameters.

Examples of allowed dimensions only if supported:
- tick cadence
- damage band
- activation timing
- area persistence
- stagger/knockback where appropriate
- player/enemy interaction

Do not invent new status-effect systems.

### D. Room weighting
Existing biome rooms may have different selection weights so the spatial rhythm differs.

Do not break content-count validation.

### E. Encounter composition
Use existing encounter/archetype weighting so one biome trends toward a different combat texture.

## Validation

Create:

`TestResults/RunVarietyDepthRetention/biome_identity_matrix.csv`

For each biome record:

- room-type distribution over large seed sample
- top enemy archetypes by frequency
- elite distribution
- hazard behavior
- average threat composition
- average ranged/melee enemy ratio if derivable
- average room size mix
- boss identity
- gameplay summary based on measured differences

The three summaries must not be identical after the pass.

If the existing docs do not support a safe mechanical distinction for one dimension, leave that dimension unchanged and say so.

---

# PHASE 11 — CONTENT CONTRACT / VALIDATOR UPDATES

Some current validators may enforce identical per-biome room distributions or previous fixed values.

Do NOT simply weaken validators to make changes pass.

Update validators to enforce the NEW intended contract.

Examples:

- every biome still has exactly 21 authored rooms
- every biome retains required room categories
- depth gating preserves D1 generation viability
- deterministic generation succeeds across tested bands
- biome weighting stays within declared ranges
- elite frequency stays within declared bounds if adjusted
- D1–D30 difficulty scaling fingerprints remain unchanged
- post-D30 reward curve is bounded and monotonic if implemented
- boss attack selection uses deterministic RNG and exposes all valid authored attacks

Add or extend a validator such as:

`RunVarietyDepthRetentionValidator`

if that fits the current architecture better than overloading unrelated validators.

A validator must fail on a deliberately broken fixture.

---

# PHASE 12 — SAVE / MIGRATION / EXPLOIT SAFETY

Deepest-depth tracking and post-D30 reward changes must not create exploits.

Verify:

- old save migration
- atomic save behavior
- personal best cannot decrease
- depth-transition replay cannot increment rewards twice
- failed descend does not award next-depth personal best
- RETURN secures existing reward exactly once
- death/wipe does not secure carried risk loot
- post-D30 bonus does not double-apply on merchant sell values unless explicitly intended
- post-D30 bonus does not mint ammo
- post-D30 bonus does not alter item affix power
- host/client transaction ids remain unaffected
- no duplicate boss cache
- no duplicate XP/coin commit

Do not refactor the save system.

---

# PHASE 13 — PERFORMANCE / DETERMINISM

The new systems must remain cheap.

Boss selection:
- no per-frame allocations in hot AI loops
- selection only when an attack choice is needed

Biome / room weighting:
- generation-time only where possible

Reward scaling:
- simple deterministic math
- no mutable global RNG

Deepest-depth:
- save only at existing safe commit points

Run deterministic reproducibility tests:

same seed + same depth + same content version
→ same room graph
→ same encounter composition
→ same boss selection sequence under the same simulated inputs

---

# PHASE 14 — REGRESSION FREEZE CHECK

Create:

`TestResults/RunVarietyDepthRetention/frozen_systems_check.csv`

Explicitly verify unchanged:

- D1–D30 enemy HP scaling
- D1–D30 enemy damage scaling
- D1–D30 boss HP scaling
- D1–D30 boss damage scaling
- Field Knife
- all 33 weapon base definitions except no changes should be expected
- recent blaster tuning
- recent Supply Chest Light 26–46 tuning
- ammo caps
- starter kit
- run-start HP rule
- depth-arrival full-heal rule
- PlayerStats aggregation
- StatConsumerIntegrity
- graphical inventory
- Dungeon Merchant
- Main Menu layout except optional tiny depth readout
- HUD rules except factual depth/personal-best context if added
- EncounterBounds
- death transaction
- extraction transaction
- backpack reorder
- non-combat interactions
- settings behavior
- audio
- art
- VFX
- character visuals
- multiplayer/network architecture

Any unexpected drift is a failure.

---

# PHASE 15 — AUTOMATED TESTS

Run all relevant existing gates.

At minimum:

- full EditMode
- full PlayMode
- production validators
- FinalProductionValidator
- ContentCountValidator or updated equivalent
- StatConsumerIntegrityValidator
- save/migration validators
- generation validators
- combat/boss tests
- economy tests
- room/non-combat tests
- smoke tests

Known existing seed/clock-sensitive test issues must be reported honestly.

Do not hide them.

Do not broaden this task into a general test-infrastructure rewrite unless one of your new deterministic tests is itself wrong.

---

# PHASE 16 — BUILT-PLAYER PROOF

Build the actually available target.

If Windows x64 module is unavailable:

`Windows x64: NOT RUN — module unavailable`

Do not install it automatically.

A macOS non-development build is acceptable on the current machine.

Built-player proof should cover, where practical:

1. fresh profile
2. enter D1
3. deepest depth displays correctly
4. descend at least once
5. personal best increments only after successful arrival
6. return / save / reload
7. personal best persists
8. boss uses more than one valid attack over repeated opportunities
9. boss outside attack band actively repositions rather than idling/chasing forever
10. depth-gated room system does not break generation
11. biome identity evidence is observable in generated encounter composition
12. no regression in D1 ammo / Field Knife / blaster tuning

Post-D30 reward logic may use deterministic simulation if reaching D31+ in a real-time smoke is impractical, but at least one PlayMode/runtime path must exercise the actual reward calculation, not a copied formula.

---

# PHASE 17 — OWNER PLAYTEST CHECKLIST

Create:

`TestResults/RunVarietyDepthRetention/HUMAN_PLAYTEST_CHECKLIST.md`

Keep it diagnostic and compact.

Include approximately 15–20 checks such as:

- Does seeing your personal-best depth make another run feel more goal-oriented?
- Does `NEW PERSONAL BEST` appear only when deserved?
- Does the Transit screen give useful information without telling you what choice to make?
- Do bosses choose attacks in a less predictable order?
- Can you still safely read boss telegraphs?
- When you kite to long range, does the boss actively re-engage without becoming unfair?
- Do elites appear often enough to remember them, but not every depth?
- Do later-depth rooms feel less repetitive because some layouts appear later?
- Can you tell Metro / Rustworks / Labs apart through combat behavior without staring at the floor art?
- Does any biome feel obviously harder for accidental reasons?
- Does D31+ offer a visible reason to keep pushing?
- Does the reward increase feel meaningful without exploding the economy?
- Does D1–D30 still feel unchanged?
- Does the recent D1 ammo tuning still feel unchanged?
- Does the Field Knife still feel unchanged?
- Do blasters still feel unchanged from the approved fine-tuning pass?

Do not ask generic “is it fun?” questions.

---

# PHASE 18 — REQUIRED REPORT

Create:

`production/RUN_VARIETY_DEPTH_RETENTION_BOSS_REPORT.md`

Required structure:

# RUINRAIL — RUN VARIETY / DEPTH RETENTION / BOSS REPORT

## 1. Executive Summary
## 2. Current-State Reverification
## 3. Owner Constraints / Frozen Decisions
## 4. Deepest Depth Persistence
## 5. Deepest Depth UI
## 6. Post-D30 Reward Curve
## 7. Reward Calibration
## 8. Return vs Descend Information
## 9. Boss Attack Selection
## 10. Boss Anti-Kite Fallback
## 11. Elite Frequency
## 12. Depth-Gated Rooms
## 13. Biome Gameplay Identity
## 14. Validator / Contract Changes
## 15. Save / Migration / Exploit Safety
## 16. Determinism / Performance
## 17. Frozen-System Verification
## 18. Automated Tests
## 19. Built-Player Evidence
## 20. Human Playtest Checklist
## 21. Known Pre-Existing Flakes
## 22. Files Changed
## 23. Deferred Design Decisions
## 24. Final Status

The report must clearly distinguish:

- implemented
- measured but unchanged
- deliberately deferred
- blocked

---

# REQUIRED ARTIFACTS

Create:

- `production/RUN_VARIETY_DEPTH_RETENTION_BOSS_REPORT.md`
- `TestResults/RunVarietyDepthRetention/current_state_before.csv`
- `TestResults/RunVarietyDepthRetention/reward_curve_before.csv`
- `TestResults/RunVarietyDepthRetention/post30_reward_calibration.csv`
- `TestResults/RunVarietyDepthRetention/boss_attack_distribution.csv`
- `TestResults/RunVarietyDepthRetention/boss_kite_matrix.csv`
- `TestResults/RunVarietyDepthRetention/elite_frequency_before.csv`
- `TestResults/RunVarietyDepthRetention/elite_frequency_after.csv`
- `TestResults/RunVarietyDepthRetention/room_depth_gating.csv`
- `TestResults/RunVarietyDepthRetention/biome_identity_matrix.csv`
- `TestResults/RunVarietyDepthRetention/frozen_systems_check.csv`
- `TestResults/RunVarietyDepthRetention/HUMAN_PLAYTEST_CHECKLIST.md`
- deterministic seed manifest / logs / captures as appropriate

---

# NON-GOALS

Do NOT in this pass:

- change D1–D30 difficulty scaling
- automatically redesign D31+ difficulty scaling
- change weapon balance
- change Field Knife
- change blaster tuning
- change D1 ammo tuning
- change ammo caps
- change boss HP
- change boss damage numbers
- add a new rarity tier
- add a new enemy roster
- add new bosses
- create many new rooms
- add start-at-depth
- add checkpoints
- add enemy kill drops
- redesign progression
- redesign inventory
- redesign Main Menu
- redesign character art
- redesign weapon art
- redesign VFX
- do the audio-mix pass
- do the black-void/background presentation pass
- do Shelter Trader polish
- add the Codex
- do grenade quick-use
- do aim-assist settings
- restructure co-op
- connect live UGS
- broadly refactor `ExpeditionScene`

Those belong to later consolidated passes.

---

# FINAL SUCCESS CONDITIONS

This task is COMPLETE only if:

1. Current findings were reverified before edits.
2. Deepest depth is persisted safely and monotonically.
3. Existing saves migrate safely.
4. Personal best is presented in at least Shelter/run-summary surfaces.
5. Start-at-depth was NOT implemented.
6. D1–D30 enemy/boss difficulty scaling remains unchanged.
7. D31+ difficulty scaling remains unchanged unless only a literal bug was fixed.
8. Post-D30 reward behavior was measured.
9. If safe architecture allows it, a conservative bounded post-D30 reward continuation was implemented and calibrated.
10. Boss attack choice is deterministic but no longer fixed by list order.
11. All six bosses have attack-distribution evidence.
12. Bosses have a fair no-valid-attack re-engagement path.
13. EncounterBounds / collision remain intact.
14. Elite frequency was measured before any change.
15. Any elite-frequency change is conservative and evidenced.
16. A small subset of existing rooms is depth-gated without generation failures.
17. All three biomes have measurable gameplay differences rooted in existing design/content.
18. Validators enforce the new contracts honestly.
19. Frozen systems pass unchanged-value checks.
20. Full relevant test/validator gates are reported truthfully.
21. An available-platform non-development build succeeds.
22. Built-player/runtime proof exists.
23. Human playtest checklist exists.
24. No unrelated redesign occurred.

If complete, print exactly:

`RUN_VARIETY_DEPTH_RETENTION_BOSS_COMPLETE`

and:

`production/RUN_VARIETY_DEPTH_RETENTION_BOSS_REPORT.md`

If a repository-local requirement remains unresolved, print:

`RUN_VARIETY_DEPTH_RETENTION_BOSS_INCOMPLETE`

and list the exact blockers.

Unavailable external platform modules are not repository-local failures, but must be reported truthfully.
