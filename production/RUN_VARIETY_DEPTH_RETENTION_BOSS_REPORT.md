# RUINRAIL — RUN VARIETY / DEPTH RETENTION / BOSS REPORT

> Executed 2026-09-24 against the working tree. Seven changes shipped; D1–D30 difficulty, post-D30 difficulty, boss HP,
> boss damage, weapon balance, the Field Knife, the D1 ammo tuning and the blaster tuning are all untouched and asserted
> so. No destructive git operation was used. Machine: macOS (Apple silicon), Unity 6000.3.24f1.

## 1. Executive Summary

| # | Objective | Status |
|---|---|---|
| 1 | Deepest depth recorded permanently | **implemented** — `PlayerProfile.DeepestDepthReached`, monotonic, written only on verified arrival |
| 2 | Visible in existing UI | **implemented** — Shelter survivor card, Main Menu profile card, both end screens, in-run Transit context |
| 3 | D31+ has a bounded reason to descend | **implemented** — coin and XP curve from Depth 30, ×1.22 at D40, ×1.59 at D100, capped at ×1.75 |
| 4 | D1–D30 combat scaling unchanged | **verified unchanged** — 78/78 frozen rows |
| 5 | Boss attack choice no longer list-order | **implemented** — seeded draw over every valid in-band attack; 0 of 24 authored attacks unreachable |
| 6 | Bosses not harmless outside all bands | **implemented** — `RepositionToEngagementRange`, all six close ~8 tiles in 3 s, 0 bounds violations, 0 damage |
| 7 | Elites visible often enough | **implemented** — chance 5/10/15/20/25 % → 8/18/25/25/25 % per slot; no-elite over 5 depths 65.8 % → 46.7 % |
| 8 | Some rooms depth-gated | **implemented** — 9 of 63 rooms to D5/D10/D20; 1500/1500 generations succeeded |
| 9 | Biomes measurably different | **implemented** — three hazard roles, per-biome archetype weighting; three distinct measured profiles |
| 10 | Accepted systems stable | **verified** — EditMode 946/948, PlayMode 788/788, build and smoke green |

Two things worth calling out beyond the brief. First, the review's boss findings were **both still true**: selection
really did return the first ready in-band attack, and no boss owned any re-engagement behaviour — a Foundry Titan at
1.6 tiles/s could never catch a player at 5. Second, `PriceService.ScaleCoinReward` and
`DepthScaling.CoinRewardMultiplier` were an authored depth curve with **no gameplay caller at all** — the same
dead-seam defect class the Stat Consumer Integrity pass eliminated for stats. Phase 4 is implemented *through* that seam,
so it now has a consumer.

## 2. Current-State Reverification

`TestResults/RunVarietyDepthRetention/current_state_before.csv` — 150+ rows captured before any edit. What the review
got right, and what was stale:

| Review claim | Verdict |
|---|---|
| Boss attack selection returns the first ready in-band attack | **confirmed** — `MovesetActorController.SelectAttack`, list order, `return attack` on first match |
| Bosses have a no-valid-attack zone and just chase | **confirmed** — max authored reach 9–14 tiles in a 36×24 arena; `FixedUpdate` walks at the authored 1.6–3.2 tiles/s |
| Elite frequency is very low | **confirmed** — a five-depth push had a 65.8 % chance of containing no elite; 6 authored variants rarely seen |
| All rooms available from Depth 1 | **confirmed** — all 63 authored rooms at `minDepth 1`, none with a max |
| The three hazards are numerically identical | **confirmed** — all three were 5–8 damage, 1.0 s tick, no delay, no stagger |
| Rarity stops improving around Depth 30 | **confirmed** — the rarity table's last band *is* D30; `WeightsAt` holds it flat forever |
| Reward growth flattens while risk rises | **confirmed and quantified** — see §6 |
| A deepest-depth field already exists | **false** — no such field existed; asserted by reflection in the before-state |
| `DepthScalingConfig.EliteChancePercent` drives elite frequency | **stale** — the generator uses `DungeonGraphRules`, not that value. Both are recorded; only the generator's was changed |

## 3. Owner Constraints / Frozen Decisions

Honoured in full, and each one asserted rather than promised (§17):

- **D1–D30 enemy and boss HP/damage scaling** — untouched.
- **D31+ difficulty scaling** — untouched. Measured only (§6); no literal bug was found, so nothing was altered.
- **No start-at-depth, no checkpoints, no paid skips, no "continue from deepest"** — not implemented. The record is a
  readout, and the Transit panel gained no button.
- **Boss HP and boss damage numbers** — untouched. Attack *selection* and out-of-band *movement* changed; bands,
  telegraphs, damage, cooldowns and phases did not.
- **Weapon balance, ammo caps, starter ammo, melee damage, blaster values, the D1 ammo change** — untouched.
- **No new rarity tier, no higher affix ceiling, no backpack flooding** — the reward curve moves coins and XP only.

## 4. Deepest Depth Persistence

`PlayerProfile.DeepestDepthReached` (int, defaults to 0). Written by exactly one method:

```csharp
ExpeditionService.RecordDepthArrival(int depth)   // monotonic; returns true only when the record rose
```

**The arrival seam.** It is called from `ExpeditionScene.BuildDepth()` at the `DepthsBuilt++` line — after the graph
generated, the layout validated, the rooms instantiated, the exits validated and the player was placed. Every earlier
return in that method is a failed arrival, so a descend that cannot build a dungeon never advances the record. Defeating
a boss does not advance it; requesting a descend does not advance it; only arriving does.

Properties, each asserted in `Phase2_DeepestDepthIsMonotonicAndCannotBeExploited`:

- monotonic — a lower or equal depth changes nothing, so replaying a transition or re-entering a room cannot increment it
- profile-persistent, and **not reset by death** (a lost run keeps the depth it reached)
- not reset by extraction or by starting a new expedition
- not lowered by a later shallow run, which also does not claim a new personal best
- deterministic — no RNG, no clock

## 5. Deepest Depth UI

| Surface | Treatment |
|---|---|
| **Shelter** survivor card | a `DEEPEST` stat row beside NAME/LEVEL/XP/POINTS — `DEPTH 17`, or `none yet`. Existing `StatRow` language, no new screen. |
| **Extraction summary** | `Deepest Depth: 17`, or `Deepest Depth: 17 — NEW PERSONAL BEST (was 12)` |
| **Run-lost screen** | a `DEEPEST DEPTH` row, emphasised only when beaten. The one piece of progress a wipe leaves intact. |
| **Main Menu** profile card | a `DEEPEST` key/value row. The card already had a natural slot, so the accepted layout was not restructured; it fits the 204×122 card with room to spare. |
| **Transit (in-run)** | factual context lines (§8). No button, no "start at best depth". |

`NEW PERSONAL BEST` appears only when `DeepestDepthReached > DeepestDepthBefore`, where "before" is captured when the
expedition starts — so a run that ends shallower than the record never claims it, and a run *lost* deeper than before
still does.

## 6. Post-D30 Reward Curve

`TestResults/RunVarietyDepthRetention/reward_curve_before.csv` — the measured problem:

| Depth | Enemy HP | Enemy damage | XP per depth | Coins per depth | Rarity score |
|---:|---:|---:|---:|---:|---:|
| 30 | ×2.90 | ×1.75 | 1 913 | **193** | 1.745 |
| 40 | ×3.35 | ×1.90 | 2 024 | **192** | 1.745 |
| 50 | ×3.80 | ×2.05 | 2 136 | **194** | 1.745 |
| 75 | ×4.65 | ×2.325 | **2 136** | **195** | 1.745 |
| 100 | ×5.50 | ×2.60 | **2 136** | **193** | 1.745 |

Risk nearly doubles between D30 and D100 (+90 % HP, +49 % damage) while **coins are dead flat at every depth in the
game**, rarity is frozen from D30, and XP stops growing at D50 — its only source of growth was the threat budget, which
caps there. Nothing about D31+ paid more than D30.

**What shipped.** One bounded curve in `EconomyConfig`, applied to coins and XP:

```
multiplier(depth) = min(1 + 7/100 × sqrt(depth − 30), 1.75)
```

| Depth | Multiplier |
|---:|---:|
| 1–30 | **×1.00 exactly** |
| 31 | ×1.07 |
| 40 | ×1.22 |
| 50 | ×1.31 |
| 75 | ×1.47 |
| 100 | ×1.59 |
| 145+ | ×1.75 (cap) |

**Where it is applied, and where it deliberately is not.** Coins scale inside `LootRoller.Roll`, so chests, the boss
cache and event rewards all inherit it — and merchant *sale proceeds* do not, because those flow through
`AddCarriedCoins`, which the curve does not touch. XP scales in `ExpeditionService.AddXp`, the single seam every XP
source passes through, so it cannot double-apply. Item affix power, the rarity table, ammo and prices are untouched.

The dead `ScaleCoinReward` / `CoinRewardMultiplier` seam is now the live implementation, and
`Phase7_TheCurveReachesRealRewardsThroughTheShippedSeams` proves it end to end: a real Depth 100 boss cache pays
materially more than the same table at Depth 30, a roller built without an economy still pays the authored amount, and a
real expedition driven to Depth 100 commits more XP for the same 100-XP kill than it does at Depth 30.

The approved spec now carries the rule: `dungeon/59_DEPTH_SCALING.md` → **Deep-Depth Reward Continuation**.

## 7. Reward Calibration

`TestResults/RunVarietyDepthRetention/post30_reward_calibration.csv` — three candidates over the same shipped formula
shape, judged against the risk they have to track:

| Candidate | D40 | D50 | D75 | D100 | Flat again from | Verdict |
|---|---:|---:|---:|---:|---|---|
| A linear 1 %/depth, cap 175 % | ×1.10 | ×1.20 | ×1.45 | ×1.70 | D105 | every step pays the same — no diminishing returns |
| **B sqrt 7 %/√depth, cap 175 %** | **×1.22** | **×1.31** | **×1.47** | **×1.59** | **D145** | **chosen** |
| C linear 2 %/depth, cap 200 % | ×1.20 | ×1.40 | ×1.90 | ×2.00 | **D80** | too steep, and reward-flat again from D80 — the exact problem this phase exists to fix |

**B was chosen** because it is the only candidate that satisfies all three requirements at once: it rises at *every*
depth past 30 (never flat where a player will be), each step is smaller than the last (asserted — every 10-depth step
pays less than the one before it), and its cap sits far outside plausible play so the flat tail is never met. Against
+90 % enemy HP from D30 to D100 it pays +59 % — it tracks risk without outrunning it.

## 8. Return vs Descend Information

The in-run Transit prompt gained factual context and nothing else (`TransitContext.Lines`):

```
Current depth: 7
Next depth: 8
Personal best: depth 12
Carried coins at risk: 430
```

plus a deep-depth line only when a bonus is actually in force, reporting the live multiplier here and at the next depth.
No "recommended", no prediction, no comparison of the two options. The full-heal-on-descend rule and the extraction loss
rules are untouched. The smoke asserts the absence of recommendation language in the shipped player.

## 9. Boss Attack Selection

**Before:** `SelectAttack` iterated the moveset in order and returned the first ready attack whose band contained the
target. An attack late in a moveset was unreachable whenever an earlier one was ready and in band — the Conductor's
14-tile burst cannon could not be selected while its 10-tile sweep was off cooldown.

**After:** every ready in-band attack becomes a candidate, and one is drawn from a deterministic per-actor stream seeded
from `RunSeed + Depth + RngStream.Encounter + room` by the composer. No `UnityEngine.Random`, no wall clock. The attack
chosen immediately before is skipped while any alternative exists, which removes the degenerate repeat without inventing
adaptive AI. Cooldowns, phases, bands, damage and telegraph timings are unchanged. The candidate list is a reused field,
so the AI loop allocates nothing per choice.

`TestResults/RunVarietyDepthRetention/boss_attack_distribution.csv` — 24 seeds × 60 distance samples per boss, cooldowns
cleared between samples so list order is the only thing that could bias the result:

| Boss | Selections | Attacks reached | Immediate repeats |
|---|---:|---|---:|
| Aegis Core | 1 152 | 4 of 4 (12.5–47.7 %) | 12.8 % |
| Scrap King | 960 | 4 of 4 (10.0–38.5 %) | 14.1 % |
| Subject Omega | 864 | 4 of 4 | 14.1 % |
| The Conductor | 1 344 | 4 of 4 | 10.9 % |
| The Foundry Titan | 960 | 4 of 4 | 22.9 % |
| Tunnel Maw | 960 | 4 of 4 | 15.6 % |

**Zero authored attacks are unreachable** (the test fails per attack if one is never selected). Repeats occur only when
the previous attack is the sole in-band candidate. `BossAttackChoiceIsDeterministicForASeedAndDiffersBetweenSeeds`
proves the same seed replays the same sequence and a different seed does not.

Elites use the same seeded selection, seeded the same way from `EliteEngagement`.

## 10. Boss Anti-Kite Fallback

The gap was real for all six bosses: the boss arena is 36×24 tiles and the longest authored attack band is 9–14 tiles,
so a player at 15+ tiles was outside everything — and the boss walked after them at 1.6–3.2 tiles/s against a player
moving at 5.

**`RepositionToEngagementRange`**: while no authored attack can reach the target, the actor closes at ×1.6 its authored
speed. It is non-damaging, uses the same obstacle steering and the same `EncounterBounds.ConstrainVelocity` as normal
pursuit, never teleports, and drops back to the authored speed the instant any band contains the target — from there
normal selection resumes. It is not a global speed increase: inside every band the boss moves exactly as before.

`TestResults/RunVarietyDepthRetention/boss_kite_matrix.csv`:

| Boss | Speed | Max reach | Start → after 3 s | Closed | Re-engaged | Bounds violations | Damage during approach |
|---|---:|---:|---|---:|---|---:|---:|
| Aegis Core | 2.6 | 12 | 20 → 11.9 | 8.07 | yes | 0 | 0 |
| Scrap King | 3.2 | 10 | 18 → 9.9 | 8.09 | yes | 0 | 0 |
| Subject Omega | 2.2 | 9 | 17 → 9.0 | 8.03 | yes | 0 | 0 |
| The Conductor | 2.0 | 14 | 22 → 13.9 | 8.06 | yes | 0 | 0 |
| The Foundry Titan | 1.6 | 10 | 18 → 10.3 | 7.73 | yes | 0 | 0 |
| Tunnel Maw | 2.8 | 10 | 18 → 9.9 | 8.06 | yes | 0 | 0 |

Five of six entered a valid band inside the 3-second window; the Foundry Titan — the slowest boss at 1.6 tiles/s —
reached 10.27 tiles against its 10-tile band, so it crossed a fraction of a second later. That is reported rather than
tuned away: the behaviour works, the Titan is simply slow by design.

## 11. Elite Frequency

Measured first, over **2 160 generated depths** (120 seeds × 6 depths × 3 biomes), before any change.

`elite_frequency_before.csv` — the review was **not** stale:

| Depths pushed | Chance of seeing no elite at all | Expected elites |
|---:|---:|---:|
| 1 | 95.0 % | 0.05 |
| 3 | 81.2 % | 0.20 |
| 5 | **65.8 %** | 0.40 |
| 10 | 29.2 % | 1.15 |

Two of three five-depth runs contained no elite at all, against six authored elite variants.

**The change, and only this change:** per-slot chance 5/10/15/20/25 % → **8/18/25/25/25 %**. Slot counts (1 up to D10,
2 from D11), elite HP, damage, rewards and the 25 % deep-band cap are untouched.

`elite_frequency_after.csv`:

| Depths pushed | No elite (before → after) | Expected elites (before → after) |
|---:|---|---|
| 1 | 95.0 % → 92.0 % | 0.05 → 0.08 |
| 3 | 81.2 % → 69.4 % | 0.20 → 0.34 |
| 5 | 65.8 % → **46.7 %** | 0.40 → 0.70 |
| 10 | 29.2 % → **11.1 %** | 1.15 → **1.95** |

A ten-depth push now almost always contains elites and averages two of them; Depth 1 stays the quietest band at 8 %, so
a first run does not become elite-heavy. The assertions test both horizons and a floor, so the rate cannot drift into
"every depth".

## 12. Depth-Gated Rooms

**9 of 63 authored rooms** (14 %), three per biome, chosen for identity that already reads as deeper content:

| Room | New minDepth | Why |
|---|---:|---|
| `*_combat_large_02` | **5** | the largest arena (32×20) and elite-capable; the other Large stays at D1 so the size class is never missing |
| `*_combat_medium_04` | **10** | elite-capable Medium; two elite-capable rooms remain at D1, a third arrives at D10 |
| `*_event_02` | **20** | event identity reads as later-run content; Event 01 remains available at every depth |

Nothing else moved. Start and Boss rooms stay available at every depth, and the four single-instance types (Loot,
Treasure, Merchant, Medical) are never gated — asserted, because gating one would remove the category from Depth 1.

`room_depth_gating.csv` — **1 500 generations (100 seeds × 3 biomes × 5 bands), 0 failures**, and the variety the gating
was for is visible:

| Depth | Distinct combat rooms used | Distinct rooms used |
|---:|---:|---:|
| 1 | 9 | 18 |
| 5 | 10 | 19 |
| 10 | 11 | 20 |
| 20 | 11 | 21 |
| 30 | 11 | 21 |

## 13. Biome Gameplay Identity

Derived from `dungeon/56_BIOMES.md`, not from stereotype. Two dimensions changed; the rest are recorded as already
differentiated or deliberately left alone.

**Hazard roles (C).** The three were numerically identical. They now have distinct textures at deliberately similar
damage per second (within 12 %, so no biome is accidentally harder):

| Hazard | Biome | Before | After | DPS | Authored basis |
|---|---|---|---|---:|---|
| Electrified Rail | Metro | 5–8 / 1.0 s | **3–5 / 0.6 s** | 6.67 | "electricity/rail machinery and cramped movement layouts" — constant chip |
| Acid Pool | Labs | 5–8 / 1.0 s | **6–9 / 1.0 s, 0.3 s delay** | 7.50 | "acid/organic danger zones" — a beat of grace, then it bites |
| Furnace Grate | Rustworks | 5–8 / 1.0 s | **9–13 / 1.6 s, stagger 4** | 6.88 | "presses, furnaces, explosive props, heavy mechanical" — rare, heavy, staggering |

**Encounter weighting (A/E).** Archetype selection was a flat pool for all three biomes. It is now weighted (default 10,
never zero — no archetype is excluded anywhere), using exactly one RNG draw per role as before:

| Biome | Raised | Lowered |
|---|---|---|
| Metro | charger 20, swarm 20, brute 15 | sniper 5 |
| Rustworks | brute 20, shield 20, bomber 15 | swarm 5 |
| Labs | swarm 20, summoner 20, bomber 15 | shield 5 |

`biome_identity_matrix.csv`, measured over 600 generated depths:

| Biome | Top archetypes | Ranged share | Hazard role |
|---|---|---:|---|
| Metro | swarm 31.5 %, grunt 15.8 %, shooter 15.7 %, charger 12.3 % | 46.8 % | constant chip |
| Rustworks | shooter 19.7 %, grunt 19.0 %, swarm 15.5 %, brute 10.9 % | **55.3 %** | heavy burst + stagger |
| Labs | swarm 31.9 %, shooter 17.1 %, grunt 15.9 %, bomber 8.5 % | 48.9 % | delayed organic |

The three measured profiles and summaries are distinct, and the validator fails if two biomes ever share a weighting.
**Honest limitation:** Metro and Labs both still lead with swarm; they separate on charger-vs-bomber, on ranged share and
on hazard behaviour rather than on their most common enemy. Sharpening that further would mean changing threat costs or
adding archetypes, which is outside this pass.

**Elite weighting (B) — measured, unchanged.** Elites are *already* biome-tagged two per biome and `PickElite` filters
by biome, so the dimension the brief asked about was working. Recorded in the matrix, not touched.

**Room weighting (D) — deliberately unchanged.** Depth gating (§12) already changes the spatial rhythm per depth, and
altering per-biome selection weights on top risks the content-count contract for no measured gain. The brief says to
leave a dimension alone when the docs do not support a safe distinction; `56_BIOMES.md` says "biomes are content/visual
sets, not separate game architectures", which does not support per-biome room-weight divergence.

## 14. Validator / Contract Changes

New: `RunVarietyDepthRetentionValidator` (`TestResults/run_variety_depth_retention.md`) — **PASS, 25 rules, 0 problems**.
It enforces the new contracts rather than counting content:

1. every biome still has exactly 21 authored rooms
2. no room category is missing at D1/D5/D10/D20/D30, at least 8 distinct combat rooms remain, and at least one
   elite-capable combat room exists at every band
3. the reward curve is **exactly** 1.0 for every depth 1–30, and past 30 it is monotonic, rises at least once, and never
   exceeds a hard cap — checked depth by depth to D500
4. no archetype has weight 0 in any biome, no biome sits entirely at the default, and no two biomes share a weighting
5. elite chance stays inside the declared 5–25 % band at every depth, and Depth 1 is never the heaviest band

`Phase11_ValidatorPassesOnTheShippedContentAndFailsOnBrokenFixtures` feeds it three deliberately broken fixtures and
requires failure each time: the only Merchant room gated out of Depth 1, a reward curve whose start depth is 1 (so it
touches D1–D30), and a curve whose percentage is 0 (so deep play stays flat).

Existing gates updated to the new approved contract rather than weakened:

- `EconomyTests` — now pins flat-through-D30 plus the bounded curve at D40/D50/D100/cap
- `DungeonGraphGeneratorTests` — the new elite band
- `RunFailedScreenTests` — the new `DEEPEST DEPTH` row
- **19 boss/elite assertions across 8 PlayMode files** — these pinned *list-order* selection ("at 3 tiles the answer is
  Combat Roll"), which is the defect this pass removed. They now assert the contract that survived: the selected attack
  is ready and its authored band really contains that distance, and the named attack is among the candidates there. One
  case where only a single attack can reach uses `OnlySelectableAt`, which still demands the exact answer. A new shared
  `BossSelectionAssert` holds the helpers and explains why re-pinning the old behaviour would re-freeze the bug.
- `DungeonEventsITests` — the cursed-chest contract was asserting a realised threat total for one seed, which the new
  role mix made seed-fragile. It now asserts the guaranteed part exactly (the scaled target), that a cursed encounter is
  never *easier*, and that it is materially harder on at least 14 of 20 seeds.

## 15. Save / Migration / Exploit Safety

- **Old saves.** A new `int` field on a `[Serializable]` profile: `JsonUtility` leaves it at 0, so no migration step and
  no new save version were needed. `Phase12_OldSavesMigrateWithoutInventingAPersonalBest` builds a document with the
  field stripped out, loads it, and asserts the record is 0 while name, XP, coins and the save version are untouched. No
  historical best is fabricated.
- **The record cannot decrease** — asserted for a shallower arrival and for a later shallow run.
- **Replay cannot increment** — `RecordDepthArrival` is monotonic; calling it again with the same depth returns false.
- **A failed descend awards nothing** — the write sits after generation, validation, instantiation and exit validation.
- **No duplicate reward commit.** XP scales inside `AddXp`, the single seam; coins scale inside `LootRoller.Roll`, once
  per rolled entry. The boss cache is still opened once by its existing gate.
- **The bonus does not reach merchant sells** — those go through `AddCarriedCoins`, which the curve does not touch. This
  was a deliberate seam choice and is documented at the call site.
- **The bonus does not mint ammo and does not touch affix power** — it multiplies coin quantities and XP amounts only.
- **Host/client transaction ids, the death transaction and the extraction transaction** — untouched; the full suites and
  the smoke's death and leave stages are green.
- The save system was not refactored.

## 16. Determinism / Performance

- **Boss selection** allocates nothing per choice (a reused candidate list) and runs only when the FSM needs an attack.
  Its stream is created on first use, so an actor that never reaches a choice allocates nothing at all.
- **Biome weighting** is generation-time and uses exactly one RNG draw per role — the same count as the flat pick it
  replaced, so the stream advances identically.
- **Reward scaling** is deterministic integer/float math with no mutable global state.
- **Deepest depth** writes to the profile at an existing safe point; the save itself is unchanged.
- `Phase13_GenerationAndEncounterCompositionAreReproducible` asserts that the same seed and depth produce the same room
  graph signature and the same encounter signature, for all three biomes at D1/D10/D30. The boss determinism test
  (§9) covers the selection sequence.

## 17. Frozen-System Verification

`TestResults/RunVarietyDepthRetention/frozen_systems_check.csv` — **78 rows, 78 UNCHANGED, 0 CHANGED**, asserted so drift
fails the suite:

- enemy HP and damage multipliers at D1, D2, D3, D5, D10, D20, D30 **and the post-D30 tail at D40, D50, D100**
- all six boss base HP values, every boss attack's damage band and telegraph timing
- Field Knife damage / rate / reach / arc
- blaster cooling delay and overheat lockout (0.5 / 1.9 — the previous pass's tuning)
- Supply Chest Light ammo 26–46 (the previous pass's D1 tuning)
- ammo caps 180/120/60/40, starter kit 60 Light
- threat budgets at D1/D10/D30, elite slot counts, 21 rooms per biome
- every non-blaster weapon's damage-and-cadence signature

Beyond the table, the frozen list in the brief — graphical inventory, Dungeon Merchant, Main Menu layout, HUD,
EncounterBounds, death and extraction transactions, backpack reorder, non-combat interactions, settings, audio, art,
VFX, character visuals, multiplayer — is covered by the full suites and the built-player smoke, all green (§18).

The two deliberate spec updates: `dungeon/59_DEPTH_SCALING.md` gained the Deep-Depth Reward Continuation section and the
new elite band. Leaving the spec stale would have made the repository disagree with itself.

## 18. Automated Tests

| Gate | Result |
|---|---|
| EditMode | **PASS — 946 passed / 948 discovered** (2 not run: the pre-existing `MultiplayerTerminalTests.LiveSessionsRelay_IntegrationCheck_OrNotRun`, which needs live UGS, and the `[Explicit]` ammo calibration sweep from the previous pass) |
| PlayMode | **PASS — 788 passed / 788 discovered** |
| `RunVarietyDepthRetentionValidator` | **PASS — 25 rules, 0 problems** |
| `FinalProductionValidator`, `ContentCountValidator`, `StatConsumerIntegrityValidator`, item/content validators, save/load validation, generation validators, combat/boss tests, economy tests, room/non-combat tests | **PASS** (they run as EditMode tests) |
| macOS release build | **PASS** — 0 errors, 175.7 MB, `TestResults/build_report_macos.md` |
| Windows x64 build | **NOT RUN — module unavailable** |
| Built-player smoke | **PASS** — `TestResults/RunVarietyDepthRetention/smoke/smoke_result.json` |

New tests this pass: 11 EditMode (`RunVarietyBaselineTests`, `RunVarietyValidationTests`) and 4 PlayMode
(`RunVarietyBossTests`), plus a 12-check built-player stage.

## 19. Built-Player Evidence

`TestResults/RunVarietyDepthRetention/smoke/` — log, `smoke_result.json`, `dungeon.png`. Stage `done`, Rustworks,
personal best reached and persisted at **depth 3**. All 12 run-variety checks passed in the shipped player:

1. the record equals the deepest depth actually arrived at (depth 2 at depth 2)
2. the record cannot be lowered by replaying an arrival
3. `boss_scrap_king` reached **4 of 4** authored attacks
4. no attack is valid outside every authored band
5. the boss actively re-engaged from outside every band (**15.8 → 10.0 tiles**)
6. defeating the boss did **not** record the next depth
7. the transit context lists 4 factual lines including the personal best
8. the transit context contains no recommendation language
9. arriving at depth 3 raised the record from 2 to 3
10. the expedition reports a new personal best
11. the next depth composed 10 rooms with depth gating active
12. the reloaded save carries the record (depth 3)

Covering the brief's twelve items: fresh profile ✔, enter D1 ✔, record displays ✔ (Shelter/Main Menu/end screens are
covered by the EditMode and PlayMode UI suites; the smoke covers the value and the persistence), descend ✔, increments
only after arrival ✔ (items 6 and 9), return/save/reload ✔, persists ✔, more than one valid attack ✔, actively
repositions ✔, gating does not break generation ✔, biome composition evidence ✔ (`biome_identity_matrix.csv` over 600
depths), and no regression in the D1 ammo / Field Knife / blaster tuning ✔ (§17 plus the smoke's own loot checks).

Post-D30 reward logic is exercised by a real runtime path, not a copied formula:
`Phase7_TheCurveReachesRealRewardsThroughTheShippedSeams` drives a real `ExpeditionService` to Depth 100 and a real
`LootRoller` over the real boss-cache table.

## 20. Human Playtest Checklist

`TestResults/RunVarietyDepthRetention/HUMAN_PLAYTEST_CHECKLIST.md` — 18 diagnostics, each carrying the measured
prediction and, where relevant, what would count as the change having gone too far (question 11: if the re-engagement
feels like the boss out-runs you unfairly, the multiplier is the lever; question 18: if D100 feels like a jackpot, the
cap is).

## 21. Known Pre-Existing Flakes

Reported, not hidden, and neither caused by this pass:

- **`CombatAimCollisionProofTests.LiveRun_DirectHit_…`** failed once ("a crosshair inside the hurtbox hits without any
  assist: expected less than 56, was 56") and passed on rerun. It draws its run seed from the clock and is documented as
  failing about half of full runs on this machine, including on an untouched baseline.
- **Built-player smoke** needed 5 attempts: three failed at the pre-existing seed-dependent `noncombat` stage (a
  generated depth with no non-combat room), one at `econ/contain/proj` (the containment tolerance of 0.06 tiles), one at
  `roomhud`. All are upstream of this pass's stage and were documented in the two previous passes.
- One **new** fragility of my own, found and fixed rather than left: the smoke's re-engagement check measured while the
  boss could still be mid-telegraph from the attack sampling above it, which produced a "15 → 15 tiles" false failure.
  It now waits for the actor to return to `Chase` before measuring, because the gap-close only drives the body there.

## 22. Files Changed

**Persistence and expedition**
- `Assets/Game/Scripts/Expedition/PlayerProfile.cs` — `DeepestDepthReached`
- `Assets/Game/Scripts/Expedition/ExpeditionService.cs` — `RecordDepthArrival`, `DeepestDepthAtExpeditionStart`,
  `IsNewPersonalBestThisExpedition`, `RewardMultipliersAt`, the XP depth curve in `AddXp`, summary record fields
- `Assets/Game/Scripts/Expedition/TransitContext.cs` *(new)* — the factual Transit context
- `Assets/Game/Scripts/App/ExpeditionScene.cs` — the arrival seam and the Transit context render

**Reward curve**
- `Assets/Game/Scripts/Economy/EconomyConfig.cs` — the bounded deep-depth continuation
- `Assets/Game/Scripts/Loot/LootRoller.cs` — the coin depth curve (reward coins only)
- `Assets/Game/Scripts/Loot/LootSourceCatalog.cs`, `Assets/Game/Scripts/Events/EventRewards.cs`,
  `Assets/Game/Scripts/Dungeon/Runtime/RoomCategoryComposer.cs` — thread the economy to both loot paths
- `Assets/Game/Scripts/UI/Base/BaseSession.cs` — supplies the economy to the expedition service

**Boss / elite behaviour**
- `Assets/Game/Scripts/Enemies/Attacks/MovesetActorController.cs` — seeded selection over every valid attack,
  `RepositionToEngagementRange`, and two diagnostics seams
- `Assets/Game/Scripts/Dungeon/Runtime/RoomCategoryComposer.cs`,
  `Assets/Game/Scripts/Dungeon/Runtime/EliteEngagement.cs`,
  `Assets/Game/Scripts/Dungeon/Runtime/DungeonRoomRuntimeComposer.cs` — seed the selection streams

**UI**
- `Assets/Game/Scripts/App/BaseHubScreen.cs` — Shelter `DEEPEST` row
- `Assets/Game/Scripts/App/MainMenuScreen.cs`, `Assets/Game/Scripts/App/GameApp.cs` — Main Menu row + `SaveProbe` field
- `Assets/Game/Scripts/UI/Base/BaseHubViewModel.cs`, `Assets/Game/Scripts/UI/RunEnd/RunFailedViewModel.cs` — end screens

**Data**
- `DungeonGraphRules.cs` — elite chance 8/18/25/25/25 %
- 9 room assets — `_minDepth` 5 / 10 / 20 (`*_combat_large_02`, `*_combat_medium_04`, `*_event_02` in all three biomes)
- 3 hazard assets — distinct damage, tick, delay and stagger
- `EncounterDirector.cs` — `BiomeEncounterWeights` and the weighted role pick

**Approved specification**
- `dungeon/59_DEPTH_SCALING.md` — Deep-Depth Reward Continuation; the new elite band

**Editor tooling**
- `Assets/Game/Scripts/Editor/Production/RunVarietyDepthRetentionValidator.cs` *(new)*

**Smoke**
- `Assets/Game/Scripts/App/SmokeRunner.DeepestDepth.cs` *(new)*, `SmokeRunner.cs`

**Tests** — new: `RunVarietyBaselineTests.cs`, `RunVarietyValidationTests.cs` (EditMode), `RunVarietyBossTests.cs`,
`BossSelectionAssert.cs` (PlayMode). Updated to the new contracts: `EconomyTests`, `DungeonGraphGeneratorTests`,
`DungeonEventsITests`, `RunFailedScreenTests`, and 8 boss/elite PlayMode files.

**Regenerated by existing tests, not hand-edited** — `Assets/Game/Resources/GameContentCatalog.asset` (`VfxSprites`),
`production/FINAL_MVP_COMPLETION_REPORT.md`, `production/COMPLETION_ASSET_MANIFEST.md`,
`production/VISUAL_SLICE_APPROVAL.md`, and the previous passes' `TestResults/FreshRunBalance/*.csv` (their tests rerun
with current data; the reward-curve rows there now include the new post-D30 behaviour).

## 23. Deferred Design Decisions

1. **Start-at-depth / checkpoints / paid skips** — explicitly not implemented, per the brief. The record exists now, so
   the data a future decision would need is being collected.
2. **Post-D30 difficulty curve** — measured only (§6). Enemy HP reaches ×5.5 and damage ×2.6 by D100 while the reward
   curve pays ×1.59. Whether that is the right ratio is an owner decision; no scaling value was changed.
3. **Metro vs Labs archetype overlap** — both still lead with swarm (§13). Sharpening it means touching threat costs or
   the roster, which is outside this pass.
4. **Per-biome room selection weights** — left unchanged deliberately (§13), because `56_BIOMES.md` does not support it.
5. **Rarity past Depth 30** — the table still has no band past D30 and this pass did not add one (no new rarity tier was
   permitted). Deep play now pays in coins and XP instead. If rarity should also continue, that is a separate decision.
6. **The Foundry Titan's re-engagement time** — at 1.6 tiles/s it needs slightly longer than 3 seconds to cross from
   +8 tiles into its band. Raising `ReengageSpeedMultiplier` or its move speed would fix it; both are balance levers
   this pass deliberately did not pull.

## 24. Final Status

All twenty-four success conditions are met:

1. Findings reverified before edits — ✔ (§2) 2. Deepest depth persisted safely and monotonically — ✔ (§4)
3. Existing saves migrate safely — ✔ (§15) 4. Presented in Shelter and run summaries — ✔ (§5)
5. Start-at-depth NOT implemented — ✔ (§3) 6. D1–D30 difficulty unchanged — ✔ (§17)
7. D31+ difficulty unchanged — ✔ (§3, §17) 8. Post-D30 reward measured — ✔ (§6)
9. Conservative bounded continuation implemented and calibrated — ✔ (§6, §7)
10. Boss choice deterministic but not list-order — ✔ (§9) 11. All six bosses have distribution evidence — ✔ (§9)
12. Fair no-valid-attack re-engagement — ✔ (§10) 13. EncounterBounds and collision intact — ✔ (§10, 0 violations)
14. Elite frequency measured before any change — ✔ (§11) 15. The change is conservative and evidenced — ✔ (§11)
16. A small room subset gated with no generation failures — ✔ (§12, 1500/1500)
17. All three biomes measurably different — ✔ (§13) 18. Validators enforce the new contracts honestly — ✔ (§14)
19. Frozen systems pass unchanged-value checks — ✔ (§17, 78/78) 20. Gates reported truthfully — ✔ (§18, §21)
21. Available-platform build succeeds — ✔ (§18) 22. Built-player/runtime proof exists — ✔ (§19)
23. Human playtest checklist exists — ✔ (§20) 24. No unrelated redesign occurred — ✔ (§22)

`RUN_VARIETY_DEPTH_RETENTION_BOSS_COMPLETE`
