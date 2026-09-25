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

## Shared Rules

- No crit/weak-spot mechanics.
- No cheap untelegraphed one-shots.
- Boss Stagger resistance is very high.
- Boss death spawns a high-quality Boss Cache and activates the Transit Car.
- A Boss never leaves its arena: pursuit stops at the arena's legal edge, charge/dash/leap endpoints are constrained to it, knockback cannot eject it, and a player outside the arena does not cause it to chase out (combat/43 Encounter Containment, 2026-09-19).
