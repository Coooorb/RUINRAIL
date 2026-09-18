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
- Depth 1–2: ~5%.
- 3–5: ~10%.
- 6–10: ~15%.
- 11–20: ~20%.
- 21+: ~25% cap.

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
