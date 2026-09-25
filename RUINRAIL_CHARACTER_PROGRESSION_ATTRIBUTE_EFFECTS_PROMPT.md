# RUINRAIL — CHARACTER PROGRESSION / ATTRIBUTE EFFECTS END-TO-END FIX PASS
## Execute from the CURRENT repository state after DEPTH_SETTINGS_DESCRIPTIONS_NONCOMBAT pass

This is a focused progression/runtime-correctness pass based on a real playtest issue.

Observed in the actual game:

- Character progression can be upgraded from the Main Menu using the existing XP/progression flow.
- The upgrade transaction/UI appears to work.
- However, the purchased attribute ranks do not appear to affect the player in actual gameplay.
- Concrete example: **Vitality was upgraded multiple times, but no meaningful effect was visible in the run.**

Do NOT treat this as a Vitality-only bug.

Audit and repair the COMPLETE progression pipeline for EVERY character attribute/stat in the current project.

Do NOT ask for intermediate approval.
Do NOT create a new large task series.
Do NOT refactor unrelated systems.
Do NOT rebalance the progression system unless repository data is clearly broken.
Do NOT invent new attribute effects.

Use the authoritative repository definitions and design documentation for all progression values/effects.

Write the final report to:

`production/CHARACTER_PROGRESSION_ATTRIBUTE_EFFECTS_REPORT.md`

---

# 0. READ FIRST — REPOSITORY TRUTH

Read the latest/current versions of:

- `CLAUDE.md`
- `CLAUDE_START_HERE.md`
- `technical/117_CODING_RULES_FOR_CLAUDE.md`
- `technical/118_TESTING_STRATEGY.md`
- all current progression/player-level/stat documentation
- all current character progression data/configs
- all current XP / Level / Skill Point / Attribute systems
- Main Menu progression UI and view models
- profile/save persistence code
- PlayerStats / stat aggregation code
- PlayerRig / PlayerStatsBinder / loadout/stat composition code
- armor/accessory modifier code
- combat stat consumers
- movement/dash stat consumers
- ammo/reload/fire-rate stat consumers
- effective max-health calculation
- run-start full-HP logic
- next-depth full-HP logic
- HUD stat/HP presentation
- network player stat replication/authority code where relevant

Also read the latest reports that touched runtime player stats and HP, including where present:

- `production/START_HP_DASH_AND_ICON_HUD_REPORT.md`
- `production/COMBAT_AIM_COLLISION_AND_FINAL_VISUAL_FIX_REPORT.md`
- `production/LOOT_AMMO_AUDIO_RUNTIME_FIX_REPORT.md`
- `production/ECONOMY_CONTAINMENT_DEATH_PROJECTILES_QOL_REPORT.md`
- `production/DEPTH_SETTINGS_DESCRIPTIONS_NONCOMBAT_QOL_REPORT.md`

Repository truth wins over assumptions from older reports.

The historical design target is a six-attribute progression system with capped ranks and persistent character progression, but derive the exact current stat names, caps, costs and effects from the repository.

Do not hardcode stale assumptions if the current authoritative data differs.

---

# 1. HARD SCOPE

Fix the complete persistent character-attribute system so that:

1. purchasing/upgrading an attribute in the Main Menu actually changes the persistent character progression state;
2. the correct XP / level / skill-point cost is paid exactly once;
3. the upgraded rank persists across save/load and game restart;
4. every attribute's effect is incorporated into authoritative derived player stats;
5. those derived stats are actually consumed by gameplay systems in the run;
6. the HUD/details UI reflects the real resulting values where applicable;
7. no later loadout/equipment/stat-binding step overwrites or discards progression bonuses;
8. co-op/networked player stats preserve the same authoritative effects;
9. rank caps and progression limits remain correct;
10. Vitality visibly and mechanically affects the player's effective maximum HP according to its authoritative definition.

Do NOT fix only UI text.

Do NOT accept “the attribute rank increased in the profile” as completion if gameplay remains unchanged.

---

# 2. FIRST: ENUMERATE THE AUTHORITATIVE ATTRIBUTE SYSTEM

Before changing code, derive the complete current attribute list from repository data.

Create a matrix containing for EVERY attribute:

- internal ID;
- player-facing name;
- current rank;
- maximum rank;
- purchase/upgrade cost rule;
- authoritative gameplay effect per rank;
- exact source/config defining the effect;
- derived stat(s) affected;
- gameplay system(s) that consume those stats;
- whether the effect stacks additively/multiplicatively according to current implementation/design;
- persistence field;
- network/runtime propagation path.

The project historically expects **six attributes** and a capped progression model, but verify the actual current repository state.

Write the machine-readable matrix to:

`TestResults/CharacterProgressionAttributeProof/attribute_effect_matrix.csv`

Do NOT invent effects for any missing/undefined stat.

If an attribute exists in UI but has no authoritative runtime effect:
- treat that as a repository-local defect;
- locate intended behavior in current docs/data;
- implement that intended behavior;
- document the source used.

If intent is genuinely absent/contradictory:
- do not invent balance;
- mark the exact ambiguity in the report and use the narrowest implementation supported by current repo evidence.

---

# 3. TRACE THE FULL UPGRADE TRANSACTION

Trace the Main Menu progression flow end-to-end:

1. current XP / progression currency displayed;
2. attribute selected;
3. cost resolved;
4. affordability checked;
5. upgrade confirmed;
6. XP / point cost deducted;
7. attribute rank incremented;
8. cap enforced;
9. profile marked dirty;
10. save written;
11. UI refreshed;
12. derived stat preview/current value refreshed;
13. run launched;
14. profile progression read;
15. persistent bonuses composed with base stats;
16. loadout/armor/accessory bonuses composed;
17. authoritative PlayerStats produced;
18. gameplay consumers use those stats.

Explicitly investigate:

- UI rank changing without profile state changing;
- profile changing but save not being written;
- save loading stale/default progression;
- attribute ranks stored but ignored by `PlayerStatsBinder`;
- progression modifiers being calculated and then overwritten by equipment binding;
- wrong order of `StatsBinder.Bind()` / PlayerRig composition;
- base stats being reconstructed after progression modifiers were applied;
- progression effects only affecting a menu preview object;
- stat caches created before save/profile load;
- run scene using a different Profile/Progression instance;
- local player using defaults while UI modifies another object;
- network prefab using default stats;
- modifiers not invalidating/recalculating derived values;
- integer/float conversion truncation;
- rank indexing off by one;
- only rank 1 being applied regardless of higher ranks;
- effect applied twice after scene reload;
- cost paid twice through duplicate callbacks;
- upgrade event not emitted after save/load;
- UI showing stale derived values.

Find the actual root cause(s).

---

# 4. VITALITY — REQUIRED DEEP VERIFICATION

Vitality is the known real-play failure and must receive explicit end-to-end verification.

Use the authoritative Vitality definition from repository data.

Do NOT assume the amount per rank.

Verify:

- Rank 0 baseline EffectiveMaxHP;
- Rank 1 effect;
- multiple ranks;
- maximum rank;
- interaction with armor that modifies Max HP;
- interaction with accessories/modifiers affecting HP;
- correct stacking/order;
- actual `PlayerHealth.MaxHealth` / equivalent value in the run;
- HUD max-HP value;
- healing cap;
- damage/death threshold;
- run-start full-heal behavior;
- new-depth full-heal behavior.

Very important:

The game already has runtime rules that set:

`CurrentHP = EffectiveMaxHP`

at:
- fresh expedition start;
- successful transition into a new dungeon depth.

After this progression fix, those rules MUST use the Vitality-modified EffectiveMaxHP.

Example only:
if base/equipment/progression together produce 135 Max HP, the player must enter the run/depth at **135/135**, not 100/135 or 120/135.

Do not hardcode any example number.

Test damaged/heal behavior as well:
- progression may raise MaxHP;
- it must not create invalid current HP values mid-run through accidental menu/stat refresh paths.

---

# 5. EVERY OTHER ATTRIBUTE — REAL GAMEPLAY EFFECT REQUIRED

For each remaining character attribute, verify its ACTUAL intended effect.

Do not assume names/effects from memory.

For each attribute:

1. identify its authoritative definition;
2. identify exact derived stat(s);
3. identify all relevant gameplay consumers;
4. verify rank 0 vs rank 1 vs multiple ranks vs max rank;
5. verify the effect is measurable in a real run;
6. verify combination with gear/accessory/weapon modifiers;
7. verify save/load;
8. verify no double application.

Examples of systems that may need verification depending on the actual attribute definitions:

- movement speed;
- dash properties;
- weapon damage;
- fire rate;
- reload speed;
- critical chance/damage;
- accuracy/spread;
- ammo capacity;
- pickup/economy modifiers;
- damage reduction;
- healing effectiveness;
- interaction speed;
- cooldowns;
- any other current derived stat.

These are examples only.

Only implement the actual six-stat design present in the repository.

---

# 6. DERIVED STAT COMPOSITION — SINGLE AUTHORITATIVE PIPELINE

There must be one understandable composition order for player stats.

Audit and document the actual order.

The intended architecture should conceptually preserve:

1. character/base stats;
2. persistent progression/attribute modifiers;
3. equipped armor/accessory/loadout modifiers;
4. temporary runtime buffs/debuffs;
5. clamping/final derived stats.

Do not duplicate progression calculations separately inside individual gameplay systems unless the existing architecture explicitly requires it.

Prefer one authoritative derived-stat source.

A later stage must NOT silently erase an earlier persistent progression modifier.

Add regression tests for composition order.

---

# 7. MAIN MENU PROGRESSION UI — MAKE THE EFFECT UNDERSTANDABLE

The upgrade UI currently allows leveling, but the player must be able to understand what the upgrade actually does.

For each attribute, the Main Menu progression view must show:

- attribute name;
- current rank / max rank;
- current upgrade cost;
- concise accurate description;
- exact effect or meaningful value where supported by authoritative data;
- next-rank effect/preview when not at cap;
- maxed state at cap.

Examples of good presentation:

`VITALITY 3 / 10`
`Maximum Health: 115 → 120`

or an equivalent presentation based on actual data.

Do not display invented values.

For percentage stats, format percentages correctly.

At max rank:
- clearly show `MAX`;
- disable purchase;
- do not deduct XP.

When unaffordable:
- clear visual disabled/unaffordable state;
- no cost deducted.

Do not redesign the entire Main Menu.
Use the established RUINRAIL UI language and current progression screen.

---

# 8. XP / LEVEL / SKILL-POINT ECONOMY CORRECTNESS

Audit the existing progression economy.

The project historically has a capped character level/attribute system, but use current repository truth.

Verify:

- XP total cannot go negative;
- required cost is checked authoritatively;
- purchase deducts exactly once;
- rejected purchase deducts nothing;
- max-rank purchase is rejected;
- duplicate button/input events cannot buy twice accidentally;
- save interruption/reload cannot duplicate rank or refund cost incorrectly;
- progression cap is enforced;
- total points/ranks cannot exceed intended maximum;
- existing earned progression remains intact.

Do NOT change the actual balance/cost curve unless current code disagrees with authoritative progression data.

If UI calls this XP, skill points, level points, or another current term:
- preserve the authoritative terminology;
- do not create a second progression currency.

---

# 9. SAVE / LOAD / PROFILE MIGRATION

Persistent progression must survive:

- leaving the Main Menu;
- starting a run;
- returning to Shelter;
- returning to Main Menu;
- application restart;
- save reload.

Audit current save version/schema.

If older saves can contain attribute ranks that were previously stored but not applied:
- preserve them and begin applying them correctly.

If fields are missing in older saves:
- migrate safely to defaults;
- do not reset existing progression.

If progression data moved/changed:
- add explicit migration/version handling according to current save architecture.

Tests:
- new profile;
- existing profile;
- save/load;
- maxed attribute;
- multiple upgraded attributes;
- old/default missing fields where applicable.

---

# 10. RUN-TIME STAT PROOF — NOT JUST UNIT TESTS

For EVERY attribute, produce runtime evidence that the value changes the intended system.

Create:

`TestResults/CharacterProgressionAttributeProof/runtime_attribute_evidence.txt`

For each attribute record at minimum:

- rank tested;
- baseline derived value;
- upgraded derived value;
- expected value from authoritative formula/data;
- actual runtime value;
- relevant gameplay consumer;
- PASS/FAIL.

Where practical, include behavioral evidence, e.g.:
- Vitality -> MaxHP/HUD/heal cap;
- speed stat -> measured movement distance over fixed time;
- reload modifier -> measured reload duration;
- damage stat -> actual damage applied to controlled target;
- cooldown stat -> measured cooldown;
- etc.

Use whatever is appropriate for the actual attribute definitions.

Do NOT fabricate behavioral metrics for attributes that affect a different mechanic.

---

# 11. EQUIPMENT / PROGRESSION INTERACTION TEST MATRIX

Progression must work together with the existing equipment system.

At minimum test:

- no armor/accessory + progression;
- armor only;
- accessory only;
- progression + armor;
- progression + accessory;
- progression + armor + accessory;
- starter loadout;
- non-starter loadout;
- item swap during run where allowed;
- next-depth rebuild.

For stats where the relevant gear does not exist, skip that exact combination and document why.

The final value must follow the authoritative stacking formula.

---

# 12. RUN START / DEPTH TRANSITION REGRESSION PROTECTION

Do not regress previous fixes.

Verify:

## Fresh expedition
After PlayerStats is fully composed:

`CurrentHP = EffectiveMaxHP`

including Vitality and equipment.

## Descend to new depth
After the new depth/player runtime stats are valid:

`CurrentHP = EffectiveMaxHP`

including Vitality and equipment.

## Same-depth operations
Do NOT full-heal from:
- room entry;
- inventory open/close;
- equipment refresh;
- stat UI refresh;
- pause;
- save event.

Add explicit tests for these cases.

---

# 13. COMBAT / MOVEMENT / HUD CONSUMERS

For every derived stat modified by progression, inspect the real consumer.

Examples:

- Damage must be read by the real damage calculation.
- Reload modifiers must alter the actual reload timer.
- Movement modifiers must alter real movement velocity.
- MaxHP must alter real health capacity.
- Defense must alter actual damage received.
- Cooldowns must alter the actual authoritative cooldown.

A tooltip or derived-stat object changing while gameplay continues to use a hardcoded/default constant is a FAIL.

Search for hardcoded base values that bypass the final derived stat.

Where a hardcoded value is correct by design, leave it alone and document why.

---

# 14. NETWORK / CO-OP SAFETY

Progression is persistent player state and must not become client-exploitable.

Verify:

- local UI cannot arbitrarily set authoritative combat stats;
- host/server-authoritative gameplay receives the legitimate progression-derived values;
- remote player representation uses the correct relevant replicated state;
- reconnect/recomposition does not lose or double progression modifiers;
- different players may have different progression ranks without cross-contamination.

If live UGS/Relay remains unavailable:
- use the deterministic/fake network harness already present;
- do not claim live Relay verification.

Do not redesign networking.

---

# 15. ATTRIBUTE DESCRIPTION / TOOLTIP CONSISTENCY

The recent item-description pass introduced stronger description validation.

Apply the same principle to character attributes.

Every attribute must have accurate player-facing text describing its actual effect.

Add a validator/test that catches:

- empty description;
- placeholder text;
- description with stale values;
- max-rank value inconsistent with data;
- UI next-rank preview inconsistent with the authoritative formula.

Prefer structured data-driven formatting over manually duplicated numeric strings where practical.

---

# 16. REQUIRED AUTOMATED TESTS

Add/extend tests for:

## Upgrade transaction
- affordable purchase succeeds;
- XP/points deducted exactly once;
- rank increments exactly once;
- unaffordable purchase rejected;
- max rank rejected;
- rapid/double input cannot double-buy.

## Persistence
- save/load preserves every attribute;
- run start reads persisted values;
- reload does not duplicate effects.

## Every attribute
For all authoritative attributes:
- rank 0;
- rank 1;
- multiple ranks;
- max rank;
- correct derived value;
- actual gameplay consumer uses it.

## Vitality
- actual MaxHP changes;
- HUD changes;
- armor + Vitality composition;
- full HP at run start;
- full HP on next depth;
- heal cap uses modified MaxHP.

## Composition
- gear + progression;
- temporary modifier + progression where relevant;
- no overwrite;
- no double-application.

## UI
- correct rank;
- correct cost;
- correct description;
- correct next-rank preview;
- MAX state;
- disabled state.

## Network harness
- per-player progression isolation;
- authoritative combat effect;
- reconnect/rebuild does not lose/double modifiers.

---

# 17. PROGRESSION SWEEP

Run an automated progression sweep across all attributes.

For every attribute:

- test every legal rank from 0 through max rank;
- compute expected derived value from authoritative data;
- compare to runtime derived value;
- verify monotonic/directional behavior where the definition requires it;
- ensure no rank is skipped;
- ensure max rank does not overflow.

Write:

`TestResults/CharacterProgressionAttributeProof/attribute_rank_sweep.csv`

Columns:

- attribute_id
- display_name
- rank
- max_rank
- cost_if_next
- expected_effect_value
- actual_effect_value
- derived_stat
- result

If the effect cannot be represented as a single scalar:
- use a concise serialized structured value or one row per affected stat.

All legal ranks for every attribute must PASS.

---

# 18. BUILT-PLAYER PROOF

Save evidence under:

`TestResults/CharacterProgressionAttributeProof/`

Capture at minimum:

1. Main Menu progression screen before upgrade;
2. Vitality upgrade purchase;
3. Vitality rank increased and next effect preview updated;
4. player entering run with Vitality-modified full HP;
5. HUD showing Vitality-modified max HP;
6. representative second attribute upgrade;
7. real gameplay evidence of second attribute effect;
8. representative third attribute effect;
9. progression retained after return/reload;
10. max-rank attribute correctly showing MAX;
11. unaffordable upgrade disabled/rejected;
12. combined progression + equipment derived stat evidence.

Additionally create runtime text/CSV evidence for ALL attributes, even when one screenshot cannot visually prove the mechanic.

Do not fake values into screenshots.

---

# 19. VALIDATION GATES

After the final source change run all relevant available gates:

- strict EditMode;
- strict PlayMode;
- FinalProductionValidator;
- ContentCountValidator;
- PresentationValidator;
- AssetPipelineValidator;
- ReleasePathScan;
- progression-specific validators/tests;
- save/migration tests;
- exploit/idempotency tests;
- network authority tests;
- existing HP/start/depth regression tests;
- relevant combat/movement tests.

Requirements:
- failed = 0;
- no stale XML;
- no missing scripts/references;
- no progression attribute without a functioning runtime effect;
- no mismatch between UI rank/effect and runtime rank/effect.

Do not hide failures by changing tests to accept broken behavior.

If an existing test reveals a real regression, fix the source.

If a test itself is demonstrably flaky on unchanged baseline:
- prove that against baseline;
- isolate the test issue;
- do not use that fact to excuse an actual progression failure.

---

# 20. RELEASE BUILD + SMOKE

On a machine with Windows Standalone Support:

- clean Windows x64 NON-DEVELOPMENT build;
- headless smoke;
- windowed smoke.

If this machine still lacks Windows Standalone Support:

- do NOT install platform modules without explicit permission;
- create the supported non-development standalone build;
- run equivalent headless/windowed smoke where supported;
- mark Windows x64 as `NOT RUN`.

Smoke must verify at minimum:

- purchase an attribute;
- leave/re-enter progression screen;
- launch expedition;
- upgraded effect is present;
- Vitality changes actual max HP;
- full-heal-on-run-start respects Vitality;
- descend to next depth;
- full-heal-on-depth-start respects Vitality;
- second/third representative attribute affects real gameplay;
- save/reload preserves progression;
- no exceptions.

---

# 21. FINAL REPORT

Write:

`production/CHARACTER_PROGRESSION_ATTRIBUTE_EFFECTS_REPORT.md`

Include:

1. authoritative attribute list;
2. exact root cause(s) of “rank increases but gameplay effect does not”;
3. all changed files;
4. progression transaction flow;
5. stat-composition order;
6. per-attribute effect and authoritative source;
7. Vitality before/after runtime examples;
8. every other attribute's runtime proof;
9. equipment/progression interaction results;
10. save/load/migration behavior;
11. network authority result;
12. `attribute_effect_matrix.csv` summary;
13. `attribute_rank_sweep.csv` summary;
14. tests added;
15. final EditMode/PlayMode counts;
16. validator results;
17. build target/result;
18. smoke result;
19. proof paths;
20. genuine remaining limitations.

Terminal status:

`CHARACTER_PROGRESSION_ATTRIBUTE_EFFECTS_COMPLETE`

only if EVERY repository-local attribute:
- upgrades correctly;
- persists correctly;
- changes the correct authoritative derived stat;
- has a measurable/verified real gameplay effect;
- is represented accurately in the Main Menu UI.

Use:

`CHARACTER_PROGRESSION_ATTRIBUTE_EFFECTS_INCOMPLETE`

if any repository-local attribute remains cosmetic, disconnected, incorrectly applied, incorrectly persisted, or unverified.

A missing Windows build module is a platform verification limitation; report that gate as NOT RUN rather than falsely claiming it. Do not confuse it with repository-local progression completeness.

Do not ask for approval during execution.

BEGIN NOW.
