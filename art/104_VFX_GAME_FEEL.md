# VFX and Game Feel

> **Status:** Approved design specification unless explicitly marked as tunable.
> **Game language:** English. Planning discussions may be in German, but all player-facing text, code naming, comments, and Claude implementation specs should be English.
Use small layered feedback instead of complex simulation:
- Muzzle flash.
- Short weapon visual recoil.
- Projectile/tracer visibility.
- Impact VFX.
- Enemy hit flash.
- Damage number.
- Knockback/stagger reaction.
- Controlled screen shake for heavy events.

Screen shake is minimal for small guns, stronger for shotgun/rocket/boss slam, and adjustable/off in Settings.

## Projectiles
Make bullets/projectiles larger/more readable than realistic physical bullets. The combat model depends on visible trajectories.

## Blaster
Energy projectile identity, weapon heat glow at high heat, distinct overheat/vent VFX.

## Explosions
Large enough to feel powerful but not so smoky that they hide hazards/projectiles for seconds.

## Legendary Drop
May use a clearly stronger glow/VFX than lower rarities, but should not overwhelm the room.
