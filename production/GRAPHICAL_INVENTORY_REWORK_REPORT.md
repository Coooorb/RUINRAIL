# RUINRAIL — Graphical Inventory Rework Report

> **Scope:** the inventory UI/UX and presentation layer of `RUINRAIL_GRAPHICAL_INVENTORY_REWORK` (the Tab inventory of
> a run), executed from the state after `LOOT_AMMO_AUDIO_RUNTIME_FIX_REPORT.md`. The item model, the inventory rules,
> the transfer service, the five equipment slots, the 8-slot backpack, loot rules, stats and the network authority
> model are unchanged. No crafting, no body-part armor, no new slot categories, no storage behaviour.
> **Terminal status:** `GRAPHICAL_INVENTORY_REWORK_COMPLETE`

| Gate | Result |
|---|---|
| `./scripts/run-unity-tests.ps1 -TestPlatform EditMode` | **PASS — 792 passed / 793 discovered, 0 failed** (1 skipped: the pre-existing `RUINRAIL_LIVE_SERVICES=1` live-Sessions check) |
| `./scripts/run-unity-tests.ps1 -TestPlatform PlayMode` | **PASS — 628 passed / 628 discovered, 0 failed** |
| `ContentCountValidator` / `FinalProductionValidator` / `PresentationValidator` / `AssetPipelineValidator` / `ReleasePathVisualScan` / `ArtProductionContract` / `AnimationAssetAudit` / `AudioAssetAudit` / `MusicAssetAudit` / `CompletionAssetManifest` | PASS 52/52 · PASS 9/9 (0 external) · PASS 8 · PASS 6 · CLEAN · PASS 10 · COMPLETE · COMPLETE · COMPLETE · generated — all exit 0 |
| Release build (`ReleaseBuildTool.BuildBatch`, Windows x64, non-development) | **Succeeded** — 0 errors, 1 warning (the external UGS project-ID notice), 157.8 MB |
| Built-player smoke, headless (`RUINRAIL.exe -batchmode -nographics -smoke -seed 2 …`) | **Success** — 15 playability + 12 combat + 24 loot + 22 audio + **34 inventory** checks, `ReturnToMenuOk: true`, 0 exceptions / missing scripts / missing references |
| Built-player smoke, windowed (`-smoke -seed 2 -screen-width 1280 -screen-height 720 -inventoryproofdir TestResults/GraphicalInventoryProof`) | **Success** — the same checks, 5 inventory captures written by the shipped executable |

Every gate ran after the last source change (suites and validators, then the build, then the two smoke runs of that
build). Unity `6000.3.24f1`; no pinned package or Editor version changed. Baseline before this pass: EditMode 792/793,
PlayMode 619/619 → **+9 PlayMode tests** (EditMode unchanged; two existing PlayMode tests updated to the new layout).

---

## 1. What the old inventory did wrong

`UI/Inventory/InventoryView.cs` was thirteen `Text` boxes on a flat plate: five equipment rows of `PRIMARY\nP9 Ranger
[COMMON]`, eight backpack boxes of wrapped text, one 31-line tooltip column, a `>`/`*` text marker for the cursor and
the selection. No icons, no slots, no frames, no rarity treatment, no details structure. And — found while wiring the
rework — **no navigation at all in the run**: `ScreenNavigation.Inventory` existed but nothing pushed it, so in the
dungeon the mouse, keyboard and controller could not move the cursor or equip anything; only the Shelter's loadout
station used the view model. Opening it did pause the world, but a click on the overlay still reached the weapon
(`FireHeld` was never gated by a menu layer). Its canvas sat under the run HUD (order 10 < 20), so the tutorial prompt
and HUD text drew across it.

## 2. What was changed

**New graphical window** (`UI/Inventory/InventoryView.cs` rewritten, `UI/Inventory/InventorySlotView.cs` new,
`UI/Theme/UiBuild.cs` new): a 600×336 window on the 640×360 reference frame (4 px grid), code-built from the skin's
9-sliced frames and the item definitions' own icons — RUINRAIL's dark-steel/charcoal chrome (`UiTheme`), amber accents,
the pixel face for every label, no glossy or fantasy elements, no copied texture.

```
┌ INVENTORY ────────── CLICK/ENTER: SELECT, MOVE   ESC: CLOSE ────────── CARRIED COINS  n ┐
│ EQUIPMENT (152 px)     │ SURVIVOR (128 px)     │ BACKPACK  n / 8 (280 px)                 │
│ [icon] PRIMARY         │ ┌──────────────┐      │ [ ][ ][ ][ ]   44 px slots, 8 px gaps     │
│        name / RARITY   │ │  portrait ×2 │      │ [ ][ ][ ][ ]                              │
│ [icon] SECONDARY       │ └──────────────┘      ├──────────────────────────────────────────┤
│ [icon] ARMOR           │ AMMO  LIGHT  60/180   │ DETAILS                                   │
│ [icon] ACCESSORY       │       MEDIUM  0/120   │ name (rarity colour)                      │
│ [icon] CONSUMABLE      │       HEAVY   0/60    │ RARITY · CATEGORY · xN · EQUIPPED         │
│  (5 rows × 52 px)      │       SHELLS  0/40    │ stat rows … affixes … Legendary special    │
│                        │ [EQUIP/UNEQUIP][DROP] │ — VS EQUIPPED — a vs b (+/-/=)            │
│                        │ [CLOSE]               │ message line (BACKPACK FULL, refusals)    │
└────────────────────────┴───────────────────────┴──────────────────────────────────────────┘
```

- **Equipment (left):** five `InventorySlotView`s (44×44) with captions PRIMARY / SECONDARY / ARMOR / ACCESSORY /
  CONSUMABLE, the item name fitted to the column (ellipsis, never a third line) and the rarity as text; an empty slot
  reads "— empty —" with the neutral slot plate and a faint centre mark.
- **Character (centre):** the survivor portrait — the player's own idle sprite (`CharacterAnimationSet` "Idle/S",
  handed over by `ExpeditionScene`; the view never loads art) at ×2 in a framed stage — the four ammo reserves with
  their per-slot caps (26), and the three action buttons (`UiControl`, the same control as the pause menu): EQUIP /
  UNEQUIP (label follows the cursor slot), DROP, CLOSE.
- **Backpack (right, top):** the authoritative 8 slots (`PlayerInventory.BackpackCapacity`) as a 4 × 2 grid; the header
  counts `n / 8` and turns amber with "— FULL" (92 BACKPACK FULL).
- **Details (right, bottom):** name in the rarity colour (luminance-floored for readability), `RARITY · CATEGORY ·
  xN · EQUIPPED · STARTER`, then up to ten `Label ……… value` rows from `ItemTooltip` (weapon damage/fire rate/magazine/
  reload/range/ammo, armor, accessory modifiers, affixes in terminal green, the Legendary special in amber) and the
  93 comparison against the equipped item with `(+) (-) (=)` as text plus colour; the last line is the action message
  (refusals, BACKPACK FULL) in the danger colour. Empty cursor slots explain themselves ("ACCESSORY — EMPTY").
- **Slot states** (each distinct without colour): hover = lighter plate; focus (keyboard/controller cursor) = amber
  corner brackets; selected (picked for a move) = 2 px amber inner frame + tint; occupied = the rarity frame
  (`ui_rarity_frame_common…legendary`) over the slot plate; stack count in a dark chip bottom-right (`x60`, `x3`).
- **Skin binding:** `UiSkin` now carries `PanelFrame`, `PanelEmphasis`, `InventorySlot` and the five rarity frames
  (`ArtIntegration.BindInventoryFrames`, batch `BindInventoryFramesBatch`; `Resources/UiSkin.asset` rebound). The
  sprites were already generated (spec 18); they were referenced by nothing at runtime. Item icons come from the one
  existing icon system (`ItemDefinition.Icon`, 72/72 bound) — no parallel icon system.
- **Primitives:** `UiBuild` (UI assembly) holds the rect/plate/border/panel/label/bracket/sliced builders; `UiKit`
  (App) forwards to it, so the front-end and the in-run inventory draw with one implementation.

**Interaction** (`InventoryViewModel` extended, `FocusNavigation` extended, `MenuInput` routing):
- One cursor for every device. Mouse: hover moves the cursor (details follow the pointer) and focuses the slot; click
  selects, a second click on the target slot moves/equips/swaps; **drag-and-drop** (uGUI drag handlers, a
  non-blocking ghost icon, drop on a slot = the same `MoveTo`); the action buttons click like every `UiControl`.
  Keyboard/controller: the window's `FocusList` is pushed on the run's `MenuInput` stack while open; a new
  `FocusList.Navigator` gives the list 2D navigation (arrows / WASD / D-pad / stick through `FocusStack.Navigate`),
  using the view model's own `NextCursor` rule (equipment column of 5, backpack 2×4 to its right, rows cross at the
  matching row, edges clamp, down off the bottom lands on the first *enabled* action button — never a dead end; CLOSE
  always exists); Enter / A activates (select → move), B / Back cancels a selection then closes.
- `InventoryViewModel`: `NextCursor` (pure), `PrimaryAction` (EQUIP a backpack item into its default slot / UNEQUIP an
  equipped one), `IconOf`, `DefinitionOf`, `AmmoReserve/AmmoCap`, `SlotLabel`, UI sounds (open Confirm, close Cancel,
  cursor Navigate, done Confirm, refused Failure — through the existing `UiSoundBus`, no new audio system).
- **Input coherence:** `Core/Input/GameplayInputGate` (new, counted hold) — while the in-run inventory or the pause
  menu is up, `PlayerInputReader` reports no Move/Fire/Special/Interact and raises no Dash/Reload/Interact/Weapon/
  Consumable action (the Inventory and Pause toggles pass through), so a click on a slot is never also a shot. Esc
  with the inventory open closes the inventory instead of pausing (`PauseMenuViewModel.BeforePauseToggle`); Tab under
  the pause is ignored; the window sits at canvas order 30 above the HUD and prompts; one window instance, the same
  focus list pushed once and removed on close; the pointer cursor is owned while open and the aim cursor returns
  (`CursorService` overlay, unchanged). The Shelter's loadout station still uses the same view model without the
  gameplay hold (it is not over gameplay).

**Network/authority:** untouched. The view model's every transfer still goes through `ItemTransferService`
(equipped-slot containers, backpack container, ground via `PlayerLootReceiver`); the host `LootAuthorityService` paths
are not involved in the local presentation. Live Relay is not claimed (no project link on this machine).

## 3. Tests

| Suite | Tests | Covers |
|---|---:|---|
| `GraphicalInventoryTests` (PlayMode, new) | 8 | window/panels inside the reference screen and on the grid, 4×2 backpack grid of hover-sized non-overlapping slots, 5-row equipment column; icon + rarity frame + count for one item of every category (weapon, armor, accessory, consumable, ammo) and 72/72 icons bound; details panel follows the cursor (name, rarity, category, stats, en dash, comparison, EQUIPPED, xN) and no label exceeds its box; hover/focus/selected states; keyboard/controller 2D navigation through `FocusStack` incl. action buttons and hints per device; mouse click-to-move, drag-and-drop, consumable slot, invalid drop feedback, EQUIP/UNEQUIP/DROP/CLOSE buttons, no duplicate/no loss; open/close holds the gate, pauses solo, reopen keeps the cursor and the loadout, dispose releases; the reader reports no gameplay input under a hold |
| `GraphicalInventoryProofTests` (PlayMode, new, live run) | 1 | boot → Shelter → dungeon; window over the real loadout with the survivor portrait; keyboard select + swap through the run's `MenuInput` stack; mouse hover/click swap and drag-drop; controller hints and action focus; Esc closes the inventory without pausing, the pause holds the gate too, reopen = one window; captures `live_01…05` |
| `InventoryUiTests` (PlayMode, updated) | 8 | coins text + graphical slot counts (the rest unchanged: service-only transfers, solo pause vs co-op, tooltips, ownership) |
| `InventoryFontRegressionTests` (PlayMode, updated) | 3 | every label on the pixel face at the authored size in whole-line boxes; world-space containment inside the window and no two text boxes overlap across the nested panels; the longest item name fits the name column or ellipsises and the details title shows it whole |

Regression: `PauseScreenInteractionTests`, `PauseMenuTests`, `FinalPlayabilityProofTests` (the "12_inventory_pixel_font"
capture), `BaseUiTests` (Shelter loadout) all pass unchanged. The built-player smoke gained a 34-check inventory stage.

## 4. Built-player proof (`TestResults/GraphicalInventoryProof/`)

Written by the shipped `RUINRAIL.exe` (windowed 1280×720, seed 2, Ruined Metro) — `SmokeRunner.Inventory.cs`:
- `inv_01_inventory_window_open.png` — the window over the run: equipment slots with the P9 Ranger / Field Knife /
  Scrap Vest / Bandage icons and rarity frames, the empty accessory slot, the portrait, ammo reserves, coins, the 4×2
  grid with the ammo stack `x60`, the Rare SMG and the Uncommon accessory.
- `inv_02_selected_item_details_panel.png` — keyboard: cursor navigated onto the SMG, Enter selects (amber selected
  frame), details show `Rattler-9 / RARE · WEAPON`, the stat rows and `— VS EQUIPPED —`.
- `inv_03_after_keyboard_swap_primary_is_smg.png` — keyboard move onto PRIMARY: swapped through the transfer service,
  the pistol parked in the backpack.
- `inv_04_after_mouse_swap_and_drag_drop.png` — mouse hover/click swapped the pistol back, drag-and-drop equipped the
  accessory, UNEQUIP returned it; no duplicate, no loss.
- `inv_05_controller_focus_on_action_button.png` — gamepad hints, D-pad down from the equipment column onto the
  UNEQUIP button with focus brackets; then CLOSE by A, world resumed, input released, aim cursor back; reopen = one
  window with the state intact.
- `smoke_windowed/smoke_result.json`, `smoke_headless/smoke_result.json` (all 34 inventory checks listed), player logs.
- Live PlayMode run: `live_01_inventory_window_over_the_run`, `live_02_keyboard_selected_smg_with_details`,
  `live_03_after_keyboard_swap`, `live_04_after_mouse_swap_and_drag_drop`, `live_05_controller_focus_on_action_button`
  (+ `_x2`), `live_inventory_evidence.txt`.

## 5. Files changed

Runtime (new): `Core/Input/GameplayInputGate.cs`, `UI/Theme/UiBuild.cs`, `UI/Inventory/InventorySlotView.cs`,
`App/SmokeRunner.Inventory.cs`. Runtime (changed): `UI/Inventory/InventoryView.cs` (rewritten),
`UI/Inventory/InventoryViewModel.cs`, `UI/Navigation/FocusNavigation.cs` (Navigator / Navigate), `UI/Theme/UiSkin.cs`
(frames), `UI/Pause/PauseMenuViewModel.cs` (gate hold, `BeforePauseToggle`), `Core/Input/PlayerInputReader.cs` (gate),
`App/UiKit.cs` (primitives forward to `UiBuild`; navigator-aware `MenuInput`), `App/ExpeditionScene.cs` (composition,
focus stack, portrait, Esc/Tab rules), `App/GameApp.cs` (gate reset per scene), `App/SmokeRunner.cs` (stage + result).
Editor: `ArtGen/ArtIntegration.cs` (`BindInventoryFrames` + batch). Assets: `Resources/UiSkin.asset` (frames bound),
`Builds/Windows64/` (rebuilt). Tests: `GraphicalInventoryTests.cs`*, `GraphicalInventoryProofTests.cs`*,
`InventoryUiTests.cs`, `InventoryFontRegressionTests.cs`. Docs: this report.

## 6. Remaining non-blocking limitations

- The Shelter's LOADOUT station keeps its list presentation (`StationPresentation` rows); only the in-run Tab inventory
  was in scope. It shares the same view model and would take the same window in a follow-up.
- The details panel shows up to ten stat/affix/comparison rows; a Legendary weapon with three affixes plus a full
  comparison can exceed that and the surplus comparison rows are cut (the stats and affixes always show first).
- Drag-and-drop is pointer-only by design; keyboard/controller use select → move on the same path.
- A hardware cursor is not part of a rendered frame; the pointer/aim switch is asserted by the tests and the smoke.
- Live UGS/Relay remains unlinked on this machine (no inventory action touches the authority paths in this pass).

## 7. Terminal status

`GRAPHICAL_INVENTORY_REWORK_COMPLETE`
