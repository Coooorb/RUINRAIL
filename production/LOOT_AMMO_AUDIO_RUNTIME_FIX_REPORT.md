# RUINRAIL — Loot / Ammo Economy / Starter Fallback / Audio Runtime Fix Report

> **Scope:** the four repository-local areas of `RUINRAIL_LOOT_AMMO_AUDIO_RUNTIME_FIX_PROMPT.md`, executed from the
> state after `COMBAT_AIM_COLLISION_AND_FINAL_VISUAL_FIX_REPORT.md`. No combat, enemy stat, weapon damage/rate/range,
> accepted art/VFX family, dungeon topology, progression, save semantic or network authority rule was redesigned;
> no pinned Editor/package version changed. One approved design update (base/75, owner instruction): the starter kit
> guarantees an ammo-free Secondary.
> **Terminal status:** `LOOT_AMMO_AUDIO_FIX_COMPLETE`

| Gate | Result |
|---|---|
| `./scripts/run-unity-tests.ps1 -TestPlatform EditMode` | **PASS — 792 passed / 793 discovered, 0 failed** (1 skipped: the pre-existing `RUINRAIL_LIVE_SERVICES=1` live-Sessions check) |
| `./scripts/run-unity-tests.ps1 -TestPlatform PlayMode` | **PASS — 619 passed / 619 discovered, 0 failed** |
| `ContentCountValidator` | PASS — 52/52 counts exact, 0 problems |
| `FinalProductionValidator` | PASS — 9/9 sections, 0 errors, 0 external blockers |
| `PresentationValidator` | PASS — 8 checks, 0 problems |
| `AssetPipelineValidator` | PASS — 6 checks, 0 problems (306 manifest roles, 188 convention sprites) |
| `ReleasePathVisualScan` | CLEAN — 0 placeholder references, 0 missing references (4 scenes, 65 prefabs, 448 renderers) |
| `ArtProductionContract` | PASS — 10 checks, 295 sprites |
| `AnimationAssetAudit` / `AudioAssetAudit` / `MusicAssetAudit` | COMPLETE — 22/22 sets, 33/33 weapon sprites; 53/53 SFX with clips; 11/6/3 music/stinger/ambience |
| Loot / content validators | `LootAmmoRuntimeTests` (7) + `SupplyChestRuntimeTests` (5) inside the suites above; seed sweep CSV written |
| Save / exploit hardening | `ExploitHardeningTests` 6/6, `PersistenceHardeningTests` 4/4 inside the suites above |
| Network pickup / chest | `NetworkLootAuthorityTests` (8) + `SupplyChestRuntimeTests.Network_…` inside the suites above |
| Ammo economy harness | `AmmoEconomyHarness` — 300 depths before/after, `TestResults/LootAmmoAudioProof/ammo_economy_simulation.md` |
| Audio runtime tests | `AudioContentRuntimeTests` (8, EditMode) + `LootAmmoAudioProofTests` (3, PlayMode, live run) |
| Release build (`ReleaseBuildTool.BuildBatch`, Windows x64, non-development) | **Succeeded** — 0 errors, 1 warning (the external UGS project-ID notice), 157.7 MB (`TestResults/build_report.md`) |
| Built-player smoke, headless (`RUINRAIL.exe -batchmode -nographics -smoke -seed 2 -savedir … -proofdir …`) | **Success** — 15/15 playability, 12/12 combat, **24/24 loot/ammo/starter**, **22/22 audio** checks, `ReturnToMenuOk: true`, 0 exceptions, 0 missing scripts/references |
| Built-player smoke, windowed (`-smoke -seed 2/11/12 -screen-width 1280 -screen-height 720 -proofdir …`) | **Success ×3** (Ruined Metro, Overgrown Labs, Rustworks) — 24/26/25 loot checks, 22/22 audio checks each, 7/9/8 captures, listener mix carried signal in 57–59 of 60 sampled frames, 0 exceptions |

Every gate above ran **after the last source change** (the suites and validators, then the build, then the four
smoke runs of that build). Unity `6000.3.24f1`. Counts are read from the final `TestResults/EditMode-results.xml` /
`PlayMode-results.xml`. Baseline before this pass: EditMode 774/775, PlayMode 611/611 → **+18 EditMode, +8 PlayMode
tests**. Smart App Control was not touched (every batch launch compiled at the first attempt).

---

## 1. Chests / loot sources — actual root causes

Traced per the 13-step path of the prompt on the shipped composition (`ExpeditionScene` → `DungeonRoomRuntimeComposer`
→ `RoomCategoryComposer`):

| # | Root cause | Where | Effect on a real run |
|---|---|---|---|
| 1 | **No world object had a renderer.** `CreateChest` / `CreateAnchorObject` built a GameObject with a trigger collider and the logic component only; `LootSpawner.CreateBarePickup` the same. `Art/World/*.png` (chest, pickups, coin, merchant, event objects, transit car) was referenced by **no runtime asset** — the completion manifest listed the roles as INTEGRATED because the sprite files existed. | `Dungeon/Runtime/RoomCategoryComposer.cs`, `Loot/LootSpawner.cs` | Every chest, every pickup, every coin pile, the merchant, every event object and the transit car were invisible, collider-only objects. A player walked past chests without ever seeing one; loot that a chest produced was equally invisible. |
| 2 | **Chests existed only in Loot/Treasure rooms and as the Boss Cache.** The generator places 1–4 branch rooms per depth (9–10 rooms at depth 1–3) and fills them from a shuffled pool of 7 optional categories (1 Merchant, 2 Event, 2 Loot, 1 Treasure, 1 Medical) minus a random number of extra Combat rooms. The spec's common container (58 "Supply Chest") was **never instantiated anywhere**; combat rooms carry no `ChestSpawn` marker. | `RoomCategoryComposer.Compose`, room prefabs | The seed index (`seed_room_index.csv`) shows 40 of the first 60 first-depths without any Loot/Treasure room: for those depths the only container was the Boss Cache, behind the boss. Zero pre-boss chests was the routine case. |
| 3 | **No interaction prompt existed.** `IInteractable` had no presentation contract; the HUD showed nothing near a chest. | `Loot/IInteractable.cs`, `ExpeditionScene` | Even a found chest gave no cue that Interact would open it. |
| 4 | **The ammo usefulness rule was inert.** `DungeonRuntimeServices.UsefulAmmoTypes` was never assigned by the scene, so `LootRoller.PickAmmo` always used the unrestricted pool. | `App/ExpeditionScene.cs` | Ammo rolls ignored the carried firearm. |
| 5 | Chest open / pickup / coin / merchant / door / dry-fire audio hooks existed in `GameplayAudioBinder` but were **never attached** by the scene. | `App/ExpeditionScene.cs` | See §6. |

**Fixes**

- `Loot/WorldObjectVisual.cs` (new): the world-object art seam — `WorldObjectArt.Resolver` is registered by the
  composition root (`content.WorldSpriteFor`), gameplay never loads by path. `WorldObjectVisual.Attach(host, key, role)`
  draws one `SpriteRenderer` child; standing objects (chests, merchant, events, transit car) y-sort **with the
  characters** (Characters layer, feet order — never under floor/wall tilemaps, the player walks in front of and behind
  them correctly); ground pickups draw on the Loot layer. `GameContentCatalog.WorldSprites` / `WorldSpriteFor(key)` bind
  every `Art/World/*.png` by file stem (builder: `GameContentCatalogBuilder`), and `Problems()` now fails the catalog if
  any of the 13 runtime keys is unbound (`WorldObjectArt.RequiredKeys`). Catalog rebuilt: 28 world sprites.
- `SupplyChest`: `AttachVisual()`, `ArtKey` = `world_boss_cache_gate` while locked → `world_supply_chest` closed →
  `world_supply_chest_open` after its one reward transaction; `LockChanged` event; `IInteractionPrompt` ("OPEN CHEST",
  "OPEN BOSS CACHE", "LOCKED", nothing once opened). The opened state is a **new frame of the same accepted crate**
  (`WorldObjectFactory.CrateOpen`, lid thrown back, dark empty interior, emissive latch off), generated by the existing
  pipeline through the new targeted `ArtIntegration.GenerateMissingWorldObjects` (writes only sprites that do not exist;
  every accepted sprite untouched). The closed chest art is preserved as is.
- `LootSpawner`: bare pickups get the final `world_item_pickup` / `world_coin_pickup` art; pickups carry the item's
  display name for the prompt ("TAKE LIGHT AMMO x32", "TAKE 13 COINS"); every pickup lands **beside** the container on
  the deterministic ring (index + 1, radius 0.8) so the opened crate never covers its own loot; pickups are tracked by
  the ground registry only once they are complete (item, name, amount) and the registry raises `PickupTracked` — the one
  seam the scene uses to give every pickup its sound, stinger and tutorial hook.
- Merchant, event objects and the transit car draw their art from `CreateAnchorObject(…, artKey)`; a resolved event is
  drawn dimmed (`WorldObjectVisual.ResolvedTint`), also when restored resolved on a revisit.
- **Interaction prompt** (ui/90 "one consistent Interact action"): `IInteractionPrompt` on chest, item pickup, coin
  pile, merchant ("TRADE WITH MERCHANT") and event objects ("<ACTION> <TITLE> (cost COINS)"); `ExpeditionScene` shows
  `[E] <text>` for the nearest usable target of `PlayerInteractor.FindNearestInteractable()` (the same query the
  Interact press uses), hidden while pause/inventory own the screen (`ExpeditionScene.CurrentInteractionPrompt`).

## 2. Loot-source rules and rates (final)

| Source | Rule | Runtime |
|---|---|---|
| Loot room (55) | 2 `ChestSpawn` markers per shipped Loot room → 2 Equipment Chests (unchanged) | now visible, prompt, opened state |
| Treasure room | 1 marker → 1 Treasure Chest (Improved table) (unchanged) | now visible |
| Boss room | Boss Cache at the marker, gate art while locked, crate once the boss falls (unchanged rule) | now visible |
| Event rooms | Cursed Chest / Locked Vault / Weapon Cache / Broken Machine / Supply Signal / Medical Station at the EventAnchor (unchanged) | now visible, prompt with cost, dimmed when used |
| **Ordinary Combat rooms (new)** | `SupplyChestPlanner`: each ordinary Combat room (never Start, Boss or a special room) rolls once on the Loot stream (`MixSeed(runSeed, depth, Loot, "SC", nodeId)`); rolls below **25 %** carry one **Supply Chest** (58 "common": coins 10–25, one ammo stack, 20 % small equipment chance — the shipped table). **Floor:** if fewer than **2** rooms qualify, the rooms with the lowest rolls are promoted, deterministically, until the depth has 2. Same seed → same rooms, same cells. | `RoomCategoryComposer.BindSupplyChest`, resolved id `chest:supply`, loot source slot 15 of the room's stride |
| Supply Chest placement | `SupplyChestPlacement`: a walkable floor cell, reachable (4-connected) from every doorway, ≥ 2 cells from the wall ring, Chebyshev ≥ 3 from every door cell, ≥ 1 cell from every authored marker footprint (enemy/player spawns, hazards, anchors), preferring cells that touch a wall/obstacle; seeded pick among the candidates. The chest is a trigger — it never blocks movement, doors, spawn points or the activation volume. | verified for all 33 shipped Combat rooms × 12 seeds (`SupplyChestPlacement_…`) |

**Seed sweep** (`LootAmmoRuntimeTests.SeedSweep_…`, `TestResults/LootAmmoAudioProof/loot_seed_evidence.csv`, 100 seeds ×
3 biomes, depths 1–3): chest/container count per depth **min 3, max 7, mean 4.2** (Supply Chests + Loot/Treasure chests +
Boss Cache); ammo-source opportunities before the boss **min 2**; no valid depth without meaningful loot before the boss.
Ordinary rooms with a Supply Chest: 690/1860 = 37 % realised (25 % by roll; the floor lifted 153/300 small depths to 2).
Special rooms always carry their promised source (every Loot room prefab 2 chest markers, Treasure 1, Boss 1 cache marker,
Event 1 anchor — asserted). 12/300 depths rolled only non-Light ammo from their two opportunities (the 70/30 rule keeps
rolls non-deterministic by design; see §3).

**Presentation / interaction proof** (`SupplyChestRuntimeTests`, `LootAmmoAudioProofTests.LiveRun_Chests…`, and the
shipped-player smoke): final crate art; Characters-layer y-sort; not inside solid geometry (`RoomRuntime.IsSpawnClear`);
trigger collider; one prompt; `TryOpen` succeeds once and rolls once (`Opened` raised once), a second open/interaction is
refused, the room state records `chest:supply` and a recomposed room restores **opened** with nothing to roll (no repeated
loot after leaving/re-entering); pickups visible on the Loot layer; the collected ammo stack leaves the ground exactly
once; host authority: two clients racing for the chest's ammo stack → one `Accepted`, one `AlreadyTaken`, retries return
the stored result, exactly one reserve increments (`Network_TwoClientsRaceForTheSupplyChestAmmo_…`; `ChestOpen_ByTwoClients_OpensOnce`
already covered the open).

## 3. Ammo economy — audit, rules, before/after

**Sources on the real runtime path** (audited, `AmmoEconomyHarness` models exactly these):

| Source | Definition | Instantiated | Amount | Relevance | Notes |
|---|---|---|---|---|---|
| Starter reserve | 75: Light ×60 | yes | 60 | — | unchanged |
| Supply Chest ammo roll | `LootTable_SupplyChest`: 100 % one stack — Light 20–40 (w4), Medium 15–30 (w3), Heavy 6–12 (w2), Shells 5–10 (w2) | **now** in ordinary rooms (§2) | 5–40, never 0 | 70/30 usefulness (26): with a Light weapon carried **78.9 %** of rolls are Light (unrestricted: 36.3 %) — `SupplyChestRolls_…` over 1000 contexts | the shipped table; quantities untouched |
| Merchant ammo slot | 58: 1 Ammo slot, trader bundle Light ×60 @ 60 coins | yes (Merchant room) | bundle | any | needs Carried Coins (supply chests give 10–25 each) |
| Broken Machine | `LootTable_BrokenMachine` (ammo / consumable / item), paid, chance | yes (Event room) | table | — | not counted as guaranteed |
| Enemy drops | **not an approved source** (no spec rule, no implementation) | — | — | — | not invented |
| Loot / Treasure / Boss Cache | equipment tables (58) | yes | — | — | no ammo |

`UsefulAmmoTypes` is now live: `ExpeditionScene` keeps a set of the ammo types the equipped Primary/Secondary firearms
consume (refreshed on every loadout change) and hands the same collection to every chest/event context, so rolls read
the current loadout. Caps unchanged (26: per backpack slot Light 180 / Medium 120 / Heavy 60 / Shells 40; `AmmoCaps_…`
asserts pickup clamps to the stack cap, nothing duplicates or refills). Rockets keep using Heavy per the weapon rules.

**Practical-supply simulation** (`AmmoEconomy_300GeneratedDepths_…`, report + CSV in `TestResults/LootAmmoAudioProof/`):
real generator/room pool, real encounter director and depth scaling (enemy HP), real Elite/Boss rosters, real loot
tables rolled with the real per-source contexts, starter P9 Ranger (12–14 dmg, 12-round magazine), **65 % accuracy**
(misses spend rounds), merchant bundle bought when the depth's coins cover it, the knife never counted. 100 seeds ×
3 biomes at depths 1–3.

| | runs **not** majority-dry (room-weighted, acceptance metric) | median rounds needed / depth | median relevant ammo found | supply chests / depth | median end reserve |
|---|---|---|---|---|---|
| **BEFORE** (shipped runtime: no ordinary-room chest, no usefulness weighting) | **111/300 = 37 %** | 289 | **0** | 0 | 0 |
| **AFTER** (25 % + floor 2, 70/30 weighting) | **276/300 = 92 %** (Metro 92, Rustworks 92, Labs 92) | 289 | 56.5 (max 140) | min 2 / median 2 / max 4 | 0 |

An engagement counts as dry when it starts with nothing to fire or more than half of its required rounds cannot be
fired; a run is majority-dry when more than half of its engagements are dry. Median dry engagements per depth fell from
4 of 7 to 2 of 7. Scarcity is preserved: every one of the 300 runs still has at least one dry room, the median
end-of-depth reserve is 0 (the boss — ~120 rounds at pistol damage, ~40 % of a depth's requirement — drains it), no depth
finds more than 140 Light, and the cap is never approached. The shot-weighted view is dominated by that boss fight
(median 55 % of all rounds unfired after the fix, 75 % before); the sensitivity table in the report shows 75 % accuracy
→ 300/300 and a floor of 3 → 285/300 at 65 %. The shipped rule stays the smallest one that meets the target.

**Opportunity floor** (§7.3): the planner's floor of 2 Supply Chests in ordinary rooms before the boss is the deterministic
budget; it works through the existing chest/room-loot system, at normal cells with the normal art — no emergency piles.

**Dry fire** (authored `weapon.dry_fire` was never played): `RangedWeapon.DryFired` / `DryFires` — a fire press on an
empty magazine with an empty reserve clicks once per fire-cooldown; `GameplayAudioBinder.Attach(RangedWeapon)` plays it.

## 4. Starter loadout — guaranteed ammo-free Secondary

- **Primary:** `weapon_p9_ranger` (P9 Ranger), forced Common, no affixes, unsellable — unchanged.
- **Secondary:** `weapon_field_knife` (**Field Knife**, existing `MeleeWeaponDefinition`, `WeaponClass.Knife`, 14–17 dmg,
  3.5/s, range 1.2, 80° — catalog values, **not buffed**), forced Common, no affixes, unsellable (`ItemInstance.IsUnsellable`,
  the trader refuses it like the pistol and vest). No new weapon was invented.
- `StarterKitService.CreateKit` adds the knife to `EquippedSlot.SecondaryWeapon`; the first-profile grant and the wipe
  rescue (`EnsureStartableLoadout`, only when no weapon exists anywhere) both carry it; repeated boots and repeated
  rescue calls never duplicate it (`RepeatedInitialisation_NeverDuplicatesTheSecondary`).
- In a live run the rig mounts it as a `MeleeWeapon` in the Secondary slot; `WeaponLoadout.SelectSlot(Secondary)` swaps
  synchronously; `HeldWeaponVisual` draws it; it consumes no ammo resource (`MeleeWeapon` has no reserve); a swing at
  Light/Medium/Heavy/Shells = 0 damages a grunt (30 → 14 / 24 / 23 in the three shipped-player runs).
- Spec updated: `base/75_STARTER_KIT.md` (design update note, previously "No guaranteed Secondary Weapon"); the Shelter
  onboarding kit summary lists it automatically.

Tests: `StarterKitTests` (+3 new, 2 updated: new profile, wipe recovery, exact ids, slots, unsellable, no duplicate),
`LootAmmoAudioProofTests.LiveRun_Chests…` (live swap at zero reserve, damage, no ammo of any type consumed, dry click),
the shipped-player smoke (`07_starter_inventory_primary_and_knife_hud.png`, `08_…`, `09_…`).

## 5. Audio — actual root causes of the silence

Traced from `AppRoot` → `GameApp.Initialize` → `AudioService` / `MusicDirector` / `MusicBinder` → scene compositions:

| # | Root cause | Where | Effect |
|---|---|---|---|
| 1 | **No AudioListener in the Main Menu or the Shelter.** The scenes are data-free; only `ExpeditionScene` created a camera and it was the only place an `AudioListener` was added. | `App/ExpeditionScene.cs` (only listener), `MainMenuScreen`/`BaseHubScreen` (none) | Main Menu music, Shelter music and every UI click were mixed into nothing: the menu and the Shelter were silent in the shipped player. |
| 2 | **The dungeon never entered the expedition music state.** `MusicBinder.Attach(expedition)` subscribes to `ExpeditionStarted`/`DepthEntered`, but the expedition starts in the Shelter, before the dungeon scene exists — the event had already fired. The binder's screen stayed `Shelter`. | `Audio/MusicBinder.cs`, `ExpeditionScene` | No biome exploration/combat track, **no ambience ever** (ambience only plays on the Expedition screen); the Shelter bed kept playing under the dungeon. |
| 3 | **Boss bed from spawn.** `BossEncounter.BossStarted` fires the moment the boss actor acquires the player as target — at depth build (`MovesetActorController.Awake` finds the player). With (2) fixed, every depth would have started on the Boss track. | `Audio/MusicBinder.cs` | Wrong bed for the whole depth. |
| 4 | **Positioned SFX were 3D at distance 10.** Every world-positioned one-shot used `spatialBlend = 1` with Unity's logarithmic rolloff while the listener sat on the camera 10 units behind the plane: ≈ −20 dB for every hit, shot, death, door. | `Audio/AudioService.cs` | Gameplay SFX inaudible under the beds. |
| 5 | **Master applied twice.** `UnitySettingsApplier` set `AudioListener.volume = master` while `AudioLevels` already multiplies master into every source gain (0.5 → 0.25). | `UI/Settings/SettingsViewModel.cs` | Quieter than set. |
| 6 | **Hooks never attached.** Chest open, item pickup, coin pickup, merchant, door lock/unlock, dry fire, Elite/Boss telegraph/hit/death and the Elite stinger had binder code but no scene wiring. | `ExpeditionScene` | Those events were silent. |
| 7 | The last round of every magazine produced no fire cue: the visual driver skipped the kick when the auto reload had already started in the same tick. | `Presentation/Animation/WeaponVisualDriver.cs` | One silent shot per magazine. |

No accidental global mute, no zero default, no `AudioListener.pause`, no clip-load failure and no missing release
reference was found (the 73 clips were bound and non-silent all along — the graph around them was dead).

**Fixes (bootstrap / mixer / events)**

- `Audio/AudioListenerRig.cs` (new): the **one** listener of the process, on the persistent `GameApp` root
  (`AudioListenerRig.Ensure` is idempotent, `AudioListener.pause = false` at boot), following `Camera.main` in a late
  `LateUpdate` (`DefaultExecutionOrder(1000)`, after the camera rig). `ExpeditionScene` no longer adds a listener;
  `NoReleaseScript_PausesTheListener_OrZeroesItsVolume_OrAddsASecondListener` scans the release scripts.
- `AudioService`: every source is 2D; positioned one-shots and following loops are attenuated in the world plane by
  `Spatializer` (full gain within 6 tiles, linear to a floor of 0.35 at 18 tiles, never below — a room-scale event is
  always heard) and panned lightly (±0.5 over 10 tiles); gains follow settings changes with the spatial factor kept.
  `VoiceStarted` / `LastStartedSource` expose every started voice for evidence.
- `MusicBinder.EnterExpedition(state)` — called by `ExpeditionScene` after `Attach(expedition)` — enters the biome
  exploration bed and the biome ambience explicitly. `Attach(BossEncounter)` keeps the defeat stinger/exploration
  return; the Boss bed now comes from `ObserveBossRoomEntered()`, which the scene raises from the **boss room's
  activation** (player inside, doors shut); normal rooms keep `ObserveCombatStarted/Ended`.
- `UnitySettingsApplier` leaves `AudioListener.volume` at 1: master reaches the sources exactly once through
  `AudioLevels` (`MasterVolume_IsAppliedExactlyOnce_…`).
- Scene wiring: chests (all kinds + Boss Cache) and the merchant per room; every tracked pickup (`GroundLootRegistry.PickupTracked`)
  gets drop/pickup cues, the Legendary stinger and the tutorial hook; `RoomDoorLock.LockChanged` → `PlayDoor` (the
  authored `world.door.open` mechanism cue for both transitions); every mounted `RangedWeapon` → dry fire (re-attached on
  remount); Elites/Bosses → telegraph/hit/death/phase cues and the Elite stinger.
- `WeaponVisualDriver`: a magazine decrease always kicks (a reload only adds), so the final round fires its cue.

**Settings / defaults (§12):** fresh profile Master/Music/SFX = 1.0, Mute = false; ambience has no separate user setting
in the approved settings document — it is the SFX gain under the 0.4 ceiling (`MusicDirector.AmbienceCeiling`), so it is
audible at the defaults and below SFX/music in the mix. A settings document without audio keys deserialises to the
defaults (JsonUtility keeps field initialisers), an explicit user mute / explicit zero survives save + reload and stays
respected (`SettingsDocumentWithoutAudioKeys_…`, `ExplicitUserMute_…`). No migration was needed because no absent key
ever became zero.

## 6. Audio runtime proof

**Tests** — `AudioContentRuntimeTests` (EditMode, 8): the 73 roles (53 SFX + 11 tracks + 6 stingers + 3 loops) resolve
**on the release path** (the shipped `GameContentCatalog`, not a project search) to 73 distinct clips; every clip's
PCM16 source is non-trivial and non-silent (parsed from the WAV: duration ≥ 0.05 s, peak ≥ 0.05, RMS ≥ 0.005 — actual
peaks 0.76–0.89, RMS 0.08–0.41); defaults audible; mute preserved; master once; spatializer; no listener misuse.
`LootAmmoAudioProofTests` (PlayMode, live boot flow): exactly one listener in the Main Menu (no camera object), the
Shelter and the dungeon (following the expedition camera); the menu bed, the Shelter bed, the biome exploration bed and
the biome ambience loop playing with `clip != null`, `isPlaying`, `volume > 0`, 2D, one bed outside a crossfade; the
combat bed on `ObserveCombatStarted` and exactly one track source after the crossfade; a UI click, a real pistol shot,
a reload, a player hit and a door lock/unlock reaching `AudioService.Play` with an allocated, playing, audible 2D voice
(0 silent, 0 unknown events); music and ambience playing while paused and after resume; return to the Main Menu through
the pause menu restores the menu bed, stops ambience, plays the expedition-failed stinger, still one listener and one
app root; all three biomes start their exploration bed + ambience without duplicates; `GameApp.Ensure` never creates a
second manager.

**Shipped player** (`TestResults/LootAmmoAudioProof/audio_runtime_evidence.txt`, written by the windowed exe, seed 2 —
the seed 11/12 folders hold their own): one line per started voice and per music/ambience state at every scene and
checkpoint — timestamp | scene/state | kind | role/event | clip | mixer group (`direct(<bus>)`: the project ships no
AudioMixer asset, sources output straight to the listener) | source volume | master/music/sfx/mute | isPlaying.
Sequence exercised in one windowed run: Main Menu (`music_mainmenu` playing) → Shelter (`music_shelter`, UI confirm
clicks) → Dungeon (`music_ruinedmetroexploration` + `ambience_ruinedmetro`, combat bed on room activation) → fire
(`weapon_fire_pistol`) → reload (`weapon_reload`) → damage enemy (`enemy_hit`) → kill enemy (`enemy_death`) → open chest
(`loot_drop_common`, `loot_chest_open`) → pickup (`loot_pickup_item`, `loot_pickup_coins`) → dry fire (`weapon_dry_fire`)
→ knife (`weapon_melee_knife_slash`) → door lock/unlock (`world_door_open`) → pause (beds keep playing) → resume → return
to menu (`music_mainmenu` restored, `stinger_expeditionfailed`). A music line logged at the instant of a scene
composition shows `vol 0.00` because the crossfade starts at 0 and ramps to 1.0 over 1.5 s; the checkpoint lines 7 s
later show `vol 1.00`. The listener's mixed output (`AudioListener.GetOutputData`) carried signal in 57–59 of 60 sampled
frames (peak 1.0–1.4 during the overlapping test bursts) in each windowed run — proof of the mix at the listener, **not**
a claim about the speakers. No loopback recording is available on this machine; no WAV was fabricated.

## 7. Built-player smoke (extended)

`SmokeRunner.LootAudio.cs` adds two stages to the shipped-player smoke after the combat stage, plus `-proofdir` and
`-seed`: **loot/ammo/starter** — starter primary/secondary mounted; every runtime art key bound; Loot/Treasure rooms
carry visible closed chests; the Boss Cache drawn as the gate while locked; ≥ 2 supply chests in ordinary rooms; the
merchant with an ammo offer (when the depth has one); walk to a Supply Chest, prompt "OPEN CHEST", open through the
interactor once, second interaction refused, pickups visible, chest cue; ammo pickup collected once with the reserve
delta and cap asserted, pickup cue; coins once; reserve → 0, dry click, swap to the knife, a swing damages a frozen
grunt with all four reserves unchanged, hit and death cues; captures of the Loot room source and the merchant. **Audio**
— one listener following the camera, one service/director on the app root, audible defaults, the biome bed and
ambience playing, one track outside a crossfade, eight representative SFX started (fire, reload, enemy hit, enemy death,
chest open, pickup, door, dry fire), a UI voice allocated 2D and audible, beds playing while paused and after resume,
listener mix sampled; after the pause-menu return: the menu bed, no ambience, one listener, the failed stinger.

Results: headless (seed 2, Ruined Metro) 15/15 + 12/12 + 24/24 + 22/22, `ReturnToMenuOk`; windowed seed 2 (Ruined
Metro, 7 captures), seed 11 (Overgrown Labs: Loot room + merchant, 26 loot checks, 9 captures), seed 12 (Rustworks: two
Loot rooms, Shells roll 0 → 8, 8 captures) — all `Success: true`, 0 exceptions / missing scripts / missing references in
the player logs.

## 8. Tests added / updated

| Suite | Tests | Covers |
|---|---:|---|
| `LootAmmoRuntimeTests` (EditMode, new) | 7 | planner determinism / share / floor / exclusions; placement on all 33 combat rooms; 100 seeds × 3 biomes sweep + CSV; seed index; ammo rolls non-zero/valid/70-30; caps; 300-run economy before/after + report |
| `AmmoEconomyHarness` (EditMode helper, new) | — | the deterministic economy model |
| `AudioContentRuntimeTests` (EditMode, new) | 8 | 73 roles on the release path; 73 non-silent clips; defaults; missing-key document; explicit mute; master once; release-script listener scan; spatializer |
| `StarterKitTests` (EditMode) | +3 (2 updated) | knife identity/slot/no ammo; no duplicate on repeated init and wipe; unsellable |
| `SupplyChestRuntimeTests` (PlayMode, new) | 5 | composer places one visible chest on a valid cell / none when unselected; opens once, pickups tracked complete, revisit restores opened; Boss Cache gate → crate → opened; merchant/event art + used tint; network race for the chest's ammo |
| `LootAmmoAudioProofTests` (PlayMode, new) | 3 | live loot/ammo/starter proof with captures; live audio graph proof; three biome beds |

Total **+18 EditMode, +8 PlayMode**. No existing test was weakened; `AnimationIntegrationTests` still asserts that a
magazine change during a reload is not a shot.

## 9. Files changed

Runtime: `Loot/WorldObjectVisual.cs`*, `Audio/AudioListenerRig.cs`*, `Dungeon/Runtime/SupplyChestPlanner.cs`*
(planner + placement), `App/SmokeRunner.LootAudio.cs`*, `Loot/IInteractable.cs`, `Loot/SupplyChest.cs`,
`Loot/WorldItemPickup.cs`, `Loot/CoinPickup.cs`, `Loot/LootSpawner.cs`, `Loot/GroundLootRegistry.cs`,
`Loot/DungeonMerchantInteractable.cs`, `Events/DungeonEventInteractable.cs`, `Dungeon/Runtime/RoomCategoryComposer.cs`,
`Dungeon/Runtime/DungeonRoomRuntimeComposer.cs`, `Dungeon/Runtime/RoomContentBinding.cs`, `Dungeon/Runtime/RoomDoorLock.cs`,
`Combat/Weapons/RangedWeapon.cs`, `Audio/AudioService.cs`, `Audio/MusicBinder.cs`, `Audio/GameplayAudioBinder.cs`,
`Presentation/Animation/WeaponVisualDriver.cs`, `UI/Settings/SettingsViewModel.cs`, `App/GameApp.cs`,
`App/GameContentCatalog.cs`, `App/ExpeditionScene.cs`, `App/BaseHubScreen.cs`, `App/SmokeRunner.cs`,
`Base/StarterKitService.cs` (* = new).

Editor: `ArtGen/WorldObjectFactory.cs` (CrateOpen motif + `world_supply_chest_open` design), `ArtGen/ArtIntegration.cs`
(`GenerateMissingWorldObjects` + batch), `Production/GameContentCatalogBuilder.cs` (world sprites).

Assets: `Art/World/world_supply_chest_open.png` (+ meta, PPU 32, point, uncompressed, pivot 0.5/0.12),
`Resources/GameContentCatalog.asset` (28 world sprites bound), `Builds/Windows64/` (rebuilt).

Docs: `base/75_STARTER_KIT.md` (design update), this report.

Tests: see §8 (`Assets/Game/Tests/EditMode/AmmoEconomyHarness.cs`, `LootAmmoRuntimeTests.cs`, `AudioContentRuntimeTests.cs`,
`StarterKitTests.cs`; `Assets/Game/Tests/PlayMode/SupplyChestRuntimeTests.cs`, `LootAmmoAudioProofTests.cs`).

## 10. Proof paths

`TestResults/LootAmmoAudioProof/`:
- shipped exe, seed 2 (Ruined Metro): `01_closed_supply_chest_in_room.png`, `02_supply_chest_opened_loot_spawned.png`
  (opened + loot presented), `04_ammo_pickup_before_collection.png`, `05_reserve_increased_after_pickup.png`,
  `07_starter_inventory_primary_and_knife_hud.png`, `08_field_knife_active_at_zero_firearm_reserve.png`,
  `09_knife_damages_enemy_at_zero_reserve.png`, `13_built_player_combat_room.png`, `audio_runtime_evidence.txt`,
  `smoke_windowed/smoke_result.json` + `player.log`, `smoke_headless/…`
- shipped exe, seed 11 (Overgrown Labs) `seed11_overgrownlabs_lootroom_merchant/`: the same plus
  `06_loot_room_guaranteed_reward_source.png`, `10_dungeon_merchant_ammo_source.png`, its own evidence + result
- shipped exe, seed 12 (Rustworks, two Loot rooms) `seed12_rustworks_two_lootrooms/`: the same set (Shells pickup)
- live PlayMode run: `live_01`…`live_09` (+ `_x2`), `live_loot_ammo_evidence.txt`
- data: `loot_seed_evidence.csv` (300 rows, the required columns), `seed_room_index.csv`,
  `ammo_economy_simulation.md` / `.csv` (600 rows: before + after)
- logs: `TestResults/lootfix-*.log`, `TestResults/EditMode-*.{xml,log}`, `PlayMode-*.{xml,log}`, `build_report.md`

## 11. Known remaining blockers / notes

- **Live UGS / Relay** is still not linked on this machine; chest/pickup authority is verified on the deterministic
  in-memory host harness (`LootAuthorityService`, fake transport) and the replicated room state, not over a live session.
  The `RUINRAIL_LIVE_SERVICES=1` check stays the one skipped test.
- No audio loopback device: the mix proof is the listener's output buffer plus the non-silent-sample validation and the
  windowed run's voice evidence; no WAV recording is claimed.
- Mix observation (not a defect of this pass): the generated clips are normalised to 0.89 peak and every event ships
  at volume 1.0, so bursts of overlapping cues can sum above 1.0 at the listener (the smoke's deliberately dense bursts
  peaked at 1.4). Level staging is data on the `AudioEventDefinition` assets if a later polish wants headroom.
- The starter-firearm economy is deliberately tight at the boss: the boss alone costs ~120 pistol rounds at 65 %
  accuracy, so the ammo-free knife is expected to matter there; 92 % of representative depths are not majority-dry.

## 12. Terminal status

`LOOT_AMMO_AUDIO_FIX_COMPLETE`
