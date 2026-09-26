# RUINRAIL — Room / HUD / Audio / QoL Fix Pass

> **Scope:** `RUINRAIL_ROOM_HUD_AUDIO_QOL_PROMPT_EN.md`, executed from the state after
> `START_HP_DASH_ICON_HUD_MERCHANT_COMPLETE`. The eight listed runtime/QoL issues only. No inventory, merchant,
> pause, weapon, enemy, room-layout, art-style, progression, save-model or multiplayer-authority redesign; no pinned
> Editor or package version changed.
> **Terminal status:** `ROOM_HUD_AUDIO_QOL_PASS_INCOMPLETE`
>
> Every repository-local issue (1–6, 8, 9) is fixed and verified. Item 7 (audio) is fixed as far as this environment
> can verify it — including OS-level proof that the shipped executable delivers audio to the Windows mixer — but
> §18 of the prompt requires a **human** to confirm the normal windowed player is audible before the status may be
> complete, and no human listener was available here. §8 below states exactly what remains and exactly what was
> found. Nothing else is outstanding.

| Gate (final sequence after the last source change: PlayMode → EditMode → validators → build → smokes) | Result |
|---|---|
| `./scripts/run-unity-tests.ps1 -TestPlatform PlayMode` | **PASS — 679 passed / 679 discovered, 0 failed** |
| `./scripts/run-unity-tests.ps1 -TestPlatform EditMode` | **PASS — 814 passed / 815 discovered, 0 failed** (1 skipped: the pre-existing `RUINRAIL_LIVE_SERVICES=1` live-Sessions check) |
| `ContentCountValidator` | PASS — 52/52 counts exact, 0 problems |
| `FinalProductionValidator` | PASS — 9/9 sections, 0 errors |
| `PresentationValidator` | PASS — 8 checks, 0 problems |
| `AssetPipelineValidator` | PASS — 6 checks, 0 problems |
| `ReleasePathVisualScan` | CLEAN — 0 placeholder references, 0 missing references |
| `ArtProductionContract` | PASS — 10 checks, 0 problems |
| `AnimationAssetAudit` / `AudioAssetAudit` / `MusicAssetAudit` | COMPLETE 22/22 + 33/33 · COMPLETE 53/53 defined, 53/53 with clips · COMPLETE 11/6/3 |
| `CompletionAssetManifest` | 309 roles — 309 INTEGRATED, 0 PLACEHOLDER, 0 MISSING (was 307; `ui.coin_icon` and `ui.vignette_low_hp` added) |
| Release build (`ReleaseBuildTool.BuildBatch`, Windows x64, **non-development**) | **Succeeded** — 0 errors, 1 warning (the pre-existing external UGS project-ID notice), 158.0 MB |
| Built-player smoke, headless (`RUINRAIL.exe -batchmode -nographics -smoke -seed 31 …`) | **PASS** — 15 playability + 12 combat + 25 loot + 24 audio + 34 inventory + 24 HUD + 1 merchant + **12 weapon-cache + 31 room/HUD/QoL** checks, `ReturnToMenuOk: true`, 0 exceptions / missing scripts / missing references |
| Built-player smoke, windowed (`-smoke -seed 31 -screen-width 1280 -screen-height 720 -proofdir … -hudproofdir …`) | **PASS** — same checks, 39 captures written by the shipped executable, listener mix carried signal in 58/60 sampled frames |
| Windows audio-session probe against the shipped windowed player (outside Unity) | **AUDIO REACHED THE WINDOWS MIXER** — peak 1.0000, 115 of 149 samples carrying signal, session volume 1.00, not muted |

Unity `6000.3.24f1`. Baseline before this pass: EditMode 793 discovered, PlayMode 659 → **+22 EditMode, +20 PlayMode
tests**. Smart App Control was not touched.

---

## 1. Weapon Cache — root cause and fix

### Root cause

The interaction path was complete up to, and including, the point where the game asked a question nobody was
listening for.

`WeaponCacheEvent.OnActivate` deliberately resolves nothing: a cache presents three weapons and the *pick* happens in
`Choose`, so activation returned `DungeonEventOutcome.Unavailable` with the detail `"choice_required"` and left the
event `Available` (`Events/WeaponCacheEvent.cs`). `DungeonEventInteractable.Interact` raised its generic `Activated`
event with that result and returned `false`.

**Nothing in the runtime composition ever subscribed to it.** There was no Weapon Cache view, no view model, and no
line in `ExpeditionScene` that reacted to a choice event — the `Choose` method was reachable only from the EditMode
tests. So the prompt resolved correctly (`[E] CHOOSE WEAPON CACHE` came from a real, usable interactable), the press
reached the event, the event answered "a choice is required", and the answer was dropped on the floor. Every one of
the prompt's suspects was ruled out on the way: the prompt and the Interact press resolve the *same* object
(`PlayerInteractor.FindNearestInteractable` serves both), the handler existed, the state was valid, and no input gate
or authority check rejected anything.

### Fix

1. `DungeonEventDetails.ChoiceRequired` (`Events/DungeonEvent.cs`) replaces the loose string literal.
2. `DungeonEventInteractable` gained `ChoiceRequested(interactable, actor)` — the exact counterpart of the merchant's
   `Opened`, raised when the bound event answers a press with `ChoiceRequired`, carrying the acting player. `Interact`
   now returns `true` for that case: the press *did* something. Without a subscriber the event is still untouched, so
   the old behaviour cannot silently return unnoticed (there is a test for exactly that).
3. New `UI/WeaponCache/WeaponCacheViewModel.cs` + `WeaponCacheView.cs`: the selection screen, built from the same
   `UiBuild`/`UiSkin` primitives as the inventory and merchant windows — graphical item rows with rarity frames and
   icons, the inventory's own `ItemTooltip` in the details panel, and `TooltipComparison` against the equipped weapon.
   No parallel UI framework.
4. `ExpeditionScene.AttachWeaponCache` subscribes per room; `OnWeaponCacheChoiceRequested` binds the screen to *that*
   cache and opens it once. While it is up: `GameplayInputGate.Hold()`, the solo world pause, the pointer cursor, the
   interaction prompt hidden, the window's `FocusList` pushed on the run's `MenuInput` stack. Esc/B/`LEAVE` close it,
   `BeforePauseToggle` closes it before pausing, the inventory toggle ignores it. Entering a new depth closes it and
   drops the binding; the scene's dispose releases input and ignores late requests.
5. The reward stays the event's: `Take()` calls `WeaponCacheEvent.Choose`, which transfers into the chooser's backpack
   and consumes the cache for the whole party. Authority unchanged.

### Required behaviour, verified

Prompt only in range and only while available; one press → one screen; one screen instance; choices show real weapon
icons, rarity frames and stats; confirm grants **exactly one** weapon; the cache is consumed and shows no prompt and
cannot be reopened; a full backpack refuses the take **without** consuming the cache; gameplay input gated while open
and fully restored on close (aim cursor, prompt, focus stack, time scale); keyboard, controller and mouse all reach
every choice and both actions; a scene transition disposes the screen. Live-run proof in §10; shipped-player proof in
the 12 `cache:` smoke checks.

---

## 2. Room titles

**Trigger.** `RoomRuntime` gained one event, `PlayerEntered(room, player)`, raised from `NotifyPlayerEntered` when a
player becomes a new occupant — that is the *existing* authoritative room-entry state: the inset
`RoomEntryTrigger` volume that already activates combat rooms and locks doors. There is no second detector (an
EditMode test forbids `OnTriggerEnter`, `Physics2D` and `RoomRuntime` from appearing anywhere under `UI/Hud`). The
volume is inset by two tiles, so standing in a doorway does not fire it; a spawn/preload does not fire it; and
`ExpeditionScene.OnRoomEntered` ignores every object that is not the local player.

**No repeats.** The scene keeps `_revealedRoom`; a reveal only happens when the entered node differs from it. Moving
around inside a room, or re-entering the room the player is already in, changes nothing. Returning to a previously
visited *different* room reveals it again (consistent rule: one reveal per genuine entry).

**Names.** `Dungeon/Rooms/RoomDisplayNames.cs` is a presentation mapping from the stable room id to a short,
atmospheric, biome-appropriate name — the definitions keep their ids as identity (coding rule 9). All **63** shipped
rooms are named, every name is unique, none contains an underscore and none falls through to the generic fallback
(asserted). Examples: Ruined Metro — *Collapsed Platform*, *Service Junction*, *Terminus Hall*; Rustworks — *Smelter
Floor*, *Boiler Junction*, *Great Furnace*; Overgrown Labs — *Specimen Wing*, *Hydroponics Bay*, *Root Nexus*. An
unnamed id added later still announces a readable `RoomType` fallback instead of an internal id.

**Special rooms** carry a second, upper-case role line: `MERCHANT`, `TREASURE`, `BOSS`, `MEDICAL`, `SUPPLY CACHE`,
`ENTRY`; an Event room shows the *actual* event it holds (`WEAPON CACHE`, `CURSED CHEST`, …) taken from the bound
instance. An ordinary Combat room has no role line, so a reveal during a fight is a single line.

**Presentation.** `HudRoomTitleView`, top-centre at y 28..48 on the 640×360 frame, below the boss-bar band: fade in
0.25 s → hold 1.35 s → fade out 0.45 s = **2.05 s** total, on a plate exactly as wide as the announced name so it
reads over a bright floor and never becomes a permanent bar. Unscaled time, so it plays at the same speed under a
pause. It is cleared outright on a depth change and at the end of a run.

**Depth objective.** The old permanent `DEPTH 1 — find and defeat the boss` block is gone. Entering a depth reveals
the Start room's name with the role line `DEPTH n — REACH THE BOSS` through the same banner, then it fades.

---

## 3. Top-left HUD cleanup

`DungeonHudView` top-left is now, in order: the **minimap panel** (100×76 at 6,6) with a compact `D1` depth chip
inside its own frame, the **biome name** on a readability plate directly under it (6,84), and the co-op party lines
under that (6,96). `HudSnapshot.Objective` and `SetObjective` were removed rather than left as dead API; the three
call sites went with them. A smoke check asserts no HUD text contains `find and defeat` or starts with `DEPTH `.

## 4. Graphical coin HUD

`HudCoinView` top-right (62×16 at 572,6): the generated pixel token, then the number, on a restrained dark plate with
a thin steel edge. The word `COINS` is gone from the HUD entirely (asserted). The number is re-rendered from the
snapshot on every publication, so a pickup, a merchant purchase or a sale updates it immediately — never a cached
value. A five-digit purse still fits the plate (asserted).

**Art:** `Assets/Game/Art/UI/ui_coin_icon.png`, 12×12, generated by the existing pipeline (`UiFactory.CoinIcon` — a
struck brass token with a dark rim, a lit upper-left arc and a stamped bar device), written by
`ArtIntegration.GenerateUiSprites` / `GenerateMissingUiSprites`, bound through `UiSkin.CoinIcon`, manifest role
`ui.coin_icon`, provenance recorded. No debug placeholder.

## 5. Minimap

**Architecture.** A lightweight room-graph map driven by the dungeon layout data, not a camera render and not a world
texture.

- `UI/Hud/MinimapModel.cs` — pure data: rooms (node id, centre in layout tiles, kind), the realised door connections,
  and what the player knows. `MinimapLayout` is pure arithmetic that fits the discovered graph into the panel.
- `ExpeditionScene.BuildMinimapForDepth` fills it from `DungeonLayout.Placements` and `.Connections` when a depth is
  built — the same objects the generator produced, so a cell can only exist for a room that exists and a line can only
  exist for a real door (asserted against seeded layouts of all three biomes, seeds 11/12/77, depths 1–3).
- `HudMinimapView` renders it with pooled plates; it never searches the scene and no graphic in the HUD is a raycast
  target (the HUD canvas has no `GraphicRaycaster` at all), so the map never consumes gameplay mouse input.
- The UI assembly never sees the dungeon's own `RoomType`: the composition root translates once into
  `MinimapRoomKind`.

**Discovery rule (roguelite, conservative).** Entering a room marks it *visited* and reveals its **direct
neighbours** as discovered outlines. Nothing else is drawn, so the generated dungeon is never shown up front. A door
line is drawn only when both its ends are discovered. A **special room's symbol appears only once the player has been
inside it** — an adjacent merchant or boss room is an anonymous outline until entered, so no unexplored special room
is spoiled. An Event room reveals which event it actually holds at that moment.

**Symbols** are compact original 5×5 pixel glyphs drawn from rectangles, each differing in *shape* as well as colour
(spec 18.4): boss = solid block with a dark core, merchant = stacked shelves, treasure = chest with a lid line, weapon
cache = cross, medical = solid core, supply = two crates, anomaly = exclamation, start = hollow ring, transit = rails.

**Style.** Dark panel, thin steel frame, amber corner ticks; visited rooms steel, the current room amber with a bright
edge, undiscovered-but-adjacent rooms a dim outline. Crisp pixels, no parchment, no glossy radar.

**Layout behaviour.** The discovered graph is scaled to fill the panel and centred on its own bounds, clamped so a
cell is never drawn outside the frame at any graph shape. When the discovered graph grows past the point where cells
would be unreadable, the panel falls back to the **two-step neighbourhood around the current room** rather than
shrinking further; the current room is always on the map. At 640×360 the block ends at x 106 / y 93, clear of the boss
bar (280..500), the room-title band and the coins.

## 6. Low-HP vignette

`HudLowHealthVignetteView`, the first child of the HUD canvas, so every HUD element draws over it and the inventory,
pause, merchant and weapon-cache windows (their own canvases at sorting order 30) always draw above it.

| Value | Default | Tunable via |
|---|---:|---|
| Threshold | **0.30 of `CurrentHP / EffectiveMaxHP`** | `HudLowHealthVignetteView.DefaultThreshold` / `Configure` |
| Pulse | **1.0 Hz** | `DefaultPulseHz` / `Configure` |
| Opacity band at the threshold | 0.18 → 0.34 | `DefaultMinAlpha` / `DefaultMaxAlpha` |
| Opacity band near death | 0.34 → 0.58 | `DefaultCriticalMinAlpha` / `DefaultCriticalMaxAlpha` |
| Fade in / out on crossing | 0.25 s | `FadeSeconds` |

It is judged against the **effective** maximum the HUD already reads (base + equipment), so a 120-HP player with the
Scrap Vest gets the frame at 36 HP, not at 30. Below the threshold the band widens toward death; the centre of the
screen never tints (the art is a falloff that is transparent in the middle), and it is never a full red screen. It
fades away when healed above the threshold, stands down while any overlay window owns the screen, and `Reset()` is
called on depth change and at the end of a run, so no stale state survives a scene change, a death or a revive.

**Art:** `Assets/Game/Art/UI/ui_vignette_low_hp.png` (160×90, `UiFactory.LowHealthVignette`), manifest role
`ui.vignette_low_hp`, bound through `UiSkin.LowHealthVignette`, provenance recorded. `PixelCanvas.SetTranslucent` was
added for it — sprite art stays opaque by rule; a screen-edge falloff is the one case where a stepped opaque ring
would read as banding.

---

## 7. Dash cooldown wipe — root cause and fix

### Root cause

`HudDashIconView` already built a cooldown overlay with `Image.type = Filled`, `fillMethod = Radial360`, and set
`fillAmount` from the authoritative `CooldownRemaining / CurrentDashCooldown` every frame. **The overlay image had no
sprite.** uGUI's `Image.OnPopulateMesh` returns a plain quad and ignores `type` entirely when there is no sprite, so
the fill amount was discarded and the whole 20×20 area sat under a flat 72 %-opaque black rectangle for the entire
cooldown — the "static grey" the player saw. The *data* was right the whole time, which is why the existing checks on
`Cooldown01` passed.

### Fix

`UiBuild.Solid()` provides a 1×1 white drawing primitive and `UiBuild.Fillable(...)` builds a filled overlay that
actually honours its fill amount. The dash overlay is now a **vertical** wipe (`FillMethod.Vertical`, origin Bottom):
the grey band stands at the bottom of the slot and drains downward, revealing the icon from the top, and is
completely gone the instant the dash is ready. A one-pixel bright line rides the moving boundary, the icon keeps 85 %
of its brightness underneath (identifiable, not dimmed away), and completing the cooldown flashes the ready brackets
**once** rather than pulsing while ready. Progress remains `1 - remaining/total` from `PlayerDash` — there is no
independent UI timer. The same missing-sprite trap was closed on the HP and boss bars.

Verified at 0 % / 25 % / 50 % / 75 % / 100 % elapsed (strictly decreasing coverage, half the cooldown = half the
cover ±1 px), against the live component over many frames (±0.06 of the authoritative fraction), on a second dash
(cover refills), on the disabled state (no stale wipe) and in the shipped player. An EditMode/PlayMode guard now fails
if any HUD source declares `Image.Type.Filled` without giving the image a sprite.

---

## 8. Audio — what was found, what was fixed, what is not proven

### 8.1 What the previous pass could not see

The prompt is right to reject clip existence, `isPlaying`, mixer values and `AudioListener.GetOutputData` as proof.
All of those sample the **engine's own graph**. They stay true when the engine is mixing perfectly into a device
nobody is listening on. Everything below was chosen because it can explain silence that those checks cannot see.

### 8.2 Root causes addressed

| # | Cause | Why it produces silence with every in-engine check still passing | Fix |
|---|---|---|---|
| 1 | **Output-device changes were never handled.** Unity opens one device at start and stays on it. On a device change it tears the audio system down and **stops every `AudioSource`** — and nothing restarted them. | After a headset is plugged in, removed, or the Windows default endpoint changes, the run is silent for the rest of the session. `isPlaying` reads false but no code notices; mixer gains and the listener are still perfect. | New `Audio/AudioOutputWatchdog.cs` on the persistent root (composed in `GameApp` before anything plays): handles `AudioSettings.OnAudioConfigurationChanged`, re-initialises onto the new configuration, clears `AudioListener.pause`, restores `AudioListener.volume`, and raises `OutputReset`. `MusicDirector.OnOutputReset` restarts the music and ambience beds on the role and biome that are already active; `AudioService.OnOutputReset` re-resolves the mixer groups and restarts every tracked loop. |
| 2 | **Run In Background was off** (`ProjectSettings.runInBackground: 0`). | Unity pauses the whole player the moment its window is not focused, and the mix goes silent. A player who alt-tabs — or a window that never took focus on launch — hears nothing, while every measurement taken from inside the process still passes. | Enabled (`runInBackground: 1`; `visibleInBackground` was already 1). An EditMode test now fails if it regresses. Co-op also benefits: a session no longer freezes when a player alt-tabs. |
| 3 | **A settings document missing an audio key deserialized to 0.** `JsonUtility` leaves an absent float at 0, which is indistinguishable from "the player turned it all the way down". | A profile written before a key existed, or a truncated document, boots the game completely silent with no error anywhere. | `UserSettingsService.MigrateAudio` inspects the raw document: a key that is genuinely **absent** is restored to its approved default and the migration is reported in the diagnostics; a key that is **present and 0** is the player's own choice and is never overridden. |
| 4 | **Ambience had no verifiable gain.** The prompt requires Ambience > 0, but there is no ambience slider — it is a fixed share of the SFX bus, so nothing checked it. | A diagnostic looking for a setting that never existed reports nothing. | `AudioLevels.AmbienceGain` (= `GainFor(Sfx) × AmbienceCeiling`) is now the single published value; `MusicDirector.AmbienceCeiling` aliases the one constant. Asserted > 0 on a fresh profile, scaled by master, zeroed by mute, and always below the SFX bed. |
| 5 | **A process could boot muted or paused** from leftover global state. | `AudioListener.volume`/`pause` are process-wide. | The watchdog clears both at boot and after every device change; a source scan fails the suite if any release script outside the three legitimate seams touches them. |

**Fresh-profile defaults verified:** Master 1.00, Music 1.00, SFX 1.00, Ambience 0.40 (the ceiling), Mute false —
in the shipped player, not only in a test.

### 8.3 Stronger runtime diagnostics

`AudioRuntimeDiagnostics.Capture()` records, and the smoke writes into the evidence file, everything §8.4 of the
prompt asks for: the Unity audio configuration (sample rate, speaker mode, DSP buffer, real/virtual voices, driver
capabilities, output sample rate), how many device changes were handled, batch/focus/run-in-background state, the
AudioListener count and enabled count, `AudioListener.volume`, `AudioListener.pause`, the resolved user levels
including ambience and mute, and for **every** `AudioSource` its clip, `isPlaying`, volume, `spatialBlend`, output
mixer group, mute and loop. `Problems()` turns all of it into a named list of in-engine causes of silence; the smoke
and the live proof both fail if that list is non-empty. The evidence file now states in writing that the listener-mix
sample is **not** a claim about the speakers.

Last shipped-player capture (`TestResults/RoomHudAudioQolProof/audio_runtime_evidence.txt`): one enabled listener,
`AudioListener.volume 1.00`, `pause False`, master/music/sfx 1.00, ambience 0.40, not muted, the biome music bed and
the biome ambience loop both playing with non-zero volume, 24/24 audio checks passed, **no in-engine cause of silence
found**.

### 8.4 Audible paths exercised in the shipped player

Main Menu music and UI confirm/cancel; Shelter music; in the Dungeon: biome ambience, exploration and combat music
beds (with the crossfade), gunfire, reload, dry-fire, melee, enemy hit/death/telegraph, chest open, item and coin
pickup, door open, dash and merchant/transit cues; music and ambience keep playing under the pause and after it;
Main Menu music restored after leaving the run with the expedition-failed stinger. Boss music/stinger routing is
covered by `MusicRoutingTests` in the PlayMode suite.

### 8.5 Loopback capture: not available — and what was captured instead

**No loopback capture tool is installed on this machine** (no `ffmpeg`, no `sox`). No WAV was fabricated.

Instead, an **OS-side probe outside Unity** was written, added to the repo harness as
`scripts/probe-audio-session.ps1` (documented in `scripts/README.md`) so the owner can rerun it, and run against the
shipped windowed executable (`TestResults/RoomHudAudioQolProof/windows_audio_session_probe.txt`). It uses the Windows
Core Audio API to read the audio-session peak meter for the RUINRAIL process **on the endpoint Windows is currently
playing out of**:

```
# default render endpoint: Realtek Digital Output (Realtek(R) Audio)
# session: pid=18200 state=0 sessionVolume=1,00 muted=False
# samples: 149, samples carrying signal: 115, peak: 1,0000
RESULT: AUDIO REACHED THE WINDOWS MIXER - peak 1,0000 on the default render endpoint.
```

That is strictly stronger than anything measurable inside Unity: Windows itself confirms the process opened a render
session on the current default endpoint, that the session is unmuted at full volume, and that real signal arrived on
it in 115 of 149 samples.

**A finding the owner should check first:** this machine's default playback endpoint is **"Realtek Digital Output"** —
an S/PDIF digital passthrough. If nothing is connected to that optical/coaxial port, Windows accepts audio and
nobody hears it, and *every* application is silent, not just RUINRAIL. The machine also reports a USB audio device and
NVIDIA HD Audio. Before treating any of this as a remaining game bug, switch the Windows default playback device to
the one actually in use and relaunch. This is also precisely the situation cause #1 above now survives: switching the
device while the game runs used to leave the run permanently silent and now does not.

### 8.6 Was normal shipped-player audio manually confirmed audible?

**No.** No human listener was available in this environment, and loopback capture is unavailable. Per §18 of the
prompt this alone forces the incomplete terminal status, and it is the only reason for it. What remains is a
one-minute human check:

1. Set the Windows default playback device to the one you are actually listening on.
2. Run `Builds/Windows64/RUINRAIL.exe` normally (no flags) and listen at the Main Menu, in the Shelter, and in a
   dungeon room while firing. `./scripts/probe-audio-session.ps1 -Seconds 30` alongside it says whether Windows is
   receiving the signal on the endpoint it is playing out of.
3. If it is audible, this pass is complete as delivered. If it is not, `TestResults/RoomHudAudioQolProof/audio_runtime_evidence.txt`
   and `windows_audio_session_probe.txt` from that run will say whether the silence is inside the engine (the problem
   list will name it) or between Windows and the speakers (session present, signal delivered).

---

## 9. Room title + minimap coordination

Both read the same authoritative event and nothing else. `ExpeditionScene.OnRoomEntered` runs once per genuine entry
of the local player and, in order: sets the run's `CurrentRoom`, resolves what the room actually holds
(`SetKind`), marks it entered on the map (visited + neighbours discovered + current marker moved), and then — only if
the node changed — plays the room-title reveal. A special room's marker becomes visible in the same step. There is no
competing room detector anywhere; the HUD assembly is forbidden by test from containing one, and remote-player
movement never reveals anything (§13 of the prompt).

## 10. Proof

### `TestResults/RoomHudAudioQolProof/` — shipped-player captures (`qol_*`, windowed run, seed 31, Overgrown Labs)

| Capture | Shows |
|---|---|
| `qol_01_weapon_cache_prompt.png` | `[E] CHOOSE WEAPON CACHE` in reach |
| `qol_02_weapon_cache_ui_open.png` | the selection window over the run: three rolled weapons with icons, rarity frames, stats and the VS EQUIPPED comparison |
| `qol_03_weapon_cache_reward_granted.png` | the reward taken, screen closed, gameplay restored |
| `qol_04_room_title_reveal.png` | `RECEPTION WING / ENTRY` revealed on entering a room |
| `qol_05_room_title_special_room.png` | a special room revealing its role line |
| `qol_06_minimap_current_room.png` | the minimap with the current room highlighted |
| `qol_07_minimap_after_discovery.png` | the map after several rooms were discovered |
| `qol_08_minimap_special_marker.png` | a special-room symbol appearing after entry |
| `qol_09_biome_label_beside_minimap.png` | the biome identity under the map |
| `qol_10_graphical_coin_display.png` | the coin token + number, top-right |
| `qol_11_low_hp_vignette.png` | the low-HP frame active |
| `qol_12…15_dash_cooldown_full / half / near_ready / ready.png` | the wipe at ~100 %, ~50 %, nearly done, and clear |
| `qol_16_clean_hud_640x360.png` | the whole HUD point-sampled to the 640×360 reference |

Also in that folder: `audio_runtime_evidence.txt` (the full runtime audio state of the shipped run),
`windows_audio_session_probe.txt` (the OS-side proof), `smoke_headless/` (the headless run's captures), and the
`live_*` captures plus `live_room_hud_qol_evidence.txt` and `live_audio_runtime_evidence.txt` written by the PlayMode
proof test from the Editor's real boot flow.

### Live-run evidence (`RoomHudAudioQolProofTests`)

```
seed 31 (OvergrownLabs) places a Weapon Cache on depth 1
minimap: 9 rooms on the depth, 2 discovered at the start, biome 'OVERGROWN LABS'
entered 'BIODOME FLOOR' (Combat); reveal #2
cache prompt: '[E] CHOOSE WEAPON CACHE'
cache screen open with 3 choices: Needle M7, Pulse Carbine B1, Wasp-45
took 'Needle M7' (weapon_needle_m7, Rare); cache consumed = True
coin readout: token + '249'
low-HP vignette at 34/120 (effective max), intensity 0,06, alpha 0,052
dash wipe: cooldown 1,4706s, vertical fill, clear at ready
```

The seed is not hard-coded: the test finds the first run seed whose depth-1 layout places a Weapon Cache by running
the shipped deterministic logic (biome draw, graph generation, room pool, event-kind pick), then asserts the real run
generated exactly what the search predicted.

## 11. Tests

**New — EditMode (22).**
`RoomTitleMinimapTests` (12): every shipped room has a unique, player-facing, non-id name; special rooms announce
their role and ordinary combat rooms do not; the reveal envelope fades in / holds / fades out inside the approved
1.5–2.5 s window; a new depth reveals nothing until the first entry; entering moves the marker and keeps what was
seen; only links between discovered rooms are drawn and they are real doors; a special symbol appears only after
entry; every marker kind has its own *shape*; cells fit the panel, keep layout orientation and land on whole pixels; a
sprawling graph falls back to the neighbourhood instead of unreadable cells; seeded dungeons of all three biomes
(3 seeds × 3 depths) map one-to-one onto the minimap with every cell inside the panel; the HUD contains no room
detector.
`AudioRuntimeHardeningTests` (10): a fresh profile is audible on every channel; a document missing its volume keys is
migrated to the defaults and the migration is reported; an explicit 0 is preserved; a document with no audio block at
all boots audible; ambience stays below combat readability and follows master and mute; the diagnostic snapshot names
every in-engine cause of silence; an output-device change restarts the beds; the watchdog is in the shipped
composition and owns the only `AudioSettings.Reset`; the shipped player keeps running when its window loses focus; no
release script silences the process globally.

**New — PlayMode (20).**
`WeaponCacheUiTests` (8): the event answers Interact with a choice request and the world handle forwards it; with no
subscriber the event stays untouched; one press opens one screen showing the rolled weapons; choosing grants exactly
one weapon, consumes the cache and cannot be repeated; a full backpack refuses without consuming; input is gated
while open and everything restores on close; keyboard, controller and mouse reach every choice and both actions; a
scene transition disposes the screen and it ignores late requests.
`RoomHudQolTests` (11): top-left is the map and the biome, not an objective block; the map draws the model's current
room, discovery, connections and markers; the HUD consumes no mouse input; coins are an icon plus a number that
follows the wallet; the room title fades in/holds/fades out and never repeats on its own; its band clears the boss
bar, the map, the coins and the tutorial prompt; the vignette appears at the threshold on **effective** max HP,
pulses, strengthens near death, clears when healed, is suppressed under an overlay and resets outright; it sits under
the HUD; the dash cooldown is a moving vertical wipe at 0/25/50/75/100 %, resets on a second dash and has no stale
disabled state; the wipe tracks the authoritative cooldown across many frames; no HUD source declares a filled image
without a sprite.
`RoomHudAudioQolProofTests` (1): the live-run proof above.

**Updated.** `DungeonHudTests` and `DungeonHudLayoutRegressionTests` follow the new top band (biome line and coin
number instead of the removed depth/objective/`n COINS` strings; the room-title panel joined the non-overlap set).

**Smoke (built player).** `SmokeRunner.RoomHudQol.cs` adds 12 `cache:` checks and 31 `roomhud:` checks; the audio
stage gained the ambience gain, the output-device watchdog and the full runtime-state problem list.

## 12. Files

**Runtime — changed:** `App/ExpeditionScene.cs`, `App/GameApp.cs`, `App/SmokeRunner.cs`,
`App/SmokeRunner.LootAudio.cs`, `Audio/AudioService.cs`, `Audio/MusicDirector.cs`, `Core/Rendering/AudioLevels.cs`,
`Dungeon/Runtime/RoomRuntime.cs`, `Events/DungeonEvent.cs`, `Events/DungeonEventInteractable.cs`,
`Events/WeaponCacheEvent.cs`, `Persistence/UserSettingsService.cs`, `UI/Hud/DungeonHudView.cs`,
`UI/Hud/DungeonHudViewModel.cs`, `UI/Hud/HudSlotViews.cs`, `UI/Theme/UiBuild.cs`, `UI/Theme/UiSkin.cs`,
`ProjectSettings/ProjectSettings.asset` (Run In Background).

**Runtime — new:** `App/SmokeRunner.RoomHudQol.cs`, `Audio/AudioOutputWatchdog.cs`,
`Dungeon/Rooms/RoomDisplayNames.cs`, `UI/Hud/HudInfoViews.cs`, `UI/Hud/HudMinimapView.cs`, `UI/Hud/MinimapModel.cs`,
`UI/WeaponCache/WeaponCacheViewModel.cs`, `UI/WeaponCache/WeaponCacheView.cs`.

**Editor — changed:** `Editor/ArtGen/ArtIntegration.cs`, `Editor/ArtGen/PixelCanvas.cs`, `Editor/ArtGen/UiFactory.cs`,
`Editor/Production/CompletionAssetManifest.cs`.

**Art — new:** `Assets/Game/Art/UI/ui_coin_icon.png`, `Assets/Game/Art/UI/ui_vignette_low_hp.png`
(+ `production/asset_provenance.json` entries, `UiSkin` bindings, manifest roles).

**Docs / harness:** `ui/91_DUNGEON_HUD.md` (top band, room-title reveal, dash wipe, low-HP vignette),
`scripts/probe-audio-session.ps1` (new, the OS-side audio probe), `scripts/README.md`.

**Tests — new:** `EditMode/RoomTitleMinimapTests.cs`, `EditMode/AudioRuntimeHardeningTests.cs`,
`PlayMode/WeaponCacheUiTests.cs`, `PlayMode/RoomHudQolTests.cs`, `PlayMode/RoomHudAudioQolProofTests.cs`.
**Tests — updated:** `PlayMode/DungeonHudTests.cs`, `PlayMode/DungeonHudLayoutRegressionTests.cs`.

## 13. Deliberately not changed

Inventory, merchant and pause UI; weapon, enemy, armor and consumable data; room layouts and dungeon generation;
progression; save semantics; the multiplayer authority model (the Weapon Cache reward still goes through the event's
own once-only `Choose`, and map discovery is local presentation derived from the local player's entries only); the
art style; the pinned Editor and package versions. Two small supporting changes were required and are called out
above: Run In Background (§8.2) and moving the contextual tutorial prompt down 12 px so it cannot be drawn over the
room-title reveal (§2).

## 14. Remaining limitations

1. **Audio has not been confirmed audible by a human**, and loopback capture is unavailable on this machine. This is
   the sole reason the terminal status is incomplete. §8.5–8.6 give the OS-level evidence that was captured instead
   and the exact one-minute check that closes it — including the likely environment cause (the default playback
   endpoint is an S/PDIF digital passthrough).
2. Live Relay / UGS Sessions are not claimed; the deterministic harness was used, as in previous passes.
3. `CombatAimCollisionProofTests.LiveRun_…` (pre-existing, not touched by this pass) failed once on its timing-
   sensitive direct-hit step during one PlayMode run and passed in the two runs before it and the run after it with
   identical sources. The gates in the table are the final clean sequence.
4. The windowed smoke captures are 1920×1080 (the shipped player applies its saved display mode and renders at the
   desktop size, the clean 3× of the 640×360 reference, rather than the 1280×720 requested on the command line — the
   same as previous passes); `qol_16_clean_hud_640x360.png` is the point-sampled 3× downscale of that back buffer and
   is therefore a true reference frame.
5. Seed 31 has no Merchant room on depth 1, so the built-player merchant stage reported its "no merchant on this
   seed" path; the merchant flow itself is unchanged by this pass and remains covered by `MerchantTradeUiTests` and
   by the previous pass's seed-11 smoke.

---

`ROOM_HUD_AUDIO_QOL_PASS_INCOMPLETE`
