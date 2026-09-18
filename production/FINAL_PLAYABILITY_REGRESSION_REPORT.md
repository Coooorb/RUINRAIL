# RUINRAIL — Final Playability / UX Regression Report

> **Scope:** the seven repository-local playability issues of `RUINRAIL_FINAL_PLAYABILITY_REGRESSION_PROMPT.md`,
> executed from the state after `POST_POLISH_REGRESSION_FIX_REPORT.md`. No character, enemy, weapon, VFX, biome or
> audio art was redesigned; no weapon/enemy stat, save semantic, networking authority rule or stable id changed.
> **Terminal status:** `FINAL_PLAYABILITY_PASS_COMPLETE`

| Gate | Result |
|---|---|
| `./scripts/run-unity-tests.ps1 -TestPlatform EditMode` | **PASS — 774 passed / 775 discovered, 0 failed** (1 skipped: the pre-existing `RUINRAIL_LIVE_SERVICES=1` live-Sessions check) |
| `./scripts/run-unity-tests.ps1 -TestPlatform PlayMode` | **PASS — 565 passed / 565 discovered, 0 failed** |
| `ContentCountValidator` | PASS — 52/52 exact |
| `FinalProductionValidator` | PASS — 9/9 sections, 0 errors, 0 external blockers |
| `PresentationValidator` | PASS — 8 checks |
| `AnimationAssetAudit` | COMPLETE — 22/22 sets, 33/33 weapon sprites |
| `ArtProductionContract` | PASS — 10 checks, 288 sprites (hardware cursors validated as Cursor textures) |
| `AssetPipelineValidator` | PASS — 6 checks |
| `ReleasePathVisualScan` | CLEAN — 0 placeholder, 0 missing |
| Save / exploit hardening | `ExploitHardeningTests` 6/6, `PersistenceHardeningTests` 4/4 inside the suites above |
| Release build (`ReleaseBuildTool.BuildBatch`, Windows x64, non-development) | **Succeeded** — 0 errors, 1 warning (the external UGS project-ID notice), 157.4 MB |
| Built-player smoke, headless (`RUINRAIL.exe -batchmode -nographics -smoke -savedir <tmp>`) | **Success** — 15/15 playability checks, `ReturnToMenuOk: true`, 0 exceptions, 0 missing scripts/references |
| Built-player smoke, windowed + screenshot | **Success** — same checks, `13_built_player_combat_room.png` captured by the shipped exe |

Every gate above ran **after the last source change**. Unity `6000.3.24f1`; no pinned package or Editor version
changed. The counts are read from the final `TestResults/EditMode-results.xml` / `PlayMode-results.xml`. Baseline
before this pass: EditMode 758/759, PlayMode 534/534 → **+16 EditMode, +31 PlayMode tests**.

Smart App Control note: every Unity batch launch in this pass compiled and ran at the first attempt; the documented
double-launch was not needed. Smart App Control was not touched.

---

## 1. Held weapon runtime visuals

**Root cause.** `WeaponVisualDriver`, `PlayerAiming.SetAimPivot` and the weapons' `SetMuzzle` seams all existed, but
nothing on the runtime path created a pivot, a weapon sprite or a muzzle, and the 33 generated `Art/Weapons/*.png`
sprites were referenced by no runtime asset — a built player could not even load them.

**Fix.**
- `GameContentCatalog.WeaponSprites` + `WeaponSpriteFor(id)` (keyed by the sprite's name == weapon stable id);
  `Problems()` reports any weapon without a held sprite. Catalog rebuilt: 33/33 bound.
- `Presentation/Animation/HeldWeaponVisual.cs` (new): one `WeaponPivot` child at grip height, rotated 360° by the
  existing `PlayerAiming` (the driver never quantises aim), one `WeaponSprite` under it fed by the driver's recoil/swing,
  one `Muzzle` at the sprite's `bounds.max.x` (the authored +X firing end) handed to every mounted
  Ranged/Blaster/Bow weapon through their existing `SetMuzzle` — so a projectile now leaves the visible barrel. The
  active slot's sprite is the only weapon renderer; slot changes swap it synchronously through
  `WeaponLoadout.ActiveSlotChanged`, and a remount (loadout change) rebinds sprite and muzzles. Left-facing aim flips
  the sprite on Y so the top stays up. Sorting: Weapons layer over the body, except when the aim points away from the
  camera (`aim.y > 0.35`: N/NE/NW), where it draws on the Characters layer one order behind the body — never below
  floor/props, never above AbovePlayer.
- `Combat/Weapons/IHeldWeaponView.cs` (new): the presentation seam a replica without a loadout is fed through.
- `App/PlayerVisualComposer.cs` (new): the **one** composition of a player's presentation (body + animation driver +
  weapon driver + held weapon), idempotent. `ExpeditionScene` now uses it for the local player.

**Tests.** `HeldWeaponBindingTests` (EditMode, 2): all 33 definitions have a sprite keyed by id, grip pivot on the
left half, muzzle end at +X, point-filtered, PPU 32; the behind-body rule. `HeldWeaponVisualTests` (PlayMode, 4):
active weapon shown, swap/Weapon1/Weapon2 change the sprite immediately, exactly one weapon renderer, recomposing
never stacks; empty hands draw nothing; muzzle at the firing end and **the fired projectile spawns at the muzzle**;
16 aim angles through 360°: pivot angle, flip, behind/in-front rule, layer bounds.

Proof: `01_local_player_held_p9_ranger.png` (+ the control frame `01_control_weapon_hidden.png`, differing by >20 px at
the hands), `13_built_player_combat_room.png` from the shipped exe.

## 2. Remote co-op character + weapon visuals

**Root cause.** `LocalPlayerEntityFactory` (the presence harness) and the NGO `PlayerNetworkEntity` prefab both build
players through `PlayerEntityBuilder`, which has no presentation; only the solo `ExpeditionScene` composed visuals.
Replicas also had no way to know which weapon to draw (not in `WeaponNetState`) and never applied the replicated aim.

**Fix (same seam, no second implementation).**
- `LocalPlayerEntityFactory` takes an optional `decorate` hook; the composition root passes
  `PlayerVisualComposer.Compose`.
- `NetworkPlayerObject.VisualComposer` (static hook, `ComposeVisuals()` on `OnNetworkSpawn`): `GameApp` registers the
  same composer at boot, so every replicated player object on every peer (host, owner, remote replica) composes the
  identical body + held weapon. Owner/non-owner input isolation is untouched.
- `WeaponNetState.ActiveWeaponId` (presentation-only field) captured by the host; `NetworkPlayerCombat` feeds a pure
  replica's `IHeldWeaponView` with it.
- `PlayerAiming.ApplyReplicatedAim` (ignored for readers with real input); `NetworkPlayerMotion.InterpolateReplica`
  applies the replicated aim so a replica's facing and weapon pivot follow the real owner.

**Tests.** `RemotePlayerVisualTests` (PlayMode, 5, fake-transport harness): host sees every member's body (local +
2 remotes) composed once with no duplicate renderer stack, despawn removes the visuals; reconnect under the grace keeps
the same entity and renderer; a client-side replica built from the release NGO prefab composes through the hook (twice,
still once), shows the replicated weapon id, follows a slot change, empties on an empty id, and turns body + pivot from
replicated aim; replicated aim never overrides an owner; `WeaponNetState` carries the active weapon id.

**Not claimed:** live UGS Relay. No project link exists on this machine; the coverage is the deterministic harness.

Proof: `02_remote_replica_body_and_weapon.png` (local aiming right, replica aiming left with its own pistol).

## 3. Inventory final font cleanup

**Root cause.** `InventoryView.Build` loaded `LegacyRuntime.ttf` directly; its boxes were sized for that face.

**Fix.** `InventoryView` draws through `UiFont.Font()` at the authored size with unit line spacing; every box is whole
lines tall and truncates vertically; backpack cells and the tooltip wrap; equipment rows became two lines (slot name /
item fitted to the 170 px column with the front-end's ellipsis rule) so a long name never reaches the backpack grid.
While checking the tooltips, the pixel face turned out to lack the en dash the damage ranges use (`12–14` rendered as
`1214`): the glyph was added to `PixelFontFactory` and the font rebuilt (91 glyphs). No release script other than the
`UiFont` fallback names the builtin font any more.

**Tests.** `InventoryFontRegressionTests` (PlayMode, 3): every label on the pixel face, authored size, truncating,
whole-line boxes; the face has every tooltip glyph (`– — … · / %` …); no two inventory boxes overlap, all inside the
panel, the longest item name fits or ellipsises. `ReleaseFontDependencyTests` (EditMode, 1): scans every release
script for `GetBuiltinResource<Font>` — only `UiFont.cs` may.

Proof: `12_inventory_pixel_font.png`.

## 4. Enemy health bars

**Root cause.** No enemy health presentation existed at all; bosses had no health UI either.

**Fix.**
- `Presentation/Vfx/WorldHealthBar.cs` (new): 24 px × 4 px (1 px near-black border, dark back, high-contrast fill) on
  the `UIWorld` layer, built once per actor at spawn and rescaled on `HealthComponent` events only — never per tick.
  Hidden at full health, shown from the first point of damage, hidden again at full or on death (the approved rule,
  no timer). Follows the actor pixel-snapped, world-aligned (never rotates). Fill shrinks in whole pixels.
- Elites: `Style.Elite` — 32 px, filled with the existing Elite accent (`FeedbackConfig.EliteBossTelegraphColor`).
- Bosses: no boss UI existed, so a dedicated screen bar was added to `DungeonHudView` (top-centre, x 280–500 — clear
  of the 266 px depth/objective block and of the coins block starting at 514): boss display name + `hp / max` + bar,
  bound by `DungeonHudViewModel.BindBoss` to the boss's authoritative `HealthComponent`, visible only while the boss
  room is active and the boss alive. No redundant world bar on a boss.
- Wiring in `ExpeditionScene`: normal enemies on spawn, Elites when their engagement spawns them, the boss at depth
  build. Clients: `ApplyReplicatedHealth` raises the same events, so a replica's bar shows the replicated value.

**Tests.** `EnemyHealthBarTests` (PlayMode, 6): full → hidden, damaged → visible, fraction → whole pixels, healed to
full → hidden, death → hidden, no objects per tick; geometry/colours/layers; follows and never rotates; Elite wider and
accented; replicated health on a client; boss screen bar only while active with name and fraction, no world bar,
placed between the top-left and coins blocks.

Proof: `03_normal_enemy_health_bar.png`, `04_elite_health_bar.png`, `05_boss_bar_during_encounter.png`,
`13_built_player_combat_room.png`.

## 5. Pause menu — real buttons and navigation

**Root cause.** `PauseMenuViewModel` existed (Resume / Settings / Quit-to-desktop) but the expedition never built a
screen for it: opening the pause only changed the HUD objective text.

**Fix.**
- `PauseMenuViewModel`: items RESUME / SETTINGS / RETURN TO MAIN MENU / QUIT GAME; `ConfirmReturn` and `ConfirmQuit`
  screens with `Confirm` / `CancelConfirmation`; `Back` (nested panel first, root resumes); Pause while confirming
  cancels; confirmation text states the approved loss rule (85 Solo Quit) when an expedition is active and a plain
  "profile is saved" otherwise; the world pause is released before any leave path runs; co-op never holds the world.
- `App/PauseMenuScreen.cs` (new): dimmed backdrop, title, four `UiKit.Control`s (hover/focus brackets/pressed
  inset/click, same as the front-end), the confirmation panel (CANCEL focused first, CONFIRM as the primary) and the
  Settings page through the new shared `App/SettingsPanel.cs` (extracted from `MainMenuScreen`, which now uses it too).
  Each screen is one focus list pushed/removed on the `MenuInput` stack — never a stacked duplicate layer.
- Esc ownership: the in-run Pause action of the player input reader owns Escape; `MenuInput.KeyboardBackEnabled=false`
  in the expedition so one key press cannot open and close the same menu; controller B still routes Back.
- **RETURN TO MAIN MENU** → `ExpeditionScene.ReturnToMainMenu()`: an active expedition ends through the existing
  `ExpeditionService.Fail()` (the one failure transaction: carried loot and coins lost, XP commits, marker closed); the
  expedition-ended handler saves, leaves the network session if one is live (`LeaveAsync`), `Menu.LeaveBase()`, loads
  the Main Menu. No second loss implementation. From the Shelter the existing Back/LEAVE path saves and leaves with
  nothing to fail. **QUIT GAME** keeps the existing `GameApp.Quit` semantics (save, quit; an open run resolves as failed
  on next boot) behind a confirmation.
- Found while wiring the built-player smoke and fixed as a strictly necessary supporting fix: after a run ended, the
  party lobby stayed "started", so START EXPEDITION was refused for a second run in the same session.
  `PartyLobby.Reopen()` (host decision, called from the session's expedition-ended handler) releases the start snapshot,
  clears Ready (81: a new run needs a new unanimous Ready) and the restored Base loadout is re-submitted so the next run
  starts from the loadout the player actually has.

**Tests.** `PauseMenuFlowTests` (EditMode, 6): resume without stacking, settings + Back to root, return-to-menu
confirmation text/cancel/confirm-once with the world pause released, no loss text outside a run, quit confirmation
(co-op text), controller stepping/wrap. `PauseScreenInteractionTests` (PlayMode, 7): four real controls, one layer on
the stack, pointer cursor while paused; hover/pressed/click; keyboard stepping; nested settings; confirmation with CANCEL
focused and controller confirm; Pause cancels a confirmation; labels fit and buttons ≥ 24 px. Live:
`FinalPlayabilityProofTests` — return to menu from an active run (failed once, marker closed, banked coins intact, at-risk
gear lost and never duplicated, XP kept, profile continues without an "abandoned expedition" message) and from the
Shelter (saved, no failure fabricated, expedition count unchanged). `BaseUiTests` +1: the party reopens and a second
expedition starts from the current loadout.

Proof: `06_pause_menu.png`, `07_pause_settings.png`, `08_pause_return_to_main_menu_confirmation.png`.

## 6. Room entry / combat lock timing

**Root cause.** `RoomEntryTrigger` was a box covering the **whole** room bounds, including the wall ring and the door
cells the `RoomDoorLock` blocker covers. A player's collider touched it from the previous room's doorway; the room
activated, and the entry blocker went solid on top of / in front of a player who had never entered — the fight ran from
the corridor.

**Fix.**
- `RoomEntryTrigger.InteriorVolume`: the room bounds inset by an explicit 2-tile margin (wall row + door blocker row +
  the 0.4-tile player reach) on every side — inside the room proper, excluding every door cell, never overlapping a
  neighbouring room. Activation now needs the player's collider inside the interior; the lock closes behind them.
- `RoomDoorLock`: a lock never closes on a player standing in the doorway — if a player overlaps the socket cells when
  the room locks, the blocker stays **pending** and engages the moment the doorway is clear (checked in FixedUpdate).
  A teammate mid-doorway walks in and the door shuts behind them; nobody is trapped inside the wall. The lock is now
  visible: a steel plate with an amber stripe across the doorway while the blocker is solid (LowProps, above floor,
  below characters), so a shut door reads as shut. `ContactFilter2D.noFilter` (the non-obsolete property) is used.
- Exit sealing and the reciprocal-exit validation from the previous pass are untouched and re-verified.

**Tests.** `RoomEntryGeometryTests` (EditMode, 2): for all 63 rooms the volume is inset by the margin, excludes every
door cell and a player standing on it; across 3 biomes × 8 seeds no volume reaches into another room.
`RoomEntryTimingTests` (PlayMode, 3): on the shipped Small/Medium/Large combat rooms of all three biomes, every socket
(N/E/S/W, 27 cases): standing in the doorway and one cell inside does not activate or lock; the interior activates
exactly once; the entry blocker is solid and drawn behind a player who is clear of it; enemies spawn inside that room;
a teammate in the doorway keeps the lock pending until they cross, then it shuts, and unlock clears pending; a sealed
spare socket stays sealed across lock/unlock. `RoomRuntimeTests.EntryTrigger_…` updated to the interior rule.
`DungeonExitValidationTests` (7) still green.

Proof: `09_combat_room_entry_locked_behind.png` (player inside, enemies inside, plated doorway behind) and
`13_built_player_combat_room.png` from the shipped exe.

## 7. Custom pixel mouse cursors

**Root cause.** None existed; the OS arrow showed everywhere including gameplay.

**Fix.**
- `Editor/ArtGen/CursorFactory.cs` (new, `RuinRail/Art/Generate Cursors`): original pixel pointer (plate, 1 px
  outline), hover variant (amber accent), and a gap-centred amber crosshair, authored at 1:1 and baked ×2 for a desktop,
  written to `Art/UI/cursor_*.png`, imported as **Cursor** textures (point, uncompressed, no mips, readable) and bound
  into `UiSkin` with hotspots: pointer tip `(2,0)`, crosshair centre `(17,17)`. No anti-aliased fringe (every pixel is
  alpha 0 or 255, asserted).
- `UI/Theme/CursorService.cs` (new): the cursor is a pure function of the scene's **base** (Pointer for Main Menu /
  Shelter, Aim for the dungeon — set by `GameApp.Compose`), an **overlay count** (pause and inventory push/pop the
  pointer over gameplay) and **hover** (`UiControl` enter/exit; only decorates the pointer, never over gameplay).
  `OnApplicationFocus(true)` re-applies the current result after alt-tab. Gamepad navigation never needs the mouse.
- `ArtProductionContract` validates cursor textures as Cursor type + readable instead of as sprites.

**Tests.** `CursorTests` (EditMode, 4): three bound textures, Cursor type, readable, point, uncompressed, no mips,
desktop-sized, hotspots inside, aim hotspot centred, pointer hotspot at the tip, hover distinct and amber, no fringe;
the resolve rule; the ownership sequence (menu → hover → dungeon → pause → inventory → pops → focus regain) applies
exactly the expected cursors, never negative; texture resolution per kind and null for the OS cursor. Live: the proof
run asserts Pointer in menus, Aim in the dungeon, Pointer while paused / in the inventory, Aim again after.

**Limitation (documented, not faked):** a hardware cursor is not part of a rendered frame, so no screenshot shows it.
The proof folder carries the bound textures at ×4 (`10_cursor_ui_pointer_x4.png`, `10_cursor_ui_hover_x4.png`,
`11_cursor_gameplay_aim_x4.png`); the state switching is validated by tests and by the shipped-player smoke checks
("custom cursors bound", "gameplay owns the aim cursor", "pause menu usable (4 controls, pointer cursor)", "aim cursor
restored after pause").

---

## 8. Built-player smoke (extended)

`SmokeRunner` now runs a playability stage in the dungeon (all in the shipped exe): player body drawn; held weapon
drawn (`weapon_p9_ranger`); no open exit into the void; HUD and inventory on the pixel face; custom cursors bound;
gameplay owns the aim cursor; pause usable (4 controls, pointer cursor) and the aim cursor restored; a combat room
activated from its interior with the entry locked behind; enemies spawned inside it with bodies drawn; an enemy's
health bar hidden at full health and shown after damage. After the first run's return/save/reload, a **second**
expedition is started and left through Pause → RETURN TO MAIN MENU → confirm: the summary is `Failed`, the session is
closed, the reloaded profile has the marker closed and banked coins intact, PLAY continues without an abandoned-run
message. Result JSON: `TestResults/smoke_result.json` (`Success: true`, `ReturnToMenuOk: true`, 15 checks).

---

## 9. Files changed

Runtime:
`Combat/Weapons/IHeldWeaponView.cs`*, `Presentation/Animation/HeldWeaponVisual.cs`*, `Presentation/Vfx/WorldHealthBar.cs`*,
`App/PlayerVisualComposer.cs`*, `App/PauseMenuScreen.cs`*, `App/SettingsPanel.cs`*, `UI/Theme/CursorService.cs`*,
`App/ExpeditionScene.cs`, `App/GameApp.cs`, `App/GameContentCatalog.cs`, `App/MainMenuScreen.cs`, `App/SmokeRunner.cs`,
`App/UiKit.cs`, `Dungeon/Runtime/RoomDoorLock.cs`, `Dungeon/Runtime/RoomRuntime.cs`, `Multiplayer/NetworkPlayerCombat.cs`,
`Multiplayer/NetworkPlayerMotion.cs`, `Multiplayer/NetworkPlayerObject.cs`, `Multiplayer/PartyLobby.cs`,
`Multiplayer/PlayerPresence.cs`, `Multiplayer/WeaponNetSync.cs`, `Player/PlayerAiming.cs`, `UI/Base/BaseSession.cs`,
`UI/Hud/DungeonHudView.cs`, `UI/Hud/DungeonHudViewModel.cs`, `UI/Inventory/InventoryView.cs`,
`UI/Navigation/FocusNavigation.cs`, `UI/Navigation/ScreenNavigation.cs`, `UI/Pause/PauseMenuViewModel.cs`,
`UI/Theme/UiControl.cs`, `UI/Theme/UiSkin.cs` (* = new).

Editor: `Editor/ArtGen/CursorFactory.cs`*, `Editor/ArtGen/PixelFontFactory.cs` (en dash),
`Editor/Production/ArtProductionContract.cs`, `Editor/Production/GameContentCatalogBuilder.cs`.

Assets: `Art/UI/cursor_pointer.png`, `cursor_pointer_hover.png`, `cursor_aim.png` (+ meta), `Resources/UiSkin.asset`
(cursors bound), `Resources/GameContentCatalog.asset` (33 weapon sprites), `Resources/Fonts/ruinrail_pixel.*` (rebuilt,
91 glyphs). Unity re-serialised `Prefabs/Rooms/OvergrownLabs/Labs_Treasure_01.prefab`, `Settings/UniversalRP.asset` and
`UniversalRenderPipelineGlobalSettings.asset` during the build's `SaveAssets`; no deliberate change was made to them.

Tests: EditMode `HeldWeaponBindingTests`*, `CursorTests`*, `RoomEntryGeometryTests`*, `PauseMenuFlowTests`*,
`ReleaseFontDependencyTests`*, `BaseUiTests` (+1), `UiNavigationTests` (updated items); PlayMode `HeldWeaponVisualTests`*,
`RemotePlayerVisualTests`*, `EnemyHealthBarTests`*, `PauseScreenInteractionTests`*, `RoomEntryTimingTests`*,
`FinalPlayabilityProofTests`*, `InventoryFontRegressionTests`*, `LiveDungeonCapture` (folder-aware), `PauseMenuTests`
(updated), `RoomRuntimeTests` (updated), `Game.Tests.PlayMode.asmdef` (+ `Game.Persistence` reference).

Proof screenshots (`TestResults/FinalPlayabilityProof/`, each with an `_x2` copy where captured):
`01_local_player_held_p9_ranger`, `01_control_weapon_hidden`, `02_remote_replica_body_and_weapon`,
`03_normal_enemy_health_bar`, `04_elite_health_bar`, `05_boss_bar_during_encounter`, `06_pause_menu`,
`07_pause_settings`, `08_pause_return_to_main_menu_confirmation`, `09_combat_room_entry_locked_behind`,
`10_cursor_ui_pointer_x4`, `10_cursor_ui_hover_x4`, `11_cursor_gameplay_aim_x4`, `12_inventory_pixel_font`,
`13_built_player_combat_room` (shipped exe). Logs: `TestResults/playability-*.log`, `smoke_result.json`.

---

## 10. Known remaining external blockers / follow-ups

- **Live UGS / Relay** is still not linked on this machine; the remote-replica composition is verified on the
  deterministic harness and the NGO prefab's composer seam, not over a live session. When a project link exists, the
  `RUINRAIL_LIVE_SERVICES=1` check (the one skipped test) and TASK 181's two-client gate remain to be run.
- The hardware cursor cannot be captured in a screenshot (see §7); a manual windowed run is the visual confirmation.
- The camera rig clamps to the dungeon's visible bounds (existing design), so in edge rooms the player is not centred
  in the frame — visible in several captures, not a regression.

## 11. Terminal status

`FINAL_PLAYABILITY_PASS_COMPLETE`
