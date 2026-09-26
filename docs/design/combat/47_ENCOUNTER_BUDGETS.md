# Encounter Budgets and Composition

> **Status:** Approved design specification unless explicitly marked as tunable.
> **Game language:** English. Planning discussions may be in German, but all player-facing text, code naming, comments, and Claude implementation specs should be English.
## Threat Costs

Approved V1 threat weights:

| Enemy | Threat |
|---|---:|
| Swarm | 0.5 |
| Grunt | 1 |
| Shooter | 1 |
| Charger | 2 |
| Bomber | 2 |
| Shield | 2 |
| Sniper | 2.5 |
| Brute | 3 |
| Summoner | 3 |

Rooms receive a threat budget based on depth and team size. Use curated/validated encounter templates alongside the budget so random generation does not create nonsensical combinations.

## Active Enemy Caps
Approved V1 active caps:
- Solo: 10 normal active enemies.
- Duo: 14.
- Trio: 18.

Summoned units must obey sensible active caps as well.

## Complexity
Normal combat rooms should often mix 2–4 roles rather than repeating a single enemy type. Difficulty grows through combinations as well as stats.
