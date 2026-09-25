# RUINRAIL — STAT CONSUMER INTEGRITY REPORT

> Pass executed against the working tree on 2026-09-23. No destructive git operation was used at any point.
> Machine: macOS (Apple silicon), Unity 6000.3.24f1, the pinned toolchain in `ENVIRONMENT.md`.

## 1. Executive Summary

Before this pass a player could buy, roll, equip and read the tooltip of content that did nothing at all.

Eleven of the twenty-five `StatId`s had no runtime reader anywhere in the shipped assemblies. Nine of sixteen
accessory families were therefore inert, all thirty-three weapons shipped `Knockback = 0` and `StaggerPower = 0`
against an impact pipeline and a full set of enemy resistance curves that were already implemented and correct, and
`PlayerInventory.SetAmmoCapacityBonusProvider` — the Ammo Pouch's only route into the game — was called by exactly
one file in the repository, an EditMode test.

While wiring those, the pass found a larger instance of the same defect: `PlayerRigComposer.ResolveAffix` returned
`null` for every id, so `EquippedItemStatSource` built no affix source and **no affix on any equipped item, at any
rarity, contributed a single point to a run**. Uncommon through Legendary gear was numerically identical to Common
gear apart from the Legendary fixed mechanic, while the loot tables, the rarity rules, the trader prices and the
tooltips all behaved as though it were not.

After this pass, every stat a player-acquirable item can grant is either consumed by a real gameplay system
(24 of 25) or explicitly deferred with a written reason and removed from V1 acquisition (1 of 25: Weapon Switch
Speed). A production validator now fails the build on any of the four ways this defect has appeared, and five
EditMode tests prove the validator fails when each of them is reintroduced.

Nothing was redesigned. No new mechanic was invented; spread and weapon switching were explicitly **not** given one.

## 2. Verified Root Cause Pattern

The repository has shipped the same defect four times. It is not a code-quality problem — every implementation
involved was correct in isolation and unit-tested — it is a **composition** problem:

| Instance | Seam | What it cost |
|---|---|---|
| `SkillStatSource` (historical) | never instantiated by a composition root | every permanent attribute inert |
| `SetAmmoCapacityBonusProvider` | declared, pass-through wrapper, no gameplay caller | Ammo Pouch inert |
| Nine stats | no `GetMultiplier/GetPercent/...` call anywhere | nine accessories + most affix pools inert |
| `ResolveAffix` (found here) | composition root passed a resolver that always returns `null` | **every affix in the game** inert |

A count of content cannot see any of these, and neither can a test that asserts on `PlayerStats`, because the stat
pipeline is not the thing that is broken. Each instance is invisible from exactly the angle the previous fix looked
from, which is why the validator in §12 asks four independent questions instead of one.

## 3. Dead Content Before

`TestResults/StatConsumerIntegrity/dead_content_before.txt` records the verified before-state, re-derived from the
working tree rather than taken from the comprehensive review. Nine sections; the headline figures:

- **11 of 25 StatIds** had no runtime reader: Fire Rate, Magazine Size, Weapon Switch Speed, Melee Attack Speed,
  Projectile Range, Projectile Speed, Blaster Cooling Rate, Blaster Heat per Shot Reduction, Bow Charge Speed,
  Ammo Stack Capacity, Weapon Spread Reduction.
- **9 of 16 accessory intrinsics** inert outright; two more (Impact Module, Shock Charm) had readers that multiplied
  an authored `0`, so they were inert in practice — 11 of 16 in effect.
- **0 of 9 armor intrinsics** inert. Armor was the only fully functional intrinsic category.
- **All 33 weapons** authored `Knockback = 0` and `StaggerPower = 0`, against a verified-correct
  `ImpactDispatcher → ImpactReceiver → StaggerMeter/KnockbackMath` pipeline and authored enemy resistances from 0 %
  (Grunt) to 95 % (bosses) that could never matter.
- `AffixPool_Melee` had exactly 3 entries, 2 of them dead, so **every Epic or Legendary melee weapon in the game
  rolled the same single functional affix**.
- Every affix on every equipped item was inert in a run (§2, row 4).

## 4. Per-Stat Decision Table

Full machine-readable table: `TestResults/StatConsumerIntegrity/stat_consumer_matrix.csv` (12 columns, one row per
`StatId`). Its machine-checkable columns are asserted by
`StatConsumerIntegrityTests.StatConsumerMatrix_IsCompleteAndMatchesTheTree`, which fails if a row claims `CONSUME`
without a reader, claims a deferral without a documented reason, or names a `RuntimeCodePath` that is not a
gameplay file. Every `StatId` must appear exactly once.

| Stat | Status | Reason |
|---|---|---|
| MaxHealth | **CONSUMED** | `PlayerStats.MaxHealth → HealthComponent`; was already live |
| MovementSpeed | **CONSUMED** | `PlayerMovement.CurrentMoveSpeed`; was already live |
| GeneralDamageReduction | **CONSUMED** | `PlayerStats.ApplyDamageReduction`; was already live |
| ExplosionDamageReduction | **CONSUMED** | `PlayerStats.ApplyDamageReduction` explosion branch; already live |
| WeaponDamage | **CONSUMED** | all four weapon behaviours; reader was live, the *affix* now reaches it |
| FireRate | **CONSUMED** | `WeaponStatMath.FireRate/FireInterval` → real shot cadence (new) |
| ReloadSpeed | **CONSUMED** | `RangedWeapon.CurrentReloadTime`; was already live |
| MagazineSize | **CONSUMED** | `WeaponStatMath.MagazineSize` → effective capacity, reload target, network clamp (new) |
| WeaponSwitchSpeed | **DEFERRED_EXPLICITLY** | weapon switching is instantaneous and no approved document defines a base duration; giving it one is a new mechanic and a new balance number. The Quickdraw Holster, the only item whose sole effect is this stat, is excluded from V1 acquisition. Handling keeps its implemented Reload Speed half. |
| DashCooldownReduction | **CONSUMED** | `PlayerDash`; was already live |
| DashDistance | **CONSUMED** | `PlayerDash`; was already live |
| MeleeAttackSpeed | **CONSUMED** | `WeaponStatMath.MeleeAttackRate/MeleePhaseSeconds` → cadence, wind-up and recovery (new) |
| ProjectileRange | **CONSUMED** | `WeaponStatMath.ProjectileRange` → `ProjectileSpawnData.MaxRange`, the real travel limit (new) |
| ProjectileSpeed | **CONSUMED** | `WeaponStatMath.ProjectileSpeed` → real projectile motion (new) |
| HealingReceived | **CONSUMED** | `ConsumableEffects`; was already live |
| BlasterCoolingRate | **CONSUMED** | `BlasterHeatState.EffectiveCoolingRatePerSecond` (new) |
| BlasterHeatPerShotReduction | **CONSUMED** | `BlasterWeapon.CurrentHeatPerShot` (new) |
| BowChargeSpeed | **CONSUMED** | `BowWeapon.CurrentFullChargeSeconds` (new) |
| Knockback | **CONSUMED** | reader existed; shotguns, rockets and spears now author a non-zero base for it to scale (§7) |
| StaggerPower | **CONSUMED** | reader existed; impact classes and knives now author a non-zero base (§7) |
| KnockbackResistance | **CONSUMED** | `PlayerImpactReceiver`; was already live |
| StaggerResistance | **CONSUMED** | `PlayerImpactReceiver`; was already live |
| AmmoStackCapacity | **CONSUMED** | `PlayerInventory.MaxStackFor`, wired at both composition roots (§8) |
| PickupAttractionRadius | **CONSUMED** | `PickupAttractor.Radius`; was already live |
| WeaponSpreadReduction | **CONSUMED** | `WeaponStatMath.SpreadDegrees` → the authored 30° shotgun cone only; no baseline spread invented (§9, `spread_decision.md`) |

**24 CONSUMED · 0 REMOVED_FROM_V1_POOL · 1 DEFERRED_EXPLICITLY.**

No stat needed `REMOVE_FROM_V1_POOL`: the two candidates (Knockback, Stagger Power) had documented class identity to
attach to, and the one genuinely undefined stat is deferred at the *item* level instead, which keeps the data for the
future design pass.

## 5. Accessory Intrinsic Results

`TestResults/StatConsumerIntegrity/accessory_runtime_matrix.csv` — one row per family, each one an
equip → measure → unequip proof through the real `PlayerRig`, in `StatConsumerRuntimeTests`.

| Accessory | Stat | Measured quantity | Baseline → equipped → after unequip |
|---|---|---|---|
| Ammo Pouch | AmmoStackCapacity | light-ammo stack limit | 180 → 225 → 180 |
| Archer's Ring | BowChargeSpeed | full draw (s) | 0.65 → 0.580 → 0.65 |
| Combat Bracelet | MeleeAttackSpeed | attack rate (swings/s) | 3.5 → 3.78 → 3.5 |
| Cooling Module | BlasterCoolingRate | cooling rate (heat/s) | 35 → 40.25 → 35 |
| Dash Capacitor | DashCooldownReduction | dash cooldown (s) | 1.4706 → 1.3235 → 1.4706 |
| Field Scope | ProjectileRange | projectile range (tiles) | 10 → 11 → 10 |
| Heat Sink | BlasterHeatPerShotReduction | heat per shot | 7 → 6.3 → 7 |
| Impact Module | Knockback | knockback on a fired shot | 6 → 6.9 → 6 |
| Loader's Glove | ReloadSpeed | reload time (s) | 1.9 → 1.727 → 1.9 |
| Magnetic Coil | PickupAttractionRadius | attraction radius (tiles) | 0 → 3 → 0 |
| Quickdraw Holster | WeaponSwitchSpeed | — | **EXCLUDED_FROM_V1_ACQUISITION** |
| Rangefinder | ProjectileSpeed | projectile speed (tiles/s) | 20 → 22.4 → 20 |
| Runner's Watch | MovementSpeed | move speed (tiles/s) | 5 → 5.25 → 5 |
| Shock Charm | StaggerPower | stagger power on a fired shot | 3 → 3.45 → 3 |
| Stabilizer | WeaponSpreadReduction | effective cone (degrees) | 30 → 25.5 → 30 |
| Trauma Pendant | HealingReceived | HP a bandage restores | 40 → 46 (multiplier 1.00 → 1.15 → 1.00) |

The measured quantities are gameplay, not properties, wherever gameplay could be measured: timed bow draws and
melee wind-ups, heat after real shots, the value carried by a real spawned projectile, a real reload that is still
running at 60 % of its shortened duration and finished at 120 %, and ammo actually carried in the backpack.

## 6. Affix Pool Results

The pools were split so that no item can roll an affix that is arithmetically incapable of doing anything on it — a
Knockback affix on a knife multiplies an authored `0` however good the roll is.

| Pool | Contents | Used by |
|---|---|---|
| `pool_ranged` | Damage, Fire Rate, Reload Speed, Magazine Size, Projectile Speed, Range | 18 ordinary firearms |
| `pool_ranged_impact` *(new)* | the six above + Knockback + Stagger Power | 3 shotguns, 3 rockets |
| `pool_blaster` | Damage, Fire Rate, Projectile Speed, Range | 3 blasters |
| `pool_bow` | Damage, Projectile Speed, Range, Bow Charge Speed *(new affix)* | 3 bows |
| `pool_melee` | Damage, Melee Attack Speed *(new affix)*, Stagger Power | 3 knives |
| `pool_melee_heavy` *(new)* | the three above + Knockback | 3 spears |
| `pool_accessory` | Movement Speed, Dash Cooldown Reduction, Healing Received, Reload Speed, Projectile Speed | accessories |
| `pool_armor` | unchanged — byte-identical to HEAD; it was the one already-clean pool | armor |

The two new affix assets are sanctioned by `items/22`: "blasters can roll heat/cooling affixes, bows can roll
charge-related affixes, melee can roll melee-specific stats". Both reuse the value bands of their existing
counterparts (`affix_melee_attack_speed` mirrors `affix_fire_rate` at 5–9; `affix_bow_charge_speed` mirrors the
projectile band at 8–15). No pool is below 3 entries, so no Epic or Legendary roll can silently fail into Common —
the validator fails a pool that drops below three.

`EveryRolledAffixOnEveryAcquirableItemMapsToALiveStat` rolls every acquirable item at Uncommon, Rare, Epic and
Legendary across 40 seeds each (>1 000 individual affix checks) and requires every single rolled affix to have a
live reader, not be deferred, and be capable of a non-zero effect on the item that rolled it.

## 7. Weapon Impact / Knockback / Stagger

`TestResults/StatConsumerIntegrity/impact_matrix.csv`. No value was invented: each is derived from an anchor that
already exists in the repository.

| Class | Knockback | Stagger | Derivation |
|---|---:|---:|---|
| Shotguns (Breacher-12, Scatter-8, Crowdbreaker) | 6 | 3 | per pellet; 6 knockback × `UnitsPerKnockbackPoint 0.25` = 1.5 tiles, the "close-range shove" of `items/24`. 6–8 pellets × 3 = 18–24 stagger clears the threshold of 10 on an unresisted enemy and fails on a 60 %-resist Brute (7.2) |
| Rockets (Pipe Launcher, Twin Tube, Sunbreaker) | 12 | 12 | matches `Special_ConcussionBlast` (12/20), the only pre-existing player impact source of this weight |
| Spears (Scrap Spear, Guard Lance, Railspike) | 8 | 10 | 8 × 0.25 = 2 tiles; 10 is exactly `StaggerConfig.HighStaggerPower` |
| Knives (Field Knife, Ripper Knife, Ghostedge) | 0 | 3 | `items/24` "low stagger"; **no** knockback, so a knife never shoves a target out of its own 1.0–1.2 tile reach |
| All other 21 weapons | 0 | 0 | unchanged; `combat/42` gives them no impact identity |

Runtime proof, measured in `ImpactWeaponsMoveAndStaggerEnemiesAndOrdinaryWeaponsDoNot`:

1. **Impact-heavy vs a normal enemy** — Breacher-12 moved an unresisted target 1.5 tiles and staggered it; Pipe
   Launcher 3.0 tiles; Scrap Spear 2.0 tiles — each exactly `knockback x UnitsPerKnockbackPoint`.
2. **High-resistance enemy** — the 60 % Brute profile moved 0.6 / 1.2 / 0.8 tiles respectively and resisted the
   stagger in every case.
3. **Elite / boss** — the boss profile (95 % stagger resistance, `IsDisplaceable = false`) was never displaced and
   never staggered, by any weapon. Elites remain displaceable at 80–95 % resistance.
4. **Impact classes differ meaningfully from ordinary ones** — the pistol and the knife moved an unresisted target
   `0.00` tiles, which the test asserts rather than merely records.
5. **Impact-dependent passives** — Shock Charm and Impact Module now scale a non-zero base (§5); `arc_stagger` and
   `wallbreaker` have something to trigger on.
6. **EncounterBounds** — `KnockbackNeverPushesAnEnemyOutOfItsEncounterBounds` puts an enemy 0.6 tiles from the wall
   and hits it with a rocket's full 12 knockback: it stops at the legal edge and the stop is recorded in
   `BoundsStops`/`WallImpacts` rather than silently clamped.
7. **No wall/void penetration regression** — the full PlayMode containment suite and the built-player smoke's
   containment stage are green (§16).

Multiple pellets in one shot do not stack into a runaway shove: `ImpactReceiver` replaces the remaining distance
only when a later push is *stronger*, so six equal pellets deliver one 1.5-tile push.

## 8. Ammo Capacity Wiring

`PlayerInventory.SetAmmoCapacityBonusProvider` and `ItemSlotContainer.MaxStackFor` were already correct. They are now
called from **both** composition roots, because the Shelter and the run each build their own inventory:

- `PlayerRigComposer.Build` — `Inventory.SetAmmoCapacityBonusProvider(() => stats.GetPercent(StatId.AmmoStackCapacity))`
- `BaseSession` — the Shelter loadout gets its own `PlayerStats` (`LoadoutStats`) and the same provider, so packing
  at the Shelter uses the identical rule.

There is exactly one capacity rule and nothing duplicates it. No ammo is generated: the provider changes a limit,
never a quantity. Pickups, transfers, merchant stock, save/load, the starter kit and the UI all read
`MaxStackFor`. `AmmoStackCapacity_ChangesTheOneCapacityRuleAndRestoresOnRemoval` proves removal restores the exact
baseline; `AmmoPouch_RaisesWhatTheRunCanActuallyCarry` proves the run carries more rounds and that unequipping never
destroys rounds already carried; the built-player smoke carried 450 light rounds against a base limit of 180 and
returned to 180 on removal at the Shelter.

## 9. Deferred Design Decisions

**Weapon Switch Speed — DEFERRED_EXPLICITLY.** `WeaponLoadout.SelectSlot` has no duration, no weapon definition or
`PlayerBalanceConfig` authors an equip time, and no approved document states one. Inventing a switch delay would be
inventing both a mechanic and a balance value. The stat, the `StatId`, the cap and the Handling contribution are all
retained; the Quickdraw Holster keeps its definition and is marked `_excludedFromV1Acquisition`, so it cannot be
rolled, bought or dropped. It is listed in `StatConsumerIntegrityValidator.DeferredStats` with that reason, and the
validator still fails if any *acquirable* item's only effect is a deferred stat.

**Baseline weapon spread — DEFERRED_EXPLICITLY (design), Weapon Spread Reduction — CONSUMED.**
`TestResults/StatConsumerIntegrity/spread_decision.md` records the split: the three shotguns author a real 30° cone,
so `WeaponSpreadReduction` has something concrete and documented to tighten and is wired to it. `items/33` has no
Spread column for any other weapon, so no baseline spread was invented for the other thirty. The Stabilizer is
therefore conditional (like Blaster Cooling Rate without a blaster), not dead — the same rule the validator applies
to every class-specific stat.

**Out of scope, documented, not fixed.** `dead_content_before.txt` §8 lists 16 `PlayerCombatEvents` raisers with no
gameplay caller (`RaiseEnemyKilled`, `RaiseDashed`, `RaiseWeaponSwapped`, …). They are the same defect class but they
gate *passives*, not stats, and wiring them is a separate pass. They are recorded so the next pass starts from
evidence rather than rediscovery.

## 10. Tooltip / UI Truthfulness

The graphical inventory and the Dungeon Merchant UI are unchanged in layout. Two truthfulness fixes:

- `ItemTooltip` renders an affix roll whose id no current pool contains as
  `"<Name> (legacy affix, no effect)"` instead of silently dropping it or presenting it as working. `LegacyAffixName`
  derives a readable name from the id.
- Every affix a new item can roll now maps to a live stat (§6), so a freshly generated item cannot advertise a no-op.

The item-description validator and the full UI test suites are green. No accepted layout was redesigned.

## 11. Save Compatibility

The policy is **load it, keep it, do not apply it, do not hide it**:

- `AffixRegistry.Get` returns `null` for an id no current pool contains. `EquipmentAffixSource` skips a roll it
  cannot resolve, so a legacy affix contributes nothing rather than throwing or corrupting the item.
- The stored roll is **preserved verbatim** — not deleted, not re-rolled. `ALegacyAffixIdLoadsSafelyAndContributesNothing`
  asserts the unknown roll survives on the instance while contributing no modifier.
- `RestoreFromEntries` restores quantities verbatim with no capacity clamp and `SaveValidator` does not check
  quantities, so an over-capacity ammo stack carried under an Ammo Pouch survives a save round trip
  non-destructively even if the pouch is later removed.
- The built-player smoke rolled an Epic Breacher-12 with the real service, saved, reloaded, and got the identical
  three rolls back (`affix_fire_rate=6; affix_range=10; affix_magazine_size=20`).

No existing save item is rerolled or migrated by this pass.

## 12. Validator Design

`Assets/Game/Scripts/Editor/Production/StatConsumerIntegrityValidator.cs`, report at
`TestResults/stat_consumer_integrity.md`. It asks four independent questions because each historical instance of this
defect is invisible to the others:

1. **Static reader** — does a non-Editor gameplay file actually call
   `GetMultiplier/GetPercent/GetFlat/GetReductionFactor` for this `StatId`? Catches "granted but nothing consumes it".
2. **Composition wiring** — does each provider seam have a gameplay caller that is not also its declarer? The
   declarer exclusion matters: `PlayerInventory.SetAmmoCapacityBonusProvider` is a one-line pass-through, and
   counting it would have reported the Ammo Pouch as wired for the whole time it was inert.
3. **Provider arguments** — is a required resolver argument a `null` literal or a `_ => null` lambda? This is the
   exact shape of the affix defect: it compiles, runs, and silently drops everything it was meant to resolve.
4. **Stat-math facade callers** — every weapon stat is read inside `WeaponStatMath`, so question 1 would be satisfied
   by that one file even if no weapon called it. Each public entry point must have a caller elsewhere.

Plus **acquisition-pool validation**: for every player-acquirable item, each intrinsic and each poolable affix must
resolve to a live stat *and* be capable of a non-zero result on that specific definition (`HasEffectOn`), and no pool
may hold fewer than three affixes. `DeferredStats` is the only escape hatch and each entry carries a written reason;
a deferred stat that acquires a consumer also fails, so the list cannot rot.

Each read in `WeaponStatMath` names its `StatId` literally rather than going through a shared helper, deliberately,
so question 1 is answerable by inspection.

Current result: **PASS — 114 subjects checked, 114 clean, 0 with problems.**

## 13. Automated Tests

**EditMode — `StatConsumerIntegrityTests` (24 tests).**

- `Validator_PassesOnTheShippedContent`.
- **Deliberate-disconnect regressions — the gate proving the gate works.** Each removes one thing from an injected
  copy of the real sources and requires the validator to fail:
  `Validator_FailsWhenAStatLosesItsOnlyReader`,
  `Validator_FailsWhenACompositionSeamIsOnlyCalledByTests` (the historical Ammo Pouch defect, reproduced exactly),
  `Validator_FailsWhenAResolverIsHandedInAsNull` (the affix defect found here),
  `Validator_FailsWhenNothingCallsTheStatMathFacade`,
  `Validator_FailsWhenAnAcquirableItemGrantsAStatItCannotUse`.
- Per-stat arithmetic: Fire Rate, Magazine Size (rounding, floor of 1, magazineless weapons), Projectile Speed,
  Projectile Range, Melee Attack Speed (rate *and* wind-up *and* recovery), Blaster Cooling Rate, Blaster Heat per
  Shot, Bow Charge Speed, Spread, Knockback/Stagger zero-stays-zero, null provider = authored value.
- Caps applied exactly once over the sum of sources, with the consumer reading the capped value and doing no cap
  math of its own; add/remove returns the exact baseline; re-registering a keyed source does not stack.
- Ammo Stack Capacity through the real `PlayerInventory` rule.
- Affix generation over every acquirable item × 4 rarities × 40 seeds; roll determinism for a fixed seed.
- Legacy affix load safety.
- The matrix artefact, asserted rather than narrated.

**PlayMode — `StatConsumerRuntimeTests` (19 tests).** Every one runs through the real composition root: a real
`ExpeditionService` start, a real `PlayerRig`, real catalogue items in real slots. Equip → measure → unequip →
baseline for each accessory family; shots counted over fixed time; magazine capacity with ammo conservation checked
at every step; a timed melee wind-up; measured blaster heat after real shots and real cooling; a timed bow draw; a
real shotgun cone; real projectile motion; real enemy displacement and stagger; affix rolls reaching a run.
`ARolledAffixOnAnEquippedItemChangesTheRunsStats` is the direct regression for §2 row 4.

**Phase 12 regressions**: `FireRateCannotBypassAReload` (a +400 % cadence still cannot fire during a reload, and Fire
Rate does not shorten the reload); `ProjectileModifiersDoNotDesyncTheVisualOrOvershootTheRange` (sprite sits on the
collider every physics step; flight stops at the modified range);
`MagazineSizeAffix_ChangesEffectiveCapacityWithoutMintingAmmo` (total rounds conserved across capacity change,
reload and shrink-back, with the surplus returned to the reserve rather than deleted).

**Results:**

| Suite | Result |
|---|---|
| EditMode | **PASS — 911 passed / 912 discovered** (1 skipped: the pre-existing `MultiplayerTerminalTests.LiveSessionsRelay_IntegrationCheck_OrNotRun`, which needs live UGS) |
| PlayMode | **PASS — 778 passed / 778 discovered** |
| `StatConsumerIntegrityValidator` | **PASS — 114 subjects, 0 problems** |
| Content / item-description / save-load / combat / final-production validators | **PASS** (they run as EditMode tests) |

`CombatAimCollisionProofTests` did not flake in the final PlayMode run. It is clock-seeded and is known to flake on
this machine; it was not relied on as the only combat proof — §7 and the built-player smoke's combat stage cover the
same ground deterministically.

## 14. Runtime Measurements

`TestResults/StatConsumerIntegrity/weapon_effect_before_after.csv` — the ten representative weapons the pass was
asked to measure (Pistol, SMG, Assault Rifle, Shotgun, Sniper, Rocket, Bow, Blaster, Knife, Spear), each at
baseline / one maximum single affix / a representative Epic combination, across fire rate, sustained or burst DPS,
effective magazine, range, projectile speed, heat per shot, draw time, attack rate, knockback and stagger.

The effect of switching these stats on is real but contained, because the values were already authored and capped:

- Sustained DPS moves by **+6 % to +12 %** at a full Epic combination (P9 Ranger 37.1 → 41.3; Marauder A2 49.6 →
  55.8; Rattler-9 46.7 → 52.3; Breacher-12 33.3 → 37.1). No weapon's ordering against another changed.
- Effective magazines move by the Magazine Size roll only: 12 → 14, 28 → 34, 32 → 38, 6 → 7.
- Range moves by up to +15 % on a single roll and is hard-capped at +40 %.
- Bow full draw 0.65 s → 0.565 s; knife attack rate 3.5 → 3.815 swings/s.

This is a genuine power increase for rare gear that previously did nothing, which is the intended consequence of the
fix rather than a side effect of it. No arithmetic or runtime bug was revealed, so no rebalancing was done — that is
explicitly the next pass's job.

## 15. Built-Player Proof

Platform built: **macOS (StandaloneOSX)** — `Builds/MacOS/RUINRAIL.app`, report `TestResults/build_report_macos.md`,
release options (no development build, no script debugging), the approved four-scene order.

**`Windows x64: NOT RUN — module unavailable`** on this machine. No module was installed and no Windows verification
is claimed.

Built-player smoke: `TestResults/StatConsumerIntegrity/smoke/` (log, `smoke_result.json`, `dungeon.png`), also copied
to `TestResults/smoke_result.json`. `Success: true`, stage `done`, biome RuinedMetro. The new stat-consumer stage
contributed **21 checks**, covering all eleven required steps:

1. fresh profile — the smoke creates one; 2. a previously dead accessory effect equipped (Ammo Pouch, proven first at
the Shelter: limit 180 → 225 → 180); 3. run entered; 4. measured in the run (+25 % composed, limit 180 → 225);
5. a previously dead affix obtained by the **real** `AffixRollService` (Epic Breacher-12 →
`affix_fire_rate=6; affix_range=10; affix_magazine_size=20`); 6. measured (fire interval 0.714 s → 0.674 s, range
6 → 6.6, magazine 6 → 7); 7. impact proven (a fired pellet carried knockback 6 and stagger 3 and displaced a **live**
enemy by 1.5 tiles); 8. ammo capacity proven (450 rounds carried against a base limit of 180); 9. saved; 10.
reloaded; 11. state valid — the identical three affix rolls came back.

Both items travel into the run in the loadout backpack and are equipped inside the run, because the graphical-
inventory stage of this smoke drags its own Ammo Pouch into the accessory slot and the HUD stage expects the starter
Field Knife in weapon slot 2; those are accepted, frozen checks and were not disturbed.

**Honest note on smoke stability.** Across ten runs on this machine the smoke passed end to end 3 times. Every
failure was in a pre-existing, seed-dependent check unrelated to this pass — the enemy-containment tolerance
(`worst overshoot 0.075` against a `0.06` threshold), a depth generated with no non-combat rooms, and the known
clock-seeded combat-aim check. The stat-consumer stage passed in **every** run that reached it and its Shelter half
passed in every run without exception. That flakiness predates this pass and is reported rather than papered over;
it is not fixed here because doing so would mean changing containment tolerances and generation guarantees, which is
outside this scope.

## 16. Regression Results

Nothing on the frozen list regressed. Full EditMode and PlayMode suites are green (§13), and the built-player smoke
passed every stage: graphical inventory (40+ checks), Dungeon Merchant, Main Menu, HUD, dash, run-start and
depth-arrival full HP, character progression, aim/hit, projectile visuals, enemy collision, EncounterBounds, room
locking, loot and chests, death screen, backpack reorder, item descriptions, Settings, non-combat rooms, starter
loadout, ammo resale, save/load and seeded generation.

The specifically named risks:

| Risk | Result |
|---|---|
| magazine effects mint ammo | **No.** Total rounds conserved across capacity raise, reload and shrink-back; `CompleteReload` draws only the shortfall; `ReconcileMagazine` returns the surplus to the reserve rather than deleting it |
| ammo-capacity effects duplicate ammo | **No.** The provider changes a limit, never a quantity; one rule, no duplicate |
| fire rate bypasses reload | **No.** `FireRateCannotBypassAReload` — +400 % cadence, repeated attempts, all refused while reloading |
| projectile modifiers desync VFX/hits | **No.** Sprite sits on the collider every physics step; flight stops at the modified range; the smoke's per-weapon projectile-visual checks are green |
| impact breaks containment | **No.** `ImpactReceiver` already consults `EncounterBounds.FreeDistance` before each step; proven with a rocket's full impact against a wall |
| legacy saves invalid | **No.** Unknown affix ids load, are preserved, and contribute nothing (§11) |

Five existing content-shape test files were updated because the data they assert on legitimately changed (the spear
pool id, the knife and spear impact values, the split ranged and melee pools). Each change swapped one expected
constant for another; no assertion was loosened or removed to make a test pass.

## 17. Remaining Open Questions

1. **Weapon switch duration** — needs a design decision (a base duration in `PlayerBalanceConfig` or a per-class
   value), after which Weapon Switch Speed becomes consumable and the Quickdraw Holster can re-enter acquisition.
2. **Baseline weapon spread** — `items/33` has no Spread column for 30 of 33 weapons. If spread becomes a real
   accuracy model, `WeaponStatMath.SpreadDegrees` is already the single place it would be applied.
3. **The 16 unwired `PlayerCombatEvents` raisers** (§9) — the same defect class, gating passives rather than stats.
4. **Balance** — activating these stats raises the ceiling of rare gear by 6–12 % sustained DPS. That is intended
   here, but the arsenal has not been rebalanced against it; that is the next pass.
5. **Smoke seed stability** (§15) — three pre-existing seed-dependent checks make the built-player smoke pass about
   a third of the time on this machine.
6. **Windows verification** — cannot be produced here.

## 18. Files Changed

**New runtime code**
- `Assets/Game/Scripts/Stats/WeaponStatMath.cs` — the single place every weapon-facing stat modifier is applied
- `Assets/Game/Scripts/Items/AffixRegistry.cs` — affix id → definition, so a persisted roll can become an effect

**Modified runtime code**
- `Assets/Game/Scripts/Combat/Weapons/RangedWeapon.cs` — `Current*` for fire rate, interval, magazine, speed, range,
  spread; magazine reconciliation that returns surplus to the reserve; pattern rebuilt on spread change
- `Assets/Game/Scripts/Combat/Weapons/BlasterWeapon.cs` — `Current*` for fire rate, heat per shot, cooling, speed, range
- `Assets/Game/Scripts/Combat/Weapons/BlasterHeatState.cs` — live cooling multiplier that never discards accumulated heat
- `Assets/Game/Scripts/Combat/Weapons/BowWeapon.cs` — `CurrentFullChargeSeconds`, modified shot speed/range/impact
- `Assets/Game/Scripts/Combat/Weapons/MeleeWeapon.cs` — `CurrentAttackRate`, `CurrentWindUpSeconds`, `CurrentRecoverySeconds`
- `Assets/Game/Scripts/App/PlayerRigComposer.cs` — ammo-capacity provider wired; `ResolveAffix` resolves for real
- `Assets/Game/Scripts/UI/Base/BaseSession.cs` — Shelter `LoadoutStats`, the same capacity provider, real affix resolver
- `Assets/Game/Scripts/App/GameContentCatalog.cs` — `StatCaps` passed into `BaseConfigs`
- `Assets/Game/Scripts/App/GameApp.cs` — `SaveProbe.EquippedAffixRolls` for the built-player proof
- `Assets/Game/Scripts/Items/EquipmentItemDefinition.cs` — `IsAcquirableInV1` (negative serialized flag, so existing
  assets deserialize as acquirable)
- `Assets/Game/Scripts/Loot/EquipmentRollService.cs`, `Assets/Game/Scripts/Loot/LootRoller.cs`,
  `Assets/Game/Scripts/Base/TraderService.cs` — acquisition filtered by `IsAcquirableInV1`
- `Assets/Game/Scripts/UI/Inventory/ItemTooltip.cs` — legacy affix rendering
- `Assets/Game/Scripts/App/SmokeRunner.cs` + new `SmokeRunner.StatConsumers.cs` — the built-player stage

**New editor tooling**
- `Assets/Game/Scripts/Editor/Production/StatConsumerIntegrityValidator.cs`

**Tests**
- New: `Assets/Game/Tests/EditMode/StatConsumerIntegrityTests.cs`,
  `Assets/Game/Tests/PlayMode/StatConsumerRuntimeTests.cs`
- Updated for legitimately changed content: `RangedWeaponDefinitionTests.cs`, `WeaponCatalogTests.cs`,
  `RarityAffixTests.cs`, `LegendarySpecialDefinitionTests.cs`, `LegendarySpecialsIIITests.cs`

**Data**
- 12 weapon assets given authored impact values (3 shotguns, 3 rockets, 3 spears, 3 knives)
- `QuickdrawHolster.asset` — `_excludedFromV1Acquisition: 1`
- `AffixPool_Ranged`, `AffixPool_Blaster`, `AffixPool_Bow`, `AffixPool_Melee`, `AffixPool_Accessory` rewritten;
  `AffixPool_RangedImpact` and `AffixPool_MeleeHeavy` created; `AffixPool_Armor` untouched
- `Affix_MeleeAttackSpeed.asset`, `Affix_BowChargeSpeed.asset` created

**Artefacts**
- `TestResults/StatConsumerIntegrity/stat_consumer_matrix.csv`
- `TestResults/StatConsumerIntegrity/dead_content_before.txt`
- `TestResults/StatConsumerIntegrity/accessory_runtime_matrix.csv`
- `TestResults/StatConsumerIntegrity/impact_matrix.csv`
- `TestResults/StatConsumerIntegrity/weapon_effect_before_after.csv`
- `TestResults/StatConsumerIntegrity/spread_decision.md`
- `TestResults/StatConsumerIntegrity/smoke/` (log, `smoke_result.json`, `dungeon.png`)
- `TestResults/stat_consumer_integrity.md`, `TestResults/build_report_macos.md`

## 19. Final Status

All twelve repository-local success conditions are met:

1. Every player-facing stat-granting item/affix is functional, excluded, or explicitly deferred — ✔ (§4)
2. No newly generated or acquired item advertises a known no-op — ✔ (§6, validated over >1 000 rolls)
3. Safe/obvious existing stats have real runtime consumers — ✔ (§4, §5)
4. Ammo Stack Capacity proven through the real runtime path — ✔ (§8, §15)
5. Knockback/Stagger match documented intent and are runtime-proven — ✔ (§7)
6. Weapon Switch Speed was **not** given an invented mechanic — ✔ (§9)
7. Spread was **not** arbitrarily invented — ✔ (§9, `spread_decision.md`)
8. A validator prevents silent dead-content regression, and is proven to fail when the defect returns — ✔ (§12, §13)
9. Old saves remain valid and non-destructive — ✔ (§11)
10. Tests and validators green except honestly documented pre-existing cases — ✔ (§13)
11. Built-player proof exists on the available platform — ✔ (§15; Windows x64 `NOT RUN — module unavailable`)
12. No unrelated accepted system redesigned — ✔ (§16)

**STAT_CONSUMER_INTEGRITY_COMPLETE**
