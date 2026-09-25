# RUINRAIL — FRESH-RUN COMBAT / ECONOMY / TTK VALIDATION PASS

## ROLE

You are performing a tightly scoped ANALYSIS + MEASUREMENT pass on the current RUINRAIL Unity repository and built-player runtime.

This is NOT a broad rebalance pass.

Do not change combat balance values, economy values, weapon values, enemy HP scaling, loot rates, starter ammo, boss HP, merchant prices, room generation, or progression numbers unless a measurement harness absolutely requires a non-shipping test-only seam.

Your job is to measure the game as it NOW ACTUALLY PLAYS after the Stat Consumer Integrity pass, because previously dead affixes/accessories/stats are now live and older balance conclusions may be stale.

Work autonomously.
Do not pause for approval.
Do not ask intermediate questions.
Do not stop after static analysis.
Use runtime evidence.

---

# REPOSITORY SAFETY

The current working tree contains intentional uncommitted work and is authoritative.

NEVER use:

- git checkout
- git restore
- git reset
- git clean
- git stash
- destructive cleanup
- broad file reverts

Do not modify unrelated work.

---

# PRIMARY OBJECTIVE

Build an evidence-based picture of:

1. Fresh-profile difficulty
2. Ammo economy
3. Combat time-to-kill
4. Weapon-class effectiveness
5. Rarity / affix impact
6. Melee vs ammo-based weapon economics
7. Blaster economics
8. Rocket / shotgun impact value
9. Boss fight duration
10. Depth scaling through at least D1 / D5 / D10 / D20 / D30
11. Merchant / coin usefulness during a run
12. Whether current encounter and loot pacing produce good risk/reward pressure

The output must tell us what should be changed NEXT.

Do not implement those balance changes in this pass.

---

# IMPORTANT CURRENT CONTEXT

The previous Stat Consumer Integrity pass completed successfully.

Important implications:

- previously dead affixes are now actually applied
- `PlayerRigComposer.ResolveAffix` and the Shelter resolver were fixed
- `WeaponStatMath` now applies weapon stats centrally
- Fire Rate, Magazine Size, Projectile Speed, Projectile Range, melee timing, blaster heat/cooling, bow charge, spread, knockback and stagger now have real consumers where applicable
- Ammo Stack Capacity is wired into runtime composition
- weapon impact values are now active for intended classes
- rare gear sustained DPS reportedly increased by roughly 6–12%
- `WeaponSwitchSpeed` remains intentionally deferred
- spread was not invented for non-authored weapons
- a `StatConsumerIntegrityValidator` now protects against silent dead stats

Therefore:

DO NOT reuse old DPS or TTK conclusions without re-measuring them against the current implementation.

---

# PREVIOUS REVIEW FINDINGS TO RE-VERIFY, NOT ASSUME

A prior comprehensive review reported:

- possible first-run ammo wall
- starter P9 + Field Knife might force melee before/at the first boss
- melee may be extremely ammo-efficient and high-DPS
- blasters may be economically strong because they use no ammo
- rockets may have low single-target sustained DPS but AoE/impact value
- snipers may have low sustained DPS but high positional value
- some normal weapons may be strictly dominated by alternatives
- deep-depth enemy HP scaling may create long, sponge-like fights
- coins and XP may not scale enough with depth
- merchant / paid event affordability may be very low in early depths
- kill rewards are indirect because normal enemies do not drop loot
- rarity improvement may be the main descend incentive

All of these must now be measured again using the CURRENT repository state.

---

# PHASE 1 — BUILD A CURRENT BASELINE

First inspect the current authoritative data and runtime formulas.

Create:

`TestResults/FreshRunBalance/current_balance_snapshot.csv`

Include at minimum:

- WeaponId
- Class
- RarityBaseline
- BaseDamageMin
- BaseDamageMax
- FireRate
- Magazine
- Reload
- AmmoType
- AmmoPerShot
- ProjectileSpeed
- Range
- Spread
- Knockback
- Stagger
- HeatPerShot
- CoolingRate
- BowCharge
- MeleeCadence
- Price
- SellValue
- Notes

Also capture:

- enemy HP / damage / move speed / threat cost / XP
- elite HP/damage/reward parameters
- boss HP / attack damage / movement / phase thresholds
- depth scaling curves
- loot rarity curves
- ammo caps
- starter loadout
- starter ammo
- merchant price ranges
- event costs
- chest ammo/coin reward ranges
- coin reward scaling
- XP scaling
- depth heal rules

Do not change these values.

---

# PHASE 2 — FRESH PROFILE RUN VALIDATION

Run a true fresh-profile scenario.

Minimum flow:

Main Menu
→ fresh/new profile
→ starter progression state
→ Shelter
→ starter loadout
→ Depth 1
→ complete main path
→ boss
→ boss cache
→ transit

Record:

- starting HP / max HP
- starting weapons
- starting consumables
- starting Light/Medium/Heavy/Shell ammo
- number of combat rooms
- enemies encountered by archetype
- shots fired
- hits
- misses
- reload count
- melee attacks
- ammo found
- ammo purchased
- ammo remaining before boss
- ammo used on boss
- ammo remaining after boss
- healing consumed
- HP before boss
- HP after boss
- coins earned
- coins spent
- items found
- rarity distribution
- run duration
- room clear duration
- boss duration

Run multiple deterministic seeds.

Minimum:
- 30 fresh-profile D1 runs
- across all 3 biomes where generation permits
- use deterministic seed list checked into test evidence

Do not use clock-derived seeds.

Create:

`TestResults/FreshRunBalance/fresh_profile_runs.csv`

---

# PHASE 3 — PLAYER SKILL SCENARIOS

The game must not be balanced only around perfect accuracy.

Measure at least three deterministic combat skill profiles:

## A. HIGH ACCURACY
Approx. 90–95% effective hit rate

## B. NORMAL ACCURACY
Approx. 70–80% effective hit rate

## C. NEW PLAYER / WASTEFUL
Approx. 50–60% effective hit rate
plus less-efficient reload timing where safely simulatable

Do not fake results by merely multiplying ammo consumption on paper if runtime harness can execute real misses.

For each profile record:

- ammo consumption
- time-to-clear
- damage taken
- melee fallback frequency
- boss ammo remaining
- fail/survive rate where deterministic simulation supports it

The goal is to determine whether the first run is viable for an ordinary player, not only for an optimal bot.

---

# PHASE 4 — AMMO ECONOMY

Measure each ammo-using weapon family.

At minimum:

- Pistol
- SMG
- Assault Rifle
- Battle Rifle
- Shotgun
- Sniper
- Rocket

For each, calculate and runtime-verify:

- rounds required per baseline normal enemy
- rounds required per elite
- rounds required per boss
- ammo consumed per room
- expected ammo gained per room/depth
- ammo efficiency per damage
- carried cap in equivalent damage
- number of combat rooms sustainable from full reserve
- reserve before and after boss

For rockets:
- include single-target
- 2-target splash
- 3-target splash
- realistic grouped-enemy case

For shotguns:
- measure realistic pellet connection, not theoretical all-pellets-only
- include knockback/stagger contribution separately from raw DPS

Create:

`TestResults/FreshRunBalance/ammo_economy_matrix.csv`

---

# PHASE 5 — WEAPON RUNTIME METRICS

Measure representative and full-catalog weapon performance using the CURRENT stat-consumer implementation.

For all 33 weapons measure:

- burst DPS
- sustained DPS
- practical sustained DPS including reload/heat/charge
- damage per ammo unit
- ammo consumed per second
- effective range
- projectile travel time to 5 / 10 / max tiles where relevant
- magazine duration
- reload downtime ratio
- melee attack rate
- blaster heat-limited cadence
- bow charge-limited cadence
- impact role
- AoE role
- practical single-target TTK against representative targets

Create:

`TestResults/FreshRunBalance/weapon_runtime_metrics.csv`

Do not label weapons "best" or "worst".

Instead classify roles such as:
- high ammo efficiency
- high burst
- high sustained
- positional
- crowd control
- AoE
- safe range
- close-risk/high-output
- utility/impact

---

# PHASE 6 — RARITY / AFFIX IMPACT

Because affixes now actually function, measure the value of rarity.

For a representative weapon from each relevant class, compare:

- Common / no affix
- Uncommon / 1 affix
- Rare / 2 affixes
- Epic / 3 affixes
- Legendary as implemented for that family

Use deterministic, documented affix combinations.

Do not cherry-pick only damage affixes.

Include combinations with:
- Fire Rate
- Reload
- Magazine
- Projectile Speed
- Range
- Damage
- Impact where applicable
- class-specific functional stats

Measure:
- DPS change
- effective range change
- magazine uptime
- ammo efficiency
- heat/charge changes
- practical TTK

Create:

`TestResults/FreshRunBalance/rarity_affix_effect.csv`

We need to answer:

> Does finding a higher-rarity version materially change how the weapon performs?

---

# PHASE 7 — MELEE ECONOMICS

Measure all melee weapons.

Important questions:

- Does melee outperform ammo weapons too broadly?
- How much real risk compensates for zero ammo cost?
- Does melee become the rational default whenever ammo is scarce?
- How much damage does the player take in realistic melee use?
- Does knockback/stagger materially improve spear/knife safety?
- Is starter Field Knife a backup tool or a primary DPS upgrade?

For each melee weapon measure:

- sustained DPS
- attack range
- attack arc
- attack cadence
- average incoming damage exposure during representative kills
- normal-enemy TTK
- elite TTK
- boss TTK
- zero-ammo economic advantage

Do not rebalance.

Create:

`TestResults/FreshRunBalance/melee_economy.csv`

---

# PHASE 8 — BLASTER ECONOMICS

Measure blasters separately because they do not use conventional ammo.

Record:

- sustained heat-limited DPS
- time to overheat if applicable
- recovery time
- shots per heat cycle
- effective downtime
- damage per minute
- comparison to ammo-using weapons at equivalent rarity
- practical advantage across a full depth where ammo scarcity exists

We need to determine whether "no ammo cost" is sufficiently paid for by heat limitations.

Create:

`TestResults/FreshRunBalance/blaster_economy.csv`

---

# PHASE 9 — ENEMY TTK BY DEPTH

Measure representative player loadouts against enemies at:

- D1
- D5
- D10
- D20
- D30

Optionally D50 for long-tail evidence if runtime time remains practical.

Use at least:

## Loadout A
Starter / low-power

## Loadout B
Mid-progression realistic Rare/Epic

## Loadout C
Strong late-account realistic build

Do not construct impossible perfect-stat builds.

Measure TTK for:

- Grunt
- Shooter
- Charger
- Brute
- Shield Enemy
- Sniper
- Summoner
- representative elite
- representative boss

For Shield Enemy:
- frontal
- correct flank/counterplay

For AoE weapons:
- single target separately from grouped targets

Create:

`TestResults/FreshRunBalance/depth_ttk_matrix.csv`

---

# PHASE 10 — BOSS DURATION

Measure all six bosses at:

- D1
- D5
- D10
- D20
- D30

using representative:
- starter/weak build where plausible
- mid build
- strong build

Record:

- theoretical uninterrupted DPS time
- practical runtime kill time
- percentage of fight spent dealing damage
- reload/heat/charge downtime
- dodge/telegraph downtime
- ammo consumed
- healing consumed
- damage taken
- whether ammo weapon can complete fight from a realistic pre-boss reserve
- melee fallback requirement

Create:

`TestResults/FreshRunBalance/boss_duration_matrix.csv`

Flag, but do not fix, fights that become mostly HP attrition.

---

# PHASE 11 — ROOM CLEAR / EXPEDITION PACING

Measure:

- average combat-small clear time
- combat-medium
- combat-large
- elite room
- boss
- loot room interaction time
- merchant interaction time
- traversal between rooms
- total depth duration

At:
- D1
- D10
- D20
- D30

Create:

`TestResults/FreshRunBalance/depth_pacing.csv`

We need to know whether deeper runs become:
- more dangerous
- more tactically complex
- or simply longer

Do not infer this only from HP scaling.

---

# PHASE 12 — COIN / MERCHANT / EVENT ECONOMY

Measure current in-run coin flow.

For deterministic depth sequences record:

- coins entering depth
- coins from containers
- coins from boss cache
- coins from other sources
- merchant prices encountered
- affordable offers
- event costs
- purchases made
- coins leaving depth
- coins carried into next depth
- coins secured on extraction

Run at least:
- D1-only extraction
- D1→D2
- D1→D5
- D1→D10 where simulation permits

Create:

`TestResults/FreshRunBalance/coin_economy.csv`

Answer:

- When does the merchant become meaningfully usable?
- Are paid event rooms usable early?
- Is there a reason to keep carried coins at risk?
- Does depth improve spending power?

Do not change prices in this pass.

---

# PHASE 13 — LOOT / RARITY REWARD CURVE

Measure the actual item rarity and acquisition curve.

Run deterministic simulations over many generated depths.

Minimum:
- 500 depth generations total
- coverage across D1 / D5 / D10 / D20 / D30
- all biomes

Record:
- Common
- Uncommon
- Rare
- Epic
- Legendary
- source type
- chest type
- weapon cache
- merchant
- boss cache
- elite source if applicable

Create:

`TestResults/FreshRunBalance/loot_rarity_curve.csv`

Compare actual runtime results to intended loot tables.

---

# PHASE 14 — DESCEND VALUE SNAPSHOT

Without changing the design, quantify the current decision:

RETURN
vs
DESCEND

For representative expedition states at:
- post-D1
- post-D5
- post-D10
- post-D20
- post-D30

record:

- secured value if returning
- carried value at risk if descending
- expected rarity improvement
- expected coin opportunity
- expected XP opportunity
- next-depth enemy HP/damage scaling
- ammo reserve
- consumables
- effective HP after the current full-heal rule

Create:

`TestResults/FreshRunBalance/descend_value_snapshot.csv`

Do not invent a "correct" choice.

The purpose is to expose where the risk/reward curve currently changes.

---

# PHASE 15 — STRICT DOMINANCE / ROLE OVERLAP CHECK

Re-check the full weapon catalog now that stats and affixes work.

A weapon should only be flagged when another comparable weapon is better across all major relevant axes without a meaningful compensating role.

Compare:

- DPS
- burst
- sustained
- range
- ammo consumption
- ammo type scarcity
- magazine
- reload
- projectile behavior
- AoE
- knockback/stagger
- Legendary special
- heat/charge
- melee reach/arc
- price if relevant

Do not declare dominance based only on DPS.

Create:

`TestResults/FreshRunBalance/weapon_role_overlap.csv`

Classification examples:
- distinct role
- heavy overlap
- likely dominated
- requires human feel test

---

# PHASE 16 — HUMAN PLAYTEST CHECKLIST

Create a short manual test sheet for the owner:

`TestResults/FreshRunBalance/HUMAN_PLAYTEST_CHECKLIST.md`

It should contain 15–25 highly targeted observations such as:

- Did you run dry before the first boss?
- Did you voluntarily switch to melee, or were you forced to?
- Did a Rare/Epic weapon feel meaningfully different?
- Did shotgun knockback feel useful?
- Did rocket splash justify its ammo cost?
- Did a blaster feel too economically safe?
- Did a Sniper feel valuable despite lower sustained DPS?
- Was the first merchant affordable enough to matter?
- Did D10 feel harder or merely slower?
- Did D20 enemies feel sponge-like?
- Did any weapon feel like an obvious non-choice?

Do not ask generic questions like "Was it fun?"

Make them diagnostic.

---

# PHASE 17 — TEST / MEASUREMENT QUALITY

All simulations must use deterministic seeds.

Create and save the seed set:

`TestResults/FreshRunBalance/seed_manifest.txt`

Avoid clock-seeded tests.

If existing flaky tests fail:
- report separately
- do not hide them
- do not treat them as proof of a balance issue
- do not broaden scope into unrelated test repair

Use runtime measurement where possible.

Do not rely only on:
- formulas
- ScriptableObject values
- static properties

If a gameplay outcome can be measured in PlayMode/built player, measure it.

---

# PHASE 18 — NO BALANCE CHANGES

This is critical.

DO NOT change shipping values for:

- weapon damage
- fire rate
- reload
- magazine
- ammo costs
- ammo caps
- starter ammo
- loot rates
- drop rates
- merchant prices
- event prices
- enemy HP
- boss HP
- depth scaling
- XP
- coins
- rarity curves
- healing
- damage reduction
- progression values

If you find a literal runtime bug where the current shipped value is not being applied as authored, fix only that bug and document it.

Otherwise:
ANALYZE, MEASURE, REPORT.

---

# PHASE 19 — REQUIRED REPORT

Create:

`production/FRESH_RUN_COMBAT_ECONOMY_BALANCE_REPORT.md`

Structure:

# RUINRAIL — FRESH-RUN COMBAT / ECONOMY / TTK VALIDATION

## 1. Executive Summary
## 2. Current Balance Snapshot
## 3. Fresh Profile Results
## 4. Skill-Profile Results
## 5. Ammo Economy
## 6. Weapon Runtime Metrics
## 7. Rarity / Affix Impact
## 8. Melee Economics
## 9. Blaster Economics
## 10. Enemy TTK by Depth
## 11. Boss Duration
## 12. Depth Pacing
## 13. Coin / Merchant Economy
## 14. Loot / Rarity Curve
## 15. Descend Value
## 16. Weapon Role Overlap
## 17. Human Playtest Checklist
## 18. Confirmed Balance Problems
## 19. Suspected / Human-Feel Problems
## 20. Things That Are Actually Fine
## 21. Recommended Next Balance Changes
## 22. Changes Explicitly NOT Recommended
## 23. Open Design Questions
## 24. Test / Runtime Evidence
## 25. Files Created / Modified
## 26. Final Status

---

# PHASE 20 — RECOMMENDATIONS FORMAT

Recommendations must be grouped as:

- CONFIRMED — strong runtime evidence
- LIKELY — multiple indicators, human validation still useful
- HUMAN-FEEL ONLY — cannot be concluded from automation
- NO CHANGE RECOMMENDED

For each proposed future change include:

- Area
- Evidence
- Current measured value
- Why it matters
- Suggested direction
- Do NOT give an arbitrary replacement number unless the data supports a narrow range
- Expected side effects
- Regression risk
- Human playtest needed: YES/NO

Do not turn the report into a giant generic backlog.

Prefer the smallest number of changes with the largest gameplay effect.

---

# REQUIRED ARTIFACTS

Create:

- `production/FRESH_RUN_COMBAT_ECONOMY_BALANCE_REPORT.md`
- `TestResults/FreshRunBalance/current_balance_snapshot.csv`
- `TestResults/FreshRunBalance/fresh_profile_runs.csv`
- `TestResults/FreshRunBalance/ammo_economy_matrix.csv`
- `TestResults/FreshRunBalance/weapon_runtime_metrics.csv`
- `TestResults/FreshRunBalance/rarity_affix_effect.csv`
- `TestResults/FreshRunBalance/melee_economy.csv`
- `TestResults/FreshRunBalance/blaster_economy.csv`
- `TestResults/FreshRunBalance/depth_ttk_matrix.csv`
- `TestResults/FreshRunBalance/boss_duration_matrix.csv`
- `TestResults/FreshRunBalance/depth_pacing.csv`
- `TestResults/FreshRunBalance/coin_economy.csv`
- `TestResults/FreshRunBalance/loot_rarity_curve.csv`
- `TestResults/FreshRunBalance/descend_value_snapshot.csv`
- `TestResults/FreshRunBalance/weapon_role_overlap.csv`
- `TestResults/FreshRunBalance/HUMAN_PLAYTEST_CHECKLIST.md`
- `TestResults/FreshRunBalance/seed_manifest.txt`

Add runtime logs/captures where useful.

---

# REGRESSION / BUILD GATES

Run relevant existing tests and validators after adding analysis harnesses.

At minimum:
- EditMode
- PlayMode
- production validators
- StatConsumerIntegrityValidator
- item/content validators
- save/load validation

Build the platform actually available.

If Windows x64 module is unavailable:
- report `Windows x64: NOT RUN — module unavailable`
- do not install it automatically
- do not claim Windows verification

A macOS non-development build and built-player smoke are acceptable on the current machine.

The analysis harness must not change shipping balance.

---

# FINAL SUCCESS CONDITIONS

This pass is COMPLETE only if:

1. Current post-stat-fix weapon/economy data has been re-measured.
2. At least 30 deterministic fresh-profile D1 runs exist.
3. Normal and lower-skill ammo scenarios are included.
4. All 33 weapons have runtime metric coverage.
5. Rarity/affix value has been measured after the affix fix.
6. Melee and blaster economics are explicitly evaluated.
7. Enemy TTK exists for D1/D5/D10/D20/D30.
8. All six bosses have duration measurements across relevant depths.
9. Coin/merchant/event affordability is measured.
10. Loot rarity curve is measured from a large deterministic sample.
11. Descend risk/reward is quantified without changing it.
12. Strict weapon role overlap is re-evaluated using current mechanics.
13. A focused human playtest checklist exists.
14. No shipping balance values were broadly changed.
15. The final report clearly separates confirmed facts from design judgement.

If complete, print exactly:

`FRESH_RUN_COMBAT_ECONOMY_BALANCE_VALIDATION_COMPLETE`

and:

`production/FRESH_RUN_COMBAT_ECONOMY_BALANCE_REPORT.md`

If any repository-local requirement cannot be completed, print:

`FRESH_RUN_COMBAT_ECONOMY_BALANCE_VALIDATION_INCOMPLETE`

and list the exact blockers.

External unavailable platform modules are not repository-local failures, but must be reported truthfully.
