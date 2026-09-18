# RUINRAIL — ROOM / HUD / AUDIO / QOL FIX PASS
## Execute from the CURRENT repository state after START_HP_DASH_ICON_HUD_MERCHANT_COMPLETE

This is a focused runtime/QoL pass based on issues observed during real gameplay.

Do NOT ask for intermediate approval.
Do NOT create a new task series.
Do NOT refactor unrelated systems.
Do NOT change unrelated balance, progression, save semantics, network authority, inventory structure, weapon stats, enemy stats, or dungeon generation unless strictly required to fix one of the issues below.

Fix the actual root cause of every listed problem, add regression coverage, run the required build/test gates, capture runtime proof, and return one consolidated report only at the end.

Write the final report to:

`production/ROOM_HUD_AUDIO_QOL_REPORT.md`

---

# 0. READ FIRST

Read the latest/current versions of:

- CLAUDE.md
- CLAUDE_START_HERE.md
- technical/117_CODING_RULES_FOR_CLAUDE.md
- technical/118_TESTING_STRATEGY.md
- production/START_HP_DASH_AND_ICON_HUD_REPORT.md
- production/LOOT_AMMO_AUDIO_RUNTIME_FIX_REPORT.md
- production/GRAPHICAL_INVENTORY_REWORK_REPORT.md
- production/COMBAT_AIM_COLLISION_AND_FINAL_VISUAL_FIX_REPORT.md
- current Weapon Cache interaction code
- current Interactable / prompt / input routing
- current dungeon room definitions / room runtime state / room category data
- current Dungeon HUD / minimap-related code if any
- current audio bootstrap / mixer / settings / AudioListener / music / ambience / SFX code
- current Dash HUD / cooldown state code
- current UiSkin / icon / HUD rendering paths

Repository truth wins over older assumptions.

---

# 1. HARD SCOPE

Fix these exact issues:

1. Weapon Cache interaction does nothing even though `[E] CHOOSE WEAPON CACHE` is shown.
2. Entering a new room should briefly display the room's name/title.
3. Top-left HUD should be simplified: remove the current text-heavy objective block and replace it with a minimap + biome display.
4. Top-right coins should be displayed graphically rather than as plain text.
5. Add a polished minimap.
6. Add a low-HP red vignette that subtly pulses while health is critically low.
7. Audio/music is still inaudible to the player and must be fixed end-to-end in the actual shipped runtime.
8. Dash cooldown feedback must show continuous cooldown progress on the icon rather than a static grey state.

Do NOT redesign:
- inventory
- merchant UI
- pause menu
- weapons
- enemies
- room layouts
- art style
- progression
- save model
- multiplayer authority model

Small supporting changes are allowed only where required for these items.

---

# 2. WEAPON CACHE INTERACTION FIX

Observed in real gameplay:

- the player stands near the Weapon Cache;
- the prompt correctly shows:
  `[E] CHOOSE WEAPON CACHE`
- pressing E / Interact does nothing;
- no selection UI or reward flow opens.

This is a runtime integration bug.

## 2.1 Trace the full interaction path

Trace:

1. Interact input event
2. gameplay/menu input gates
3. interaction query / target detection
4. prompt target resolution
5. Weapon Cache interactable callback
6. Weapon Cache service / state
7. selection UI / view-model creation
8. UI screen activation
9. focus stack
10. pointer cursor
11. gameplay input blocking while UI is open
12. reward selection
13. transaction/authority path
14. close/cleanup path

Explicitly investigate:

- prompt and actual interactable resolving different GameObjects;
- interaction handler missing;
- callback/event not subscribed;
- view exists but is never instantiated/composed;
- menu focus stack never receives the cache screen;
- UI opens behind another canvas;
- input event is consumed by another gate;
- cache state is invalid/null and silently aborts;
- network/authority check incorrectly rejects local interaction;
- selection service exists only in tests/editor and is absent in runtime composition.

Do not stop at “the prefab exists”.

## 2.2 Required behavior

When in range:
- prompt appears once;
- pressing Interact opens the Weapon Cache selection UI exactly once;
- player can select one of the intended weapon choices;
- reward is granted exactly once;
- no duplication;
- no reward after already consumed/opened if current design is one-use;
- closing/canceling behaves according to current approved rules;
- gameplay input is blocked while the selection UI is open;
- focus/cursor/input state restores correctly after close.

## 2.3 Visual consistency

If the Weapon Cache UI exists but is placeholder/text-only:
- bring it up to the current RUINRAIL UI standard;
- reuse graphical item slots, icons, rarity frames and tooltip/details formatting already used by Inventory/Merchant where possible;
- do not create a parallel UI framework.

## 2.4 Tests

Add tests for:
- prompt visible in range;
- no prompt out of range;
- Interact opens UI;
- exactly one UI instance;
- choices display valid weapon icons/data;
- select grants exactly one weapon;
- consumed cache cannot be duplicated;
- input is gated while open;
- close restores input;
- keyboard/mouse/controller work;
- scene transition cleans stale cache UI.

---

# 3. ROOM TITLE / ROOM NAME REVEAL

When the player actually enters a new room, briefly show a room title.

Goal:
The player should immediately understand what kind of room they have entered.

## 3.1 Trigger behavior

Trigger only when the player genuinely enters a new room interior.

Do NOT trigger:
- while standing in doorway;
- every frame;
- repeatedly while moving inside the same room;
- from merely seeing an adjacent room;
- from a room preload/spawn event before the player enters.

Use the authoritative room-entry state already used by combat-room activation where possible.

## 3.2 Display behavior

Recommended presentation:
- top-center or slightly below the top HUD;
- subtle fade/slide in;
- short hold;
- fade out;
- approximately 1.5–2.5 seconds total;
- readable pixel font;
- optional small secondary room category line if useful;
- must not obscure combat.

Do not create a huge cinematic banner.

## 3.3 Room names

Inspect whether player-facing room display names already exist.

If they do:
- use them.

If they do not:
- add a minimal `DisplayName`/equivalent field or mapping;
- assign meaningful names to every room definition that can be entered during normal play.

Names should be:
- short;
- atmospheric;
- understandable;
- biome-appropriate;
- not internal IDs like `Metro_Combat_04`.

Naming direction examples only:
- Ruined Metro: “Collapsed Platform”, “Service Junction”, “Maintenance Hall”
- Rustworks: “Smelter Floor”, “Boiler Junction”, “Machine Bay”
- Overgrown Labs: “Specimen Wing”, “Hydroponics Bay”, “Containment Hall”

Do not blindly use these names if existing room themes support better names.

Special rooms should be clearly named:
- Merchant
- Treasure
- Weapon Cache
- Boss
- Transit
- etc., using polished player-facing names.

## 3.4 Tests

Test:
- entering a new room shows correct title;
- staying in same room does not repeat;
- entering another room updates title;
- returning to an already visited room follows the intended rule consistently;
- title does not fire while blocked in doorway;
- special room title matches actual room role.

---

# 4. TOP-LEFT HUD CLEANUP

The current top-left HUD is too text-heavy.

Remove the permanent multi-line objective/depth text block currently occupying that area.

The top-left should instead contain:

1. **Minimap**
2. **Biome name**

Keep the biome display compact.

Do not permanently show redundant instructions such as:
- `DEPTH 1`
- `find and defeat the boss`
in the current large text-heavy form.

If depth/objective information still needs to exist:
- move it to a more appropriate lightweight presentation;
- or show it contextually/temporarily;
- do not keep the same large block.

The requested permanent top-left presentation is:
- minimap first;
- biome identity clearly visible.

---

# 5. GRAPHICAL COIN HUD

The top-right coin display should no longer be plain text only.

Implement a compact graphical coin presentation.

Requirements:
- use/create an original RUINRAIL pixel coin/token icon;
- icon + numeric amount;
- restrained panel/frame if needed;
- clean alignment;
- readable at 640×360;
- current carried coin value updates immediately;
- no redundant `COINS` word required if icon is obvious.

If an existing final coin icon already exists:
- reuse it.

If not:
- create one through the existing art/UI pipeline;
- bind it through UiSkin/content manifest;
- no debug placeholder.

Test:
- coin amount updates after pickup;
- merchant purchase/sale updates immediately;
- no stale value.

---

# 6. MINIMAP

Implement a polished in-run minimap.

This should be a room-graph-style minimap suitable for RUINRAIL's dungeon structure.

Do NOT render the full world texture or build an expensive live camera minimap unless the current architecture already uses that approach.

Prefer a lightweight graph/map representation driven by actual dungeon layout data.

## 6.1 Required information

At minimum show:
- current room;
- discovered/visited rooms;
- valid room connections;
- player/current-room marker;
- biome label near/under/above the minimap.

Current room must be visually distinct.

Unvisited rooms:
- either hidden entirely;
- or shown only if already discovered through an adjacent-room discovery rule;
- preserve roguelite discovery feel.

Do not reveal the entire generated dungeon from the start unless the existing game design explicitly says to.

## 6.2 Special room markers

Only show special markers if consistent with current design.

Preferred conservative behavior:
- Merchant / Treasure / Weapon Cache / Boss / Transit icons appear only after that room has been discovered or entered;
- do not spoil unexplored special-room locations.

Use compact original pixel symbols.

## 6.3 Visual style

Match current RUINRAIL HUD:
- dark panel;
- thin steel/charcoal frame;
- amber/teal accents;
- crisp pixels;
- restrained contrast;
- no fantasy parchment;
- no modern glossy radar.

Keep it small enough not to dominate the screen.

## 6.4 Layout behavior

At 640×360:
- minimap must not overlap HP/Dash/weapon HUD;
- must not cover room-title reveal;
- must not clip at screen edges;
- inventory/pause/merchant overlays still render above it.

If dungeon graph grows beyond the panel:
- center/scale intelligently around the current room or fit discovered graph;
- do not allow unreadably tiny cells;
- prefer local neighborhood view if needed.

## 6.5 Tests

Test:
- current room marker;
- discovered room appears;
- connection lines/doors match actual graph;
- undiscovered room remains hidden according to rule;
- special icon discovery;
- current room updates on entry;
- no room shown that does not exist;
- works in all 3 biomes;
- deterministic seeded dungeon map matches runtime layout.

---

# 7. LOW-HP RED VIGNETTE

Add a low-health danger vignette.

## 7.1 Threshold

Use a tunable health percentage threshold.

Recommended default:
- activate at **30% HP or lower**

If current UX spec already defines a threshold, use that instead.

Do not hardcode against base HP; use:

`CurrentHP / EffectiveMaxHP`

## 7.2 Visual behavior

When above threshold:
- invisible.

When at/below threshold:
- red vignette around screen edges;
- subtle pulse;
- central play area remains readable;
- not a full red screen;
- no aggressive rapid flash.

Recommended pulse:
- approximately 0.8–1.2 Hz;
- opacity gently oscillates within a restrained range;
- intensity may increase slightly as HP approaches 0.

Make values tunable.

## 7.3 State behavior

- starts immediately when crossing into low-HP range;
- fades away when healed above threshold;
- no lingering state after scene change/death/revive;
- do not obscure Inventory/Pause/Merchant UI.

## 7.4 Tests

Test:
- >30% -> hidden;
- exactly threshold -> active;
- lower HP -> stronger or equal intensity;
- heal above threshold -> hidden;
- uses EffectiveMaxHP;
- no activation from stale HP state.

---

# 8. AUDIO / MUSIC — REAL RUNTIME FIX REQUIRED

The player still hears no music or SFX in actual gameplay despite a previous pass claiming the audio runtime was fixed.

Treat this as an unresolved critical runtime bug.

Do NOT accept:
- clip existence;
- `isPlaying == true`;
- mixer values in logs;
- test harness playback;
as sufficient proof by themselves.

Find why the actual player experiences silence.

## 8.1 End-to-end audit

Audit:
- Windows build audio device output;
- Unity audio initialization;
- active AudioListener;
- listener enabled state;
- listener placement;
- duplicate listeners;
- AudioSource routing;
- AudioMixer groups;
- master/music/SFX/ambience values;
- persisted settings;
- mute state;
- `AudioListener.volume`;
- `AudioListener.pause`;
- source volume;
- mixer attenuation;
- spatialBlend;
- doppler/min/max distance;
- scene bootstrap;
- persistent audio object lifetime;
- clip load path;
- actual AudioClip sample data;
- source playback;
- scene transitions;
- pause/unpause;
- focus lost/regained;
- Windows player configuration if relevant;
- whether build is using the intended audio backend;
- whether headless/test-specific configuration leaks into normal builds.

## 8.2 Verify fresh-profile settings

A fresh/default profile must not start muted.

Verify:
- Master > 0
- Music > 0
- SFX > 0
- Ambience > 0

Preserve intentional user-selected mute values.

If old save data has missing keys that deserialize as zero:
- migrate missing/uninitialized values;
- do NOT override an explicit user-set 0.

## 8.3 Required audible paths

Verify in the shipped windowed executable:

### Main Menu
- music audible;
- UI hover/click audible.

### Shelter
- music/ambience audible.

### Dungeon
- biome ambience audible;
- dungeon/exploration music audible;
- gunfire audible;
- reload audible;
- dry-fire audible;
- pickup audible;
- chest/cache audible;
- enemy hit/death audible;
- door lock/unlock audible;
- dash audible if authored;
- merchant interaction audible if authored.

### Boss
- boss music/stinger behavior audible according to current design.

## 8.4 Stronger proof requirement

Because previous automated checks said audio was active while the player still heard nothing, add stronger runtime diagnostics.

At minimum log during the normal shipped-player run:

- active output device / Unity audio configuration if available;
- AudioListener count and active state;
- AudioListener.volume;
- each relevant AudioSource:
  - clip
  - isPlaying
  - volume
  - spatialBlend
  - outputAudioMixerGroup
- resolved mixer parameters;
- loaded user volume settings;
- whether the app is muted/paused.

If feasible on this environment:
- capture actual loopback/output audio from the shipped executable.

If loopback capture is unavailable:
- explicitly say so;
- do not fabricate a WAV;
- perform a manual audible verification on the machine and document it separately from automated evidence.

The terminal status must NOT claim audio fixed unless normal windowed player playback has been manually confirmed audible.

---

# 9. DASH ICON — CONTINUOUS COOLDOWN WIPE

Current behavior:
- ready state looks acceptable;
- cooldown state becomes statically grey;
- the player cannot tell how much cooldown remains.

Replace this with a continuous cooldown progress effect.

## 9.1 Desired visual

Use a classic cooldown wipe/mask over the Dash icon.

Desired behavior:
- when Dash is used, the icon becomes covered by a grey/dark overlay;
- as cooldown progresses, the overlay steadily recedes;
- by the time Dash is ready, the overlay is fully gone;
- ready state returns clearly.

The requested visual is:
**the grey coverage moves vertically across the icon to reveal it as cooldown completes.**

Prefer a vertical fill/mask.

## 9.2 Authoritative progress

Progress must come from the actual Dash cooldown:

`progress = 1 - remainingCooldown / totalCooldown`

or equivalent authoritative state.

No independent UI timer.

## 9.3 Visual details

- keep underlying icon visible enough to identify;
- overlay should be semi-opaque, not solid featureless grey;
- optional thin bright edge at the moving boundary if it improves readability;
- ready state may use a subtle one-time amber flash when cooldown completes;
- do not repeatedly flash while ready.

## 9.4 Tests

Test:
- immediately after dash -> near/full overlay;
- halfway -> approximately half covered;
- near ready -> small remaining cover;
- ready -> no overlay;
- UI progress tracks gameplay state;
- second dash resets overlay;
- pause does not desync;
- scene transition resets correctly.

---

# 10. ROOM TITLE + MINIMAP COORDINATION

Room title reveal and minimap must update from the same authoritative room-entry event/state.

When entering a new room:

1. runtime room state updates;
2. minimap current room updates;
3. room is marked discovered/visited;
4. room-title reveal plays;
5. special-room icon becomes visible if discovery rules permit.

Do not build separate competing room-detection systems.

---

# 11. HUD LAYOUT TARGET

At 640×360:

## Top-left
- minimap
- biome label

## Top-center
- temporary room-title reveal only when entering a room

## Top-right
- coin icon + amount

## Bottom-left
- HP bar/value
- Dash icon with continuous cooldown wipe

## Bottom-center
- current graphical weapon HUD

## Bottom-right
- current graphical consumable HUD

Do not reintroduce large permanent text blocks.

Keep the screen readable during combat.

---

# 12. INPUT / OVERLAY COHERENCE

Do not regress:
- Inventory
- Merchant
- Pause
- Weapon Cache selection UI

All overlays must:
- push appropriate focus;
- gate gameplay input;
- own pointer cursor;
- close cleanly;
- restore aim cursor/gameplay input;
- not duplicate themselves.

HUD/minimap remain informational and should not consume gameplay mouse input.

---

# 13. NETWORK / AUTHORITY SAFETY

Do not change network authority rules.

For room discovery/minimap:
- local presentation may derive from authoritative room state;
- do not reveal rooms due to remote player movement unless current co-op design explicitly shares discovery;
- if party discovery is already shared, preserve it.

Weapon Cache reward remains authoritative and duplication-safe.

Audio remains local presentation triggered from authoritative gameplay events where appropriate.

If UGS remains unavailable:
- use deterministic harness;
- do not claim live Relay verification.

---

# 14. REQUIRED TESTS

Add/extend tests covering:

## Weapon Cache
- prompt;
- Interact;
- UI open;
- selection;
- one reward;
- no duplicate;
- close/reopen behavior;
- input gating.

## Room Titles
- correct name;
- one reveal per room entry;
- no repeated spam;
- special rooms named correctly.

## Minimap
- current room;
- discovery;
- connections;
- special markers after discovery;
- all 3 biomes;
- deterministic layout mapping.

## Coins
- graphical icon exists;
- count updates after pickup;
- count updates after merchant transaction.

## Low HP
- threshold;
- pulse active;
- hidden above threshold;
- effective max HP used.

## Audio
- bootstrap;
- listener;
- settings;
- mixer routing;
- scene music;
- ambience;
- representative SFX;
- focus/pause transitions;
- fresh-profile non-muted defaults.

## Dash Cooldown
- 0%, 25%, 50%, 75%, 100% visual progression;
- matches runtime cooldown;
- no static-grey-only state.

---

# 15. BUILT-PLAYER PROOF

Save evidence under:

`TestResults/RoomHudAudioQolProof/`

Capture at minimum:

1. Weapon Cache prompt visible;
2. Weapon Cache selection UI open after E;
3. selected weapon reward granted;
4. room-title reveal in normal room;
5. room-title reveal in special room;
6. minimap with current room;
7. minimap after several discovered rooms;
8. special-room minimap marker after discovery;
9. biome name next to minimap;
10. graphical coin display;
11. low-HP vignette active;
12. Dash icon at ~100% cooldown remaining;
13. Dash icon at ~50%;
14. Dash icon near ready;
15. Dash icon ready;
16. clean full HUD at 640×360.

Also write:

`TestResults/RoomHudAudioQolProof/audio_runtime_evidence.txt`

with actual runtime audio state.

If possible, include manual verification notes that audio was physically audible from the shipped executable.

---

# 16. FULL VALIDATION GATES

After the final source change run:

- strict EditMode;
- strict PlayMode;
- ContentCountValidator;
- FinalProductionValidator;
- PresentationValidator;
- AssetPipelineValidator;
- ReleasePathScan;
- ArtProductionContract;
- audio/music audits;
- relevant room/interactable/HUD/input tests.

Requirements:
- failed = 0;
- no stale XML;
- no missing scripts/references;
- no release placeholders.

Do not disable Smart App Control.

---

# 17. RELEASE BUILD + SMOKE

Produce a clean Windows x64 NON-DEVELOPMENT build.

Run:
- headless smoke;
- windowed shipped-player smoke.

Require:
- Weapon Cache actually opens and grants reward;
- room titles show correctly;
- minimap updates;
- biome label visible;
- coin display graphical;
- low-HP vignette works;
- Dash cooldown visibly progresses;
- audio is manually confirmed audible in the normal shipped player;
- no exceptions;
- no missing references.

Do not claim live Relay success unless actually run over UGS.

---

# 18. FINAL REPORT

Write:

`production/ROOM_HUD_AUDIO_QOL_REPORT.md`

Include:
- root cause of Weapon Cache interaction failure;
- exact fix;
- room-name source/mapping;
- room-title trigger/fade behavior;
- minimap architecture and discovery rules;
- coin HUD changes;
- low-HP threshold/pulse values;
- exact audio root cause(s);
- exact audio runtime fix;
- whether normal shipped-player audio was manually confirmed audible;
- dash cooldown wipe implementation;
- test counts;
- build result;
- smoke result;
- proof paths;
- any genuine remaining limitation.

Terminal status:

`ROOM_HUD_AUDIO_QOL_PASS_COMPLETE`

only if all repository-local issues above are fixed and verified.

Use:

`ROOM_HUD_AUDIO_QOL_PASS_INCOMPLETE`

if any required local issue remains.

If audio still cannot be heard in the normal shipped executable, terminal status MUST be incomplete even if automated audio tests pass.

Do not ask for approval during execution.

BEGIN NOW.
