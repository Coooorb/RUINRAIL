# RUINRAIL — Post-Polish Regression Fix Report

> **Scope:** three live-dungeon regressions reported after the UI/biome polish pass — the invisible player, an open
> room exit onto the void, and the overlapping lower-left HUD. Root causes fixed; nothing in gameplay design,
> characters, weapons or VFX was redesigned.
> **Terminal status:** `REGRESSION_FIX_COMPLETE`

| Gate | Result |
|---|---|
| EditMode (`./scripts/run-unity-tests.ps1 -TestPlatform EditMode`) | **PASS — 758 passed / 759 discovered, 0 failed** (1 ignored, pre-existing) |
| PlayMode (`./scripts/run-unity-tests.ps1 -TestPlatform PlayMode`) | **PASS — 534 passed / 534 discovered, 0 failed** |
| Release build (`Unity.exe -batchmode -nographics -quit -executeMethod RuinRail.EditorTools.Production.ReleaseBuildTool.BuildBatch`) | **Succeeded** — 0 errors, 1 warning (Unity Services project-ID notice, pre-existing), 157.2 MB, 9 s |
| Built-player smoke, headless (`RUINRAIL.exe -batchmode -nographics -smoke -savedir <tmp>`) | **Success** — `MainMenu;Base;Dungeon;Base;`, 10 rooms, Overgrown Labs, save reloaded, exit 0 |
| Built-player smoke, windowed with screenshot (`RUINRAIL.exe -smoke -savedir <tmp> -screenshot TestResults/RegressionProof/built_player_dungeon.png -screen-fullscreen 0`) | **Success** — 10 rooms, Rustworks, save reloaded, screenshot written, exit 0 |

Unity `6000.3.24f1`; no pinned package or Editor version changed. Each harness run was executed exactly as listed;
the counts above are read from `TestResults/EditMode-results.xml` and `TestResults/PlayMode-results.xml` of the final
runs, after the last source change. (On this machine the first Unity launch after a C# edit can die on the Smart App
Control block described in `UI_AND_BIOME_POLISH_REPORT.md` §1; in this session every launch compiled first time.)

Baseline before this pass: EditMode 751/752, PlayMode 526/526. Net new: +7 EditMode, +8 PlayMode tests.

---

## 1. Reproduction first

The reported symptoms were reproduced from a live run before anything was changed. A new PlayMode helper
(`Tests/PlayMode/LiveDungeonCapture.cs`) boots the real `GameApp`, plays Main Menu → Shelter → Transit → Dungeon and
renders the real camera-rig framing plus the real HUD canvases into a 640×360 render texture.

`TestResults/RegressionProof/before_start_room.png` (and `_x2`) is that frame: no player anywhere near the camera
target, `HP` and `DASH READY` drawn through each other at the bottom-left in the blurred builtin TTF, and the start
room's top doorway open onto black.

A note on the capture tool itself, because it produced a misleading first frame: a Screen Space **Overlay** canvas
silently drops its sorting layer, so when the helper switches it to Screen Space Camera it lands on `Default`, which
the 2D renderer draws *underneath every world layer*. The helper now pins captured canvases to `ScreenUI` for the
duration and restores the layer afterwards. This was a capture artefact only — the shipped game runs overlay canvases,
which always draw on top.

---

## 2. Priority 1 — the player was never given a body

### Root cause

`PlayerEntityBuilder` composes movement, aiming, dash, health, stats, loot and life state — and no renderer.
`ExpeditionScene` mounted weapons, camera, HUD, VFX and audio on that object and added a `WeaponVisualDriver`, but
never a `SpriteRenderer`, a `SpriteAnimator` or a `PlayerAnimationDriver`. The animation architecture from TASK 138
(driver → animator → renderer) existed and was unit-tested, and the final player sheet from TASK 153/166 was sliced
into `player_animation_set.asset` with all 48 clips — but nothing on the release path ever bound the two together, and
the `CharacterAnimationSet` assets were not referenced by `GameContentCatalog`, so a built player could not even load
them. The player was a collider with a camera following it. The same was true of every enemy, Elite and Boss.

There was no tint, alpha, sorting or animation fault to fix on the renderer, because there was no renderer.

### Fix

| File | Change |
|---|---|
| `Presentation/Animation/CharacterVisual.cs` (new) | The one runtime seam for a character body: a `Body` child with a `SpriteRenderer` (white, enabled, `Characters` sorting layer, y-sorted at the feet through the existing `SpriteSorting`/`YSorter`) and a `SpriteAnimator` bound to the actor's `CharacterAnimationSet`. Idempotent — a second call returns the existing body rather than stacking a renderer. |
| `App/GameContentCatalog.cs` | `AnimationSets` list + `AnimationSetFor(actorId)`; `Problems()` now reports a missing `player` set so an incomplete catalog fails the boot-flow test instead of shipping an invisible player. |
| `Editor/Production/GameContentCatalogBuilder.cs` | Collects every `CharacterAnimationSet`; catalog asset rebuilt (`Assets/Game/Resources/GameContentCatalog.asset`, 22 sets). |
| `App/ExpeditionScene.cs` | Player: `CharacterVisual.Attach` + `PlayerAnimationDriver.Configure(...)` right after the rig is built. Regular enemies: the existing `OnEnemySpawned` hook already added an `EnemyAnimationDriver` that had nothing to draw into — it now gets a body first, and `HitFlash` is handed the body renderer it previously searched for and never found. Boss: bound at depth build from `RoomContentBinding.Boss`. Elite: bound when it spawns. |
| `Dungeon/Runtime/EliteEngagement.cs` | `Spawned` event (the Elite actor exists) so presentation can bind its body; gameplay never waits on it. |

Actor ids match definition ids (`player`, `grunt`, `elite_tunnel_stalker`, `boss_the_conductor`, …), so no mapping
table was invented.

### Verification (all asserted, not eyeballed)

`PlayerVisibilityTests` (PlayMode, 3): the catalog carries the player set with zero missing clip roles across all 8
facings and reports no problems; a player built through `PlayerEntityBuilder` + `CharacterVisual` has an enabled
`SpriteRenderer` on `Characters` above `LowProps`, white/opaque, y-sorted; the first driver tick assigns a sprite; every
one of the 6 states × 8 facings the driver can request resolves to an authored frame (never a placeholder hold, never a
blank); attaching twice never stacks a second renderer; a missing set still yields an enabled renderer and the explicit
placeholder path rather than an exception.

`DungeonRegressionProofTests` (PlayMode, live run): the player spawns in the start room; the visual exists, is
enabled and active; sprite assigned; alpha 1, no tint; `Characters` layer, ordered above `GroundDetails`; the driver's
animator is the body's and is not on its placeholder path. Then the proof: the frame is captured with the body
renderer enabled and again with it disabled, and **the 40×56 px region at the player's feet differs by more than 150
pixels** between the two — the body is what is drawn there.

Screenshots: `TestResults/RegressionProof/live_start_room.png` (player centre-frame), `live_start_room_player_hidden.png`
(the control frame), and `built_player_dungeon.png` — **a frame captured by the shipped `RUINRAIL.exe` itself** during
the smoke run (new optional `-screenshot <path>` argument on `SmokeRunner`; it needs a graphics device, so the headless
smoke reports `Screenshot: ""` and the windowed run reports the path).

---

## 3. Priority 2 — spare door sockets were open floor onto the void

### Root cause

Every handmade room except the Boss arenas is authored with 3–4 door sockets (`D` cells, painted as floor) so the
assembler can attach it from any side. A placed room only uses as many sockets as the graph gives it neighbours — a
branch leaf uses one of its four. `DungeonAssembler` already prefers rooms with the fewest spare sockets and
`DungeonLayoutValidator` already rejects *unintended* socket contacts, but nothing addressed the *intended* spares:
each one was left as an open doorway on the room's edge with nothing beyond it. That is the reported room.

Requiring socket count == graph degree is not possible with the approved pool (leaves need 1-socket rooms and only the
Boss rooms have one), and redesigning the 63 rooms is not this task. The approved rule is therefore enforced where the
dungeon is realised: a spare socket is not an exit.

### Fix

| File | Change |
|---|---|
| `Dungeon/Generation/RoomExitSealer.cs` (new) | Seals a socket by painting the room's **own** wall tile — the one flanking the door run on the same edge — onto exactly the socket's cells on the room's **Walls** tilemap, then `TilemapCollider2D.ProcessTilemapChanges()` so the composite collider blocks immediately. Same tile, same layer, same collider as the surrounding wall: sealed means not drawn open, not walkable, not shootable through. No freehand geometry; exact grid cells. |
| `Dungeon/Generation/DungeonLayoutInstantiator.cs` | After each prefab is placed, `SealUnconnected` walls every socket the layout has no connection for. |
| `Dungeon/Generation/DungeonExitValidator.cs` (new) | Scene-level validation of the instantiated dungeon: for every socket of every placed room, **either** it is connected — the partner instance exists, carries the reciprocal (opposite-direction, equal-width) socket, the two door runs are adjacent cell-for-cell in world space, and both doorways are open — **or** every cell of it is wall. Anything else is reported as `void exposure` / `walled on one side` / not adjacent. |
| `Dungeon/Generation/DungeonLayoutValidator.cs` | Reciprocity added at layout level: a connection's two sockets must face each other, and no socket may be claimed by more than one connection. |
| `Dungeon/Generation/DungeonGenerationPipeline.cs` | `firstRound` parameter so a caller that rejects a round after instantiation continues from the next deterministic round (round *n* is a pure function of seed, depth and *n*, exactly as before). |
| `App/ExpeditionScene.cs` | `BuildDepth` now generates → instantiates → runs `DungeonExitValidator`; a failing round is logged, destroyed and **regenerated from the next round**, never patched. If no round within the pipeline's limit passes, the expedition fails exactly as a generation failure did before. The accepted round's (empty) problem list is exposed as `ExitProblems`. |

### Verification

`DungeonExitValidationTests` (EditMode, 7), against the shipped pools loaded from the release catalog:

- **135 generated dungeons** (3 biomes × 3 depths × 15 seeds) instantiate with zero exit problems; every connected
  socket is open on both sides and every spare socket is walled (the sweep sealed sockets, asserted > 0).
- Sealing walls exactly the socket cells (tile count grows by the socket width and nothing else), with the flanking
  wall tile, `ColliderType.Grid`, and leaves the room's other sockets open.
- A dungeon instantiated **without** sealing is rejected by the validator with only `void exposure` problems, and the
  same dungeon passes once sealed.
- A connected doorway walled on one side is rejected (`walled on one side`).
- The pipeline resumes from round *n*+1 deterministically (two calls agree), and the reroll differs from the rejected
  round.
- Layout validator rejects a connection whose sockets do not face each other and a socket claimed twice.

`DungeonRegressionProofTests` (PlayMode, live run): `run.ExitProblems` is empty and the validator re-run on the live
scene agrees; for every spare socket the room reports it sealed, and its cells sample as drawn (non-black) in a
zoomed-out overview capture of the whole layout; two sealed doorways are captured close-up at 1:1 and the door cells
are under 20 % black. Final run: **13 spare sockets sealed across 9 rooms, 8 reciprocal connections.**

Screenshots: `live_dungeon_overview.png` (whole depth, every outer edge closed), `live_sealed_exit_1.png` and
`live_sealed_exit_2.png` (former doorways, now wall), plus the start-room frames above.

---

## 4. Priority 3 — HP text and DASH text were given the same rectangle

### Root cause

In `DungeonHudView.Build`, inside the 30 px HP panel: the HP text was anchored top-left at `y = −20` with a 10 px box,
i.e. occupying the panel's bottom 10 px — and the Dash text was anchored bottom-left at `y = 0` with a 10 px box, i.e.
the **same** bottom 10 px. Two labels, one band. The bar sat above them and the top 10 px of the panel was empty. The
overlap was in the geometry, not in any one screenshot, so it reproduced at every resolution.

Two things compounded it: the HUD still drew with `Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")` at size 8
(blurry at 640×360, and the font `FINAL_ART_PRODUCTION_SPEC` 29.12 requires gone from the release path — flagged as a
follow-up in the polish report), and its labels used `VerticalWrapMode.Overflow`, so a line could spill out of its box.

### Fix

| File | Change |
|---|---|
| `UI/Theme/UiFont.cs` (new) | The pixel-font loader, fallback and ascent nudge moved down from `App/UiKit` into the UI assembly so the in-run HUD can use the same face and metrics as the front-end. |
| `App/UiKit.cs` | `Font()`, `UsingPixelFont`, `TopOffset`, `PixelFontResource` now delegate to `UiFont` — public API unchanged, no second implementation. |
| `UI/Hud/DungeonHudView.cs` | Draws with `UiFont.Font()` at the face's authored cap height (7), `lineSpacing = 1`, `Truncate` vertically, the same first-line nudge the front-end uses. Every stacked block is laid out as exclusive bands at one line pitch (10 px = 9 px line box + 1): HP panel = bar (8) → HP text (9) → Dash text (9), 27 px tall; weapons, consumable, top-left, party and coins likewise. Panels stay corner-anchored with integer offsets from the 640×360 reference; nothing depends on the window size. |

### Verification

`DungeonHudLayoutRegressionTests` (PlayMode, 4): the bar, HP text and Dash text occupy separate bands in reading order
(bar above HP above Dash), each text band a full line box, the block sitting on the 6 px bottom/left margins; every
HUD `Text` uses the project pixel face at its authored size with unit line spacing and vertical truncation; no two HUD
lines share pixels and all stay inside 640×360; every line is built exactly once (no duplicate layers); the longest
approved HP (`HP 999 / 999  [SHIELD]`) and Dash (`DASH 100%`) strings fit their band widths. The existing
`DungeonHudTests` (panel-level layout, live weapon/resource display, co-op lines) still pass unchanged.

`DungeonRegressionProofTests` (PlayMode, live run): exactly one `DungeonHudView` in the scene; exactly two `Text`
objects under the HP panel; both on `UiFont.Font()` at size 7; the two reference rects do not overlap; and off the
captured frame each band contains drawn text pixels, the bar band is more than half red fill, and the bar overlaps
neither text band.

At 640×360 the bottom-left now reads, top to bottom: red HP bar / `100 / 120` / `DASH READY` — see
`live_start_room.png` and the shipped-build `built_player_dungeon.png`.

---

## 5. Files changed

Runtime / editor:

- `Assets/Game/Scripts/Presentation/Animation/CharacterVisual.cs` — new
- `Assets/Game/Scripts/App/GameContentCatalog.cs`, `Assets/Game/Scripts/Editor/Production/GameContentCatalogBuilder.cs`
- `Assets/Game/Resources/GameContentCatalog.asset` — rebuilt (now references the 22 `CharacterAnimationSet`s)
- `Assets/Game/Scripts/App/ExpeditionScene.cs`
- `Assets/Game/Scripts/Dungeon/Runtime/EliteEngagement.cs`
- `Assets/Game/Scripts/Dungeon/Generation/RoomExitSealer.cs` — new
- `Assets/Game/Scripts/Dungeon/Generation/DungeonExitValidator.cs` — new
- `Assets/Game/Scripts/Dungeon/Generation/DungeonLayoutInstantiator.cs`, `DungeonLayoutValidator.cs`, `DungeonGenerationPipeline.cs`
- `Assets/Game/Scripts/UI/Theme/UiFont.cs` — new
- `Assets/Game/Scripts/App/UiKit.cs`
- `Assets/Game/Scripts/UI/Hud/DungeonHudView.cs`
- `Assets/Game/Scripts/App/SmokeRunner.cs` — optional `-screenshot <path>`

Tests:

- `Assets/Game/Tests/EditMode/DungeonExitValidationTests.cs` — new (7)
- `Assets/Game/Tests/PlayMode/LiveDungeonCapture.cs` — new helper
- `Assets/Game/Tests/PlayMode/DungeonRegressionProofTests.cs` — new (1, live run + captures)
- `Assets/Game/Tests/PlayMode/PlayerVisibilityTests.cs` — new (3)
- `Assets/Game/Tests/PlayMode/DungeonHudLayoutRegressionTests.cs` — new (4)

Generated `.meta` files for the new sources. Unity also re-serialised `Assets/Settings/UniversalRP.asset`,
`UniversalRenderPipelineGlobalSettings.asset` and `Assets/DefaultNetworkPrefabs.asset` as a side effect of the build /
test runs; no deliberate change was made to them.

Proof captures: `TestResults/RegressionProof/` — `before_start_room`, `live_start_room`,
`live_start_room_player_hidden`, `live_dungeon_overview`, `live_sealed_exit_1`, `live_sealed_exit_2` (each with an
`_x2` nearest-neighbour copy) and `built_player_dungeon.png` from the shipped executable. Smoke result:
`TestResults/smoke_result.json`. Logs: `TestResults/regression-build.log`, `regression-smoke.log`,
`regression-smoke-visual.log`.

---

## 6. Known limitations and follow-ups (unchanged design, outside this task)

- **Weapon sprites are still not bound at runtime.** `WeaponVisualDriver` computes recoil/swing for a weapon sprite
  transform that nothing composes; the player body is drawn, the held weapon is not. Untouched here because the task
  forbids weapon redesign and the fix is a separate binding of the weapon sprite catalog.
- **Co-op remote replicas** (`LocalPlayerEntityFactory` / `NgoPlayerPresence`) build players through
  `PlayerEntityBuilder` and would need the same `CharacterVisual.Attach` call; that path is not composed by the solo
  `ExpeditionScene` and was not exercised by the reported regression.
- **`InventoryView` still uses `LegacyRuntime.ttf`** (the polish report's other flagged follow-up). Not part of the
  reported HUD regression; left for its own pass.
- `ReleaseBuildTool` reports the build at **157.2 MB** (was 115.1 MB): the 22 character sheets now ship because the
  catalog references their animation sets. That is the cost of characters being visible.
- The PlayMode proof fixture leaves the Dungeon scene loaded after it finishes, so its `TearDown` clears every scene
  root except the test runner; without that, later physics tests collided with the leftover room geometry (192
  failures in the first full run, all gone once the fixture cleaned up after itself).

---

## 7. Terminal status

`REGRESSION_FIX_COMPLETE`
