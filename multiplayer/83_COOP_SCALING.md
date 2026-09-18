# Co-op Scaling

> **Status:** Approved design specification unless explicitly marked as tunable.
> **Game language:** English. Planning discussions may be in German, but all player-facing text, code naming, comments, and Claude implementation specs should be English.
Party size changes encounter pressure without increasing incoming damage per hit.

Initial target multipliers:

| Party | Threat Budget | Normal Enemy HP | Boss HP | Enemy Damage |
|---|---:|---:|---:|---:|
| Solo | 100% | 100% | 100% | 100% |
| Duo | 140% | 120% | 165% | 100% |
| Trio | 175% | 135% | 220% | 100% |

Prefer more enemies/role combinations plus moderate HP increases instead of simply tripling every enemy's health.

Scaling is fixed based on the expedition's starting party size. If a player dies/disconnects later, do not dynamically reduce the existing run's difficulty.
