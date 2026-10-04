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

**Biome enemy palettes (2026-09-28).** Normal enemies and Elites wear their biome's shared enemy palette as one multiply tint on the body sprite, never a new sprite or variant: Ruined Metro cold steel blue, Rustworks rust/ember, Overgrown Labs bio green, Cryo Vaults pale cyan. Silhouettes, value patterns, animation and hitboxes are unchanged, so every type stays the same type in every biome. Elites take the tint at half strength and keep more of their authored colour; bosses keep their own identity. The hit flash replaces the tint for its frames and returns to it; telegraphs, status and impact effects are separate renderers the tint never touches (`EnemyBiomeTint`).

**Dungeon chest art (2026-10-02, replaces the 2026-09-28 biome chest tint).** Every chest built through the shared chest path is drawn by `ChestArt`, per biome, tier and state, at 1:1 on the pixel grid with its own contact shadow. All tiers share one construction so they read as a family: a reinforced container in three-quarter view with corner brackets, an overhanging lid band, a lock plate and a status lamp (lit while full, dark once looted, dull warning while locked). Size, trim and markings climb with the tier: Supply Chest (plain field container), Elite reward (the same container with steel bands and a gold double chevron), Loot-room equipment chest (wide gear case with handle and latches), Treasure chest (brass-bound strongbox with domed lid), and the Boss Cache (nearly twice the bulk, on a skid, with a gold frame, corner caps, an emblem gem and a glowing biome core that lights the floor in front). Each biome builds the container its own way: Metro a blue-grey transit maintenance crate with hazard tape and a stencilled number, Rustworks a riveted rust-iron tool chest with black-iron straps and rust streaks, Labs a rounded off-white specimen case with a teal stripe and creeping moss, Cryo an insulated dark cold-storage box with a frosted lid, cyan seal and amber label. The opened state shows the lid hinged back and the emptied interior. The locked Boss Cache wears its biome's lock: Metro clamp bars and padlock, Rustworks chains, Labs a containment field, Cryo an ice casing. Art keys, state swaps, colliders (1×1 trigger), prompts, loot and spawn rules are unchanged. Event objects (Cursed Chest, Weapon Cache) are drawn by `InteractableArt` (below).

**Non-combat interactable art (2026-10-02).** The dungeon merchant and every event object are drawn by `InteractableArt` per biome and state, in the same three-quarter, outlined, grounded construction as the chests, at 1:1 with the old pivot so nothing moves. Each has one silhouette and a lit status element that carries its state: merchant stall (striped awning, goods on the counter, lantern); Medical Station (green cross lit / dark once used); Weapon Cache (open case with weapons in foam / shut and dark once taken); Cursed Chest (chained, violet seams / prised open and pouring violet light while its guardians are up / dead once cleared); Locked Vault (round door with spoked handle, amber keypad lamp / swung open, green lamp); Broken Machine (panel hanging off, sparks and smoke / panel shut, green lamp once repaired / burnt black once failed); Supply Signal (beacon mast and dish, amber / transmitting / dark once the drop lands); Secure Relay (upload screen / green lock once the member has secured an item). Biome sets the body material and its frost, moss or rust. The state follows the event's own phase (`InteractableStatePresenter`; the Secure Relay keeps its per-member swap), and the resolved tint still dims a used object. Art keys, prompts, colliders, rules and rewards are unchanged.

**Damaging-floor art (2026-10-02).** Each painted hazard cell is re-skinned in place by `HazardArt` with a per-cell 8-frame tile at the original loop rate (collider type None, biome-stem name). Every frame is a pure function of biome, cell and frame, so a region reads as one shaped installation: its outer boundary carries a clear edge and its interior animates as one continuous surface across cells. Metro Electrified Rail: a ballast trench with sleepers and a live third rail on ceramic insulators, current pulsing along it and arcs jumping to the ballast, framed by worn yellow-black warning stripes. Rustworks Furnace Grate: a riveted iron frame and heavy transverse bars over a deep-red flickering pit with hot spots and rising embers. Overgrown Labs Acid Pool: toxic green that deepens toward the middle, with wandering glints, bubbles that swell and pop, and a corroded foam rim. Cryo Vaults Coolant Leak: pale coolant with a shimmering caustic surface, drifting vapour and a jagged white frost crust. The same cells are painted. Damage, ticks, the RoomHazard trigger boxes and the hazard definitions are untouched.

**Attack telegraphs (2026-10-03, replaces the stretched telegraph sprites).** Every enemy, Elite and Boss danger marker is painted at runtime by `TelegraphMarkerView` on the world pixel grid (32 px per tile, never rotated or stretched) from the attack's real footprint (`AttackFootprint`, the same shapes the attack resolver strikes), so a marker can never be smaller than what it warns about: every pixel that touches the footprint is part of it. Construction: a bright outer edge on a 1 px dark backing (2 px edge for Elites/Bosses), a transparent centre with a quiet world-anchored pattern (diagonal hatch for rings and zones, chevrons along a dash, a dashed centre line on a shot lane), normal enemies amber-orange, Elites/Bosses red. Timing reads off the ground: a fill sweeps from the source to the far edge and arrives on the frame the attack commits (scaled telegraphs included), the edge blinks white-hot twice in the final ~0.24 s, and the footprint flashes on impact (0.14 s; 0.22 s for Elites/Bosses, and a Boss's ground strike adds the configured boss-slam shake). Danger that outlasts the telegraph stays drawn: later windows of a multi-hit move (refilling to each window), a dash while it runs, volleys still to fire, a lobbed bomb's ring until it lands. Shot and dash lanes stop at the first wall they really stop at. Markers draw on the ground-details layer above hazards and loot glows and below every character, projectile and pickup. A heavy marker (a full ring of lanes, a 30-tile rail zone) is rasterised over a few frames within a 2.5 ms per-frame budget and shown only once complete — within the first few frames of a warning that lasts at least 0.3 s — so it never costs a frame hitch and is never shown half-drawn.

**Room environment layer (2026-10-02).** Every room gets a runtime-painted environment layer from the composer's dressing seam (`RoomEnvironmentDressing`, after `RoomPropDressing`), deterministic per run seed, depth and node, so every peer sees the same room. Floor tiles merge into larger slabs (seams removed from the tile's own pixels), and the floor carries low-frequency mottle, door-to-centre wear lanes, stains, cracks, biome markings, contact shadows under walls and cover, dirty edges, wall-foot grit and bleed, wall-side patches and dithered lamp pools. Walls get stains, streaks, chips and biome furniture (posters, cables, vines, frost, warning plates, lamp fixtures). Biome vocabulary: Metro damp soot, oil and water damage, a safety-yellow platform strip, concrete rubble, ticket litter and sodium/fluorescent lamps. Rustworks oil, rust bleed and soot, faded hazard stripes, scrap and steel offcuts, furnace glow. Overgrown Labs moss carpets, root mats in corners, biomass, glass shards, loose files, bio-green/violet glow. Cryo Vaults frost creeping from walls, ice sheets and meltwater, pale guide lines, amber stencils, cold cyan light with sparse amber emergency light. Each room tells its category's story on one wall run, placed asymmetrically: Merchant camp rug, crates and lantern; Medical cot, med crates and bandages on a clinical patch; Event scorch with scattered papers and shards; Start arrival kit and footprints; Loot/Treasure pried crates and straw (Treasure adds a painted vault frame); Combat casings, planks and scorch; Boss heavy cracks, scorch and rubble. Rules: no collider, light, tile or marker change; the floor texture sits on Ground above the floor tiles and the wall texture on LowProps only over wall cells, so nothing covers an actor, projectile, telegraph or loot; opaque clutter stays within two steps of a wall and never on door clearance, a marker or its neighbours, a hazard, a FloorDetail prop or a foreground cell; the central combat space gets only flat, low-alpha wear; hazard cells are never painted; no saturated red. Dressing a depth costs ~20 ms per small room and up to ~75 ms per boss arena; the textures are destroyed with the room.

**Dungeon surroundings (2026-10-02).** Everything the camera shows outside the rooms is the biome's surrounding structure, not filler. `DungeonSurroundings` sits over the `WorldSubstrate` underlay and is composed by the expedition scene for every depth. It has one calm ground material per biome, organic ground patches (Metro damp, Rustworks oil and slag, Labs moss carpets, Cryo frost sheets) and long spines autotiled across open ground: Metro paired rail tracks with derelict train cars on them, Rustworks paired pipe trunks, Labs cable ducts, Cryo coolant lines. Structures line those spines (cable boxes and booths; presses, beam stacks and furnaces; server rows, partitions and benches; freezers, compressors and pallets). Open stretches are organised as yards: a pillared station hall, a tank farm, a lab bay, cold-store rack aisles. Sparse debris fills the rest. Every room sits in a one-cell moat of its own stepped cast shadow, kept free of structures, so the room walls stay the crispest edge on screen. The surroundings are a step below the rooms in value: outside framings measure under 0.75 of the room brightness, about 0.3–0.5 in practice. Presentation only: Ground layer between the underlay (-1000) and the floors (0), no collider, nothing inside a room rect. The layout is a pure function of run seed, depth, biome and room rects, so it is identical on every peer, and the biome kit is painted once per session.

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

### Cryo Vaults

**Mood:** an abandoned underground preservation and cold-storage facility whose automated systems still partly run.

Use:
- dark steel deck plates and insulated ribbed wall panels with frozen pipe bands and a frost crust;
- ice blue, pale cyan and white frost as the cold family; frost patches, fractured ice and condensation on the floor;
- cryo containers with frosted glass and small status lights, refrigeration vents, drain grating;
- sparse amber/red emergency lighting as the only warm accent;
- never a bright snow field: the frost sits on dark steel, and value stays low enough that actors read first.

## 8. Safehouse / Shelter / Transit

The Safehouse should feel like the one place people have forced into relative safety:

- patched but maintained;
- warmer and more inhabited than dungeons;
- salvaged furniture, storage, workshop equipment, transit machinery, practical lighting;
- still visibly built from old infrastructure rather than a pristine headquarters.

The expedition transit car should feel heavy, mechanical and dependable, with enough wear to belong in the setting.

**Front-end places (2026-10-02).** The Main Menu and Shelter backdrops are rendered as real lit places from one shared perspective camera each (`FrontEndScenes`). Both are drawn by the editor art generator (`ShelterSceneFactory` on `FrontEndSceneRenderer`) with procedural materials, practical lamps that cast shadows, distance fog and banded pixel-art light. The Main Menu is the old transit tunnel: tiled walls, ribs, rails, crates and drums, a derailed handcar, and the Shelter door glowing at the end. The Shelter is the room itself: the transit door, a storage shelving run, the workbench with tool board and terminal, the generator and its exhaust stack, water drums, a canvas rug, and three hanging practicals. `FrontEndAmbience` animates the same spots at runtime: one unreliable lamp, the breathing door indicator, dust in lamp light, a ceiling drip, a scrolling terminal, and vapour from the exhaust. Every element is small and dim, sits under all UI, takes no input and runs on unscaled time. Focal content stays behind the centre, and the outer thirds are darker and vignetted so the menu column and hub side columns stay readable.

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
- Cryo Vaults: cold blue-white ambient over dark steel, amber/red emergency accents only.
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
