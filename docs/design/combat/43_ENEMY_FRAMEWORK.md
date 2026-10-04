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

**Telegraph truth (2026-10-03).** A telegraph shows exactly the attack's damaging footprint and timing: the attack resolver and the danger marker both take their geometry from `AttackFootprint` (strike circles and zones, the dash lane up to the wall that stops it, every volley lane from muzzle to range up to the first wall), and the marker's countdown is the committed telegraph (boss phase-two and attack-speed scaling, a moveset enemy's own move timing). A marker may be drawn larger than the footprint only by whole pixels at its edge (and shot lanes at a readable minimum width), never smaller. Danger that continues after the telegraph (later hit windows, a running dash, remaining volleys, a bomb in flight) stays marked until it is over. Co-op clients receive the committed telegraph length, the locked direction, the moveset slot and a lob's landing point, so they draw the host's danger.

**Telegraph fairness contract (2026-10-03).** If a telegraphed attack damages a player, the player's drawn body overlaps the red on that frame. Enemy damage reaches a player only through the player's exclusive hurtbox (`CombatHurtbox.AttachPlayer`: two boxes measured to lie inside the drawn body in every live frame of every facing; nothing is drawn below the feet, so nothing there can be hit), never through the larger feet-level movement collider, and a projectile in flight is never its shooter's body (`DamageTargets.Resolve`, shared by the attack resolver, area damage and projectiles). A dash damages only inside the lane it was warned with, however its body is shoved. Shot lanes stay drawn while their shots can still arrive (lane length at shot speed). A boss's summons get the same presentation, red telegraph included, as every room enemy. Damaging floors are not telegraphs and are outside this contract.

No Support Enemy exists. Normal enemies do not heal/buff other enemies.

## Encounter Containment (implementation note 2026-09-19)

An encounter actor's collider may not cross the legal encounter-room boundary into a different room. Every actor a room spawns (encounter, reinforcements, summons, event waves), its Elite and its Boss is bound to that room's interior (the room minus the wall ring, which is also where the door cells and the combat door blockers are). Movement, dash/charge endpoints and knockback steps are constrained to that interior before they are committed, so the legal edge behaves like a wall the AI cannot path through: pursuit slides along it and holds there, a doorway is never a route even while the door is open or a lock is still pending on a player in it, and no open door of an event room is an exit. The pull-back clamp is a sub-step safety net for physics pushes, never the mechanism. Bosses additionally do not acquire a target before their arena is entered (BossEngagement hands it over on entry), so a player elsewhere on the depth never draws a Boss out of its arena. Closed combat doors remain physical blockers exactly as before.

## Pursuit Around Room Geometry (implementation note 2026-09-28)

A chasing enemy, Elite or Boss heads straight for its target while the straight line is clear. When walls or obstacles block that line and the target is inside the actor's room, the actor follows a route around them — a half-tile grid of cells its body fits in, sampled from the room's solid geometry inside its encounter bounds — and still slides along walls as before; the old steering alone pressed into a wall that stood head-on to the target and held there. A target outside the room (beyond an open doorway or a locked door) is not routed to: the actor holds at the room edge exactly as Encounter Containment above describes. Speeds, ranges, timing and states are untouched.

**Crowds (2026-09-28).** Enemy bodies stay solid to each other, but a pursuer no longer shoves into the back of another: it keeps a small gap from nearby bodies, steps around one directly ahead (holding the chosen side briefly), and when boxed in backs out of the pile for a moment (diagonally, or straight back out of a one-wide passage) before coming around again. Held up behind bodies for a moment, it asks the route for a way around them and takes it when one reaches the target. These crowd manoeuvres never pick a wall or an enemy-damaging hazard floor; walls and the room edge still have the final word, and speeds, ranges, timing and states are unchanged. When every place in attack range is taken, the others wait around the crowd rather than in a pressing queue.
