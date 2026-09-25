# RUINRAIL — D1 AMMO / BLASTER FINE-TUNING REPORT

> Executed 2026-09-23 against the working tree, after the Stat Consumer Integrity and Fresh-Run Validation passes.
> Two balance values changed. Everything else is frozen and asserted frozen. No destructive git operation was used.
> Machine: macOS (Apple silicon), Unity 6000.3.24f1, the pinned toolchain in `ENVIRONMENT.md`.

## 1. Executive Summary

Two small, owner-directed corrections, both calibrated against deterministic measurement rather than chosen by feel:

| | Change | Measured effect |
|---|---|---|
| **D1 ammo** | Supply Chest Light-ammo quantity **20–40 → 26–46** | mixed P9 + Field Knife play reaches the boss with **+11 median / +10.8 mean** usable Light rounds |
| **Blasters** | Overheat lockout **2.2 s → 1.9 s**, cooling delay **0.6 s → 0.5 s** (all three) | practical sustained output **+7.9 % to +9.0 %**, share of cycle unable to fire **54–60 % → 50–57 %** |

Both land inside the brief: the ammo gain is in the owner's 10–15 round band, and the blaster gain is in the stated
5–10 % band. Neither change touches damage, fire rate, magazines, caps, boss HP, depth scaling or the Field Knife.

Pistol-only clearing still costs: a pistol-heavy run arrives at the boss with a median 48 of a 180 cap (27 %) and is
still forced onto the knife 2.8 times per depth. Mixing weapons remains the ammo-efficient play, and leaning on melee
still conserves more than mixing does — the ordering the starter kit is built around is intact.

## 2. Owner Playtest Constraints

The brief's design decisions were treated as authoritative over the previous pass's automated conclusions, per the
canonical priority in `CLAUDE_START_HERE.md` ("latest explicit user instruction/task" outranks the approved spec):

- D1 room clearing is broadly correct; the shortfall is only ~10–15 Light rounds in normal mixed play.
- The Field Knife is correctly tuned and the two-weapon loadout is intentional. **Not touched.**
- D1–D30 and post-D30 depth scaling are accepted. **Not touched.**
- Boss HP stays. **Not touched.**
- Ammo caps stay. **Not touched.**
- Blasters are useful and need only a mild buff, preferably on heat/cooling rather than raw damage.

The previous pass had modelled a much larger first-boss deficit. That model assumed pistol-only clearing, which is not
how the game is played — see §7, where the same harness reproduces both behaviours and the difference is 39 rounds.

## 3. Baseline Before

`TestResults/D1AmmoBlasterFineTune/baseline_before.csv` — every value this pass either changed or promised not to,
read from the working tree at the start of the pass (not from stale pre-stat-fix figures).

| Subject | Value before |
|---|---|
| P9 Ranger | 12–14 dmg, 4.0/s, mag 12, reload 1.2 s, range 10, Light, 1 round/shot, 26.7 sustained DPS |
| Field Knife | 14–17 dmg, 3.5/s, reach 1.2, arc 80°, wind-up 0.08 / recovery 0.12, 40.7 sustained DPS |
| Starter Light ammo | 60 |
| Ammo caps | Light 180, Medium 120, Heavy 60, Shells 40 |
| Supply Chest | 25 % of eligible ordinary combat rooms, minimum 2 per depth, ammo roll 100 % |
| Supply Chest Light quantity | **20–40**, pick weight 4 |
| Useful-ammo weighting | 70 % |
| D1 boss HP | 1 000–1 350 base, ×1.00 at D1 |
| Blasters (all three) | heat/shot 10 / 7 / 8, cooling 35 / 35 / 40, max heat 100, **cooling delay 0.6 s, lockout 2.2 s** |
| Blaster practical DPS | Arc 21.3, Pulse 22.1, Redline 24.1; 54–60 % of cycle unable to fire |

## 4. D1 Ammo Change

**Exact change — one field in one asset:**

```
Assets/Game/ScriptableObjects/Loot/LootTable_SupplyChest.asset
  Ammo roll → Ammo_Light entry
    MinQuantity: 20 → 26
    MaxQuantity: 40 → 46
```

Nothing else in the table moved: the Medium, Heavy and Shells entries, every weight, the 100 % ammo-roll chance, the
coin roll and the 20 % equipment roll are untouched. No new chest, no new source, no guaranteed pre-boss refill.

## 5. Why This Intervention Was Chosen

The brief's preferred order was investigated and the result is recorded rather than asserted:

| Candidate | Why not |
|---|---|
| **Useful-ammo weighting 70 % → 80 %** (Option A, "bias toward the equipped ammo type") | Measured worth only ~4 rounds. The Light entry already carries weight 4 of 11 and the starter has exactly one useful type, so the weighting is already doing nearly all it can. Too small to close the gap. |
| **Supply Chest minimum per depth 2 → 3** (Option A) | A whole extra chest is ~30 rounds — double the top of the owner's band, and it changes room contents rather than quantities. |
| **Starter ammo 60 → 72** (Option B) | Allowed only if the loot path cannot produce a clean correction. It can, so Option B was not needed; and a starter bump front-loads the ammo instead of paying it out as the player explores. |
| **Deterministic pre-boss floor** (Option C) | Explicitly the last resort, and the brief warns it must not feel like a secret refill. Unnecessary. |
| **Supply Chest Light quantity +6** ✔ | The smallest lever that lands in the band, on an existing visible source, with no change to chest count, placement or rules. |

The size was calibrated, not guessed. `D1AmmoBlasterFineTuneTests.CalibrateLightAmmoQuantity` (an `[Explicit]` test, so
it does not run in a normal suite but the result stays reproducible) swept the candidates over the same 30 seeds:

| Light quantity | Mixed median gain | Mixed mean gain | Pistol-heavy median at boss | Pistol-heavy forced melee kills |
|---|---:|---:|---:|---:|
| 20–40 (before) | — | — | 40.5 | 2.87 |
| 22–42 (+2) | +4.0 | +3.6 | 40.5 | 2.87 |
| 24–44 (+4) | +8.0 | +7.2 | 44.0 | 2.87 |
| 25–45 (+5) | +9.5 | +9.0 | 45.5 | 2.87 |
| **26–46 (+6)** ✔ | **+11.0** | **+10.8** | 48.0 | 2.83 |
| 28–48 (+8) | +14.0 | +14.4 | 51.5 | 2.77 |
| 30–50 (+10) | +18.0 | +18.0 | 54.5 | 2.73 |

`+6` and `+8` both sit inside 10–15. **`+6` was taken because the brief asks for the smallest clean intervention**, and
because it leaves pistol-heavy forced-melee behaviour essentially untouched (2.87 → 2.83 kills per depth) where `+10`
starts eroding it.

## 6. D1 Ammo Before / After

`TestResults/D1AmmoBlasterFineTune/d1_ammo_after.csv` — 180 rows: 3 usage profiles × 2 table versions × 30 seeds,
10 seeds per biome, reusing the fresh-run validation seed manifest so before and after are directly comparable.

The comparison runs **in one process on one code path**: the shipped table and an in-memory clone carrying the old
quantity are both driven through the same simulator over the same seeds, so the delta is the change and nothing else.

Usable Light rounds on arrival at the boss, normal accuracy:

| Usage profile | Melee share | Median before | Median after | Median gain | Mean before | Mean after | Mean gain |
|---|---:|---:|---:|---:|---:|---:|---:|
| **MIXED** | 0.50 | 75.5 | 86.5 | **+11.0** | 80.8 | 91.6 | **+10.8** |
| PISTOL_HEAVY | 0.00 | 36.5 | 48.0 | +11.5 | 41.3 | 51.9 | +10.6 |
| MELEE_HEAVY | 0.85 | 106.0 | 118.0 | +12.0 | 112.0 | 122.8 | +10.8 |

Light ammo found per depth: 100.9 → 111.7 (+10.8). Forced melee kills — enemies the firearm could not finish — are
unchanged in mixed play (0.9 → 0.9) and barely moved in pistol-heavy play (2.9 → 2.8).

The acceptance assertions in `Phase3_WritesTheD1AmmoBeforeAndAfter` fail the build if the mixed gain leaves the 8–18
window, if fewer than 30 seeds or 3 biomes are covered, if pistol-heavy play stops being forced onto the secondary, if
pistol-heavy arrives with more than half a cap, or if mixing stops being more efficient than pistol-only.

## 7. Pistol-Heavy vs Mixed-Weapon Results

This is the check that the correction did not dissolve the resource decision:

| | Reserve at boss (median) | Share of the 180 cap | Forced melee kills / depth | Voluntary melee kills / depth |
|---|---:|---:|---:|---:|
| PISTOL_HEAVY | 48.0 | 27 % | 2.8 | 0.0 |
| MIXED | 86.5 | 48 % | 0.9 | 17.5 |
| MELEE_HEAVY | 118.0 | 66 % | 0.5 | 30.0 |

(A depth holds roughly 39 enemies, so the mixed profile's 17.5 voluntary melee kills is the modelled 50 % share.)

Three things the numbers establish:

1. **Pistol-only clearing did not become resource-neutral.** It still ends the depth at just over a quarter of the
   cap and is still pushed onto the knife roughly three times per depth.
2. **Mixing is still the efficient play** — 38.5 rounds better than pistol-only at the boss.
3. **Melee is still worth leaning on** — 31.5 rounds better again than mixing. The extra ammo did not make the knife
   redundant.

It also explains the previous pass's much gloomier figure: that pass modelled pistol-only clearing, which lands at 48
rounds, while the owner plays mixed, which lands at 86.5. The 39-round gap is the two-weapon loadout working, not a
model error — and it is why the correction needed to be ~11 rounds rather than the ~60 the earlier report implied.

## 8. Blaster Change

**Exact change — two fields on three assets:**

```
ArcBlasterB4.asset, PulseCarbineB1.asset, Redline.asset
  _coolingDelaySeconds:     0.6 → 0.5
  _overheatLockoutSeconds:  2.2 → 1.9
```

Damage, fire rate, heat per shot, cooling rate, max heat, range and projectile speed are **unchanged on all three** —
asserted per weapon in `Phase5_WritesBlasterBeforeAndAfter`.

**Why these two fields and not cooling rate.** Cooling rate cannot improve sustained output at all:
`BlasterHeatState.AddShotHeat` resets the cooling delay on every shot, and every blaster's firing interval
(0.111–0.167 s) is far below even the shortened 0.5 s delay, so **no cooling happens while the trigger is held**,
whatever the rate. Raising cooling rate would have looked like a buff and changed nothing in a sustained fight. The
lockout is the only timing that gates the sustained cycle, and the delay is what gates recovery for a player who paces
their shots — so the pair covers both the brief's "heat recovery" and "overheat recovery feel" axes without touching
raw damage, which the brief names as the last resort.

The shortened delay cannot enable indefinite firing: sustaining fire would need an inter-shot gap above 0.5 s, i.e.
under 2 shots/s against authored rates of 6–9/s — a DPS sacrifice far larger than the heat it would avoid.

## 9. Blaster Before / After

`TestResults/D1AmmoBlasterFineTune/blaster_before_after.csv` — every blaster, before and after, with all the fields
the brief asked for including 30-second and 60-second output and both recovery timings.

| Blaster | Sustained DPS | Unable to fire | Cycle | 60 s output | Full-heat recovery | Half-heat recovery |
|---|---|---|---|---|---|---|
| Arc Blaster B4 | 21.34 → **23.13** (+8.4 %) | 56.9 % → 53.3 % | 3.87 → 3.57 s | 1 280 → 1 388 | 3.46 → 3.36 s | 2.03 → 1.93 s |
| Pulse Carbine B1 | 22.09 → **23.84** (+7.9 %) | 54.0 % → 50.3 % | 4.08 → 3.78 s | 1 325 → 1 431 | 3.46 → 3.36 s | 2.03 → 1.93 s |
| Redline ★ | 24.08 → **26.24** (+9.0 %) | 60.4 % → 56.8 % | 3.64 → 3.34 s | 1 445 → 1 574 | 3.10 → 3.00 s | 1.85 → 1.75 s |

Shots per heat cycle are unchanged (10 / 15 / 13) — heat still buys exactly the same number of shots; only the wait
afterwards is shorter.

**Acceptance, asserted in the test rather than claimed here:**

- The buff is between +3 % and +12 % on every blaster — noticeable, not a leap.
- Heat still costs **more than 40 %** of a sustained cycle on every blaster (actual: 50.3–56.8 %).
- No blaster reaches assault-rifle sustained output (Marauder A2 35.6): they land 26–35 % below it.
- Against the free starter pistol (26.7) the best blaster is still **1.8 % behind**; the other two are 11–13 % behind.
- No ammo-system interaction: blasters still consume no reserve.
- The Cooling Module and Heat Sink affixes still apply on top, so the accessory keeps its purpose.

## 10. Frozen Systems Verification

`TestResults/D1AmmoBlasterFineTune/frozen_values_check.csv` — **69 rows, 69 UNCHANGED, 0 CHANGED**, all asserted in
`Phase6_FrozenValuesDidNotChange` so drift fails the suite rather than a reader's attention:

- **Field Knife** — damage 14–17, attack rate 3.5, reach 1.2, arc 80°, wind-up 0.08 / recovery 0.12, knockback 0 /
  stagger 3. **Not altered.**
- **P9 Ranger** — 12–14, 4.0/s, mag 12, reload 1.2. Not altered.
- **Ammo caps** — 180 / 120 / 60 / 40. **Not altered.**
- **Starter kit** — 60 Light rounds. Not altered.
- **Depth scaling** — enemy HP and damage multipliers checked value-by-value at D1, D2, D3, D5, D10, D20, D30 **and
  the post-D30 tail at D50 and D100**. **Not altered.**
- **Boss base HP** — all six bosses, one row each (1 000 / 1 050 / 1 050 / 1 150 / 1 250 / 1 350). **Not altered.**
- **Every non-blaster weapon** — all 30 checked by a damage-and-cadence fingerprint against values recorded before the
  pass. Not altered.
- **Threat budgets** — D1, D5, D10, D20, D30. Not altered.

Beyond the explicit table, the full suites cover the rest of the brief's freeze list — loot rarity curves, XP, coins,
merchant and event prices, depth-heal rules, graphical inventory, HUD, movement/dash, aim assist, the previous pass's
knockback/stagger tuning, save/persistence, progression, non-combat rooms, audio, biome generation and multiplayer —
and all of it is green (§11).

**One deliberate exception, stated plainly:** `items/33_WEAPON_CATALOG.md` was updated, because the blaster change
contradicts the approved specification's "Cooling Delay = 0.6s. Overheat Lockout = 2.2s". Leaving the spec stale would
have made the repository lie about its own design. The spec now carries the new values plus a dated note recording
what changed, why, and that damage/fire rate/heat/cooling/max heat were not touched. `WeaponCatalogValidator`'s
approved constants were updated to match, which is what makes the production validators pass honestly rather than by
weakening them.

## 11. Automated Tests

| Gate | Result |
|---|---|
| EditMode | **PASS — 932 passed / 934 discovered** (2 not run: the pre-existing `MultiplayerTerminalTests.LiveSessionsRelay_IntegrationCheck_OrNotRun`, which needs live UGS, and the `[Explicit]` calibration sweep) |
| PlayMode | **PASS — 784 passed / 784 discovered** |
| Production validators, `FinalProductionValidator`, `ContentCountValidator`, `LootRoomGateValidator`, `WeaponCatalogAudit`, item-description validator, save/load validation, `StatConsumerIntegrityValidator` | **PASS** (they run as EditMode tests) |
| Combat / loot / ammo / blaster tests | **PASS** |
| macOS release build | **PASS** — 0 errors, 175.6 MB, `TestResults/build_report_macos.md` |
| Windows x64 build | **NOT RUN — module unavailable** |
| Built-player smoke | **PASS** — `TestResults/D1AmmoBlasterFineTune/smoke/smoke_result.json` |

Five tests pinned the old blaster timings and were updated to the new approved values, each as a one-constant edit
with no assertion weakened: `WeaponCatalogTests`, `BlasterHeatStateTests`, `ItemDescriptionTests`,
`LegendarySpecialsIIITests` and, indirectly, the validators that read `WeaponCatalogValidator`. One of them —
`BlasterHeatStateTests.NewState()` — kept its own 0.6 s / 2.2 s fixture timings on purpose, because those tests
exercise the heat state machine rather than the authored balance, and a comment now says so.

All simulations use the deterministic seed manifest from the fresh-run validation pass. Nothing in this pass is
clock-seeded.

## 12. Built-Player Evidence

`TestResults/D1AmmoBlasterFineTune/smoke/` — log, `smoke_result.json`, `dungeon.png`. Stage `done`, 9 rooms,
OvergrownLabs, save/reload verified.

The shipped player confirms both halves of the change in a real run:

- **`reserve increased after collection (Light 108 → 139, +31, cap 180)`** — a real Supply Chest paid out 31 Light
  rounds, inside the new 26–46 band and above the old 40 ceiling.
- The ammo-scarcity path is intact: `firearm reserve is 0 and the magazine empty`, `dry fire click at zero reserve`,
  `swap to the ammo-free secondary works with reserve 0`, `ammo-free secondary damages an enemy at zero firearm
  reserve (HP 30 → 12)`, `no ammo consumed by the melee swing`. The change did not remove the dry state; it delayed it.
- `starter secondary is the ammo-free Field Knife` — the starter kit is unchanged.

## 13. Human Playtest Checklist

`TestResults/D1AmmoBlasterFineTune/HUMAN_PLAYTEST_CHECKLIST.md` — the brief's ten questions, each carrying the
measured prediction so a session can confirm or contradict it, and each naming what would count as the change having
gone too far (question 7: if you can hold the trigger indefinitely on a blaster, that is a bug; question 10: if mixed
play now ends D1 comfortable, the correction overshot).

## 14. Known Pre-Existing Flakes

Reported, not hidden, and neither caused by this pass:

- **`CombatAimCollisionProofTests.LiveRun_DirectHit_…`** did not fail in this pass's PlayMode runs. It draws its run
  seed from the clock and is documented as failing about half of full runs on this machine, including on an untouched
  baseline.
- **Built-player smoke, `noncombat` stage** — runs 1 and 2 failed with `non-combat rooms driven on this depth:`
  (empty): the generated depth contained no non-combat room. Run 3 passed end to end and is the artefact above. This
  is the same clock-seeded generation sensitivity documented in the previous pass; the stage that failed is upstream
  of anything this pass touched.

## 15. Files Changed

**Balance data (the two changes)**
- `Assets/Game/ScriptableObjects/Loot/LootTable_SupplyChest.asset` — Light ammo quantity 20–40 → 26–46
- `Assets/Game/ScriptableObjects/Items/ArcBlasterB4.asset` — cooling delay 0.6 → 0.5, lockout 2.2 → 1.9
- `Assets/Game/ScriptableObjects/Items/PulseCarbineB1.asset` — same
- `Assets/Game/ScriptableObjects/Items/Redline.asset` — same

**Approved specification and its code-side guard**
- `items/33_WEAPON_CATALOG.md` — blaster Cooling Delay and Overheat Lockout, with a dated note explaining the change
- `Assets/Game/Scripts/Items/Validation/WeaponCatalogValidator.cs` — `BlasterCoolingDelay` and `BlasterOverheatLockout`

**Tests**
- Created: `Assets/Game/Tests/EditMode/D1AmmoBlasterFineTuneTests.cs` — Phases 1, 3, 5, 6, 8 plus the calibration sweep
- Extended: `Assets/Game/Tests/EditMode/FreshRunBalanceRunHarness.cs` — a deliberate melee-share usage dimension and a
  Supply Chest table override, both test-only, so before/after runs on one code path
- Updated to the new approved values: `WeaponCatalogTests.cs`, `BlasterHeatStateTests.cs`, `ItemDescriptionTests.cs`,
  `LegendarySpecialsIIITests.cs`

**Regenerated by existing tests, not hand-edited**
- `Assets/Game/Resources/GameContentCatalog.asset` (`VfxSprites` list), `production/FINAL_MVP_COMPLETION_REPORT.md`,
  `production/COMPLETION_ASSET_MANIFEST.md`, `production/VISUAL_SLICE_APPROVAL.md`
- `TestResults/FreshRunBalance/*.csv` — the previous pass's artefacts are rewritten by their own tests and now carry
  post-change blaster and ammo numbers. `production/FRESH_RUN_COMBAT_ECONOMY_BALANCE_REPORT.md` documents the
  **pre-change** state and should be read as the baseline this pass corrected, not as current figures.

**Artefacts**
- `production/D1_AMMO_BLASTER_FINE_TUNING_REPORT.md`
- `TestResults/D1AmmoBlasterFineTune/baseline_before.csv`
- `TestResults/D1AmmoBlasterFineTune/d1_ammo_after.csv`
- `TestResults/D1AmmoBlasterFineTune/blaster_before_after.csv`
- `TestResults/D1AmmoBlasterFineTune/frozen_values_check.csv`
- `TestResults/D1AmmoBlasterFineTune/HUMAN_PLAYTEST_CHECKLIST.md`
- `TestResults/D1AmmoBlasterFineTune/smoke/` and `TestResults/calib.log`

## 16. Remaining Open Questions

1. **The Supply Chest quantity is not depth-scoped.** The table is shared across every depth, so the +6 applies at D30
   as well as D1. That is consistent with the direction of the deeper-depth ammo pressure the previous pass measured,
   but if D1 is ever tuned independently of D30 the table will need a depth dimension it does not currently have.
2. **Blaster cooling rate is inert during sustained fire** (§8). If "cooling rate" is meant to be a meaningful stat
   while the trigger is held, the cooling-delay reset in `BlasterHeatState.AddShotHeat` is the thing to revisit — it
   also means the Cooling Module accessory only pays off between engagements.
3. The owner-observed shortfall was described for mixed play; the same +6 gives pistol-heavy play +11.5 rounds too.
   Whether pistol-only should have been left entirely untouched is a design question this pass could not settle from
   the loot path, since one quantity feeds both.

## 17. Final Status

All fifteen success conditions are met:

1. D1 ammo received a small correction consistent with the ~10–15 round shortfall — ✔ +11 median, +10.8 mean (§6)
2. Mixed P9 + Field Knife play remains the intended resource pattern — ✔ 38.5 rounds better than pistol-only (§7)
3. Pistol-only clearing still has a meaningful ammo cost — ✔ 27 % of cap at the boss, 2.8 forced melee kills (§7)
4. **Field Knife is unchanged** — ✔ asserted, six fields (§10)
5. **Boss HP is unchanged** — ✔ asserted, all six bosses (§10)
6. **Ammo caps are unchanged** — ✔ asserted, all four (§10)
7. **D1–D30 depth scaling is unchanged** — ✔ asserted at D1/2/3/5/10/20/30 (§10)
8. **Post-D30 scaling is unchanged** — ✔ asserted at D50 and D100 (§10)
9. Blasters received only a mild buff — ✔ +7.9 % to +9.0 %, no damage or fire-rate change (§9)
10. Heat remains a meaningful constraint — ✔ still 50.3–56.8 % of a sustained cycle, asserted above 40 % (§9)
11. No broad weapon rebalance occurred — ✔ all 30 non-blaster weapons asserted unchanged (§10)
12. Deterministic before/after evidence exists — ✔ 180 ammo rows and 6 blaster rows, one seed manifest (§6, §9)
13. Frozen-value verification passes — ✔ 69/69 UNCHANGED (§10)
14. Test/validator/build gates are truthful and documented — ✔ including the two flakes (§11, §14)
15. A focused human playtest checklist exists — ✔ (§13)

`D1_AMMO_BLASTER_FINE_TUNING_COMPLETE`
