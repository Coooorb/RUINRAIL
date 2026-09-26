# Normal Enemy Archetypes

> **Status:** Approved V1 design specification.
> **Game language:** English.

There are exactly **9 normal enemy archetypes** for V1. Enemy factions are not a separate gameplay system. Biomes may reuse the same underlying archetype AI with different art variants. New archetypes unlock by Depth; after Depth 14, normal enemy variety comes from combinations and scaling rather than continually adding systems.

All stats below are **pre-Depth-scaling baseline values**. When an enemy first becomes eligible at a later Depth, apply the standard Depth multiplier for that actual Depth.

| Enemy | Unlock | Base HP | Core damage | Move speed | Base XP |
|---|---:|---:|---:|---:|---:|
| **Grunt** | 1 | 30 | 6–8 melee | 3.0 | 12 |
| **Shooter** | 1 | 24 | 5–7 projectile | 2.6 | 14 |
| **Swarm** | 1 | 10 | 3–5 melee | 4.0 | 5 |
| **Charger** | 3 | 45 | 18–22 charge | 2.7 | 25 |
| **Brute** | 5 | 90 | 18–28 depending on attack | 1.8 | 40 |
| **Bomber** | 7 | 40 | 16–20 grenade | 2.5 | 28 |
| **Shield Enemy** | 9 | 55 | 10–14 bash/attack | 2.2 | 30 |
| **Sniper** | 11 | 30 | 28–34 shot | 2.2 | 35 |
| **Summoner** | 14 | 65 | 4–6 direct | 2.0 | 45 |

## Behavior Rules

### Grunt
Basic melee pursuer. Short telegraphed swing. Low Stagger and Knockback resistance.

### Shooter
Maintains medium distance and fires visible projectiles/short bursts. No hitscan.

### Swarm
Very small, fast, low-HP melee pressure. Normally appears in groups.

### Charger
Stops, shows roughly **0.7s** direction telegraph, then charges in a straight line. Missing or hitting a wall creates a clear recovery window.

### Brute
Slow heavy melee enemy with high Stagger/Knockback resistance. Uses Heavy Swing and telegraphed Ground Slam.

### Bomber
Throws a projectile toward the player's approximate position. The landing/explosion zone is clearly telegraphed before detonation.

### Shield Enemy
Frontal shield reduces incoming frontal projectile damage by **80%**. Side, rear, and appropriate AoE/explosion positioning bypasses the frontal reduction. No shield-HP subsystem. May use Shield Bash.

### Sniper
Visible aim line tracks, locks, then fires a very fast projectile. Never untelegraphed hitscan.

### Summoner
Summons only Swarm enemies: **2–3 Swarms every 9 seconds**, with a maximum of **6 living summoned Swarms per Summoner**. Low direct damage.
