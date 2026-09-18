# RUINRAIL — LOOT / AMMO ECONOMY / STARTER FALLBACK / AUDIO RUNTIME FIX PASS
## Execute from the CURRENT repository state after COMBAT_COLLISION_FIX_COMPLETE

This is a focused runtime-content and playability pass.

The player has manually observed three important problems in a real run:

1. Chests / meaningful loot containers appear to be absent or effectively absent during normal play.
2. Ammo economy can leave the player completely out of ammunition with no reliable way to continue using the starter loadout.
3. The game appears to have no audible music, ambience or gameplay SFX in the real built-player experience despite previous reports claiming the audio content exists.

Additionally, the starter loadout must always include a second weapon that requires no ammunition so a run cannot become functionally unplayable when the firearm runs dry.

Do NOT ask for intermediate approval.
Do NOT create a new task series.
Do NOT add unrelated features.
Do NOT claim success based only on definitions/assets/tests that never execute in the real runtime path.

Fix the actual runtime behavior, add regression coverage, validate with built-player evidence, and return one consolidated report only at the end.

---

# 0. READ FIRST

Read the latest/current versions of:

- CLAUDE.md
- CLAUDE_START_HERE.md
- technical/117_CODING_RULES_FOR_CLAUDE.md
- technical/118_TESTING_STRATEGY.md
- current loot / chest / room-content / dungeon-generation specifications
- current ammo / inventory / weapon / starter-kit specifications
- current audio / music / ambience / SFX specifications
- production/FINAL_PLAYABILITY_REGRESSION_REPORT.md
- production/COMBAT_AIM_COLLISION_AND_FINAL_VISUAL_FIX_REPORT.md
- production/POST_POLISH_REGRESSION_FIX_REPORT.md
- production/UI_AND_BIOME_POLISH_REPORT.md

Then inspect the current implementations for:

- SupplyChest
- LootSpawner
- LootSourceCatalog
- room content binding/composition
- Treasure / Loot / Weapon Cache / Cursed Chest / Locked Vault room logic
- ground pickups
- ammo pickups
- enemy loot drops
- merchant/shop ammo availability
- starter loadout / starter grant / wipe recovery logic
- weapon slot initialization
- ammo reserve initialization
- AudioService / AudioManager / audio bootstrap
- AudioListener
- AudioSource creation/routing
- mixer groups / volume settings
- music state machine
- ambience state machine
- SFX event bindings
- scene transitions / pause / focus handling
- release asset catalog / Addressables / Resources / direct references used by audio

Repository truth wins over older documents.

Create/update:

`production/LOOT_AMMO_AUDIO_RUNTIME_FIX_REPORT.md`

---

# 1. HARD SCOPE

Fix these four areas:

1. Chest / loot-source runtime presence
2. Ammo availability and economy
3. Starter loadout with guaranteed ammo-free secondary
4. Actual audible runtime audio: music + ambience + SFX

Do NOT redesign combat, enemy stats, weapon damage/rate/range, accepted art/VFX, dungeon topology, progression, save semantics, or the network authority model.

Do NOT solve this by flooding every room with ammo, giving infinite ammo, putting a chest in every room, forcing permanent max reserves, replacing authored audio with placeholder beeps, or inventing a new weapon when a suitable approved ammo-free weapon already exists.

---

# 2. CHEST / LOOT-SOURCE RUNTIME AUDIT

The codebase may already contain chest and loot systems. The observed problem is that a real player completed runs without seeing meaningful chest/container content.

Treat this as a runtime integration problem until proven otherwise.

For each relevant loot source, trace the complete path:

1. content definition exists;
2. room category/content rule references it;
3. dungeon generator selects the room/category;
4. runtime room composer instantiates the loot source;
5. placement resolves inside valid walkable room space;
6. chest/container GameObject is active and visible;
7. collider / interaction trigger is reachable;
8. interaction prompt works;
9. open action is accepted exactly once;
10. loot is generated from the correct table;
11. loot is visible/pickable;
12. network authority prevents duplicate rewards;
13. opened state does not reset accidentally during the same run.

Do not stop at “the prefab exists”.

---

# 3. REQUIRED LOOT-SOURCE BEHAVIOR

Preserve documented room-specific behavior if the current GDD/spec is more explicit. Where the repository is under-specified, use these conservative V1 requirements.

## 3.1 Treasure / loot-focused rooms

Any room explicitly authored/categorized as Treasure, Loot, Weapon Cache, Cursed Chest, Locked Vault, or an equivalent approved loot-special room must contain its corresponding meaningful reward source at runtime.

A Treasure/Loot room must never instantiate as an effectively empty room with no accessible reward.

Default if current spec does not define exact count:

- Treasure/Loot room: at least 1 guaranteed meaningful chest/container
- Weapon Cache: guaranteed weapon-cache reward source
- Cursed Chest room: guaranteed cursed chest/reward interaction
- Locked Vault: guaranteed vault reward source if the room is eligible/unlocked according to existing rules

Do not create duplicate reward sources if the room already has a different approved reward mechanism.

## 3.2 Ordinary rooms

Do NOT place a chest in every ordinary room.

If documented spawn rates exist, preserve them unless real generated runs prove they produce near-zero loot.

If under-specified, use an initial V1 target of approximately **25% of eligible ordinary non-boss rooms** containing a small supply container / minor loot source, selected deterministically by seed.

Placement must never block doors, activation volumes, player spawn points, or enemy spawn points.

## 3.3 Run-level presence target

Across a normal generated depth:

- the player should encounter multiple meaningful loot opportunities;
- a normal depth must not routinely produce zero chest/container interactions.

Validation:

- sweep at least **100 seeds per biome**;
- record chest/container count per generated depth;
- no valid normal depth should have zero total meaningful loot opportunities unless an explicit game rule requires it;
- special loot rooms must always instantiate their promised reward source.

If pure RNG permits a zero-loot depth, add the smallest deterministic loot-budget guarantee needed without changing room topology.

---

# 4. CHEST PRESENTATION / INTERACTION

Chests/containers must be visibly readable as interactable world objects.

Requirements:

- final existing RUINRAIL sprite/art, not debug boxes;
- correct world sorting;
- not hidden under floor/wall tilemaps;
- not spawned inside or behind inaccessible collision;
- obvious closed state;
- obvious opened state;
- one interaction prompt;
- one reward transaction;
- no duplicate opening;
- no repeated loot after leaving/re-entering;
- no invisible blocker after opening unless intended.

If the existing chest visual is already final, preserve it.

Add PlayMode proof for actual opening and reward pickup.

---

# 5. AMMO ECONOMY — GOAL

Ammo must remain meaningful and sometimes scarce. The player may need to swap weapons, use the ammo-free secondary, search/open loot, buy ammo when appropriate, and conserve expensive types.

But a normal run should not routinely become firearm-dead simply because the generated depth produced no useful ammo opportunities.

The target is scarcity without routine softlock.

---

# 6. AMMO SOURCES — AUDIT THE REAL RUNTIME PATH

Audit built-player behavior for:

- enemy ammo drops;
- chest/supply-container ammo;
- ground ammo pickups;
- treasure/loot-room rewards;
- merchant ammo inventory;
- weapon-cache behavior where applicable;
- boss/post-boss rewards if applicable;
- room-clear reward logic if present;
- starter reserve ammo.

For every source verify:

- definition exists;
- source is instantiated;
- amount is non-zero;
- correct ammo type is produced;
- pickup is reachable;
- reserve increments correctly;
- ammo caps remain enforced;
- no duplication exploit;
- network authority is correct.

---

# 7. AMMO ECONOMY TUNING RULES

Existing caps remain:

- Light: 180
- Medium: 120
- Heavy: 60
- Shells: 40
- Rockets continue using Heavy ammo according to current weapon rules.

Do not change these unless the authoritative repository spec already differs.

## 7.1 Relevant-ammo weighting

Where the current loot architecture supports it cleanly, ammo rewards should prefer ammo types relevant to weapons currently carried without becoming fully deterministic.

If no approved weighting exists, use:

- **70%** reward weight toward currently carried ammo types;
- **30%** general/random valid ammo.

Do not award invalid/nonexistent ammo types.

## 7.2 Practical-supply target

Use actual weapon ammo costs, enemy HP/density, and actual loot tables to measure supply.

For the starter-firearm path, model a representative player with reasonable but imperfect accuracy. Include misses. Do not count the ammo-free secondary as firearm sustainability.

Acceptance target across at least **300 generated depth simulations** spanning all 3 biomes:

- at least **85%** of representative runs provide enough relevant firearm ammo supply that the player does not spend the majority of the depth at zero primary reserve;
- poor accuracy / deliberate overuse can still run dry;
- median end-of-depth reserve must remain meaningfully below hard cap rather than permanently full;
- ammo pickups must not become visually or numerically excessive.

If the existing simulator cannot model this, create a small deterministic economy harness using actual combat and loot data.

Document before/after distributions.

## 7.3 Guaranteed opportunity floor

If pure random generation currently allows a depth with no usable ammo opportunities, add a small deterministic ammo-opportunity budget at depth realization.

Preferred minimum:

- at least **2 independent opportunities before the boss** where relevant ammo can reasonably appear;
- satisfy through existing chest/drop/room-loot systems;
- do not spawn obvious emergency ammo piles directly in front of the player;
- do not bypass normal loot presentation.

If current content already guarantees equivalent or better supply, do not add a second system.

---

# 8. STARTER LOADOUT — GUARANTEED AMMO-FREE SECONDARY

The starter loadout must always provide a fallback weapon that requires no ammunition.

## 8.1 Required default

Primary:
- use the current approved starter firearm, expected to be **P9 Ranger** if that remains repository truth.

Secondary:
- use the existing approved starter-appropriate ammo-free melee weapon.

Prefer:
- **Field Knife**, but only if a weapon definition with that exact identity already exists.

If Field Knife does not exist:
- select the existing lowest-tier/starter-appropriate approved melee weapon;
- do NOT invent a new weapon;
- document the exact weapon chosen.

## 8.2 Starter behavior

The ammo-free secondary must:

- occupy Secondary;
- require no ammo;
- be immediately swappable in-run;
- function against normal enemies;
- use existing melee logic;
- work with firearm reserve at 0;
- survive/reappear correctly through the existing new-profile/wipe starter recovery path;
- not duplicate on repeated initialization;
- follow existing starter-item sell/drop/risk restrictions.

Do not buff its damage solely because it is the fallback.

## 8.3 Tests

Add tests for:

- new profile starter loadout;
- post-wipe starter recovery;
- primary present;
- ammo-free secondary present;
- correct slot assignment;
- swap works;
- secondary damages enemy at reserve ammo = 0;
- no ammo consumed;
- no duplicate grants.

---

# 9. AUDIO — TREAT CURRENT SILENCE AS A RUNTIME BUG

Previous work reportedly created:

- 53 SFX
- 11 music tracks
- 6 stingers
- 3 ambience loops

Total: **73 authored audio roles**.

The current observed built-player experience appears silent.

Do NOT respond with “73/73 files exist”. Prove the audio graph is actually alive in the shipped runtime.

---

# 10. AUDIO BOOTSTRAP / LISTENER / MIXER AUDIT

Trace from application boot:

1. exactly one valid AudioListener for normal local gameplay;
2. persistent AudioService/AudioManager is composed;
3. required clips are on the release path;
4. mixer groups resolve;
5. master/music/SFX/ambience settings load;
6. fresh-profile defaults are non-zero unless spec says otherwise;
7. no accidental global mute;
8. AudioListener.pause is not left active;
9. pause/resume restores intended state;
10. focus loss/regain does not permanently mute;
11. Main Menu starts intended music/ambience;
12. Shelter starts intended music/ambience;
13. Dungeon starts appropriate music/ambience;
14. transitions stop/crossfade previous beds cleanly;
15. SFX events actually call playback;
16. AudioSources route to intended mixer groups;
17. 2D/spatial settings are correct for the game.

Explicitly check for:

- missing or disabled listener;
- multiple listeners;
- source volume = 0;
- mixer attenuation around mute;
- settings defaulting to zero;
- clip load failure;
- release-path failure;
- Play() never reached;
- audio service destroyed on scene transition;
- missing event subscription;
- listener on disabled camera;
- sources on inactive objects;
- headless-test assumptions leaking into real build.

Fix actual root cause(s).

---

# 11. REQUIRED AUDIBLE BEHAVIOR

## Music

At minimum:

- Main Menu: audible music bed;
- Shelter/Base: audible appropriate music bed;
- Dungeon exploration/combat: audible music appropriate to state/content;
- Boss encounter: boss transition/layer according to current audio design;
- transit/result moments: specified stingers where applicable.

Do not play all music simultaneously.

## Ambience

All three biomes must have audible ambience using existing authored roles.

Ambience must loop correctly, not restart every room unless intended, transition cleanly, and sit below primary SFX/music in the mix.

## SFX

Verify actual playback for representative high-value events:

- UI hover/click
- weapon fire
- reload
- empty/dry-fire if authored
- player hit
- enemy hit
- enemy death
- pickup
- chest open
- door/combat lock
- dash
- boss/event stinger where applicable

Use existing roles. No placeholder beeps when final roles exist.

---

# 12. AUDIO DEFAULT SETTINGS

For a fresh profile, unless authoritative settings say otherwise:

- Master > 0
- Music > 0
- SFX > 0
- Ambience > 0

Preserve user-adjustable settings.

Do not overwrite an intentional explicit user mute.

If old saves are missing/uninitialized audio keys and currently deserialize to zero, migrate absent values to documented defaults only when they are genuinely absent/uninitialized.

---

# 13. AUDIO RUNTIME VERIFICATION — NOT FILE EXISTENCE

Add runtime proof.

For music/ambience verify:

- clip != null;
- AudioSource active;
- isPlaying == true after startup window;
- source/category/master configuration is non-muted.

For SFX verify:

- event fires;
- valid clip resolves;
- playback voice/source is allocated;
- playback begins.

Where practical, inspect clip sample data and verify each of the 73 clips contains non-trivial, non-silent samples.

Do not claim audible hardware output from headless mode.

---

# 14. BUILT-PLAYER AUDIO PROOF

Audio must be exercised in a normal **windowed shipped executable**, not only isolated tests.

Run this sequence:

Main Menu
→ Shelter
→ Dungeon
→ fire weapon
→ reload
→ damage enemy
→ kill enemy
→ open chest
→ pickup loot/ammo
→ combat door lock/unlock
→ pause/resume
→ return to menu

Record runtime evidence to:

`TestResults/LootAmmoAudioProof/audio_runtime_evidence.txt`

For each relevant event include:

- scene/state;
- audio role;
- clip name/id;
- mixer group;
- source volume;
- master/category setting;
- isPlaying/start confirmation;
- timestamp.

If reliable loopback/output recording is available, capture a short WAV proof.

If not, do not fabricate one; use runtime source state + non-silent sample validation + windowed playback verification.

---

# 15. LOOT / AMMO BUILT-PLAYER PROOF

Capture deterministic proof of:

1. closed chest/container in an actual generated room;
2. chest opened;
3. loot spawned/presented;
4. ammo pickup before collection;
5. reserve ammo increased after collection;
6. Treasure/Loot room with guaranteed reward source;
7. starter inventory showing Primary + ammo-free Secondary;
8. ammo-free secondary active while firearm reserve is 0;
9. player damages an enemy with the ammo-free secondary;
10. representative merchant/ammo source if naturally available in the tested depth.

Save under:

`TestResults/LootAmmoAudioProof/`

Also write:

`TestResults/LootAmmoAudioProof/loot_seed_evidence.csv`

Columns:

- biome
- depth
- seed
- room_count
- chest_container_count
- special_loot_source_count
- ammo_source_opportunity_count
- relevant_ammo_quantity_budget

---

# 16. NETWORK / AUTHORITY

Do not regress co-op architecture.

For chests/ammo:

- host-authoritative reward generation;
- chest opens once;
- two clients cannot duplicate reward;
- ammo pickup consumed once;
- remote clients see opened state;
- pickup removal remains consistent.

For audio:

- local presentation audio need not network AudioSources;
- replicated gameplay events may trigger local SFX through existing event/state paths;
- avoid double-playing the same SFX on owner.

Use current fake/network harness if live UGS is unavailable.

Do not claim live Relay verification.

---

# 17. TESTS TO ADD

At minimum:

## Loot
- special loot room guarantees reward source;
- ordinary eligible room selection deterministic;
- chest placement never inside wall;
- chest reachable;
- chest opens once;
- loot generated;
- no duplicate network reward;
- 100+ seeds/biome do not produce zero meaningful loot opportunities per valid normal depth.

## Ammo
- each ammo source produces non-zero valid quantity;
- caps enforced;
- carried-weapon relevance weighting works;
- minimum opportunity budget works;
- 300-run economy simulation/report;
- no infinite ammo;
- no duplicate pickups.

## Starter
- approved starter primary;
- approved ammo-free secondary;
- correct slots;
- wipe/new-profile behavior;
- no duplicate starter grants;
- fallback works at zero firearm reserve.

## Audio
- exactly one listener;
- audio service survives scene transitions;
- 73 roles resolve;
- 73 clips non-silent;
- fresh defaults audible;
- explicit user mute preserved;
- menu music starts;
- shelter music/ambience starts;
- all three biome ambience paths start;
- dungeon music starts;
- representative SFX playback;
- pause/resume does not permanently mute;
- return-to-menu restores correct music;
- no duplicate persistent audio manager.

---

# 18. FULL VALIDATION GATES

After the final source change run strict EditMode and PlayMode:

- process exit code 0;
- XML exists;
- total > 0;
- passed > 0;
- failed = 0.

Also run:

- ContentCountValidator
- FinalProductionValidator
- PresentationValidator
- AssetPipelineValidator
- ReleasePathScan
- loot/content validators
- save/exploit hardening
- network pickup/chest tests
- ammo economy harness
- audio runtime tests

Do not accept stale XML.

Do not disable Windows Smart App Control automatically.

---

# 19. RELEASE BUILD + BUILT-PLAYER SMOKE

Produce a clean Windows x64 NON-DEVELOPMENT build.

Run:

- headless smoke for deterministic state checks;
- windowed shipped-player smoke for visual + audio runtime proof.

Require:

- chests/meaningful loot visibly occur in generated play;
- loot-focused rooms are not empty;
- chest opens;
- reward appears exactly once;
- ammo can be found;
- reserve changes correctly;
- ammo remains scarce but practical;
- starter loadout always includes ammo-free secondary;
- fallback remains usable at zero firearm ammo;
- music active;
- ambience active;
- representative SFX play;
- audio survives pause/scene transitions;
- no exceptions;
- no missing references.

---

# 20. IMPORTANT SUCCESS CRITERION

Do not mark this pass complete merely because:

- chest classes exist;
- loot tables exist;
- audio files exist;
- audio clips are referenced;
- isolated tests can instantiate prefabs.

Completion requires proof from the integrated runtime and shipped-player path.

---

# 21. FINAL REPORT

Write:

`production/LOOT_AMMO_AUDIO_RUNTIME_FIX_REPORT.md`

Include:

- actual root cause of missing/rare chests;
- where runtime composition failed or rates were insufficient;
- final loot-source rules/rates;
- seed-sweep results;
- actual ammo economy before/after;
- 300-run ammo simulation results;
- exact starter primary + secondary used;
- confirmation that secondary requires no ammo;
- actual root cause of silent audio;
- exact bootstrap/mixer/event fixes;
- proof that music/ambience/SFX run in shipped player;
- tests added and exact counts;
- build result;
- smoke result;
- proof paths;
- any genuine remaining blocker.

Terminal status:

`LOOT_AMMO_AUDIO_FIX_COMPLETE`

only if all repository-local issues above are fixed and verified in the integrated runtime.

Use:

`LOOT_AMMO_AUDIO_FIX_INCOMPLETE`

if any repository-local problem remains.

Missing UGS project linking is not a reason to leave this pass incomplete. Finish all local work and list live Relay separately.

Do not ask for approval during execution.

BEGIN NOW.
