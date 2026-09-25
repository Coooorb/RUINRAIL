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


> **Implementation note (2026-09-19, projectile visuals pass):** every travelling projectile — player and enemy — is drawn in flight by a sprite parented to the pooled projectile itself (`ProjectileVisual`), so it is exactly where the authoritative shot is, points along its velocity, disappears on the registered hit / wall / expiry and is reset before reuse; there is no separate fake bullet. Profiles live in one release catalog (`ProjectileVisualCatalog`): a family default per ranged weapon class — Pistols small warm bullet + short tracer, SMGs thin fast tracer, Assault Rifles medium tracer, Battle Rifles heavier brighter tracer, Shotguns small readable pellets, Snipers thin high-contrast rail, Bows arrow aligned to travel, Blasters cyan energy bolt (2-frame flicker), Rocket Launcher rocket body + flickering exhaust trail — with a Legendary variant per family (amber accent, longer tracer) bound to every Legendary ranged weapon, and hostile profiles per attack: muted red/orange round (shooter/summoner), thin red rail (enemy sniper), amber/red industrial bolt (Railguard), rust shard (Crusher), sickly green lab energy (Prototype X-7), and larger attack-specific Boss profiles (Scrap King heavy round, Conductor arc, Titan ember, Omega spore, Aegis lab energy). Every sprite keeps a near-white core and a dark edge so it reads on all three biome floors; sizes 3–20 px, pivot at the head so nothing draws ahead of the physics point; sorting layer Projectiles. Speed, damage, range, collision and authority are untouched.

## Blaster
Energy projectile identity, weapon heat glow at high heat, distinct overheat/vent VFX.

## Explosions
Large enough to feel powerful but not so smoky that they hide hazards/projectiles for seconds.

## Legendary Drop
May use a clearly stronger glow/VFX than lower rarities, but should not overwhelm the room.
