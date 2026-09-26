# RUINRAIL — PRESENTATION / AUDIO / UX-QOL REPORT

> Pass scope: presentation, audio data, UX/QoL seams. No combat, economy, progression, boss, biome or multiplayer
> redesign. Every artefact referenced here lives under `TestResults/PresentationAudioUxQol/`.

## 1. Executive Summary

Thirteen work areas were re-verified against the current working tree before anything was changed. Forty-four of the
review findings were still real, three were already fixed and are recorded as stale.

The substantial work:

- **The black void is gone.** At the 640×360 reference the viewport is 20 × 11.25 tiles and 40 of the 63 shipping rooms
  are 16 × 12, so a small room *always* left four tiles of the camera's solid-black clear colour on screen. A dark,
  biome-aware environmental underlay now renders below the floor on the existing Ground layer, with no collider and no
  tile occupancy, and the camera clears to that biome's darkest note rather than to black.
- **The audio had no mix at all.** All 53 events shipped at gain 1.0, pitch 1.0–1.0, zero throttle and the default
  instance cap. They now carry an authored category mix (0.28–0.95), pitch variation and interval/instance limits on
  every high-frequency cue, and no variation at all on loops and warnings.
- **The music loops were off their own bar grid.** `BuildMusic` allocated one release tail more than the 8-bar grid and
  crossfaded only part of it back, so every repetition slipped 1.2 s off the beat through a 2-second dip. The tail is
  now wrapped onto the head and the files are exactly their grid; the fade-in to half peak fell from 3.48 s to 0.24 s.
- **Two dead seams were found and closed.** The player rig composed no `GrenadeLauncher`, so `CanThrowGrenades` was
  false and all four authored grenades were refused with `UnsupportedEffect` — they could be looted, priced and
  equipped but never thrown. And `PickupAttractor.SetBaseRadius` had no caller, so baseline attraction was 0 tiles.
  Both are the project's recurring "seam exists, nothing calls it" shape.
- **The Shelter Trader is now legible.** It was a list of `BUY <name>` strings beside one key/value line per offer; two
  rolls of the same weapon were one word apart. It now uses the Dungeon Merchant's own `MerchantRowView` rows and the
  inventory's tooltip/comparison layout, with affordability and already-equipped marked on the row.
- **New QoL:** a small 1.25-tile baseline pickup reach that will not pull through walls (Magnetic Coil still adds +3 on
  top, a 3.4× reach), a rebindable `QuickGrenade` action, an **Aim Assist On/Off** setting that defaults ON and reaches
  the real `ShotSolver`, a one-time low-ammo teaching prompt, timed-buff HUD chips bound to the runtime's own timers,
  and an eight-page Help/Codex reachable from the Main Menu and Pause.

Frozen systems were re-asserted against the shipped data and are unchanged.

## 2. Current-State Reverification

`TestResults/PresentationAudioUxQol/current_state_before.csv` — 47 rows, every one measured against the working tree
rather than taken from the review.

| Status | Count | Notes |
|---|---:|---|
| CONFIRMED (still real) | 44 | Every finding acted on below |
| STALE (already fixed) | 3 | Not reimplemented — see below |

**Stale findings, not reimplemented:**

| Finding | Why it is stale |
|---|---|
| Ambience loop continuity | `BuildAmbience` writes exactly 12.000 s with `MakeSeamless(1.2f)`; measured edge discontinuity is 0.0000. Only the *music* beds were off-grid. |
| Duplicate AudioListener | One `AudioListenerRig` on the persistent audio root guards the process listener; the dungeon camera explicitly adds none. |
| Door open/locked readability | Each biome already ships two authored sprites — open is a clear doorway, locked is a shutter filling it — and the sprite and the collider change together. Residual, documented not fixed: Ruined Metro's open lamp `#8A6329` and lock lamp `#C06434` are close in hue, but the shutter, not the lamp, carries the state. |

**Selected measurements that drove the work:**

| Area | Measured |
|---|---|
| Viewport vs rooms | 20 × 11.25 tiles viewport; 40 rooms at 16×12, 12 at 24×16, 6 at 32×20, 6 at 36×24 |
| Camera | `clearFlags = SolidColor`, `backgroundColor = Color.black`; clamp centres the camera on any axis the room does not fill |
| Audio | 53/53 events at gain 1.0, pitch 1.0–1.0, interval 0 ms, cap 4, one clip each |
| Music loops | exploration 27.87 s vs a 26.67 s 8-bar grid; tail below half peak for 1.47–2.01 s |
| Shelter Trader | focus rows are `"BUY " + DisplayName`; data rows are name + `"<price> C"`, no `UiText.Fit` |
| Pickup attraction | `_baseRadiusTiles` = 0 and `SetBaseRadius` has no caller anywhere |
| Grenades | `PlayerRigComposer` passes `launcher: null`; no `GrenadeLauncher` is composed anywhere |
| Input map | 13 actions, no `QuickGrenade` |
| Aim assist | `AimAssistConfig` exists and `ShotSolver` already returns the raw direction for a null config — but no setting reaches it |
| Prompts | 8 prompts, none about ammo |
| Help/Codex | none exists |
| Prop dressing | baked into prefabs; `FloorDetail` holds 1–8 tiles against a 146–148 tile small-room floor, identical on every visit |
| Secondary text | `InkFaint #68736F` on `Charcoal` = **3.34 : 1**, below 4.5 : 1 |
| Damage numbers | white `TextMesh`, no outline, shadow or plate |

## 3. Frozen Owner-Approved Systems

Nothing in this pass may move a balance value, a curve, a cap or a starter loadout. The full list is asserted in
`PresentationFrozenSystemsTests` and written to `frozen_systems_check.csv`; section 20 has the result.

Deliberately untouched: D1 ammo tuning, Field Knife, blaster tuning, all other weapon base balance, D1–D30 and
post-D30 difficulty scaling, the post-D30 reward curve, deepest-depth persistence, boss seeded attack selection, boss
anti-kite, elite frequency, room depth gating, biome gameplay identity, ammo caps, starter kit, run-start and
depth-arrival health, `PlayerStats`, `AffixRegistry`, `WeaponStatMath`, StatConsumerIntegrity, the graphical inventory,
Dungeon Merchant behaviour, non-combat rooms, progression, save/persistence, death/extraction, `EncounterBounds`, room
locks, minimap logic, character/weapon art, VFX and the multiplayer architecture.

## 4. World Substrate / Black-Void Fix

**Implemented.** `Assets/Game/Scripts/Presentation/World/WorldSubstrate.cs`, created per depth under the dungeon root
by `ExpeditionScene.BuildDepth` and destroyed with it.

Why an underlay rather than a camera trick: the void is not only *outside* the layout, it is between placed rooms and
inside the viewport of any room narrower than 20 tiles — which is 63 % of them. No camera clamp can cover that.

| Rule | How it is guaranteed |
|---|---|
| Renders below the gameplay floor | Existing `SortingLayers.Ground` at order **−1000**; floor tilemaps sit at `BaseOrderOf(Floor)` = 0. **No sorting layer was added** — art/102's twelve-layer contract is untouched. |
| Non-traversable, no pathing impact | No collider is created, and the validator fails if the component ever gains a `Collider2D` field. |
| No room-sealing / tile-occupancy impact | It is not a tilemap and not a room child; door sockets, `RoomExitSealer`, encounter bounds and the minimap never see it. |
| Deterministic for layout size | `CoverageFor(bounds)` is the layout rect grown by a constant 24 tiles — a pure function, asserted as such. |
| Quieter than the floor | Measured below. |
| No high-frequency noise | One low-contrast structural rhythm on the 16 px grid plus ~4 % single-value speckle; internal contrast 1.15–1.20 : 1. |
| Camera never sees past it | 24-tile margin against a 10 × 5.625-tile camera half-extent. |

**Biome palettes** (derived from the approved `RuinPalette` structural darks, deliberately darker than each biome's own floor):

| Biome | Substrate base | Luminance | Floor base | Floor luminance | Substrate as % of floor | Reads as |
|---|---|---:|---|---:|---:|---|
| Ruined Metro | `#1D2124` | 0.0148 | `#4E5153` | 0.0813 | **18 %** | deep tunnel bed / sleeper banding |
| Rustworks | `#1F1A17` | 0.0109 | `#393E41` | 0.0470 | **23 %** | dark industrial pit / plate seams and soot |
| Overgrown Labs | `#141C1B` | 0.0106 | `#7C837C` | 0.2197 | **5 %** | dark service deck / structural ribs |

The camera's clear colour is now that biome's darkest note, so even a window wider than layout-plus-margin shows the
same dark ground rather than black.

**Artefact:** `world_substrate_matrix.csv` — 15 cases (3 biomes × small / medium / large / layout-edge / cluster with
gaps), each recording layout size, coverage, uncovered viewport tiles (0 everywhere), sorting layer and order, collider
count (0 everywhere) and raw-black-void (NO everywhere). Also `substrate_luminance.csv`.

**Art note, stated honestly:** the underlay tile is generated deterministically at runtime from the biome palette
rather than authored as a PNG through the ArtGen pipeline. A `WorldSubstrate.SkinResolver` seam (the same shape as
`RoomDoorLock.SkinResolver`) accepts authored art without touching any caller, and the runtime tile is the fallback.
Authoring three substrate sheets is listed in section 26.

## 5. Audio Mix Before

`TestResults/PresentationAudioUxQol/audio_mix_before.csv` — all 53 events plus the 11 music beds, 3 ambience beds and
6 stingers, with bus, gain, pitch range, min interval, max instances, clip count, loop flag, clip duration, positional
flag, high-frequency flag and the runtime trigger path.

The state was uniform and unauthored:

| Field | Value across all 53 events |
|---|---|
| Gain | **1.0** |
| Pitch min / max | **1.0 / 1.0** |
| Min interval | **0 ms** |
| Max instances | **4** (the serialized field default, not an authored choice) |
| Clips | **1** each |
| Loops | 2 (`player.revive.start`, `weapon.blaster.heat_rising`) |

Bus routing was already correct and was not changed: Weapons 20, Enemies 8, Player 9, Loot 11, UI 5. Music and
ambience are not `AudioEventDefinition`s — they run through `MusicCatalog`/`MusicDirector`.

## 6. Audio Mix Changes

**Implemented** as data on the 53 event assets. The runtime already honoured every field
(`AudioService` applies `Volume`, `Random.Range(PitchMin, PitchMax)`, `MinIntervalMs` and `MaxInstances`), so no audio
architecture was rebuilt — only authored.

`AudioEventDefinition._volume` is `Range(0,1)`, so the mix attenuates: the loudest cues stay near the top and
everything frequent sits well beneath them.

| Category | Events | Gain | Pitch | Min interval | Max instances |
|---|---:|---|---|---|---|
| UI | 4 | 0.28–0.55 | ±1–3 % | 45–120 ms | 2 |
| Player weapons | 15 | 0.40–0.88 | ±1–6 % | 25–150 ms | 2–5 |
| Enemy weapons (telegraphs) | 3 | 0.66–0.86 | 0–±3 % | 60–120 ms | 2–4 |
| Impacts | 3 | 0.44–0.95 | ±3–7 % | 30–90 ms | 3–5 |
| Enemy vocals | 1 | 0.60 | ±6 % | 60 ms | 4 |
| Player feedback | 8 | 0.34–0.72 | 0–±4 % | 0–250 ms | 1–3 |
| Pickups / interactions | 7 | 0.32–0.62 | ±2–6 % | 55–150 ms | 2–4 |
| Doors / room transitions | 3 | 0.50–0.78 | 0–±3 % | 120–300 ms | 1–3 |
| Critical warning / low HP | 5 | 0.50–0.95 | none | 120–500 ms | 1 |
| Stingers | 4 | 0.78–0.90 | none | 150–400 ms | 1 |

Overall gain range **0.28–0.95** — a 3.4× spread (≈10.6 dB). Nothing is at 1.0 and nothing is near-silent; the
validator fails on either.

The mix's ordering rules are asserted rather than described: `player.death` > `enemy.hit`, `player.hit` >
`loot.pickup.coins`, `enemy.telegraph.boss` > `enemy.telegraph`, `weapon.rocket.explosion` > `weapon.fire.smg`,
`loot.drop.legendary` > `loot.drop.common`, `ui.purchase` > `ui.navigate`.

**No dedicated low-HP audio event exists** in this build. The "critical warning" category is authored over the cues
that do exist (player downed/death, blaster overheat and its warning, UI failure); a low-HP heartbeat would be a new
event and new content, and is listed in section 26 rather than invented here.

**Artefact:** `audio_mix_after.csv` (written by the EditMode suite from the shipped assets) and
`audio_runtime_matrix.csv` (runtime proof: one listener, pitch range live, throttle refusing a second play inside its
interval).

**Preserved and re-verified:** the persistent `AudioListenerRig`, 2-D relative attenuation and panning (`Spatializer`),
the expedition music state machine, the output-device watchdog and loop restart, and settings volume persistence.
## 7. Repetition / Variation Policy

Fifty-three events, one clip each. No additional clips were fabricated, so variation comes from pitch and throttling:

| Class | Gain band | Pitch | Min interval | Max instances | Why |
|---|---|---|---|---|---|
| Most repeated (enemy.hit, melee swings, SMG fire) | 0.44–0.52 | ±6–7 % | 25–40 ms | 4–5 | Fires many times a second; quietest and widest variation |
| Ordinary weapon fire | 0.56–0.66 | ±3–4 % | 30–55 ms | 4–5 | Frequent, still the player's main feedback |
| Signature weapons (sniper, rocket, legendary) | 0.82–0.95 | ±1–2 % | 120–150 ms | 2–3 | Must stay recognisable; barely varied |
| Telegraphs | 0.66–0.86 | ±0–3 % | 60–120 ms | 2–4 | Legibility first: boss telegraph is not varied at all |
| Pickups / drops | 0.32–0.62 | ±2–6 % | 55–100 ms | 2–4 | Arrive in bursts |
| UI | 0.28–0.55 | ±1–3 % | 45–120 ms | 2 | Never the loudest thing on screen |
| Critical (downed, death, overheat, failure) | 0.50–0.95 | none | 250–500 ms | 1 | A warning that wobbles is a worse warning |
| Loops (revive channel, blaster heat) | 0.34–0.40 | none | n/a | 1 | A randomised loop drifts against itself |

**Remaining repetition, stated honestly:** every event still has exactly one clip. Pitch variation and interval
throttling reduce machine-gun sameness but cannot replace clip variety. Adding two or three alternates for
`enemy.hit`, `weapon.fire.smg`, `weapon.melee.knife_slash` and `loot.pickup.coins` is external content work and is
listed in section 26.

## 8. Music / Ambience Loop Handling

**Implemented — and this was a real defect, not a polish item.**

`GameAudioFactory.BuildMusic` allocates `new Clip(dur + 1.2f)` so the last notes can ring out, then called
`MakeSeamless(0.8f)`. The file was therefore **1.2 s longer than its own 8-bar grid**: every repetition slipped off the
beat by 1.2 s, and the joint passed through a 2-second dip because the tail decays to silence and the head starts from it.

The fix is a new `AudioSynth.Clip.WrapTailToLoop(loopSeconds)`: the samples past the grid are added back onto the head
(so the ring-out lands over the next repetition's downbeat, which is what continuous play would sound like) and the
buffer is trimmed to exactly the grid, followed by a short 0.25 s seamless crossfade.

| Track class | Before | After | Grid | Fade-in to half peak |
|---|---:|---:|---:|---|
| Exploration / menu / Shelter (72 bpm) | 27.87 s | **26.667 s** | 26.667 s | 3.48 s → **0.24 s** |
| Combat (108 bpm) | 18.98 s | **17.778 s** | 17.778 s | 0.56 s → **0.28 s** |
| Boss (126 bpm) | 16.44 s | **15.238 s** | 15.238 s | 0.95 s → **0.48 s** |

Regenerated through a new batch entry, `AudioIntegration.GenerateMusicBatch` (menu: *RuinRail → Art → Regenerate Music
Loops*), which touches only the 11 music files — regenerating everything would rewrite 62 byte-identical clips.
The `MusicDirector` state machine, crossfades and stinger handling are untouched.

**Ambience is stale and was not changed:** `BuildAmbience` already writes exactly 12.000 s with `MakeSeamless(1.2f)`
and measures a 0.0000 edge discontinuity.

**Honest limitation:** loop *lengths* are unchanged — 26.7 s exploration, 17.8 s combat, 15.2 s boss, 12 s ambience.
They are now continuous and on the beat, but a long session still hears the same bar. Longer-form music is external
composition work (section 26). **No soundtrack was rewritten.**

**Artefact:** `music_loop_alignment.csv`.

## 9. Shelter Trader Presentation

**Implemented.** New `Assets/Game/Scripts/UI/Base/ShelterTraderPresentation.cs` plus a counter layout in
`BaseHubScreen`.

Every fact needed was already on the offer — `TraderOffer.Item` is a rolled `ItemInstance` — so nothing new was
computed. The counter maps an offer onto the merchant's own `MerchantRow` and reuses `MerchantRowView`,
`ItemTooltip.Build`, `TooltipComparison.Compare`, `ItemDetailLayout.Compose/Render` and `DetailPager`.

| Required | Delivered |
|---|---|
| Item icon | `MerchantRowView` icon slot |
| Item name | Row label, fitted to the row width |
| Rarity | Rarity frame sprite + rarity word in the subtitle |
| Category | Subtitle: `RARE · WEAPON` |
| Price | Right-aligned amber price on the row |
| Owned/equipped state | Row is annotated `EQUIPPED` when the same definition is already worn |
| Details pane | Description, Legendary line, stats, affixes — the inventory's own layout, paged |
| Useful stat rows | From `ItemTooltip.Build` |
| Affix lines | From `ItemTooltip.Build` |
| Comparison to equipped | `ShelterTraderPresentation.CompareFor` uses the same slot rule as the Dungeon Merchant |
| Buy/sell affordance | Unchanged focus list (`trader.buy.N`, `trader.sell`); a row the player cannot afford is annotated `NO COINS` |
| Mouse / keyboard / controller | One focus list drives all three, as before; a `FocusWindow` scrolls the offers by focus, never a scrollbar |

**Identical-name ambiguity is fixed:** two offers of one definition now differ by rarity frame, rarity word, price and
the full details pane.

**Truncation is fixed:** the old data row had no `UiText.Fit` call at all; every row string and every detail line is
now fitted to its measured width.

**Layout:** the Trader is the one station whose controls *are* its data, and a 147 px half-column cannot hold an icon,
a name, a rarity/category line and a price. It gets the full 302 px panel width — four offer rows over a rule over the
details pane — instead of the two-column split the other stations use. Banked Coins were already a permanent row in
the left column, so nothing was lost by the data column going away.

**Not changed:** prices, offer generation, `TraderService.Buy`/`Sell`, transaction authority, the focus-list ids, and
the navigation contract (`UiNavigationTests` still asserts the same two ids).

**Artefact:** `shelter_trader_matrix.csv` — 72/72 catalog items still bound and 72/72 icons still present; two
same-definition offers proven distinguishable by rarity, price and detail rows.

## 10. Status-Effect Display

**Implemented**, and deliberately small because the runtime is small.

Re-verification first: the only timed effects that exist on the player at runtime are the three authored timed-buff
consumables. There is **no runtime timed debuff on the player** in this build, so no negative chip was invented.

| Effect | Duration | What it does |
|---|---:|---|
| Armor Injector | 10 s | General Damage Reduction +20 % (and raises the Armor Injector cap) |
| Combat Stim | 8 s | Movement Speed +20 % |
| Damage Stim | 10 s | Weapon Damage +20 % |

`ConsumableEffectRunner` already exposed `ActiveBuffDefinitionIds` and `RemainingSeconds(id)`, so the HUD **reads the
authority that owns the countdown**. There is exactly one timer per effect and it is not in the UI: a chip cannot
outlive its effect or disagree with it.

**Presentation:** up to four 16 px chips, right-aligned in a strip directly above the bottom-right consumable slot —
beside the slot that granted them, in space no other HUD block uses. Each chip is the consumable's own icon over a
vertical "time left" fill with a coloured frame (green for a buff, red reserved for a debuff if one is ever authored).
One detail line above the strip names the longest-running effect, its stat change and its seconds.

Stack counts are **not** shown, because buffs refresh rather than stack (`ApplyBuff` sets `Remaining`, it does not add);
showing a stack count would be a lie about the rule.

**Preserved:** a PlayMode test asserts the strip's world rect does not overlap the minimap, coins, boss bar, dash icon,
HP block, weapon slots, consumable slot, room title or notice line. The biome label, enemy-remaining chip and low-HP
vignette are untouched.

**No stale UI:** a defect found by the first test run — the detail line was hidden but not cleared on expiry — was
fixed in `DungeonHudView.Render`, so the text itself is emptied, not just deactivated.

**Artefact:** `status_effect_matrix.csv` — activation, chip shown, icon present, fill at start, and cleared-on-expiry
for every timed effect.

## 11. Pickup Attraction

**Implemented**, and it closed a dead seam: `PickupAttractor._baseRadiusTiles` defaulted to 0 and `SetBaseRadius` had
**no caller anywhere**, so only the Magnetic Coil gave any attraction at all.

| | Before | After |
|---|---:|---:|
| Baseline reach | 0.00 tiles | **1.25 tiles** |
| With Magnetic Coil (+3 flat) | 3.00 tiles | **4.25 tiles** |
| Coil as a multiple of baseline | ∞ (0 → 3) | **3.4×** |

1.25 tiles is about one tile of reach past the body: walk over or brush past a coin or ammo pile and it comes to you;
a pile 3.5 tiles away does not move without the Coil, and a pile 8 tiles away never moves at all. The value is capped
in code at `MaxBaseRadiusTiles = 2` and the validator fails above it, so it cannot creep into a room vacuum.

**Eligibility is unchanged and already correct:** only `CoinPickup` and ammo `WorldItemPickup`s implement
`IAttractablePickup`. Chests, merchants, event objects, doors, weapon caches and every large choice interactable are
not attractable at all — equipment is not even moved.

**Through-wall attraction is now impossible.** Acquisition is a circle overlap, so a coin behind a one-tile wall was
inside the radius and would have been dragged into it. A new `IsObstructed` line check refuses any pickup with an
`EnvironmentObstacle` between it and the player — the same thing a projectile treats as wall. This also closes the
same hole on the Coil's reach, where it already existed. Room Sweep (`Pull`/`SweepAll`) deliberately still bypasses
both radius and obstruction, which is what that Legendary mechanic is.

No capacity bypass and no duplication: collection still goes through each pickup's own `Interact`.

**A defect this pass found in its own work, and fixed at the cause.** With a full backpack, a stack that could not be
taken was still *pulled*: attraction dragged it onto the player, collection failed, and it then sat at their feet as
the nearest interactable — silently taking the interaction prompt away from the chest, weapon cache or event object
they were standing at. The built-player smoke caught it twice. The fix is a new
`IAttractablePickup.CanBeCollectedBy(interactor)`, which asks whether the pickup could actually be taken *right now*,
capacity included (`CoinPickup`: always, coins have no cap; `WorldItemPickup`: the backpack must have room for the
stack). Attraction now acquires only what it can collect, and releases anything that stops being takeable mid-pull, so
an untakeable stack stays exactly where it fell. Room Sweep is unaffected in radius and line of sight, and gains the
same capacity rule.

**Artefact:** `pickup_attraction_matrix.csv` — near (1.0 tiles, collected), mid (3.5, ignored on the baseline and
collected with the Coil), far (8.0, ignored either way), behind a wall (2.0, refused), and backpack-full (0.8, not
even pulled).

## 12. Grenade Quick-Use

**Implemented**, and this closed the larger of the two dead seams.

`PlayerRigComposer` called `consumables.Configure(..., launcher: null, ...)`, so `ConsumableTargets.ThrowGrenade` was
null, `ConsumableEffectRunner.CanThrowGrenades` was false, and `ConsumableUseAction.TryUse` refused every grenade with
`UnsupportedEffect`. **All four authored grenades could be looted, priced and equipped but never thrown.** The player
rig now composes a `GrenadeLauncher`.

On top of that:

| Requirement | How |
|---|---|
| Dedicated action | `QuickGrenade` in the Player map — `Q` (keyboard) / `LB` (gamepad) |
| Rebindable | Added to `InputRebinder.ActionLabels` as "Quick Grenade"; it appears in Settings → CONTROLS |
| First usable grenade by existing rules | `ConsumableUseAction.FindQuickGrenade()`: the Active Consumable if it already holds grenades, then the backpack left to right |
| Existing authoritative service | A second entry point (`TryUse(ItemInstance)`) on the *same* channel — same cooldown, same channel, same effect runner, same throw rules. No second consumable system. |
| Decrements exactly once | The shared `ConsumeOne`; an emptied backpack stack now leaves the backpack the same way an emptied equipped stack leaves its slot |
| Safe no-op when none | `FindQuickGrenade` returns null and nothing happens, nothing is spent |
| Mouse/keyboard and controller | Both bound; the action is device-agnostic |
| Blocked by every gate | `PlayerInputReader.OnQuickGrenade` raises only when `GameplayAllowed` — inventory, merchant, pause, event modal and any other `GameplayInputGate` hold all suppress it, exactly like Fire and the Consumable key |

The Active Consumable slot is **not** changed by a quick-use, and save/load is unaffected (no new persisted state).

**Artefact:** `grenade_quick_use_matrix.csv`.

## 13. Aim-Assist Setting

**Implemented.** Current strength is unchanged: cone half-angles (18° mouse, 24° controller), the sniper ×0.75 and
rocket ×0.65 class multipliers, the scoring and the melee-is-zero rule are all untouched.

- `SettingsData.Accessibility.AimAssist`, **default ON**.
- Published through the same path as the other preferences into a new `AssistPreferences` static (the shape
  `FeedbackPreferences` already uses), from `Bootstrap`, `Apply`, `Discard` and `ResetToDefaults`.
- Read in exactly one place: `ShotSolver.Solve`. Every weapon that fires goes through it, so there is no second
  assist path and nothing to miss.
- OFF: the shot leaves on the raw aim, no target is selected, no proximity assist. ON: byte-identical to before.
- The row toggles live (like the audio rows) so the difference is audible-equivalent — feelable — while adjusting;
  DISCARD and Back restore the persisted value.

Measured in the PlayMode suite with a target inside the cone: **ON bends 11.31° onto the target, OFF bends 0.00° and
selects no target, ON again reproduces 11.31° exactly.**

Backward compatibility: `JsonUtility` leaves a field a document does not mention at its initializer, so an older
settings file without the key loads with aim assist **ON** and its other values intact — asserted with a hand-written
older document in the EditMode suite.

**Artefact:** `aim_assist_setting_matrix.csv`.

## 14. Low-Ammo Prompt

**Implemented** through the existing contextual prompt system — `TutorialPromptId.LowAmmo`, the ninth prompt.

Copy: *"Ammo running low — Mouse Wheel: use both weapons to conserve rounds."* (the control token resolves to the
current device and to any rebind). It teaches resource management. It does **not** say melee, and the validator fails
if that word ever appears in it.

Trigger, in `ExpeditionTutorialBinder.ObserveAmmoReserve()`, all four conditions required:

1. the weapon in hand is a firearm;
2. the player has actually been firing (so it cannot fire at spawn);
3. both weapon slots are filled, so the advice is something they can act on right now;
4. magazine + reserve for that weapon is at or below **two magazines** (`LowAmmoMagazineThreshold = 2`).

Two magazines is deliberately early — with the starter P9 that is 24 rounds, while there is still ammo to spend and a
decision to make, rather than in the boss fight where the lesson arrives too late.

No spam and no UI lock: `TutorialPromptService` shows one prompt at a time, queues the rest, marks it seen through
`ITutorialProgress` (the gameplay save) so it never returns, and is silent when tutorial prompts are off in Settings.
Swapping weapons completes it. **No ammo value, drop or cap changed.**

**Artefact:** `ammo_prompt_matrix.csv` — queued behind an active prompt, promoted when the band frees, never
re-triggered after completion, silent when disabled, and the threshold arithmetic.

## 15. Help / Codex

**Implemented.** `CodexContent` (the text, Unity-free and EditMode-testable), `CodexViewModel` (which section, how far
down) and `CodexPanel` (the panel, built with `UiKit`/`UiControl`/`FocusList` exactly like `SettingsPanel`).

Eight sections, in page order: **CORE RUN RULES, LOADOUT, AMMO, RARITY AND AFFIXES, COMBAT, CO-OP AND DOWNED,
CONTROLS, SETTINGS.** It is a manual, not an encyclopedia — no lore.

Numbers are **read from the code that enforces them**, not retyped: backpack capacity from
`PlayerInventory.BackpackCapacity`, ammo caps from the live `AmmoBalanceConfig` (180/120/60/40), control names from the
live rebinder's glyphs. The EditMode suite asserts each of those against the source of truth, so the page cannot drift
from the build.

**Honesty about co-op:** the page says co-op is built for up to 3 players and driven from the Multiplayer Terminal
(Solo / Host Co-op / Join by Code, private sessions, no matchmaking), and states plainly that online sessions require
Unity Services to be configured for the build and that the terminal reports the error when they are not — which is the
current state (no cloud project is configured). It does not claim live co-op works. Downed/bleedout/revive **are**
real and are described as such.

**Access:** Main Menu → **HELP** (a new entry between SETTINGS and QUIT) and Pause → **HELP** (between SETTINGS and the
two destructive entries). Both open the same panel over the same view model.

Navigation: focus walks the eight section rows plus BACK; long pages page with the mouse wheel, PageUp/PageDown or the
right stick — the same `DetailPagingInput` the inventory and merchant use, so all three devices reach every line. Back
(Esc / B) leaves. Text is fitted, and the EditMode suite proves every line of every section is reachable by paging.

Uses the accepted `UiFont` and `UiSkin`; the validator fails if the UI face ever falls back to a Unity built-in.

**Artefact:** `codex_matrix.csv`.

## 16. HUD / Bossbar / Readability

Old findings were re-measured first; only what was still real was changed.

### A. HUD top-band crowding — **STALE, not changed**

Measured from the HUD's own geometry: minimap 6–106, boss bar 280–500, coins 572–634 across the top band, with 174 px
and 72 px of empty space between them; the room-title reveal sits at y 28, below the boss band's 23 px bottom; the
biome line and party lines do not share pixels; the enemy chip sits clear of the coins. **No overlap exists.** A
regression test now pins those relationships so the band cannot silently crowd later.

### B. Boss-bar black rectangle — **fixed at the source**

The bar's backing was a plain `Image` with **no sprite** at `rgba(0.12, 0.12, 0.12, 0.9)`, with no frame and no plate
behind the name — a bare dark quad dropped on the HUD. It now gets the HP block's treatment: one translucent
`NearBlack` theme plate behind the whole block (as the *first* sibling — nothing is laid over it to hide the old
rectangle) and a `PanelEdgeSoft` border around the bar, so the empty portion reads as the bar's track.

### C. Shelter / Main Menu secondary text contrast — **fixed**

`InkFaint #68736F` on `Charcoal` measures **3.34 : 1**, below the 4.5 : 1 threshold for body text. The five
*informational* sites — the Shelter's contextual help line, the AT RISK block, the Shelter footer hint, the onboarding
line and the two Main Menu detail blocks — now use `InkMuted #8E9894` at **5.53 : 1**.

The `InkFaint` token itself is **unchanged**, because it still means something: SOLD rows, an already-chosen cache
option and an empty slot are *meant* to be quieter than the live rows. Main Menu identity, layout, wordmark and
backdrop are untouched.

### D. Damage numbers — **fixed**

They were plain white `TextMesh` with no outline, shadow or plate, so a number landing on a lit floor tile or a muzzle
flash vanished exactly when it mattered. Each number now draws a near-black copy one reference pixel down-right, one
sorting order behind it. One extra quad, readable at any background value, pixel feel preserved — no blur, no
resampling, no translucent box in the middle of the fight.

### E. Doors — **STALE, not changed**

Each biome already ships an open sprite (clear doorway + system lamp) and a locked sprite (a shutter filling the
doorway + lock lamp), and `RoomDoorLock` changes the sprite and the collider together. Residual, documented not fixed:
Metro's open lamp `#8A6329` and lock lamp `#C06434` are close in hue — but the shutter, not the lamp, carries the
state, and changing a biome's authored lamp colour is biome art.

### F. Trader truncation — **fixed** (see section 9)

**Artefact:** `readability_matrix.csv` — contrast ratios with thresholds, the three validated window shapes (640×360
reference, 16:9 at 3×, and a non-16:9 window where `ScreenMatchMode.Expand` keeps the 640×360 frame and adds margin
rather than cropping), the Main Menu action column's new four-control arithmetic, and the damage-number and boss-bar
changes.

## 17. Prop Dressing

**Implemented** as `RoomPropDressing`, applied per room in `DungeonRoomRuntimeComposer.Attach` before anything is
spawned or bound. **Not a room-art redesign** — no new art, no geometry change.

Every prop in the game is baked into its prefab's `FloorDetail` tilemap, so the same room id looked pixel-identical on
every visit, at a density of 1–8 detail tiles against a 146–148 tile small-room floor (under 6 %). The pass varies what
is already there:

- **Flip / rotate** each authored detail tile (6 variants) — a flipped crate is the same crate in the same cell. Safe
  because the tilemap anchor is (0.5, 0.5) and the tile sprites are centre-pivoted 32 × 32, so a variant stays in its cell.
- **Add** a few more copies of the tiles that room already uses, on plain floor, capped at 4 % of the room's floor
  cells (minimum 2), and never adjacent to existing detail — a cluster of identical props is the repetition being fixed.

Why it cannot affect gameplay, by rule rather than by hope:

| Rule | Guarantee |
|---|---|
| Writes only to `FloorDetail` | Which is neither `BlocksMovement` nor `IsTrigger`; the layer carries no collider (asserted) |
| Never into geometry, a hazard or a foreground piece | A cell is eligible only if `Floor` has a tile and `Walls`, `Obstacles`, `Hazards` and `AbovePlayer` do not |
| Never obstructs a doorway | Every cell within 2 tiles of any door socket is excluded |
| Never covers a spawn, chest, boss anchor or interactable | Every `RoomMarker` cell **and its eight neighbours** are excluded |
| Never hides a telegraph | Density cap of 4 % of floor; the validator fails above 10 % |
| Deterministic | Seed is (run seed, depth, node id) through the Biome RNG stream — the same depth of the same run dresses identically; two instances of one room id in one layout differ |

**Artefact:** `prop_dressing_matrix.csv` — per room: pattern count, authored detail, added detail, blocked doors (0),
blocked markers (0), navigation regressions (0), and determinism.

## 18. Validator / Contract Changes

**New: `PresentationAudioUxQolValidator`** (`RuinRail/Production/Validate Presentation / Audio / UX-QoL`, report at
`TestResults/presentation_audio_ux_qol.md`). No existing validator was weakened.

Every rule guards something this pass could quietly lose later:

| Rule | What it fails on |
|---|---|
| substrate (per biome) | a substrate that is not clearly quieter than its floor, a ramp with no texture, a raw-black clear colour |
| substrate (non-traversable) | a non-negative sorting order, a `Collider2D` field on the component, a margin smaller than the camera's reach |
| audio mix | any event at full gain, any event below the mix floor, an extreme (>12×) gain spread, zero events |
| repetition policy (22 high-frequency events) | one clip and no pitch range, no minimum interval, an instance cap above 6 |
| loop policy | a pitch-randomised loop, a loop not capped at one instance |
| pickup attraction | base attraction off, base past the declared bound, the Coil granting nothing, the Coil no longer beating the base |
| status display | a timed buff with no duration, icon or name; no timed effect at all; more effects than chips |
| input action (14 actions) | any gameplay action missing, including `QuickGrenade` |
| grenade quick-use | no `GrenadeLauncher` composed on the player, no quick-grenade entry point, a press not gated by `GameplayInputGate` |
| aim assist | `ShotSolver` not reading the setting, the settings layer not publishing it, a default that is not ON |
| low-ammo prompt | the prompt missing, no copy, or copy that tells the player to use melee |
| codex section (×8) | a missing section, an empty section, a section with no title |
| codex facts | the loadout page not stating the real backpack capacity, the combat page not mentioning aim assist |
| glyph (×14) | an action with no glyph text (it would fall through to its own enum name) |
| font | no UI font, or a Unity built-in placeholder face |
| audio listener | more than one place adding an `AudioListener`, or the dungeon camera adding one |
| shelter trader | the counter not using `MerchantRowView`, having no details pane, or not comparing against the equipped item |
| prop dressing | writing outside `FloorDetail`, no door clearance, a density cap high enough to hide a telegraph |

**Proven to fail, not assumed to work.** Five deliberately broken fixtures are fed to `Validate(...)` in the EditMode
suite and each is asserted to fail the right rule:

1. every high-frequency event reset to gain 1.0 / pitch 1.0 / no throttle → the mix **and** repetition rules fail;
2. a base pickup radius past `MaxBaseRadiusTiles` → the attraction rule fails;
3. a base pickup radius equal to the Coil's +3 → the attraction rule fails (the accessory stops being stronger);
4. a timed buff with no duration, icon or name → the status-display rule fails;
5. the same validator over the shipped content → passes.

**Also new:** `PresentationFrozenSystemsTests`, which re-asserts every frozen value and writes
`frozen_systems_check.csv`; and a regression test pinning the HUD top-band geometry so the "crowding" finding cannot
quietly become true again.

**Extended, additively:** `FocusWindow` gained an optional `itemCount`, so a panel can window part of a focus list while
the rest are permanent controls (the counter's SELL row sits under a scrolling offer list). Default behaviour is
unchanged. `MerchantRowView` gained `Annotate(note, colour)`, which the Dungeon Merchant does not call.

## 19. Settings Compatibility

**Aim Assist is the only new setting**, and it went into the existing architecture — no second settings framework:

- stored in `SettingsData.Accessibility` (the settings document, never the gameplay save);
- rendered as a `SettingsRow` on the existing **GAMEPLAY** page, at the top;
- published through `SettingsViewModel.PublishFeedback` alongside the other preferences;
- carried by `Copy`, so APPLY / DISCARD / RESET all behave like every other row.

The fixed category set is unchanged and asserted: **VIDEO, AUDIO, CONTROLS, GAMEPLAY**. The GAMEPLAY page's row order
is asserted exactly — `aim_assist`, `shake`, `shake.intensity`, `damage_numbers`, `hit_flash`, `tutorials`,
`tutorials.reset` — one row gained, none lost.

Re-verified working: Master / Music / SFX / Ambience volume, mute, display mode, resolution, VSync, frame-rate limit,
input rebinding (both schemes), screen shake and intensity, damage numbers, hit flash, tutorial prompts, reset tutorials.

**Older documents get safe defaults, not a reset.** `JsonUtility` leaves a field a document does not mention at its
initializer, so a settings file written before this pass loads with `AimAssist = true` and every value it *does* contain
intact. Asserted with a hand-written older JSON document carrying non-default volumes and accessibility values: aim
assist arrives ON, master volume stays 0.4, shake intensity stays 0.5, damage numbers stay off.

## 20. Frozen-System Verification

`TestResults/PresentationAudioUxQol/frozen_systems_check.csv`, written by `PresentationFrozenSystemsTests` from the
shipped data. Each row records the frozen value, the observed value and which existing suite owns it.

| System | Frozen value | Result |
|---|---|---|
| D1 ammo tuning (supply-chest Light rounds) | 26–46 | UNCHANGED |
| Field Knife damage / rate / class | 14–17, 3.5, Knife | UNCHANGED |
| Blaster cooling delay (×3 blasters) | 0.5 s | UNCHANGED |
| Blaster overheat lockout (×3 blasters) | 1.9 s | UNCHANGED |
| Weapon count | 33 | UNCHANGED |
| All weapon base damage / rate | digest recorded for future diffing | RECORDED |
| Ammo stack caps | 180 / 120 / 60 / 40 | UNCHANGED |
| Enemy health & damage scaling D1–D50 | digests recorded | RECORDED |
| Deep-depth reward start | depth 30 | UNCHANGED |
| Reward multiplier at D1 and D30 | 1.00 | UNCHANGED |
| Reward multiplier cap | 1.75 | UNCHANGED |
| Elite chance D1/5/10/20/30 | 8 / 18 / 25 / 25 / 25 | UNCHANGED |
| Depth-gated rooms | 9 | UNCHANGED |
| Rooms per biome | 21 each | UNCHANGED |
| Biome encounter weighting | 14 authored deviations, per-biome sets recorded | UNCHANGED |
| Max level | 61 | UNCHANGED |
| Deepest depth starts at | 0 | UNCHANGED |
| Starter kit | P9 Ranger, Field Knife, Bandage | UNCHANGED |
| Base max health | 100 | UNCHANGED |
| Downed bleedout / revive percent | 20 s / 30 % | UNCHANGED |
| Depth-arrival health rule | fill to effective max | UNCHANGED |

Section 21 reports whether the gate actually passed.

The wider frozen list (`PlayerStats`, `AffixRegistry`, `WeaponStatMath`, StatConsumerIntegrity, the graphical inventory,
Dungeon Merchant behaviour, non-combat rooms, progression, save/persistence, death/extraction, `EncounterBounds`, room
locks, minimap logic, character/weapon art, VFX, multiplayer architecture) is owned by the existing suites, which were
run in full — see section 21. Three of those suites needed **test** updates because this pass deliberately changed the
behaviour they pinned; each is listed there with its reason, and no assertion was weakened to get green.

## 21. Automated Tests

All gates were run with the repository harness (`./scripts/run-unity-tests.sh`). PlayMode runs before EditMode, because
`FinalMvpAuditTests` reads `TestResults/PlayMode-results.xml`.

| Gate | Result |
|---|---|
| **PlayMode** (full) | **798 / 798 passed**, 0 failed |
| **EditMode** (full) | **972 passed, 0 failed, 2 skipped** |
| `PresentationAudioUxQolValidator` | **PASS** — 78 rules checked, 78 clean, 0 with problems |
| `FinalProductionValidator` | PASS (`FinalValidationTests`) |
| `ContentCountValidator` | PASS (`ContentCountTests`) — 72 items, 33 weapons, 63 rooms unchanged |
| `StatConsumerIntegrityValidator` | PASS (`StatConsumerIntegrityTests`, 24 tests) |
| `RunVarietyDepthRetentionValidator` | PASS (25 rules) |
| Save / migration validators | PASS (`SaveMigrationTests`, `SaveSlotTests`, `AutosaveTests`) |
| Settings tests | PASS (`SettingsPagesTests`, `PauseSettingsRebindingTests`, `SettingsPersistenceTests`) |
| UI / navigation tests | PASS (`UiNavigationTests`, `BaseUiTests`, `PauseScreenInteractionTests`) |
| Audio runtime tests | PASS (`AudioServiceTests`, `MusicDirectorTests`, `DungeonAudioProofTests`, `AudioAssetAuditTests`) |
| Inventory / merchant tests | PASS (`GraphicalInventoryProofTests`, `MerchantViewTests`, `MerchantProofTests`) |
| Combat smoke | PASS (`CombatAimCollisionProofTests` — see section 24) |
| Generation smoke | PASS (`DungeonRegressionProofTests`, the three room-set suites, `CrossBiomeRunTests`) |
| Run-variety / depth / boss regression | PASS (`BossSelectionAssert` suites, `DeepestDepthTests`, `EconomyTests`) |
| Frozen-systems freeze | PASS (`PresentationFrozenSystemsTests`) |

**The 2 skipped EditMode tests are pre-existing `[Explicit]` / environment-gated cases, not this pass's:**

| Skipped | Why |
|---|---|
| `D1AmmoBlasterFineTuneTests.CalibrateLightAmmoQuantity` | The `[Explicit]` calibration sweep from the D1 ammo pass; it is a tool, run on demand, not a gate. |
| `MultiplayerTerminalTests.LiveSessionsRelay_IntegrationCheck_OrNotRun` | Requires live Unity Services, which are not configured. Reports NOT RUN by design. |

### First-run failures and what each one actually was

The first full run surfaced 10 PlayMode and 7 EditMode failures. Every one had a cause; none was silenced.

| Failure | Cause | Resolution |
|---|---|---|
| `StatusChips…ClearOnExpiry` | **Product bug in this pass:** the status detail line was deactivated but not cleared on expiry, so it still held an expired buff's name | Fixed in `DungeonHudView.Render` — the text is emptied |
| `LootAmmoAudioProofTests` | **Real consequence of the new baseline reach:** an ammo pickup was auto-collected before the test interacted with it, and the visual assert did not tolerate a collected pickup | Test updated to skip collected pickups; behaviour is correct |
| `WorldPickupTests.MagneticCoil…` | Asserted 0 baseline attraction — the behaviour this pass deliberately changes | Now asserts the baseline, the additive Coil, and that the Coil is still >2× the baseline |
| `PauseMenuTests`, `PauseScreenInteractionTests` ×2, `UiNavigationTests`, `BaseUiTests` | Pinned the pause root at four entries and the Main Menu at three | Updated to five and four with HELP; step counts adjusted |
| `PauseSettingsRebindingTests` ×3 | Hard-coded 13 actions / 16 keyboard bindings / 13 gamepad bindings | Updated to 14 / 17 / 14, with `ApprovedActions.Length` used where a literal was redundant |
| `PauseSettingsRebindingTests` (rebind) | The test rebound gamepad Dash onto LB, which is now Quick Grenade's default | Target moved to D-pad up, **and** a new assertion added proving the LB conflict is reported with the right action |
| `SettingsPagesTests` | Pinned the GAMEPLAY row set | Updated to include `settings.aim_assist` |
| `FinalMvpAuditTests` | The audit hard-codes the action count | Updated to 14 with a comment naming why |
| `PresentationAudioUxQolTests` (validator) | **Bug in my own validator:** its `AddComponent<AudioListener>` search counted its own source file | Fixed to search runtime code only and to require exactly one site |
| `PresentationFrozenSystemsTests` | **My own miscount:** I wrote 14 authored biome weights; there are 12 | Corrected to 12 |
| `PresentationAudioUxQolRuntimeTests` (pickup) | **My own test bug:** the "far" coin was at 6 tiles, past the Coil's 4.25-tile reach | Distances corrected to 1.0 / 3.5 / 8.0, which now also proves the Coil does not vacuum |
| `PresentationAudioUxQolRuntimeTests` (grenade) | **My own test bug:** a stackable add merges into a container-owned stack and zeroes the source instance | The live stack is read back from the backpack |
| `PresentationAudioUxQolRuntimeTests` (props) | **My own test bug:** `root.gameObject` read after `DestroyImmediate(root.gameObject)` | The GameObject is captured before destroying |
| `CombatAimCollisionProofTests` | Known pre-existing flake — section 24 | Passed on re-run with no change to it or to `ShotSolver` |

Two product bugs, one validator bug, three of my own test bugs, eight expectation updates for behaviour this pass
deliberately changed, and one known flake. **No assertion was weakened to get green.**

### Side effect worth naming

The EditMode run regenerates `production/FINAL_MVP_COMPLETION_REPORT.md` (the audit writes it). On this machine its
build and smoke lines read NOT RUN because the audit runs in `-nographics` batch mode. That file's regeneration is a
harness side effect, not an edit from this pass, and it was left as the run produced it rather than reverted with a
destructive git operation.

## 22. Built-Player Evidence

### Build

| Target | Result |
|---|---|
| **Windows x64** | `Windows x64: NOT RUN — module unavailable` (no Windows Build Support module on this macOS machine; nothing was installed) |
| **macOS (StandaloneOSX)** | non-development build, approved scene order, `BuildOptions.None` — the repository's documented verification substitute (`ReleaseBuildTool.BuildMacBatch`) |

The release target remains Windows x64. The macOS build exists so the built-player smoke can run here at all.

### Built-player smoke

A new smoke stage, `SmokeRunner.PresentationQolChecks`, runs inside the shipped player on the first depth and checks
each seam this pass created or found dead:

1. the depth has an environmental underlay with art;
2. it is on the Ground layer below the floor;
3. it carries no collider;
4. the dungeon camera no longer clears to raw black (and clears to that biome's colour);
5. the underlay covers everything the clamped camera can see;
6. the underlay is quieter than the biome floor;
7. the depth's rooms were dressed deterministically;
8. no shipped audio event is at full gain;
9. the most repeated cues ship with pitch variation and a throttle;
10. the process holds exactly one `AudioListener`;
11. no status chip is on screen before anything is active;
12. using a timed buff shows its chip with time left;
13. the status detail line names the active effect;
14. the chip clears the moment the effect expires;
15. the survivor has a baseline pickup reach inside its declared bound;
16. the Magnetic Coil is still meaningfully stronger;
17. a coin the player walks over comes to them;
18. a coin across the room is left where it is;
19. the player rig composes a grenade launcher and `CanThrowGrenades` is true;
20. quick-grenade throws a backpack grenade without touching the Active Consumable slot;
21. quick-grenade spent exactly one;
22. the quick-grenade press is gated by the gameplay input gate;
23. quick-grenade with nothing to throw is a silent no-op;
24. with Aim Assist OFF a shot leaves on the raw aim and selects no target;
25. Aim Assist returns to its ON default;
26. the Help page ships all 8 sections with content;
27. the Help page's ammo caps are the ones the build enforces;
28. every Help line is reachable by paging.

The pre-existing smoke stages continue to cover the frozen behaviour in the same run: D1 ammo and the starter kit
(`LootAmmoStarterChecks`), combat and the Field Knife (`CombatChecks`), the HUD and Dungeon Merchant
(`MerchantChecks`, `RoomHudQolChecks`), inventory, weapon cache, non-combat rooms, progression, the deepest-depth
record and boss variety (`RunVarietyDepthChecks`), death and extraction, and save/reload.

### Smoke result

**PASS.** `TestResults/PresentationAudioUxQol/smoke/smoke_result.json` — `Success: true`, `Error: ""`, with **all 29
of this pass's checks green** in the shipped macOS player, alongside every pre-existing stage:

```
+ the depth has an environmental underlay
+ the underlay is on the Ground layer below the floor (order -1000)
+ the underlay carries no collider, so it cannot be walked on or shot
+ the dungeon camera no longer clears to raw black
+ the underlay covers everything the clamped camera can see
+ the underlay is quieter than the RuinedMetro floor
+ the depth's rooms were dressed deterministically (9 rooms, 23 extra detail tiles)
+ no shipped audio event is at full gain (53 events, loudest 0.95)
+ the most repeated cues ship with pitch variation and a throttle
+ the process still holds exactly one AudioListener
+ no status chip is on screen before anything is active
+ using Armor Injector shows its chip with time left
+ the status detail line names the active effect
+ the chip clears the moment the effect expires
+ the survivor has a baseline pickup reach of 1.25 tiles
+ the Magnetic Coil is still meaningfully stronger (1.25 -> 4.25 tiles)
+ a coin the player walks over comes to them
+ a coin across the room is left where it is (no room vacuum)
+ the player rig composes a grenade launcher, so grenades can be thrown at all
+ a grenade stack can be carried for the quick-use check
+ quick-grenade throws a backpack grenade without touching the Active Consumable slot
+ quick-grenade spent exactly one (1 left of 2)
+ the quick-grenade press is gated by the gameplay input gate
+ quick-grenade with nothing to throw is a silent no-op
+ with Aim Assist OFF a shot leaves on the raw aim and selects no target
+ Aim Assist is back ON and defaults ON
+ the Help page ships all 8 sections with content
+ the Help page's ammo caps are the ones the build enforces
+ every Help line is reachable by paging (8/8 sections)
```

### Visual proof

`TestResults/PresentationAudioUxQol/smoke/dungeon.png` — a live Rustworks depth from the shipped player. The frame is
filled by room and dressing with **no raw black anywhere**, the HUD reads cleanly (minimap, biome label, coins, HP,
both weapon slots, dash icon, tutorial band), and the room's dressing is visible without competing with the floor.
The earlier per-stage captures (`live_01…live_05`, `ecdp_*`) are written by the pre-existing stages in the same run.

### Audio diagnostics

`WriteAudioEvidence` runs in the same smoke and records which voices actually started, per event id, across the
Shelter, the dungeon, combat, the merchant window and the return to the menu. The mix checks above read the shipped
event assets from inside the running player, so the numbers are the built artefact's, not the editor's.

### Smoke stability on this machine — reported, not worked around

The smoke was run **16 times**. It is variable on this machine, and two of the five failure modes were genuinely
caused by this pass and were fixed at the cause:

| Failing stage | Runs | Cause | Action |
|---|---:|---|---|
| `loot: loot spawned as visible pickups` | 1 | **This pass:** the new baseline reach collects a chest's coins/ammo while the player stands at the chest, so the check's "still on the ground" assumption broke | Fixed: the loot checks now assert the loot *reached the player*, by attraction or by Interact, against a pre-open snapshot |
| `noncombat: WeaponCache prompt ''` | 2 | **This pass, a real defect:** a stack the backpack had no room for was dragged onto the player, failed to be collected, and then sat at their feet as the nearest interactable — silently taking the interaction prompt from the cache | Fixed at the cause: `IAttractablePickup.CanBeCollectedBy` makes attraction capacity-aware, so an untakeable stack is never pulled. Has not recurred. |
| `combat: direct crosshair-on-enemy projectile reduces HP` | 3 | Pre-existing; the built-player analogue of the documented `CombatAimCollisionProofTests` flake | None. Not caused by this pass: `IsSpawnClear` only tests `EnvironmentObstacle` colliders, and nothing this pass adds has a collider. |
| `econ/contain/proj` containment overshoot | 4 | Pre-existing tolerance sensitivity: observed 0.075 and 0.307 tiles against a 0.06 tolerance — a physics-settling margin that varies run to run | None. **The tolerance was not widened**: room containment is a frozen system, and loosening a gate to get green is not a fix. The PlayMode containment proof passed in every run. |
| `noncombat: non-combat rooms driven: ''` | 1 | Pre-existing seed variance: that run's generated depth had no non-combat room for the stage to drive | None; generation is unchanged and frozen-checked |

Clean end-to-end passes: **runs 4, 8 and 15**. Run 15 is the archived evidence — the first clean run after every fix
above, with all 29 checks.

## 23. Human Playtest Checklist

`TestResults/PresentationAudioUxQol/HUMAN_PLAYTEST_CHECKLIST.md` — **39 diagnostic checks** across seven groups (world
and space, audio, Shelter Trader, HUD and readability, QoL and controls, teaching and Help, rooms and regression).

Every line asks what was observed, not whether it was fun: *"Entering a small room, does the dungeon read as one
continuous underground space, or as a lit room floating in black?"*, *"After five minutes of continuous combat, are the
sounds less fatiguing — specifically the repeated ones?"*, *"Does any pickup ever come to you through a wall or a shut
door?"*, *"Turn Aim Assist back ON — does it feel exactly as it did before this pass?"*, *"Did anything you had already
accepted feel accidentally changed?"*

## 24. Known Pre-Existing Flakes

| Flake | Evidence | Why it is not this pass |
|---|---|---|
| `CombatAimCollisionProofTests.LiveRun_DirectHit_Assist_…` (PlayMode) | Failed on the first PlayMode run of this pass and passed on the next three with no change to it or to `ShotSolver` between them. A known, documented seed/clock sensitivity on this machine. | `AssistPreferences` defaults ON and is restored in `TearDown`; the only test that turns it off runs *after* this one alphabetically, so it cannot have leaked. |
| `combat: direct crosshair-on-enemy projectile reduces HP` (smoke) | 3 of 16 smoke runs. The built-player analogue of the same check. | The victim's placement uses `RoomRuntime.IsSpawnClear`, which tests only `EnvironmentObstacle` colliders. Nothing this pass adds has a collider — the substrate has none, and `FloorDetail` (the only layer prop dressing writes) has none. |
| `econ/contain/proj` containment overshoot (smoke) | 4 of 16 smoke runs, at 0.075 and 0.307 tiles against a 0.06 tolerance — magnitudes that vary by 4× between runs, which is settling variance rather than a systematic shift. | Nothing in this pass touches enemy movement, containment or colliders. The PlayMode containment proof (`EconomyContainmentDeathProjectileProofTests`) passed in **every** run. The tolerance was deliberately **not** widened: containment is a frozen system. |
| `noncombat: non-combat rooms driven: ''` (smoke) | 1 of 16 runs; that depth's seed produced no non-combat room to drive. | Generation is unchanged and frozen-checked (rooms per biome, depth gating, elite band all UNCHANGED). |

Two further smoke failure modes were **not** flakes — they were real consequences of this pass, and both were fixed at
the cause rather than tolerated. They are in section 22's table, and neither has recurred.

## 25. Files Changed

### Created (11)

| File | What |
|---|---|
| `Assets/Game/Scripts/Presentation/World/WorldSubstrate.cs` | The environmental underlay |
| `Assets/Game/Scripts/Core/Rendering/AssistPreferences.cs` | Aim-assist setting → runtime |
| `Assets/Game/Scripts/UI/Base/ShelterTraderPresentation.cs` | Offers as merchant rows + tooltip/comparison |
| `Assets/Game/Scripts/UI/Codex/CodexContent.cs` | The Help text (facts read from the code) |
| `Assets/Game/Scripts/UI/Codex/CodexViewModel.cs` | Section / paging state |
| `Assets/Game/Scripts/App/CodexPanel.cs` | The Help panel and its driver |
| `Assets/Game/Scripts/Dungeon/Runtime/RoomPropDressing.cs` | Deterministic dressing variation |
| `Assets/Game/Scripts/App/SmokeRunner.PresentationQol.cs` | Built-player smoke stage (28 checks) |
| `Assets/Game/Scripts/Editor/Production/PresentationAudioUxQolValidator.cs` | The contract gate |
| `Assets/Game/Tests/EditMode/PresentationAudioUxQolTests.cs` | Contracts, broken fixtures, matrices |
| `Assets/Game/Tests/EditMode/PresentationFrozenSystemsTests.cs` | The regression freeze |
| `Assets/Game/Tests/PlayMode/PresentationAudioUxQolRuntimeTests.cs` | Runtime proof |

### Modified — production code

| File | Change |
|---|---|
| `App/ExpeditionScene.cs` | Camera clear colour per biome; create the substrate per depth; bind the HUD's status effects |
| `App/PlayerRigComposer.cs` | Compose the missing `GrenadeLauncher`; expose `Consumables` |
| `App/BaseHubScreen.cs` | The Trader counter (rows + details + paging); five secondary-text contrast sites |
| `App/MainMenuScreen.cs` | HELP entry and panel; two contrast sites; status line moved clear of the fourth control |
| `App/PauseMenuScreen.cs` | HELP panel over the pause root |
| `Combat/Weapons/ShotSolver.cs` | One gate: the aim-assist setting |
| `Core/Input/PlayerInputReader.cs`, `IPlayerInputReader.cs`, `NullPlayerInputReader.cs` | `QuickGrenadeUsed`, gated like Fire |
| `Core/Input/RuinRailInputActions.cs`, `Settings/Input/RuinRailInputActions.inputactions` | The `QuickGrenade` action (Q / LB) |
| `Core/Input/InputRebinder.cs` | "Quick Grenade" label, so it is rebindable in CONTROLS |
| `Items/Consumables/ConsumableUseAction.cs` | `TryUse(ItemInstance)`, `FindQuickGrenade()`, `Discard` for backpack stacks |
| `Player/PlayerConsumableUser.cs` | `TryQuickGrenade()` on the same channel |
| `Player/PickupAttractor.cs` | 1.25-tile baseline, a declared bound, and the through-wall check |
| `Persistence/SettingsData.cs` | `Accessibility.AimAssist`, default ON |
| `UI/Settings/SettingsViewModel.cs` | The AIM ASSIST row, publish, copy, discard |
| `UI/Hud/DungeonHudViewModel.cs` | `HudStatusEffect`, `BindStatusEffects`, `RefreshStatusEffects` |
| `UI/Hud/DungeonHudView.cs` | The chip strip and detail line; the boss bar's plate and frame |
| `UI/Hud/HudInfoViews.cs` | `HudStatusChipView` |
| `UI/Merchant/MerchantView.cs` | `MerchantRowView.Annotate` (additive) |
| `UI/Theme/FocusWindow.cs` | Optional `itemCount` (additive, default unchanged) |
| `UI/Base/MainMenuViewModel.cs` | `Help` entry and state |
| `UI/Pause/PauseMenuViewModel.cs` | `Help` item, `Help` screen, `LeaveHelp`, back/close paths |
| `UI/Onboarding/TutorialPrompts.cs` | `LowAmmo` prompt + copy; `QuickGrenade` glyphs |
| `UI/Onboarding/ExpeditionTutorialBinder.cs` | The low-ammo context and its threshold |
| `Presentation/Vfx/DamageNumbers.cs` | The one-pixel shadow |
| `Dungeon/Runtime/DungeonRoomRuntimeComposer.cs`, `RoomRuntime.cs` | Apply and record the dressing |
| `Editor/ArtGen/AudioSynth.cs` | `Clip.WrapTailToLoop` |
| `Editor/ArtGen/AudioIntegration.cs` | `GenerateMusic` / `GenerateMusicBatch` |
| `App/SmokeRunner.cs` | The new stage and its result field |

### Modified — data

- **53** `Assets/Game/ScriptableObjects/Audio/Events/*.asset` — the authored mix (gain, pitch, interval, instances).
- **11** `Assets/Game/Audio/Music/*.wav` — regenerated on their bar grid.

### Modified — tests (existing suites this pass deliberately changed the behaviour of)

| File | Why |
|---|---|
| `PlayMode/WorldPickupTests.cs` | Asserted 0 baseline attraction; now asserts the baseline and that the Coil is still additive and stronger |
| `PlayMode/PauseMenuTests.cs`, `PlayMode/PauseScreenInteractionTests.cs` | Pinned a four-entry pause root; now five with HELP |
| `EditMode/BaseUiTests.cs` | Pinned a three-entry Main Menu; now four with HELP |
| `PlayMode/LootAmmoAudioProofTests.cs` | Assumed no pickup could be collected before it was interacted with |
| `PlayMode/FakePlayerInputReader.cs`, `EditMode/PauseMenuFlowTests.cs`, `EditMode/PauseSettingsRebindingTests.cs`, `Multiplayer/PlayerNetMotion.cs` | The new `QuickGrenadeUsed` member on `IPlayerInputReader` |

No assertion was weakened. Each change above updates what a test *expects* because the approved behaviour changed, and
adds a new assertion covering the new behaviour.

## 26. Deferred / External Content Needs

| Item | Why it is external, not deferred laziness |
|---|---|
| **2–3 alternate clips** for `enemy.hit`, `weapon.fire.smg`, `weapon.melee.knife_slash`, `loot.pickup.coins` | Every event ships one clip. Pitch and interval reduce sameness; only clip variety removes it. `AudioEventDefinition._clips` is already an array and `AudioService` already picks among them, so this is content, not code. |
| **Longer-form music** | Loops are now continuous and on the beat but are 15–27 s. Longer beds are composition work. The `MusicCatalog` slots are fixed at 11 tracks and need no change. |
| **A low-HP / critical warning audio event** | None exists. Adding one is a new authored cue plus a trigger, which is a feature rather than a mix decision, so it was not invented. |
| **Three authored substrate sheets** | The underlay is generated deterministically from the biome palette at runtime. `WorldSubstrate.SkinResolver` accepts authored art with no caller change; the generated tile is the documented fallback. |
| **Windows x64 build** | No Windows Build Support module on this machine. Not installed; reported as NOT RUN. |
| **Metro door lamp hue** | Metro's open and lock lamps are close in hue. The shutter carries the state unambiguously, so this was left alone rather than changing a biome's authored art in a presentation pass. |
| **Human playtest** | 39 checks written; the answers require a person at the controls. |

## 27. Final Status

### Implemented

| # | Success condition | Status |
|---:|---|---|
| 1 | Old review findings reverified against the current tree | 47 rows: 44 confirmed, 3 stale |
| 2 | All three biomes have a coherent non-walkable substrate | Ground layer, order −1000, no collider, per-biome palette |
| 3 | Small-room black-void presentation materially improved | 0 uncovered viewport tiles in all 15 matrix cases; clear colour no longer black |
| 4 | Audio events have an authored category-based mix | 53/53 authored, 0.28–0.95, 10 categories |
| 5 | Frequent repeated SFX have explicit variation/throttle policy | 22 high-frequency events: pitch range + interval + instance cap |
| 6 | Existing audio runtime/device/settings behaviour still correct | Listener, spatialiser, music state machine, watchdog, volumes all re-verified |
| 7 | Shelter Trader comparable to Dungeon Merchant quality | Same row primitive, same tooltip/comparison layout, same paging |
| 8 | Runtime timed effects have honest visible HUD feedback | 3 real effects, 3 chips, bound to the runner's own timers |
| 9 | Base pickup attraction small, Coil meaningfully stronger | 1.25 tiles vs 4.25 tiles (3.4×), capped at 2 tiles in code |
| 10 | Grenade quick-use through the existing authoritative path | One channel, one entry point; the missing launcher composed |
| 11 | Aim Assist can be disabled and defaults to ON | ON 11.31° bend, OFF 0.00°, ON again identical |
| 12 | Low-ammo teaching contextual and non-spammy | One prompt, four conditions, once per profile |
| 13 | Help/Codex documents only current real game rules | 8 sections; numbers read from the code; co-op stated honestly |
| 14 | Remaining HUD/bossbar/readability/truncation issues fixed | Boss bar, contrast, damage numbers, truncation fixed; crowding and doors stale |
| 15 | Prop-dressing variety improves without affecting gameplay | 0 blocked doors, 0 blocked markers, 0 navigation regressions |
| 16 | Settings remain backward compatible | Older document without the key loads ON with its other values intact |
| 17 | Validators enforce the new contracts honestly | New gate, 5 broken fixtures proven to fail it, nothing weakened |
| 18 | Frozen systems unchanged | `frozen_systems_check.csv` |
| 19 | Full relevant test/validator gates reported truthfully | Section 21 |
| 20 | Available-platform non-development build succeeds | macOS; Windows x64 NOT RUN — module unavailable |
| 21 | Built-player/runtime proof exists | 28-check smoke stage + screenshots + audio diagnostics |
| 22 | Human playtest checklist exists | 39 checks |
| 23 | No unrelated redesign occurred | Section 25; nothing in the non-goals list was touched |

### Already fixed / stale review findings (not reimplemented)

Ambience loop continuity; duplicate `AudioListener`; door open/locked readability.

### Deliberately deferred

Metro's door lamp hue (the shutter already carries the state unambiguously).

### External asset / content dependency

Alternate SFX clips; longer-form music; a low-HP warning cue; authored substrate sheets. See section 26.

### Blocked

Windows x64 build — module unavailable on this machine; not installed, reported as NOT RUN.

---

## Appendix A — Artefact index

| Artefact | Contents |
|---|---|
| `TestResults/PresentationAudioUxQol/current_state_before.csv` | 47-row reverification of every referenced finding |
| `.../world_substrate_matrix.csv` | 15 cases: 3 biomes × 5 room/layout shapes |
| `.../substrate_luminance.csv` | Substrate vs floor luminance and ramp contrast per biome |
| `.../audio_mix_before.csv` | All 53 events + 20 beds, before |
| `.../audio_mix_after.csv` | All 53 events, after, read from the shipped assets |
| `.../audio_runtime_matrix.csv` | Runtime listener, pitch range and throttle proof |
| `.../music_loop_alignment.csv` | Each music bed's length against its bar grid |
| `.../shelter_trader_matrix.csv` | Offer rows, icons, rarity, price, detail/comparison rows |
| `.../status_effect_matrix.csv` | Every timed effect: activation, chip, icon, fill, expiry |
| `.../pickup_attraction_matrix.csv` | Near / mid / far / behind-wall, with and without the Coil |
| `.../grenade_quick_use_matrix.csv` | Slot used, quantity before/after, throws, gate held |
| `.../aim_assist_setting_matrix.csv` | ON / OFF / ON-again bend angles and target selection |
| `.../ammo_prompt_matrix.csv` | Queueing, once-only, disabled, threshold |
| `.../codex_matrix.csv` | Section titles, line counts, glyph usage |
| `.../readability_matrix.csv` | Contrast ratios, window shapes, menu column, numbers, boss bar |
| `.../prop_dressing_matrix.csv` | Per room: patterns, authored/added detail, blocked doors/markers, navigation |
| `.../frozen_systems_check.csv` | Every frozen value: frozen vs observed vs owner |
| `.../HUMAN_PLAYTEST_CHECKLIST.md` | 39 diagnostic checks |
| `TestResults/presentation_audio_ux_qol.md` | The validator's own report |
| `TestResults/build_report_macos.md` | The macOS build report |
| `TestResults/smoke_result.json` | The built-player smoke result, including this pass's stage |
