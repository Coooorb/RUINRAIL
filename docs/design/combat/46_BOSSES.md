# Boss Catalog

> **Status:** Approved V1 design specification.
> **Game language:** English.

Each biome has two Bosses. Bosses have approximately four core attacks and two phases. Phase 2 starts at **50% HP** and increases/combines familiar pressure rather than introducing an unrelated rule set.

Stats below are Solo pre-Depth-scaling baselines. Party-size HP scaling is defined in the co-op scaling spec.

| Boss | Biome | Solo Base HP | Strongest single-hit target | Base XP |
|---|---|---:|---:|---:|
| **The Conductor** | Ruined Metro | 1,050 | 30–36 | 650 |
| **Tunnel Maw** | Ruined Metro | 1,150 | 28–34 | 700 |
| **The Foundry Titan** | Rustworks | 1,350 | 30–36 | 800 |
| **Scrap King** | Rustworks | 1,000 | 22–28 | 650 |
| **Subject Omega** | Overgrown Labs | 1,250 | 28–34 | 750 |
| **A.E.G.I.S. Core** | Overgrown Labs | 1,050 | 28–34 | 700 |
| **The Warden** | Cryo Vaults | 1,150 | 28–34 | 700 |
| **Subject Zero** | Cryo Vaults | 1,250 | 28–34 | 750 |

## Ruined Metro

### The Conductor
Automated metro/security command robot.
- Burst Cannon.
- Projectile Sweep.
- Marked Rail Strike.
- Emergency Dash.
- Phase 2 activates clearly telegraphed rail-line arena hazards and increases pressure.

### Tunnel Maw
Large tunnel mutant.
- Bite/Lunge.
- Claw Sweep.
- Marked Leap.
- Roar with limited Swarm pressure.
- Phase 2 becomes more aggressive and can briefly burrow; underground movement and emergence remain visibly telegraphed.

## Rustworks

### The Foundry Titan
Huge industrial robot.
- Hydraulic Slam.
- Marked Rocket Barrage.
- Arm Sweep.
- Furnace Blast cone.
- Phase 2 exposes/overloads the reactor, moderately increases pattern speed, and lets some attacks leave short-lived burning zones.

### Scrap King
Mobile armored wasteland fighter.
- Automatic Burst.
- Grenade Throw.
- Combat Roll/Dash.
- Heavy Melee Swing at close range.
- Phase 2 sheds armor: less defense, more movement/aggression.

## Overgrown Labs

### Subject Omega
Large mutation/biomass experiment.
- Arm Slam.
- Charge.
- Spore Projectile Burst.
- Vine danger zone.
- Phase 2 creates additional temporary organic area denial and faster slam combinations.

### A.E.G.I.S. Core
High-tech security core.
- Triple Energy Burst.
- Radial Projectile Ring.
- Telegraphed line energy attack.
- Reposition Dash.
- Phase 2 combines familiar patterns, e.g. a radial ring while preparing a line attack.

## Cryo Vaults (2026-09-29)

Values chosen inside the existing bosses' envelopes (HP 1,000–1,350, strongest hit 28–34, phase 2 at 50% with ×0.8 timing).

### The Warden
Autonomous vault security machine — sensor mast, red security optic, long emitter arm. Area control and positioning.
- Sweep Fan (7 ice lances over 100°).
- Aimed Volley (four aimed lances, 0.18 s apart).
- Lockdown Charge — the long charge (1.3 s telegraph, 9 tiles, 28–34).
- Emergency Purge (radial ring of 14 lances, close range).
- Phase 2 adds Purge Lanes: long telegraphed 12 × 1.5 tile zones that cut the arena into lanes.

### Subject Zero
The preserved experimental subject, out of its cradle — frost-pale, one outsized claw arm, a cyan cryo-port in the chest.
- Frenzy Charge (fast: 0.7 s telegraph, 16 tiles/s).
- Chained Slam (three slams 0.45 s apart, radius 2.4).
- Claw Cleave (large close cleave, radius 3.0, 28–34).
- Radial Burst (12 shards all round).
- Phase 2 adds Thaw Ruptures: telegraphed 4 × 4 zones anywhere in the arena.

## Shared Rules

- No crit/weak-spot mechanics.
- No cheap untelegraphed one-shots.
- Boss Stagger resistance is very high.
- Boss death spawns a high-quality Boss Cache and activates the Transit Car.
- A Boss never leaves its arena: pursuit stops at the arena's legal edge, charge/dash/leap endpoints are constrained to it, knockback cannot eject it, and a player outside the arena does not cause it to chase out (combat/43 Encounter Containment, 2026-09-19).
- Every Boss opens with a room introduction inside a fixed 2.2 s hold (the Boss has no target and gameplay input is held; Confirm skips it after 0.25 s; the hold and the introduction always end together): letterbox and rim close as the camera travels to the Boss, a reveal beat as it arrives (the Boss flares in its own glow, a ragged burst of the biome's matter — frost, grit, embers, spores or sparks — runs out across the floor, one screen kick, the Boss's power cue), then a name band on the letterbox: biome · BOSS, the name, and the Boss's identity line above, in its own accent. Presentation only; nothing about the fight changes (2026-10-04).
