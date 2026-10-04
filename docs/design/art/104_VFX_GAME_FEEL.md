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

> **Implementation note (2026-10-03, shot-feel pass):** each player projectile profile in `ProjectileVisualCatalog` also names the rest of its shot's stack — a muzzle-flash kind, an impact kind, their seconds and the held weapon's kick — authored in `ProjectileFactory.ShotFeel` and drawn at native pixel size (never resampled). Weight ladder: SMG light flash + light spark (1 px kick); Pistol / Assault Rifle standard flash + spark burst (2 px); Battle Rifle heavy flash + heavy burst with debris (3 px); Shotgun heavy flash, a light spark per pellet (4 px); Sniper long cyan rail flash with side vents + heavy burst (3 px); Blaster cyan ring flash + plasma shock ring (2 px); Rocket heavy flash, its radius-true explosion (4 px); Bow no flash, light spark (1 px). Player rounds are thicker outlined slugs (5–7 px tall; SMG 5, Sniper 3), pellets 7 px chunks, blaster bolts 7 px plasma. The flash sits on the shot's solved spawn point; impacts face back toward the shooter, full on a target and dimmer/shorter on a wall. Rocket blasts use a radius-true sheet (`explosion_r<px>`, one per shipped rocket radius) drawn at native pixels, its broken cream/amber spark ring on the exact gameplay radius (never the telegraphs' solid orange outline). In co-op another player's trigger pull shows its profile's flash once (pellets share it) and its rocket's blast, drawn by the zero-damage presentation pool, which resolves no damage. Hostile projectiles keep their own read (no player impact sparks). Presentation only: damage, speed, range, collision, shake tiers and authority are unchanged.

## Blaster
Energy projectile identity, weapon heat glow at high heat, distinct overheat/vent VFX.

## Explosions
Large enough to feel powerful but not so smoky that they hide hazards/projectiles for seconds.

## Legendary Drop
May use a clearly stronger glow/VFX than lower rarities, but should not overwhelm the room.

## Ground Loot Rarity (implementation note 2026-09-28)
A dropped item shows its rarity on the floor in the established rarity colour (the same one lists, tooltips and slot frames use): Common and ammo nothing, coins unchanged; Uncommon/Rare/Epic/Legendary a pixel ground glow of 14/18/22/26 px that breathes slowly; Epic adds a 20 px soft shimmer column, Legendary a 28 px one with a rising glint. It follows the item, changes with it, disappears the moment the item is taken or the depth is left, and draws on the ground-details layer below hazard footprints, so hazards, telegraphs, characters and the item itself always draw over it.
