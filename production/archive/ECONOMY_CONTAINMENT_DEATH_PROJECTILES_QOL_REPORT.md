# RUINRAIL — Economy / Room Containment / Starter Fallback / Death Screen / Backpack Reorder / Projectile Visuals Pass

> **Scope:** `RUINRAIL_ECONOMY_CONTAINMENT_DEATH_PROJECTILES_PROMPT.md`, executed from the repository state after
> `ROOM_HUD_AUDIO_QOL_PASS`. The six listed runtime/QoL issues only. No weapon damage, enemy damage/HP, progression,
> save semantics, room topology, network authority or inventory capacity changed; no pinned Editor or package version
> changed; Smart App Control was not touched.
> **Terminal status:** `ECONOMY_CONTAINMENT_DEATH_PROJECTILES_INCOMPLETE`
>
> Every repository-local requirement (§2–§13 of the prompt) is fixed, tested and verified, and the built-player
> proof (§14) exists — from a **macOS** player, because this machine (`/Applications/Unity/Hub/Editor/6000.3.24f1`
> carries only `MacStandaloneSupport`) has no Windows Build Support module, so the **Windows x64 release build and its
> smokes (§16) are NOT RUN here**. A non-development macOS build of the identical scenes/options was produced as the
> verification substitute and both its smokes pass end to end (§10). §12 lists exactly what remains.

| Gate (final sequence after the last source change: build → smokes → PlayMode → EditMode) | Result |
|---|---|
| `./scripts/run-unity-tests.sh PlayMode` (strict, Unity `6000.3.24f1`, macOS) | **PASS — 721 passed / 721 discovered, 0 failed, 0 skipped** |
| `./scripts/run-unity-tests.sh EditMode` (strict) | **PASS — 849 passed / 850 discovered, 0 failed** (1 skipped: the pre-existing `RUINRAIL_LIVE_SERVICES=1` live-Sessions check) |
| `ContentCountValidator` | PASS — 52/52 counts exact, 0 problems |
| `FinalProductionValidator` | PASS — 9/9 sections, 0 errors, 0 outstanding external assets |
| `PresentationValidator` | PASS — 8 checks, 0 problems |
| `AssetPipelineValidator` | PASS — 6 checks, 0 problems (339 manifest roles with a stable id and a convention path) |
| `ReleasePathVisualScan` | CLEAN — 0 placeholder references, 0 missing references (4 scenes, 65 prefabs, 448 renderers) |
| `ArtProductionContract` | PASS — 10 checks, 0 problems |
| `CompletionAssetManifest` | **339 roles — 339 INTEGRATED, 0 PLACEHOLDER, 0 MISSING** (was 309; 30 `Projectile visuals` roles added, provenance recorded) |
| Projectile / weapon, room / AI / collision, merchant / economy, inventory, save / exploit-hardening, network-authority tests | all inside the two suites above (new files listed in §9) |
| Release build, Windows x64 non-development (`ReleaseBuildTool.BuildBatch`) | **NOT RUN — no Windows Build Support module on this macOS machine** |
| Release build, **macOS** non-development (`ReleaseBuildTool.BuildMacBatch`, verification substitute, same scenes / `BuildOptions.None`) | **Succeeded** — 0 errors, 2 warnings (the pre-existing UGS project-ID notice and the Unity Cloud symbol-upload notice), 175.5 MB |
| Built-player smoke, headless (`RUINRAIL.app/Contents/MacOS/RUINRAIL -batchmode -nographics -smoke -seed 31 …`) | **PASS** — 15 playability + 12 combat + 25 loot + 24 audio + 34 inventory + 24 HUD + 1 merchant + 12 weapon-cache + 31 room/HUD + **46 economy/containment/reorder/projectile + 7 starter-fallback + 9 death-screen** checks, `ReturnToMenuOk: true`, 0 exceptions / missing scripts / missing references |
| Built-player smoke, windowed (`-smoke -seed 11 -screen-width 1280 -screen-height 720 -proofdir … -hudproofdir … -inventoryproofdir …`) | **PASS** — same stages (seed 11 has a merchant room: 15 merchant checks incl. the UI ammo sale), 66 captures written by the shipped executable, music carried signal in 59/60 sampled frames |

Baseline before this pass: EditMode 815 discovered, PlayMode 679 → **+35 EditMode, +42 PlayMode tests**.

---

## 1. Merchant ammo sell price — root cause and fix

### Root cause

`DungeonMerchantService.QuoteSellValue` and `TraderService.QuoteSellValue` share one rule for every stackable:
`unitValue = SellValue(FlatPrice(definition)) × Quantity`. For consumables the flat price *is* a per-unit price, so
that is right. For ammo the flat price is the **bundle** price (Light ×60 = 60 Coins, base/77), so the 35 % item rule
was applied to the *whole bundle price* and then multiplied **per round**: `RoundToStep(60 × 0.35) = 20` Coins × 180
rounds = **3,600 Coins** for one full Light stack that costs 180 Coins to buy. Every one of the prompt's suspects was
checked on the way: the stack count was multiplied once (not twice), no rarity multiplier reaches ammo, the buy price
was not reused as payout, the bundle size was consistent (Merchant sells exactly `bundle.Units`), the UI showed the
same number the service paid (both call `QuoteSellValue`), and a retry was already refused (`SourceMissingItem`). The
defect was purely "bundle price treated as per-round value".

### Fix

No stronger authoritative rule exists (base/77 only lists bundle prices), so the prompt's rule is now data and code:

- `EconomyConfig.AmmoSellPercentOfPurchaseValue` = **15** (serialized, `Range(0,100)`; the existing asset picks the default up).
- `PriceService.AmmoSellValue(ammo, quantity)` = `⌊bundlePrice × quantity × 15 ÷ (bundleUnits × 100)⌋` in `long`
  arithmetic — 15 % of the **equivalent current purchase value for the exact quantity**, rounded **down**, no rarity
  multiplier, no 5-Coin step rounding, 0 for tiny quantities (no minimum-1 micro-stack exploit), no overflow.
  `PriceService.AmmoPurchaseValue` exposes the equivalent purchase value.
- Both services route `AmmoItemDefinition` through it; the UI row price and the `Sold` event carry the same number, the
  payout is credited exactly once and a repeated request for the same stack is `SourceMissingItem` (nothing more).
- base/77 carries the design update.

### Before / after

| Sale | Before | After |
|---|---:|---:|
| Light ×60 (one 60-Coin bundle) | 1,200 | **9** |
| Light ×180 (full backpack stack, 180 Coins of ammo) | 3,600 | **27** |
| Medium ×40 (70-Coin bundle) / ×120 full stack | 1,000 / 3,000 | **10 / 31** |
| Heavy ×20 (90-Coin bundle) / ×60 full stack | 600 / 1,800 | **13 / 40** |
| Shells ×12 (80-Coin bundle) / ×40 full stack | 360 / 1,200 | **12 / 40** |
| Light ×10 / ×1 | 200 / 20 | **1 / 0** (0 → `NO VALUE`, not a sale) |
| Buy Light bundle (−60) then sell it back | +1,140 net | **−51 net** (15 % returned, 85 % lost) |

Live-run evidence (`live_economy_containment_death_projectile_evidence.txt`): the SELL row for 60 light ammo is quoted
at 9 (was 1,200); `Sell()` moved Carried Coins 0 → 9; a second sell of the same stack returned `SourceMissingItem`.
Built player (windowed, seed 11): UI SELL of 119 light ammo — UI quote 17 == payout 17 (Coins 519 → 536, capture
`ecdp_02`); every bundle quote 9 / 10 / 13 / 12.

Consumables keep the approved per-unit 35 % rule (asserted: Bandage ×3 = 60).

## 2. Enemies leaving their room — root cause and fix

### Root cause

Two things, one shared consequence.

1. **A chasing body only respected solid geometry.** `EnemyController`/`MovesetActorController.FixedUpdate` steer
   toward the target and commit a velocity; `ObstacleSteering` and the physics body stop it at walls and at the
   combat-door **blocker**. Anything that is *not* solid is a route: a door whose lock is **pending** because a player
   is still standing in it (`RoomDoorLock` deliberately keeps the blocker off until the doorway is clear), an Event room
   that never locks while its Cursed-Chest / Supply-Signal waves are alive, and every cleared room. A pursuing pack
   slipped through beside the player, a knockback could push an enemy through an open doorway, and a Charger's dash
   only stopped at *walls*.
2. **The Boss acquired its target at spawn.** `MovesetActorController.Awake` finds the first `PlayerInput` in the
   scene; the arena is composed while the player already stands in the Start room, so the Boss went straight to
   `Chase` on depth load and walked out of its (unlocked, unentered) arena through the open door to meet the player
   anywhere on the depth — exactly the "especially Bosses" observation.

### Fix — authoritative room-bound context, movement constrained before it is committed

- **`EncounterBounds`** (`Combat/EncounterBounds.cs`): the room-bound context of one encounter actor — the world
  interior it may never leave (`Interior`) and the rectangle its body centre may occupy (`Legal` = interior inset by
  the body's collider radius). `ConstrainVelocity` cuts any velocity component that would carry the centre past the
  edge to exactly reach it (the tangent is kept, so pursuit *slides* along the edge and holds there facing the
  target), `FreeDistance` gives a dash/knockback the distance it may still travel, `ClampCenter` is the invariant. A
  `FixedUpdate` clamp pulls a body back by at most one physics step of contact push (diagnostic `Corrections`) — the
  safety net, never the mechanism; there is no visible teleport.
- **Room interior** (`RoomRuntime.InteriorWorldBounds` / `InteriorWorldBoundsOf`): the room minus its one-tile wall
  ring — which is where the door cells and the door blockers are. Every door cell is outside it (asserted for every
  shipped room of all three biomes), so **encounter exits are non-traversable for encounter AI** whatever the door does;
  closed combat doors remain physical blockers exactly as before.
- **Ownership** (`RoomRuntime.BindEncounterBounds`): the room binds every actor it spawns — encounter,
  reinforcements, summons (through `EncounterRuntime.Register`), event waves (`SpawnAdditionalEncounter`) — in
  `OnEnemySpawned`; `EliteEngagement.Begin` binds the Elite; `RoomCategoryComposer.BindBoss` binds the Boss and its
  summons.
- **Movers:** `EnemyController` and `MovesetActorController` constrain the steered velocity; `AttackResolver.TickDash`
  ends a dash at the legal edge like a wall (`LastDashStoppedByBounds`, same recovery window; the check is made in
  physics-step units because the tick runs at frame rate while the body moves per physics step); `ImpactReceiver`
  cuts a knockback step at the edge and ends it there **without** the wall-impact bonus (an open doorway is not a
  wall; `BoundsStops` diagnostic).
- **Boss target:** `BindBoss` clears the target the actor auto-acquired at spawn; `BossEngagement.Begin` hands the
  entering player over on the real arena entry, so the fight starts when the room does and *a player outside the arena
  never draws the Boss out*. A pursuit-only diagnostics seam (`MovesetActorController.SuppressAttacks`) lets the
  containment proofs ask only where the body may go.
- Network: the host runs the bounds; `AuthoritativeEnemySpawner.Capture` publishes the host body's position, so the
  replicated position is inside the room (asserted).

**Invariant, verified:** an encounter enemy's collider does not cross the legal encounter-room boundary — melee pursuit
at open doorways of all four exit directions across all three biomes (closest approach = the legal edge, 0.85 tiles
from the door line for a normal body; 1.30 for a Boss), a ranged enemy advancing to and backing off from a doorway, a
Charger's charge aimed through an open door, an Elite, Boss pursuit and a Boss dash in every biome, knockback toward an
open door, a lock still pending on a player standing in the doorway, an event wave in an unlocked room, and the
replicated position. Live-run: 5-enemy pack held at the open South door (closest 0.82, worst overshoot 0.034 tiles —
a sub-2 px contact push pulled back the next step, tolerance 0.06); Boss (Aegis Core) held at the open West arena door
(closest 1.30, overshoot 0.000, 0 corrections). Built player: `ecdp_03`, `ecdp_04`.

## 3. Automatic free Starter Loadout fallback

`StarterKitService.EnsureEquippedLoadout(PlayerInventory)`: when the live Base loadout has **no weapon equipped** in
either weapon slot (nothing equipped, everything stored, or a weapon slot emptied by the save validator's quarantine of
an unresolved reference), the existing kit (`CreateKit()` — P9 Ranger, Field Knife, Scrap Vest, Bandage ×1, Light Ammo
×60; the equipment Common, affix-free, unsellable) is merged through the **same** `MergeKitInto` the first-profile
grant and the no-weapon-anywhere rescue use: kit pieces fill *empty* slots, the rest goes to the backpack. A loadout
with a weapon equipped is never touched; a second call changes nothing; nothing is written to Storage.

`BaseSession.EnsureStarterLoadoutIfEmpty()` guards it (never during a run, never while the Base loadout is not the
authoritative safe loadout) and counts `StarterLoadoutFallbacks`. It runs from **every** Ready/Start path:
- the MULTIPLAYER station's READY control (`TerminalViewModel.ToggleReady` → `PrepareLoadout` hook; the station shows
  a full-width **`STARTER LOADOUT EQUIPPED`** line, and the station's state rows now refresh when the terminal changes
  instead of only on reopen);
- the TRANSIT station's READY (`MultiplayerPanelViewModel.SetReady` → feedback `STARTER LOADOUT EQUIPPED. Ready.`);
- START EXPEDITION (`TransitPanelViewModel.StartExpedition` → resolve → validate → preserve or fall back → commit → start).

It never runs on scene load: `BaseSession.Open` keeps only the existing first-profile grant and rescue grant.
Verified: valid loadout preserved byte-for-byte (fingerprint), no loadout → starter, empty equipment with backpack
items → starter and the items kept, armor-only → weapons added and the armor kept, unresolved weapon reference →
quarantined by the validator → starter, repeated preparation → no duplicates, wipe → recovery at the next Ready,
reopening the Shelter → nothing granted, Storage untouched. Built player: `ecdp_05a/05b`; live run: `live_05a/05b`.

**Residual, stated in §12:** the kit's Bandage and 60 Light Ammo are ordinary items (base/75), so a player who
deliberately strips every weapon before each run receives ~29 Coins of sellable consumables per run through the
fallback — the same property the existing rescue grant already has.

## 4. Death / Run Lost screen

`UI/RunEnd/RunFailedViewModel` + `App/RunFailedScreen`, in the established style (charcoal panel, steel edge,
danger-red `RUN LOST` title rule, pixel font, label/value rows, the loss lines in danger red).

**Trigger.** `ExpeditionScene.OnExpeditionEnded` — the existing expedition-ended callback after
`ExpeditionService.Fail()` closed the run (exactly once; no second loss implementation) — shows the screen only for a
**conclusive** loss: the summary is a failure **and** the party roster is wiped or the local player is Dead. Solo
death is conclusive (the last standing player dies outright). Co-op: a Downed-and-revivable player, or a Dead player
with a living teammate, sees no final screen — the existing spectate/wait behaviour is untouched; only the wipe
(`PartyLifeRoster.TeamWiped` → `PartyExpeditionBinding` → `Fail()`) shows it. Leaving through the pause menu keeps its
own path (`LeavingToMainMenu`). Every other end hands back to the Shelter as before.

**Content (tracked stats only):** DEPTH REACHED, BIOME, ROOMS CLEARED, ENEMIES DEFEATED, ELITES DEFEATED, BOSS DEFEATED
YES/NO, CARRIED COINS LOST, ITEMS LOST, XP EARNED (KEPT), and LEVEL a → b when the level changed. Nothing invented.

**Controls:** `RETURN TO SHELTER` (primary, focused first) and `MAIN MENU`; no Retry. Both are real `UiKit` controls on
the run's one `MenuInput` focus stack, so mouse, keyboard and controller reach them; each choice resolves once and the
other is then closed. While showing: `GameplayInputGate.Hold()`, the solo world pause, the pointer cursor, the dim plate
swallows world clicks, the inventory cannot open, Esc never opens the pause menu over it (`PauseMenuViewModel.
HandlePauseInput` is the reader's path, vetoed by the owner), Back does nothing. The loss is saved
(`SaveNow("expedition_end")`) before the screen appears; RETURN TO SHELTER only changes the scene; MAIN MENU uses the
existing leave path. Verified: solo death → screen; downed/revivable → no screen; co-op wipe → screen once, later
bleed-out death re-fails nothing; `Fail()` once; mouse click / keyboard-controller step + confirm; no duplicate layer;
marker closed, carried coins gone, banked coins intact, loadout empty at the Shelter. Built player: `ecdp_06`,
`ecdp_07`; live run: `live_06`, `live_07`.

## 5. Backpack slot reorder — root cause and exact behaviour

### Root cause

`InventoryViewModel.MoveTo` handled *every* pair of slots except one: the last branch, `// Backpack -> backpack: …
moving within it changes nothing about ownership`, simply `return Done()` — a no-op that reported success. The pointer
handlers, the selection state, the drop handler (`OnSlotDropped → MoveTo`) and the keyboard path (`Activate → MoveTo`)
were all correct and all ended in that no-op, which is why the previous test claims (which only checked the result
code) were insufficient. There was no auto-compaction anywhere (`ItemSlotContainer` keeps slot indices on remove and
through snapshots).

### Fix

`ItemSlotContainer.TryMove(from, to)` → `SlotMoveResult`: empty target = **Moved** (to exactly that slot), occupied
target = **Swapped**, same-definition stackable target = **Merged** up to the stack limit with the remainder left in
the source slot, out of range / empty source = **Invalid** (nothing touched), same slot = **Unchanged**. One `Changed`
per operation; no instance created, duplicated or lost. `PlayerInventory.MoveBackpackSlot` exposes it and the view
model's backpack → backpack branch calls it (`LastReorder` diagnostic).

**Exact behaviour:** left click occupied A → pick up; click empty B → A lands in B; click occupied B → A and B swap;
click A again → cancel. Drag A onto empty B → move; onto occupied B → swap; onto itself / from an empty slot → nothing.
Keyboard/controller select → confirm is the same `MoveTo`. Slot order is the player's: nothing compacts or re-sorts,
and the order survives close/reopen, an unrelated move (unequip takes the first free slot and disturbs nothing else),
a runtime refresh and a snapshot round trip (save/load, the run hand-over). Verified with 0 → empty 5, 5 → occupied 2,
drag 0 → 7 then onto 1, keyboard/controller, close/reopen, stack merge with remainder (light ammo, not a round lost),
full stack → swap, different stackable → swap, invalid moves. Built player: `ecdp_08`–`ecdp_11` (before / after click
move / swapped / drag-drop); live run: `live_08`–`live_11` with the slot order printed before and after each step.

## 6. Projectile visual architecture

- **`ProjectileVisual`** (`Combat/Projectiles/ProjectileVisual.cs`): a body `SpriteRenderer` (and a trail renderer)
  parented to the pooled `Projectile` itself. It is therefore exactly where the authoritative body is (`Body.transform
  .localPosition == 0` — asserted while in flight), points where the body points (the projectile already rotates to
  its velocity), is applied in `Projectile.Activate` and cleared in `ReturnToPool`, so it vanishes on the registered
  hit / wall / expiry and never continues past it, and carries no stale frame or trail into the next spawn. Sorting
  layer `Projectiles` (above floor, props and characters, under the above-player wall band). Multi-frame profiles loop
  on `Time.deltaTime`. No separate bullet object exists that could diverge from physics.
- **`ProjectileSpawnData.VisualId`** names the profile (presentation only; `WithVisual` copies a shot); speed, damage,
  range, knockback, stagger, collision, sweep and authority are untouched (asserted per weapon and per enemy attack).
- **`ProjectileVisualCatalog`** (`ScriptableObjects/Presentation/ProjectileVisualCatalog.asset`, referenced by
  `GameContentCatalog.ProjectileVisuals`, activated by `GameApp`): every profile (id, frames, frame time, trail), the
  family default per `WeaponClass`, the player and hostile defaults. `Problems()` is part of `GameContentCatalog.
  Problems()`, so a missing profile fails the content gate.
- **Resolution:** a weapon resolves `WeaponDefinition.ProjectileVisualId` (override) else its class family default
  (`ProjectileVisualCatalog.ResolveWeaponVisualId`), passed through `ProjectileEmitter.Emit` by `RangedWeapon`,
  `BowWeapon`, `BlasterWeapon`, and to the Legendary specials through `SpecialContext.ProjectileVisualId`
  (`PlayerRigComposer`). `EnemyDefinition.ProjectileVisualId` (`EnemyProjectileAttack`) and `EnemyAttackDefinition.
  ProjectileVisualId` (`AttackResolver.FireVolley`) name the hostile profiles; an empty id falls back to the side's
  default so **no hostile shot ever flies invisible**. Without a catalog (bare tests) nothing draws and nothing breaks.
- **Pooling:** the pool adds the component once per pooled instance (prefab or code path); 40 pellets = 40 visuals =
  80 renderers and nothing more (asserted). No per-shot allocation beyond the pool.
- **Art pipeline:** `Editor/ArtGen/ProjectileFactory` draws every profile from `PixelCanvas` + `RuinPalette`;
  `ArtIntegration.GenerateProjectileVisuals` writes frame strips to `Assets/Game/Art/Vfx/vfx_proj_<id>.png` (PPU 32,
  point, uncompressed, no mips, pivot at the head so nothing draws ahead of the physics point) — also part of
  `Generate All Final Art`; `ProjectileArtIntegration.BindCatalog` builds the catalog asset and writes the per-weapon /
  per-archetype / per-attack ids into the definitions by stable id. Manifest category `Projectile visuals` (30 roles,
  `vfx.proj_*`, all INTEGRATED), naming convention placement, provenance entries. No placeholder quads; batch entry
  `ArtIntegration.GenerateProjectileVisualsBatch`.

### Player families and per-weapon mappings

| Family (class default) | Profile | Sprite | Weapons |
|---|---|---|---|
| Pistol | `proj_pistol` | 8×3 small brass bullet, bright head, short ochre tracer, dark edge | P9 Ranger, Kestrel-12; **Quickfang → `proj_pistol_legendary`** (10×3, amber accent) |
| SMG | `proj_smg` | 7×2 thin, fast-looking hot tracer | Rattler-9, Wasp-45; **Buzzsaw → `proj_smg_legendary`** (9×2) |
| Assault Rifle | `proj_rifle` | 10×3 medium steel rifle tracer | AR-17, Marauder A2; **Vanguard → `proj_rifle_legendary`** (12×3) |
| Battle Rifle | `proj_battle_rifle` | 12×3 heavier two-row tracer, brighter core | Hound BR, Sentinel BR; **Judicator → `proj_battle_rifle_legendary`** (14×3) |
| Shotgun | `proj_pellet` | 3×3 pellet, white core, dark corners (one per pellet) | Scatter-8, Breacher-12; **Crowdbreaker → `proj_pellet_legendary`** (amber) |
| Sniper | `proj_sniper` | 16×1 thin high-contrast cyan rail, white head, dark tail | Longshot S1, Needle M7; **Farline → `proj_sniper_legendary`** (20×1, amber) |
| Bow | `proj_arrow` | 14×3 arrow: steel shaft, pale head, olive fletching | Recurve Bow, Compound Bow; **Stormstring → `proj_arrow_legendary`** (amber fletching) |
| Blaster | `proj_energy_bolt` | 8×4 cyan bolt, white core, 2-frame flicker | Pulse Carbine B1, Arc Blaster B4; **Redline → `proj_energy_bolt_legendary`** (amber) |
| Rocket Launcher | `proj_rocket` | 12×5 steel rocket, rust band, amber nose, 2-frame exhaust, plus `proj_rocket_trail` (8×3, 2 frames) behind | Pipe Launcher, Twin-Tube; **Sunbreaker → `proj_rocket_legendary`** |

All 27 ranged weapons resolve a valid profile (asserted); the 9 Legendary-mechanic weapons carry their family's
Legendary variant (its own sprite, asserted). Knife/Spear need none. Every profile keeps a near-white core pixel and a
dark edge/tail pixel, so it reads over the metro concrete, the rust floor and the labs tiles (asserted per sprite; the
10× contact sheet on all three floor colours is `projectile_sprite_contact_sheet_10x.png`), is ≤ 20×7 px, nearest-
neighbour, and pixel-distinct from every other profile (asserted).

### Enemy projectile mappings

| Attack | Profile | Sprite |
|---|---|---|
| Shooter, Summoner rounds | `proj_enemy_round` | 8×3 muted red/orange tracer, bright core, dark edge |
| Sniper enemy | `proj_enemy_sniper` | 14×1 thin red rail |
| Railguard burst cannon / rail sweep (Metro Elite) | `proj_enemy_rail` | 10×3 industrial amber/red bolt |
| Crusher scrap barrage (Rustworks Elite) | `proj_enemy_scrap` | 5×5 rust shard |
| Prototype X-7 energy burst / broad salvo / radial pulse (Labs Elite) | `proj_enemy_energy` | 8×4 sickly-green bolt, 2-frame flicker |
| Scrap King auto burst | `proj_boss_scrap` | 10×4 heavy red/orange round, hot core |
| The Conductor burst cannon / projectile sweep | `proj_boss_arc` | 10×5 amber/red electric arc, 2-frame zigzag |
| Foundry Titan furnace blast | `proj_boss_furnace` | 7×7 molten ember, dark crust, breathing core |
| Subject Omega spore burst | `proj_boss_spore` | 6×6 toxic spore bulb, pale core, 2-frame pulse |
| Aegis Core triple burst / radial ring / ring-while-line | `proj_boss_energy` | 9×5 teal-green lab bolt, white core, 2-frame flicker |

All 14 shipped projectile-motion attacks and all 3 projectile archetypes name a hostile profile (asserted); Boss
attacks carry `proj_boss_*` profiles. The established telegraph colour language (orange normal, red Elite/Boss) is
untouched.

## 7. Readability, sorting, collision, performance

At 640×360 the sprites are 3–20 px long and 1–7 px tall; fast bullets carry a short tracer, slow ones a body/animation;
no bloom; no projectile speed changed for visibility. Sorting layer `Projectiles`. Controlled frame captures (world
frozen a few physics steps after the muzzle; position evidence in the notes) show every family, the pellet spread, the
enemy round and the Boss volley. Pooled reset asserted (rocket → pellet reuse: frame 0, no trail leak, new sprite).

## 8. Proof

`TestResults/EconomyContainmentDeathProjectileProof/`

| # (prompt §14) | Shipped player, windowed (`smoke_windowed/`) | Live run in the Editor (`live_*`, 640×360 + `_x2`) |
|---|---|---|
| 1 corrected ammo sale payout | `ecdp_01_ammo_sell_quote.png` | `live_01_ammo_sell_quote.png` |
| 2 exact coin change after sell | `ecdp_02_ammo_sold_coin_change.png` (`SOLD LIGHT AMMO FOR 17 COINS`, 519 → 536) | `live_02_ammo_sold_exact_coin_change.png` |
| 3 normal enemies contained near the exit | `ecdp_03_enemies_contained_at_open_doorway.png` | `live_03_…` |
| 4 Boss contained at the arena boundary | `ecdp_04_boss_contained_at_arena_boundary.png` | `live_04_…` |
| 5 no loadout → Starter Loadout | `ecdp_05a_no_loadout_before_ready.png`, `ecdp_05b_starter_loadout_equipped.png` | `live_05a/05b` |
| 6 Run Lost screen | `ecdp_06_run_lost_screen.png` | `live_06_run_lost_screen.png` |
| 7 Return to Shelter from it | `ecdp_07_return_to_shelter_after_run_lost.png` | `live_07_…` |
| 8–11 backpack before / click move / swap / drag-drop | `ecdp_08`…`ecdp_11` | `live_08`…`live_11` |
| 12 P9 projectile | `ecdp_12_p9_projectile_visible.png` | `live_12_…` |
| 13 SMG / AR (+ battle rifle) | `ecdp_13a/13b/13c` | `live_13a/13b/13c` |
| 14 shotgun pellets | `ecdp_14_shotgun_pellets_visible.png` | `live_14_…` |
| 15 sniper tracer | `ecdp_15_sniper_tracer_visible.png` | `live_15_…` |
| 16 bow / blaster / rocket (+ Legendary Quickfang) | `ecdp_16a/16b/16c/16d` | `live_16a/16b/16c/16d` |
| 17 enemy projectile | `ecdp_17_enemy_projectile_visible.png` | `live_17_…` |
| 18 Boss projectile | `ecdp_18_boss_projectile_visible.png` | `live_18_…` |

Also there: `live_economy_containment_death_projectile_evidence.txt` (every quote, coin change, closest approach /
overshoot / correction count, slot order per step, projectile world position → capture pixel, run-lost rows, starter
notice), `projectile_sprite_contact_sheet_10x.png`, `smoke_headless/` (the headless run's captures) and, in
`TestResults/`, `smoke_mac_headless/smoke_result.json`, `smoke_mac_windowed/smoke_result.json` (each with
`AmmoSaleEvidence` and `ProjectilePositions`), `build_report_macos.md`, `build_macos.log`. Nothing was drawn into a
capture by hand; every projectile frame is the real pooled sprite at the real body position.

## 9. Tests

**New — EditMode (35).** `AmmoResaleEconomyTests` (23 cases): the 15 % rule; full bundles Light/Medium/Heavy/Shells;
partial and full stacks incl. tiny quantities → 0; monotonic, never negative, bounded by the purchase value, never the
old per-round payout, `int.MaxValue` safe; no rarity multiplier; Merchant quote == payout == `Sold` event, once,
cannot oversell, retry pays nothing; a 0 quote is `NoValue`, not a sale; buy → immediate sell loses ≥ 85 %; the Shelter
Trader uses the same rule; consumables keep 35 % per unit. `StarterLoadoutFallbackTests` (12): empty loadout, empty
equipment with backpack items, valid loadout preserved (fingerprint), secondary-only counts, armor-only, idempotent,
unresolved reference quarantined then starter, Ready with gear in storage (Transit path), the terminal's READY path
(notice shown, no second kit), Start with a valid loadout, never during a run + wipe recovery, reopening the Shelter
grants nothing.

**New — PlayMode (42).** `EncounterContainmentTests` (14): bounds math, rebinding, room interior vs door cells in every
biome, melee pursuit at every exit direction in every biome, ranged advance/back-off, Charger dash, Elite, Boss pursuit
in every biome (pursuit-only), Boss dash, knockback without wall bonus, event-wave ownership, composed Boss bound and
idle until entry, pending lock, replicated position. `RunFailedScreenTests` (8): content from the real `Fail()`
summary, success ignored / never twice, input hold + pause + pointer + focus, mouse, keyboard/controller, dispose,
solo death once, co-op downed no screen / wipe once. `BackpackReorderTests` (8). `ProjectileVisualTests` (11): catalog
complete, every ranged weapon resolves by family with Legendary overrides, every hostile attack resolves, sprites
distinct/readable/crisp/pivoted, follow/rotate/end-on-wall-hit, pooled reuse, fallbacks, allocation, the real
emitters for P9/SMG/AR/BR/shotgun/sniper/rocket/Legendary, bow + blaster (+ Legendary Redline), enemy shooter /
Elite energy / Boss spore. `EconomyContainmentDeathProjectileProofTests` (1): the live-run proof above.

**Updated.** `HeldWeaponVisualTests`, `PlayerVisibilityTests`, `RemotePlayerVisualTests` exclude the pooled projectile
renderers from their body/weapon renderer counts. `CombatAimCollisionProofTests` (pre-existing): its frozen dummy is
now also placed clear of hazard pools and the direct-shot step logs the line of fire — see §12.4.

**Smoke (built player).** `SmokeRunner.EconomyContainment.cs` (46 checks), `SmokeRunner.StarterDeath.cs` (7 + 9
checks); the second expedition of the smoke now starts with everything stripped into Storage and ends by death → Run
Lost → RETURN TO SHELTER, the third leaves through the pause menu as before. The combat stage's "through a shut door"
probe stands its dummy's bounds down while it is deliberately parked outside its room.

## 10. Build and smoke

- **Windows x64: NOT RUN.** `ls /Applications/Unity/Hub/Editor/6000.3.24f1/PlaybackEngines` → `MacStandaloneSupport`
  only; `BuildTarget.StandaloneWindows64` cannot be produced on this machine. `ReleaseBuildTool.BuildBatch` is
  unchanged and remains the release entry.
- **macOS verification build:** `ReleaseBuildTool.BuildMacBatch` (new; same `SceneOrder`, `BuildOptions.None`, output
  `Builds/MacOS/RUINRAIL.app`, report `TestResults/build_report_macos.md` — explicitly labelled a verification
  substitute, not a platform commitment). Succeeded, 0 errors, 2 pre-existing environment warnings.
- **Headless smoke** (seed 31) and **windowed smoke** (seed 11, 1280×720): both `Success: true`, all stages through
  `ReturnToMenuOk`, every requirement of §16 checked inside the shipped player — ammo resale sane (§1), encounter
  enemies stay in their room and the Boss in its arena (§2), Starter Loadout auto-equips only with no valid loadout
  (§3), Death screen works (§4), backpack slots reorder/swap and the order persists (§5), player and enemy projectiles
  visible, family-appropriate, ending on actual hits (§6), 0 exceptions / missing scripts / missing references.

## 11. Files

**Runtime — new:** `Combat/EncounterBounds.cs`, `Combat/Projectiles/ProjectileVisual.cs`,
`Combat/Projectiles/ProjectileVisualCatalog.cs`, `UI/RunEnd/RunFailedViewModel.cs`, `App/RunFailedScreen.cs`,
`App/SmokeRunner.EconomyContainment.cs`, `App/SmokeRunner.StarterDeath.cs`.
**Runtime — changed:** `Economy/EconomyConfig.cs`, `Economy/PriceService.cs`, `Base/TraderService.cs`,
`Loot/DungeonMerchantService.cs`, `Base/StarterKitService.cs`, `UI/Base/BaseSession.cs`, `UI/Base/BaseHubViewModel.cs`,
`UI/Base/StationPresentation.cs`, `UI/Multiplayer/MultiplayerUiViewModels.cs`, `App/BaseHubScreen.cs`,
`Enemies/EnemyController.cs`, `Enemies/Attacks/MovesetActorController.cs`, `Enemies/Attacks/AttackResolver.cs`,
`Combat/Impact/ImpactReceiver.cs`, `Dungeon/Runtime/RoomRuntime.cs`, `Dungeon/Runtime/EliteEngagement.cs`,
`Dungeon/Runtime/RoomCategoryComposer.cs`, `App/ExpeditionScene.cs`, `UI/Navigation/ScreenNavigation.cs`,
`UI/Pause/PauseMenuViewModel.cs`, `Items/ItemSlotContainer.cs`, `Items/PlayerInventory.cs`,
`UI/Inventory/InventoryViewModel.cs`, `Items/WeaponDefinition.cs`, `Enemies/EnemyDefinition.cs`,
`Enemies/Attacks/EnemyAttackDefinition.cs`, `Enemies/EnemyProjectileAttack.cs`, `Combat/Projectiles/Projectile.cs`,
`Combat/Projectiles/ProjectilePool.cs`, `Combat/Projectiles/ProjectileSpawnData.cs`, `Combat/Weapons/ProjectileEmitter.cs`,
`Combat/Weapons/RangedWeapon.cs`, `Combat/Weapons/BowWeapon.cs`, `Combat/Weapons/BlasterWeapon.cs`,
`Combat/Weapons/Specials/ILegendarySpecial.cs`, `Combat/Weapons/Specials/SpecialPrimitives.cs`, `App/PlayerRigComposer.cs`,
`App/GameApp.cs`, `App/GameContentCatalog.cs`, `App/SmokeRunner.cs`.
**Editor — new:** `Editor/ArtGen/ProjectileFactory.cs`, `Editor/ArtGen/ProjectileArtIntegration.cs`.
**Editor — changed:** `Editor/ArtGen/ArtIntegration.cs`, `Editor/ArtGen/ProvenanceRecorder.cs`,
`Editor/Production/AssetNamingConvention.cs`, `Editor/Production/CompletionAssetManifest.cs`,
`Editor/Production/GameContentCatalogBuilder.cs`, `Editor/Production/ReleaseBuildTool.cs`.
**Content:** `Assets/Game/Art/Vfx/vfx_proj_*.png` (30 strips), `ScriptableObjects/Presentation/ProjectileVisualCatalog.asset`
(new), `Resources/GameContentCatalog.asset` (projectile catalog bound), 9 Legendary weapon assets + 3 enemy
definitions + 14 attack definitions (profile ids), `production/COMPLETION_ASSET_MANIFEST.md`, `production/asset_provenance.json`.
**Docs:** `base/77_ECONOMY.md`, `base/75_STARTER_KIT.md`, `ui/92_INVENTORY_UI.md`, `ui/90_UI_UX_OVERVIEW.md`,
`combat/43_ENEMY_FRAMEWORK.md`, `combat/46_BOSSES.md`, `art/104_VFX_GAME_FEEL.md`.
**Tests — new:** `EditMode/AmmoResaleEconomyTests.cs`, `EditMode/StarterLoadoutFallbackTests.cs`,
`PlayMode/EncounterContainmentTests.cs`, `PlayMode/RunFailedScreenTests.cs`, `PlayMode/BackpackReorderTests.cs`,
`PlayMode/ProjectileVisualTests.cs`, `PlayMode/EconomyContainmentDeathProjectileProofTests.cs`.
**Tests — updated:** `PlayMode/HeldWeaponVisualTests.cs`, `PlayMode/PlayerVisibilityTests.cs`,
`PlayMode/RemotePlayerVisualTests.cs`, `PlayMode/CombatAimCollisionProofTests.cs`.

Not part of this pass (pre-existing local modifications on this machine, left as found): `ProjectSettings/
PackageManagerSettings.asset`, the executable bit on `scripts/run-unity-tests.sh`, `RUINRAIL.slnx`.
`production/FINAL_MVP_COMPLETION_REPORT.md` is regenerated by the EditMode suite with this machine's NOT RUN lines for
the Windows build; it was restored to the checked-in Windows-based version after each run.

## 12. Remaining genuine limitations

1. **No Windows x64 build on this machine** — the only reason for the incomplete status. The macOS build of the
   identical scenes and options passed both smokes with every §16 requirement checked in the shipped player; run
   `ReleaseBuildTool.BuildBatch` and the two smokes on the Windows machine to close §16 as written.
2. **Fallback kit consumables.** The Starter Loadout fallback grants the kit as base/75 defines it, so the Bandage and
   60 Light Ammo (ordinary, sellable: ~29 Coins) are minted each time a player reaches Ready with no weapon equipped.
   The equipment copies are unsellable, Storage is never written, and it takes deliberately stripping every weapon
   before each run — the same residual the existing no-weapon-anywhere rescue grant already had.
3. **Containment tolerance.** In a crowded pack the physics solver can push a body up to ~1 px (0.034 tiles measured)
   past the legal edge before the next step's clamp pulls it back; the tests and smoke allow 0.06 tiles (2 px). No
   collider ever reached a door cell.
4. **`CombatAimCollisionProofTests.LiveRun_DirectHit_…` (pre-existing) is timing/geometry sensitive on this machine:**
   it failed in 4 of 7 full-suite runs here — including a run of the **untouched baseline tree** (git stash, 678/679)
   — and passed in every filtered run. Two causes were found and addressed in the test only: its frozen dummy could be
   placed inside a hazard pool (whose ticks changed the HP deltas it measures), and the direct-shot step now logs the
   line of fire. The final two full-suite sequences passed 721/721.
5. Live Relay / UGS Sessions are not claimed; the deterministic harness was used, as in previous passes.
6. The windowed captures are the shipped player's real 1280×720 back buffer; the live-run captures are point-sampled
   640×360 reference frames (+ `_x2`).

---

`ECONOMY_CONTAINMENT_DEATH_PROJECTILES_INCOMPLETE`
