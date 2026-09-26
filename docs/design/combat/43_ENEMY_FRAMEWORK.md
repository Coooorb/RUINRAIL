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

## Encounter Containment (implementation note 2026-09-19)

An encounter actor's collider may not cross the legal encounter-room boundary into a different room. Every actor a room spawns (encounter, reinforcements, summons, event waves), its Elite and its Boss is bound to that room's interior (the room minus the wall ring, which is also where the door cells and the combat door blockers are). Movement, dash/charge endpoints and knockback steps are constrained to that interior before they are committed, so the legal edge behaves like a wall the AI cannot path through: pursuit slides along it and holds there, a doorway is never a route even while the door is open or a lock is still pending on a player in it, and no open door of an event room is an exit. The pull-back clamp is a sub-step safety net for physics pushes, never the mechanism. Bosses additionally do not acquire a target before their arena is entered (BossEngagement hands it over on entry), so a player elsewhere on the depth never draws a Boss out of its arena. Closed combat doors remain physical blockers exactly as before.
