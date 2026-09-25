# RUINRAIL — D1 AMMO / BLASTER FINE-TUNING PASS

## ROLE

You are performing a tightly scoped BALANCE FINE-TUNING pass on the current RUINRAIL Unity repository.

This pass follows the completed:
- Stat Consumer Integrity pass
- Fresh-Run Combat / Economy / TTK Validation pass
- owner/player-feel validation

The purpose is NOT to rebalance the whole game.

The purpose is to make two small, evidence-backed corrections while explicitly preserving the parts that currently feel right.

Work autonomously.
Do not pause for approval.
Do not ask intermediate questions.
Do not broaden scope.

---

# REPOSITORY SAFETY

The current working tree contains intentional uncommitted work and is authoritative.

NEVER use:
- `git checkout`
- `git restore`
- `git reset`
- `git clean`
- `git stash`
- destructive cleanup
- broad file reverts

Do not discard unrelated work.

---

# AUTHORITATIVE DESIGN DECISIONS FOR THIS PASS

These decisions come from actual owner playtesting and override purely theoretical balance conclusions.

## 1. D1 ammo is only slightly too low

The previous automated pass modeled a larger first-boss ammo deficit.

The owner's actual playtest result is different:
- D1 room clearing feels broadly correct
- the player is EXPECTED to use both equipped weapons
- the starter Field Knife is not merely an emergency fallback
- using only the pistol to clear every room should carry a resource cost
- arriving at the boss slightly low on Light Ammo is acceptable
- the perceived shortfall is roughly only 10–15 Light rounds in a normal mixed-weapon run

Therefore:

DO NOT treat D1 as a major ammo-wall redesign.

The target is a SMALL correction only.

## 2. Field Knife is correctly tuned

The Field Knife feels appropriate in real play.

DO NOT:
- nerf its damage
- reduce its attack speed
- shorten its reach
- add ammo/resource cost
- weaken it because its paper DPS exceeds the P9
- redefine it as an emergency-only weapon

The two-weapon loadout is intentional.
Melee is meant to participate meaningfully in room clearing and ammo conservation.

## 3. D1–D30 depth scaling is intentionally accepted

Do NOT alter enemy/boss depth scaling from Depth 1 through Depth 30 in this pass.

The earlier scaling curve through D30 was a deliberate design choice and is currently accepted.

Any concern about scaling AFTER Depth 30 belongs to a separate future analysis.

Do not touch post-D30 scaling in this pass either.

## 4. Boss HP stays unchanged

Do NOT solve the D1 ammo issue by lowering boss HP.

## 5. Ammo caps stay unchanged

Do NOT increase:
- Light cap
- Medium cap
- Heavy cap
- Shell cap

## 6. Blasters need only a mild buff

The automated validation found blasters weaker than expected.

The owner's actual playtest says:
- blasters are useful
- they are not badly underpowered
- they could use a small quality/balance improvement
- a large DPS correction would be inappropriate

Therefore:
- apply only a conservative buff
- prefer heat/cooling feel over raw damage inflation
- preserve the identity: no conventional ammo, but heat management

---

# PRIMARY OBJECTIVE

Implement and verify exactly two balance adjustments:

1. A SMALL Depth-1 ammo-economy improvement, targeted around the owner-observed ~10–15 Light-round shortfall in a normal mixed P9 + Field Knife run.

2. A SMALL blaster usability/balance buff, preferably through heat/cooling behavior rather than a large raw-damage increase.

Everything else is frozen unless a literal implementation bug is discovered.

---

# PHASE 1 — RE-VERIFY CURRENT VALUES

Before editing, inspect the current authoritative values after the previous completed passes.

Record:
- starter P9 stats
- starter Field Knife stats
- starter Light Ammo amount
- Light Ammo cap
- Supply Chest frequency
- Supply Chest ammo roll rules
- useful-ammo weighting
- other guaranteed/pre-boss ammo opportunities
- D1 room count distribution
- D1 boss HP
- P9 practical ammo need
- current blaster damage
- blaster heat per shot
- cooling rate
- overheat/lockout rules
- heat recovery timing
- relevant affix interactions

Create:
`TestResults/D1AmmoBlasterFineTune/baseline_before.csv`

Do not reuse stale pre-stat-fix values.

---

# PHASE 2 — D1 AMMO: SMALL CORRECTION ONLY

The goal is NOT to make full-pistol clearing free.

The target scenario is:

Fresh profile
+ starter P9 Ranger
+ starter Field Knife
+ normal mixed-weapon use
+ normal accuracy
+ typical D1 main path
→ reach the boss with approximately 10–15 more usable Light rounds than the current real-play experience

The correction should be small enough that:
- a player who deliberately uses only the pistol still pays for that choice
- melee remains relevant
- ammo conservation remains meaningful
- the player is not expected to reach the boss with a nearly full Light reserve
- no ammo cap changes are required
- no boss HP change is required

## Preferred adjustment order

Investigate these in order and choose the smallest clean intervention:

### Option A — Small D1 supply adjustment
Prefer a small increase in usable Light Ammo through existing D1 loot/supply logic.

Examples:
- slightly increase the D1 useful-ammo quantity
- slightly improve the guaranteed pre-boss useful-ammo floor
- slightly bias an existing D1 supply opportunity toward the equipped primary ammo type

Do NOT create a large new reward source.

### Option B — Small starter ammo adjustment
Only if the current loot path cannot produce a precise/clean correction.

A starter-ammo increase is acceptable only if it remains close to the observed 10–15-round shortfall.

Do not add 30–60 rounds simply because the simulation can consume them.

### Option C — Tiny deterministic pre-boss safety floor
Only if A/B are clearly worse architecturally.

If used:
- it must be subtle
- it must not feel like the game is secretly refilling the player
- it must use an existing visible loot/supply source
- it must not bypass inventory rules

## Explicitly forbidden solutions

DO NOT:
- lower boss HP
- increase Light cap
- add automatic ammo regeneration
- make every enemy drop ammo
- guarantee full reserve before boss
- remove ammo scarcity
- nerf the Field Knife
- reduce room enemy counts
- reduce D1 encounter threat just to save bullets

---

# PHASE 3 — D1 AMMO VALIDATION

Run deterministic fresh-profile D1 samples AFTER the chosen adjustment.

Minimum:
- 30 deterministic runs
- all 3 biomes represented where generation permits
- normal mixed P9 + Field Knife usage
- normal accuracy profile
- same deterministic seed manifest as previous validation where possible

Record:
- start Light Ammo
- Light Ammo found
- Light Ammo spent in rooms
- melee contribution
- ammo before boss
- ammo spent on boss
- ammo after boss
- whether melee was used at boss
- whether melee was required at boss
- total depth duration

Create:
`TestResults/D1AmmoBlasterFineTune/d1_ammo_after.csv`

Primary acceptance target:

The median/typical normal mixed-weapon run should gain roughly 10–15 additional usable Light rounds relative to the verified current baseline.

Do not optimize to exact arithmetic if RNG/room mix makes that unreasonable.

The important behavioral target is:

> Mixed-weapon play feels supported; pistol-only room clearing still has a cost.

Also run:
- pistol-heavy profile
- melee-heavy profile

The pistol-heavy profile should NOT become resource-neutral.

---

# PHASE 4 — BLASTER MILD BUFF

Do not perform a major blaster redesign.

Preserve:
- zero conventional ammo use
- heat as the primary limiting mechanic
- current class identity
- existing projectile behavior
- existing visuals/audio
- existing affix/stat consumers

Preferred tuning axis:
1. Heat recovery / cooling
2. Heat-per-shot
3. Overheat recovery feel

Raw damage should be the last resort.

## Target

The buff should be noticeable but modest.

The owner already considers blasters useful.

Aim for a small improvement in practical uptime / flow, not a large paper-DPS jump.

A reasonable engineering target is approximately:
- ~5–10% improvement in practical sustained output OR
- a similarly modest reduction in heat downtime

Do not exceed that range without evidence that the actual implementation behaves differently than expected.

This is guidance, not permission to force a specific percentage if the game's heat cycle requires a different minimal step.

## Preferred approach

If current blasters spend approximately half or more of their cycle in heat lockout, prefer a small cooling-rate increase or small heat-per-shot reduction that:
- shortens downtime
- preserves the need to manage heat
- does not allow indefinite full-rate firing
- keeps blaster identity distinct from conventional firearms

Do NOT turn blasters into zero-ammo assault rifles with no meaningful drawback.

---

# PHASE 5 — BLASTER VALIDATION

Measure every blaster before and after.

Record:
- raw damage
- fire rate
- heat per shot
- cooling rate
- time to high heat / overheat
- shots per heat cycle
- lockout duration
- percentage of cycle spent unable to fire
- practical sustained DPS
- 30-second damage output
- 60-second damage output
- relevant affix interaction
- recovery from partial heat
- recovery from full heat

Create:
`TestResults/D1AmmoBlasterFineTune/blaster_before_after.csv`

Acceptance:
- practical flow is modestly improved
- heat still matters
- no infinite full-rate firing
- no large raw-DPS leap
- no ammo-system interaction
- no new dominance over conventional weapon classes solely because ammo is free

---

# PHASE 6 — FREEZE CHECKS

Explicitly verify that the following did NOT change:
- Field Knife damage
- Field Knife attack speed
- Field Knife reach
- Field Knife arc
- all melee values unless necessary for test-only harness
- D1–D30 enemy HP scaling
- D1–D30 boss HP scaling
- post-D30 scaling
- Light Ammo cap
- Medium Ammo cap
- Heavy Ammo cap
- Shell cap
- boss base HP
- room encounter counts
- room threat budgets
- weapon damage outside blasters
- non-blaster weapon fire rate
- loot rarity curves
- XP values
- coin values
- merchant prices
- event prices
- depth-heal rules
- graphical inventory
- HUD
- player movement/dash
- aim assist
- knockback/stagger tuning from the previous pass
- save/persistence
- progression
- non-combat rooms
- audio
- biome generation
- multiplayer code

Create:
`TestResults/D1AmmoBlasterFineTune/frozen_values_check.csv`

Any unexpected change is a failure.

---

# PHASE 7 — REGRESSION TESTS

Run:
- EditMode
- PlayMode
- all production validators
- StatConsumerIntegrityValidator
- item/content validators
- save/load validation
- relevant combat tests
- relevant loot/ammo tests
- relevant blaster tests
- fresh-profile smoke
- built-player smoke on the available platform

Use deterministic seeds.

Known pre-existing flaky tests:
- clock/seed-sensitive CombatAimCollisionProof-related checks
- seed-dependent containment/non-combat smoke checks

If they fail:
- report separately
- rerun deterministically where possible
- do not hide them
- do not treat them as caused by this pass without evidence

---

# PHASE 8 — PLAYER-FEEL PROOF

Create a short owner playtest checklist:

`TestResults/D1AmmoBlasterFineTune/HUMAN_PLAYTEST_CHECKLIST.md`

Keep it focused.

Include:
1. In a fresh D1 run, did mixed P9 + Field Knife play leave you only slightly ammo-constrained at the boss?
2. Did you feel rewarded for using both weapons?
3. If you intentionally cleared almost everything with the pistol, did ammo become noticeably tighter?
4. Did the Field Knife still feel unchanged?
5. Did the boss remain the same difficulty apart from slightly better ammo availability?
6. Did the blaster feel a little smoother?
7. Did heat still force pauses / management?
8. Did the blaster avoid feeling obviously stronger than comparable ammo weapons?
9. Did any ammo pickup pattern feel artificial or overly generous?
10. Did the run still preserve resource tension?

Do not ask generic "is it fun?" questions.

---

# PHASE 9 — REPORT

Create:
`production/D1_AMMO_BLASTER_FINE_TUNING_REPORT.md`

Structure:

# RUINRAIL — D1 AMMO / BLASTER FINE-TUNING REPORT

## 1. Executive Summary
## 2. Owner Playtest Constraints
## 3. Baseline Before
## 4. D1 Ammo Change
## 5. Why This Intervention Was Chosen
## 6. D1 Ammo Before / After
## 7. Pistol-Heavy vs Mixed-Weapon Results
## 8. Blaster Change
## 9. Blaster Before / After
## 10. Frozen Systems Verification
## 11. Automated Tests
## 12. Built-Player Evidence
## 13. Human Playtest Checklist
## 14. Known Pre-Existing Flakes
## 15. Files Changed
## 16. Remaining Open Questions
## 17. Final Status

Explicitly state:
- exact ammo change
- exact blaster change
- why neither change was larger
- confirmation that Field Knife was not altered
- confirmation that D1–D30 depth scaling was not altered
- confirmation that boss HP was not altered
- confirmation that ammo caps were not altered

---

# REQUIRED ARTIFACTS

Create:
- `production/D1_AMMO_BLASTER_FINE_TUNING_REPORT.md`
- `TestResults/D1AmmoBlasterFineTune/baseline_before.csv`
- `TestResults/D1AmmoBlasterFineTune/d1_ammo_after.csv`
- `TestResults/D1AmmoBlasterFineTune/blaster_before_after.csv`
- `TestResults/D1AmmoBlasterFineTune/frozen_values_check.csv`
- `TestResults/D1AmmoBlasterFineTune/HUMAN_PLAYTEST_CHECKLIST.md`
- deterministic logs / captures as appropriate

---

# NON-GOALS

DO NOT in this pass:
- rebalance all 33 weapons
- nerf melee
- change Field Knife
- change boss HP
- change D1–D30 depth scaling
- change post-D30 scaling
- increase ammo caps
- add global enemy ammo drops
- change XP scaling
- change coin scaling
- change rarity progression
- redesign Return vs Descend
- add deepest-depth tracking
- change bosses
- change boss attack selection
- solve boss kiting
- redesign biomes
- change black-void presentation
- change audio mix
- restructure co-op
- connect UGS
- refactor ExpeditionScene broadly

Those are separate future passes.

---

# FINAL SUCCESS CONDITIONS

This pass is COMPLETE only if:

1. D1 ammo receives only a small correction consistent with the owner's ~10–15-round shortfall.
2. Mixed P9 + Field Knife play remains the intended resource pattern.
3. Pistol-only clearing still has a meaningful ammo cost.
4. Field Knife is unchanged.
5. Boss HP is unchanged.
6. Ammo caps are unchanged.
7. D1–D30 depth scaling is unchanged.
8. Post-D30 scaling is unchanged.
9. Blasters receive only a mild buff.
10. Heat remains a meaningful constraint.
11. No broad weapon rebalance occurred.
12. Deterministic before/after evidence exists.
13. Frozen-value verification passes.
14. Relevant test/validator/build gates are truthful and documented.
15. A focused human playtest checklist exists.

If complete, print exactly:

`D1_AMMO_BLASTER_FINE_TUNING_COMPLETE`

and:

`production/D1_AMMO_BLASTER_FINE_TUNING_REPORT.md`

If a repository-local requirement remains unresolved, print:

`D1_AMMO_BLASTER_FINE_TUNING_INCOMPLETE`

and explain the exact blocker.

Unavailable external platform modules are not repository-local failures, but must be reported truthfully.
