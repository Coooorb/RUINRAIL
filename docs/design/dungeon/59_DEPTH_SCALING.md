# Depth Scaling

> **Status:** Approved design specification unless explicitly marked as tunable.
> **Game language:** English. Planning discussions may be in German, but all player-facing text, code naming, comments, and Claude implementation specs should be English.
All values below are approved V1 baseline targets and must be centralized/configurable. Do not silently rebalance them during implementation.

## Enemy HP Target Multipliers

| Depth | HP |
|---:|---:|
| 1 | 100% |
| 2 | 108% |
| 3 | 116% |
| 5 | 132% |
| 10 | 170% |
| 20 | 235% |
| 30 | 290% |
| 50 | 380% |
| 100 | 550% |

Use a smooth curve/formula approximating these targets rather than a hardcoded table if practical.

## Enemy Damage Target Multipliers

| Depth | Damage |
|---:|---:|
| 1 | 100% |
| 5 | 115% |
| 10 | 130% |
| 20 | 155% |
| 30 | 175% |
| 50 | 205% |
| 100 | 260% |

Damage grows slower than HP.

## Speed Caps
Attack frequency and movement speed scale only slightly. Initial attack-frequency target reaches only about +10% by very deep play and then caps. Do not create hyper-fast unreadable enemies.

## Solo Threat Budget Targets

| Depth | Base Threat Budget |
|---:|---:|
| 1 | 4–6 |
| 3 | 5–7 |
| 5 | 6–8 |
| 10 | 8–10 |
| 20 | 10–13 |
| 30 | 12–15 |
| 50+ | 14–18 |

Threat budgets cap rather than growing forever.

## Elite Chance per Dungeon

Per elite slot (1 slot up to Depth 10, 2 from Depth 11):

- Depth 1–2: ~8%.
- 3–5: ~18%.
- 6–10: ~25%.
- 11–20: ~25%.
- 21+: ~25% cap.

> Raised from 5/10/15/20/25 by the run-variety pass (2026-09-24). At the old rates a measured five-depth expedition had
> a 65.8% chance of containing no elite at all, so the six authored elite variants were rarely seen. Depth 1–2 stays the
> lowest band so a first run is not elite-heavy, and the 25% per-slot cap is unchanged.

## Loot Rarity V1 Baseline Targets

| Depth | Common | Uncommon | Rare | Epic | Legendary |
|---:|---:|---:|---:|---:|---:|
| 1 | 60% | 31% | 8% | 0.9% | 0.1% |
| 5 | 45% | 36% | 16% | 2.7% | 0.3% |
| 10 | 30% | 38% | 25% | 6.4% | 0.6% |
| 20 | 18% | 32% | 34% | 15% | 1% |
| 30+ | 12% | 26% | 39% | 21.5% | 1.5% |

High-quality sources (boss/treasure/elite) use improved tables. Legendary remains rare even in deep play.

## Endless Rule
After roughly Depth 30 no new mandatory gameplay systems are introduced. Content can stop growing while difficulty continues through slow stat curves, capped threat complexity, hard encounter templates, capped elite frequency, and better-but-capped loot quality.

## Deep-Depth Reward Continuation (from Depth 30)

The loot rarity table above has no band past Depth 30, and the threat budget caps at Depth 50, so without a reward
curve every reward axis goes flat while enemy HP keeps climbing to x5.5 and damage to x2.6 by Depth 100. Coins and XP
therefore continue on one bounded curve, authored in `EconomyConfig`:

    multiplier(depth) = min(1 + percentPerRootDepth/100 x sqrt(depth - startDepth), cap)

with `startDepth = 30`, `percentPerRootDepth = 7` and `cap = 175%` for both coins and XP.

| Depth | Multiplier |
|---:|---:|
| 1–30 | x1.00 (exactly unchanged) |
| 40 | x1.22 |
| 50 | x1.31 |
| 75 | x1.47 |
| 100 | x1.59 |
| 145+ | x1.75 (cap) |

Rules this curve obeys, and a change to it must keep:

- Depth 1 through Depth 30 is **exactly** x1.00 — the accepted early-game economy is untouched.
- It rises at every depth past the start, so deeper is never reward-flat.
- Each step is smaller than the last (square root), so the curve cannot run away.
- It stops at an authored cap, so Depth 100+ cannot inflate the economy.
- It applies to **reward** coins (chests, boss cache, events) and to XP. It does **not** apply to merchant sale
  proceeds, item affix power, ammo, or the rarity table — no new rarity tier and no higher power ceiling.
- Rarity remains capped by the Depth 30+ band above; deeper play buys more coins and XP, not better item tiers.
