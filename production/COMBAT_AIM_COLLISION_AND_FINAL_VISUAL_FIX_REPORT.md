# RUINRAIL — Combat Aim / Hit Registration / Enemy Collision + Final Visual Fix Report

> **Scope:** the six repository-local issues of `RUINRAIL_COMBAT_AIM_COLLISION_FINAL_FIX_PROMPT_V2.md`, executed from
> the state after `FINAL_PLAYABILITY_REGRESSION_REPORT.md`. No character, weapon, accepted VFX family, biome art,
> unrelated UI or audio was redesigned; no weapon damage/rate/range/ammo stat, enemy health/damage stat, save semantic,
> networking authority rule, stable id or room topology changed.
> **Terminal status:** `COMBAT_COLLISION_FIX_COMPLETE`

| Gate | Result |
|---|---|
| `./scripts/run-unity-tests.ps1 -TestPlatform EditMode` | **PASS — 774 passed / 775 discovered, 0 failed** (1 skipped: the pre-existing `RUINRAIL_LIVE_SERVICES=1` live-Sessions check) |
| `./scripts/run-unity-tests.ps1 -TestPlatform PlayMode` | **PASS — 611 passed / 611 discovered, 0 failed** |
| `ContentCountValidator` | PASS — 52/52 counts exact, 0 problems |
| `FinalProductionValidator` | PASS — 9/9 sections, 0 errors, 0 external blockers |
| `PresentationValidator` | PASS — 8 checks, 0 problems |
| `AnimationAssetAudit` | COMPLETE — 22/22 actor animation sets, 33/33 weapon sprites |
| `ArtProductionContract` | PASS — 10 checks, 0 problems (door sprites conform: PPU 32, point, uncompressed) |
| `AssetPipelineValidator` | PASS — 6 checks, 0 problems |
| `ReleasePathVisualScan` | CLEAN — 0 placeholder references, 0 missing references |
| Release build (`ReleaseBuildTool.BuildBatch`, Windows x64, non-development) | **Succeeded** — 0 errors, 1 warning (the external UGS project-ID notice), 157.6 MB (`TestResults/build_report.md`) |
| Built-player smoke, headless (`RUINRAIL.exe -batchmode -nographics -smoke -savedir <tmp>`) | **Success** ×3 runs (Metro, Metro, Rustworks) — 15/15 playability checks + **12/12 combat checks**, `ReturnToMenuOk: true`, 0 exceptions, 0 missing scripts/references (`TestResults/combat_smoke_result_headless.json`) |
| Built-player smoke, windowed + screenshot | **Success** ×3 runs (Metro, Labs, Metro) — same checks; `13_built_player_combat_room.png` captured by the shipped exe (`TestResults/combat_smoke_result_windowed.json`) |
| Proof captures | `TestResults/CombatAimCollisionProof/` — 17 captures (+ `_x2` zooms) and `position_evidence.txt` |

Every gate above ran **after the last source change**. Unity `6000.3.24f1`; no pinned package or Editor version
changed. Counts are read from the final `TestResults/EditMode-results.xml` / `PlayMode-results.xml`. Baseline before
this pass: EditMode 774/775, PlayMode 565/565 → **+46 PlayMode tests**, EditMode unchanged. Smart App Control was not touched.

---

## 1. Hit registration — the actual root causes of direct misses

One shot was traced end to end (cursor → `PlayerAiming` → weapon pivot → muzzle → `ProjectilePool` → `Projectile`
sweep → enemy collider → `HealthComponent`). Five independent defects were found; each one alone makes a crosshair
that is visibly on an enemy miss, and they compound. **None of them is fixed by aim assist** — assist was added only
after `DirectAimHitTests` proved the raw path hits with assist off.

| # | Root cause | Where | Effect |
|---|---|---|---|
| 1 | **Normal enemies had no `Collider2D` at all.** `DefaultEnemySpawner` added a `Rigidbody2D` and the behaviours but no collider (Elites/Bosses had a feet-level circle). | `Enemies/EnemySummoner.cs` | A projectile's `CircleCast` sweep had nothing to hit on a Grunt/Shooter/Swarm/…: a perfectly aimed shot passed straight through the sprite. This is also root cause #1 of enemies walking through walls (§6). |
| 2 | **Feet-level hit volume vs a 1.5-tile body.** Where a collider existed (Elite r=0.6, Boss r=0.8) it sat at the feet pivot; the drawn body extends ~1.2–2.2 tiles upward. | Elite/Boss spawners | Aiming at the torso/head — where a player naturally aims — missed above the circle. |
| 3 | **Aim measured from the body, projectile launched from the pivot.** `PlayerAiming` computed the direction from `transform.position` (feet) while the shot left the weapon pivot/muzzle ~0.55 tiles away. | `Player/PlayerAiming.cs` | A parallel-offset line: the bullet ran beside the crosshair line, the error growing with the angle. |
| 4 | **Stale pooled body position.** `Projectile.Activate` set `transform.position` but the `Rigidbody2D` still held its last pooled position until the next physics sync; the first sweep started from the previous shot's end point. | `Combat/Projectiles/Projectile.cs` | The first physics step of a recycled projectile swept from somewhere else entirely — a shot that started inside a wall or past the target. |
| 5 | **Muzzle past a close target.** The spawn point was the muzzle even when a target or wall already lay between hand and muzzle. | `RangedWeapon`/`BlasterWeapon`/`BowWeapon` | Point-blank shots spawned *behind* the enemy and missed. |

### Exact aim corrections

- **`CombatHurtbox`** (new, `Combat/CombatHurtbox.cs`): a trigger `BoxCollider2D` child (`Hurtbox`) on every enemy,
  Elite and Boss, resolving to the owner's `IDamageable` via `GetComponentInParent`. Conservative sizes, never larger
  than the drawn body: Normal 0.7×1.2 @ y 0.6, Elite 0.9×1.6 @ 0.8, Boss 1.5×2.2 @ 1.1 (tiles). `AimPoint` = box centre.
  `Physics2D.queriesHitTriggers` is on, so the projectile sweep and the assist queries see it.
- **Solid body for normal enemies**: `CircleCollider2D` r = `DefaultEnemySpawner.BodyRadius` (0.35) on the enemy root,
  `freezeRotation`, layer `Enemy` (see §6).
- **Pivot-anchored aim**: `PlayerAiming.AimOrigin` (the weapon pivot) is the origin of the pointer direction;
  `AimWorldPoint` and `HasPointerAim` are exposed. The crosshair, the pivot and the bullet line are now collinear.
- **Body sync on spawn**: `Projectile.Activate` sets `_rigidbody2D.position = transform.position` (pinned by
  `DirectAimHitTests.ProjectileBody_StartsAtTheMuzzle_NotAtTheLastPooledPosition`).
- **Close-range pull-back**: `ShotSolver.BlockedBetween(hand, muzzle)` — if a hostile hurtbox/body or an
  `EnvironmentObstacle` lies between hand and muzzle, the projectile spawns at the hand (`ShotSolution.SpawnPulledBack`).
- **Team rules made explicit**: `TeamMember.IsTagged`/`AreAllies`. A projectile passes through its own tagged team only;
  untagged test dummies stay hittable; an Elite slam no longer injures the enemies beside it now that they are solid.
- Damage authority is untouched: `DirectAimHitTests.Damage_StaysHostAuthoritative`.

`DirectAimHitTests` (PlayMode, 7) runs the real path with **assist off** for every direct projectile class in the
catalog (`weapon_p9_ranger`, `rattler_9`, `ar_17`, `sentinel_br`, `scatter_8`, `longshot_s1`, `pulse_carbine_b1`,
`pipe_launcher`), plus: crosshair just inside the hurtbox edge hits / just outside misses, a wall stops the shot with no
false hit, a point-blank target is not spawned past, long range within weapon range hits, all 8 directions hit.

---

## 2. Soft aim assist — algorithm and config

`Combat/Weapons/ShotSolver.cs` + `Combat/Weapons/AimAssistConfig.cs` (ScriptableObject
`Assets/Game/ScriptableObjects/Combat/AimAssistConfig.asset`, referenced by `GameContentCatalog.AimAssist`, bound to
every `RangedWeapon`/`BlasterWeapon`/`BowWeapon` in `PlayerRigComposer`).

**Config (as shipped):** mouse half-angle **18°**, controller **24°**, crosshair proximity **28 px** (reference
640×360, 32 px/tile), class multipliers as a data table on the asset: Sniper **0.75**, Rocket **0.65**, every other
projectile class 1.0, melee (Knife/Spear) **0** — no assist. (The table is data, not a class switch: the Combat/ source
scan in `RangedWeaponDefinitionTests` still passes.)

**Per shot (`ShotSolver.Solve`):**
1. raw direction = pivot → crosshair (pointer) or the stick direction (controller);
2. spawn = muzzle, or hand when `BlockedBetween` (§1);
3. candidates = alive hostiles (`HealthComponent.IsAlive`, not tagged with the shooter's team) whose hurtbox centre
   is inside weapon range, **in front** of the raw aim (`dot > 0` — a target behind the player is never pulled in,
   however close), inside the class-scaled cone **or** directly under the crosshair, with **clear line of sight** from
   the spawn point (an `EnvironmentObstacle` or another hostile in front rejects it — the assist never targets through
   walls or doors);
4. score (lower wins): crosshair proximity ×3 (capped at 4 proximity units), angle/half-angle ×2, distance/range ×0.5;
   a crosshair inside a hurtbox scores −1000 (wins outright);
5. the shot's **direction** is bent toward the winner's hurtbox centre; nothing else changes — no target lock, no
   auto-fire, no cursor/camera movement, no curving. Spread weapons apply their pellet pattern around the assisted
   centre. No candidate → the raw direction is used unchanged.

`AimAssistTests` (PlayMode, 7): shipped values; 0°/10°/17° selected and 25° not with the mouse cone; 21° rejected by
the mouse cone but accepted by the controller cone; 15° accepted by AR but rejected by Sniper (13.5°) and Rocket
(11.7°); crosshair-over-target beats a nearer on-axis candidate; wall / dead / behind-player rejected; two targets —
crosshair proximity before angle; no target → raw aim unchanged; shotgun spread stays centred on the assisted
direction. Authority unchanged (§1).

---

## 3. Auto reload

`RangedWeapon.TryFire`: after the shot is emitted, if `MagazineAmmo == 0`, not already reloading and the reserve holds
at least `AmmoCostPerShot`, the **existing** `TryStartReload()` is called (same gates, same duration, same cancellation
on unequip/swap, same reserve accounting at completion). `AutoReloads` counts them for diagnostics. Magazine weapons only:
`BlasterWeapon` (heat), `BowWeapon` (charge) and `MeleeWeapon` have no magazine and were not touched.

`AutoReloadTests` (PlayMode, 7): P9 Ranger final shot → reload starts exactly once, a manual reload during it is a
no-op; it takes the configured 1.2 s and the reserve drops by exactly the rounds loaded; reserve 0 → no reload, no
crash; weapon swap cancels it like a manual one and it is not retried; **every magazine-fed catalog weapon**
(Pistol, SMG, AR, BR, Shotgun, Sniper, Rocket classes covered) auto-reloads on its final round; blaster/bow/melee are
exactly the classes with no magazine; a partial magazine never auto-reloads.

---

## 4. The large red/orange quad — identity and fix

**Identity:** the `EffectPool` **placeholder** — a flat 8×8 white sprite (`EffectPool.Placeholder`, documented as the
stand-in "until effect art arrives") — tinted with `FeedbackConfig.EnemyTelegraphColor` by `TelegraphIndicator` and
scaled to **`AttackRange × 2` on both axes** for every normal enemy telegraph. A Shooter (attack range 7) therefore
drew a **14×14-tile orange square** each time it wound up a shot; a Bomber the same. It was not a collider gizmo and
not a debug draw: the final `Art/Vfx` sheets existed but were never bound to the pool, so *every* effect (muzzle,
impact, explosion, melee arc, stagger, heal, status, loot glow and all telegraphs) was the placeholder quad.

**Fix:**
- `GameContentCatalog.VfxSprites` + `VfxFramesFor(kind)` bind the accepted VFX frames (`vfx_<kind>_<row>_<frame>`)
  to the catalog (builder: `GameContentCatalogBuilder`); `ExpeditionScene` calls `effects.SetSpriteResolver(content.VfxFramesFor)`.
  `PooledEffect` now plays frames over its lifetime and normalises any frame size to the requested world footprint
  (`SetWorldSize`); `Problems()` fails the catalog if any of the 13 runtime kinds has no frames.
- `TelegraphIndicator.ShapeOf(EnemyController)` draws **the mechanic's shape**, never a range square: melee contact =
  ring of the contact reach around the body; Shooter/Sniper = a **0.6-tile lane** along the locked direction to the
  projectile range, **clipped at the first wall**; Bomber = ring of the blast radius **at the landing point**; Charger =
  the dash lane; moveset enemies = the move's authored motion. Elite/Boss shapes unchanged (real hit shapes).
- Nothing on the release path spawns the placeholder: `TelegraphArtTests` asserts every runtime kind resolves to
  catalog frames, frames advance, the Shooter lane is ≪ the old square, Grunt/Bomber/Charger shapes follow their
  mechanics, and a live Shooter telegraph draws `vfx_telegraph_projectile_*` at the lane footprint. The built-player
  smoke checks `HasArtFor` for all 13 kinds; the live proof capture `05_combat_no_orange_quad.png` is scanned for
  saturated orange fill (0–96 px found, i.e. lane art; the old quad would be ~200 000 px).

---

## 5. Door visuals

The orange stripe/plate was the `RoomDoorLock` blocker's own debug-ish plate. Replaced with pixel-art doors:

- **`DoorFactory`** (editor, `RuinRail/Art/Generate Doors`) generates 64×32 (2×1 tiles, PPU 32, point, uncompressed)
  open/locked sprites per biome at `Assets/Game/Art/World/Doors/door_<biome>_{open,locked}.png` in the biome
  palettes: **Ruined Metro** — transit service shutter in steel/concrete, small amber stencil, red lock lamp;
  **Rustworks** — heavier blackened-steel barrier with restrained hot-orange chevrons and an oxide-orange lamp;
  **Overgrown Labs** — damaged pale security panels with a centre seam, system light **cyan when open**, **red when
  locked**. The open state is only the housing rail and posts — the doorway is visibly clear.
- `GameContentCatalog.DoorSkins` / `DoorSkinFor(biome)`; `RoomDoorLock.SkinResolver` is registered by
  `ExpeditionScene` (the runtime never loads by path). `RoomDoorLock` draws a `DoorVisual` `SpriteRenderer` (LowProps,
  order 20) per **unsealed** socket, rotated 90° for E/W and flipped so the housing rail faces the wall; the sprite
  switches **with the blocker** (`IsPlateVisible` ⇔ locked sprite ⇔ solid; `IsOpenVisible` ⇔ open sprite ⇔
  traversable). A sealed spare socket is wall and gets no door.
- Room-entry timing (previous pass) is preserved: `RoomEntryTimingTests` still asserts the door is drawn shut only
  after the room activates from its interior and never on a teammate in the doorway.

`DoorVisualTests` (PlayMode, 5): distinct open/locked sprites per biome, 2×1 tiles; the PNGs carry the lamp colours
(Metro red + amber, Rustworks oxide-orange, Labs cyan open / red locked), the locked shutter fills the doorway and the
open housing leaves it clear, >6 colours (not a flat plate); every unsealed socket of every biome × size class draws
its biome door covering the blocker area and switches with the blocker; a sealed socket has no door; a player body
walks through the open doorway and is stopped by the locked one.

---

## 6. Enemies through walls — root cause and fixes

**Root cause:** the same missing collider as §1 — a normal enemy was a `Rigidbody2D` **with no `Collider2D`**, so the
physics world had nothing to stop it; velocity-driven chase moved it straight through tilemap walls, obstacles and the
door blockers. Elites/Bosses had a body circle but their chase heading and dashes did not account for it.

**Fixes (movement / collision):**
- Solid body on every normal enemy (`CircleCollider2D` r 0.35, `freezeRotation`), `CombatLayers.TagEnemyBody`.
- **Layers** (`Combat/CombatLayers.cs`, `TagManager`: 8 `Enemy`, 9 `Player`): enemies are solid against the world
  (Default layer walls/obstacles/door blockers) and each other; `Physics2D.IgnoreLayerCollision(Enemy, Player)` keeps
  the pre-existing feel where an enemy body does not shove the player body (hits still go through hurtboxes/attacks).
- **`ObstacleSteering`** (new) used by `EnemyController` and `MovesetActorController` chase: a `CircleCast` of the
  body radius 0.75 tiles ahead; a head-on wall makes the actor **ease up to it and hold** (facing its target — what
  "blocked by a wall" looks like); an oblique wall is **slid along** toward the goal side, with the side held 0.6 s so
  corners are rounded instead of oscillated; boxed in → stop. No navigation graph (V1 scope), no pushing into geometry.
- **Fast movement:** dashes/lunges/charges (`AttackResolver.TickDash`, shared by Charger, Elites, Bosses) probe ahead
  by `step + max(hitRadius/2, bodyRadius)` each fixed step and end the dash at the wall (`LastDashStoppedByWall`), so a
  big body sees the wall before it is pressed against it. Knockback (`ImpactReceiver`) already probed walls per step.
  All bodies are dynamic `Rigidbody2D`s moved by velocity, so Unity's continuous contact solving is the final word —
  the invariant below measures actual penetration.
- **Spawn safety:** `RoomRuntime.IsSpawnClear` rejects any EnemySpawn marker whose body circle overlaps solid
  geometry (wall, obstacle, sealed socket, door blocker); `SpawnPointsFor` filters with it.
- Doors: a locked `RoomDoorLock` blocker is an `EnvironmentObstacle` box over the socket cells — a wall to enemies,
  projectiles, dashes and the assist's line of sight; unlocked, the collider is disabled and the doorway is a route.
- Network: enemy positions replicate **only** from the host's `AuthoritativeEnemySpawner.Capture` snapshots; an
  `EnemyReplica` has no `Rigidbody2D` and no `EnemyController`, so a client can never see an enemy somewhere the host's
  wall-constrained simulation did not put it.

**Invariant:** *no enemy centre/collider may transition from one side of a solid wall to the other without
traversing a valid opening.* Measured as **penetration** (how far the body circle sits inside any solid
`EnvironmentObstacle` collider) ≤ 0.12 tiles (contact offset) on every fixed step, plus containment in the room rect
while the doors are locked, across all three biomes × three size classes with five archetypes chasing targets outside
on all four sides.

`EnemyCollisionTests` (PlayMode, 14): body/hurtbox/layer contract; straight wall stops the chase; diagonal wall is
slid along; corner never cut; boxed-in stops; a Shooter keeping distance never backs through the wall behind it;
Charger charge stops at the wall; Elite lunge (Tunnel Stalker) and Boss dash (Subject Omega) end at the wall; knockback
into a wall stops at it; spawn points inside geometry rejected; closed door is a wall and the open doorway is the route
in every biome; locked rooms contain every chasing enemy with zero penetration across biomes × sizes; a sealed socket
is wall for enemies; the client replica follows only the host's wall-constrained position and has no body of its own.

---

## 7. Tests added (this pass)

| Suite (PlayMode) | Tests | Covers |
|---|---:|---|
| `DirectAimHitTests` | 7 | §1 hit registration, assist OFF |
| `AimAssistTests` | 7 | §2 cone/candidates/scoring/LOS/authority |
| `AutoReloadTests` | 7 | §3 |
| `TelegraphArtTests` | 5 | §4 final VFX on the release path, mechanic-shaped telegraphs |
| `DoorVisualTests` | 5 | §5 door art, collider/visual sync, traversal, sealed sockets |
| `EnemyCollisionTests` | 14 | §6 walls, doors, dashes, knockback, spawn, biomes, network |
| `CombatAimCollisionProofTests` | 1 | live-run proof captures + deterministic position evidence |

Updated: `RoomEntryTimingTests` (binds the door skins in its fixture), `SniperReferenceTests` (a dry magazine with reserve now auto-reloads: the cadence test asserts the automatic reload, its 2.4 s and the exact Heavy accounting). Total **+46 PlayMode tests**.

---

## 8. Proof (`TestResults/CombatAimCollisionProof/`)

Captured by `CombatAimCollisionProofTests` on a live expedition (real boot flow, real dungeon, real rooms, weapons,
enemies) at the 640×360 reference resolution (`_x2` zooms alongside); `position_evidence.txt` holds the deterministic
numbers for every item (positions, angles, HP deltas, penetration, door states).

| # | File | Evidence |
|---|---|---|
| 1 | `01_direct_crosshair_hit_hp_reduced.png` | assist OFF, crosshair on the hurtbox centre (36.00, 6.60), P9 Ranger; HP 56→42, bar drawn |
| 2 | `02_near_miss_corrected_by_assist.png` | crosshair 12.0° off; `Assisted=True`, direction bent to the hurtbox centre; HP 42→28 |
| 3 | `03_outside_cone_no_snap.png` | crosshair 40.0° off; `Assisted=False`, raw direction used; HP 28→28 |
| 4 | `04_auto_reload_after_final_shot.png` | mag 1→0 with reserve 60; `IsReloading=True`, `AutoReloads=1`, 1.2 s |
| 5 | `05_combat_no_orange_quad.png` | live combat during an enemy telegraph; orange-fill pixel scan ≪ 1500 (old quad ≈ 200 704) |
| 6 | `06_ruined_metro_open_door.png` (+ `_live`) | Metro open housing, blocker off, `door_ruinedmetro_open` |
| 7 | `07_ruined_metro_combat_locked_door.png` (+ `_live`) | Metro shutter with red lamp, blocker solid |
| 8 | `08_rustworks_combat_locked_door.png` (+ `08_rustworks_open_door.png`) | Rustworks barrier, chevrons, oxide lamp |
| 9 | `09_overgrown_labs_combat_locked_door.png` (+ `09_overgrown_labs_open_door.png`) | Labs pale panels, red light (cyan when open) |
| 10 | `10_enemy_blocked_by_wall.png` | pack pressed on the inside of the wall (1.58 tiles from the outer face = 1 tile wall + body radius), player 1.6 tiles outside; worst penetration 0.005 |
| 11 | `11_charger_stopped_by_wall.png` | Charger charge from 3.5 tiles ends 1.36 tiles from the outer face (`stoppedByWall=True`), inside the room, penetration 0.005 |
| 12 | `12_enemy_contained_by_closed_door.png` | pack held 0.85 tiles inside the shut door (all `Chase`), blocker solid, worst penetration 0.005 |
| 13 | `13_built_player_combat_room.png` | written by the shipped exe at the end of the built-player combat stage (windowed smoke) |

The room's biome is rolled per run; the `_live` door captures show that run's biome, the numbered 06–09 set is
captured on each biome's shipped medium combat room in the same scene so all three biomes are always present.

---

## 9. Build and smoke

**Release build** (`ReleaseBuildTool.BuildBatch`): StandaloneWindows64, options `None` (non-development), scenes
Bootstrap/MainMenu/Base/Dungeon — **Succeeded**, 0 errors, 1 warning (UGS project-ID notice, external), 157.6 MB,
`Builds/Windows64/RUINRAIL.exe`. Log: `TestResults/combat-build.log`, report `TestResults/build_report.md`.

**Built-player smoke** (`SmokeRunner`, shipped exe): the existing boot → menu → shelter → dungeon → return → save/reload
→ main-menu flow with its 15 playability checks, extended by a **combat stage** run in the shipped player inside the
entered, locked combat room (`CombatChecks` in `smoke_result.json`):

1. enemies have solid bodies (non-trigger circle, `Enemy` layer);
2. enemies have hurtboxes;
3. door visual is the final biome art (locked sprite while solid);
4. no placeholder effect art on the release path (all 13 effect kinds have catalog frames);
5. direct crosshair-on-enemy projectile reduces HP (pivot → hurtbox centre, assist off);
6. aim assist bends a near miss (10°) onto the hurtbox;
7. aim assist does not target through a shut door;
8. auto reload starts after the final magazine round;
9. enemies cannot pass through walls (pack chases the player outside a wall for 100 fixed steps: contained, penetration ≤ 0.12);
10. fast enemy (Charger) cannot tunnel through solid geometry (charge ends at the wall, body inside the room);
11. enemies cannot pass through the shut combat door;
12. open doorway is traversable and drawn open.

Headless (`-batchmode -nographics -smoke -savedir <tmp>`): **Success** on three consecutive runs (Ruined Metro,
Ruined Metro, Rustworks), 15/15 + 12/12 checks, `ReturnToMenuOk: true`, 0 exceptions, 0 missing scripts/references.
Windowed (`-smoke -screenshot … -screen-width 1280 -screen-height 720`): **Success** on three runs (Ruined Metro,
Overgrown Labs, Ruined Metro), same checks, `13_built_player_combat_room.png` written by the shipped exe (the player
in the reopened Labs/Metro doorway at the end of the stage). Logs: `TestResults/combat-smoke*.log`.

Live Relay/UGS is **not** claimed — nothing in this pass was tested over UGS.

---

## 10. Changed files

**Runtime:** `Combat/CombatHurtbox.cs` (new), `Combat/CombatLayers.cs` (new), `Combat/TeamMember.cs`,
`Combat/Projectiles/Projectile.cs`, `Combat/Weapons/ShotSolver.cs` (new), `Combat/Weapons/AimAssistConfig.cs` (new),
`Combat/Weapons/RangedWeapon.cs`, `BlasterWeapon.cs`, `BowWeapon.cs`, `Player/PlayerAiming.cs`,
`Player/PlayerEntityBuilder.cs`, `Multiplayer/NetworkPlayerObject.cs`, `Enemies/EnemySummoner.cs`,
`Enemies/Elites/EliteSpawner.cs`, `Enemies/Bosses/BossSpawner.cs`, `Enemies/EnemyController.cs`,
`Enemies/ObstacleSteering.cs` (new), `Enemies/Attacks/MovesetActorController.cs`, `Enemies/Attacks/AttackResolver.cs`,
`Enemies/EnemyMovesetAttack.cs`, `Enemies/EnemyProjectileAttack.cs`, `Presentation/Vfx/EffectPool.cs`,
`Presentation/Vfx/TelegraphIndicator.cs`, `Dungeon/Runtime/RoomDoorLock.cs`, `Dungeon/Runtime/RoomRuntime.cs`,
`App/GameContentCatalog.cs`, `App/PlayerRigComposer.cs`, `App/ExpeditionScene.cs`, `App/SmokeRunner.cs`.
**Editor:** `Editor/ArtGen/DoorFactory.cs` (new), `Editor/Production/GameContentCatalogBuilder.cs`.
**Assets:** `ProjectSettings/TagManager.asset` (layers 8/9), `ScriptableObjects/Combat/AimAssistConfig.asset` (new),
`Art/World/Doors/*.png` (6, new), `Resources/GameContentCatalog.asset` (rebuilt).
**Tests:** the seven suites in §7 (new) + `RoomEntryTimingTests`.

## 11. Remaining blockers

None repository-local. Not claimed: live Relay/UGS multiplayer (not tested over UGS in this pass; the `LiveSessionsRelay_IntegrationCheck_OrNotRun` EditMode test stays skipped without `RUINRAIL_LIVE_SERVICES=1`).

Notes for the next pass (not defects): enemy pathing is deliberately V1 steering (press/slide/round corners), not a navigation graph — an enemy whose target is behind a wall with no nearby opening holds at the wall rather than routing around the room; enemy bodies do not push player bodies (kept from the previous feel, documented in §6).
