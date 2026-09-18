# RUINRAIL — FINAL ART PRODUCTION SPEC
## Version 2.0 — Release Art Source of Truth

**Status:** APPROVED PRODUCTION SPEC  
**Purpose:** This document is the binding visual-production contract for RUINRAIL V1.  
**Scope:** All final character art, weapons, items, biomes, Shelter, world objects, UI, VFX, lighting, animation source sheets and Unity import rules.

This document replaces vague mood-only instructions. A file existing in the project is not enough to count as final art. Every release-bound visual must satisfy the technical and stylistic acceptance rules in this document.

---

# 1. VISUAL IDENTITY

RUINRAIL is an original top-down 2D pixel-art extraction roguelite.

Its visual identity is built from four reference principles without copying any protected asset, character, UI, composition, prop, weapon or tile from another game.

### Construction and readability
Use the **compact, simplified, highly readable top-down sprite construction associated with arcade pixel action games such as Soul Knight**:
- strong silhouette first;
- simplified anatomy;
- reduced internal noise;
- large readable equipment shapes;
- readable at 640×360;
- body and held weapon visually separated;
- enemies identifiable before details are read.

### Pixel-world character
Use the **gritty survival-world feeling associated with games such as ZERO Sievert**:
- harsh, worn surfaces;
- dense but controlled environmental storytelling;
- scavenged materials;
- grime, rust, stains and repairs;
- grounded industrial props;
- survival/extraction mood.

### Atmosphere
Use **Fallout-like post-apocalyptic atmosphere as the strongest thematic influence**:
- decay;
- concrete;
- rust;
- patched machinery;
- retro-industrial technology;
- faded signage;
- improvised repairs;
- melancholy;
- old-world infrastructure repurposed for survival.

Do not reproduce specific Fallout objects, armor, logos, terminals, vault motifs, typography or recognizable designs.

### Secondary technology influence
Use **ARC Raiders-like industrial sci-fi sophistication as a secondary influence**:
- selective clean high-tech modules;
- readable emitters, screens and powered devices;
- compact futuristic mechanisms;
- controlled bright accents against a dirty world.

The world as a whole must remain dirty and post-apocalyptic rather than glossy sci-fi.

### Final formula
**Simple readable sprite geometry + gritty pixel materials + heavy post-apocalypse + selective modern industrial sci-fi.**

---

# 2. GLOBAL TECHNICAL ART RULES

## 2.1 Reference rendering
- Reference gameplay resolution: **640×360**
- World pixels per unit: **32 PPU**
- Tile grid: **32×32 px**
- Filtering: **Point / nearest**
- Texture compression: **None** for final pixel sprites unless platform profiling proves a visually lossless exception
- Mip maps: **Off** for ordinary pixel sprites
- Anti-aliasing inside sprites: **Forbidden**
- Fractional sprite scaling during normal gameplay: **Forbidden**
- Sprite transform rotation is allowed for the separate WeaponPivot.
- Camera/presentation must preserve hard pixel edges.

## 2.2 Pixel discipline
Every final sprite must obey:
- no blurred edges;
- no semitransparent anti-alias fringe;
- no one-pixel random noise used only to simulate detail;
- intentional clusters rather than checkerboard speckle;
- isolated single pixels only for highlights, sparks, indicator lights or similarly intentional micro-features;
- large shapes must read before texture.

### Minimum cluster rule
For a gameplay entity larger than 24 px:
- primary mass should be built from clusters generally **2–6 px wide**;
- 1 px accents should not dominate the surface;
- repeated microtexture must not reduce silhouette clarity.

## 2.3 Outline rule
Use a **selective exterior dark outline**, not a universal comic-book outline.

- Exposed silhouette edge: usually 1 px dark edge.
- Internal forms: separate primarily by value/color, not black lines everywhere.
- Bright emissive effects may break the outline.
- Ground-contact edge may be softened by shadow rather than outlined.
- Do not use pure black for every contour. Preferred darkest structural colors are colored near-blacks.

Recommended global darks:
- `#111518` charcoal
- `#161A1C` warm charcoal
- `#10181A` green-black
- `#1B1715` rust-black

## 2.4 Light direction
Canonical sprite lighting:
- key light from **upper-left / north-west**;
- soft ambient fill;
- highlights biased to upper/left-facing planes;
- lower/right planes darker.

Environment lights may locally override hue, but not structural readability.

## 2.5 Value ramps
Per major material, normally use:
- 1 shadow
- 1 base
- 1 light
- optional 1 highlight

Maximum typical ramp: **4 values**.
Very small icons may use 2–3.
Bosses and large props may use 5 only where necessary.

Do not create smooth gradient ramps.

## 2.6 Saturation
Most of the world is desaturated.
High saturation is reserved for:
- hazards;
- active technology;
- rarity emphasis;
- important pickups;
- boss/elite signals;
- telegraphs;
- health/healing;
- selected biome identity accents.

## 2.7 Shadowing
Characters/world props:
- use a simple grounded blob/ellipse shadow if existing presentation supports it;
- shadow must not be mistaken for collision;
- opacity/brightness should remain consistent across character families.

No realistic soft raster shadows are painted into ordinary sprites.

---

# 3. GLOBAL PALETTE

The palette may expand where needed, but final assets should be derived from these families.

## 3.1 Neutral structure
- Near black: `#111518`
- Charcoal: `#1B2023`
- Dark steel: `#2B3235`
- Mid steel: `#4B5556`
- Pale steel: `#788181`
- Concrete shadow: `#343638`
- Concrete: `#555759`
- Concrete light: `#777873`
- Dust beige: `#9A8D73`

## 3.2 Rust / industrial warmth
- Burnt rust dark: `#4B261C`
- Rust: `#8E4327`
- Oxide orange: `#C06434`
- Warning ochre: `#D49A3A`
- Dirty yellow: `#C2A34B`

## 3.3 Survival greens
- Deep olive: `#26352A`
- Olive: `#425640`
- Canvas green: `#66715A`
- Sick green: `#798D54`
- Pale toxic: `#A4C75A`

## 3.4 Technology accents
- Terminal green: `#6FBF8C`
- Electric cyan: `#52A9A4`
- Cold blue: `#568FA2`
- Amber active: `#E7A74A`
- Emergency red: `#C84B42`

## 3.5 Health / rarity reserved accents
Use existing rarity contract. Art may present:
- Common: restrained neutral
- Uncommon: green-family accent
- Rare: blue/cyan-family accent
- Epic: violet-family accent
- Legendary: amber/orange-gold family accent

Do not recolor gameplay sprites entirely by rarity. Rarity belongs mainly in icon frames, glows and UI.

---

# 4. PERSPECTIVE AND GEOMETRY

## 4.1 Character perspective
Use a **top-down three-quarter arcade perspective**:
- top of head/shoulders visible;
- torso readable;
- feet visible enough to anchor motion;
- not side-view;
- not isometric;
- not realistic overhead.

The player and humanoid enemies must share one canonical perspective.

### Direction rules
Eight facings:
- N
- NE
- E
- SE
- S
- SW
- W
- NW

Mirror use is allowed only when:
- asymmetrical armor/weapons are corrected;
- readable handedness is not broken;
- silhouette still matches the design.

## 4.2 Player scale
Approved visual canvas target: **32×48 px**.

Recommended occupied region in neutral stance:
- width: 18–26 px
- height: 34–44 px

The canvas may contain empty pixels for animation.

### Player proportion
Approximate visual mass:
- head/helmet: 8–10 px high
- shoulders: 14–18 px wide
- torso: 12–16 px wide
- legs/boots: compact, readable separation
- hands simplified to 2–4 px masses

Do not use oversized chibi head proportions.
The player may be stylized, but should feel like a compact adult survivor.

## 4.3 Normal enemy scale
Humanoid normal enemies:
- similar or slightly larger/smaller than player;
- normally 28×40 to 36×52 source canvas;
- visual mass should communicate archetype.

Swarm may be substantially smaller.
Brute may be larger.

## 4.4 Elite scale
Typical elite visual mass:
- **1.15×–1.45×** normal equivalent;
- use stronger silhouette, armor mass, emissive identifiers and/or unique appendages;
- never simply scale a normal sprite without redesign.

## 4.5 Boss scale
Typical boss source canvas:
- minimum **48×48**
- often **64×64**, **80×80** or wider depending on architecture.

Boss silhouette must remain clear in the arena and must not obscure telegraphs unfairly.

---

# 5. CHARACTER ANIMATION STANDARD

The current runtime expects six animation-state roles × eight facings for each of 22 character families.

Do not interpret this as 1,056 independent drawings. Use reusable directional source sheets and generate/bind clips through the existing pipeline.

## 5.1 Canonical six visual states
Use the actual repository animation-state enum if names differ. Map to these concepts without changing gameplay:
1. Idle
2. Move
3. Attack / Action
4. Hit / React
5. Special / Utility / Cast
6. Death / Defeat

For the player, state mapping may include dash/use/downed according to the existing contract. Do not invent runtime states merely to match this document.

## 5.2 Recommended frame ranges
- Idle: 2–4 frames
- Move: 4–6 frames
- Attack: 3–6 frames depending on telegraph
- Hit: 1–3 frames
- Special: 3–8 frames
- Death: 4–8 frames

## 5.3 Frame rate
Default art playback target:
- **8–12 fps**
- idle may be slower;
- attack timing must match actual gameplay windup/active/recovery, not arbitrary animation timing.

## 5.4 Motion quality
- feet should visibly plant rather than glide;
- shoulders/torso may shift 1–2 px;
- head bob should be restrained;
- recoil should follow weapon/action direction;
- death should preserve entity identity until gameplay considers it dead;
- hit reaction must not hide attack telegraph state.

## 5.5 Telegraph principle
For any attack with an existing telegraph:
- animation must reinforce the telegraph;
- anticipation pose is visually distinct from active strike;
- do not use a bright effect only; silhouette itself should help.

---

# 6. PLAYER — FINAL DESIGN PROFILE

## Role
Player-controlled scavenger/explorer. Must be neutral enough to support gear-driven identity but visually distinctive enough to anchor the game.

## Geometry
- compact adult body;
- medium shoulder width;
- practical stance;
- head not oversized;
- backpack/utility silhouette visible but not enormous;
- held weapon is separate through WeaponPivot, so hands/arms should visually accommodate multiple weapon classes.

## Clothing
Base outfit:
- layered scavenger jacket or protective overshirt;
- utility harness;
- reinforced work trousers;
- heavy boots;
- fingerless/protective gloves;
- small survival pouches;
- respirator/scarf element may be present but must not hide all identity.

## Material balance
- cloth/canvas: dominant
- worn metal/plastic armor plates: secondary
- powered/high-tech elements: minor

## Palette
Dominant:
- charcoal
- olive
- dirty beige
- muted steel

Accent:
- one consistent muted amber/orange or terminal-green element for player recognition.

## Prohibited
- clean space marine armor
- fantasy armor
- giant anime hair
- heroic cape
- recognizable Fallout armor
- oversized Soul-Knight-like copied head/body ratio

---

# 7. NORMAL ENEMIES — 9 DESIGN PROFILES

All normal enemies must be identifiable in under one second by silhouette and posture.

## 7.1 Grunt
Function: basic close-range threat.

Visual:
- human scavenger/raider silhouette;
- stocky compact torso;
- improvised asymmetric chest/shoulder protection;
- cloth hood, mask or battered helmet;
- heavy boots;
- melee-first posture;
- one visible crude striking implement or arm silhouette appropriate to runtime.

Palette:
- dark brown
- dirty olive
- steel
- rusty orange identification accent

Readability:
- broad shoulders;
- forward aggressive lean;
- no long gun silhouette.

## 7.2 Shooter
Function: standard ranged pressure.

Visual:
- leaner than Grunt;
- clear firearm-ready posture;
- lighter armor;
- ammo band/pouch or tech sight;
- exposed forearms or narrower shoulders.

Palette:
- desaturated blue-gray
- dirty tan
- steel
- small amber/cyan ranged-tech accent

Readability:
- weapon-ready silhouette;
- straighter stance than Grunt.

## 7.3 Swarm
Function: fast small melee attacker.

Visual:
- clearly smaller;
- hunched or low center of gravity;
- mutated/feral silhouette or small scavenger-creature form consistent with current lore;
- oversized hands/claws only if repository concept supports it;
- high motion energy.

Palette:
- dark sick green
- gray-brown
- pale contamination accent

Readability:
- low, compact silhouette;
- never confused with human player.

## 7.4 Charger
Function: telegraphed line charge.

Visual:
- front-heavy silhouette;
- reinforced shoulders/head/chest;
- leg/boot mass emphasizing acceleration;
- striped/marked frontal armor to make facing obvious.

Palette:
- steel
- dark red-brown
- warning ochre

Readability:
- direction of charge obvious from pose;
- anticipation should compress body backward before launch.

## 7.5 Brute
Function: heavy melee + ground slam.

Visual:
- significantly larger than player;
- thick torso;
- oversized industrial protection;
- heavy forearms;
- large boots/anchors;
- scrap plate layering.

Palette:
- deep steel
- concrete gray
- rust
- warning yellow details

Readability:
- visually heavy;
- slam windup exaggerates raised arms/upper mass.

## 7.6 Bomber
Function: lobbed explosives.

Visual:
- backpack/satchel silhouette;
- visible canisters or grenades;
- lighter direct armor than Brute;
- one arm/action silhouette designed for throwing.

Palette:
- dirty tan
- olive
- oxide orange
- emergency-red explosive markings

Readability:
- explosive load visible;
- throw anticipation clear.

## 7.7 Shield Enemy
Function: frontal defense.

Visual:
- unmistakable large frontal shield;
- body partly hidden behind it;
- shield looks scavenged/industrial rather than medieval;
- rectangular/angled metal geometry.

Palette:
- dark steel
- worn paint
- warning stripe remnants

Readability:
- shield facing direction must be obvious in all eight facings.

## 7.8 Sniper Enemy
Function: long-range aim-line threat.

Visual:
- tall/narrow or crouched precision silhouette;
- long weapon profile;
- hood/visor/optic;
- minimal bulky armor.

Palette:
- desaturated gray-green
- dark cloth
- cold blue optic accent

Readability:
- long weapon / aiming posture immediately distinguishes it.

## 7.9 Summoner
Function: creates Swarms.

Visual:
- unusual tech backpack, antenna, canisters or biological apparatus;
- less direct-combat posture;
- strong hand/device casting silhouette.

Palette:
- dirty lab/industrial neutrals
- toxic green or terminal cyan emissive accent

Readability:
- unique backpack/device;
- cast state visually obvious.

---

# 8. ELITES — 6 DESIGN PROFILES

Elite visuals must preserve biome identity and use one distinct elite marker:
- stronger emissive accent;
- unique helmet/head;
- larger silhouette;
- unique mechanical/biological apparatus.

Do not merely recolor normal enemies.

## 8.1 Tunnel Stalker — Ruined Metro
Theme:
- ambush predator adapted to tunnels;
- agile, narrow, threatening silhouette;
- dark body with pale metal/bone-like edge shapes;
- low profile for Lunge/Tunnel Rush.

Palette:
- charcoal
- oxidized steel
- muted bone-gray
- dim red/amber eye or sensor accent

Signature:
- elongated forearm/blade/claw shape;
- cloak/torn tunnel fabric or cable-like details.

## 8.2 Railguard — Ruined Metro
Theme:
- armored old transit/security war machine or heavily armored guard silhouette;
- broad plated body;
- rail/industrial motifs without copying real transit branding.

Palette:
- steel
- faded transit blue/green
- warning yellow
- red emergency indicator

Signature:
- integrated cannon/weapon block;
- reinforced lower body for Ground Shock;
- readable sweep apparatus.

## 8.3 Scrap Executioner — Rustworks
Theme:
- brutal industrial execution-machine/scavenger;
- asymmetric heavy tool/weapon silhouette;
- chains/cables kept visually controlled.

Palette:
- soot black
- rust
- furnace orange
- dirty steel

Signature:
- large execution/tool arm;
- face obscured by industrial mask.

## 8.4 Crusher Unit — Rustworks
Theme:
- compact industrial crushing machine;
- very broad shoulders/arms;
- hydraulic pistons or reinforced forearms.

Palette:
- dark steel
- worn yellow paint
- oxide red
- hot orange active elements

Signature:
- crushing arm geometry;
- piston/ram motion visible during attacks.

## 8.5 Mutated Brute — Overgrown Labs
Theme:
- failed biological/chemical experiment;
- heavy but organic asymmetry;
- lab restraint remnants;
- growths readable as large clusters, not noisy gore.

Palette:
- gray flesh/cloth
- deep olive
- toxic yellow-green
- lab-white remnants

Signature:
- one overgrown arm/shoulder;
- glowing contaminated tissue or canister.

## 8.6 Prototype X-7 — Overgrown Labs
Theme:
- advanced experimental combat prototype;
- cleaner than the rest of the world but visibly damaged/abandoned;
- ARC-like industrial sci-fi influence strongest here.

Palette:
- pale ceramic/steel
- charcoal joints
- cyan/green energy
- restrained warning orange

Signature:
- angular experimental armor;
- compact energy emitters;
- Blink Shot state uses strong positional glow cues.

---

# 9. BOSSES — 6 DESIGN PROFILES

Bosses must be immediately unique, biome-specific and readable at gameplay distance.

## 9.1 The Conductor — Ruined Metro
Identity:
- corrupted transit-control / armored rail-command figure or machine;
- central metro authority visual language;
- commanding vertical silhouette.

Design:
- conductor-like coat/armor language abstracted into industrial plates;
- integrated rail signal lights;
- one prominent ranged cannon mechanism;
- emergency-red and warning-yellow signaling.

Phase-2 visual:
- stronger rail-line light activation;
- exposed powered elements;
- no full palette swap.

## 9.2 Tunnel Maw — Ruined Metro
Identity:
- massive tunnel-adapted creature/machine that visually belongs to collapsed underground infrastructure.

Design:
- wide low silhouette;
- mouth/intake/front aperture as central read;
- concrete/metal debris integrated sparingly;
- rail/cable motifs.

Palette:
- tunnel charcoal
- concrete gray
- rust
- sick emergency green/amber

## 9.3 Foundry Titan — Rustworks
Identity:
- enormous industrial forge machine.

Design:
- tall/heavy mass;
- furnace core;
- segmented armor;
- pipes/hydraulics;
- glowing heat vents.

Palette:
- blackened steel
- rust
- orange
- white-hot highlights used only in active core/attack states.

## 9.4 Scrap King — Rustworks
Identity:
- ruler built from accumulated scavenged machinery and trophies.

Design:
- irregular crown-like antenna/metal silhouette;
- layered mismatched plates;
- more personality than Foundry Titan;
- powerful but improvised.

Palette:
- many muted scrap neutrals unified by rust/ochre;
- controlled red/amber power accents.

## 9.5 Aegis Core — Overgrown Labs
Identity:
- defensive research-facility core / autonomous protection system.

Design:
- geometric armored central mass;
- shield-emitter shapes;
- cleaner pale surfaces cracked/overgrown;
- readable energy core.

Palette:
- pale lab gray
- charcoal
- cyan/green shield energy
- dark vegetation intrusion

## 9.6 Subject Omega — Overgrown Labs
Identity:
- ultimate biological/experimental lab subject.

Design:
- large asymmetric organic silhouette;
- restraint hardware and scientific implants;
- not generic zombie;
- mutation shaped by lab technology.

Palette:
- desaturated flesh/gray
- lab white
- dark olive
- toxic green
- limited red biological accent

Phase-2:
- expanded growth/energy state while keeping silhouette trackable.

---

# 10. WEAPON ART — GLOBAL RULES

All 33 weapon sprites:
- authored pointing to **+X / right**;
- pivot/grip compatible with existing WeaponPivot;
- no baked perspective rotation;
- muzzle point obvious;
- class silhouette clear at gameplay scale;
- no anti-aliasing;
- normally 1 px selective outline;
- 3–5 material colors plus highlights;
- Legendary variant visually distinct but same class family.

Suggested source canvas:
- pistols: 20–28 × 10–16
- SMG: 24–32 × 10–16
- AR/BR: 28–38 × 10–16
- shotgun: 30–40 × 10–16
- sniper: 36–48 × 8–14
- rocket: 32–46 × 12–18
- bow: 28–38 × 24–38
- blaster: 26–38 × 12–20
- knife: 18–26 × 8–14
- spear: 36–52 × 6–12

Do not force every weapon to exactly these canvas bounds if the current pivot contract requires more padding.

---

# 11. 33 WEAPON DESIGN PROFILES

## Pistols
### P9 Ranger
Baseline reliable service pistol.
- simple squared slide;
- worn dark steel;
- polymer/wood-brown grip;
- tiny amber sight;
- visually plain, dependable.

### Kestrel-12
Faster/light pistol.
- slimmer slide;
- slightly longer barrel vents;
- lighter frame;
- cool steel/gray.

### Quickfang — Legendary
Aggressive fast pistol.
- compact angular slide;
- visible heat/rapid-fire ports;
- stronger orange/amber accent;
- silhouette still clearly pistol.

## SMGs
### Rattler-9
Compact scavenger SMG.
- box magazine;
- short barrel;
- wrapped/grippy handguard.

### Wasp-45
Heavier-caliber SMG.
- chunkier receiver;
- heavier muzzle;
- yellow/black worn warning detail.

### Buzzsaw — Legendary
Extreme-rate SMG.
- ventilated barrel shroud;
- oversized magazine/drum visual;
- hot orange cooling accents.

## Assault Rifles
### AR-17
Baseline modular rifle.
- balanced receiver/barrel;
- utilitarian stock;
- minimal sci-fi.

### Marauder A2
Heavier, slower rifle.
- reinforced receiver;
- thicker barrel;
- scavenged rail/plate additions.

### Vanguard — Legendary
Advanced overrun rifle.
- cleaner industrial geometry;
- compact powered module;
- cyan/amber status light.

## Battle Rifles
### Sentinel
Longer, deliberate rifle.
- long receiver;
- clear marksman silhouette;
- heavy stock.

### Hound
More aggressive battle rifle.
- shorter chunky barrel;
- forward grip;
- worn brown/steel.

### Judicator — Legendary
Powerful precision BR.
- strong long-line silhouette;
- reinforced barrel spine;
- bright charged-line indicator.

## Shotguns
### Breacher
Short brutal pump/industrial shotgun.
- thick barrel;
- blocky receiver;
- battered steel.

### Scatter
Higher-capacity shotgun.
- longer magazine/tube or box;
- wider handguard.

### Crowdbreaker — Legendary
Concussion-focused shotgun.
- oversized muzzle device;
- strong warning-yellow/orange ring;
- heavy front silhouette.

## Snipers
### Longshot
Traditional rugged long rifle.
- long thin barrel;
- clear optic;
- dark neutral palette.

### Needle
Lighter/faster precision rifle.
- slimmer receiver;
- smaller optic;
- pale steel/cyan tiny accents.

### Farline — Legendary
Rail-like precision sniper.
- clean linear barrel;
- visible charged spine;
- cyan/green energy strip.

## Rocket Launchers
### Pipe
Improvised single-shot launcher.
- rough tube;
- crude sights;
- clamps/tape/metal bands.

### Twin-Tube
Two-shot launcher.
- unmistakable twin barrel/tube structure.

### Sunbreaker — Legendary
High-impact launcher.
- reinforced body;
- hot amber/orange core;
- warning geometry;
- not fantasy ornate.

## Bows
### Recurve
Simple scavenged composite bow.
- clear curved limbs;
- wrapped grip;
- muted wood/composite colors.

### Compound
Mechanical pulley/cam silhouette.
- stronger industrial look;
- cables simplified for readability.

### Stormstring — Legendary
Advanced energetic bow.
- angular reinforced limbs;
- subtle charged cyan/green string emitters;
- fan-shot identity suggested by branching detail.

## Blasters
### Pulse B1
Basic energy weapon.
- compact industrial body;
- visible heat/energy cell;
- cyan/green emitter.

### Arc B4
Heavier blaster.
- larger coil housing;
- segmented energy path.

### Redline — Legendary
High-output blaster.
- aggressive venting;
- warning-red/amber heat strips;
- overheated state visually obvious.

## Knives
### Field Knife
Practical survival knife.
- plain steel blade;
- dark wrapped handle;
- compact.

### Ripper
Fast serrated knife.
- saw/serrated silhouette;
- lighter frame.

### Ghostedge — Legendary
Blink-strike knife.
- clean dark blade;
- small cold-cyan edge/emitter;
- restrained high-tech feel.

## Spears
### Scrap Spear
Improvised long spear.
- pipe/rod shaft;
- bolted scrap-metal head.

### Guard Lance
More engineered long thrust weapon.
- reinforced shaft;
- compact industrial guard;
- pale steel.

### Railspike — Legendary
Heavy impaling spear.
- rail-spike-inspired head;
- powered charge line;
- amber/cyan active strip.

---

# 12. ITEM ICON RULES — 72 ICONS

All item icons:
- square source;
- recommended 24×24 or 32×32 depending existing UI;
- 2–4 px internal padding;
- transparent background;
- object occupies ~70–85% of usable area;
- icon silhouette must remain readable without tooltip;
- rarity is NOT painted permanently into the item sprite; rarity frame/glow belongs to UI.

## Weapons
Use simplified version of weapon sprite, oriented diagonally or horizontally consistently according to UI contract.

## Armor — 9
Each icon should emphasize armor silhouette rather than full character:
- chest/vest/plate form;
- clear material class;
- Legendary passive variants use a small controlled emissive motif if appropriate.

## Accessories — 16
Each must use a unique object metaphor:
examples may include modules, charms, lenses, batteries, injectors, sensor parts, reinforced components, depending on actual accessory definitions.
Do not invent a visual that contradicts the accessory's existing name/function.

## Consumables — 10
Use clear container silhouettes:
- med/injector/canister/grenade/device/etc. according to actual definition.

## Ammo — 4
Exactly:
- Light
- Medium
- Heavy
- Shells

Each icon must differ by cartridge/shell geometry and color cue while remaining readable at small size.

---

# 13. BIOME 1 — RUINED METRO

## Visual thesis
Abandoned underground transport infrastructure repurposed and decayed over years.

### Target visual weighting
Approximate screen material balance in ordinary rooms:
- 55–70% concrete/dirty floor neutrals
- 10–20% rail/steel
- 5–12% rust/warm damage
- 3–8% warning paint/signage
- 2–6% active emergency/tech lighting
- remaining percentage props/debris

Do not turn every tile into noisy debris.

## Floor
32×32 modules:
- poured concrete;
- cracked concrete;
- old platform tile;
- maintenance panels;
- drainage seams.

Floor texture:
- broad 4–10 px forms;
- sparse 1 px grime;
- no dense speckle.

## Floor detail
- faded arrows;
- maintenance numbers;
- grime patches;
- cable channels;
- old safety-line fragments.

## Walls
- concrete retaining walls;
- tiled station walls;
- steel service panels;
- collapsed sections.

Wall height must remain consistent across room set.

## Obstacles
- benches;
- ticket/service machines;
- collapsed barriers;
- luggage/debris clusters;
- maintenance carts;
- electrical boxes.

## Hazards
- electrified rail/panel;
- steam/leak;
- unstable debris;
- exposed power.

Hazard color must be clearly separate from decorative lights.

## Doors
- industrial sliding/service door;
- strong rectangular silhouette;
- powered indicator;
- open/closed state obvious.

## Rail / transit
- tracks;
- sleepers;
- platform edges;
- signal boxes;
- cable conduits;
- transit car materials.

## Lighting
Base:
- cool gray/green ambient
Accent:
- amber emergency
- rare red emergency
- terminal green

Avoid pure horror darkness. Gameplay remains readable.

---

# 14. BIOME 2 — RUSTWORKS

## Visual thesis
Heavy industrial production zone: furnaces, scrap processing, machinery and heat.

### Screen balance
- 40–55% dark steel/soot
- 15–25% worn metal/rust
- 5–12% hot orange/amber machinery
- 5–10% warning paint
- remaining pipes/props/floor variation

## Floor
- steel plate;
- diamond plate simplified;
- soot-stained concrete;
- grates.

## Detail
- oil stains;
- heat discoloration;
- bolt lines;
- scratched route arrows.

## Walls
- steel panels;
- furnace brick;
- reinforced structures;
- pipe walls.

## Obstacles
- presses;
- scrap bins;
- conveyor machinery;
- furnace units;
- pipe manifolds.

## Hazards
- molten/hot surface;
- flame jet;
- pressure vent;
- active machinery.

## Doors
- heavy sliding industrial door;
- thicker than Metro;
- warning stripes used sparingly.

## Lighting
Base:
- warm dark neutral
Accent:
- furnace orange
- amber
- occasional red

Avoid making the entire screen orange. Neutral darks must dominate.

---

# 15. BIOME 3 — OVERGROWN LABS

## Visual thesis
Abandoned research facility overtaken by biological growth and contamination.

### Screen balance
- 35–50% pale/dark lab surfaces
- 15–25% vegetation
- 5–12% toxic/energy green
- 5–10% dark structural damage
- remaining machinery/props

## Floor
- pale laboratory tile;
- polymer panel;
- cracked clean-room floor;
- dark maintenance flooring.

## Detail
- broken labels;
- stains;
- roots/vines crossing seams;
- hazard symbols kept generic/original.

## Walls
- pale composite;
- observation panels;
- dark structural gaps;
- vegetation breakthrough.

## Obstacles
- lab benches;
- broken containment;
- server/research racks;
- planters/growth;
- fallen equipment.

## Hazards
- contamination pool;
- toxic gas source;
- unstable experiment;
- electrical lab discharge.

## Doors
- research/security doors;
- cleaner geometry than other biomes;
- damaged seals;
- small powered panel.

## Lighting
Base:
- cold pale/cyan gray
Accent:
- toxic green
- cold blue
- rare warning amber/red

Vegetation should not hide traversable floor boundaries.

---

# 16. THE SHELTER / SAFEHOUSE

## Visual thesis
The safest location in RUINRAIL, but still improvised, repaired and resource-limited.

It should feel warmer and more human than dungeons.

## Palette
- charcoal
- worn steel
- warm beige
- canvas green
- amber practical lights
- restrained terminal green/cyan

## Architecture
- reinforced underground room;
- salvaged walls and bulkheads;
- patched cables;
- storage crates;
- work surfaces;
- practical lighting;
- hand-painted or stenciled original signage.

## Stations
Each station must have unique silhouette and iconography:

### Storage
- lockers/crates/shelving
- strong rectangular storage read

### Loadout
- weapon rack / preparation bench
- clear interaction zone

### Trader
- counter/display
- salvaged goods
- distinct lamp/sign

### Character
- terminal/mirror/diagnostic station depending current implementation
- identity/progression read

### Workshop
- tools
- bench
- parts
- powered machinery

### Multiplayer Terminal
- cleaner high-tech terminal
- connection/signal visual language
- strongest ARC-like tech influence in Shelter

### Expedition Transit
- heavy transit entrance/car platform
- strongest connection to Metro/rail identity
- clear departure focal point

---

# 17. WORLD OBJECTS

## Supply Chest
- rugged industrial crate;
- clear closed/open states;
- latch/emissive strip;
- rarity glow may surround but not recolor the entire chest.

## Coin Pickup
- not fantasy gold coin styling;
- use RUINRAIL currency token/metal marker consistent with existing economy UI;
- small warm highlight for visibility.

## Item Pickup
- ground item silhouette + controlled glow;
- shared-loot readability;
- no giant beam unless existing VFX contract uses one.

## Dungeon Merchant
- portable scavenger trading station or character/terminal according to current runtime;
- visually distinct from Shelter Trader.

## Medical Station
- clear medical cross/health language must be original and readable;
- pale panel + green/white medical accent;
- worn industrial housing.

## Transit Car
- armored underground transport;
- old transit shell reinforced with salvage;
- doors, windows/panels, hazard markings;
- should visually embody the RUINRAIL name.

## Dungeon event objects — 6
Create visual object family based on actual event definitions:
- Locked Vault
- Cursed Chest
- Broken Machine
- Supply Signal
- Medical Station
- Weapon Cache

Do not rename or alter mechanics.

---

# 18. UI ART SYSTEM

## 18.1 UI thesis
Post-apocalyptic industrial terminal UI:
- dark metal/charcoal;
- thin warm rust/amber accents;
- occasional terminal green/cyan;
- pixel-crisp;
- modern enough to read quickly;
- not a direct imitation of Fallout terminal UI or ARC Raiders interface.

## 18.2 Grid
Base spacing unit: **4 px**.
Common padding:
- 4
- 8
- 12
- 16

Avoid arbitrary 3/7/11 px spacing unless optical correction is required.

## 18.3 Borders
- normal panel border: 1–2 px
- emphasized panel: 2–3 px
- corners mostly squared or clipped/chamfered
- no glossy rounded mobile-app cards

## 18.4 Buttons
Recommended target height:
- 24–32 px at 640×360 depending screen

States:
- normal
- hover/focus
- pressed
- disabled

Focus state must be controller-visible without relying only on color.

## 18.5 Inventory slots
- square;
- readable rarity frame;
- 2–4 px inner padding;
- selected state does not obscure icon.

## 18.6 HUD
Must prioritize:
1. health
2. ammo/weapon
3. consumable
4. interaction prompt
5. co-op status
6. depth/expedition context

Decorative frame weight must be low.

## 18.7 Typography
Use a legally safe pixel-compatible font.

Rules:
- headings may be stylized;
- body/tooltips must remain easy to read;
- uppercase reserved for major commands/status, not every paragraph;
- numbers must be unambiguous;
- `1`, `I`, `0`, `O` should be clearly differentiated if possible.

Suggested reference sizes at 640×360:
- micro labels: 6–8 px effective
- body: 8–10 px
- subhead: 10–12 px
- heading: 14–18 px
- title: 20+ px as screen permits

Use actual font metrics and existing TextFit tests rather than blindly enforcing these numbers.

## 18.8 Glyphs
Provide both keyboard/mouse and controller glyphs for current actions:
- Move
- Aim
- Fire
- Special
- Dash
- Reload
- Interact
- Weapon1
- Weapon2
- WeaponSwap
- Consumable
- Inventory
- Pause

Glyph silhouette must remain recognizable at smallest rendered size.

---

# 19. VFX ART SYSTEM

VFX must communicate mechanics before spectacle.

## Global rules
- hard pixel edges;
- limited frame count;
- no smooth particle gradients unless converted to deliberate pixel clusters;
- do not cover telegraphs;
- screen flash duration restrained;
- Legendary effects may be larger but must not obscure hazards.

## 19.1 Muzzle flash
- 2–4 frames
- warm white/yellow core
- orange edge
- size scales with weapon class

## 19.2 Projectile impact
- 2–5 frames
- material-independent core version plus optional surface tint
- compact

## 19.3 Explosion
- 5–8 frames
- clear radius read
- hot center -> orange -> smoke/debris
- rocket vs grenade may reuse family but scale differently

## 19.4 Melee arc
- 3–5 frames
- directional
- does not imply larger hitbox than real mechanics

## 19.5 Stagger
- brief star/shock/impact motif
- not cartoon comedy
- readable above enemy

## 19.6 Heal
- green/white upward energy or medical particles
- avoid fantasy magic aesthetic

## 19.7 Status
- icon/particle family per actual status
- minimal persistent screen noise

## 19.8 Loot glow
- rarity-aware;
- restrained;
- Legendary strongest;
- ground item itself remains visible.

## 19.9 Telegraph zone
- high contrast ring/fill;
- transparent center where possible;
- edge more important than fill.

## 19.10 Telegraph dash/charge
- directional corridor/arrow;
- source-to-target path obvious.

## 19.11 Telegraph projectile/aim
- thin high-contrast line;
- endpoint/source readable;
- Sniper line must remain visible in every biome.

## 19.12 Slam
- radius ring + ground warning;
- clear timing stages.

## 19.13 Generic elite/boss phase effect
Use current manifest role if present:
- controlled strong emissive pulse;
- not full-screen blindness.

---

# 20. BIOME LIGHTING

Lighting is part of gameplay readability.

## Ruined Metro
- ambient tint: cool gray-green
- main practical lights: sick fluorescent / faded warm fixtures
- emergency accent: amber/red
- floor never crushed into black

## Rustworks
- ambient tint: charcoal/warm steel
- local heat: orange
- machinery indicators: amber/red
- reserve pure bright orange for heat/hazards

## Overgrown Labs
- ambient tint: cold pale cyan-gray
- contamination: green
- research tech: cyan
- warning: amber/red

## Contrast requirement
At target resolution, important entities must retain silhouette separation from the floor/background.

Telegraph luminance difference must be objectively measurable and visually distinct under each lighting profile.

---

# 21. UNITY IMPORT RULES

For ordinary pixel sprite textures:
- Texture Type: Sprite (2D and UI)
- Sprite Mode: Single or Multiple as appropriate
- Pixels Per Unit: 32 unless UI/source role explicitly requires another established value
- Filter Mode: Point
- Compression: None
- Generate Mip Maps: Off
- Alpha Is Transparency: On where applicable
- Wrap Mode: Clamp for isolated sprites/UI; tile textures follow existing tile pipeline
- Mesh Type: Full Rect unless existing batching/packing requirements prove otherwise

Character sheets:
- slicing grid/pivots must be deterministic;
- same frame dimensions across a character family;
- pivot kept consistent, normally feet/body anchor per existing animation driver.

Weapon sprites:
- pivot/grip point aligned with current WeaponPivot contract;
- +X direction;
- muzzle metadata/reference must remain correct.

UI sprites:
- use sliced/9-sliced regions only when pixel borders remain integer aligned.

Do not silently change PPU/pivot per asset to make alignment "look close."

---

# 22. FILE NAMING

Use existing repository naming conventions when present.

Otherwise:
- Characters: `chr_<id>_<state>_<direction>_<frame>`
- Weapon world sprite: `wpn_<weapon_id>`
- Item icon: `ico_<item_id>`
- Tile: `tile_<biome>_<role>_<variant>`
- Prop: `prop_<biome_or_base>_<id>`
- VFX: `vfx_<role>_<frame>`
- UI: `ui_<screen_or_component>_<role>`
- Glyph: `glyph_<device>_<action>`

IDs must map to stable gameplay IDs, not display names where repository convention differs.

---

# 23. FINAL-ART VS FALLBACK-ART RULE

Every generated/imported visual gets one conceptual status:

### `PLACEHOLDER`
Temporary existing debug/prototype art.
Must not ship.

### `PIPELINE_FALLBACK`
Automatically/programmatically produced asset used to prove the integration path.
May be used temporarily.
Does NOT count as final merely because validators can load it.

### `FINAL_ART`
May ship only if it passes all relevant acceptance rules below.

A validator must not simply test `sprite != null`.
Where practical, final manifests should distinguish placeholder/fallback/final provenance.

If the environment cannot create art that satisfies the FINAL_ART acceptance bar, report the role as incomplete rather than dishonestly promoting a colored rectangle.

---

# 24. FINAL ART ACCEPTANCE GATES

## 24.1 Sprite acceptance
A character/weapon/object sprite passes only if:
- transparent background where expected;
- point-filter compatible;
- no anti-alias fringe;
- correct PPU/pivot;
- correct perspective;
- silhouette identifiable at gameplay scale;
- uses approved visual language;
- not a copied third-party asset;
- no obvious placeholder geometry;
- material shading follows global rules;
- palette is controlled;
- collision/gameplay shape not misleading.

## 24.2 Character family acceptance
Each of 22 families:
- all required directions;
- all required runtime animation-state roles;
- consistent body scale;
- state transitions do not jump pivots;
- attack anticipation readable;
- hit/death readable;
- no missing/invisible frames.

## 24.3 Weapon acceptance
Each of 33:
- unique readable silhouette;
- +X;
- grip/pivot correct;
- muzzle alignment correct;
- class family coherent;
- Legendary distinct;
- no gameplay hitbox/stat change.

## 24.4 Icon acceptance
72/72:
- readable at final slot size;
- no cropped silhouette;
- no rarity baked into object;
- actual ItemDefinition binding exists;
- tooltip/item ID matches.

## 24.5 Biome acceptance
Each biome:
- floor/wall/obstacle/hazard visually distinguishable;
- hazard cannot be mistaken for decoration;
- props do not hide navigable corridors;
- door states obvious;
- tiles seam correctly;
- biome identity recognizable from screenshot with UI hidden;
- no placeholder tile references in release rooms.

## 24.6 UI acceptance
- all supported screens skinned;
- keyboard and controller focus readable;
- font replaced;
- no text clipping at reference resolution;
- rarity readable without relying solely on tiny text;
- critical HUD data readable during combat;
- no missing icons.

## 24.7 VFX acceptance
- all current roles present;
- telegraph size corresponds to mechanics;
- telegraphs visible in all three biome lighting profiles;
- no white-square placeholder;
- effects do not obscure player/enemy for excessive duration.

---

# 25. VISUAL SELF-REVIEW CHECKLIST

For automated approval, render/capture representative screenshots if tooling permits:
1. Player + P9 vs Grunt in Ruined Metro
2. Shooter/Sniper telegraphs in Metro
3. Brute/Bomber/Shield encounter
4. one Ruined Metro Elite + Boss
5. Rustworks normal encounter
6. Rustworks Elite + Boss
7. Overgrown Labs normal encounter
8. Labs Elite + Boss
9. Shelter with all stations
10. inventory with mixed rarity/icons
11. HUD combat
12. transit vote
13. downed/revive
14. Legendary drop
15. each biome with telegraph overlay

Objective checks:
- player identifiable in every screenshot;
- enemy archetypes visually distinct;
- hazards visible;
- UI does not cover critical battlefield;
- no debug/magenta/missing texture;
- no flat-color prototype room.

If image inspection is unavailable to the coding agent, do not fabricate visual judgment. Run structural validators and clearly report that visual inspection remains an external dependency.

---

# 26. CONTENT COUNTS — FINAL VISUAL TARGETS

Release visual requirements:
- 22 character visual families
- 1,056 runtime animation clip roles resolved
- 33 weapon world sprites
- 72 item icons
- 15 core biome tile roles
- 18 biome dressing role families
- 3 biome lighting looks
- all world-object manifest roles
- all base-station visuals
- 16 UI/font/glyph role families or current manifest equivalent
- 13 VFX/telegraph role families or current manifest equivalent
- 0 release-bound placeholder tile/sprite/VFX/font references

Repository manifest truth wins if exact support-role counts have since been refined, but approved gameplay/content counts must not change.

---

# 27. AUDIO VISUAL COHERENCE NOTES

Although audio is specified elsewhere, visual/audio pairing should follow:
- heavy weapons -> heavier recoil/flash and heavier transient audio;
- blasters -> energy color family + heat-state sound;
- Metro -> hollow mechanical ambience;
- Rustworks -> machinery/heat ambience;
- Labs -> ventilation/electronics/organic contamination ambience;
- boss phase visual cue must coincide with boss audio cue;
- Legendary drop glow must coincide with Legendary stinger.

---

# 28. PROHIBITED VISUAL OUTCOMES

Reject art that is:
- generic flat colored rectangles;
- programmer art promoted to final;
- smooth vector/cartoon art with pixel filter;
- hyper-detailed noisy pixel art unreadable at 640×360;
- high-resolution painting downscaled into pixels;
- direct imitation of a reference game's asset;
- fantasy-medieval unless specifically required by an existing item silhouette;
- clean glossy sci-fi across the whole world;
- monochrome darkness that hides combat;
- cute/chibi character design inconsistent with the survival tone;
- excessive bloom/particles;
- realistic gore as a substitute for design;
- UI copied from Fallout/ARC/Soul Knight/ZERO Sievert.

---

# 29. FINAL VISUAL DEFINITION OF DONE

RUINRAIL V1 visual production is complete only when:

1. The player, every enemy, Elite and Boss is visibly rendered with final original art.
2. Every required animation role resolves to a valid final source/clip.
3. All 33 weapons are visible and correctly aligned.
4. All 72 item icons are bound.
5. All three biomes have distinct final tiles, props and lighting.
6. All 63 rooms use final release tiles/dressing with zero release placeholder references.
7. Shelter/base stations are visually complete.
8. All required world objects have final visuals.
9. UI is fully skinned and uses a real pixel-compatible font.
10. Keyboard/gamepad glyphs are present.
11. All VFX/telegraphs use final pixel visuals.
12. No release-bound white square, flat debug tile, invisible entity, LegacyRuntime.ttf dependency or missing sprite remains.
13. Final screenshots/readability checks pass where inspection is possible.
14. All art validators are green.
15. No copyrighted reference-game asset has been copied into the project.

This document is the visual production source of truth for the final autonomous completion pass.
