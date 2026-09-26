# RUINRAIL — Run-Start HP, Dash Tuning, Icon HUD and Merchant Report

> **Scope:** `RUINRAIL_START_HP_DASH_ICON_HUD_MERCHANT_PROMPT_V2` — the run starting at the true effective maximum HP,
> the ~15 % dash nerf, the stale-equipment HUD bug, the icon-based dash / weapon / consumable HUD bands, and the
> merchant flow that showed `[E] TRADE WITH MERCHANT` but opened nothing. Executed from the state after
> `GRAPHICAL_INVENTORY_REWORK_REPORT.md`. Weapon/armor/enemy stats, consumable effects, backpack capacity, the slot
> model, the starter kit, save/wipe semantics and the multiplayer authority model are unchanged.
> **Terminal status:** `START_HP_DASH_ICON_HUD_MERCHANT_COMPLETE`

| Gate (final sequence after the last source change: PlayMode → EditMode → validators → build → smokes) | Result |
|---|---|
| `./scripts/run-unity-tests.ps1 -TestPlatform EditMode` | **PASS — 792 passed / 793 discovered, 0 failed** (1 skipped: the pre-existing `RUINRAIL_LIVE_SERVICES=1` live-Sessions check) |
| `./scripts/run-unity-tests.ps1 -TestPlatform PlayMode` | **PASS — 659 passed / 659 discovered, 0 failed** (see §9 for the one flaky pre-existing live test observed during the pass) |
| `ContentCountValidator` / `FinalProductionValidator` / `PresentationValidator` / `AssetPipelineValidator` / `ReleasePathVisualScan` / `ArtProductionContract` / `AnimationAssetAudit` / `AudioAssetAudit` / `MusicAssetAudit` / `CompletionAssetManifest` | PASS · PASS 9/9 (0 external) · PASS 8 · PASS 6 (307 manifest roles incl. the new `ui.dash_icon`) · CLEAN · PASS 10 · COMPLETE · COMPLETE · COMPLETE · generated — all exit 0 |
| Release build (`ReleaseBuildTool.BuildBatch`, Windows x64, non-development) | **Succeeded** — 0 errors, 1 warning (the external UGS project-ID notice), 157.9 MB |
| Built-player smoke, headless (`RUINRAIL.exe -batchmode -nographics -smoke -seed 11 …`) | **Success** — 15 playability + 12 combat + 26 loot + 22 audio + 34 inventory + **24 HUD + 15 merchant** checks, `ReturnToMenuOk: true`, 0 exceptions / missing scripts / missing references |
| Built-player smoke, windowed (`-smoke -seed 11 -screen-width 1280 -screen-height 720 -hudproofdir TestResults/StartHpDashIconHudProof`) | **Success** — the same checks; 15 captures written by the shipped executable |

Unity `6000.3.24f1`; no pinned package or Editor version changed. Baseline before this pass: EditMode 792/793,
PlayMode 628/628 → **+31 PlayMode tests** (EditMode unchanged; four existing PlayMode test files updated to the
icon layout and the new dash values). Smart App Control was not touched.

---

## 1. Root causes

| # | Symptom | Root cause (verified in code) | Fix |
|---|---|---|---|
| 1 | A new run started at `100 / 120` with the Scrap Vest | `PlayerEntityBuilder` creates the `HealthComponent` at the base maximum (100). `PlayerRig.Build` then registers the loadout's stat sources (`LoadoutStatRegistrar`) and `PlayerStatsBinder.Bind()` → `SyncMaxHealth` → `HealthComponent.ResizeMaxHealth(120)`, which by design **clamps and never refills** (the mid-run invariant). Nothing ever filled the entity to the effective maximum at run start. | `PlayerRig.Build` now calls `InitializeRunHealth` **once, after** `StatsBinder.Bind()`: `health.SetMaxHealth(StatsBinder.Stats.MaxHealth)` — the one authoritative figure the HUD reads. Only the rig build (a new run) fills; every later equipment change still goes through `ResizeMaxHealth`. |
| 2 | Dash recharged / travelled too far | Config values `_dashSpeed 20`, `_dashCooldown 1.25` (design update requested). | See §2. |
| 3 | HUD still showed the P9 after equipping the Wasp-45 | `DungeonHudViewModel.BindWeapons` **cached the weapon component references**. `PlayerRig.MountWeapons` destroys and re-adds the weapon components on every `EquippedChanged`, so the HUD kept reading a destroyed `RangedWeapon` (its last magazine) and its definition name. HP/MaxHp were also only refreshed on `Damaged`/`Healed`, and the consumable count only on inventory events (a use decrements the stack in place). | The view model now resolves the weapon **live** on every refresh (`_loadout.GetSlot(slot)`, a destroyed component counts as none), takes icon/rarity/definition id from the **inventory's equipped item** (the authority), and `Tick()` re-reads HP/MaxHp and the active stack. The bound references only serve a loadout-less binding (tests). |
| 4 | Bottom-right was a permanent `Bandage x3` text line | Text-only HUD. | Icon slot + `xN` chip (§4). |
| 5 | Bottom-left was a permanent `DASH READY` / `DASH n%` text line | Text-only HUD. | Dash icon slot with ready / cooldown-sweep / disabled states (§4). |
| 6 | Bottom-centre was `>1 P9 Ranger  12 / 36` text | Text-only HUD. | Two graphical weapon slots (§4). |
| 7 | `[E] TRADE WITH MERCHANT` shown, E did nothing | `DungeonMerchantInteractable.Interact` raised `Opened`, but the **only subscriber was the audio binder** (`GameplayAudioBinder`). No trade view-model, no view, nothing pushed on the focus stack. The `DungeonMerchantService` (offers, `Buy`, `Sell`, `QuoteSellValue`, sold-state) existed and was tested, unused by any screen. | New `MerchantViewModel` + `MerchantView`, wired in `ExpeditionScene` (§5). |

## 2. Dash tuning — exact before / after

`Assets/Game/ScriptableObjects/Player/PlayerBalanceConfig.asset` and the C# defaults in `PlayerBalanceConfig.cs`:

| Value | Before | After | Rule |
|---|---:|---:|---|
| `_dashCooldown` | 1.25 s | **1.4706 s** | `NewCooldown = OldCooldown / 0.85` (recharge rate × 0.85) |
| `_dashSpeed` | 20 u/s | **17 u/s** | `NewDistance = OldDistance × 0.85` over the unchanged duration |
| `_dashDuration` | 0.18 s | 0.18 s | unchanged |
| `_dashIFrameDuration` | 0.10 s | 0.10 s | unchanged |
| Dash distance (speed × duration) | 3.60 tiles | **3.06 tiles** | × 0.85 |
| Direction rules / wall collision / `PlayerDash` code | — | — | unchanged (only two doc comments updated) |

Specs updated with a design-update note: `player/14_DASH_AND_MOVEMENT.md` ("~1.47 s"), `07_TUNABLE_VALUES.md`
(line 21: `1.4706 seconds (1.25 / 0.85)`, distance 3.06). The host-side `DashValidator` reads the config, so co-op
enforcement follows automatically (`NetworkMotionTests` re-asserted at 1.4706 and its post-cooldown wait extended
from 1.3 s to 1.5 s). Tests that hard-coded 1.25 (`PlayerBalanceConfigTests`, `NetworkMotionTests`,
`PlayerStatsWiringTests`, incl. the 35 % cap case `1.4706 × 0.65`) were updated. The +30 % distance and 35 % cooldown
caps are relative and unchanged.

New `DashTuningTests` (6): exact config values; recharge takes 15 % longer with no early second dash (still on
cooldown at 1.25 s, ready after 1.4706 s); displacement ≈ 3.06 tiles and clearly under the old 3.6; i-frames still
~0.10 s and end before the 0.18 s movement; the dash stops at a wall with no tunneling; the HUD cooldown fraction
equals `CooldownRemaining / CurrentDashCooldown` (e.g. > 55 % left after 0.5 s, 0 when ready).

## 3. Run-start HP

`App/PlayerRigComposer.cs` (`PlayerRig.Build`): after `MountWeapons(); Inventory.EquippedChanged += …;
StatsBinder.Bind();` → `InitializeRunHealth(health)` sets the entity to `PlayerStats.MaxHealth` (which is
`round((BaseMaxHealth + Σflat) × multiplier)` over every registered equipped-item source). `RunStartHealth` records
it for diagnostics. No mid-run free heal: `OnEquippedChanged` still only calls `StatsBinder.Bind()` →
`ResizeMaxHealth` (clamp only).

New `RunStartHealthTests` (7): no armor → 100/100; Scrap Vest → **120/120**; the heaviest catalog armor → its own
effective max; multiple modifiers aggregated flat-then-percent (`(100+20+10)×1.10 → 143`) and a fresh run fills to
it; a mid-run armor swap raises the max without healing, removing armor clamps **only** when current exceeds the
reduced max (72 stays 72; 120 → 100); a new expedition after a damaged run is full again and the HUD denominator is
`PlayerStats.MaxHealth`; the fresh-profile starter-kit grant (`StarterKitService.GrantFirstProfileKit`, the wipe /
new-profile path) starts at 120/120.

Built-player proof (`hud_01_full_hp_at_run_start.png`, smoke check *run starts at effective max HP (120/120,
PlayerStats 120)*) and the live PlayMode proof (`live_01_…`, evidence line `run start HP 120/120`).

## 4. Icon HUD (`UI/Hud/DungeonHudView.cs` rewritten, `UI/Hud/HudSlotViews.cs` new, `UI/Hud/DungeonHudViewModel.cs` reworked)

Layout at the 640×360 reference (corner-anchored, integer offsets, 4 px grid; every panel inside the frame, no two
panels and no two text lines share pixels — asserted by `DungeonHudLayoutRegressionTests` and the smoke):

```
top-left  DEPTH n — BIOME / objective / party lines          top-centre boss bar          top-right  n COINS

bottom-left                          bottom-centre (256 px, centred)                  bottom-right
[dash 24×24] [HP bar 150×8      ]    [1][icon 32] name        [2][icon 32] name        [icon 32 ]
             [120 / 120         ]    rarity frame  12 / 124    rarity frame  (melee:     rarity frame
             (6,6 / 34,6)            brackets=active RMB line  icon only)                x3 chip
```

- **Dash (`HudDashIconView`, 24 px slot, 16 px icon):** the generated `ui_dash_icon` (two amber chevrons with a
  motion trail, `UiFactory.DashIcon`) on the skin's slot plate. **Ready:** full icon, amber corner brackets.
  **Cooldown:** dimmed icon under a radial `Image.Filled` sweep whose `fillAmount` is
  `CooldownRemaining / CurrentDashCooldown` from `PlayerDash` (the authority) — it clears as the dash recharges.
  **Disabled** (Downed/Dead/no dash): dimmed plate, grey icon, rust strike. No text anywhere in the slot.
- **Weapons (`HudWeaponSlotView` ×2, 40 px slot + 80 px text column):** slot plate, the equipped item's **rarity frame**
  (skin frame by `ItemInstance.Rarity`), the definition's **icon**, a **slot-number chip** (1 / 2). The **active** slot
  has the bright plate, amber brackets, amber name and number; the inactive one is dimmed. The resource line shows
  `12 / 124` (or `NO AMMO` in danger red, `HEAT n%` / `OVERHEATED`, `READY` / `DRAW n%`) **only for weapons that have
  a resource**; a melee weapon shows icon + name only. The legendary RMB line (`RMB READY` / `RMB n%`) is a third line
  under the resource. An empty slot shows the neutral plate and centre mark.
- **Consumable (`HudConsumableSlotView`, 40 px):** icon + rarity frame + `xN` chip bottom-right (`x3` → `x2` after a
  use; nothing equipped → neutral plate, no text).
- **HP:** bar over the number, on a translucent plate (readability over bright tiles); shield tag unchanged.
- Everything is drawn through `UiBuild`/`UiSkin` (the inventory's plate, rarity frames, pixel face), so the HUD and
  the inventory share one visual language. The HUD never searches the scene or mutates gameplay (the existing
  source-scan test still passes over the new files).

View-model changes (`DungeonHudViewModel`): `HudWeaponState.{DefinitionId, Icon, Rarity, ShowsResource}`,
`HudSnapshot.{ConsumableIcon, ConsumableRarity, HasConsumable, DashDisabled}`; `WeaponOf(slot)` reads the loadout
live; `RefreshConsumable()` and the HP re-read run in `Tick()`; icons come from `ItemDefinition.Icon` (the one icon
system), rarity from the equipped instance. `BindWeapons` keeps its signature.

Art pipeline: `UiFactory.DashIcon()`, `ArtIntegration.GenerateUiSprites` writes `ui_dash_icon.png`,
`ArtIntegration.GenerateMissingUiSprites[Batch]` (writes only what is missing, applies the import contract, binds
`UiSkin.DashIcon`, records provenance), `UiSkin.DashIcon` / `EditorSetDashIcon`, `BindUiSkin` binds it too,
`CompletionAssetManifest.UiRoles` += `ui.dash_icon` (INTEGRATED, provenance recorded), `GameplayMockRenderer` draws
the icon instead of `DASH READY`. Spec note added to `ui/91_DUNGEON_HUD.md`.

New `HudEquipmentSyncTests` (7, against the real `PlayerRig`): starter kit (P9 active with ammo, knife icon only,
bandage chip); **Wasp-45 equipped through the inventory window replaces the P9 on the HUD with its own magazine /
reserve, icon and rarity** (and stays after the window closes); secondary replacement + `1`/`2` switching move the
highlight synchronously; drag/drop (same transfer path) and dropping the equipped secondary leave an empty slot;
consumable replace (medkit) and decrement (`x2` → `x1` → empty); reopening the inventory publishes nothing, a scene
transition rebinds a fresh HUD and the disposed one follows nothing; a destroyed weapon component is never read.

## 5. Merchant (`UI/Merchant/MerchantViewModel.cs`, `UI/Merchant/MerchantView.cs` new; `App/ExpeditionScene.cs`)

Flow: `PlayerInteractor` (E, one press → one `Interact`) → `DungeonMerchantInteractable.Interact` → `Opened` →
`ExpeditionScene.OnMerchantOpened` binds the view model to **that room's** `DungeonMerchantService` and the run's
inventory / Carried wallet and opens it **once** (`_merchant.IsOpen`, pause or inventory open → ignored). While open:
`GameplayInputGate.Hold()` (no shot, dash, interact or weapon change leaks through), the solo world pause, the
pointer cursor (`CursorService.PushOverlay`), the interaction prompt hidden, the window's `FocusList` pushed on the
run's `MenuInput` stack. Esc/B/`CLOSE` close it (`BeforePauseToggle` closes the trade screen before pausing, `Back`
routes to it before the inventory); closing releases the gate, resumes the world, pops the focus list and restores
the aim cursor and the prompt. Entering a new depth closes it and drops the binding; the scene's `OnDestroy` disposes
it (a disposed screen ignores late `Opened` events).

Window (600×336 on the 640×360 frame, same chrome as the inventory):

```
┌ MERCHANT — DEPTH n ────── ENTER: TRADE   ESC: CLOSE ────────────── CARRIED COINS  n ┐
│ STOCK / YOUR BACKPACK        [BUY][SELL] │ DETAILS                                  │
│ [icon+rarity] Name              300 C    │ name (rarity colour)                     │
│               RARITY · CATEGORY          │ RARITY · CATEGORY · xN · PRICE n C       │
│ [icon+rarity] Light Ammo         60 C    │ stat rows … affixes … Legendary special   │
│               COMMON · AMMO · x60        │ — VS EQUIPPED — a vs b (+/-/=)           │
│ [icon+rarity] Name              SOLD  —  │ message / block reason (SOLD OUT, …)     │
│   (up to 8 rows × 32 px)                 ├──────────────────────────────────────────┤
│                                          │ BACKPACK  n / 8 SLOTS FREE               │
│                                          │ [   BUY / SELL   ]   [     CLOSE     ]   │
└──────────────────────────────────────────┴──────────────────────────────────────────┘
```

- **BUY:** the depth's deterministic offers (`DungeonMerchantService.Offers`), each an icon row with the skin's slot
  plate, **rarity frame**, name, rarity · category (· xN), **price**; sold rows dim to `SOLD —`. `Buy()` →
  `merchant.Buy(offer.Index, BackpackContainer)` (debit → `TryAdd` → refund on failure, sold state marked by the
  service) — **exactly once**: a repeated confirm returns `AlreadySold` and changes neither coins nor backpack.
  Refusals show `NOT ENOUGH COINS` / `BACKPACK FULL` / `SOLD OUT` in the details panel before and after the attempt.
- **SELL:** every backpack item with its `QuoteSellValue`; starter gear (`IsUnsellable`) is listed as `STARTER` and
  refused (`Sell()` → `merchant.Sell(BackpackContainer, instanceId)` → `Unsellable`, no coin change).
- **Details:** `ItemTooltip.Build` (same as the inventory) + `TooltipComparison` against the equipped item of the
  slot the offer would go to; coins, free backpack slots, hints per input device.
- **Input:** mouse (rows select, tabs and BUY/SELL/CLOSE click, hover/focus brackets), keyboard/controller (one
  `FocusList` with a 2D navigator: tabs ← →, rows ↑ ↓, then BUY/SELL and CLOSE; ENTER/A on a focused row or the
  action button trades; a fresh open starts on the first row). UI sounds through `UiSoundBus` like the inventory.

New `MerchantTradeUiTests` (9): prompt in reach + E opens the trade screen exactly once (focus list pushed, gate
held, world paused; a second E swallowed); window shows icons / rarity frames / prices / coins / free slots and the
tooltip-based details, every text on the pixel face; buy debits exactly the price, delivers exactly the offer's
quantity once, marks the offer sold, repeated confirms change nothing; buy refused without coins / with a full
backpack (refund, offer not sold) with visible feedback; sell credits the quote, removes the item, starter gear is
protected; keyboard/controller navigation across rows, tabs and actions, controller hints, mouse select and close;
close restores gameplay and reopen keeps the sold state; a scene-transition dispose releases input/focus and ignores
further opens; the co-op authority path `LootAuthorityService.RequestMerchantBuy` is idempotent per transaction id
(cached result on replay, charged once, `AlreadyTaken` afterwards).

Selling is approved by `dungeon/58` ("Player may sell dungeon-held items for Carried Coins"); no refresh button
(none in the spec); offers, prices and rarity rolls are the service's (untouched).

## 6. Files

**Runtime / editor (changed):** `App/PlayerRigComposer.cs` (run-start fill), `App/ExpeditionScene.cs` (merchant
wiring, `HudView`, prompt gating), `App/SmokeRunner.cs` (two new stages, `HudChecks` / `MerchantChecks`),
`Player/PlayerBalanceConfig.cs` + `.asset` (dash values), `Player/PlayerDash.cs` (comments only),
`UI/Hud/DungeonHudViewModel.cs`, `UI/Hud/DungeonHudView.cs` (rewritten), `UI/Theme/UiSkin.cs` (`DashIcon`),
`Editor/ArtGen/UiFactory.cs` (`DashIcon`), `Editor/ArtGen/ArtIntegration.cs` (generate/bind the icon),
`Editor/ArtGen/GameplayMockRenderer.cs`, `Editor/Production/CompletionAssetManifest.cs` (`ui.dash_icon`).
**New:** `UI/Hud/HudSlotViews.cs`, `UI/Merchant/MerchantViewModel.cs`, `UI/Merchant/MerchantView.cs`,
`App/SmokeRunner.HudMerchant.cs`, `Assets/Game/Art/UI/ui_dash_icon.png` (+ provenance entry, skin binding).
**Docs:** `player/14_DASH_AND_MOVEMENT.md`, `07_TUNABLE_VALUES.md`, `ui/91_DUNGEON_HUD.md`.
**Tests new:** `RunStartHealthTests` (7), `DashTuningTests` (6), `HudEquipmentSyncTests` (7),
`MerchantTradeUiTests` (9), `StartHpDashIconHudProofTests` (1 live run). **Tests updated:**
`DungeonHudLayoutRegressionTests` (icon bands, +1), `DungeonHudTests` (slot views), `DungeonRegressionProofTests`
(dash slot pixels), `PlayerBalanceConfigTests`, `NetworkMotionTests`, `PlayerStatsWiringTests` (1.4706).

## 7. Built-player proof — `TestResults/StartHpDashIconHudProof/`

Written by `Builds/Windows64/RUINRAIL.exe -smoke -seed 11 -screen-width 1280 -screen-height 720 -hudproofdir …`
(the windowed run; the headless run performs the same checks without captures):

| Capture | Shows |
|---|---|
| `hud_01_full_hp_at_run_start.png` | first frames of the run: `120 / 120` with the Scrap Vest |
| `hud_02_dash_icon_ready.png` | dash icon slot, READY (brackets, full icon) |
| `hud_03_dash_icon_cooldown.png` | dash icon under the cooldown sweep right after a dash |
| `hud_04_weapon_slots_p9_active.png` | slot 1 P9 Ranger active (icon, frame, brackets, `n / n`), slot 2 Field Knife icon only |
| `hud_05_weapon_slots_wasp45_after_equip.png` | slot 1 now the Wasp-45 with its own magazine / reserve (no stale P9) |
| `hud_06_knife_active_no_fake_ammo.png` | slot 2 active, no ammo line |
| `hud_07_bandage_icon_stack_chip.png` | consumable icon slot with `x3` chip |
| `hud_08_after_consume_bandage.png` | `x2` after a use (HP readout followed the damage / heal) |
| `hud_09_inventory_equip_live_hud.png` | the inventory window open right after the equip, HUD already updated beneath |
| `hud_10_clean_640x360_frame.png` | the back buffer point-sampled to the 640×360 reference |
| `merchant_11_prompt_in_reach.png` | `[E] TRADE WITH MERCHANT` |
| `merchant_12_menu_open.png` | the trade window over the run |
| `merchant_13_item_selected.png` | an offer focused, details + price |
| `merchant_14_purchase_success.png` | `BOUGHT … FOR n COINS`, coins debited, row `SOLD` |
| `merchant_15_closed_gameplay_restored.png` | window gone, prompt back, aim cursor |

The live PlayMode proof (`StartHpDashIconHudProofTests`) writes the matching `live_01…live_15` captures (+ `_x2`)
and `live_hud_merchant_evidence.txt` into the same folder from the Editor's real boot flow (seed 11, OvergrownLabs).

## 8. What was deliberately not changed

Weapon, armor, enemy and consumable data; backpack capacity and the slot model; the starter kit; save/wipe
semantics; the multiplayer authority model (`RequestMerchantBuy` unchanged, only tested); the dash's i-frames,
direction rules and collision; `HealthComponent`'s clamp-never-refill invariant for mid-run changes; the inventory
window of the previous pass. The `PlayerBalanceConfig` asset keeps its GUID (values edited in place).

## 9. Notes and limitations

- `CombatAimCollisionProofTests.LiveRun_DirectHit_…` (pre-existing live test, not touched by this pass) failed once
  in an earlier full PlayMode run and once in isolation on the *direct crosshair shot* step (HP 56 → 56), then passed
  in isolation and in the final full run with identical sources (`hp 56->43`). It is timing-sensitive
  (`FireAndSettle` waits at most 1.5 s of `Time.time`). The EditMode `FinalMvpAuditTests` reads the last
  `PlayMode-results.xml`, so one EditMode run that followed the flaky PlayMode run reported that file (658/659); the
  gates in the table are the final clean sequence.
- The built-player windowed smoke captures are 1920×1080 (the shipped player applies its saved display mode and
  rendered at the desktop size, the clean 3× of the 640×360 reference, rather than the 1280×720 requested on the
  command line — the same as the previous pass); `hud_10_clean_640x360_frame.png` is the point-sampled 3× downscale
  of that back buffer, so it is a true 640×360 reference frame.
- Live Relay / Sessions are not claimed (out of scope, as before).
- The merchant sells from the backpack only (equipped gear must be unequipped first); that matches "dungeon-held
  items" in `dungeon/58` and keeps the starter kit protected by `IsUnsellable`.
