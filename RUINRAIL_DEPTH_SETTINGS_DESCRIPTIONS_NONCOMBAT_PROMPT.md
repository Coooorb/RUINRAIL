# RUINRAIL — DEPTH HEAL / ITEM DESCRIPTIONS / SETTINGS UI / ENEMY COUNT / NON-COMBAT ROOM FIX PASS
## Execute from the CURRENT repository state after ECONOMY_CONTAINMENT_DEATH_PROJECTILES pass

This is a focused gameplay/QoL/runtime-integration pass based entirely on issues observed during real play.

Do NOT ask for intermediate approval.
Do NOT create a new task series.
Do NOT refactor unrelated systems.
Do NOT change unrelated weapon balance, enemy balance, progression, save semantics, inventory capacity, room topology, or network authority.

Fix the actual runtime behavior, add regression coverage, run all relevant validation/build gates available on this machine, capture built-player proof, and return one consolidated report only at the end.

Write the final report to:

`production/DEPTH_SETTINGS_DESCRIPTIONS_NONCOMBAT_QOL_REPORT.md`

---

# 0. READ FIRST

Read the latest/current versions of:

- CLAUDE.md
- CLAUDE_START_HERE.md
- technical/117_CODING_RULES_FOR_CLAUDE.md
- technical/118_TESTING_STRATEGY.md
- production/GRAPHICAL_INVENTORY_REWORK_REPORT.md
- production/START_HP_DASH_AND_ICON_HUD_REPORT.md
- production/ROOM_HUD_AUDIO_QOL_REPORT.md
- production/ECONOMY_CONTAINMENT_DEATH_PROJECTILES_QOL_REPORT.md if present
- current Depth / Transit / Descend Deeper transition code
- current PlayerStats / health / effective max-health calculation
- all ItemDefinition data and tooltip/description generation
- current Settings UI, settings model, persistence and input/rebinding code
- current RoomRuntime / encounter state / enemy-count tracking
- all non-combat room definitions, event interactables, prompts, event services, UI views and rewards

Repository truth wins over older reports.

---

# 1. HARD SCOPE

Fix these exact issues:

1. Entering a NEW dungeon depth must restore the player to full effective HP.
2. Audit and improve all player-facing item descriptions, especially Consumables.
3. Rework Settings into proper category pages/submenus where settings can actually be adjusted.
4. Active normal combat rooms must show how many enemies remain.
5. Broken Machine and ALL other non-combat room types must be audited and made actually functional end-to-end.

Do NOT expand scope into unrelated features.

---

# 2. FULL HP WHEN DESCENDING TO A NEW DEPTH

Observed requirement:

When the party chooses to **Descend Deeper** and enters a newly generated dungeon depth, each living player should begin that new depth at full effective HP.

This is separate from the already-fixed rule that a brand-new expedition begins at full effective HP.

## 2.1 Correct rule

On successful transition:

`Depth N -> Transit/Depth Transition -> Depth N+1`

after the player's current loadout/stat modifiers are resolved for the new depth:

`CurrentHP = EffectiveMaxHP`

for each player who is entering the new depth as a valid living expedition participant.

Use the authoritative current effective-max-health calculation.

Do NOT hardcode base HP.

Examples:
- 100 effective max -> enter next depth at 100/100
- 120 effective max because of armor/modifiers -> enter next depth at 120/120

## 2.2 Trigger exactly once

The heal must occur exactly once per successful depth transition.

Do NOT trigger full healing from:
- entering a new room;
- returning to a previously visited room;
- opening Transit UI;
- merely voting to descend before transition succeeds;
- scene reload/recomposition inside the same depth;
- inventory/equipment changes;
- pause/menu operations.

Do not create a repeatable heal exploit by reopening Transit or retriggering transition presentation.

## 2.3 Co-op

For co-op:
- apply on authoritative transition to the next depth;
- all valid participating players start the new depth at their own EffectiveMaxHP;
- do not resurrect a player through this rule if the authoritative run state says they are dead and not eligible to continue;
- preserve current downed/death/wipe semantics.

## 2.4 Tests

Add tests for:
- Depth 1 -> Depth 2 heals to full;
- 120 max HP -> 120/120;
- already-full player remains full;
- transition fires heal once;
- returning to Shelter does not use this depth-heal path;
- room transitions do not heal;
- Transit UI reopen does not heal;
- co-op players each use own EffectiveMaxHP;
- no resurrection exploit.

Document exact hook/order used.

---

# 3. COMPLETE ITEM DESCRIPTION AUDIT

Observed issue:

Not every item has a useful or accurate explanation of what it does.

This is especially noticeable for Consumables.

Perform a complete audit of every player-facing item definition in the current content.

Do NOT audit only the items currently visible in one test loadout.

## 3.1 Coverage

Audit all existing player-facing item categories, including at minimum:

- all weapons;
- all armor;
- all accessories;
- all consumables;
- ammo items/stacks where they have player-facing descriptions;
- Legendary/special items;
- starter items;
- any other ItemDefinition present in the authoritative catalog.

Use current repository counts, not stale hardcoded assumptions.

Every item must have a meaningful player-facing description.

## 3.2 Description correctness rule

Descriptions must be derived from the item's ACTUAL mechanics/data.

Do NOT invent effects.

For each item compare its text against:
- ItemDefinition values;
- modifiers;
- active effect implementation;
- duration;
- amount;
- cooldown/use restrictions;
- ammo type;
- weapon special;
- affixes;
- proc conditions;
- Legendary behavior.

If the existing description contradicts runtime behavior:
- fix the description OR the clearly unintended data-binding bug;
- do not silently change balance just to make text match.

## 3.3 Consumables — highest priority

Every consumable description must clearly tell the player:

- what happens when used;
- exact amount/percentage where relevant;
- duration where relevant;
- whether effect is instant or timed;
- important restriction/condition;
- whether it affects HP, movement, defense, damage, etc.

Examples of desired clarity:

BAD:
`Restores health.`

GOOD:
`Restore 35 HP instantly.`

BAD:
`Boosts speed for a while.`

GOOD:
`Gain +20% move speed for 8 sec.`

Use actual repo values, not these example numbers.

If a Consumable has a multi-part effect:
- state both important effects concisely.

## 3.4 Weapons

Weapon description/details should expose the relevant identity/mechanic without becoming a paragraph.

Ensure the graphical Inventory / Merchant / Weapon Cache details can communicate, where applicable:
- weapon class;
- damage;
- fire rate;
- magazine;
- reload;
- range;
- ammo type;
- important special behavior;
- Legendary special.

Do not duplicate every stat into prose if the stat panel already presents it.

The description field should explain the weapon's identity/special mechanic.

## 3.5 Armor / Accessories

Clearly state actual mechanical effect(s).

Examples:
- Max HP modifier;
- damage reduction;
- move speed;
- reload modifier;
- ammo modifier;
- conditional bonuses.

Use actual numbers where useful.

Avoid vague text like:
`Improves survivability.`

if exact behavior is available.

## 3.6 Presentation

Descriptions must render correctly in:
- Inventory;
- Merchant;
- Weapon Cache;
- Shelter Loadout if it exposes item details;
- any other shared tooltip/details view.

Requirements:
- wrap correctly;
- no clipping;
- no overlapping text;
- full important description remains accessible;
- pixel font preserved.

If the current 10-row Details cap can hide important description/effect information:
- change the details layout so the actual description is always available;
- use scrolling, dynamic layout or prioritization;
- do not simply truncate critical item behavior.

## 3.7 Validation

Create an ItemDescriptionValidator or equivalent that fails when:
- player-facing item has empty/null description;
- placeholder text remains;
- description contains internal/debug IDs;
- required consumable effect text cannot be generated/resolved;
- Legendary special is omitted where required.

Also add targeted tests that compare structured tooltip values to actual authoritative data for representative items.

---

# 4. SETTINGS UI — REAL CATEGORY PAGES / SUBMENUS

Observed issue:

Settings currently feel strange/unfinished.

Selecting a setting/category such as Video or audio-related settings should open a proper page/menu where the relevant options can be adjusted.

The player should not be navigating a flat/debug-like list.

## 4.1 Overall Settings structure

Build a clear category-based Settings screen.

Recommended top-level categories:

1. `VIDEO`
2. `AUDIO`
3. `CONTROLS`
4. `GAMEPLAY`

Only add a category if relevant settings actually exist.

Do NOT make `Music Volume` itself a top-level category if a coherent `AUDIO` page can contain it.

## 4.2 VIDEO page

Expose only settings actually supported reliably by the project/platform.

At minimum inspect and, where supported, provide:
- Display Mode / Fullscreen mode;
- Resolution;
- VSync;
- Frame-rate limit;
- any currently existing pixel-perfect/display scaling option.

Do not invent graphics-quality toggles that do nothing.

Changing settings should:
- apply correctly;
- show current value;
- persist where appropriate;
- survive restart.

If resolution/fullscreen changes require confirmation/revert handling under current architecture, implement it safely.

## 4.3 AUDIO page

Create a proper Audio settings page.

At minimum expose the existing categories:
- Master Volume;
- Music Volume;
- SFX Volume;
- Ambience Volume.

Use graphical sliders or clearly adjustable stepped controls.

Requirements:
- actual current value visible;
- mouse drag/click;
- keyboard left/right;
- controller left/right;
- 0–100% or equivalent clear value;
- changes preview/apply immediately where appropriate;
- persist to settings;
- explicit 0 remains a valid mute;
- fresh profile defaults remain audible.

Do not confuse labels/tabs with values.

## 4.4 CONTROLS page

Expose the current supported control settings/rebinding path.

Where existing rebind UI already exists:
- integrate it into the category page;
- do not create a second binding system.

At minimum clearly expose:
- bindings/rebind entry;
- controller/keyboard navigation;
- any existing sensitivity/aim setting if supported.

Do not invent unsupported control settings.

## 4.5 GAMEPLAY page

Only expose existing meaningful gameplay preferences, if present.

Examples only if already supported:
- screen shake;
- aim-assist preference/strength;
- damage numbers;
- etc.

Do not expose balance cheats or fake toggles.

If there are no valid gameplay preferences, omit the category rather than create empty controls.

## 4.6 Navigation

Settings must work from:
- Main Menu;
- Pause Menu.

Behavior:
- opening `SETTINGS` shows category navigation;
- selecting a category opens its page;
- Back returns from category page to Settings categories;
- Back again returns to the screen that opened Settings;
- Pause -> Settings -> Back returns to Pause;
- Main Menu -> Settings -> Back returns to Main Menu.

Support:
- mouse;
- keyboard;
- controller;
- visible focus;
- custom pointer cursor.

No dead focus states.

## 4.7 Visual style

Use existing RUINRAIL UI language:
- graphical panels;
- dark steel/charcoal;
- restrained amber;
- pixel font;
- clear sliders/toggles/selectors;
- not a plain text/debug list.

## 4.8 Tests

Add tests for:
- open from Main Menu;
- open from Pause;
- each real category opens;
- Audio page controls actual audio settings;
- Master/Music/SFX/Ambience each change independently;
- explicit mute persists;
- Video setting apply/persist;
- Control rebind navigation;
- Back-stack correctness;
- mouse/keyboard/controller;
- no gameplay input leakage under Pause Settings.

---

# 5. ENEMIES REMAINING HUD IN NORMAL COMBAT ROOMS

Observed requirement:

When the player is actively fighting enemies in a normal combat room, display how many enemies remain.

Boss rooms and non-combat rooms are explicitly excluded.

## 5.1 Eligibility

Show enemy-remaining UI ONLY when:

- current room is an active standard combat encounter;
- encounter has activated;
- there are normal/Elite encounter enemies remaining.

Do NOT show it in:
- Boss rooms;
- Merchant rooms;
- Treasure rooms;
- Weapon Cache rooms;
- Broken Machine rooms;
- Transit rooms;
- other non-combat/event rooms;
- cleared combat rooms;
- before the encounter activates.

## 5.2 Display

Use a compact graphical HUD element.

Suggested:
- small hostile/enemy icon;
- `× 5` or `5 REMAINING`;
- positioned where it does not conflict with minimap, room title, coins, HP, weapons or consumable HUD.

Prefer icon + number over another large text block.

The number must update immediately as enemies die/despawn according to legitimate encounter resolution.

## 5.3 Authoritative source

Do NOT count arbitrary enemy GameObjects in the scene every frame.

Use the room encounter's authoritative enemy membership/alive state.

Count only enemies belonging to the current active encounter.

Do not count:
- enemies in adjacent rooms;
- dead/pending-despawn actors;
- ambient/non-encounter entities;
- Boss in Boss room.

## 5.4 Completion behavior

Example:
- encounter starts with 6 -> display 6;
- one dies -> 5;
- final enemy dies -> 0 briefly if desirable, then hide;
- room clear logic remains unchanged.

Do not delay room-clear just for HUD animation.

## 5.5 Co-op

All clients should see the same authoritative remaining count for the active room.

Do not double-count network replicas.

## 5.6 Tests

Add tests:
- normal combat room shows count;
- count decrements;
- Elite counts correctly;
- adjacent room enemy excluded;
- dead enemy excluded;
- Boss room hidden;
- non-combat room hidden;
- cleared room hidden;
- replicated/client display matches authoritative count.

---

# 6. BROKEN MACHINE + ALL NON-COMBAT ROOMS — COMPLETE RUNTIME AUDIT

Observed in real play:

`Broken Machine` does not work correctly.

There is concern that other non-combat rooms may have the same problem, except for the specific rooms already fixed in previous passes.

Do NOT fix only Broken Machine.

Perform a complete audit of every non-combat/special-event room type in the current 63-room content pool.

## 6.1 Build the authoritative room-type list

From current room definitions/categories, enumerate every non-standard-combat room type.

This likely includes some combination of:
- Merchant;
- Treasure/Loot;
- Weapon Cache;
- Broken Machine;
- Cursed Chest;
- Locked Vault;
- Transit;
- other event/special rooms in current repository.

Do not assume this list is complete.
Derive it from repository data.

Include the final audited list in the report.

## 6.2 End-to-end interaction contract

For every interactive non-combat room/object, verify:

1. room is actually reachable/generated;
2. required world object is instantiated;
3. final sprite/visual is bound;
4. collider/interaction range exists;
5. prompt appears;
6. correct input action is shown;
7. Interact event reaches the intended target;
8. callback/service executes;
9. required UI opens if the mechanic needs UI;
10. input/focus/cursor state is correct;
11. reward/cost/outcome executes exactly once;
12. state changes visually after use;
13. interaction cannot duplicate rewards;
14. re-entry behavior is correct;
15. network authority remains correct;
16. room can complete/exit without softlock.

A prompt that appears but does nothing is a FAIL.
A collider-only object with no visible art is a FAIL.
A test-only service with no runtime subscriber is a FAIL.

## 6.3 Broken Machine

Trace the exact intended design from repository/GDD.

Determine:
- what the player is supposed to receive/choose/pay/risk;
- whether it is one-use;
- whether it needs an option UI;
- what its success/failure/reward behavior is.

Implement the authoritative intended mechanic end-to-end.

Do NOT invent a random new Broken Machine mechanic if the design already exists.

If the design is genuinely under-specified:
- use the smallest implementation consistent with existing definitions/data and surrounding event architecture;
- document the decision explicitly.

Requirements:
- visible object;
- prompt;
- E/Interact works;
- any required choice/menu works;
- outcome executes;
- used state is clear;
- no duplicate outcome;
- no room softlock.

## 6.4 Already-fixed rooms

Merchant and Weapon Cache were recently fixed.

Do NOT rewrite them unnecessarily.

But include them in regression audit to ensure:
- prompt still works;
- menu opens;
- close/reopen works;
- no regressions from shared interaction changes.

## 6.5 Non-interactive special rooms

For special rooms whose reward happens automatically or via chest/object:
- verify intended reward source exists;
- room is not empty by mistake;
- completion/exit semantics work.

## 6.6 Consistent shared architecture

If several broken non-combat rooms share the same missing runtime seam:
- fix the shared seam rather than adding one-off hacks for every room.

Prefer:
- common event-interactable routing;
- common option/choice UI primitives;
- common focus/input gate behavior;
- existing reward/transaction services.

Do not build independent UI frameworks for each event.

---

# 7. NON-COMBAT ROOM REGRESSION TEST MATRIX

Create a test matrix covering EVERY discovered non-combat room category.

For each category, validate:
- generated/instantiated;
- object/reward present;
- prompt where appropriate;
- interaction executes;
- UI opens where appropriate;
- outcome/reward/cost correct;
- one-use/idempotency;
- close/cleanup;
- no duplicate reward;
- no softlock;
- room can be exited;
- mouse/keyboard/controller where UI exists.

Also run representative generated-depth sweeps across all 3 biomes.

Write:

`TestResults/DepthSettingsDescriptionsNonCombatProof/noncombat_room_matrix.csv`

Columns:
- room definition/id;
- player-facing room name;
- biome;
- category/type;
- interaction type;
- prompt;
- UI required;
- runtime object present;
- interaction success;
- reward/outcome success;
- idempotency success;
- result.

No discovered non-combat room type may remain `NOT IMPLEMENTED` while still appearing in the playable dungeon pool.

---

# 8. BUILT-PLAYER PROOF

Save evidence under:

`TestResults/DepthSettingsDescriptionsNonCombatProof/`

Capture at minimum:

1. damaged player at end of one depth;
2. same player entering next depth at full EffectiveMaxHP;
3. Consumable with clear exact description in Inventory;
4. representative weapon description/details;
5. representative armor description;
6. representative accessory description;
7. Settings category screen;
8. Video settings page;
9. Audio settings page with Master/Music/SFX/Ambience controls;
10. Controls settings page;
11. normal combat room showing enemy remaining count;
12. same room after enemies die showing decremented count;
13. Boss room with NO normal-enemy remaining HUD;
14. non-combat room with NO enemy remaining HUD;
15. Broken Machine prompt;
16. Broken Machine interaction/menu/outcome;
17+. one proof capture for each additional non-combat room mechanic not already represented above.

If a setting cannot be meaningfully shown in one screenshot, supplement with state evidence.

---

# 9. FULL VALIDATION GATES

After the final source change run all relevant available gates:

- strict EditMode;
- strict PlayMode;
- ContentCountValidator;
- FinalProductionValidator;
- PresentationValidator;
- AssetPipelineValidator;
- ReleasePathScan;
- item-description validator;
- room/event/non-combat matrix;
- settings persistence tests;
- encounter/HUD tests;
- save/exploit tests;
- network authority tests.

Requirements:
- failed = 0;
- no stale XML;
- no missing scripts/references;
- no player-facing placeholder descriptions;
- no playable non-combat room with dead interaction.

If the machine lacks Windows build support:
- do NOT falsely claim the Windows build;
- run the supported non-development standalone target;
- clearly mark Windows verification as NOT RUN.

Do not modify/install platform modules without explicit user permission.

---

# 10. RELEASE BUILD + SMOKE

On a machine with Windows Standalone Support:
- produce clean Windows x64 NON-DEVELOPMENT build;
- run headless + windowed smoke.

Otherwise:
- build the supported non-development standalone target;
- run equivalent smoke;
- explicitly leave Windows build gate NOT RUN.

Smoke must verify:
- next depth starts full HP;
- item descriptions display;
- Settings category pages work;
- enemy remaining count works only in eligible combat rooms;
- Broken Machine works;
- every generated non-combat room encountered has a functional runtime outcome;
- no exceptions/missing references.

---

# 11. FINAL REPORT

Write:

`production/DEPTH_SETTINGS_DESCRIPTIONS_NONCOMBAT_QOL_REPORT.md`

Include:
- depth-heal hook and anti-exploit semantics;
- exact item count audited;
- description problems found/fixed by category;
- Consumable descriptions and how they map to actual mechanics;
- Settings architecture and categories;
- exact adjustable options actually implemented;
- enemy-remaining HUD source and eligibility rules;
- authoritative list of all non-combat room categories;
- Broken Machine intended mechanic, root cause and fix;
- root causes/fixes for every other broken non-combat room;
- noncombat_room_matrix.csv summary;
- tests added;
- exact EditMode/PlayMode counts;
- build target/result;
- smoke result;
- proof paths;
- genuine remaining limitations.

Terminal status:

`DEPTH_SETTINGS_DESCRIPTIONS_NONCOMBAT_COMPLETE`

only if every repository-local requirement above is fixed and verified.

Use:

`DEPTH_SETTINGS_DESCRIPTIONS_NONCOMBAT_INCOMPLETE`

if any repository-local issue remains.

A missing platform build module may keep that specific platform gate NOT RUN, but do not confuse it with unfinished repository-local work; report both clearly.

Do not ask for approval during execution.

BEGIN NOW.
