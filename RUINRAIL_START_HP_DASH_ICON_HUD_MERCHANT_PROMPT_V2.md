# RUINRAIL — START HP / DASH TUNING / LIVE HUD EQUIPMENT SYNC + ICON HUD PASS
## Execute from the CURRENT repository state after GRAPHICAL_INVENTORY_REWORK_COMPLETE

This is a focused gameplay-state and HUD presentation pass.

The player has manually observed the following issues in a real run:

1. A run does not always start at the true effective maximum HP after armor/max-HP modifiers are applied.
2. Dash recharges too quickly and travels too far.
3. The in-run HUD can display stale equipped weapon/item information after changing equipment during the run.
4. The bottom-right consumable HUD is too text-heavy and should become an icon-based slot.
5. The bottom-left Dash HUD is too text-heavy and should become an icon-based cooldown indicator.
6. The bottom-center weapon HUD is too text-heavy and should become graphical weapon slots/icons while keeping essential ammo information readable.
7. Merchant interaction is visibly detected (`[E] TRADE WITH MERCHANT`) but pressing E does not open any trade menu.

Do NOT ask for intermediate approval.
Do NOT create a new task series.
Do NOT redesign unrelated systems.
Do NOT change inventory rules, weapon stats, armor stats, item IDs, save semantics, networking authority or dungeon generation.

Fix the actual state/data-flow bugs first, then update the HUD presentation.

Write the final report to:
`production/START_HP_DASH_AND_ICON_HUD_REPORT.md`

---

# 0. READ FIRST

Read the latest/current versions of:

- CLAUDE.md
- CLAUDE_START_HERE.md
- technical/117_CODING_RULES_FOR_CLAUDE.md
- technical/118_TESTING_STRATEGY.md
- production/GRAPHICAL_INVENTORY_REWORK_REPORT.md
- production/FINAL_PLAYABILITY_REGRESSION_REPORT.md
- production/COMBAT_AIM_COLLISION_AND_FINAL_VISUAL_FIX_REPORT.md
- current player health/max-health/stat/loadout initialization code
- current armor modifier/stat aggregation code
- current dash configuration and runtime code
- current DungeonHudView / HUD presenter / HUD view-model code
- current weapon equip/swap/inventory transfer events
- current consumable equip/use events
- current UiSkin / item icon / weapon icon / cursor/input architecture

Repository truth wins over older documents.

---

# 1. HARD SCOPE

Fix only:

1. Run-start HP initialization
2. Dash cooldown/recharge tuning
3. Dash travel-distance tuning
4. Live HUD synchronization after equipment changes
5. Bottom-right consumable icon HUD
6. Bottom-left Dash icon/cooldown HUD
7. Bottom-center graphical weapon HUD
8. Merchant interaction / trade-menu runtime fix

Do NOT alter:
- weapon damage
- fire rate
- magazine size
- reload time
- enemy health/damage
- armor stat values
- consumable effects
- backpack capacity
- equipment slot model
- starter kit composition
- save/wipe semantics
- multiplayer authority rules

---

# 2. RUN MUST START AT TRUE EFFECTIVE MAX HP

Observed bug:

Example:
- player base max HP = 100
- starting armor grants +20 max HP
- effective max HP = 120
- expedition currently starts at 100/120 instead of 120/120

This is wrong.

## 2.1 Correct initialization order

At the start of a NEW expedition/run, health initialization must happen only after all effective max-HP sources for the starting loadout are resolved.

The order must be:

1. load persistent player progression/stat state;
2. load/equip run starting loadout;
3. apply armor/accessory/perk/stat modifiers that affect max HP;
4. calculate final effective maximum HP;
5. set current HP to that final effective maximum HP;
6. enter the expedition.

The player must start every newly-created expedition at:

`CurrentHP == EffectiveMaxHP`

unless an explicit authoritative game rule says the run intentionally starts wounded. Do not invent such a rule.

## 2.2 Effective max HP

Use the existing stat/modifier aggregation path.

Do NOT hardcode:
`100 + armorBonus`

Effective max HP may include:
- base HP;
- armor max-HP modifier;
- persistent progression/stat modifier;
- accessory modifier if such an approved modifier exists;
- any other currently approved max-HP modifier.

The same authoritative effective-max calculation must be used by gameplay and HUD.

## 2.3 No free-heal exploit

This full-heal behavior applies only to initialization of a NEW expedition/run.

Do NOT make every equipment change during a run heal the player to full.

If max HP changes during a run:
- preserve current approved semantics;
- do not grant free full healing from unequip/re-equip;
- clamp CurrentHP only when required by reduced EffectiveMaxHP;
- preserve current revive/healing/bandage semantics.

## 2.4 Tests

Add deterministic tests:

- base 100 HP, no modifier -> run begins 100/100;
- base 100 + Scrap Vest +20 -> begins 120/120 if +20 is the authoritative value;
- multiple max-HP modifiers -> starts at exact aggregated max;
- changing armor mid-run does NOT full-heal;
- removing +HP armor clamps only if CurrentHP exceeds new max;
- new expedition after previous damaged run starts at full effective max;
- wipe/recovery/new profile path still starts correctly;
- HUD denominator matches authoritative EffectiveMaxHP.

Use actual current item/stat values if repository data differs from the example.

---

# 3. DASH NERF — EXACT TUNING

The current Dash feels too frequent and travels too far.

Apply approximately a 15% nerf to both:

1. recharge rate;
2. travel distance.

Do this through the existing authoritative dash configuration/data.

## 3.1 Recharge

Interpret the request as:

`Dash recharge rate = old recharge rate × 0.85`

If implementation stores cooldown duration in seconds:

`NewCooldownDuration = OldCooldownDuration / 0.85`

This makes recharge exactly 15% slower by rate.

Example only:
- old cooldown 1.25 s
- new cooldown ≈ 1.4706 s

Do NOT blindly use the example if current repo value differs.

## 3.2 Distance

Set:

`NewDashDistance = OldDashDistance × 0.85`

If distance derives from speed × duration:
- adjust the smallest authoritative value(s) so measured displacement is ~85% of old;
- do not unintentionally change i-frame duration.

## 3.3 Preserve other dash behavior

Do NOT change unless required:
- i-frame duration
- dash direction
- input buffering
- wall collision
- animation/VFX
- network authority

## 3.4 Tests

Add tests proving:
- recharge is approximately 15% slower;
- measured unobstructed displacement is approximately 15% shorter;
- dash still stops at walls;
- no tunneling;
- i-frame duration unchanged;
- second dash cannot start early;
- HUD cooldown state matches actual gameplay cooldown.

Document exact before/after values.

---

# 4. FIX STALE HUD EQUIPMENT STATE

Observed bug:

HUD may keep showing P9 Ranger even after equipping/switching to Wasp-45 during the run.

This is a synchronization bug.

## 4.1 Investigate root cause

Trace:
- PlayerInventory/equipment containers
- ItemTransferService
- active weapon slot
- WeaponSwap
- inventory equip/move/drag-drop
- pickup/equip path if supported
- starter loadout initialization
- DungeonHudViewModel / DungeonHudView
- consumable state

Find where the HUD cached initial/starter equipment and failed to refresh.

## 4.2 Correct invariant

The HUD must always reflect CURRENT authoritative equipment state.

Update immediately when:
- Primary changes;
- Secondary changes;
- active weapon slot changes;
- weapon swaps;
- weapon equipped from backpack;
- equipped weapon dropped/replaced;
- consumable changes;
- consumable stack changes;
- any HUD-presented equipped item changes.

No stale name/icon/ammo values.

## 4.3 Architecture

Prefer:
- existing equipment/inventory change events;
- one authoritative HUD refresh seam;
- view-model state notifications.

Avoid:
- duplicate inventory cache;
- random manual refresh calls scattered everywhere;
- string polling hacks.

If needed, add the smallest central equipment-changed event/snapshot seam.

## 4.4 Tests

Test:
- P9 Ranger -> equip Wasp-45 -> HUD updates immediately;
- primary replacement;
- secondary replacement;
- weapon swap 1/2;
- equip from backpack;
- drag/drop equip;
- drop equipped weapon;
- consumable replacement;
- consumable stack decrement;
- reopening inventory does not desync HUD;
- scene transition does not restore stale starter display.

---

# 5. HUD PRESENTATION GOAL

Convert the main in-run action/equipment HUD from text-heavy presentation to graphical icon-based presentation.

Use existing RUINRAIL item icons and UiSkin.

Keep essential numeric information:
- HP current/max;
- firearm magazine/reserve ammo;
- consumable stack count.

Goal:
**icons first, numbers second, minimal text.**

---

# 6. BOTTOM-CENTER WEAPON HUD — GRAPHICAL SLOTS

Replace text rows such as:
`>1 P9 Ranger 12 / 124`
` 2 Field Knife`

with two compact graphical weapon slots.

Each slot must contain:
- weapon icon;
- small slot number `1` or `2`;
- rarity frame/accent where appropriate;
- clear active-weapon highlight;
- ammo readout only when relevant.

## 6.1 Firearm

Display compact ammo:
`12 / 124`

or magazine prominent + reserve smaller.

Do not permanently print the full weapon name if icon identity is clear.

## 6.2 Ammo-free weapon

Field Knife/melee:
- icon only;
- no fake ammo count.

## 6.3 Active state

Current active weapon must be unmistakable:
- amber brackets/frame;
- brighter plate;
- or existing RUINRAIL focus treatment.

Weapon swap updates instantly.

---

# 7. BOTTOM-RIGHT CONSUMABLE HUD — ICON SLOT

Replace permanent text such as:
`Bandage x1`

with a graphical consumable slot.

Requirements:
- actual equipped consumable ItemDefinition.Icon;
- existing slot/rarity frame art;
- small stack chip `x1`, `x3`, etc.;
- no permanent full item-name text;
- neutral empty state;
- updates immediately after use/equip replacement;
- optional compact input hint allowed.

Do not add extra consumable slots.

---

# 8. BOTTOM-LEFT DASH HUD — ICON + COOLDOWN STATE

Replace `DASH READY` with a graphical Dash icon.

## 8.1 Icon

If final Dash icon exists, use it.

If not:
- create through existing UI/art pipeline;
- pixel-art;
- RUINRAIL style;
- readable at ~16–24 px;
- suggested motif: boot / forward chevrons / motion burst;
- no vector/smooth placeholder.

Bind through UiSkin/content UI path.

## 8.2 States

### Ready
- full/bright icon;
- ready frame/accent.

### Cooldown
- dimmed;
- show cooldown progress via:
  - fill mask,
  - dark overlay,
  - or simple pixel progress treatment.

Do not use permanent words `DASH READY`.

Optional tiny numeric countdown only if genuinely needed.

### Disabled
If dash unavailable for another reason:
- visually distinct disabled state.

## 8.3 Sync

Dash HUD must derive from the same authoritative cooldown state as gameplay.
No separate drifting timer.

---

# 9. HP HUD

Keep HP bar and exact `Current / Max` text.

Ensure:
- denominator uses effective max HP;
- new run starts full;
- no stale 100/120 initialization.

Do not expand scope into a full HP redesign.

---

# 10. VISUAL STYLE

Use the same approved language as the new graphical inventory:
- dark charcoal/steel plates;
- restrained amber accents;
- crisp pixel edges;
- existing UiSkin;
- item icons;
- rarity frames;
- compact stack chips;
- clean brackets/highlights.

At 640×360:
- weapons do not overlap objective text;
- Dash does not overlap HP;
- consumable does not clip screen edge;
- HUD does not overlap itself;
- Inventory/Pause still render above HUD.

---

# 11. INPUT FEEDBACK

HUD is informational, not mouse-interactive.

Update weapon slots from:
- number keys;
- weapon swap input;
- controller;
- inventory equip actions.

Consumable icon updates from actual equipped consumable state.

Dash icon updates from actual dash cooldown.

---

# 12. NETWORK / CO-OP SAFETY

Do not regress network rules.

Local HUD displays the local player's authoritative/replicated state only.

No phantom local equipment state.

Live Relay does not need to be claimed if UGS remains unavailable.

---


# 13. MERCHANT INTERACTION / TRADE MENU RUNTIME FIX

Observed in a real generated run:

- the player stands next to the merchant;
- the on-screen prompt correctly displays:
  `[E] TRADE WITH MERCHANT`
- pressing the interact key does nothing;
- no merchant/trade menu opens.

This is a real runtime integration bug.

Do NOT remove the merchant or interaction prompt.
Do NOT replace the merchant with a simple auto-purchase shortcut.
Fix the complete interaction flow.

## 13.1 Trace the complete interaction path

Trace from input to UI:

1. `PlayerInputReader` / Interact action receives E/controller interact;
2. gameplay input gates allow Interact when no blocking overlay is open;
3. interaction query detects the merchant as the current valid interactable;
4. current/interactable target is the same object that generated the prompt;
5. merchant interaction callback/event executes;
6. merchant/trade service exists and is resolved;
7. merchant inventory/stock is available;
8. trade view-model is created/bound;
9. trade screen/view is instantiated/enabled;
10. menu focus list is pushed;
11. pointer cursor is enabled for mouse;
12. gameplay movement/fire/interact is gated while the menu is open;
13. Buy/Sell actions route through the existing economy/inventory authority paths;
14. closing the menu restores gameplay input/focus/cursor cleanly.

Find the actual broken seam.

Explicitly investigate:
- prompt system and interaction system using different target instances;
- Merchant GameObject missing an interactable handler/component;
- interaction event not subscribed;
- merchant screen/view exists but is never composed;
- screen navigation enum/state exists but is never pushed;
- merchant view-model has no runtime binding;
- interaction is being consumed by a menu/input gate;
- `InteractPressed` event fires before the merchant is registered;
- collider/trigger is prompt-only and does not resolve the merchant service;
- canvas exists but is inactive/behind another canvas;
- merchant stock initialization failure prevents opening;
- null service/reference silently aborts;
- input action mismatch between E prompt and actual bound action.

Do not stop at “the merchant prefab exists”.

## 13.2 Merchant menu requirements

Use the current approved RUINRAIL visual language.

The merchant menu must be a real graphical UI, consistent with the new Inventory:

- dark charcoal/steel panels;
- amber focus/highlight;
- pixel font;
- item icons from `ItemDefinition.Icon`;
- rarity frames where relevant;
- clean slot/grid or list-card presentation;
- no debug-style text wall.

At minimum display:

### Merchant stock
For each item:
- icon;
- name;
- rarity;
- price;
- category;
- available stock/count if stock is finite.

### Player side
Show:
- carried coins;
- relevant backpack capacity / free slots;
- selected player's item when selling;
- price/value;
- enough information to understand whether the transaction is valid.

### Details
Selected item details should reuse the existing tooltip/details model where possible:
- weapon stats;
- armor/accessory/consumable information;
- ammo quantity;
- comparison against currently equipped item where already supported.

Do not build a second independent tooltip/stat formatting system if the Inventory one can be reused.

## 13.3 Buy behavior

Buying must use the existing economy/inventory transaction path.

Requirements:
- cannot buy without enough carried coins;
- cannot buy when backpack/equipment destination is invalid/full unless existing auto-equip rules apply;
- coins deducted exactly once;
- item granted exactly once;
- merchant stock decremented exactly once if stock is finite;
- failed transaction changes nothing;
- no duplication through repeated input, double-click, controller confirm spam or network retries;
- appropriate success/failure UI feedback;
- transaction remains authoritative under current networking rules.

If ammo is sold by the merchant:
- ammo purchase must respect ammo caps;
- do not charge for ammo that cannot be received unless current design explicitly supports partial purchase and prices it correctly.

## 13.4 Sell behavior

If selling is part of the already approved merchant design, make it functional.

Requirements:
- player can select eligible backpack/equipment items;
- sale price uses existing authoritative item/economy data;
- carried coins increase exactly once;
- sold item leaves inventory exactly once;
- starter items marked unsellable remain unsellable;
- currently equipped item follows existing unequip/sell rules;
- invalid sale produces feedback without state mutation.

Do NOT invent selling if the authoritative merchant design explicitly excludes it. In that case, document that and implement the approved buy-only interface.

## 13.5 Input support

Merchant UI must support:

### Mouse
- hover;
- click;
- buy/sell/select buttons;
- scroll if required;
- pointer cursor.

### Keyboard
- arrow/WASD navigation;
- Enter/confirm;
- Esc/back.

### Controller
- D-pad/stick;
- confirm;
- back;
- visible focus at all times.

No dead focus states.

## 13.6 Interaction/prompt behavior

When player is in merchant interaction range and no blocking UI is open:
- prompt appears once;
- pressing Interact opens merchant menu exactly once.

While merchant menu is open:
- merchant prompt may hide;
- pressing Interact must not immediately reopen/duplicate the menu;
- player cannot shoot/dash/use consumables through the trade UI;
- inventory/pause/menu ownership must remain coherent.

On close:
- return to gameplay;
- if player is still in range, merchant prompt may reappear;
- Interact can open the merchant again.

## 13.7 Multiplayer / authority

Do not regress co-op architecture.

Use current host-authoritative transaction services.

Verify:
- client purchase request cannot mint items/coins locally;
- duplicate request is safe/idempotent according to current transaction system;
- merchant stock state remains coherent;
- local presentation closes/opens independently without freezing authoritative simulation improperly.

If live UGS remains unavailable, use the deterministic network harness.
Do not claim live Relay verification.

## 13.8 Merchant tests

Add deterministic tests for:

- prompt visible in merchant range;
- Interact event fires;
- merchant menu opens;
- only one menu instance opens per press;
- focus stack is pushed;
- gameplay input is gated;
- Esc/back closes;
- close restores gameplay input;
- reopen works;
- buy valid item;
- insufficient coins rejected;
- full backpack rejected;
- exact coin deduction;
- exact item grant;
- no duplicate on repeated confirm;
- starter/unsellable item cannot be sold;
- valid sell path if supported;
- item icon/details render;
- mouse navigation;
- keyboard navigation;
- controller navigation;
- scene/run transition cleans up trade UI;
- no stale merchant menu remains after leaving expedition.

---

# 14. TESTS TO ADD


## HP
- new run starts at true effective max;
- armor modifier included;
- no mid-run free heal;
- HUD current/max correct.

## Dash
- recharge ~15% slower;
- distance ~15% shorter;
- collision intact;
- i-frames unchanged;
- icon state matches cooldown.

## Equipment sync
- weapon replaced mid-run -> HUD updates;
- P9 -> Wasp-45 if both exist;
- swap updates active highlight;
- inventory equip/drag-drop updates HUD;
- consumable change updates icon;
- consumable use decrements stack.

## Presentation
- two weapon icon slots exist;
- active state distinct;
- firearm ammo displayed;
- melee has no fake ammo;
- consumable icon slot exists;
- Dash icon exists;
- no permanent `DASH READY`;
- no permanent `Bandage xN` text;
- no permanent full weapon-name rows;
- HUD fits 640×360;
- no overlap among HP/Dash/weapons/consumable/objective/coin areas.

## Merchant
- `[E] TRADE WITH MERCHANT` target and actual interaction target are the same merchant;
- E/Interact opens trade menu exactly once;
- merchant stock renders with icons/prices;
- player carried coins render correctly;
- valid purchase succeeds exactly once;
- insufficient coins/full inventory reject cleanly;
- selling works if supported by the authoritative design;
- starter unsellable item remains protected;
- gameplay input is blocked while trade UI is open;
- mouse/keyboard/controller navigation works;
- closing and reopening works without duplicate screens or stale focus.

---

# 15. BUILT-PLAYER PROOF

Capture under:
`TestResults/StartHpDashIconHudProof/`

At minimum:

1. new run with +MaxHP armor showing CurrentHP == EffectiveMaxHP;
2. graphical Dash icon;
3. Dash cooldown progress;
4. graphical weapon slots with P9 active;
5. equip/switch to Wasp-45 and HUD updates immediately;
6. Field Knife active with no fake ammo;
7. Bandage consumable icon with stack chip;
8. consume one Bandage and count updates;
9. inventory equip action followed by correct live HUD;
10. clean 640×360 frame with no overlaps;
11. player in merchant range with `[E] TRADE WITH MERCHANT`;
12. merchant trade menu open after pressing E;
13. merchant item selected with icon/details/price visible;
14. successful purchase showing exact coin/item-state change;
15. closing the merchant menu and returning to gameplay cleanly.

If Wasp-45 exact name differs, use actual corresponding existing weapon and document it.

---

# 16. FULL VALIDATION

After final source change run:

- strict EditMode;
- strict PlayMode;
- ContentCountValidator;
- FinalProductionValidator;
- PresentationValidator;
- AssetPipelineValidator;
- ReleasePathScan;
- relevant inventory/HUD/input/combat tests;
- merchant interaction/trade/economy tests.

Requirements:
- failed = 0;
- no stale XML;
- no missing scripts/references;
- no release placeholders.

Do not disable Smart App Control.

---

# 17. RELEASE BUILD + SMOKE

Produce clean Windows x64 NON-DEVELOPMENT build.

Run headless + windowed smoke.

Require:
- correct full effective HP at expedition start;
- new Dash cooldown/distance;
- Dash HUD matches cooldown;
- weapon HUD updates after runtime equip/swap;
- consumable HUD updates after runtime equip/use;
- no stale P9 display after Wasp-45 is active/equipped;
- all icon assets resolve;
- no gameplay exceptions;
- Inventory/Pause still work correctly above HUD;
- pressing E on the merchant reliably opens the trade menu;
- merchant buy/sell behavior (as approved by current design) works without duplication or coin errors;
- closing merchant UI restores gameplay input/focus/cursor correctly.

---

# 18. FINAL REPORT

Write:
`production/START_HP_DASH_AND_ICON_HUD_REPORT.md`

Include:
- root cause of incorrect run-start HP;
- exact initialization-order fix;
- exact old/new Dash cooldown/recharge values;
- exact old/new measured Dash distance;
- root cause of stale HUD equipment data;
- exact synchronization/event fix;
- final weapon HUD layout;
- final consumable HUD layout;
- final Dash HUD behavior;
- root cause of merchant prompt-with-no-menu bug;
- exact merchant interaction/UI composition fix;
- merchant buy/sell transaction verification;
- tests added;
- exact test counts;
- build/smoke results;
- proof capture paths;
- any remaining genuine limitation.

Terminal status:

`START_HP_DASH_ICON_HUD_MERCHANT_COMPLETE`

only if all repository-local requirements above are fixed and verified.

Use:
`START_HP_DASH_ICON_HUD_MERCHANT_INCOMPLETE`

if anything remains unfinished.

Do not ask for approval during execution.

BEGIN NOW.
