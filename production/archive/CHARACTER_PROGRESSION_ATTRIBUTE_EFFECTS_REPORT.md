# RUINRAIL — Character Progression / Attribute Effects, End-to-End Fix Pass

> **Status:** Verification report for the progression → runtime correctness pass.
> **Date:** 2026-09-23 · **Unity:** 6000.3.24f1 (pins unchanged, `ENVIRONMENT.md`).
> **Terminal status: `CHARACTER_PROGRESSION_ATTRIBUTE_EFFECTS_COMPLETE`**

---

## 0. Terminology note (the prompt's "Main Menu" vs. this repository)

The prompt speaks of upgrading attributes "from the Main Menu". In this repository the Main Menu has no progression
screen: `MainMenuScreen` is PLAY / SETTINGS / QUIT, and PLAY opens the Shelter. Attributes are bought at the Shelter's
**CHARACTER station** (`BaseStation.Character`), which is the authoritative progression UI (`base/73_CHARACTER_STATION`,
`ui/94`, and `player/13`: "Skill points may only be spent in The Shelter"). Everything below therefore reads
"Main Menu progression screen" as "the Character Station reached from the Main Menu's PLAY".

---

## 1. The authoritative attribute list

Derived from `player/13_LEVELING_AND_SKILL_POINTS.md` and `Assets/Game/Scripts/Progression/Skills.cs`. Six attributes,
ten ranks each, one Skill Point per rank, one Skill Point per level, level cap 61 → exactly 60 points → every rank of
all six is reachable.

| Attribute | Effect per rank | At rank 10 | Derived stat(s) | Gameplay consumer |
|---|---|---|---|---|
| **Vitality** | +2 Max HP (flat) | +20 HP | `MaxHealth` | `PlayerStatsBinder` → `HealthComponent.ResizeMaxHealth` |
| **Power** | +1 % Weapon Damage | +10 % | `WeaponDamage` | `RangedWeapon` / `BowWeapon` / `BlasterWeapon` / `MeleeWeapon` damage roll |
| **Mobility** | +1 % Movement Speed | +10 % | `MovementSpeed` | `PlayerMovement.CurrentMoveSpeed` |
| **Recovery** | +2 % Healing Received | +20 % | `HealingReceived` | `ConsumableEffectRunner` heal |
| **Handling** | +1 % Reload Speed **and** +1 % Weapon Switch Speed | +10 % each | `ReloadSpeed`, `WeaponSwitchSpeed` | `RangedWeapon.CurrentReloadTime` — **`WeaponSwitchSpeed` has no consumer** (§12) |
| **Resilience** | +2 % Knockback Resistance **and** +2 % Stagger Resistance | +20 % each | `KnockbackResistance`, `StaggerResistance` | `PlayerImpactReceiver` (knockback displacement, stagger meter) |

The design table is asserted against the implementation as an oracle in
`CharacterProgressionAttributeTests.EveryAttribute_MatchesTheApprovedDesignTable` — a change to `SkillRules` that
contradicts `player/13` now fails a test rather than shipping.

Machine-readable: `TestResults/CharacterProgressionAttributeProof/attribute_effect_matrix.csv` (6 rows; internal id,
display name, current rank, max rank, cost rule, effect per rank, effect source, derived stats, consumers, stacking,
persistence field, runtime propagation path).

---

## 2. Root cause

**One defect, in exactly one place, and it disconnected all six attributes at once.**

`SkillStatSource` — the adapter that feeds `PlayerProfile.Skills` into the stat pipeline — existed, was correct, was
unit-tested, and **was never instantiated by any runtime code**. Before this pass:

```
$ grep -rn "SkillStatSource" Assets --include='*.cs'
Assets/Game/Tests/EditMode/ProgressionTests.cs:126       (test)
Assets/Game/Tests/EditMode/CharacterStationTests.cs:56   (test)
Assets/Game/Scripts/Progression/Skills.cs:102            (the definition itself)
```

`PlayerRig.Build` registered the `LoadoutStatRegistrar` (armor / accessory / affixes) on `PlayerStats` and nothing
else. The profile's `SkillAllocation` never reached the run, so `PlayerStats` never saw a single progression modifier.

Everything *else* in the chain was already correct, which is why the symptom looked like a UI bug:

* the purchase transaction debits exactly one point and increments exactly one rank (`ProgressionService.TrySpend`);
* the rank is written to `PlayerProfile.Skills`, marked dirty by `AutosaveBinder` (`"skills"`) and persisted;
* `PlayerStats` sums sources and clamps once, so nothing could have overwritten the bonus — there simply was none;
* `PlayerRig.InitializeRunHealth` and `DepthArrivalHeal` already read `PlayerStats.MaxHealth`, so both filled to an
  effective maximum that had no Vitality in it.

**Vitality was not a special case.** The rank rose, the profile saved, the UI showed the new number, and the run
computed `MaxHealth` from base + equipment only — 100/100 or 120/120, never 110/110.

### Two further defects found while verifying

2. **The Character panel showed stale values after a keyboard/controller purchase.** `BaseHubScreen` refreshed the
   station's data column from the *pointer* activation path only (`UiKit.Control(... RefreshStationData())`).
   Enter / gamepad-A go through `MenuInput.Poll → FocusStack.Activate`, which had no such refresh, so the panel kept
   the ranks it was built with until the player left and re-entered the station. This is precisely the
   "UI showing stale derived values" failure mode the prompt lists. Fixed by subscribing to the authoritative
   `CharacterStation.Changed` event; regression-tested (and the test was verified to fail without the fix).

3. **The built-player smoke asserted the wrong cursor invariant** (pre-existing; see §13).

---

## 3. The upgrade transaction, end to end

| # | Step | Owner | Verified by |
|---:|---|---|---|
| 1 | XP earned in a run, committed to the profile immediately | `ExpeditionService.RecordEnemyDefeated → ProgressionService.AddXp` | `ProgressionTests`, smoke |
| 2 | Level and unspent points derived from lifetime XP | `LevelCurve`, `ProgressionService.ReconcilePoints` | `ProgressionTests` |
| 3 | Panel shows rank / cap, price, description, effect now, next-rank preview | `StationPresentation.Character` ← `CharacterPanelViewModel` ← `SkillCatalog` | `CharacterPanel_ShowsRankCostDescriptionPreviewAndMax`, captures |
| 4 | Control offered only if the purchase would succeed (at base, below cap, point in hand) | `ScreenNavigation.Character` `isEnabled` | `CharacterControls_AreOfferedOnlyWhenThePurchaseWouldSucceed` |
| 5 | Affordability + cap checked authoritatively; rejection is atomic | `ProgressionService.TrySpend` | `Purchase_DeductsExactlyOnce…`, `MaxRank_IsRejected_AndNeverDeducts` |
| 6 | Exactly one point debited, exactly one rank incremented | `ProgressionService.TrySpend` | ditto + 15 smoke lines |
| 7 | `SkillsChanged` → profile marked dirty → save written | `AutosaveBinder`, `AutosaveService` | `AutosaveTests`, smoke reload |
| 8 | Panel refreshed from the authoritative sheet (**fixed**) | `BaseHubScreen.OnCharacterSheetChanged` | `CharacterStation_ShowsTheNewRank_AfterAKeyboardPurchase` |
| 9 | Run launched; profile progression read | `TransitPanelViewModel → ExpeditionService.Start` | smoke |
| 10 | **Progression registered on the stat pipeline (fixed)** | `PlayerEntityBuilder.Options.Progression → PlayerStatsBinder.ApplyProgression` | all runtime proofs |
| 11 | Equipment sources registered on top and summed | `LoadoutStatRegistrar` | equipment matrix |
| 12 | Authoritative `PlayerStats` produced, clamped once | `PlayerStats.Recompute` | rank sweep, caps test |
| 13 | Gameplay consumers read the final values | movement / weapons / health / impact / consumables | runtime evidence |

An open expedition's `ExpeditionService.Start` reconciles spent-vs-earned points (anti-exploit). A profile whose
allocation exceeds what its XP paid for is repaired; a legitimate allocation is preserved untouched. This surfaced
during verification when a test fixture allocated ranks on a 0-XP profile and had them correctly refunded — the
fixture was wrong, not the rule.

---

## 4. Stat composition order (one authoritative pipeline)

`PlayerStats` is unchanged: sources are keyed by a stable id, every source's modifiers are summed per stat, and the
global cap (`player/16`) is applied **once** to the sum. Insertion order is irrelevant by construction.

```
base stats (PlayerBalanceConfig: 100 Max HP, 5 u/s move)
  + "skills"            ← SkillStatSource(PlayerProfile.Skills)      [NEW: was missing]
  + "equipped:<id>" ×N  ← armor base, accessory intrinsic, affixes
  + "passive:*" / "consumable:*" / "buff:*"  ← temporary effects
  = summed per stat → clamped once at the player/16 cap → PlayerStats
                                                          ↓
      MaxHealth · MovementSpeed · WeaponDamage · ReloadSpeed · HealingReceived · resistances → consumers
```

The progression source is **owned by `PlayerStatsBinder`**, not registered once by a caller. `Configure()` rebuilds the
`PlayerStats` instance, so a caller-registered source could be silently dropped by a later composition step; holding
the allocation makes re-registration automatic. Registration is by the constant key `SkillStatSource.Id` (`"skills"`),
so applying it repeatedly is idempotent and can never double a bonus — asserted directly
(`ApplyProgression` called three times → unchanged value, exactly one source id).

Regression tests for the order: `Progression_SumsWithEquipment_AndNeitherOverwritesTheOther` (gear added after,
temporary buff on top, every gear source removed again — the permanent bonus is untouched by all of it) and
`GlobalCaps_ClampTheSumOnce_NeverTheAttributeAlone`.

---

## 5. Vitality — deep verification

All figures measured in a real `PlayerRig` run (`runtime_attribute_evidence.txt`), base 100 HP:

| Case | Expected | Actual | Result |
|---|---:|---:|---|
| Rank 0 baseline | 100 | 100 | PASS |
| Rank 1 | 102 | 102 | PASS |
| Rank 3 | 106 | 106 | PASS |
| Rank 5 | 110 | 110 | PASS |
| Rank 10 (max) | 120 | 120 | PASS |
| Armor only (Scrap Vest +20) | 120 | 120 | PASS |
| Armor + rank 7 | 134 | 134 | PASS |
| Heal cap after damage at rank 7 + armor | 134 | 134 | PASS |
| HUD `Snapshot.MaxHp`, rank 6 + armor | 132 | 132 | PASS |
| **Run start**, every case above | `CurrentHP == EffectiveMaxHP` | equal | PASS |
| **Descend to depth 2**, rank 8 + armor | 88 → 136 / 136 | 136 / 136 | PASS |

Built player (`prog_05…png`): the HUD reads **130 / 130** — base 100 + Scrap Vest 20 + Vitality 5 (+10) — at the first
frame of the run, and **130 / 130** again on depth 2 after the descend.

Same-depth operations must **not** heal, and do not: a `PlayerStats.Recompute()`, a `StatsBinder.Bind()`, a repeated
`ApplyProgression()`, a mid-run weapon unequip and a mid-run armor unequip all leave current HP exactly where it was
(`Vitality_NewDepthFillsToTheModifiedMaximum_AndNothingElseDoes`, equipment matrix). Room entry, inventory open/close,
pause and save events are covered by the pre-existing depth-heal smoke stage (`DepthArrivalHeals == 1`).

---

## 6. Every other attribute — runtime proof

From `TestResults/CharacterProgressionAttributeProof/runtime_attribute_evidence.txt`:

| Attribute | Rank | Baseline | Upgraded | Expected | Consumer | Result |
|---|---:|---|---|---|---|---|
| **Power** | 5 | 20 dmg | 21 | 21 | projectile damage actually applied to a target's `HealthComponent` | PASS |
| **Power** | 10 | 20 dmg | 22 | 22 | ditto | PASS |
| **Mobility** | 10 | 5.0 u/s | 5.5 u/s | 5.5 | `PlayerMovement.CurrentMoveSpeed` | PASS |
| **Mobility** | 10 | 4.00 u travelled | 4.40 u travelled | ratio 1.10 | measured over 40 fixed steps | PASS |
| **Recovery** | 5 | 40 HP restored | 44 | 44 | `ConsumableEffectRunner` heal | PASS |
| **Recovery** | 10 | 40 HP restored | 48 | 48 | ditto | PASS |
| **Handling** | 10 | 2.0000 s | 1.8182 s | 1.8182 | `RangedWeapon.CurrentReloadTime` | PASS |
| **Handling** | 10 | — | measured reload **1.8183 s** | < 2 s | the real reload timer, frame-measured | PASS |
| **Resilience** | 10 | 6.00 stagger pressure | 4.80 | 4.80 | `PlayerImpactReceiver` stagger meter | PASS |
| **Resilience** | 10 | 2.00 u knockback | 1.60 u | 1.60 | `PlayerImpactReceiver` displacement | PASS |

Rank 1 of Power reads 20 → 20: `round(20 × 1.01) = 20`. That is the approved integer-damage rule, not a missing
effect — ranks 5 and 10 move the same roll to 21 and 22, and in the built player the equipped P9 Ranger goes from
12–14 to **13–15** at Power rank 5.

Every consumer is the *real* one: the damage is the value the projectile carries and the HP the target loses, the
movement figure is accompanied by a measured displacement, the reload figure by a measured reload.

---

## 7. Full rank sweep

`TestResults/CharacterProgressionAttributeProof/attribute_rank_sweep.csv` — **88 rows, 88 PASS, 0 FAIL**
(6 attributes × their affected stats × ranks 0–10 = 8 stat-series × 11 ranks).

Per row: expected value from the authoritative rule vs. the value `PlayerStats` actually produced, plus a step check
that every rank is worth exactly one more increment than the rank below it (no skipped rank, no double step), plus an
overflow check that `SetRank(max + 5)` clamps to 10.

---

## 8. Equipment / progression interaction matrix

Every case is a real `PlayerRig` run; expected values are computed from the item definitions' own `BaseModifiers()`
and the per-rank rule, never from the pipeline under test (Vitality 6, Mobility 3):

| Case | Max HP (exp/act) | Movement (exp/act) | Run start | Result |
|---|---|---|---|---|
| no gear, no progression | 100 / 100 | +0 % / +0 % | 100/100 | PASS |
| no gear + progression | 112 / 112 | +3 % / +3 % | 112/112 | PASS |
| armor only | 122 / 122 | +0 % / +0 % | 122/122 | PASS |
| armor + progression | 134 / 134 | +3 % / +3 % | 134/134 | PASS |
| starter loadout + progression | 132 / 132 | +3 % / +3 % | 132/132 | PASS |
| accessory only | 100 / 100 | +5 % / +5 % | 100/100 | PASS |
| accessory + progression | 112 / 112 | +8 % / +8 % | 112/112 | PASS |
| armor + accessory + progression | 134 / 134 | +8 % / +8 % | 134/134 | PASS |
| mid-run weapon unequip | 134 unchanged | — | HP not healed | PASS |
| mid-run armor unequip (next-depth-style rebuild) | 112 = base + Vitality only | — | — | PASS |

No combination was skipped: the catalog carries both an armor with a Max HP bonus and an accessory with a Movement
Speed intrinsic, so every row of the required matrix has real data.

---

## 9. Save / load / migration

* Every rank round-trips: six attributes set to six different ranks, saved, reloaded — all six restored, and the
  reloaded allocation reaches the stat pipeline (`EveryAttributeRank_SurvivesSaveAndReload_AndAppliesAfterwards`).
* Unspent points reconcile against the reloaded ranks; legitimate progression is never reset
  (`ProfileWithRanksButNoLevels_IsRepairedWithoutLosingLegitimateProgression`).
* **Older saves that stored ranks which were never applied now apply them.** A legacy v1 bare-profile document with
  `Resilience 6` / `Recovery 2` migrates to the v2 envelope with both ranks intact and both reaching the pipeline
  (`LegacyV1Document_MigratesWithEveryAttributeRankIntact`). No schema change was needed: `SkillAllocation` was already
  a serialized field of `PlayerProfile`, so no migration step was added and no existing progression is touched.
* Built player: purchase → leave the station → re-enter → save → reload proves the ranks on disk
  (`SaveProbe.SkillRanks`, added for this).

---

## 10. Network / co-op

Verified on the deterministic local harness (`Assets/Game/Tests/PlayMode/CharacterProgressionAttributeProofTests`):

* **Per-player isolation** — three player objects composed with different allocations: host 120 Max HP / +10 % damage,
  client 104 Max HP / +0 % damage, a third with no allocation at 100 Max HP. No cross-contamination.
* **Authority** — progression enters the pipeline only through `PlayerStatsBinder.ApplyProgression`, fed from the
  profile the host composes. No UI path, RPC or `NetworkVariable` writes `PlayerStats`; `NetworkPlayerCombat` sends
  input intents only and the host runs the unchanged weapon components. A client cannot set its own combat stats.
* **Reconnect / recomposition** — applying the same allocation repeatedly is idempotent (one keyed source, value
  unchanged); removing it removes exactly its contribution and nothing else.
* **Live UGS / Relay: NOT RUN** — no live service in this environment. Not claimed.

---

## 11. Main Menu (Shelter) progression UI

The Character Station's data column now shows, per attribute: **name · rank / cap** (with `MAX` at the cap), a
one-line description, and one line per affected stat giving the value now and what the next point buys
(`Max HP +10->+12`, `Knockback Resistance +20% MAX`). The header carries LEVEL, XP, SKILL POINTS and **RANK COST
1 POINT**; the respec control carries its real Banked-Coin price. See `prog_03_max_rank_and_unaffordable_states.png`.

* **At the cap:** the row reads `10 / 10 MAX`, every effect line ends in `MAX`, no next-rank preview is offered, the
  control is disabled, and a purchase attempt deducts nothing.
* **When unaffordable:** the control renders in its disabled state and the panel subtitle says why
  ("No Skill Points. Earn XP on an expedition."). Nothing is deducted.
* **No invented values.** Every number on the screen is formatted from `SkillRules.ModifiersForRank` through
  `SkillCatalog`; the authored descriptions contain no numerals at all, so a description cannot go stale. Percentages
  are formatted by the shared `StatLabels.Format`, so an attribute and an item never read two different ways.
* The Character station gets a narrower control column (0.31 of the panel body) so its data column can draw full
  sentences; every other station is unchanged.

The Main Menu itself was not redesigned; no station was added, removed or reordered.

---

## 12. The one documented gap: `WeaponSwitchSpeed`

Handling is defined (`player/13`) as "+1 % reload speed **and** weapon switch speed", and `WeaponSwitchSpeed` is an
approved stat with its own global cap (`player/16`: +50 %) and an accessory that grants it (Quickdraw Holster, +15 %).
Its reload half is consumed and measured. **Its switch half has no consumer, because this build has no weapon-switch
duration to scale:** `WeaponLoadout.SelectSlot` swaps instantly, `IEquippableWeapon` has no ready/equip time,
`PlayerBalanceConfig` defines none, and no document in the repository states a base switch duration.

Per the prompt's rule for absent intent, no balance was invented — adding a switch delay would be a new game mechanic
and a new tunable number, which `technical/117` rules 1, 2 and 10 forbid without a design update. Instead the gap is
**pinned**: `AttributeDescriptionValidator` reports every stat an attribute feeds that no system reads, and
`AttributeDescriptionValidator_PassesAndPinsTheKnownDisconnectedStats` asserts that set equals exactly
`{ WeaponSwitchSpeed }`. A newly disconnected stat now fails a test immediately.

Handling itself is **not** cosmetic: its Reload Speed half is verified with a frame-measured reload (2.0000 s →
1.8183 s at rank 10), so no attribute is without a measurable runtime effect.

**Open design question for `06_OPEN_DECISIONS.md`:** does V1 want a weapon-switch duration at all? If yes, it needs an
approved base value; if no, Handling's design line and the Quickdraw Holster's intrinsic should be revised. Not decided
here.

---

## 13. Changed files

**New**

| File | Purpose |
|---|---|
| `Assets/Game/Scripts/Progression/SkillCatalog.cs` | Player-facing names, descriptions and data-formatted effect/preview text for the six attributes |
| `Assets/Game/Scripts/Editor/Production/AttributeDescriptionValidator.cs` | The attribute gate (text, per-rank effect consistency, next-rank preview, consumer scan) → `TestResults/attribute_descriptions.md` |
| `Assets/Game/Scripts/App/SmokeRunner.Progression.cs` | Built-player progression stage (39 checks + 6 proof captures) |
| `Assets/Game/Tests/EditMode/CharacterProgressionAttributeTests.cs` | 15 tests; writes the matrix and rank-sweep CSVs |
| `Assets/Game/Tests/PlayMode/CharacterProgressionAttributeProofTests.cs` | 9 runtime-proof tests; writes the runtime evidence file |

**Modified — the fix**

| File | Change |
|---|---|
| `Assets/Game/Scripts/Stats/PlayerStatsBinder.cs` | Owns the `SkillAllocation`; `ApplyProgression()` registers `SkillStatSource` by its stable key and re-registers after `Configure()` |
| `Assets/Game/Scripts/Player/PlayerEntityBuilder.cs` | `Options.Progression`; the one player composition applies it before any equipment source |
| `Assets/Game/Scripts/App/PlayerRigComposer.cs` | `Build(..., SkillAllocation progression)` — progression exists before the loadout registrar and before the run-start fill |
| `Assets/Game/Scripts/App/ExpeditionScene.cs` | Passes `session.Profile.Skills` into the rig |

**Modified — UI**

| File | Change |
|---|---|
| `Assets/Game/Scripts/UI/Base/BaseHubViewModel.cs` | `CharacterPanelViewModel`: rank, cap, cost, description, effect now / next, `CanAllocate`, `CanRespec`, status line |
| `Assets/Game/Scripts/UI/Base/StationPresentation.cs` | `StationRow.Text` (full-width line); the Character view's per-attribute block |
| `Assets/Game/Scripts/UI/Navigation/ScreenNavigation.cs` | Character controls carry their real price and are enabled only when the purchase would succeed |
| `Assets/Game/Scripts/App/BaseHubScreen.cs` | Renders text rows; wider data column for Character; **refreshes the panel on `CharacterStation.Changed`** (the stale-panel fix) |

**Modified — verification**

| File | Change |
|---|---|
| `Assets/Game/Scripts/App/GameApp.cs` | `SaveProbe.SkillRanks` / `UnspentSkillPoints` so the built player can prove persistence |
| `Assets/Game/Scripts/App/SmokeRunner.cs` | Calls the progression stage; `PointerLayerOwnsCursor` |
| `SmokeRunner.{Inventory,HudMerchant,RoomHudQol,StarterDeath}.cs` | Use `PointerLayerOwnsCursor` |
| `Assets/Game/Tests/EditMode/UiNavigationTests.cs` | Fixture earns Skill Points so the Character controls are purchasable |
| `Assets/Game/Tests/PlayMode/ShelterUiInteractionTests.cs` | `CharacterStation_ShowsTheNewRank_AfterAKeyboardPurchase` regression test |

### The smoke cursor assertion (pre-existing, corrected)

Five smoke checks asserted `CursorService.Current == CursorKind.Pointer` while a menu layer was open. `CursorService`
deliberately resolves the pointer to its **Hover** decoration whenever the mouse rests on a control of that very menu,
and in a built player the OS cursor sits wherever it was left. The check therefore tested where the mouse happened to
be, not that the menu had taken the cursor. Diagnosed from the shipped player:

```
[SMOKE] cursor diag: hoveredControls=[pause.Resume] ... mouse=(420.21, 260.53)
[SMOKE] FAIL playability: pause menu usable (showing=True controls=6 cursor=Hover base=Aim overlays=1 hover=True)
```

The pause layer *had* taken the cursor (`overlays=1`); the pointer simply rested on RESUME. The checks now assert the
invariant — `Resolve(Base, Overlays, hover:false) == Pointer` — which is strictly the same assertion whenever nothing
is hovered. **This failure reproduces with every line of this pass's code removed** (baseline build,
`TestResults/smoke-baseline.log`), so it was not introduced here; it is fixed because §20 requires a passing smoke.
No product behaviour was changed.

---

## 14. Tests added

**EditMode — `CharacterProgressionAttributeTests` (15)**
approved design table · 60 points max all six and nothing exceeds a cap · purchase deducts exactly once and every
rejection changes nothing · max rank rejected and never deducts · XP never negative and the point economy balances
(incl. respec) · every rank survives save/reload and applies afterwards · corrupt vs. legitimate allocation ·
legacy v1 migration · progression + equipment composition and no double application · caps clamp the sum once ·
panel shows rank/cost/description/preview/MAX · controls offered only when the purchase would succeed ·
description/effect validator gate · matrix CSV · rank-sweep CSV.

**PlayMode — `CharacterProgressionAttributeProofTests` (9)**
Vitality effective max HP and run-start fill · Vitality + armor and the heal cap · Vitality on the HUD ·
new-depth fill and no same-depth healing · Power damage applied · Mobility measured travel · Recovery healing ·
Handling measured reload (+ the documented `WeaponSwitchSpeed` gap) · Resilience stagger + knockback ·
equipment interaction matrix · per-player network isolation and authority.

**PlayMode — `ShelterUiInteractionTests` (1 added)**
the Character panel shows the new rank, effect and preview after a keyboard/controller purchase (verified to fail
without the `CharacterStation.Changed` subscription).

**Built player — `SmokeRunner.Progression` (39 checks)** listed in §16.

---

## 15. Validation gates

| Gate | Command | Result |
|---|---|---|
| Strict EditMode | `./scripts/run-unity-tests.sh EditMode` | **PASS — 887 passed / 888 discovered**, 0 failed, 1 ignored (`LiveSessionsRelay_IntegrationCheck_OrNotRun`, no live service) |
| Strict PlayMode | `./scripts/run-unity-tests.sh PlayMode` | **PASS — 759 passed / 759 discovered**, 0 failed |
| FinalProductionValidator | `FinalProductionValidatorTests` | PASS (1/1) |
| ContentCountValidator | `ContentCountValidatorTests` | PASS (3/3) |
| PresentationValidator | `PresentationValidationTests` | PASS (3/3) |
| AssetPipelineValidator | `AssetPipelineTests` | PASS (8/8) |
| ReleasePathScan | `ReleasePathVisualScanTests` | PASS (5/5) |
| **AttributeDescriptionValidator (new)** | `CharacterProgressionAttributeTests` | PASS — 6/6 attributes, `TestResults/attribute_descriptions.md` |
| ItemDescriptionValidator | `ItemDescriptionTests` | PASS (10/10) |
| FinalMvpAudit | `FinalMvpAuditTests` | PASS (1/1) |
| Save / migration | `SaveSlotTests`, `AutosaveTests`, progression persistence tests | PASS |
| Exploit / idempotency | `ExploitHardeningTests`, `PersistenceHardeningTests` | PASS |
| Network authority | `NetworkCombatTests`, `MultiplayerGateTests`, progression isolation | PASS |
| HP start / depth regression | `RunStartHealthTests`, `DepthArrivalHealTests`, `ArmorHealthInvariantTests` | PASS |
| Combat / movement | `PlayerStatsWiringTests`, `PlayerMovementTests`, weapon reference tests | PASS |

No test was weakened to accept broken behaviour. Two test-side corrections were made and both are justified above:
the `UiNavigationTests` fixture now earns the Skill Points that the (new, spec-required) disabled state needs, and the
smoke's cursor assertion was corrected to the invariant it meant to test, with a baseline build proving the failure
pre-dates this pass.

### Known flaky test (proved against baseline, not caused here)

`CombatAimCollisionProofTests.LiveRun_DirectHit_Assist_AutoReload_NoQuad_Doors_WallsAndDoorContainment` composes a
live dungeon from a **clock-derived random run seed**, so each execution tests different geometry. Run five times in
complete isolation with no other test in the process:

| Run | Seed / biome | Result |
|---:|---|---|
| 1 | 16689819 Rustworks | FAIL |
| 2 | 16714656 Rustworks | PASS |
| 3 | 16739825 OvergrownLabs | PASS |
| 4 | 16764699 Rustworks | FAIL |
| 5 | 16788708 OvergrownLabs | PASS |

The failing assertion also varies between runs ("a crosshair inside the hurtbox hits", "no snap, no hit"), which a
deterministic regression cannot do. This is a pre-existing test-design issue (an unseeded live run), not a progression
failure, and it is not used to excuse anything: the reported PlayMode result above is a genuine 759/759 run.

---

## 16. Build and smoke

| Gate | Result |
|---|---|
| **Windows x64 non-development build** | **NOT RUN** — this macOS machine has no Windows Standalone Support module. No platform module was installed (explicit permission was not given). This is a platform-verification limitation, not a repository-local gap. |
| Supported non-development standalone build | **PASS** — `ReleaseBuildTool.BuildMacBatch`, StandaloneOSX, `BuildOptions.None`, approved scene order: Succeeded, 0 errors, 2 pre-existing environment warnings (Unity Cloud project id / access token), 175.6 MB. `TestResults/build_report_macos.md`, `Builds/MacOS/RUINRAIL.app` |
| Headless smoke | **PASS** (exit 0) — `RUINRAIL -batchmode -nographics -smoke -seed 79 -savedir …`; **334 checks**, `ReturnToMenuOk: true`, 0 exceptions/errors |
| Windowed smoke | **PASS** (exit 0) — `RUINRAIL -smoke -seed 79 -screen-width 1280 -screen-height 720 -progressionproofdir … -screenshot …`; same checks, 6 progression captures written by the shipped executable |

Smoke stage counts (headless): progression **39**, playability 15, combat 12, loot 25, audio 24, inventory 35, HUD 24,
merchant 1, weapon cache 1, room/HUD 31, economy/containment 46, depth/settings/non-combat 65, starter fallback 7,
death screen 9.

The 39 progression checks, in order, cover: nothing purchasable on a fresh profile · no attribute above rank 0 · an
unaffordable purchase refused with nothing deducted · 24 Skill Points earned by levelling · fourteen individual
purchases each costing exactly one point (Vitality→5, Mobility→4, Power→5) · each attribute's next-rank preview ·
Resilience driven to the cap showing MAX · a purchase at the cap refused with nothing deducted · the maxed control
disabled · the panel stating the rank price · the panel showing every attribute's rank, description and effect lines ·
leaving and re-entering the station running no transaction · the saved profile reloading with every rank intact ·
the run's pipeline carrying the ranks · **Vitality 5 → 130 max HP (100 base + 20 vest + 10)** · the expedition
starting at **130/130** · the HUD reading 130 · healing capped at 130 · **Mobility 4 → 5.2 u/s** real move speed ·
**Power 5 → ×1.05**, the equipped P9 Ranger going from 12–14 to **13–15** damage · the maxed resistances reaching the
impact receiver · depth 2 still carrying the ranks · **the new depth filling to 130**.

---

## 17. Proof paths

```
TestResults/CharacterProgressionAttributeProof/
  attribute_effect_matrix.csv                          6 attributes × 12 columns
  attribute_rank_sweep.csv                             88 rows, 88 PASS, 0 FAIL
  runtime_attribute_evidence.txt                       38 measured lines, every attribute, all PASS
  prog_00_windowed_player.png                          the shipped windowed player
  prog_01_character_station_before_upgrade.png         all six attributes at 0 / 10, 24 points
  prog_02_vitality_rank_raised_and_preview_updated.png the purchase and the refreshed preview
  prog_03_max_rank_and_unaffordable_states.png         Vitality 5/10, Power 5/10, Mobility 4/10,
                                                       Resilience 10/10 MAX, 0 points, every control disabled
  prog_04_progression_retained_after_reentering.png    ranks retained after leaving/re-entering the station
  prog_05_run_start_full_hp_and_hud_with_vitality.png  HUD 130 / 130 at run start
  prog_06_next_depth_full_hp_with_vitality.png         depth 2 at the Vitality-modified maximum
TestResults/attribute_descriptions.md                  the attribute gate report
TestResults/build_report_macos.md                      the release build
TestResults/EditMode-results.xml / PlayMode-results.xml
TestResults/smoke-headless.log / smoke-windowed.log / smoke-baseline.log
```

---

## 18. Remaining limitations

1. **Windows x64 build and its smokes: NOT RUN.** No Windows Standalone Support module on this machine; none installed
   without permission. Substituted by a non-development StandaloneOSX build and both smokes on the shipped `.app`.
2. **Live UGS / Relay co-op: NOT RUN.** Network progression was verified on the deterministic local harness only.
3. **`WeaponSwitchSpeed` has no runtime consumer** (§12). Composed, capped and displayed correctly; nothing in the
   build consumes it because no weapon-switch duration exists to scale. Pinned by a test; needs a design decision.
4. **`CombatAimCollisionProofTests` is seed-flaky** (§15). Pre-existing, proved in isolation, unrelated to progression.
   Making that test seed-deterministic is a separate, out-of-scope change.
5. **Power rank 1 is invisible on a 20-damage roll** (`round(20 × 1.01) = 20`). That is the approved integer-damage
   rule; higher ranks and higher rolls move as designed. No balance was changed.
6. Running the EditMode suite regenerates `production/FINAL_MVP_COMPLETION_REPORT.md`; on macOS its build/smoke lines
   read NOT RUN for Windows. Unchanged behaviour from earlier passes.

---

**`CHARACTER_PROGRESSION_ATTRIBUTE_EFFECTS_COMPLETE`** — every repository-local attribute upgrades correctly, persists
correctly, changes its authoritative derived stat, has a verified measurable gameplay effect, and is represented
accurately in the Shelter's progression screen.
