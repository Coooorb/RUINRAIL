# RUINRAIL — FULL GAME COMPREHENSIVE REVIEW

> **SUPERSEDED — historical document (2026-09-25).** Current release status: `production/FINAL_RELEASE_CANDIDATE_AUDIT.md`. Its findings 'co-op is not playable / not connected' and 'the audio mix does not exist yet' were resolved by the co-op completion and presentation/audio passes. The body below is kept unchanged as the record of its time.


> **Type:** Analysis only. No code, content, balance value, asset or test was changed by this pass.
> **Date:** 2026-09-23
> **Reviewed state:** current working tree (uncommitted work included and treated as authoritative), Unity `6000.3.24f1`.
> **Method:** repository inspection (design docs, ScriptableObject data, gameplay code, UI, validators, tests, production reports) plus first-hand reading of the captured gameplay/UI frames in `TestResults/`.
> **Evidence convention:** every claim below is marked **[FACT]** (verifiable in the repository, with the path), **[INFERENCE]** (reasoned consequence of facts), or **[OPINION]** (design judgement). Do not treat opinions as defects.

---

## 1. Executive Summary

RUINRAIL is, as software, in unusually good shape. 69,279 lines of gameplay code against 57,475 lines of tests, 1,647 automated tests green, a clean 12-assembly architecture, zero-growth memory across depth cycles, deterministic seeded generation, a complete art and audio pipeline with 306 integrated asset roles, and a built player that runs the whole loop end to end without an exception. Almost every system a designer asked for exists, is wired, and is covered.

As a **game**, it has one structural problem that dwarfs everything else, and it is not visible from any test result:

> **A large fraction of the content the player is supposed to chase does nothing.**

Concretely, and verifiably:

- **11 of 16 accessory families** have an Intrinsic that no runtime system reads. **[FACT]**
- **6 of 8 affixes** in the ranged-weapon pool, **5 of 6** in the blaster pool, **4 of 5** in the bow pool and **2 of 3** in the melee pool are no-ops. An Epic melee weapon *always* rolls exactly one functional affix. **[FACT]**
- **All 33 weapons** ship with `Knockback = 0` and `StaggerPower = 0`, so the entire player→enemy stagger/knockback system — a design doc, a player attribute's worth of resistance stats, a per-enemy resistance curve from 0 % to 95 %, two affixes, two Legendary accessory passives and a wall-impact damage rule — is inert. **[FACT]**
- **Enemies drop nothing on death.** Killing something yields XP and an unlocked door, and nothing else. **[FACT]**
- **Player damage scaling tops out around +24 % sustainable** while enemy HP scales to +450 %. There is no power axis that grows with depth. **[FACT/INFERENCE]**
- **The three biomes are gameplay-identical.** Same room-type distribution, same dimensions, same difficulty values slot-for-slot, same door sets, same enemy pool, numerically identical hazards. Biome identity is art, music and the boss. **[FACT]**
- **Co-op is not playable.** The networking layer is extensive and tested in isolation, but nothing in the shipping code path puts a second player into `ExpeditionScene`; the run composer is hardcoded solo. **[FACT]**

None of this is caught by the test suite, because the suite verifies that systems behave as specified in isolation — not that the content plugged into them produces an effect. `AccessoryPassiveTests` contains a passing test that asserts exact percentages of `WeaponSpreadReduction`, a stat with no consumer anywhere in the game. **[FACT]**

The recently completed character-progression pass found and fixed exactly this failure mode for Vitality (`SkillStatSource` existed and was tested but nothing instantiated it at runtime). **The same failure mode is still present in at least five other places.** That is the single most important finding of this review.

Beneath that, the design has three open questions that matter more than any polish item: *why descend*, *why keep playing after the twentieth run*, and *what is the skill ceiling of the combat*. All three currently have weak answers, and all three are fixable with data and small systems rather than rewrites.

The art and audio pipelines are real and complete, but the **audio mix does not exist yet** (53/53 events at volume 1.0, zero pitch variation, 16–27 second music loops) and the **dungeon renders as lit islands in a black void** because nothing is drawn outside room bounds. **[FACT]**

**What should not change:** the stat pipeline, the save/persistence architecture, the graphical inventory, the Dungeon Merchant UI, the main menu, the seeded RNG model, the pooling/perf work, the room-containment rules and the depth-heal rules. These are solved.

---

## 2. Current Game State

### 2.1 What exists and runs

**[FACT]** Verified from the repository and the current test/build artefacts:

| Area | State |
|---|---|
| Scenes | 4 (`Bootstrap`, `MainMenu`, `Base`, `Dungeon`) plus a test scene |
| Assemblies | 12 (`Core`, `Gameplay`, `Dungeon`, `UI`, `Audio`, `Presentation`, `Networking`, `Persistence`, `App`, `Editor`, 2 test) |
| Gameplay code | 69,279 lines |
| Test code | 57,475 lines across 272 test files |
| Tests | EditMode 887 passed / 888 discovered (1 ignored: live Relay); PlayMode 759 / 759 |
| Content | 3 biomes, 63 rooms, 33 weapons, 9 armor, 16 accessories, 10 consumables, 9 normal enemies, 6 elites, 6 bosses, 11 Legendary weapon specials, 16 Legendary accessory passives, 6 event types |
| Assets | 267 PNG, 73 WAV, 22 animation sets, 1 bitmap font — all procedurally generated by the project's own `Editor/ArtGen` pipeline |
| Build | Windows x64 is the declared release target; a macOS StandaloneOSX build is the verification substitute on the current machine |
| Smoke | Headless and windowed built-player smokes pass, 334 checks, 0 exceptions |
| Performance | Depth cycles show 0.00 MB mono-heap growth and constant live GameObject counts; generation 2.2–2.8 ms average per depth |

### 2.2 What is claimed but not true in the current tree

**[FACT]** `production/AUDIO_EVENT_AUDIT.md` (dated 18 Sep) states `Content: BLOCKED_EXTERNAL_ASSET — 0/53 events have clips`. The current generated report `TestResults/audio_assets.md` (23 Sep) states `COMPLETE — 53/53 events have clips`, and 53 WAVs exist on disk. The `production/` copy is stale and contradicts reality. Anyone reading `production/` for status will be misled.

**[FACT]** `06_OPEN_DECISIONS.md` states "There are **no intentionally open core V1 game-design decisions**." This review identifies several places where the implementation silently diverges from an approved design document (weapon spread, weapon knockback/stagger, accessory intrinsics), which means there *are* open decisions — they were simply never surfaced as such.

---

## 3. What Is Working Well

These are genuine strengths. Several are better than the genre norm.

**3.1 The stat pipeline.** **[FACT]** `PlayerStats` is a single deterministic aggregator: sources keyed by stable id, all modifiers of a stat summed, the global cap applied exactly once at the end, insertion order irrelevant by construction. `Recompute()` is the only place cap math happens and no consumer re-does it. This is the correct architecture and it is now correctly fed by progression, equipment, passives and temporary buffs. **[OPINION]** Do not touch it.

**3.2 Save and persistence.** **[FACT]** `SaveSlot` is a plain serializable envelope with a version, a migration pipeline that advances exactly one version per step and refuses to skip, a validator that rejects negative XP/coins/points and duplicate instance ids, item quarantine that preserves unresolvable items rather than dropping them, atomic writes with temp/current/backup candidate recovery, and an autosave that only writes at documented safe points. Save is 13 KB with 60 storage items; save 0.23 ms, load+validate 0.19 ms.

**3.3 Deterministic generation.** **[FACT]** `RngStreams.Derive(runSeed, depth, stream)` gives each subsystem (Biome, Encounter, Loot, …) an independent reproducible stream keyed by run seed and depth, with further mixing per room and per chest source. Two chests never share rolls; a client can rebuild the host's dungeon from the seed. Generation is 2.2–2.8 ms average, 9.7–15.2 ms worst.

**3.4 Performance and lifecycle hygiene.** **[FACT]** `ProjectilePool`, `EffectPool` (cap 64, oldest recycled), `DamageNumberPool` (cap 48) and 24 pooled audio sources all confirmed reusing instances. Eight consecutive depth transitions produce constant live GameObject counts and −0.01 MB heap change. A hot-loop audit found no `FindObjectsByType`, LINQ or string building in `Update`/`FixedUpdate` bodies.

**3.5 Encounter containment.** **[FACT]** `EncounterBounds` binds every encounter actor to the interior of the room that spawned it and every mover consults it before committing a velocity, dash endpoint or knockback step. A boss cannot be pulled out of its arena, a charger cannot path through a doorway, and a player standing outside is not chased out. This is a class of bug most projects ship with, and it is solved here.

**3.6 The graphical inventory.** **[FACT/OPINION]** (`TestResults/StartHpDashIconHudProof/live_09_inventory_equip_live_hud_x2.png`) Five equipment slots with icon, name and rarity; a survivor portrait; a four-line ammo reserve readout; an 8-slot backpack grid; and a DETAILS pane carrying a written description plus structured stat rows and affix lines. `ItemTooltip` even builds candidate-vs-current comparisons. This is a strong, readable, complete screen.

**3.7 The Dungeon Merchant UI.** **[FACT]** (`live_13_merchant_item_selected_x2.png`) Icon, name, rarity, category, quantity, price, a details pane with prose and stat rows, a backpack-slots-free counter, explicit BUY/CLOSE. Clean and complete.

**3.8 The Main Menu.** **[FACT/OPINION]** (`TestResults/PolishPreview/ui_main_menu_x2.png`) A rail in forced perspective leading to a lit door, amber-on-charcoal, and the subtitle `SHELTER · EXPEDITION · EXTRACTION` which teaches the whole loop in three words. This is the single most confident piece of visual identity in the project.

**3.9 The HUD specification and implementation.** **[FACT]** `ui/91_DUNGEON_HUD.md` is unusually precise and the implementation matches: graphical weapon slots with rarity frames and active brackets, a dash icon with a continuous cooldown wipe, a room-graph minimap that only reveals visited/adjacent rooms, a room-title reveal on genuine entry, a low-HP vignette keyed to *effective* max HP that stands down under overlays, and an enemy-remaining chip with carefully scoped visibility rules.

**3.10 Run-start and depth-arrival health.** **[FACT]** Both fill to `PlayerStats.MaxHealth` (base + armor + accessory + affix + progression), exactly once, through the ordinary host-authoritative heal path, and provably not on room entry, menus, equipment changes or scene rebuilds. This was hard-won and is well protected by tests.

**3.11 Telegraph discipline.** **[FACT]** Every enemy and boss attack has an authored telegraph (0.3–1.4 s) and the attack direction is locked at telegraph start, so a committed attack stays dodgeable. `MovesetActorController` explicitly zeroes velocity the instant an attack is committed, which was a real fix for a real frame-ordering bug.

**3.12 Exploit hardening.** **[FACT]** The starter kit is unsellable and granted once; `ProgressionService.ReconcilePoints` refuses an allocation that no XP paid for; ammo resale was corrected from a 3,600-coin exploit to 15 % of purchase value; the expedition marker can only resolve as a failure on boot (no mid-run resume); `ExpeditionStartCoordinator` dedupes by transaction id.

---

## 4. Biggest Current Weaknesses

Ranked by how much they damage the finished game, not by how hard they are to fix.

### 4.1 Dead content: a large share of the loot chase has no runtime effect

This is the headline problem and it has four independent branches.

**(a) Accessory Intrinsics.** **[FACT]** Every one of the 16 accessory families has a fixed Intrinsic (`items/30_ACCESSORY_CATALOG.md`), authored into the assets as a `StatModifierEntry`. Cross-referencing every `StatId` against the gameplay assemblies for a reader (`GetMultiplier` / `GetPercent` / `GetFlat`) gives:

| Accessory | Intrinsic | Consumer |
|---|---|---|
| Runner's Watch | Movement Speed +5 % | `PlayerMovement.CurrentMoveSpeed` — **live** |
| Loader's Glove | Reload Speed +10 % | `RangedWeapon.CurrentReloadTime` — **live** |
| Dash Capacitor | Dash Cooldown −10 % | `PlayerDash` — **live** |
| Trauma Pendant | Healing Received +15 % | `ConsumableEffects` — **live** |
| Magnetic Coil | Pickup radius +3 tiles | `PickupAttractor` — **live** |
| Field Scope | Projectile Range +10 % | **none** |
| Combat Bracelet | Melee Attack Speed +8 % | **none** |
| Quickdraw Holster | Weapon Switch Speed +15 % | **none** |
| Cooling Module | Blaster Cooling Rate +15 % | **none** |
| Heat Sink | Blaster Heat/shot −10 % | **none** |
| Archer's Ring | Bow Charge Speed +12 % | **none** |
| Rangefinder | Projectile Speed +12 % | **none** |
| Ammo Pouch | Ammo Stack Capacity +25 % | **none** (see (d)) |
| Stabilizer | Weapon Spread −15 % | **none** |
| Impact Module | Knockback +15 % | multiplies an authored 0 |
| Shock Charm | Stagger Power +15 % | multiplies an authored 0 |

**11 of 16 accessory families are cosmetic.** The accessory slot is one of five equipment slots and a whole loot category.

Verification: `RangedWeapon` reads only `StatId.ReloadSpeed`, `StatId.WeaponDamage`, `StatId.Knockback` and `StatId.StaggerPower`; it takes `MagazineSize`, `ProjectileSpeed`, `Range` and the fire-rate cooldown (`1f / _definition.FireRate`) directly from the definition with no stat applied. `BowWeapon`, `BlasterWeapon` and `MeleeWeapon` each read only `WeaponDamage` plus the two zeroed impact stats.

**(b) Affixes.** **[FACT]** `AffixRollService` draws distinct affixes from the equipment family's `AffixPool`. Mapping each pool against the same consumer analysis:

| Pool | Affixes | Functional | Dead |
|---|---|---|---|
| `AffixPool_Ranged` | Damage, FireRate, ReloadSpeed, MagazineSize, ProjectileSpeed, Range, Knockback, StaggerPower | **2 / 8** | FireRate, MagazineSize, ProjectileSpeed, Range, Knockback, StaggerPower |
| `AffixPool_Blaster` | Damage, FireRate, ProjectileSpeed, Range, Knockback, StaggerPower | **1 / 6** | the other 5 |
| `AffixPool_Bow` | Damage, ProjectileSpeed, Range, Knockback, StaggerPower | **1 / 5** | the other 4 |
| `AffixPool_Melee` | Damage, Knockback, StaggerPower | **1 / 3** | Knockback, StaggerPower |
| `AffixPool_Accessory` | MovementSpeed, DashCooldownReduction, HealingReceived, ReloadSpeed, ProjectileSpeed, Knockback, StaggerPower | **4 / 7** | ProjectileSpeed, Knockback, StaggerPower |
| `AffixPool_Armor` | MaxHealth, DamageReduction, MovementSpeed, HealingReceived, DashCooldownReduction, KnockbackResistance, StaggerResistance | **7 / 7** | — |

**[INFERENCE]** Because Epic and Legendary roll 3 *distinct* affixes from the pool:
- An **Epic melee weapon always rolls Damage + Knockback + Stagger Power** — exactly one functional affix, always the same one. Epic and Legendary melee weapons are numerically identical to each other.
- An Epic ranged weapon's expected functional affix count is `3 × 2/8 = 0.75`. The most common outcome is **zero functional affixes on an Epic weapon**.
- An Uncommon melee weapon has a 2-in-3 chance of being a completely blank item.

This directly contradicts Core Pillar 2 ("Random whole-number affix rolls create better and worse versions of the same item"). **[FACT]** `02_CORE_PILLARS.md`.

Worse: **the UI advertises the dead affixes.** `ItemTooltip` renders every roll as `"<Affix> (affix) +N"` with no distinction, so the player will compare, buy, keep and discard items on the basis of numbers that do not exist in the simulation. **[FACT]** `Assets/Game/Scripts/UI/Inventory/ItemTooltip.cs:141-145`.

**(c) The whole player-side impact system.** **[FACT]** All 33 weapon assets carry `_knockback: 0` and `_staggerPower: 0` (`grep -h "_knockback:\|_staggerPower:" Assets/Game/ScriptableObjects/Items/*.asset` returns 33 zeros of each). The plumbing is complete and correct — `RangedWeapon`/`Bow`/`Blaster`/`Melee` all multiply the definition value by the wielder's stat and pass it into `ProjectileSpawnData` → `ImpactDispatcher` → `ImpactReceiver` — it just multiplies zero.

Consequences, all verified:
- `items/24_WEAPON_CLASSES.md` defines Shotgun as "close-range multi-pellet burst, **high knockback/stagger**". Not realised.
- `combat/42_STAGGER_KNOCKBACK.md` defines the system and says "Shotguns, rockets, and heavy melee can have higher knockback". Not realised.
- Enemies carry a fully authored resistance curve that can never matter: Grunt/Swarm/Shooter/Sniper 0 %, Bomber 10 %, Charger/Summoner 20 %, Shield Enemy 40 %, Brute 60 %, elites 80–95 %, bosses 95 %.
- `WallbreakerPassive` (Impact Module) and `ArcStaggerPassive` (Shock Charm) can never fire, because both are triggered by the player knocking back or staggering an enemy.
- The only player sources of impact in the entire game are `Special_ConcussionBlast` (the Crowdbreaker Legendary special: knockback 12, stagger 20), three consumables (stagger 2 / 4 / 20) and the Dash Capacitor's `Discharge` passive. Every other Legendary special ships with `_knockback: 0, _staggerPower: 0`.

Note the asymmetry: **enemies stagger and knock back the player normally** — 30+ enemy attack definitions carry knockback 1–9 and stagger 1–9. So Resilience (the attribute) is meaningful defensively; it is the offensive half of the system that is dead.

**(d) The same "adapter exists, nothing calls it" bug the Vitality pass just fixed.** **[FACT]** `ItemSlotContainer.SetAmmoCapacityBonusProvider` / `PlayerInventory.SetAmmoCapacityBonusProvider` exist and are correct. The only caller in the repository is `AccessoryFrameworkTests.cs:44`. No runtime code wires it, so the Ammo Pouch's +25 % capacity never applies. This is structurally identical to the `SkillStatSource` defect: a correct adapter, a passing test, and no runtime call site.

### 4.2 There is no power progression

**[FACT]** Maximum *sustainable* player damage bonus, from every permanent source that works:

| Source | Bonus |
|---|---|
| Power attribute at rank 10 | +10 % |
| One `Affix_Damage` roll (max) — affixes are distinct, so only one | +10 % |
| Combat Harness armor base | +4 % |
| **Total** | **+24 %** (global cap is +50 %) |

Situational additions: Damage Stim +20 % for 10 s, Steady Aim +12 % while stationary, Fresh Mag +15 % for 3 shots, Long Shot +15 % beyond 7 tiles.

**[FACT]** Enemy HP scaling (`DepthScalingConfig`): 100 % → 132 % (D5) → 170 % (D10) → 235 % (D20) → 290 % (D30) → 380 % (D50) → 550 % (D100).

**[INFERENCE]** The player's permanent offensive growth is ~24 %; enemy HP growth is unbounded. Weapon choice adds at most ~2× (the sustained-DPS band across all 33 weapons is 32–63 DPS, see §8), and that is a sidegrade, not progression. Therefore:

- Time-to-kill rises roughly linearly with depth and cannot be countered.
- A depth-30 Foundry Titan has 3,915 HP. The highest-DPS build in the game (Ghostedge at 62.7 sustained × 1.24 = 77.7 DPS) needs **50 seconds of uninterrupted damage**, realistically 2–3 minutes with dodging. At depth 50 that is 66 s uninterrupted; at depth 100, 95 s uninterrupted.
- Survivability holds up much better (max effective HP ≈ 155 before armor affixes, 40 % DR cap, enemy damage only 205 % at D50), so deep play does not get *lethal* — it gets **long**.

**[OPINION]** "Endless depth" currently converges on bullet-sponge attrition rather than an escalating skill test. This is the design consequence that most threatens the long tail.

### 4.3 The three biomes are the same game

**[FACT]** Extracted from all 63 room definitions: every biome has an identical room table — 2 Start, 5 Combat Small, 4 Combat Medium, 2 Combat Large, 1 Loot, 1 Treasure, 1 Merchant, 1 Medical, 2 Event, 2 Boss. Identical dimensions per size class (16×12 / 24×16 / 32×20, boss 36×24). Identical `_difficulty` values slot-for-slot (Small 1,1,2,1,2; Medium 2,2,2,3; Large 3,3). Identical `_supportsElite` flags on the same four slots. Identical `_selectionWeight` (1, Treasure 0.5). Identical `_supportedDoors` bitsets position-for-position. Every room is `_minDepth: 1, _maxDepth: 0` — **no room is gated by depth**.

**[FACT]** `EncounterDirector.IsEligible` filters by `UnlockDepth` and room exclude-tags only. `Biome` is carried in `EncounterContext` and never read. **All three biomes spawn the same nine archetypes.**

**[FACT]** The three biome hazards are numerically identical: `Hazard_AcidPool`, `Hazard_ElectrifiedRail` and `Hazard_FurnaceGrate` all have `_damageMin: 5, _damageMax: 8, _tickIntervalSeconds: 1, _knockback: 0, _staggerPower: 0, _affectsPlayers: 1, _affectsEnemies: 1`.

**[FACT]** There is one sprite set per enemy archetype; no biome art variants exist (`Assets/Game/Art/Characters/`).

**[FACT]** No loot table is biome-specific.

**[INFERENCE]** Answering the key question directly: **remove the art and you cannot tell which biome you are in, except by which of the six bosses appears (and, in the 5–25 % of depths that have one, which elite).** Biome identity is floor tiles, props, lighting tint, music and ambience.

**[FACT]** This sameness is *enforced*: `ContentCountValidator` asserts the identical per-biome distribution as a content contract (`production/126`), and passes 52/52.

### 4.4 Combat has no cost model

**[FACT]** Four independent findings that compound:

1. **Every weapon except the three shotguns has `_spreadDegrees: 0`.** There is no spread, no bloom, no recoil, no accuracy state anywhere.
2. **Movement is instantaneous velocity assignment** — `_rigidbody2D.linearVelocity = moveInput * CurrentMoveSpeed` (`PlayerMovement.FixedUpdate`). No acceleration, no deceleration, no friction, no momentum.
3. **There is no movement penalty for firing, reloading or switching**, and no weapon-switch duration at all (`WeaponLoadout.SelectSlot` is instant; no equip time exists in any definition or config).
4. **Aim assist is always on** — an 18° (mouse) / 24° (pad) cone bending shot direction toward the best hurtbox, with no player-facing toggle.

**[INFERENCE]** The player can strafe at full speed while firing with perfect accuracy and assisted aim, and can swap weapons instantly at any moment. The only in-combat resources are HP, ammo, the dash cooldown and reload downtime. The only skill axis is "read the telegraph, dodge, keep shooting." **[OPINION]** That is a legitimate arcade model and it is Soul-Knight-adjacent by intent — but it is a *thin* mastery curve for a game whose core loop asks for dozens of hours. `items/24_WEAPON_CLASSES.md` describes the SMG as having "closer-range spread" and the Pistol as "accurate"; neither statement is true in the build, so class identity is thinner than designed.

### 4.5 Combat is pure attrition: nothing drops from kills

**[FACT]** `RoomRuntime.OnEnemyDied` increments a counter and unregisters the handler. Nothing else. There is no loot roll, no coin drop, no ammo drop, no health drop from any enemy, elite or normal.

**[FACT]** All in-run reward comes from placed containers: ~25 % of eligible combat rooms get a Supply Chest with a floor of 2 per depth (`SupplyChestPlanner`), plus 0–2 Loot rooms, 0–1 Treasure, 0–1 Merchant, 0–2 Events, and the Boss Cache.

**[FACT]** Combat rooms lock their doors on entry and unlock only on clear (`RoomRuntime.Activate` → `SetDoorsLocked(true)`). **The player cannot flee a fight.**

**[INFERENCE]** Every combat is mandatory, costs ammo and HP, cannot be escaped, and returns only XP and passage. The classic roguelite reward rhythm — *enemy dies → something pops out* — is entirely absent. This is a significant contributor to the "shoot things, walk to the next room" feel identified in §6.

### 4.6 Every boss can be kited to death

**[FACT]** Boss arenas are 36×24 tiles. Boss move speeds are 1.6–3.2; the player's is 5.0 (5.5 with Mobility 10). Every boss's largest phase-1 trigger band is smaller than the arena:

| Boss | Largest phase-1 band | Arena |
|---|---:|---|
| The Conductor | 14 (Burst Cannon 4–14) | 36×24 |
| A.E.G.I.S. Core | 12 (Line Energy 0–12) | 36×24 |
| Tunnel Maw | 10 (Marked Leap 3–10) | 36×24 |
| The Foundry Titan | 10 (Rocket Barrage 3–10) | 36×24 |
| Scrap King | 10 (Auto Burst 3–10) | 36×24 |
| Subject Omega | 9 (Spore Burst 3–9) | 36×24 |

**[FACT]** `MovesetActorController.SelectAttack` returns the **first ready attack in list order whose band contains the player**, and `null` otherwise — in which case the boss simply walks toward the player in `Chase`.

**[FACT]** Sniper ranges are 18–21 tiles; Battle Rifle 14–16.

**[INFERENCE]** A player with a Sniper who keeps more than ~14 tiles between themselves and the boss is never attacked at all during phase 1, and outruns every boss by 56–212 %. Farline (51.5 avg × 1.24 = ~64 damage) kills a 1,050 HP Conductor in 17 shots; at ~3 shots per 6-second kite cycle that is ~40–50 seconds with zero damage taken. Phase-2 arena hazards (0–30 bands on Conductor/Titan/Omega) partially close this, but only for three of the six bosses and only below 50 % HP.

**[INFERENCE]** Deterministic priority selection also means boss behaviour is a pure function of `(distance, cooldowns)`. Once a player learns the list order and the bands, there is nothing left to read. See §10.

### 4.7 Co-op is not connected

**[FACT]** `ExpeditionScene` — the only run composer — builds exactly one `PlayerRig`, passes `isCoop: false` in five places, and its own class documentation calls it "The **solo** expedition run". `PlayerPresenceService` and `NgoPlayerEntityFactory` (the code that would spawn remote players) are constructed **only in tests**. `BaseSession` calls `Lobby.Join(LocalClientId, …)` and nothing else ever joins.

**[INFERENCE]** Linking a UGS project — the blocker named in `production/FINAL_SHIPPABLE_V1_REPORT.md` §14 — would not make co-op playable. The composition that puts two players into one dungeon does not exist yet. The extensive NGO sync work (`PlayerNetMotion`, `EnemyNetSync`, `HealthNetSync`, `LootAuthority`, `DungeonNetSync`, `ReconnectGrace`) is real and tested in isolation, but it is not assembled.

**[INFERENCE]** There is also a latent difficulty bug waiting behind that connection: `ExpeditionStartCoordinator.Apply` passes `snapshot.PartySize` into `ExpeditionService.Start`, which scales the whole dungeon (threat 175 %, normal HP 135 %, boss HP 220 % for a trio), while `ExpeditionScene` would still build one player.

### 4.8 The audio mix does not exist

**[FACT]** All 53 audio event definitions have `_volume: 1`, `_pitchMin: 1`, `_pitchMax: 1`, `_maxInstances: 4`, `_minIntervalMs: 0` and exactly one clip. The `AudioEventDefinition` schema supports clip variations, pitch randomisation, per-event gain and a repetition throttle — **none of it is authored**.

**[FACT]** Default bus levels are Master 1.0 / Music 1.0 / SFX 1.0 / Ambience 1.0, with the only authored relationship being `AmbienceCeiling = 0.4`.

**[INFERENCE]** A UI navigation click is mixed as loud as a rocket explosion. The Buzzsaw fires 11 times per second playing the identical unpitched sample with no minimum interval — this will read as a digital artefact rather than a gun.

**[FACT]** Music loops are 16–27 seconds (`music_*boss.wav` ~16 s, `*combat.wav` ~18 s, `*exploration.wav`/`shelter`/`mainmenu` ~27 s); ambience beds are ~12 s. **[INFERENCE]** A ten-minute depth plays the same 27-second exploration phrase roughly 22 times.

### 4.9 The dungeon renders as islands in a black void

**[FACT]** `ExpeditionScene` clamps the camera to the bounding box of the **whole generated layout**, not the current room (`_camera.SetVisibleBounds(bounds)` over `Generation.Layout.Placements`). Nothing is drawn between or outside rooms.

**[FACT]** A Combat Small room is 16×12 tiles = 512×384 reference pixels. The reference frame is 640×360. **A small room is narrower than the screen.** Eleven of the 21 rooms per biome are 16×12.

**[FACT]** First-hand from `TestResults/DepthSettingsDescriptionsNonCombatProof/smoke_windowed/dsnc_13_boss_room_no_enemy_hud.png`: roughly 40 % of the frame is pure black, with two lit rooms separated by a black vertical band.

**[INFERENCE]** For the majority of rooms the player will permanently see black bars or voids at the frame edges, and adjacent rooms read as disconnected islands. This undermines the "continuous underground sector" fiction and is the single most "unfinished-looking" thing in the game.

---

## 5. Player Journey Review

### First boot → Main Menu
**[FACT]** `MainMenuScreen` is PLAY / SETTINGS / QUIT with a PROFILE panel. **[OPINION]** Strong. The subtitle teaches the loop. Minor: the "Start a new profile" line overlaps the door art and the PROFILE body text is dim grey on near-black (low contrast).

### Shelter, first visit
**[FACT]** `ShelterOnboarding` runs four steps: choose display name → acknowledge starter kit → equip weapon and armor → start first expedition, each with one prompt line. The right column permanently shows an **AT RISK** panel stating that loadout and backpack are lost on failure while XP, Banked Coins and Storage are not.

**[OPINION]** The single most important and most surprising rule of the genre is communicated, plainly, at the right moment. Good. **[FACT]** There is no Help/Codex page (`ui/95` lists one as optional, "may"), so nothing explains rarity, affixes, ammo types, downed/revive or the descend decision before the player meets them.

### Character progression
**[FACT]** Six attributes, ten ranks each, one Skill Point per rank, shown with rank/cap, a description, and per-stat "now → next" previews, with MAX at the cap and disabled controls when unaffordable. **[OPINION]** Clear and honest. **[INFERENCE]** But it is a flat +N% list with no choices: with 60 points and 60 ranks available, **every player eventually maxes everything**, so allocation order is the only decision and it is temporary. There is no build identity here by design (`13`: "intentionally not a build-defining skill tree"), which is a legitimate choice — but it means the progression screen stops mattering permanently after ~60 levels.

### Loadout
**[FACT]** Five equipment slots, 8 backpack slots, ammo occupies slots. **[OPINION]** Good pressure. **[INFERENCE]** Because four ammo types can each claim a slot, a player carrying two ammo weapons plus loot is genuinely squeezed — that is good design.

### Enter dungeon → first room
**[FACT]** Start room is safe, the depth objective rides the room-title reveal (`DEPTH 1 — REACH THE BOSS`), and the minimap shows only visited and adjacent rooms. **[OPINION]** Objective is obvious. Good.

### First combat
**[FACT]** Doors lock on entry, the enemy-remaining chip appears, contextual prompts fire for MoveAim → Fire → Dash (at the first telegraph) → Reload (at the first empty magazine).

**[INFERENCE — the first serious friction]** The starter kit is a P9 Ranger (12–14 damage, 4 shots/s), a Field Knife, a Scrap Vest, one Bandage and **60 Light Ammo**. A depth-1 run is 4–8 combat rooms at threat budget 4–6 (roughly five enemies of ~30 HP), then a boss of 1,000–1,350 HP.

- Rooms: ~5 enemies × ~150 HP total ÷ 13 damage ≈ 12 hits minimum per room, realistically ~15. Six rooms ≈ **90 rounds**.
- Boss: 1,050 ÷ 13 ≈ **81 rounds**, with no misses.
- Supply: 72 rounds carried, plus ~2 Supply Chests. Ammo rolls are 70 % "useful type" weighted, so expect roughly 40–45 more Light.

**[INFERENCE]** A first-time player arrives at the boss door with roughly a quarter of the ammunition the boss requires. The designed fallback is the Field Knife — 1.2-tile reach, 80° arc — against a 1,050 HP boss whose Marked Rail Strike hits for 30–36. This is survivable by a skilled player and brutal for a new one. **[OPINION]** This is the highest-risk moment in the entire new-player experience and it happens in the first 20 minutes.

### First loot
**[FACT]** Pickups require walking to the item and pressing Interact; `PickupAttractor._baseRadiusTiles` defaults to 0 and is never set by `PlayerEntityBuilder`, so coins and ammo are **not** auto-collected without a Magnetic Coil. **[OPINION]** In a game with this much floor loot, per-pile button presses are friction most peers have removed.

### Merchant / events
**[INFERENCE]** At depth 1 the player has essentially no Carried Coins (chests give 10–25). Event prices are Locked Vault 250, Medical Station 150, Broken Machine 100; merchant equipment is 250–3,000+ and even an ammo bundle is 60–90. **Nothing in the in-run economy is affordable on depth 1–2.** It becomes affordable only because Carried Coins accumulate across depths within one expedition — which is a genuinely good pro-descend incentive, but it means the merchant and the paid events are decorative for the first two depths of every run.

### Boss → Transit → the decision
**[FACT]** Boss dies → Boss Cache spawns → transit activates → RETURN or DESCEND, with a free full heal to effective max HP on descend.

**[INFERENCE — the core tension is late]** Because the descend heal is free and total, and because rarity improvement is the only depth reward, depths 1–5 are a low-stakes formality: you are always at full HP, you are carrying little of value, and descending is close to strictly correct. The emotionally important moment the vision document names — *"standing at the post-boss Transit Car while carrying items the player does not want to lose"* — cannot occur until the player has accumulated real value, which is depth 5+. **[INFERENCE]** And every expedition starts at Depth 1 (§13), so that formality is replayed every single run.

### Death
**[FACT]** Solo death at 0 HP ends the expedition immediately; the RUN LOST screen lists the figures; XP is already committed and permanent.

### Return to Shelter → next run
**[FACT]** The Trader refreshes after every ended expedition, success or failure. **[OPINION]** Good — failure still advances something.

---

## 6. Core Gameplay Loop

**What creates fun [OPINION, grounded in the data]**
- Reading a telegraph and dashing through it. The telegraph discipline is real and the i-frames are honest.
- The backpack squeeze: deciding what to carry when ammo competes with loot.
- The descend decision, *once* the player is carrying value.
- Opening a Treasure Chest at depth 10+ where the Epic rate is 25 %.

**What creates friction**
- Per-pile pickup presses (§5).
- Grenades requiring an inventory swap to use (only one Active Consumable slot; in solo the inventory pauses the world), which makes 5 of the 10 consumables menu actions rather than combat actions. **[FACT]** `ConsumableUseAction` reads only `EquippedSlot.ActiveConsumable`.
- Ammo scarcity at the start, without a compensating drop from kills.

**What creates tension**
- Low ammo before a boss. This is currently the *only* reliable tension source inside a depth, and it is accidental rather than designed.
- Low-HP vignette + no retreat option.

**What creates boredom [INFERENCE]**
- Deep-run bullet sponges (§4.2).
- The same 27-second music loop (§4.8).
- Rooms with no reward: a combat room without a Supply Chest (75 % of them) is pure cost.

**What creates repetition [INFERENCE]**
- 11 combat rooms per biome. A depth draws ~5–9 non-boss rooms from a pool of 17, with duplicates discouraged — so roughly 40 % of a biome's rooms are seen per visit, and **essentially the whole biome within 3 visits**. With 3 biomes, a player has seen all 63 rooms in about 10 depths.
- One Merchant room, one Treasure room, one Medical room and two Event rooms **per biome, ever**.

**What creates mastery**
- Telegraph reading, positioning against ranged enemies (Shooter/Sniper/Bomber keep distance correctly via `PreferredDistance`), ammo budgeting, and route choice on branches.

**What creates meaningful decisions**
- Descend vs return; what to carry; which of three weapons at a Weapon Cache; whether to open a Cursed Chest. **[OPINION]** The Weapon Cache (choose 1 of 3) is the single best decision moment in the game and it is one of six event types with a 0–2 rooms-per-depth allowance.

---

## 7. Combat

**Movement.** **[FACT]** 5.0 u/s, instant velocity, no acceleration. Dash 17 u/s × 0.18 s = 3.06 tiles, 1.4706 s cooldown, 0.10 s i-frames. **[OPINION]** Responsive and correct for the genre; the dash numbers are well-tuned and the i-frame window is honest (short enough to require timing).

**Aiming.** **[FACT]** Fully independent 360° aim; assist cone 18°/24° scaled 0.75 for sniper, 0.65 for rocket, 0 for melee; it bends direction only, never locks, never auto-fires, never moves the cursor. **[OPINION]** A good implementation. **[OPINION]** It should be toggleable — some players will feel the 18° bend as their shots being taken away, and there is no setting for it.

**Shooting.** **[FACT]** Zero spread on 30 of 33 weapons; no recoil; no falloff; hard maximum range per weapon. **[INFERENCE]** Range is a binary cliff rather than a curve, which makes "range" a positioning constraint rather than a feel difference.

**Melee.** **[FACT]** Knife 1.0–1.2 tile reach with 70–80° arcs; Spear 2.6–3.0 tiles with 20° arcs. **[FACT]** Melee sustained DPS (46.8–62.7) is the **highest band in the game**, above every firearm, and costs no ammo. **[INFERENCE]** Combined with ammo scarcity and the player's speed advantage over most enemies, hit-and-run melee is close to a dominant strategy.

**Reload.** **[FACT]** `CurrentReloadTime = ReloadTime / (1 + ReloadSpeed%)`, capped at +40 %. Auto-reload on empty magazine when reserve allows. **[OPINION]** Correct and satisfying.

**Weapon switching.** **[FACT]** Instantaneous. No duration exists in any definition, config or component. **[INFERENCE]** This removes a decision (there is never a reason not to switch) and leaves `WeaponSwitchSpeed` — a stat with a global cap, an attribute half, an accessory intrinsic and a Legendary passive — with nothing to scale.

**Enemy contact and attacks.** **[FACT]** All 9 archetypes share one FSM: Idle → Chase → Telegraph → Attack → Recovery. Differences come from attack kind (MeleeContact / Projectile / Charge / Moveset / Lob), preferred distance, timings, the Shield Enemy's 80 % frontal reduction, and the Summoner. Ranged archetypes correctly back away below `PreferredDistance` (Shooter 4, Bomber 4.5, Sniper 8). **[OPINION]** Solid, readable, and enough variety for nine archetypes. There is no flanking, cover use or group coordination — appropriate for the scope.

**Crowd control.** **[FACT]** Effectively none available to the player (§4.1c). Smoke Grenade blinds normal enemies (`SmokeZone.IsLineOfSightBlocked`); Shock Grenade carries stagger 20. Those two consumables are the entire player CC toolkit.

**Is it fun after 20 runs? [OPINION]** The dodging is. The shooting is not, because nothing about it changes: no spread to manage, no recoil to control, no weapon that handles differently, no enemy that must be staggered rather than out-damaged.

---

## 8. Weapons

**[FACT]** Sustained DPS computed from the shipped assets. For magazine weapons, `sustained = avgDamage × magazine ÷ (magazine ÷ fireRate + reloadTime)`. For blasters, heat-limited: `min(fireRate, coolingRate ÷ heatPerShot) × avgDamage`. For bows, full-draw damage ÷ charge time. Melee = burst (no reload).

| Weapon | Class | Role | Current strength | Current weakness | Potential issue | Evidence | Recommendation (direction only) |
|---|---|---|---|---|---|---|---|
| P9 Ranger | Pistol | starter | 10-tile range, cheap ammo | 37.1 sustained DPS | Starter ammo budget ~½ of a depth-1 clear | §5, `StarterKitService` | Revisit starter Light Ammo count or add a kill-drop |
| Kestrel-12 | Pistol | fast sidearm | 38.5 DPS, 15-round mag | 9-tile range, 3.5 ammo/s | Near-identical to P9 | DPS table | Differentiate by handling, not numbers |
| Quickfang ★ | Pistol | Legendary | 42.8 DPS, `snapfire` | — | +11 % over Kestrel | asset data | Fine |
| Rattler-9 | SMG | spray | 46.7 DPS | 8-tile range, **6.7 ammo/s** | **Dominated by Wasp-45** on DPS, range *and* ammo economy | DPS table | Give it a real advantage (spread/handling) or reposition |
| Wasp-45 | SMG | controlled SMG | 47.3 DPS, 9 tiles, 5.3 ammo/s | — | Strictly better than Rattler-9 | DPS table | — |
| Buzzsaw ★ | SMG | Legendary | 56.8 DPS, `lead_bloom` | 7.1 ammo/s | Highest firearm DPS in the game | DPS table | Watch alongside ammo economy |
| AR-17 | Assault Rifle | baseline AR | 46.6 DPS | 12 tiles, 5.2 ammo/s | **Strictly dominated by Marauder A2** (lower DPS, shorter range, worse ammo) | DPS table | Needs a distinguishing property |
| Marauder A2 | Assault Rifle | heavy AR | 49.6 DPS, 13 tiles, 4.5 ammo/s | — | Dominates AR-17 on all axes | DPS table | — |
| Vanguard ★ | Assault Rifle | Legendary | 54.1 DPS, `overrun` | 5.4 ammo/s | — | DPS table | Fine |
| Sentinel BR | Battle Rifle | precision | 15 tiles, 2.0 ammo/s | 40.0 DPS | — | DPS table | Fine trade vs Hound |
| Hound BR | Battle Rifle | rapid BR | 43.3 DPS | 14 tiles, 2.6 ammo/s | — | DPS table | Fine |
| Judicator ★ | Battle Rifle | Legendary | 16 tiles, `piercing_line` | 39.5 DPS — below both regulars | Legendary with *lower* sustained DPS than the common Hound BR | DPS table | Verify this is intended |
| Breacher-12 | Shotgun | breacher | 6-tile range | **33.3 DPS, lowest non-rocket** | Class identity ("high knockback/stagger") not realised | `_knockback: 0` | Restore the impact identity or re-role |
| Scatter-8 | Shotgun | spread | 40.5 DPS, 8 pellets | 5-tile range | Same | asset data | Same |
| Crowdbreaker ★ | Shotgun | Legendary | 40.6 DPS, **the only weapon in the game that knocks back** (`concussion_blast`: knockback 12, stagger 20) | — | Its special is the sole surviving proof the impact system exists | `Special_ConcussionBlast` | Keep; use as the template |
| Longshot S1 | Sniper | long range | 20 tiles | **32.8 DPS — lowest in the game bar rockets** | Highest price (600) + scarcest ammo + worst DPS | DPS table | Value is kiting, not damage — see §4.6 |
| Needle M7 | Sniper | fast sniper | 37.7 DPS | 18 tiles, 1.0 ammo/s | Better DPS than Longshot | DPS table | — |
| Farline ★ | Sniper | Legendary | 21 tiles, `rail_shot` | 32.4 DPS | Enables the boss-kite exploit | §4.6 | Address via boss bands, not the weapon |
| Pipe Launcher | Rocket | single AoE | 65–80, 2-tile blast | **17.3 DPS**, 4 Heavy/shot (15 shots max carried) | Needs ~3 targets to match an AR | DPS table, `_explosionRadiusTiles: 2` | Check AoE compensates |
| Twin-Tube | Rocket | double AoE | 18.7 DPS, 2-round mag | 13 tiles | Better than Pipe Launcher | DPS table | — |
| Sunbreaker ★ | Rocket | Legendary | 70–85, `meteor_salvo` | 18.0 DPS | Most expensive class, lowest DPS | DPS table | — |
| Recurve Bow | Bow | fast draw | 42.3 DPS, no ammo | 0.65 s charge | Better DPS than Compound | DPS table | — |
| Compound Bow | Bow | heavy draw | 32–38 per shot | 35.0 DPS | Dominated by Recurve on DPS | DPS table | Differentiate by pierce/range |
| Stormstring ★ | Bow | Legendary | 41.2 DPS, `arrow_storm` | — | — | DPS table | Fine |
| Pulse Carbine B1 | Blaster | sustained energy | 40.0 DPS, **no ammo** | heat management | — | heat math | — |
| Arc Blaster B4 | Blaster | heavy energy | 38.5 DPS, no ammo | 10 heat/shot | — | heat math | — |
| Redline ★ | Blaster | Legendary | **45.0 DPS with zero ammo cost**, `overcharge_barrage` | 650 price | Best economics in the game: beats every sniper, shotgun, pistol, bow and battle rifle and never runs dry | heat math | Watch — likely the strongest practical weapon |
| Field Knife | Knife | starter melee | **54.2 DPS, no ammo** | 1.2 tiles | Out-DPSes the starter pistol (52.0) it is meant to back up | DPS table | Intentional? |
| Ripper Knife | Knife | fast melee | **57.5 DPS** | 1.0 tile, 70° arc | Beats every sniper, shotgun, pistol, BR, bow and blaster | DPS table | — |
| Ghostedge ★ | Knife | Legendary | **62.7 DPS — highest sustained in the game**, `blink_strike` | 1.2 tiles | Free, infinite ammo, top DPS | DPS table | Primary balance watch item |
| Scrap Spear | Spear | reach melee | 46.8 DPS | 2.6 tiles | **Dominated by Guard Lance** (lower DPS *and* less reach) | DPS table | Needs a distinguishing property |
| Guard Lance | Spear | long reach | 48.4 DPS, 3.0 tiles | — | Dominates Scrap Spear | DPS table | — |
| Railspike ★ | Spear | Legendary | 3.0 tiles, `impaling_charge` | 47.6 DPS | — | DPS table | Fine |

**Cross-cutting findings [FACT/INFERENCE]:**

1. **The DPS band is 32–63 for 30 of 33 weapons** (rockets 17–19 are the outlier). Across 11 classes and five rarities, the entire arsenal fits in a 2× window. **[OPINION]** That is a very flat catalogue for a loot game; there is no "this weapon changed my run" moment available in the numbers.
2. **Melee occupies the top of that band** while costing nothing and being immune to the ammo economy.
3. **Snipers occupy the bottom** while costing the most and using the scarcest ammo; their value is entirely positional.
4. **Three strict dominations exist**: AR-17 < Marauder A2, Scrap Spear < Guard Lance, Rattler-9 ≲ Wasp-45.
5. **Legendary weapons are ~+9–20 % over their class's best regular**, which correctly matches the design intent that Legendary value is the mechanic, not the numbers.
6. **Class identity rests on four levers only**: damage, fire rate, hard range, ammo type. With spread, recoil, handling, falloff and switch time all absent, two weapons with similar DPS and range are functionally the same weapon.

---

## 9. Enemies

**[FACT]** Nine archetypes, all sharing `EnemyController`'s FSM, gated by `UnlockDepth`, costed by `ThreatCost`, composed by `EncounterDirector` into 2–4 roles against a depth threat budget.

| Enemy | Unlock | HP | Purpose | Counterplay | Telegraph | Obsolescence risk |
|---|---:|---:|---|---|---|---|
| Grunt | 1 | 30 | baseline melee pressure | outrange / kite (player 5.0 vs 3.0) | 0.4 s swing | High — never stops being trivial |
| Shooter | 1 | 24 | forces movement, holds 4 tiles | close the gap or break LoS | visible projectile | Low — projectile pressure stays relevant |
| Swarm | 1 | 10 | numbers, threat 0.5 | AoE / melee arcs | contact | Medium — no AoE identity to punish them |
| Charger | 3 | 45 | commits you to a dodge | sidestep, punish recovery | 0.7 s direction lock | Low — best-designed normal enemy |
| Brute | 5 | 90 | anchor, 60 % impact resist | kite; resist stat currently inert | Ground Slam | Medium |
| Bomber | 7 | 40 | area denial | move out of the telegraphed zone | landing zone shown | Low |
| Shield Enemy | 9 | 55 | **positional puzzle** — 80 % frontal projectile reduction | flank, or use AoE/explosions | bash | Low — the only enemy that demands a specific answer |
| Sniper | 11 | 30 | long-range threat, holds 8 tiles | break line, rush | visible tracking aim line | Low |
| Summoner | 14 | 65 | priority target, 2–3 Swarms / 9 s, max 6 | kill it first | — | Low |

**[OPINION]** This is a well-chosen set. Charger and Shield Enemy are genuinely good: each teaches a specific verb (dodge-commit; flank). Shooter/Sniper/Bomber create real positioning.

**[INFERENCE] Weaknesses:**
- **Grunt and Swarm never become interesting.** They scale in HP only, so at depth 30 they are the same behaviour with 2.9× the HP — pure time cost.
- **Brute and Shield Enemy's headline stat is dead.** Brute's 60 % and Shield's 40 % stagger/knockback resistance can never matter (§4.1c), so Brute is just "slow enemy with more HP".
- **No enemy combinations are authored.** `EncounterDirector` picks 2–4 roles by seed and fills to a threat budget; there are no designed pairings (e.g. "Shield in front, Sniper behind"). Variety is statistical, not compositional.
- **Enemy XP does not scale with depth** (`XpValue => _definition.BaseXp`). See §14.

---

## 10. Bosses

**[FACT]** Six bosses, 4–5 authored attacks each, two phases at 50 % HP (`BossController`), phase 2 prepending arena-hazard attacks and applying a timing multiplier. Each boss is pinned to a specific arena by a `boss:<id>` room tag. Both bosses of a biome share one music track.

Extracted attack data (band in tiles / telegraph / recovery / cooldown / damage):

| Boss | HP | Attacks (in priority order) |
|---|---:|---|
| The Conductor | 1,050 | EmergencyDash 0–4 (0.4/0.5/6, 8–10) · ProjectileSweep 2–10 (0.8/1.0/5, 12–14 ×7) · BurstCannon 4–14 (0.6/0.8/3, 10–12) · MarkedRailStrike 0–12 (1.1/1.2/7, 30–36) · RailLineHazard 0–30 (1.4/0.6/9, 20–24) |
| Tunnel Maw | 1,150 | ClawSweep 0–2.2 · Bite 1.5–4.5 · Roar 0–8 · MarkedLeap 3–10 (28–34) · BurrowEmerge 2–9 (24–28) |
| Foundry Titan | 1,350 | ArmSweep 0–2.6 · HydraulicSlam 0–2.8 (30–36) · FurnaceBlast 2–7 (×5) · RocketBarrage 3–10 · ReactorBurn 0–30 |
| Scrap King | 1,000 | HeavySwing 0–2.2 (22–28) · CombatRoll 1.5–6 · GrenadeThrow 4–9 · AutoBurst 3–10 |
| Subject Omega | 1,250 | ArmSlam 0–2.6 (28–34) · Charge 2.5–8 · VineZone 2–8 · SporeBurst 3–9 (×6) · OrganicDenial 0–30 |
| A.E.G.I.S. Core | 1,050 | RadialRing 0–5 (×10) · Reposition 1–6 · TripleBurst 3–10 · LineEnergy 0–12 (28–34) · RingWhileLine 0–6 (×12) |

**[OPINION] What is good:** the attack sets are varied in shape (single, sweep, radial, zone, charge, marked-ground), telegraphs are long enough to read (0.4–1.4 s), damage respects `combat/40`'s 25–40 heavy-attack ceiling, and no attack is untelegraphed. Arenas are large enough for real movement. Phase 2 correctly "increases familiar pressure" rather than changing rules.

**[INFERENCE] What is a problem:**

1. **Behaviour is fully deterministic.** `SelectAttack` returns the first ready attack in list order whose band contains the player. There is no weighting, no randomisation, no context. **A boss's entire behaviour is a lookup on (distance, cooldowns).** After three or four fights there is nothing left to read — the player is executing a memorised loop, which is exactly the "pattern memorisation without adaptation" failure mode.
2. **All six are kitable to death** (§4.6).
3. **Bosses become sponges with depth** (§4.2): the Foundry Titan needs ~50 s of *uninterrupted* best-in-game DPS at depth 30, ~95 s at depth 100.
4. **Only three bosses have a phase-2 full-arena attack** (Conductor `RailLineHazard`, Titan `ReactorBurn`, Omega `OrganicDenial` — all 0–30 bands). Tunnel Maw, Scrap King and A.E.G.I.S. Core have no answer to a long-range kiter in either phase.
5. **Boss repetition is high early.** Depth 1–3 will draw from only 6 bosses, and every expedition starts at depth 1 (§13).

**[FACT]** Not sponges in the "unfair burst" sense: no boss can one-shot, and `combat/46` explicitly forbids it. Not visual chaos either — projectile counts peak at 12 (RingWhileLine).

---

## 11. Rooms and Level Design

**[FACT]** 63 rooms: per biome 2 Start (16×12), 5 Combat Small (16×12), 4 Combat Medium (24×16), 2 Combat Large (32×20), 1 Loot, 1 Treasure, 1 Merchant, 1 Medical, 2 Event (all 16×12), 2 Boss (36×24).

**[FACT]** Generation: 9–10 rooms at depths 1–3, 10–11 at 4–8, 10–13 at 9+; main path 6–9; 1–3 branches of 1–3 rooms; Boss at the end and never adjacent to Start; Merchant never adjacent to Start or Boss; Elite never directly after Start or before Boss; duplicates discouraged; up to 32 regeneration attempts on validation failure.

**[OPINION] Strengths:** the graph rules are well chosen, branches genuinely create route decisions (optional content is preferentially placed on them), and the minimap's progressive reveal makes those decisions legible. Room containment is exemplary.

**[INFERENCE] Weaknesses:**

1. **The pool is too small for the consumption rate.** 17 non-boss rooms per biome; a depth consumes 5–9 of them. Two or three visits exhaust a biome. Ten depths exhaust all 63 rooms.
2. **Exactly one Merchant / Treasure / Loot / Medical room exists per biome, forever.** Every Rustworks merchant visit, in every run, for the life of the game, is the same `Room_Rust_Merchant_01` layout.
3. **No depth gating.** Every room is `_minDepth: 1`; a depth-30 dungeon uses the same rooms as depth 1. There is no "you only see this room deep down" moment.
4. **Room geometry does not vary within a room.** `_allowsAuthoredSizeVariation: 0` on every room inspected.
5. **A 16×12 room is smaller than the 640×360 frame** (512×384 px), so small rooms are fully visible on entry — no exploration within a room, and permanent black margins (§4.9).
6. **Difficulty values are unused as a design lever across biomes** because they are identical slot-for-slot.

**[FACT]** From the captured frames (`dsnc_11`, `dsnc_13`, `01_direct_crosshair_hit_hp_reduced.png`): room dressing reads as algorithmic — identical crates in perfect rectangles or paired columns — and large floor areas carry only sparse repeating decals. Cover exists as prop clusters but does not read as deliberate combat geometry (no chokepoints, no sightline breaks placed against the enemy roster).

---

## 12. Biomes

| | Ruined Metro | Rustworks | Overgrown Labs |
|---|---|---|---|
| **Visual identity** | tiles/platforms/cables | rust/pipes/furnaces | glass/terminals/overgrowth |
| **Gameplay identity** | none | none | none |
| **Enemy identity** | none — same 9 archetypes | none | none |
| **Room identity** | none — identical table, dimensions, difficulty, doors | none | none |
| **Hazard identity** | Electrified Rail: 5–8 dmg / 1 s | Furnace Grate: 5–8 dmg / 1 s | Acid Pool: 5–8 dmg / 1 s |
| **Loot identity** | none — shared tables | none | none |
| **Audio identity** | 3 tracks + 1 ambience | 3 tracks + 1 ambience | 3 tracks + 1 ambience |
| **Boss identity** | The Conductor, Tunnel Maw | Foundry Titan, Scrap King | Subject Omega, A.E.G.I.S. Core |
| **Elite identity** | Tunnel Stalker, Railguard | Scrap Executioner, Crusher Unit | Mutated Brute, Prototype X-7 |

**[INFERENCE]** *"If I remove the art and look only at gameplay, can I still tell which biome I am in?"* — **No**, except by which boss appears at the end and, in the 5–25 % of depths that contain one, which elite. Every other axis is identical by data.

**[FACT]** Biome selection is per depth, weighted 40/40/20 against the previous biome, drawn from a dedicated deterministic RNG stream. **[OPINION]** The selection model is good; there is simply nothing different to select between.

**[OPINION]** The art *does* differentiate them (`FINAL_SHIPPABLE_V1_REPORT` §11 confirms biome identity is recognisable with the UI hidden), and the music/ambience sets are per biome. So the biomes feel different for the first few minutes and then stop mattering.

---

## 13. Extraction / Depth System

**[FACT]** The decision: after a boss dies, RETURN TO SHELTER (secure carried items, bank carried coins) or DESCEND (new biome, +1 depth, everything still at risk, free full heal to effective max HP). Co-op requires unanimous living-player approval to continue. No individual extraction, no party splitting.

**What argues for descending [FACT]:**
- Loot rarity improves substantially. Standard table Epic 0.9 % (D1) → 6.4 % (D10) → 15 % (D20) → 21.5 % (D30). Legendary 0.1 % → 0.6 % → 1.0 % → 1.5 %. Treasure/Elite/Boss sources use Improved and Strongly Improved tables.
- Carried Coins accumulate across depths, which is what finally makes the in-run merchant and the paid events usable.

**What argues against descending [FACT/INFERENCE]:**
- Everything carried is lost on death.
- Ammo attrition compounds and enemies drop none.
- **Coin rewards do not scale with depth.** `EconomyConfig._coinRewardPercentPerDepth: 0`, and `PriceService.ScaleCoinReward` has no caller outside a test. A Boss Cache pays 80–140 coins at depth 1 and at depth 50.
- **XP does not scale with depth.** A depth-50 Grunt with 380 % HP awards the same 12 XP as a depth-1 Grunt.
- **Rarity tables stop improving after depth 30.**

**[INFERENCE] Consequences:**

1. **XP per minute and coins per minute are both maximised at Depth 1**, because enemies die fastest there for identical reward. A player optimising meta-progression should farm shallow and never descend. The only reason to descend is item rarity.
2. **The risk/reward curve inverts past depth 30**: risk keeps rising (HP 290 %→550 %), reward stops (rarity capped, coins flat, XP flat). There is no reason to go past 30 other than self-imposed challenge, and the game does not acknowledge the attempt (see 4 below).
3. **Depths 1–5 are a low-stakes formality.** Free full heal every depth, little carried value, gentle scaling (108 %/116 %/132 %).
4. **[FACT]** `ExpeditionState` always starts at `Depth = 1`, and `PlayerProfile` has no deepest-depth field. **There is no depth checkpoint, no "start at depth N", and no record of the deepest depth reached.** For a game whose identity is "endless depths", the player's depth record is not stored, not shown, and not usable.

**[OPINION]** This is the most consequential design gap after §4.1. A player who has learned to survive depth 15 must replay depths 1–14 every session, through rooms they have exhausted, against bosses they have memorised, for XP and coins that are worth no more than they were on the first run.

**[FACT]** The free full heal on descend is also worth naming: it removes HP attrition as a between-depths pressure entirely, leaving ammo as the only carry-over constraint.

---

## 14. Progression

**[FACT]** XP is permanent and committed immediately (even on failure). `XPToNextLevel(L) = 250 + 50(L−1) + 5(L−1)²`, cap Level 61, total 454,550 XP, 60 Skill Points, six attributes × 10 ranks = exactly 60. Respec costs 2,500 Banked Coins. Attribute effects (all verified live as of the progression pass): Vitality +2 Max HP, Power +1 % Weapon Damage, Mobility +1 % Movement Speed, Recovery +2 % Healing Received, Handling +1 % Reload Speed and +1 % Weapon Switch Speed, Resilience +2 % Knockback and Stagger Resistance — per rank.

**[INFERENCE] Pacing:** a depth-1 run yields roughly 300–480 enemy XP plus a 650–800 boss = ~1,000–1,200 XP. 454,550 ÷ ~1,100 ≈ **400+ depths** to reach Level 61. Since XP does not scale with depth, that number does not improve with skill.

**Problems [FACT/INFERENCE]:**

1. **No choice.** 60 points buy 60 ranks and there are exactly 60 ranks. Every maxed player has an identical character. Allocation *order* is the only decision, and respec exists at 2,500 coins. This is explicitly intended (`13`: "intentionally not a build-defining skill tree"), so it is not a defect — but it means the progression screen is a temporary distraction, not a long-term goal. **[OPINION]**
2. **Half of Handling is invisible.** `WeaponSwitchSpeed` has no consumer; the attribute's description promises "Faster reloads and swaps" and delivers only reloads.
3. **Resilience is half-invisible in the other direction**: both its stats work, but only defensively, because the player can never apply impact.
4. **Every increment is sub-perceptual.** +2 HP, +1 % damage, +1 % move speed per rank. The Character panel's now→next preview (`Max HP +10->+12`) is honest and well presented, but the player will never *feel* a rank.
5. **Equipment is the real progression**, as designed — but equipment progression is capped at +24 % damage and gated behind mostly-dead affixes (§4.1, §4.2).

**Dead progression space [INFERENCE]:** after Level 61 (~400 depths) there is nothing left to earn except items, and item power is capped. The game has no answer for the player who has everything.

---

## 15. Economy

**[FACT]** One currency (Coins), two domains (Carried during an expedition, Banked after extraction). Rarity multipliers ×1.00/1.35/1.90/3.00/5.00; sell = 35 % of buy, rounded to 5; ammo resale = 15 % of purchase value (corrected from a prior 3,600-coin exploit). Weapon base prices 250–700, armor 300–650, accessories 300, consumables 60–1,500. Event prices scale with depth and cap.

**Conceptual player states [INFERENCE]:**

| State | Situation |
|---|---|
| **Early run (D1–2)** | ~0–70 Carried Coins. Nothing in the merchant or events is affordable — not even a 60-coin ammo bundle, reliably. The in-run economy is inert. |
| **Mid run (D3–6)** | Coins have accumulated across depths (~250–700). Ammo bundles, Broken Machine (100), Medical Station (150) become real; a Locked Vault (250–1,000) becomes a genuine decision. **This is where the in-run economy starts working.** |
| **Late run (D10+)** | Coins are plentiful relative to flat prices, because income is flat but the number of depths visited grows. Merchant equipment becomes affordable. |
| **Early account** | Banked coins near zero; Trader level 1 (4 offers, 1.9 % Epic). Income is dominated by selling extracted gear, not by found coins. |
| **Mid account** | Trader level 2 (2,500) then 3 (7,000) are the two big sinks, alongside Storage upgrades and the 2,500 respec. |
| **Advanced account** | Trader 3 gives 8.4 % Epic per offer, ~3 equipment offers per run, refreshed after **every** expedition including failures. |

**Findings [FACT/INFERENCE]:**

1. **Coin pickups are economically irrelevant.** A Boss Cache pays 80–140; a single extracted Rare AR sells for ~300 (450 × 1.9 × 0.35 ≈ 300). Selling extracted gear is the dominant income source by a wide margin. **[INFERENCE]** This weakens the "carried coins are lost on death" stake — the real stake is always the items.
2. **Coins and XP do not scale with depth; event prices do** (`250 + 25×(D−1)`). Affordability per depth therefore worsens; it is only saved by cross-depth accumulation.
3. **The Shelter Trader competes with the dungeon and may win.** Trader level 3's 8.4 % Epic rate is comparable to a depth-10 chest (6.4 %) and far better than a depth-1 chest (0.9 %) — and it is **completely risk-free**, refreshes after failures, and is paid for by selling loot from safe shallow runs. **[INFERENCE]** "Farm depth 1, sell everything, buy from the Trader" is a plausible dominant strategy that directly undercuts the extraction thesis. This deserves a designer's attention more than any number in the table.
4. **No inflation risk found.** Sinks (Trader 2,500 + 7,000, Storage, respec 2,500) are meaningful and prices are flat.
5. **No poverty spiral.** The Starter Kit rescue grant plus the Starter Loadout fallback guarantee a playable run from zero. **[OPINION]** Well handled.
6. **No dead currency** — there is only one, as designed.

---

## 16. Loot

**[FACT]** Four chest types (Supply / Equipment / Treasure / Boss Cache), three rarity tables (Standard / Improved / Strongly Improved) banded by depth to 30, ammo-awareness at 70 % useful types, party-size gating for co-op-only drops (Defibrillator), per-source deterministic RNG substreams.

**[OPINION]** The *structure* of the loot system is excellent — clean tables, honest depth bands, no junk loot, no crafting materials, one shared pickup in co-op.

**[INFERENCE] The problem is not the tables, it is what the items do:**

- An Epic weapon is usually numerically identical to an Uncommon one of the same type, because most affixes are inert (§4.1b).
- 11 of 16 accessories are inert (§4.1a), so an accessory drop is a coin token 69 % of the time.
- Armor is the **only** category where every affix works and where base values differ meaningfully (Max HP +10 to +35, DR +2 % to +9 %, plus distinct third properties). **[OPINION]** Armor is the one genuinely satisfying loot category in the game today.
- Rarity's most reliable effect is its **price multiplier**, i.e. its sell value — which means loot is mostly a currency drop wearing an item's clothes.

**[FACT]** The Boss Cache is well-built: 100 % Equipment, 100 % Coins (80–140), 60 % Additional Item, 100 % Ammo, 60 % Consumable, on the Strongly Improved table (depth 1: 2 % Legendary, 10 % Epic). **[OPINION]** The post-boss moment is mechanically rewarding; it is the item semantics that let it down.

---

## 17. UI / UX

**Strengths [FACT/OPINION]:** Main Menu; graphical inventory; Dungeon Merchant; HUD weapon/consumable/dash icon slots; minimap progressive reveal; room-title reveal; low-HP vignette keyed to effective max HP; the Character Station's now→next previews; the AT RISK panel; the event notice line with explicit refusal reasons (`BROKEN MACHINE: NOT ENOUGH COINS (100 NEEDED)`); full keyboard/mouse **and** controller navigation on every screen with an explicit focus-stack model; disabled-state rendering that is read live every frame.

**Concrete problems:**

1. **[FACT] The Shelter Trader is a bare text list.** `StationPresentation.Trader` renders `DisplayName` + `Price` only. No rarity, no icon, no affixes, no details pane, no comparison. In the captured frame `ui_shelter_trader_x2.png` two offers are both literally named "Buzzsaw" at 350 C and 475 C with **nothing on screen to explain the difference**. This is the primary long-term gear acquisition surface and it is markedly weaker than the in-run Merchant, which has all of that.
2. **[FACT] No status-effect UI.** `ui/91` specifies "Small status-effect icons/timers" under player state. `HudSnapshot` has no status list. Three timed-buff consumables exist (Combat Stim +20 % move 8 s, Damage Stim +20 % damage 10 s, Armor Injector +20 % DR 10 s) and the player has **no indication** that a buff is active or when it ends.
3. **[FACT] Top-centre overcrowding.** In `dsnc_13` the boss bar, the room title (`PRIME GREENHOUSE / BOSS`), the tutorial line and the event notice are all stacked in the top band simultaneously, overlapping and competing. The tutorial line is drawn *through* the room title.
4. **[FACT] A large black rectangle sits beside the boss bar** in `dsnc_11` (roughly x 800–1140, y 0–100). It reads as a rendering artefact or an un-skinned panel and is very visible.
5. **[INFERENCE] The tooltip advertises dead affixes.** `"Magazine Size (affix) +15"` is rendered identically to `"Damage (affix) +8"` although only one exists in the simulation.
6. **[FACT] Low contrast in Shelter body text.** Grey-on-dark-grey in the PARTY / AT RISK / PROFILE panels, worsened where the Shelter background art shows through panels that have no opaque backing.
7. **[FACT] Name truncation** in the ON THE COUNTER list (`Armor Inject…`).
8. **[FACT] Grenades are menu actions.** Only `EquippedSlot.ActiveConsumable` can be used; swapping requires opening the inventory, which pauses the world in solo.
9. **[FACT] Coins and ammo need a button press each** without a Magnetic Coil.

**[OPINION] Terminology is consistent** (Shelter / Expedition / Depth / Transit / Carried vs Banked Coins / At Risk), and the game is careful about it. Good.

---

## 18. Visual Direction

**Strongest elements [OPINION, from the captured frames]:** the Main Menu composition; the UI skin (amber-on-charcoal, bracket/notch focus language, a pixel font that disambiguates `0/O`, `1/I/l`, `5/S`); item icons; weapon HUD slots with rarity frames; telegraph shapes (hollow, high-contrast).

**Weakest elements [FACT/OPINION]:**

1. **The black void (§4.9).** The most damaging single visual issue. Up to ~40 % of a frame is empty black, and rooms read as disconnected islands.
2. **Floor readability.** In `dsnc_11` and `01_direct_crosshair_hit_hp_reduced.png` the floors are near-uniform mid-grey with sparse repeated decals. The Overgrown Labs "overgrowth" in that frame is a single small green patch. Floors carry almost no biome signal and almost no depth cue.
3. **Player-vs-enemy silhouette.** In `01_direct_crosshair_hit_hp_reduced.png` the player and the Grunt are both small brown-grey humanoids of near-identical mass and outline. **[OPINION]** At gameplay distance, *"which one is me"* is not instantly answerable. This is the readability issue that matters most in a top-down shooter, and `FINAL_SHIPPABLE_V1_REPORT` §3 already flags character art as the weakest category by the project's own assessment.
4. **Algorithmic prop placement.** Crates in perfect hollow rectangles and paired columns read as generated, not composed.
5. **Damage numbers** are white on mid-grey — low contrast.
6. **Door readability.** The door in `dsnc_11` is a small brown rectangle that does not read as a door at a glance.

**[OPINION] Does RUINRAIL look like one coherent game?** The UI absolutely does — it is a confident, consistent system. The world does not yet: the world layer is a flat grey substrate with sparse props, floating in black, populated by figures that are hard to tell apart. The gap between UI quality and world quality is the visual story of the project right now.

**[FACT]** Per the project's own classification, nothing is a placeholder; 44 character-art roles are the honest weak category and the pipeline will accept hand-drawn sheets at the same paths with no code change.

---

## 19. Audio

**[FACT] Architecture is good:** 53 events on 5 buses, 11 music tracks (menu, shelter, 3 biomes × exploration/combat/boss), 6 stingers, 3 ambience beds; deterministic role resolution (`MusicStateResolver`); an ambience ceiling at 0.4 × SFX so ambience can never mask combat; separate Master/Music/SFX/Ambience sliders plus mute; pooled sources with oldest-steal; Vorbis streaming for beds and PCM decode-once for short cues after a measured regression.

**[FACT] Content is complete:** 53/53 events have clips; 73 WAVs on disk. (`production/AUDIO_EVENT_AUDIT.md` claiming 0/53 is stale — see §2.2.)

**[FACT] The mix does not exist:**
- All 53 events: `_volume: 1`, `_pitchMin: 1`, `_pitchMax: 1`, `_minIntervalMs: 0`, one clip each.
- All four buses default to 1.0.

**[INFERENCE] Consequences:** no volume hierarchy (a UI click equals a rocket); no pitch variation on any sound, so an 11-shots-per-second SMG plays the identical sample 11 times a second with no throttle; one clip per event means no round-robin, so every Grunt dies with exactly the same sound.

**[FACT/INFERENCE] Loop lengths are too short:** 16 s boss, 18 s combat, 27 s exploration, 12 s ambience. A ten-minute depth loops the exploration bed ~22 times; a 90-second boss fight loops its bed ~6 times.

**[FACT] Coverage gaps by design:** one `enemy.hit` / `enemy.death` for all 21 enemy types; 7 weapon-fire events for 33 weapons; no footsteps. **[OPINION]** The weapon and enemy consolidation is a reasonable scope decision; the missing mix is not.

**[FACT] Verification honesty:** real-device audibility has been probed outside Unity in prior passes and the smoke records per-voice evidence (`audio_runtime_evidence.txt`), so "audio plays" is verified. "Audio sounds good" is not, and has never been claimed.

---

## 20. Multiplayer

**[FACT] What exists and is tested:** `PartyLobby` with Ready validation; `MultiplayerTerminalService` (Solo / Host / Join, join codes, error model); `NetworkSessionController`; `IMultiplayerServices` abstraction over UGS with `UnityMultiplayerServices` + `NgoNetworkDriver` composed by default in a release build; `HostAuthority` enumerating every host-owned decision; `PlayerNetMotion` / `NetworkPlayerMotion` with server-time interpolation; `NetworkPlayerCombat` (owner sends intents, host runs the unchanged weapon components); `EnemyNetSync`, `HealthNetSync`, `DungeonNetSync`, `WeaponNetSync`; `LootAuthority`; `ReconnectGrace`; `PartyReviveAuthority`; transit voting requiring unanimous living approval; an authored `PlayerNetworkEntity.prefab` registered with NGO. `MultiplayerGateTests` drives a full solo/duo/trio flow over deterministic local doubles with item/coin/XP conservation asserted at every step.

**[FACT] What does not exist:** any runtime code that composes a co-op *run*. `PlayerPresenceService` and `NgoPlayerEntityFactory` are constructed only in tests. `ExpeditionScene` builds one `PlayerRig`, passes `isCoop: false` five times, registers only the local player in `PartyLifeRoster`, and reveals rooms for the local player only.

**[INFERENCE] Architectural risks, in order of cost to fix later:**

1. **The run composer is solo-shaped.** `ExpeditionScene` is an 892-line composition root that assumes one player, one HUD, one inventory, one pause. Making it multi-player is not a wiring task; it is a structural change to the largest file in the project. **This is the expensive one, and it gets more expensive with every feature added to that file.**
2. **Party-size scaling is already plumbed but unguarded.** `ExpeditionStartCoordinator` passes `snapshot.PartySize` into `ExpeditionService.Start`, which scales threat 175 %, normal HP 135 % and boss HP 220 % for a trio. If presence is connected before the run composer is, a trio lobby will produce a trio-scaled dungeon with one player in it.
3. **Solo-only assumptions in UI pausing.** `isCoop: false` drives `TimeScalePause` behaviour for inventory, merchant, weapon cache, pause and run-failed. In co-op these must not pause the world; the flag exists but is hardcoded.
4. **Static service locators must be reset per session** (`ProjectileVisualCatalog.Active`, `RoomDoorLock.SkinResolver`, `WorldObjectVisual.Resolver`, `NetworkPlayerObject.VisualComposer`, `DamageAuthority.LocalIsAuthoritative`). These already cause order sensitivity in tests; in a host/client process they are a correctness hazard.
5. **Loot is a single shared pickup** by design, and `LootAuthority` handles the race — good — but the in-run inventory is per-player and the Dungeon Merchant debits `CarriedWallet`, which is per-expedition, not per-player. Worth a design pass before connecting.

**[FACT]** No claim of verified live multiplayer is made anywhere, correctly. The honest status is: **architecture prepared and unit-proven; the game is solo.**

---

## 21. Technical Architecture

**[OPINION] Overall: strong.** This is a well-structured Unity project by any standard.

**Strengths [FACT]:** 12 assemblies with a sensible dependency direction (Core ← Gameplay ← Dungeon/UI/Audio/Presentation/Networking ← App); static definitions, runtime state and save data kept separate as `technical/111` demands; ScriptableObjects for all balance data with no balance constants in behaviour scripts (spot-checked and holds); deterministic seeded RNG with named streams; pooling everywhere it matters; explicit composition roots (`GameApp`, `ExpeditionScene`, `BaseSession`, `PlayerRig`) rather than scattered singletons; no `FindObjectsByType`/LINQ/string-building in hot loops; disposables with symmetric subscribe/unsubscribe.

**Risks [FACT/INFERENCE]:**

1. **Mutable global statics as service locators.** Six of them (§20.4). They are set by composition roots and reset by tests, but nothing enforces it. This is the proven cause of PlayMode order sensitivity.
2. **`ExpeditionScene` is 892 lines and growing** and is the single point where every system is wired. It already carries camera, lighting, HUD, inventory, merchant, weapon cache, pause, tutorial, audio, minimap, vote and depth-rebuild responsibilities. **[OPINION]** It is the most likely future maintenance problem in the codebase, and the co-op work has to go through it.
3. **`SmokeRunner` is a nine-file partial class** (`SmokeRunner.cs` + 8 feature partials, ~2,700 lines) that has become the de-facto integration test. It is valuable, but it accretes one partial per pass and its checks are order-coupled (a failure in an early stage masks all later stages).
4. **Fragile initialization order is a real category here.** Two already-fixed bugs of this shape are documented in-tree: the Charger keeping chase velocity through the first telegraph frame (frame-ordering dependent) and `PlayerStatsBinder.Configure` rebuilding `PlayerStats` and dropping caller-registered sources (fixed by making the binder own the allocation). **[INFERENCE]** The "correct adapter, no runtime caller" bug class (§4.1d) is the same family and is not yet eliminated.
5. **UI is built entirely in code** (`UiKit`, `ScreenLayout`, `UiBuild`) with no prefabs. **[OPINION]** Excellent for testability and pixel discipline, costly for iteration speed — every visual tweak is a code change plus a rebuild, which materially slows UI polish.
6. **Editor art/audio generation is part of the build path** (`GameContentCatalogBuilder.Build()` runs inside `ReleaseBuildTool.BuildForTarget`). Convenient, but it couples release builds to the editor-only generation assemblies.

---

## 22. QA / Testing

**[FACT]** 1,647 tests (888 EditMode, 759 PlayMode), 57,475 lines of test code, ~0.83 test-to-code ratio. Validators: `FinalProductionValidator`, `ContentCountValidator`, `PresentationValidator`, `AssetPipelineValidator`, `ReleasePathVisualScan`, `ArtProductionContract`, `AnimationAssetAudit`, `AudioAssetAudit`, `MusicAssetAudit`, `ItemDescriptionValidator`, `AttributeDescriptionValidator`, `CompletionAssetManifest`, `VisualSliceGate`. Two built-player smokes (headless + windowed) with 334 checks.

**[OPINION] This is a genuinely impressive QA apparatus** — and it is also the clearest illustration in the project of why automated correctness is not the same as a good game.

**Where the suite produces false confidence [FACT]:**

1. **It tests that systems obey their specs, not that content produces effects.** Every one of the findings in §4.1 passed every gate. `AccessoryPassiveTests.LockIn_Extra25SpreadReduction_After1SecondContinuousFire_UntilFiringStops` asserts exact percentages of `WeaponSpreadReduction` — **a stat no system in the game reads.** The test is correct, passing, and meaningless.
2. **`ContentCountValidator` counts, it does not exercise.** 52/52 exact — 33 weapons exist, 16 accessories exist. It cannot tell that 11 of those accessories do nothing.
3. **No test asserts a *gameplay consequence* of an affix or an intrinsic.** The progression pass introduced exactly this style of test for the six attributes (`CharacterProgressionAttributeProofTests`, with a consumer-scan validator) and it immediately pinned a real gap (`WeaponSwitchSpeed`). That pattern is not applied to affixes, intrinsics, or weapon impact data.
4. **No balance, TTK or pacing tests.** Nothing asserts that a starter player can complete depth 1 with the ammunition provided, that weapon DPS lands in an intended band, or that time-to-kill at depth 30 is bounded.
5. **No long-session/soak test** beyond 8 depth cycles for memory. Nothing plays 50 depths.
6. **No controller-specific or accessibility tests**, though controller navigation is structurally covered by the focus-list tests.
7. **Seed sensitivity is a known live issue.** `CombatAimCollisionProofTests.LiveRun_…` composes a live dungeon from a **clock-derived** run seed; run five times in isolation it fails 2/5 with different assertions failing and a different biome/seed each time. It is not a regression detector in its current form.
8. **`FinalMvpAuditTests` reads `TestResults/PlayMode-results.xml`**, so one flaky PlayMode failure also fails EditMode — the flake amplifies.

**[OPINION] The single highest-value test to add** is a "every granted stat has a consumer, and every authored item property produces a measurable runtime delta" gate, generalising `AttributeDescriptionValidator` to weapons, affixes and accessories.

---

## 23. Replayability / Retention

**After 5 runs [INFERENCE]:** the player has seen most of one or two biomes' rooms, 2–4 of the 6 bosses, probably no elite (5–10 % per depth at these depths), and has a few levels. Novelty is still doing the work.

**After 20 runs [INFERENCE]:** all 63 rooms seen. All 6 bosses seen and their deterministic attack orders internalised. All 9 normal enemies seen. Maybe 2–3 elites seen. Level ~20–30 of 61. The loop is now visibly "enter dungeon → clear 5–8 identical-feeling rooms → fight a memorised boss → descend or leave."

**After 50 runs [INFERENCE]:** nothing structurally new remains. Elites are the only content still being discovered, and only because they are rare (`EliteChancePercent` 5/10/15/20/25 % per dungeon). Level ~60. Item hunting continues, but the ceiling is +24 % damage and most affixes are inert.

**After all weapons seen [INFERENCE]:** because the DPS band is 32–63 and class identity rests on four levers, "a new weapon" is rarely a new experience. The Legendary specials (11 of them) are the real variety and they are the rarest drop.

**After all upgrades unlocked [INFERENCE]:** no goal remains. There is no deepest-depth record, no achievement, no unlock, no cosmetic, no challenge mode, no seeded daily. The profile stores XP, coins, storage, loadout and skills — and nothing about what the player has *done*.

**[INFERENCE] The honest summary:** RUINRAIL currently has enough systemic variety for roughly 10–20 hours and then becomes "enter dungeon → shoot enemies → collect loot → boss → extract" with the variation coming almost entirely from which of 63 known rooms the generator picks.

**[OPINION] The four cheapest levers that would change this**, in order of impact per unit of work:
1. Make loot actually vary the run (fix §4.1 — this is data + small consumers, not new systems).
2. Record and use the deepest depth reached (a profile field, a UI line, and optionally a start-depth choice).
3. Raise elite frequency substantially — six hand-authored mini-boss fights are currently near-invisible content.
4. Give bosses non-deterministic attack selection (weighted picks instead of first-ready).

---

## 24. New Player Experience

**The first 30 minutes [INFERENCE], stage by stage:**

| Stage | Assessment |
|---|---|
| Name entry | Clear, validated, explains the rules inline. |
| Starter kit | Explained, listed by name. |
| Equip gear | Prompted explicitly. |
| At-risk rule | **Communicated well** by the permanent AT RISK panel. This is the rule most extraction games fumble and RUINRAIL gets it right. |
| First room | Objective clear (`DEPTH 1 — REACH THE BOSS`), minimap legible. |
| First combat | Prompts fire in the right order (move → fire → dash at first telegraph → reload at first empty mag). |
| **First ammo crisis** | **Unexplained.** Nothing teaches that ammo is finite, that it comes only from chests, or that the knife is the fallback. The player discovers this by running dry. |
| First loot | Requires learning that pickups need a button press; rarity/affix meaning is never explained. |
| First merchant | Cannot afford anything. No explanation why. |
| First boss | **The hard wall** (§5): likely out of pistol ammo, facing 1,000+ HP with a 1.2-tile knife. |
| Death | The RUN LOST screen states the loss clearly; XP persistence is not explained at that moment. |
| Return | Trader refreshed; no guidance on what to do with banked coins. |

**[INFERENCE] The two genuine confusions** are (a) the ammo economy and (b) what rarity/affixes mean. Neither has a tutorial prompt and there is no Codex page.

**[OPINION] This does not need a tutorial level.** It needs two things: the ammo economy made visible (the HUD already shows reserve; a one-time prompt when reserve drops below one magazine would carry it), and the starter ammo budget reconciled with a depth-1 clear.

---

## 25. Accessibility / QoL

**[FACT] Present:** display mode, resolution, VSync, frame-rate limit with apply/revert confirmation; Master/Music/SFX/Ambience sliders and mute; **full rebinding for keyboard/mouse and gamepad** with reset; screen-shake toggle; damage-numbers toggle; hit-flash toggle; tutorial prompts toggle and reset; full controller navigation of every screen with a focus-stack model, live disabled states, and shape-based focus cues (corner brackets + a 2 px selection notch) that survive a colourblind read; a pixel font explicitly designed to disambiguate `0/O`, `1/I/l`, `5/S`, `8/B`, `2/Z`; rarity shown as a **word** (`UNCOMMON`) as well as a frame colour.

**[FACT/OPINION] Absent:**
- **No aim-assist toggle or strength slider.** The 18°/24° cone is always on.
- **No UI scale / text size option.** At 640×360 reference with integer upscaling this is likely acceptable, but it is untested against real low-vision use.
- **No colourblind palette option** (mitigated by rarity words and shape-based focus).
- **No hold/toggle options** for fire or aim.
- **No input-buffering** (verified absent from the weapon/dash paths).
- **No motion/flash reduction** beyond the hit-flash toggle; the low-HP vignette pulses at ~1 Hz with no way to disable it.
- **No difficulty or assist options** (defensible for a roguelite).
- **No status-effect timers** (§17.2) — an accessibility issue as much as a UX one.

---

## 26. Content Production

**[FACT] Cheap to add today:**
- **A weapon.** One ScriptableObject + one 32×32 sprite + a price-table row. `RangedWeapon`/`MeleeWeapon`/etc. are class-agnostic and data-driven.
- **An enemy.** One `EnemyDefinition` + one animation set. `EncounterDirector` picks it up automatically via `UnlockDepth`/`ThreatCost`/`SpawnTags`.
- **An elite or boss.** `EnemyAttackDefinition` assets plus a moveset list; `MovesetActorController` is fully data-driven, including phase-2 hazard lists.
- **An affix or accessory.** One asset plus a pool entry — **but see the risk below.**

**[FACT] Expensive or risky today:**

1. **Adding an item property that needs a consumer.** The current failure mode — author the data, wire the stat, forget the consumer, ship a dead item — has already happened at least six times. There is nothing in the pipeline that catches it. **[OPINION] This is the most important production risk in the project**, because it silently degrades every future content addition.
2. **A room.** Requires a hand-authored prefab with markers, door sockets, tile painting and a validator pass. 22,606 tile cells were repainted across 63 rooms in one pass — the tooling exists (`Editor/Rooms/*RoomSet.cs`) but room authoring is code-driven layout specs, not a visual editor.
3. **A biome.** Requires 21 rooms, a tile set, prop packages, a lighting look, 3 music tracks, 1 ambience, 2 elites, 2 bosses — **and, because `ContentCountValidator` enforces the exact per-biome room distribution, it must match the existing shape exactly.** That contract makes biomes expensive *and* prevents them from being structurally distinct.
4. **UI.** Code-built screens mean every visual change is a code change (§21.5).
5. **Audio.** Adding an event is cheap (one asset + one clip), but there is no mix to slot into, so each addition inherits the flat 1.0 default.
6. **Anything touching the run.** Must go through `ExpeditionScene` (§21.2).

**[OPINION] The one production change worth making before more content:** a validator that fails when an authored item property, affix or intrinsic has no runtime consumer. It would have caught every finding in §4.1 and will pay for itself on the next content pass.

---

## 27. Player Persona Review

**A) New player**
- *Annoyed by:* running out of ammo without warning; the depth-1 boss with a knife; picking up every coin individually; not knowing what "+15 Magazine Size (affix)" means (or that it means nothing).
- *Misunderstands:* that enemies drop nothing; that the merchant is unaffordable by design early; that rarity mostly changes sell price.
- *Stops playing because:* the first boss wall, ~20 minutes in.
- *Plays another run because:* the AT RISK panel made the stakes clear and the RUN LOST screen showed the XP was kept.

**B) Casual player**
- *Annoyed by:* replaying depths 1–5 every session; 27-second music loops; no status timers.
- *Misunderstands:* why descending is worth it (the only real reason — rarity — is never stated).
- *Stops playing because:* the loop stops changing around run 10–15.
- *Plays another run because:* the Trader refreshed and there might be something good.

**C) Experienced roguelite player**
- *Annoyed by:* no build variety (progression is a flat +N% list everyone maxes); affixes that change nothing; bosses with deterministic attack order; no run modifiers, no boons, no branching choices beyond the Weapon Cache.
- *Misunderstands:* nothing — they will read the systems quickly and correctly.
- *Stops playing because:* there is no build to discover. This persona is the least served by the current design.
- *Plays another run because:* Legendary specials are genuinely distinct and worth chasing.

**D) Extraction-game player**
- *Annoyed by:* the inability to retreat from a room; extraction only at fixed post-boss points; carried coins being economically irrelevant so the "lose your coins" stake is hollow.
- *Misunderstands:* nothing — the at-risk rule is well communicated.
- *Stops playing because:* the greed decision is only interesting from depth 5 onward and they must replay 1–4 to reach it every time.
- *Plays another run because:* the post-boss decision, when it finally has stakes, is the real thing.

**E) Hardcore player**
- *Annoyed by:* the power ceiling (+24 % damage) against uncapped enemy HP; deep bosses as multi-minute sponges; rarity capped at depth 30; **no record of their deepest depth**.
- *Misunderstands:* nothing.
- *Stops playing because:* the game does not acknowledge depth achievement in any way. There is literally nothing to show for reaching depth 40.
- *Plays another run because:* they set their own goals — which the game should be capturing and does not.

**F) Co-op player**
- *Annoyed by:* co-op not being playable (§4.7).
- *Plays because:* they cannot, yet.

**G) Controller player**
- *Annoyed by:* aim assist with no toggle; grenades requiring a menu; no hold/toggle options.
- *Well served by:* complete controller navigation of every screen, full rebinding, shape-based focus cues, and a per-scheme glyph system that reflects actual rebinds. **[OPINION] Controller support is better here than in most indie top-down shooters.**

---

## 28. Critical Improvements

> Grouping is by value, not by a score. Nothing here is implemented.

### C1 — Make the dead content live
- **Area:** Items, stats, combat.
- **Evidence:** §4.1 (a)–(d). 11/16 accessory intrinsics, 6/8 ranged affixes, 5/6 blaster, 4/5 bow, 2/3 melee, all 33 weapons' knockback/stagger, and `SetAmmoCapacityBonusProvider`.
- **Why it matters:** this is the loot game's loot. Core Pillar 2 is not currently true.
- **Player impact:** every item decision the player makes is partly fictional; Epic weapons are usually identical to Uncommon ones.
- **Technical impact:** each dead stat needs either a consumer in the owning component (fire-rate cooldown, magazine size, projectile speed/range, blaster heat/cooling, bow charge, weapon-switch duration, spread) or an honest removal from the data.
- **Design impact:** requires design decisions the docs do not currently answer — e.g. is there a weapon-switch duration at all? Should shotguns knock back?
- **Suggested direction:** treat it as one pass with a per-stat decision table: *consume it*, *remove it from pools and catalogues*, or *record it as deliberately deferred*. Wire the cheap ones first (`MagazineSize`, `ProjectileSpeed`, `ProjectileRange`, `FireRate`, `MeleeAttackSpeed`, `AmmoStackCapacity` are all one-line reads in components that already hold the stats provider). Author non-zero `_knockback`/`_staggerPower` on shotguns, rockets and heavy melee per `combat/42`.
- **Complexity:** medium (mostly small consumers + a data pass).
- **Regression risk:** medium — turning six stats on at once changes effective balance everywhere. Do it behind the existing rank-sweep/proof test pattern.

### C2 — A "no dead content" validator
- **Area:** QA / production.
- **Evidence:** §22. Every §4.1 finding passed 1,647 tests and 13 validators.
- **Why it matters:** without it, C1 will regress and every future content pass can reintroduce the bug.
- **Player impact:** none directly; it protects all future impact.
- **Suggested direction:** generalise `AttributeDescriptionValidator`'s consumer scan to all `StatId`s, all affix pools and all accessory intrinsics; fail on any granted stat with no reader; keep a pinned, explicitly documented exception list (today: `WeaponSwitchSpeed`).
- **Complexity:** low. **Regression risk:** low.

### C3 — Decide what co-op is, before `ExpeditionScene` grows further
- **Area:** Multiplayer / architecture.
- **Evidence:** §4.7, §20. The composer is solo-shaped and is the largest file in the project.
- **Why it matters:** "1–3 players" is in the vision statement and on the box. The cost of retrofitting rises with every pass.
- **Technical impact:** either commit to extracting a multi-player-capable run composer now, or explicitly de-scope co-op for V1 and stop paying to maintain the network layer.
- **Suggested direction:** make the call explicitly and record it in `04_SCOPE_AND_NON_GOALS.md`. If co-op stays, the next structural step is separating "the run" from "the local player's presentation" inside `ExpeditionScene`.
- **Complexity:** high. **Regression risk:** high.

### C4 — Fix the first-run ammo wall
- **Area:** Economy, new player experience.
- **Evidence:** §5, §19. ~170 rounds needed, ~115–135 obtainable.
- **Why it matters:** it is the most likely point of first-session abandonment.
- **Suggested direction:** any one of — raise the Starter Kit's Light Ammo; guarantee a Supply Chest on the main path before the boss; or introduce a small ammo drop from enemies (which would also address §4.5). **Do not** simply lower boss HP; the problem is supply, not the boss.
- **Complexity:** low. **Regression risk:** low.

### C5 — Author the audio mix
- **Area:** Audio.
- **Evidence:** §4.8, §19. 53/53 events at volume 1.0, zero pitch variation, no repetition throttle, 16–27 s loops.
- **Why it matters:** the mix is the difference between "audio exists" and "the game sounds good", and it is the loudest first impression after the visuals.
- **Suggested direction:** per-event gain by category; pitch ranges on repeated impacts (weapon fire, enemy hit, footstep-class sounds); `_minIntervalMs` on high-rate events; 2–3 clip variations on the most-repeated events; longer music beds.
- **Complexity:** medium (data authoring, no code). **Regression risk:** low.

---

## 29. High-Value Improvements

### H1 — Give the world something to stand on outside room bounds
- **Evidence:** §4.9, §18.1. Camera clamps to the whole layout; nothing is drawn between rooms; 16×12 rooms are narrower than the frame.
- **Impact:** the biggest single change to how finished the game looks.
- **Direction:** a dark substrate/backdrop layer under the layout, or camera clamping to the current room's bounds, or connective tunnel art between placements. Any of the three removes the void.
- **Complexity:** low–medium. **Regression risk:** low (presentation only).

### H2 — Player/enemy silhouette separation
- **Evidence:** §18.3, and the project's own assessment of character art as the weakest category.
- **Direction:** the pipeline accepts hand-drawn sheets at the same paths with no code change. An outline/rim treatment on the player, or a distinct player palette, would help immediately without new art.
- **Complexity:** low (outline) to high (art pass). **Regression risk:** low.

### H3 — Record and use the deepest depth reached
- **Evidence:** §13.4, §23, §27E. `PlayerProfile` has no such field; every run starts at Depth 1.
- **Impact:** gives the endless-depth fantasy a scoreboard, and opens the door to a start-depth option later.
- **Direction:** a profile field + a Shelter readout + the expedition summary. Whether to allow starting deeper is a separate design decision with real extraction-economy consequences.
- **Complexity:** low for the record; medium for start-depth. **Regression risk:** low / medium.

### H4 — Non-deterministic boss and elite attack selection
- **Evidence:** §10.1. `SelectAttack` returns the first ready in-band attack.
- **Direction:** seeded weighted selection among *all* ready in-band attacks, keeping determinism for replay/network by drawing from the encounter stream.
- **Complexity:** low (one method). **Regression risk:** medium — several boss tests assert specific attack sequences.

### H5 — Close the boss kite window
- **Evidence:** §4.6. Every boss has a band ceiling below its arena size, and every boss is slower than the player.
- **Direction:** a long-range or arena-wide attack in phase 1 for the three bosses that lack one, or a "no attack available" fallback behaviour (reposition/dash-close) instead of plain chase.
- **Complexity:** low (data + one FSM branch). **Regression risk:** medium.

### H6 — Raise elite frequency
- **Evidence:** §11, §23. 5/10/15/20/25 % per dungeon means six hand-authored mini-bosses are nearly invisible.
- **Direction:** this is a pure data change in `DungeonGraphRules`/`DepthScalingConfig`, but it changes pacing and reward density, so it needs a designer's decision rather than a bump.
- **Complexity:** trivial. **Regression risk:** low mechanically, medium for balance.

### H7 — Bring the Shelter Trader up to the Dungeon Merchant's standard
- **Evidence:** §17.1. Two identically named offers at different prices with nothing on screen to distinguish them.
- **Direction:** reuse `MerchantView`'s presentation (icon, rarity, category, details pane, affix lines) for the Shelter Trader.
- **Complexity:** medium. **Regression risk:** low.

### H8 — Status-effect display
- **Evidence:** §17.2. Three timed buffs, no indication of any of them; `ui/91` specifies it.
- **Complexity:** low. **Regression risk:** low.

---

## 30. Medium Improvements

- **M1 — Enemy kill drops.** §4.5. Even a small chance of ammo or coins would restore the core reward rhythm and partially solve C4. *Design decision required: it changes the attrition model deliberately.*
- **M2 — Base pickup attraction radius > 0.** §5, §17.9. Turns per-pile button presses into walking over things. The Magnetic Coil would still add +3 tiles on top.
- **M3 — A quick-use path for grenades.** §17.8. Either a second consumable slot or a modifier key that throws the first grenade in the backpack.
- **M4 — Depth-gated rooms.** §11.3. Every room is `_minDepth: 1`. Even a handful of deep-only rooms would give depth a visible identity.
- **M5 — Biome gameplay identity.** §12. Differentiated hazards (the three are numerically identical today), biome-weighted enemy pools, or biome-specific room-type counts. *Note that `ContentCountValidator` currently enforces the identical distribution, so the contract has to change first.*
- **M6 — Ammo-economy tutorial prompt.** §19. One prompt the first time reserve drops below one magazine.
- **M7 — Aim-assist toggle in Settings.** §7, §25.
- **M8 — Deterministic seed for `CombatAimCollisionProofTests`.** §22.7. It is currently a 40 %-failure coin flip that also fails EditMode through `FinalMvpAuditTests`.
- **M9 — Fix the HUD top-band overcrowding and the black rectangle beside the boss bar.** §17.3, §17.4.
- **M10 — Resolve the three strictly dominated weapons.** §8: AR-17, Scrap Spear, Rattler-9.

---

## 31. Low-Priority Polish

- **L1** — Refresh the stale `production/AUDIO_EVENT_AUDIT.md` (§2.2).
- **L2** — Contrast pass on Shelter body text and the Main Menu PROFILE panel (§17.6).
- **L3** — Damage-number contrast (§18.5).
- **L4** — Door sprite readability (§18.6).
- **L5** — Name truncation in the trader counter list (§17.7).
- **L6** — Longer music loops (§4.8) — listed separately from C5 because it is asset work, not mix work.
- **L7** — Prop placement variety in room dressing (§11, §18.4).
- **L8** — A Help/Codex page covering ammo, rarity, affixes, at-risk rules, downed/revive (`ui/95` already permits it).
- **L9** — Low-HP vignette pulse toggle for motion comfort (§25).

---

## 32. Freeze / Do Not Change

> These are solved. Future passes should not redesign them without new evidence of a concrete problem.

| System | Why it is frozen |
|---|---|
| **`PlayerStats` pipeline** | Correct single-aggregation model with caps applied once; keyed sources; order-independent. Now correctly fed by progression, equipment, passives and buffs. |
| **Save / persistence** | Versioned envelope, one-step migrations, validator, quarantine, atomic writes with candidate recovery, documented safe points. Fast and leak-free. |
| **Seeded RNG model** | Named independent streams keyed by run seed + depth, with per-room and per-source mixing. Reproducible across peers. |
| **Encounter containment (`EncounterBounds`)** | Solves a hard class of bug thoroughly and is well tested. |
| **Run-start and depth-arrival health rules** | Hard-won, precisely scoped, heavily regression-tested. Do not touch. |
| **Graphical inventory** | Complete, readable, with comparison support. |
| **Dungeon Merchant UI** | Complete and clear. |
| **Main Menu** | The strongest piece of visual identity in the project. |
| **HUD layout and rules** | Well specified and faithfully implemented, including the careful visibility scoping of the enemy chip and vignette. |
| **Pooling and perf work** | Zero-growth across depth cycles, clean hot loops. |
| **Controller / focus-stack navigation** | Better than most peers; shape-based focus cues survive a colourblind read. |
| **Character Station presentation** | Rank/cap, description, now→next preview, MAX state, disabled states, all data-derived. |
| **Starter-kit and rescue-kit safety net** | No softlock is possible; nothing of value can be minted. |
| **Exploit hardening** | Point reconciliation, ammo resale, marker resolution, transaction dedupe. |
| **Character, weapon, VFX and inventory art** | Accepted by the owner; the only art issue raised here is silhouette separation (H2), which is additive, not a redesign. |

---

## 33. Technical Risks

| Risk | Evidence | Consequence if unaddressed |
|---|---|---|
| **"Adapter with no caller" bug class** | §4.1d — `SetAmmoCapacityBonusProvider` is test-only, exactly like `SkillStatSource` was | Silent dead content on every future pass |
| **`ExpeditionScene` as a 892-line single wiring point** | §21.2 | Co-op retrofit cost grows monotonically; merge/regression risk concentrates |
| **Mutable global service-locator statics** | §21.1 — six of them | Proven PlayMode order sensitivity; a real hazard in a host/client process |
| **Party-size scaling plumbed ahead of player spawning** | §20.2 | A trio lobby would produce a trio-scaled dungeon with one player |
| **`SmokeRunner` as a nine-partial ordered integration test** | §21.3 | Early-stage failures mask later stages; grows one partial per pass |
| **Clock-seeded live-run test** | §22.7 | ~40 % flake that also fails EditMode via the audit |
| **Release build depends on editor generation assemblies** | §21.6 | Build/runtime coupling; harder to move generation offline later |
| **No balance/TTK regression tests** | §22.4 | Data changes (like C1) cannot be validated automatically |

---

## 34. Design Risks

| Risk | Evidence | Consequence |
|---|---|---|
| **Loot that does not change the run** | §4.1, §16 | Pillar 2 is not true; the loot chase is hollow |
| **No power curve against an unbounded difficulty curve** | §4.2 | Endless depth becomes sponge attrition |
| **Shallow farming may dominate deep diving** | §13, §15.3 | The extraction thesis is undercut by its own economy |
| **Biomes are cosmetic** | §12 | Content that costs three times as much as it plays |
| **Deterministic boss behaviour** | §10.1 | Bosses stop being encounters and become executions |
| **Progression with no choices** | §14.1 | No build identity; the only build lever is equipment, which is capped |
| **No retreat, no mid-run extraction** | §4.5, §6 | Tension is front-loaded into one decision per depth |
| **No record of achievement** | §13.4, §23 | Nothing to show for the thing the game is named after |
| **Combat with no cost model** | §4.4 | Thin mastery curve; class identity cannot express itself |

---

## 35. Open Questions

These need a designer's answer, not an engineer's. `06_OPEN_DECISIONS.md` currently states there are none; these are the ones this review surfaced.

1. **Is there a weapon-switch duration in V1?** `WeaponSwitchSpeed` has a global cap (+50 %), half of the Handling attribute, an accessory intrinsic (+15 %) and a Legendary passive — and nothing to scale. Either a base duration exists or all four should be retired.
2. **Should shotguns, rockets and heavy melee knock back and stagger?** `combat/42` and `items/24` say yes; all 33 weapons say zero.
3. **Does the SMG have spread?** `items/24` says "closer-range spread"; the data says `_spreadDegrees: 0` for all non-shotguns.
4. **Should enemies drop anything?** The current answer is "no", and it is not stated anywhere as a deliberate choice.
5. **Should XP and coins scale with depth?** Today they do not, which makes depth 1 the optimal farm.
6. **What is the intended answer to "why descend"** beyond item rarity, and **"why go past depth 30"** once rarity caps?
7. **Is co-op in V1?** §4.7 / C3.
8. **Should the player be able to start deeper than Depth 1?** This is the single biggest retention lever and it has real extraction-economy consequences.
9. **Is the Field Knife out-DPSing the starter pistol intended?** (54.2 vs 52.0, and it costs no ammo.)
10. **Is the Judicator Legendary meant to have lower sustained DPS than the common Hound BR?** (39.5 vs 43.3.)
11. **Should biomes differ in gameplay**, and if so, is `ContentCountValidator`'s identical-distribution contract still correct?
12. **What is the intended time-to-kill at depth 30+**, given the +24 % player ceiling?

---

## 36. Suggested Future Roadmap

> Sequencing only. No work is implied or started by this document.

**Phase A — Truth (before any new content)**
Make what exists do what it says. C1 (dead content), C2 (the validator that keeps it dead-free), M10 (dominated weapons), and answers to Open Questions 1–4. Nothing else should be built until an item's stats mean what they say, because every future item inherits the problem.

**Phase B — The first hour**
C4 (ammo wall), M6 (ammo prompt), M2 (pickup radius), H8 (status effects), L8 (Codex). This is the cheapest block of work with the highest effect on whether a new player reaches their second run.

**Phase C — Presentation**
H1 (the void — biggest visual win per unit of effort), C5 (audio mix), H2 (silhouettes), M9 (HUD crowding), L2–L7.

**Phase D — Depth and retention**
H3 (depth record, then the start-depth decision), H4 (boss variety), H5 (kite window), H6 (elite frequency), M4 (depth-gated rooms), and a designer's answer to Open Questions 5–6 on the depth reward curve.

**Phase E — The structural decision**
C3: commit to co-op and restructure the run composer, or de-scope it and say so. Deferring this is itself a decision, and it gets more expensive each pass.

**Phase F — Long tail**
Only after A–D: build variety (the thing persona C wants), more Legendary specials, biome gameplay identity (M5), enemy combination authoring.

---

## 37. Final Assessment

**[OPINION]**

RUINRAIL is an unusually well-engineered game that has not yet been playtested. That sentence explains almost everything in this report.

The engineering is real: the architecture is clean, the determinism is genuine, the persistence is careful, the performance is measured and leak-free, the accessibility and controller support are above the indie norm, and the 1,647 tests are not vanity — they encode real rules and they have caught real bugs. The UI layer in particular (main menu, inventory, merchant, HUD) is confident, consistent work that a shipping game would be happy with.

What has not happened is the pass where someone plays it and asks *"did that do anything?"* — and the answer, for a surprising fraction of the game's content, is no. Eleven of sixteen accessories do nothing. Most weapon affixes do nothing. No weapon can knock back or stagger, so an entire authored subsystem with its own design document, its own resistance curves and two Legendary passives is inert. Nothing drops from a kill. The three biomes are the same dungeon in three costumes. The player gets 24 % stronger over an entire account while enemies get 450 % tougher. Every expedition starts at Depth 1 and the game does not remember how deep you ever got.

None of these are bugs in the engineering sense. They are the gap between "the system works" and "the content plugged into the system produces an experience" — and that gap is invisible to a test suite that verifies systems against specs. The progression pass proved this precisely: `SkillStatSource` was correct, tested and never called, and Vitality did nothing for months while every gate stayed green. The same failure mode is still present in at least five other places, and one of them (`SetAmmoCapacityBonusProvider`) is a line-for-line repeat.

The good news is that almost none of this requires new systems. The stat pipeline is already the right shape to consume `FireRate`, `MagazineSize`, `ProjectileSpeed` and the rest — the components just never ask. The weapon definitions already have knockback and stagger fields wired all the way to the impact receiver — they are simply zero. The enemy resistance curves, the affix pools, the accessory intrinsics, the global caps and the loot tables are all authored and waiting. **RUINRAIL's biggest problems are data problems wearing the costume of design problems.**

The genuine design questions underneath are fewer and sharper: *why descend past depth 5, why keep playing past run 20, and what is the skill ceiling of a combat model with no spread, no recoil, no momentum and no switch cost?* Those deserve a designer's decision, not an engineer's fix. They are also the questions that will determine whether this becomes a game people finish or a game people admire and put down.

The project is close. It is closer than the list of findings above makes it sound, because the expensive half — the architecture, the pipelines, the persistence, the determinism, the tooling — is done and done well. What remains is mostly a pass of *making the existing content mean what it claims*, followed by the presentation work (the void, the mix, the silhouettes) that would move it from "runs correctly" to "feels finished."

**Honest bottom line:** this is not a game that needs to be redesigned. It is a game that needs to be turned on.

---

*End of review. Analysis only — no code, content, balance value, asset, test or working-tree state was modified by this pass.*
