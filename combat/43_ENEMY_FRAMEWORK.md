# Enemy Framework

> **Status:** Approved design specification unless explicitly marked as tunable.
> **Game language:** English. Planning discussions may be in German, but all player-facing text, code naming, comments, and Claude implementation specs should be English.
## Hierarchy

1. Normal enemies — nine reusable combat roles.
2. Elites — six hand-designed mini-bosses, two per biome.
3. Bosses — six full bosses, two per biome.

Elites are **not** normal enemies with random modifiers. Do not create random elite-affix systems for MVP.

## AI Structure

A straightforward finite-state model is sufficient: acquire target, move/position, telegraph, attack, recovery. Reuse modular attack behaviours instead of building nine unrelated AI codebases.

No Support Enemy exists. Normal enemies do not heal/buff other enemies.
