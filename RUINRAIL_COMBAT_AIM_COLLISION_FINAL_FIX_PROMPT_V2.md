# RUINRAIL — COMBAT AIM / HIT REGISTRATION / ENEMY COLLISION + FINAL VISUAL FIX PASS V2
## Execute from the CURRENT repository state after FINAL_PLAYABILITY_PASS_COMPLETE

This is a focused final combat-readability, hit-registration, enemy-collision and shooting-feel pass.

Do NOT ask for intermediate approval.
Do NOT create a new task series.
Do NOT add unrelated gameplay features.

Fix all issues below, add regression coverage, run the relevant full test/build gates, and return one consolidated report only at the end.

---

# 0. READ FIRST

Read the latest/current versions of:

- CLAUDE.md
- CLAUDE_START_HERE.md
- technical/117_CODING_RULES_FOR_CLAUDE.md
- technical/118_TESTING_STRATEGY.md
- FINAL_ART_PRODUCTION_SPEC.md
- production/FINAL_PLAYABILITY_REGRESSION_REPORT.md
- production/POST_POLISH_REGRESSION_FIX_REPORT.md
- production/UI_AND_BIOME_POLISH_REPORT.md
- current aiming, projectile, muzzle, ranged weapon, damage, enemy movement, enemy charge/lunge, Rigidbody2D/Collider2D, collision layers, wall/tilemap colliders, room doors, telegraphs and reload code

Repository truth wins over older documents.

Create/update:

`production/COMBAT_AIM_COLLISION_AND_FINAL_VISUAL_FIX_REPORT.md`

---

# 1. HARD SCOPE

Primary issues to fix:

1. Player cannot reliably hit enemies even when the crosshair appears directly over them.
2. Add a generous but controlled soft aim-assist layer after the root hit-registration issue is fixed.
3. Add automatic reload when a magazine becomes empty and reserve ammo exists.
4. Remove the large red/orange square/quad visual seen during gameplay if it is a debug/placeholder/incorrect telegraph or collider visualization.
5. Replace the current orange-line door/door-lock visual with a proper RUINRAIL pixel-art door/lock state.
6. Enemies can move through walls/solid blockers even though the player cannot. Fix enemy collision/pathing so solid world geometry is respected.

Do NOT redesign:
- characters
- weapons
- accepted VFX families except the bad debug/placeholder quad if it is one
- biome art
- unrelated UI
- audio

Do NOT change:
- approved weapon damage/rate/range/ammo stats
- enemy health/damage stats
- save semantics
- networking authority model
- stable IDs
- room topology except where necessary to correct collision/pathing behavior

---

# 2. HIT REGISTRATION — ROOT CAUSE FIRST

This is the highest-priority combat issue.

Observed behavior:
The player can place the crosshair directly on an enemy and still fail to hit the enemy.

This must NOT be solved by simply increasing aim-assist until the bug is hidden.

First determine why a direct visual aim can miss.

## 2.1 Trace one complete shot

Trace:

1. raw input / cursor screen position
2. screen-to-world conversion
3. resolved aim world position
4. player aim direction
5. weapon pivot rotation
6. muzzle world position
7. projectile spawn position
8. projectile initial direction / velocity
9. projectile collider shape and Rigidbody2D settings
10. projectile collision layer / contact filter / trigger behavior
11. enemy hurtbox / collider position and dimensions
12. enemy layer
13. DamageAuthority / IDamageable resolution
14. network authority path where applicable
15. projectile range/lifetime
16. EnvironmentObstacle filtering

Use temporary diagnostics as needed, but no debug rendering may remain enabled in release.

## 2.2 Correctness invariant

For an unobstructed enemy inside valid range:

**If the crosshair world point lies inside that enemy's valid hurtbox/collider, firing a normal direct projectile weapon must produce a hit.**

Aim assist is not required for this invariant.

For spread weapons:
- center/base aim must still be correct;
- spread is applied around the correct direction.

## 2.3 Explicit checks

Investigate:
- wrong ScreenToWorldPoint depth/camera;
- cursor coordinate space mismatch;
- muzzle offset causing parallel misses;
- visual pivot rotation differing from projectile direction;
- projectile collider too small;
- hurtbox smaller/offset relative to visible body;
- wrong physics layer;
- ContactFilter2D misconfiguration;
- trigger/collision mismatch;
- tunneling;
- collision-detection mode;
- projectile spawning past close targets;
- team filter rejecting enemies;
- environment hit occurring first incorrectly;
- pixel-perfect visual position differing from physics position;
- stale aim direction;
- authority rejecting damage;
- Y/Z mismatch between sprite and collider.

Fix actual root cause(s).

Do not arbitrarily enlarge every enemy collider.
If a hurtbox is clearly smaller than the visible combat silhouette, align it conservatively and document it.

---

# 3. DIRECT-AIM REGRESSION TESTS

Add deterministic tests for:

- P9 Ranger, target directly under crosshair center -> hit;
- crosshair near target edge but still inside hurtbox -> hit;
- target just outside collider with assist disabled -> miss;
- wall between player and target -> no false hit;
- close-range target -> projectile does not spawn past it;
- long-range target within weapon range -> hit;
- targets in N / NE / E / SE / S / SW / W / NW directions -> correct hit;
- representative projectile classes:
  - pistol
  - SMG
  - AR
  - battle rifle
  - shotgun center direction
  - sniper
  - blaster
  - rocket where direct projectile rules apply
- host-authoritative damage remains valid.

Where practical, add PlayMode proof showing visible enemy HP reduction.

---

# 4. GENEROUS SOFT AIM ASSIST

Only after direct hit registration is correct, add aim assist.

Goal:
The player should not require pixel-perfect precision in a 640×360 top-down pixel game.

This is soft projectile-direction assistance, not target lock and not auto-fire.

## 4.1 Defaults

Use tunable configuration.

### Mouse / keyboard
- assist half-angle: **18 degrees**
- crosshair proximity preference: approximately **28 px** at 640×360
- maximum target distance follows current weapon range

### Controller
If controller aim is reliably distinguishable:
- assist half-angle: **24 degrees**

If not:
- shared default: **20 degrees**

These defaults are intentionally forgiving.

## 4.2 Candidate rules

Target must:
- be hostile and alive;
- be within weapon range;
- be generally in front of raw aim;
- be inside configured assist cone;
- have clear line of sight from muzzle to target aim point;
- not be behind solid environment geometry;
- not be behind the player just because it is close.

## 4.3 Selection

Score primarily by:
1. screen-space/crosshair proximity;
2. angular difference;
3. distance as weaker tie-breaker.

Do NOT choose nearest world-space enemy blindly.

If crosshair is already on a target hurtbox:
- that target gets overwhelming priority.

## 4.4 Aim point

Use:
- hurtbox center or dedicated combat aim point.

Do not use:
- sprite corner;
- health-bar position.

## 4.5 Correction behavior

When a valid assisted target exists:
- final shot direction = muzzle -> selected aim point;
- projectile is emitted in that direction;
- no bullet teleporting;
- no curved bullets unless weapon explicitly has that mechanic;
- do not move mouse cursor;
- do not camera-lock.

Avoid obvious aim snapping.

## 4.6 Weapon multipliers

Suggested:
- Pistol / SMG / AR / Battle Rifle: 1.0×
- Shotgun: 1.0× center assist, preserve pellet spread
- Sniper: 0.75×
- Blaster: 1.0×
- Bow: 1.0× on release
- Rocket: 0.65×
- Melee: no projectile aim assist

Do not change weapon stats.

---

# 5. AIM ASSIST TESTS

Test:
- 0° target -> selected;
- 10° -> selected;
- 17° -> selected with 18° mouse default;
- outside cone -> not selected;
- direct crosshair-over-target wins;
- target behind wall -> rejected;
- dead target -> rejected;
- target behind player -> rejected;
- two targets -> best score wins;
- controller stronger if implemented;
- sniper multiplier narrows cone;
- rocket multiplier narrows cone;
- no valid target -> raw aim unchanged;
- shotgun spread preserved around assisted center;
- host-authority/damage path unchanged.

---

# 6. AUTO-RELOAD ON EMPTY MAGAZINE

Add automatic reload for magazine-based ranged weapons.

After a successful shot causes:
`magazineAmmo == 0`

then if:
- reserve ammo > 0;
- weapon is not already reloading;
- weapon uses normal magazine reload;

automatically begin the existing reload flow.

Reuse existing reload implementation.

Do NOT auto-reload:
- melee;
- bows if not magazine-based;
- blasters/heat weapons;
- weapons with zero reserve;
- special resource systems.

Rocket:
- if it uses standard ranged reload, auto-reload is allowed after last loaded rocket;
- preserve Heavy ammo cost.

Auto reload obeys existing cancellation/interruption:
- weapon swap
- action gates
- death/downed
- scene transition

Tests:
- P9 final shot -> reload starts;
- reserve 0 -> no reload;
- no duplicate reload;
- swap cancels according to current rules;
- representative mag-fed weapon classes;
- bow/blaster/melee excluded;
- ammo integrity preserved.

---

# 7. REMOVE RED / ORANGE GAMEPLAY QUAD

Observed in real gameplay:
A large red/orange rectangle/square appears near or in front of the player.

Find what it actually is.

Investigate:
- debug collider visualization;
- room activation debug;
- telegraph fallback sprite;
- melee/attack hitbox visualization;
- interaction/debug volume;
- missing sprite fallback;
- debug material;
- editor-only visual leaking into build.

Required outcome:

If debug-only:
- remove/disable from release;
- keep editor diagnostics only behind explicit debug/editor flag if useful.

If real telegraph:
- replace raw quad with accepted final telegraph VFX;
- ensure size/timing matches actual mechanics.

If incorrect asset binding:
- bind correct final asset.

Add release-path validator/test where feasible.

Proof screenshot: no unintended orange/red quad.

---

# 8. PROFESSIONAL DOOR / COMBAT-LOCK VISUALS

Observed:
Closed/locked doorway is represented mainly by a thin orange line.

Replace with intentional pixel-art door/lock presentation.

## Open state
- clearly traversable;
- no invisible blocking collider;
- housing/frame may remain.

## Combat-locked state
- real physical or energy-assisted barrier;
- collider and visual state agree;
- instantly readable as locked/closed;
- not just one flat line.

## Biome skins

### Ruined Metro
- industrial transit/service shutter
- steel/concrete
- small amber/red lock indicator

### Rustworks
- heavier industrial shutter/barrier
- blackened steel
- restrained hot/warning accent

### Overgrown Labs
- damaged lab/security door
- pale paneling
- cyan/green system light
- amber/red lock state

Shared gameplay implementation; biome-specific visual skin only.

Preserve room-entry fix:
- player enters destination room first;
- lock engages behind them;
- co-op players are not prematurely stranded.

Test visual/collider synchronization.

---

# 9. ENEMY WALL COLLISION / PATHING FIX

Observed behavior:
The player is correctly blocked by walls/solid room geometry, but enemies can move through walls and solid blockers.

This is a gameplay bug.

## 9.1 Core rule

Enemies must obey the same intended solid-world boundaries as the player unless a specific enemy is explicitly designed in the approved V1 specification to ignore them.

Do NOT invent wall-phasing exceptions.

Enemies must NOT pass through:
- room walls;
- Tilemap/CompositeCollider2D solid geometry;
- solid obstacles;
- closed combat doors;
- sealed unused sockets;
- other world blockers marked solid by the current collision contract.

Enemies MAY pass through:
- valid open doorways;
- traversable floor;
- explicitly non-solid gameplay volumes.

## 9.2 Root-cause investigation

Inspect:
- EnemyController movement;
- Rigidbody2D body type;
- Collider2D setup;
- isTrigger usage;
- layer collision matrix;
- ContactFilter2D;
- movement via `transform.position` or other direct teleport-like writes;
- Rigidbody2D velocity / MovePosition;
- obstacle masks;
- TilemapCollider2D / CompositeCollider2D setup;
- pathfinding/navigation graph;
- steering logic;
- knockback;
- charge attacks;
- lunges;
- blink/special enemy movement;
- spawn locations;
- door-lock collider layers.

Determine whether enemies are:
1. ignoring physics entirely;
2. on a non-colliding layer;
3. using direct transform writes;
4. routing through invalid navigation cells;
5. tunneling during fast movement;
6. being pushed through colliders during attacks/knockback.

Fix the actual cause.

## 9.3 Movement requirements

After fix:

- walking enemies stop at walls;
- enemies may slide along walls naturally;
- enemies do not jitter continuously against walls;
- enemies do not become permanently embedded in colliders;
- enemies can use valid open passages;
- pursuit does not choose a straight line through a wall if a valid route around exists;
- melee enemies remain on the correct side of a wall until they find a legal route;
- ranged enemies do not phase through walls to regain line of sight;
- enemies respect closed combat doors;
- enemies remain contained inside locked combat rooms as intended.

## 9.4 Fast attack movement

Explicitly validate:
- Charger charge;
- Tunnel Stalker / elite lunge/rush;
- any boss dash/charge;
- knockback-driven enemy displacement.

These must not tunnel through walls.

If required:
- use swept collision/cast before displacement;
- clamp movement to collision contact;
- use appropriate Rigidbody2D collision detection;
- stop/cancel charge on solid impact according to existing attack semantics.

Do NOT change attack damage or cooldowns merely to fix collision.

## 9.5 Pathing behavior

If the current project has pathfinding/navigation:
- wall cells must be unwalkable;
- closed combat-door cells must be unwalkable;
- open doorway cells must be walkable;
- navigation data must match runtime collider geometry.

If no full pathfinding exists and enemies use steering/chase:
- add the smallest robust obstacle-avoidance / collision-constrained movement needed;
- do NOT build a large unrelated navigation system unless necessary.

## 9.6 Spawn safety

Enemy spawn points must be validated:
- not inside wall collider;
- not inside sealed socket;
- not outside playable room bounds.

If invalid:
- choose/reject the spawn using existing deterministic room logic;
- do not teleport through walls after spawn.

---

# 10. ENEMY COLLISION REGRESSION TESTS

Add deterministic coverage:

- normal enemy walks into straight wall -> blocked;
- enemy approaches wall diagonally -> slides/stops, never crosses;
- enemy in room corner -> cannot escape through corner;
- enemy cannot cross closed combat door;
- enemy can traverse valid open doorway;
- enemy chasing player behind wall does not cross wall;
- if valid route exists, enemy eventually uses valid path/doorway under current AI design;
- ranged enemy does not phase through wall to gain line of sight;
- Charger charge stops at wall;
- representative Elite lunge/rush stops at wall;
- representative Boss dash/charge does not tunnel through wall;
- knockback cannot push enemy through wall;
- enemy spawn is rejected/corrected if inside wall;
- all 3 biomes' wall/obstacle layers block enemies;
- sealed unused room socket blocks enemies;
- room combat lock contains enemies;
- multiplayer/network-authoritative enemy position remains on legal side of wall.

Sweep representative seeds/depths across all three biomes.

Add a test invariant:
**No enemy center/collider may transition from one side of a solid wall to the other without traversing a valid opening.**

---

# 11. BUILT-PLAYER PROOF

Capture at least:

1. direct crosshair-on-enemy shot with HP reduction;
2. near-miss aim corrected by assist;
3. target outside assist cone -> no snap;
4. auto reload after final magazine shot;
5. scene with no unintended orange/red quad;
6. Ruined Metro open door;
7. Ruined Metro combat-locked door;
8. Rustworks combat-locked door;
9. Overgrown Labs combat-locked door;
10. normal enemy visibly blocked by a wall;
11. Charger/fast enemy stopped by solid wall;
12. enemy contained by closed combat door.

Save under:
`TestResults/CombatAimCollisionProof/`

If screenshot alone cannot prove collision, accompany with deterministic PlayMode position/contact evidence.

---

# 12. FULL TEST / VALIDATION

After final source change:

Run strict EditMode:
- exit 0
- XML exists
- total > 0
- passed > 0
- failed = 0

Run strict PlayMode with same requirements.

Also run:
- projectile/ranged tests
- ammo/reload tests
- enemy health/damage tests
- enemy movement/collision tests
- network combat/enemy-authority tests
- dungeon room/door tests
- presentation/art/release-path validators
- content-count validator
- final production validator

Fix regressions and rerun.

Do not disable Smart App Control automatically.

---

# 13. RELEASE BUILD + SMOKE

Produce clean Windows x64 NON-DEVELOPMENT build.

Run built-player smoke and interactive/windowed combat proof if available.

Require:
- direct crosshair-on-enemy shots damage correctly;
- aim assist works;
- aim assist does not shoot through walls;
- auto reload works;
- no red/orange debug quad;
- door visuals are final;
- enemies cannot pass through walls;
- enemies cannot pass through closed combat doors;
- fast enemy movement cannot tunnel through solid geometry;
- valid open doorways remain traversable;
- no exceptions/missing references.

Do not claim live Relay success unless actually tested over UGS.

---

# 14. FINAL REPORT

Write:

`production/COMBAT_AIM_COLLISION_AND_FINAL_VISUAL_FIX_REPORT.md`

Report:
- actual root cause(s) of direct misses;
- exact aim corrections;
- aim-assist algorithm and config values;
- auto-reload implementation;
- identity/root cause of the red/orange quad;
- door visual changes;
- actual root cause of enemies crossing walls;
- movement/pathing/collision fixes;
- fast-movement collision handling;
- tests added;
- exact EditMode/PlayMode results;
- build result;
- smoke result;
- proof paths;
- any genuine remaining blocker.

Terminal status:

`COMBAT_COLLISION_FIX_COMPLETE`
only if all repository-local issues in this pass are fixed and verified.

`COMBAT_COLLISION_FIX_INCOMPLETE`
if repository-local work remains.

Do not ask for approval during execution.

BEGIN NOW.
