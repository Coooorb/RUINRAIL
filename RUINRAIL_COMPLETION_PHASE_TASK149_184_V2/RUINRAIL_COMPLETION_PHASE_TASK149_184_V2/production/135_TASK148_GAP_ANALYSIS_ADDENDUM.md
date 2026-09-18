# TASK 148 Gap Analysis Addendum — Exact Release Gaps

> **Purpose:** Preserve the post-TASK-148 repository findings that refine the completion roadmap. This document is evidence input for TASK 149 and later completion gates; it does not supersede the validated gameplay/GDD scope.

## Proven Baseline

- TASK 001–148 executed.
- EditMode: 661 / 662 discovered, 661 passed, 0 failed, 1 skipped because live Unity services were not available.
- PlayMode: 498 / 498 passed, 0 failed.
- Production validator: 9/9 sections, 0 errors.
- ContentCountValidator: 52/52 exact.
- PresentationValidator: 8 checks, 0 problems.
- Windows x64 non-development build succeeded.
- Built-player smoke succeeded through MainMenu → Base → Dungeon → Base with save reload and no player-log exceptions.

## Exact Presentation Gaps Found

### Character / Animation

- 22 required animation sets: player + 9 normal enemies + 6 Elites + 6 Bosses.
- Current contract: 48 clip roles per set (8 directions × 6 clips) at the existing 8–12 fps rules.
- Baseline total: 1,056 clip roles.
- Present final clips at TASK 148: 0.

### Weapons

- Required final weapon sprites: 33.
- Present final weapon sprites at TASK 148: 0.
- Existing runtime expects 360° WeaponPivot presentation; blaster/bow/reload states must use the existing weapon visual driver contract rather than new gameplay systems.

### Biome Environment

- Three final biome art packages required: Ruined Metro, Rustworks, Overgrown Labs.
- Required categories include floor, floor detail, wall, obstacle, hazard, props, doors, rail/transit and biome-specific cables/pipes/plants where applicable.
- TASK 148 state used five flat-colour placeholder tiles shared by all biomes.
- Three distinct biome lighting/emergency-light looks are required; TASK 148 state was neutral white/intensity 1.

### World Objects

Final presentation required for the existing runtime objects, including at minimum:

- supply chest
- dungeon merchant
- medical station
- transit car
- coin pickup
- item pickup
- door/socket presentation
- other existing interactable/world-object roles enumerated by TASK 149

### VFX

The existing feedback/VFX architecture is present but the TASK 148 visual content was effectively a white placeholder square. Final roles include at minimum:

- muzzle flash
- projectile/weapon impact
- explosion
- melee arc
- stagger
- heal
- status effect
- loot glow
- telegraph zone
- telegraph dash
- telegraph projectile
- telegraph slam
- any additional current role enumerated by the validator

### UI

Final presentation must replace grey rectangles / legacy font / text-only markers. Baseline missing roles include:

- panel frames
- rarity borders
- item/ammo/consumable icons (audit baseline: 72 item-family icon roles before UI-only glyphs)
- HP and XP bars
- keyboard glyphs
- controller glyphs
- pixel font
- main-menu background
- Shelter station presentation
- final buttons/focus states/inventory/HUD iconography required by current views

## Exact Audio Gaps Found

### SFX

The code contains 53 audio-event IDs and zero production clips at TASK 148. TASK 149 must enumerate current IDs from code and reconcile to this baseline. The TASK-148 list was:

1. weapon.fire.pistol
2. weapon.fire.smg
3. weapon.fire.assault_rifle
4. weapon.fire.battle_rifle
5. weapon.fire.shotgun
6. weapon.fire.sniper
7. weapon.fire.blaster
8. weapon.bow.draw
9. weapon.bow.release
10. weapon.melee.knife_slash
11. weapon.melee.spear_thrust
12. weapon.fire.rocket_launch
13. weapon.rocket.explosion
14. weapon.reload
15. weapon.dry_fire
16. weapon.blaster.heat_rising
17. weapon.blaster.overheat_warning
18. weapon.blaster.overheat
19. weapon.blaster.vent
20. weapon.legendary_special
21. enemy.telegraph
22. enemy.telegraph.elite
23. enemy.telegraph.boss
24. enemy.hit
25. enemy.stagger
26. enemy.death
27. enemy.elite.spawn
28. enemy.boss.phase
29. player.hit
30. player.dash
31. player.downed
32. player.revive.start
33. player.revive.complete
34. player.death
35. player.consumable.use
36. player.heal
37. player.hazard
38. loot.pickup.item
39. loot.pickup.coins
40. loot.drop.common
41. loot.drop.uncommon
42. loot.drop.rare
43. loot.drop.epic
44. loot.drop.legendary
45. loot.chest.open
46. world.door.open
47. world.transit.depart
48. world.merchant.open
49. ui.navigate
50. ui.confirm
51. ui.cancel
52. ui.failure
53. ui.purchase

### Music / Stingers / Ambience

Production clips missing at TASK 148:

- 11 music roles: Main Menu; The Shelter; Ruined Metro Exploration/Combat/Boss; Rustworks Exploration/Combat/Boss; Overgrown Labs Exploration/Combat/Boss.
- 6 stingers: LegendaryDrop, EliteEncounter, BossDefeated, ExtractionSuccess, ExpeditionFailed, LevelUp.
- 3 ambience loops: RuinedMetro, Rustworks, OvergrownLabs.

The existing `MusicDirector` crossfade default of 1.5 s is a PROTOTYPE value and must be handled by the prototype-value sign-off task, not silently treated as final.

## Multiplayer Gaps Found

- Built `GameApp` currently uses `FakeMultiplayerServices` + `FakeNetworkDriver`; Host/Join in the shipped TASK-148 executable are offline fakes.
- A real Unity Gaming Services project ID/configuration is not linked in the audited environment.
- Boot composition needs the actual `NetworkManager` path used for release.
- Final network player prefab is not authored/registered; `NgoPlayerEntityFactory` receives no release prefab and `DefaultNetworkPrefabs.asset` has no final entry.
- `RUINRAIL_LIVE_SERVICES=1` live Sessions/Relay integration test was NOT RUN.
- Real host + join-code built-client smoke over Relay was NOT RUN.

## Engineering Follow-ups Found

- 12 deprecated `Physics2D.*NonAlloc` calls across 7 files produce CS0618 warnings; migrate to supported `ContactFilter2D`/current overloads without changing gameplay semantics.
- Build-target player profiling was NOT RUN.
- Managed-allocation evidence from the editor/Mono path was not trustworthy enough for release profiling; measure in the built Windows player/profiler.
- TASK 148 only established Windows x64. Other platforms are not implicitly required; TASK 184 must state the approved release-platform scope truthfully.
- No final loading/transition/error screens existed; scene loads were synchronous with no production transition presentation.

## 35 PROTOTYPE Markers / Design Sign-off Categories

TASK 149 must re-enumerate all current `PROTOTYPE` markers and reconcile the current count against the TASK-148 baseline of 35. They include these categories:

- camera follow sharpness / aim offset
- feedback shake ranges, hit-flash duration, damage-number lifetime
- recoil distance/duration
- strike-hold duration
- get-up duration
- blaster heat-loop thresholds
- ambience cap
- network snap tolerance / render delay
- pickup pull speed
- Downed crawl fraction / revive distance
- depth-scaling attack-frequency / movement curves
- coin reward depth scaling (baseline flat/0 behavior)
- dungeon event threat multiplier / repair chance / wave interval / heals-revives per station / weapon-cache quality
- merchant/trader rarity quality and category weights
- stagger numbers
- grenade throw range/speed
- tutorial consumable prompt threshold
- lighting floor
- placeholder-font advance metric
- music crossfade and any other current marker discovered by repository scan

These values are functional, not necessarily defective. They require explicit KEEP / TUNE / SPEC-SOURCE sign-off with evidence. Do not invent replacements merely to remove the word `PROTOTYPE`.

## Human Validation Still Required

Automation does not establish:

- telegraph readability against final art
- feel/balance across depths
- controller feel
- real-font text fit
- accessibility/readability with final effects
- fun/pacing
- real multiplayer experience

The V1 localisation scope remains English-only unless an approved spec says otherwise.
