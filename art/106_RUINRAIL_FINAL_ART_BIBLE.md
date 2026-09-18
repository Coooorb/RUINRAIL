# RUINRAIL Final Art Bible

> **Status:** Approved production art-direction specification for the post-TASK-148 completion phase.
> **Game language:** English.
> **Scope:** This document refines `art/100_ART_DIRECTION.md`; it does not change gameplay design.

## 1. Visual Identity Statement

RUINRAIL is a top-down 2D pixel-art extraction roguelite with **compact, highly readable sprite construction**, a **gritty survival/extraction pixel-world finish**, and a **strong post-apocalyptic retro-futurist atmosphere** with a restrained modern industrial sci-fi layer.

The intended reference hierarchy is:

1. **Sprite construction and geometry — Soul Knight is the strongest construction reference.** Use compact forms, simplified anatomy, strong silhouettes, readable equipment, clean directional poses, and action readability at gameplay scale. Do not copy characters, costumes, weapons, tiles, effects, UI, or proprietary shapes.
2. **Pixel-world finish and environmental art character — Zero Sievert is an important visual reference.** Favor rough, survival-oriented, worn, utilitarian environments and a grounded extraction-game feeling. Preserve RUINRAIL's own geometry, palette, props, layouts, lore, and silhouettes.
3. **Atmosphere — Fallout 4 is the strongest atmospheric reference.** Emphasize decay, salvaged retro-futurist machinery, oxidized metal, abandoned infrastructure, improvised shelter technology, dust, grime, old signage, and melancholy post-apocalyptic spaces. Do not reproduce recognizable Fallout assets, brands, UI, factions, props, or iconography.
4. **Secondary atmosphere/technology — ARC Raiders is a medium-strength reference.** Use restrained modern industrial sci-fi accents, strong machine silhouettes, clear luminous technology cues, and a slightly cleaner high-tech layer where appropriate. Avoid glossy generic sci-fi and do not reproduce recognizable ARC Raiders assets or designs.

The result must be unmistakably **RUINRAIL**, not a replica of any reference title.

## 2. Non-Negotiable Readability Rules

- Gameplay readability beats realism and decorative density.
- Player, enemies, Elite/Boss telegraphs, projectiles, loot, hazards, doors, interactables and extraction/transit choices must remain legible against every biome.
- Normal body construction is simplified and chunky; micro-detail is reserved for close-up UI art or large Bosses.
- Silhouette communicates gameplay role before surface detail does.
- Bright emissive accents are scarce and functional: danger, interactability, high-tech state, rarity, or navigation.
- Do not use texture noise that causes characters, bullets, loot or telegraphs to disappear into floors/walls.

## 3. Technical Pixel Rules

Preserve the approved project rules:

- Reference resolution: **640×360**.
- Aspect ratio: **16:9**.
- Tile grid: **32×32 px**.
- World Pixels Per Unit: **32 PPU**.
- Normal gameplay sprite transform scale: **1,1,1**.
- Pixel-perfect orthographic camera.
- Player humanoid target canvas: approximately **32×48 px**.
- Similar humanoids: comparable scale.
- Swarm: approximately **16–24 px**.
- Brute: approximately **48×64 px**.
- Elites: approximately **48–64+ px**.
- Bosses: approximately **64–128 px**, depending on design.
- Sprite animation target: approximately **8–12 FPS** while simulation remains frame-rate independent.
- Player body remains **8-directional** while the separate weapon/pivot keeps mathematical **360° aim**.

Do not arbitrarily upscale low-resolution art and smooth it. Preserve hard pixel edges.

## 4. Character Construction

### Player

- Compact, readable survivor silhouette.
- Practical scavenged clothing/armor rather than heroic fantasy armor.
- Head, torso, hands/weapon relationship must remain readable at 32×48.
- Clothing may use worn fabric, patched protective plates, straps, masks, utility pouches and salvaged tech, but avoid cluttering the silhouette.
- The weapon is a separate visual system; do not bake every firearm into the body animation.
- Player must visually read as capable but vulnerable: a survivor/explorer, not a power-armored superhero.

### Normal Enemies

Each of the nine archetypes must have an immediately distinct silhouette corresponding to gameplay role:

- Grunt: direct melee threat; simple aggressive humanoid silhouette.
- Shooter: ranged weapon silhouette visibly different from Grunt.
- Swarm: small, fast, low-profile threat.
- Charger: forward-heavy body/readable charge posture.
- Brute: large mass, broad shoulders/body, heavy attacks.
- Bomber: explosive payload or thrower silhouette readable before attack.
- Shield: shield dominates front-facing silhouette.
- Sniper: long weapon and deliberate aiming silhouette.
- Summoner: support/controller silhouette with readable summon-tech or ritual-tech cue.

### Elites and Bosses

- Elites must look like escalations of the world rather than recolored normal enemies.
- Boss silhouettes must be identifiable from a room-scale view before attack starts.
- Attack telegraph components should be visually built into the design where possible.
- Larger sprites may carry more surface detail, but attack readability still wins.

## 5. Weapon Construction

- All **33 weapons** require distinct readable silhouettes.
- Weapons within the same class share a visual family language but must not become palette swaps.
- Legendary weapons may use stronger silhouette accents, emissive parts, hazard markings or unusual mechanisms while remaining consistent with the setting.
- Class identity at a glance:
  - Pistol: compact one-handed profile.
  - SMG: compact rapid-fire body.
  - Assault Rifle: balanced medium rifle.
  - Battle Rifle: heavier/longer precision rifle.
  - Shotgun: thick barrel/receiver and close-range weight.
  - Sniper: long precision silhouette.
  - Rocket Launcher: bulky explosive launcher.
  - Bow: obvious limb/string shape with charge readability.
  - Blaster: industrial energy weapon, not clean space-opera laser gun.
  - Knife: short close-combat silhouette.
  - Spear: long narrow reach silhouette.
- Surface language: stamped metal, salvaged parts, worn polymers, wrapped grips, improvised repairs, restrained luminous tech where justified.

## 6. Material and Palette Language

Global base palette favors:

- charcoal and soot-black;
- oxidized steel and cold gray;
- aged beige/off-white;
- muted olive and desaturated green;
- rust/burnt orange;
- restrained warning yellow;
- scarce cyan/green/amber emissive technology.

Avoid rainbow saturation. Rarity color and combat telegraphs must still remain readable and accessible.

## 7. Biome Identity

### Ruined Metro

**Mood:** abandoned transit infrastructure, damp concrete, failing emergency systems, old public-space materials.

Use:
- concrete, ceramic/stone station tile, rails, sleepers, cables, conduit, broken signs, gates, benches, utility cabinets, rubble;
- cool dirty gray/green base colors;
- emergency amber/red/yellow accents used sparingly;
- old infrastructure mixed with scavenger repairs;
- darkness as atmosphere, never as a readability mechanic.

### Rustworks

**Mood:** industrial salvage complex still partially alive.

Use:
- oxidized steel, plates, rivets, pipes, furnaces, conveyors, cranes, scrap piles, pressure systems, hazard markings;
- rust orange/brown against dark metal;
- furnace heat, sparks and warning light as local accents;
- stronger mechanical density than Metro without obscuring navigation.

### Overgrown Labs

**Mood:** damaged research facility reclaimed by uncontrolled organic growth.

Use:
- aged off-white lab surfaces, glass, terminals, clean-tech remnants, bio equipment;
- dark/desaturated greens and plant growth invading sterile geometry;
- restrained cyan/green scientific lighting;
- broken containment and organic irregularity contrasted with laboratory structure.

## 8. Safehouse / Shelter / Transit

The Safehouse should feel like the one place people have forced into relative safety:

- patched but maintained;
- warmer and more inhabited than dungeons;
- salvaged furniture, storage, workshop equipment, transit machinery, practical lighting;
- still visibly built from old infrastructure rather than a pristine headquarters.

The expedition transit car should feel heavy, mechanical and dependable, with enough wear to belong in the setting.

## 9. UI Direction

- UI is **RUINRAIL utilitarian shelter/field equipment**, not a direct copy of any reference game.
- Dark metal/charcoal surfaces, worn edge details, restrained rust/amber/green accents.
- Chunky pixel-frame language with high information clarity.
- Body copy must remain more readable than decorative headings.
- Important states use both color and shape/icon/text; never color alone.
- Inventory rarity frames must not overwhelm item silhouette.
- Buttons and focus states must be obvious for keyboard/mouse and controller.
- Pixel font is appropriate for headings/labels if readable; longer body text may use a crisp readable bitmap/pixel-compatible face.

## 10. VFX Direction

- Short, layered and readable rather than simulation-heavy.
- Muzzle flash, recoil, tracer/projectile, impact, hit flash, damage number, status and knockback feedback should reinforce existing gameplay events.
- Shotgun, Rocket and Boss attacks can feel heavier than small arms.
- Blaster uses energy identity and Heat/Overheat state cues.
- Legendary drops use a stronger but still controlled effect.
- Smoke/debris may never hide active hazards or projectiles for long.

## 11. Lighting Direction

- 2D lighting is atmospheric seasoning, not a visibility requirement.
- Ruined Metro: cool/dim ambient with localized emergency lights.
- Rustworks: warm industrial heat sources against dark metal.
- Overgrown Labs: colder scientific light with organic green contamination.
- Character/telegraph readability must survive with reduced lighting complexity.

## 12. Originality / Reference Boundary

References describe **high-level visual qualities only**. Production must not:

- trace, rip, recolor or modify copyrighted sprites from reference games;
- reproduce recognizable characters, robots, weapons, buildings, logos, UI layouts, icons, maps, signs or faction marks;
- copy exact palettes or sprite sheets;
- use screenshots as direct paint-over bases for final assets.

Every final sprite, prop, environment, icon and effect must be original RUINRAIL content.

## 13. Final Asset Acceptance Standard

An asset is production-ready only when:

1. it is original and follows this Art Bible;
2. it is readable at 640×360 gameplay scale;
3. world scale/PPU/import settings are correct;
4. its silhouette communicates role;
5. it remains readable against its intended biome;
6. required directional/animation frames exist or the asset's design intentionally uses an approved reduced set;
7. it creates no broken references or missing-material/sprite warnings;
8. it has been reviewed in the actual game, not only in an image editor;
9. placeholder art for the same role is removed from the release path;
10. it passes the relevant production validator and screenshot/manual review gate.
