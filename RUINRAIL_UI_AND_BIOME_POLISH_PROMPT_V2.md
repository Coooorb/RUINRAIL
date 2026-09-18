# RUINRAIL — TARGETED POLISH PASS V2
## Main Menu / Shelter Hub Overhaul + Biome Tile Overhaul
## Execute from the CURRENT repository state after the final autonomous art/audio pass

This is a focused production-polish pass.

Do NOT redesign the player, enemies, Elites, Bosses, weapons, or VFX unless a real technical bug requires a fix.
The current character art, weapon art and VFX are explicitly accepted for this pass.

The ONLY primary goals are:

1. Rework and polish the Main Menu / Shelter so it becomes a strong atmospheric **hub screen** with the right vibe and a significantly better PC UX.
2. Fix Main Menu / Shelter usability issues: mouse support, navigation states, text overlap/layout bugs and empty-panel presentation.
3. Rework the biome floor/tile visual language for Ruined Metro, Rustworks and Overgrown Labs so the environments look less repetitive, less noisy and more authored.

Do not ask for intermediate approval.
Do not stop after each screen or biome.
Execute the pass autonomously, test it, capture results, and return one final report.

---

# 1. READ FIRST

Before changing anything, read the current versions of:

- CLAUDE.md
- CLAUDE_START_HERE.md
- technical/117_CODING_RULES_FOR_CLAUDE.md
- technical/118_TESTING_STRATEGY.md
- FINAL_ART_PRODUCTION_SPEC.md
- production/FINAL_SHIPPABLE_V1_REPORT.md
- production/FINAL_AUTONOMOUS_COMPLETION_LOG.md
- relevant UI implementation files
- relevant biome art generation/import/editor tooling
- current screenshot/art preview tooling

Repository truth wins over older documentation.

Create/update:

`production/UI_AND_BIOME_POLISH_REPORT.md`

Use it as the final report for this pass.

---

# 2. HARD SCOPE BOUNDARIES

Do NOT change:

- gameplay balance
- weapon stats
- enemy stats
- enemy movesets
- player movement/combat
- item definitions except UI presentation wiring if strictly necessary
- character sprite design
- weapon sprite design
- VFX design
- audio content
- room topology / room logic
- dungeon generation rules
- save format
- networking rules
- stable IDs

Do NOT create new gameplay features.

This is UI/UX polish + shelter presentation polish + environment-art polish only.

---

# PART A — MAIN MENU / SHELTER HUB OVERHAUL

The current UI is functional but visibly unfinished.

Known observed problems from screenshots:

- top-right text overlaps other text;
- current focus/selection is not visually obvious;
- top navigation tabs look almost identical whether active or inactive;
- keyboard/controller navigation exists, but mouse interaction is missing or insufficient;
- instructions imply arrow-key navigation only;
- large right-side content areas can look like empty black space;
- some text/layout positioning feels prototype-like rather than final;
- the current screen does not make it obvious which section owns the visible content;
- display-name area and account/level data are not spatially separated cleanly.

The goal is NOT to make a flashy UI.
The goal is to make it feel like a polished PC game **front-end and shelter hub** in the existing RUINRAIL visual style.

---

# A0. MAIN MENU / SHELTER VIBE DIRECTION

The Main Menu / Shelter must be reworked not only for usability, but also for **overall presentation, composition and mood**.

Use the supplied reference direction as **high-level inspiration for structure and vibe**, not as a direct copy.

## Target vibe

The menu should feel like:
- a **lived-in shelter / safe hub**
- a **post-apocalyptic home base**
- a place where the player prepares, manages gear, checks progression and launches expeditions

It should feel closer in presentation intent to:
- an atmospheric hub/menu screen
- a visually framed “base of operations”
- a game front-end with presence and mood

It must still remain clearly RUINRAIL:
- pixel-art presentation
- Soul-Knight-like sprite language
- Zero Sievert / Fallout 4 / Arc Raiders inspired atmosphere
- industrial, rugged, shelter-like, grounded
- not sleek sci-fi
- not modern glossy mobile UI
- not a direct clone of Arc Raiders

## Shelter/Main Menu composition target

Rebuild the Shelter/Main Menu around these presentation ideas:

### 1. Large visual hub backdrop
The screen should present a clear sense of place.

Use a strong shelter/base backdrop or framed shelter scene so the menu feels embedded in the game world rather than floating over a black box.

The background should suggest:
- indoor shelter/base
- improvised infrastructure
- industrial walls / panels / cables / lamps / workstations
- warm lived-in safe-zone contrast against the harsh outside world

### 2. Character / hub presence
The screen should feel inhabited.

Where the current architecture supports it, the Shelter/Main Menu should visually present:
- the player presence,
- and/or the loadout/base context,
- and/or a staged shelter scene

This does NOT require a fully new 3D presentation.
Stay within the current RUINRAIL visual language and technical architecture.

A 2D/pixel-art shelter presentation, staged scene, framed hub tableau or similarly grounded presentation is acceptable.

### 3. Clear information architecture
Use a structure inspired by the reference:

- **Top area:** global navigation / identity / profile / currencies / meta info
- **Left area:** supportive information, progression, contextual section lists, stats, tasks, storage categories or similar
- **Center / background:** main shelter presence / world framing / content anchor
- **Right area:** action-oriented panel such as Play / Transit / Multiplayer / party status / expedition launch
- **Bottom area:** contextual controls / hints where useful

This does NOT mean copying the exact reference layout 1:1.
It means the screen should feel like a composed, deliberate front-end rather than a sparse tools menu.

### 4. Strong “Play” / expedition call-to-action
There should be a clearly readable primary action area.

The user should immediately understand:
- where to start playing,
- where to go to expedition/transit,
- and where party/multiplayer status lives.

The primary action should be visually stronger than secondary utility tabs.

### 5. Left-side information panel should feel intentional
The left area should not be random debug lists.

Use it for structured contextual information that already exists in the game architecture, such as:
- storage categories
- character summary
- tutorial/help snippets
- inventory/loadout context
- shelter station context
- party/session status
- progression/stat summary

Do not invent fake features just to fill the column.

### 6. Visual hierarchy
The main menu should now feel like a **real front-end screen**, not a temporary menu shell.

Required hierarchy:
- strongest: active section / play action
- medium: contextual content panels
- weaker: background support content
- subtle: control hints / secondary chrome

### 7. Atmosphere
The mood should combine:
- strong Fallout-like shelter mood
- medium Arc-Raiders-like home-base presentation energy
- RUINRAIL’s pixel-art identity
- warmer safe-zone tone than the dungeon scenes
- but still gritty and industrial

### 8. Hard boundary
Do NOT copy logos, branding, exact text layout or exact UI elements from any external game.
Take only the **presentation idea**:
- hub-as-menu
- left info stack
- top nav
- right action card
- center shelter presence
- strong sense of place

---

# A1. INPUT / MOUSE SUPPORT

Every ordinary menu button, tab and selectable UI control must support:

- mouse hover;
- left-click activation;
- keyboard navigation;
- controller navigation;
- visible focus;
- visible active/selected state;
- pressed state;
- disabled state where applicable.

Mouse and keyboard/controller must coexist.

Do NOT break existing keyboard/controller navigation.

### Mouse rules

When pointer enters an interactable element:
- hover state must become visible immediately;
- cursor interaction must correspond to the actual button bounds;
- no invisible oversized click areas;
- no dead zones inside visible buttons.

On left click:
- activate the same action as keyboard/controller confirm;
- no duplicate activation;
- no click-through to controls underneath.

If the user switches from mouse to keyboard/controller:
- keyboard/controller focus must still work;
- do not require clicking first.

If the user switches back to mouse:
- hover feedback must work immediately.

### Navigation hint

Do not show only:
`Enter: Confirm  Esc: Back  Arrows: Navigate`

Instead provide adaptive or combined instructions.

Acceptable example:
`Enter / A: Confirm   Esc / B: Back   Mouse: Select   Arrows / D-Pad: Navigate`

Use current input glyph system if available.

---

# A2. VISUAL STATES

Every button/tab/control must have at least these clearly distinguishable states:

1. Normal
2. Hover
3. Focused
4. Active / Selected
5. Pressed
6. Disabled, if applicable

Hover and Focused may share some visual language but must remain visible.

### State styling

Follow RUINRAIL's industrial pixel UI.

Suggested hierarchy:

Normal:
- dark charcoal panel
- low-contrast border

Hover:
- slightly lighter panel
- thin amber/steel highlight

Focused:
- strong 1–2 px outline or bracket
- visible even without color perception
- no subtle 2% tint-only change

Active/Selected:
- persistent stronger accent
- e.g. amber line, terminal-green marker, side notch, filled segment or double border
- must remain visible even when pointer leaves

Pressed:
- visibly depressed/inset by 1 px or equivalent pixel-state change

Disabled:
- reduced contrast
- no hover activation
- still readable

Do not use excessive glow.

---

# A3. TOP NAVIGATION TABS

The Shelter navigation tabs currently include:

- STORAGE
- LOADOUT
- TRADER
- CHARACTER
- WORKSHOP
- MULTIPLAYER
- TRANSIT
- LEAVE

Requirements:

- exactly one active primary section at a time;
- active tab must remain visibly active;
- mouse click changes tab;
- keyboard/controller horizontal navigation works;
- leaving/re-entering a section preserves valid focus according to current UX architecture;
- text must never overflow the tab bounds;
- tab widths may differ if required for readability;
- do not force all labels into widths that cause cramped text.

`LEAVE` is an action, not a persistent content tab.
Style it as an action distinct from content tabs if appropriate.

---

# A4. SCREEN LAYOUT

Rebuild the Shelter/Main UI layout around four clear zones:

## Zone 1 — Header
Contains:
- RUINRAIL / current screen identity
- current player/profile summary
- level
- relevant currency/status
- display name

Rules:
- no overlap;
- no text rendered outside intended bounds;
- right-aligned profile metadata must reserve its own measured space;
- long display names must use a defined strategy:
  - truncate with ellipsis,
  - wrap where intentional,
  - or enforce max width according to existing name rules.
- never allow one string to render through another.

## Zone 2 — Primary Navigation
Contains tabs/actions.

Rules:
- consistent spacing;
- active state obvious;
- no accidental mixed alignment.

## Zone 3 — World/Hub Presentation
Contains:
- a shelter scene / backdrop / player-base framing / staged visual presence;
- must visually anchor the screen;
- must make the menu feel like it exists in the RUINRAIL world.

This zone may be partially overlaid by content, but the sense of place must remain.

## Zone 4 — Contextual Panels / Action Areas
Contains:
- left information panel(s)
- right play/party/transit action panel(s)
- lower or overlay content panels as appropriate
- section-specific UI content

Do NOT leave giant blank black rectangles if the section has content that can be presented.

For each section:
- Storage: inventory/storage presentation
- Loadout: equipped slots / loadout data
- Trader: available trader content
- Character: level/stats/skills
- Workshop: workshop content
- Multiplayer: host/join/service state
- Transit: expedition departure/state
- Settings/Main Menu screens: actual settings/menu content

Use current view models/data.
Do NOT invent new systems just to fill space.

If a section genuinely has little content:
- use intentional composition;
- include heading, short contextual description and existing relevant data;
- do not fill space with fake lore or fake stats.

---

# A5. DISPLAY NAME / PROFILE HEADER BUGS

Inspect the screenshot-equivalent state where the top-right area contains overlapping strings.

Find the actual root cause.

Possible causes to investigate:
- anchors;
- fixed-width text;
- wrong alignment;
- content-size fitter interaction;
- multiple views rendered simultaneously;
- duplicate labels;
- stale old view not hidden;
- absolute positioning;
- incorrect font metrics after replacing LegacyRuntime.ttf.

Fix the root cause.

Do NOT "fix" this by moving the text randomly until one screenshot looks okay.

Add a regression test or layout assertion for:
- short name;
- maximum-length valid name;
- level 1;
- high multi-digit level;
- representative currency values;
- 640×360 reference resolution.

No overlap is acceptable.

---

# A6. FONT / TEXT FIT

Use the current final pixel font.

Audit all Main Menu/Shelter screens for:

- clipping
- overlaps
- incorrect line height
- vertical cut-off
- overly tight tracking
- strings outside bounds
- button labels not centered
- wrapped text in controls that should be single-line

Test at the approved reference resolution: 640×360.

If additional supported resolutions exist:
- validate scaling behavior there too.

Do not reduce every font globally just to make one label fit.

Prefer:
1. correct anchors
2. correct padding
3. correct control width
4. appropriate wrapping/truncation
5. only then small size adjustment

---

# A7. CONTENT PANEL PRESENTATION

The current content area is visually too empty.

Improve presentation without overdesign.

Use:
- section header
- structured subpanels
- item slots
- stat rows
- separators
- relevant icons
- selected item/detail panel where current data supports it

Avoid:
- giant decorative backgrounds
- fake data
- excessive borders
- noisy sci-fi overlays

The UI should feel like:
"post-apocalyptic industrial shelter interface"
not:
"debug menu"
and not:
"modern mobile app".

The player should feel they are standing in or operating from their shelter, not browsing a sterile box menu.

---

# A8. MAIN MENU

If Main Menu and Shelter share systems, make both consistent.

Main Menu must support:
- mouse hover/click
- keyboard
- controller
- obvious focus
- no overlap
- no hidden selection state

Buttons:
- Play / Continue as current implementation supports
- Settings
- Quit
- any existing approved entries

Do not add new product features.

Main Menu must also present a stronger identity:
- clear RUINRAIL branding
- shelter/front-end atmosphere
- readable structure
- obvious primary action

---

# A9. UI TESTS

Add/extend automated tests where feasible for:

- mouse click invokes same action as confirm;
- hover state activates correctly;
- focus state visible flag/style applied;
- active tab persists;
- disabled control ignores click;
- keyboard/controller navigation still works;
- maximum-length display name does not overlap level/currency;
- no duplicate screen panels visible;
- all text bounds fit at 640×360;
- Main Menu and Shelter navigation maps remain valid.

Run relevant UI EditMode/PlayMode tests before continuing to Part B.

---

# PART B — BIOME TILE / FLOOR OVERHAUL

The current biome props, characters, weapons and VFX are acceptable.

The weak point is the environment base layer, especially the floor tiles.

Observed problem:

The floor textures rely too heavily on small repeated pixel noise.
This makes large rooms look like wallpaper rather than authored physical spaces.

The objective is:

- less uniform micro-noise;
- larger material shapes;
- clearer hierarchy;
- more calm/negative visual space;
- stronger biome identity;
- better contrast with characters/VFX;
- still unmistakably pixel art.

Do NOT repaint characters, weapons or VFX as part of this section.

---

# B1. GLOBAL FLOOR DESIGN RULES

For each biome, the base floor should obey:

### 1. Calm base first
At least approximately 60–75% of an ordinary floor tile's visual mass should be calm structural material.

Examples:
- concrete slab
- steel plate
- lab panel
- worn platform tile

### 2. Detail second
Micro-detail should support the material, not cover it.

Avoid:
- uniform salt-and-pepper noise
- same-density speckles across every tile
- evenly distributed colored pixels

### 3. Large shape hierarchy
Use:
- seams
- panels
- cracks
- wear zones
- patches
- drains
- plate divisions
- edge damage
- larger grime regions

Prefer 4–12 px structures over random 1 px noise.

### 4. Variation families
Create multiple compatible variants.

At minimum per biome floor family:
- clean/base
- worn
- cracked/damaged
- utility/panel
- accent/detail

Do not turn each room into random noise by mixing every variant everywhere.

### 5. Macro composition
Room visuals should include quiet zones and focal zones.

Do not distribute high-detail tiles uniformly across the entire room.

### 6. Gameplay readability
Player/enemies must separate from floor.
Telegraphs must remain clearly visible.

---

# B2. RUINED METRO OVERHAUL

Current issue:
The Metro floor reads too much like generic dark stone/asphalt with repeated noise.

Target:
Clearly abandoned subway / underground infrastructure.

## Floor family

Build a stronger mixture of:

1. poured concrete slab
2. station/platform tile
3. maintenance-panel floor
4. cracked concrete
5. grime/water-damaged variant

### Concrete slab
- larger slab boundaries;
- sparse cracks;
- subtle broad stains;
- very limited one-pixel grain.

### Platform tile
- rectangular tile rhythm;
- worn grout;
- occasional missing/chipped area;
- not a brick wall pattern.

### Maintenance panels
- large steel/concrete access panels;
- bolts/slots sparse;
- directional utility feel.

## Metro macro identifiers

Rooms should gain controlled use of:
- platform edge lines;
- maintenance stripes;
- cable channels;
- track-adjacent surfaces;
- drainage;
- faded route markings;
- rail infrastructure.

Not every room needs visible rails.
But the biome should read as transit infrastructure even without the depth label.

## Color

Dominant:
- charcoal
- concrete gray
- dirty steel

Accents:
- faded ochre/yellow
- rust
- restrained terminal cyan/green

Reduce uniform tiny yellow pixels on the base floor.

## Walls
Keep current acceptable wall style where possible, but improve consistency if the floor overhaul exposes mismatch.

---

# B3. RUSTWORKS OVERHAUL

Current issue:
The floor has too many orange/rust pixels distributed everywhere, producing a noisy wallpaper effect.

Target:
Heavy industrial space with defined metal structures and controlled heat/rust.

## Floor family

Build:

1. large dark steel plates
2. soot-stained industrial concrete
3. grate/service walkway
4. heat-damaged plate
5. oil/wear variant

### Steel plates
- large plate boundaries;
- fewer but clearer rivets;
- wear concentrated near seams.

### Industrial concrete
- darker broad stains;
- sparse chips;
- rust only near metal interfaces.

### Grates
- strong directional pattern;
- use as specific zones, not entire rooms.

### Heat damage
- orange/brown discoloration grouped near machinery/hazard areas;
- do not evenly scatter orange across the whole floor.

## Color

Dominant:
- dark charcoal
- blackened steel
- soot gray

Secondary:
- rust brown

Accent only:
- orange
- warning ochre
- hot red

The whole screen must NOT become orange.

## Macro layout

Use higher-detail/hot zones around:
- machinery
- furnace areas
- pipes
- hazards
- industrial work cells

Keep traversal lanes visually calmer.

---

# B4. OVERGROWN LABS OVERHAUL

Current issue:
The floor reads as bright gray tiles with uniformly scattered green dots.

Target:
Abandoned research facility overtaken by localized vegetation/contamination.

## Floor family

Build:

1. clean/aged lab panel
2. cracked lab tile
3. maintenance/utility panel
4. contaminated floor
5. vegetation-intrusion edge/patch

### Lab base
- large clean panels;
- cold gray/green-gray;
- subtle seams;
- low micro-noise.

### Damage
- cracks grouped;
- broken panels localized;
- dark exposed structure below selected areas.

### Vegetation
Do NOT scatter green pixels uniformly.

Instead use:
- clustered moss patches;
- root/vine edges;
- plant intrusion from walls/objects;
- contamination patches.

Green should appear in intentional regions.

### Contamination
- readable toxic areas;
- distinct from harmless vegetation;
- hazard colors must remain gameplay-clear.

## Color

Dominant:
- pale cool gray
- green-gray
- dark structural gray

Secondary:
- olive vegetation

Accent:
- toxic green
- cyan tech

Keep the overall floor calmer than current version.

---

# B5. TILE SEAMS / REPETITION

Inspect rooms at full 640×360 view.

Reject obvious repeating visual patterns such as:

- every 32×32 tile showing identical crack placement;
- diagonal motifs lining up unnaturally;
- repeating orange dots;
- repeating green dots;
- obvious checkerboarding;
- every floor tile having equal detail density.

Use:
- variant selection
- controlled rotations/flips only where art allows
- detail overlays
- authored room dressing

without breaking deterministic generation.

Do not introduce per-frame/random visual changes.

---

# B6. FLOOR DETAIL LAYER

Move non-structural visual noise out of the base floor where appropriate.

Use Floor Detail layer for:
- stains
- faded signage
- debris marks
- cracks
- moss
- cable markings
- maintenance numbers
- route lines
- localized rust

This lets the base floor remain clean enough for gameplay readability.

Do not turn Floor Detail into another full-coverage noise layer.

---

# B7. ROOM COMPOSITION

Across all 63 rooms:

- preserve room geometry;
- preserve collision;
- preserve logic markers;
- preserve door sockets;
- preserve hazards;
- preserve encounter composition.

Only visual dressing changes.

For ordinary combat rooms:

Target approximate visual hierarchy:
- 60–75% calm navigable floor
- 10–20% structural variation
- 5–15% props/detail clusters
- remainder focal/hazard/event elements

This is a guideline, not a gameplay rule.

Boss/special rooms may intentionally be more composed.

---

# B8. BIOME IDENTIFICATION TEST

Each biome should be identifiable from a screenshot with:

- depth label hidden
- HUD hidden
- no unique boss visible

A viewer should distinguish:
- Ruined Metro
- Rustworks
- Overgrown Labs

based on floor/walls/props/lighting alone.

If they only differ mainly by palette, the overhaul is insufficient.

---

# B9. READABILITY TEST

Capture representative 640×360 scenes after overhaul.

Required screenshots:

1. Ruined Metro normal combat
2. Ruined Metro telegraph-heavy encounter
3. Rustworks normal combat
4. Rustworks telegraph-heavy encounter
5. Overgrown Labs normal combat
6. Overgrown Labs telegraph-heavy encounter
7. Shelter UI main view
8. Storage
9. Loadout
10. Trader
11. Character
12. Workshop
13. Multiplayer
14. Transit
15. Main Menu

Verify objectively where possible:

- player readable;
- enemies readable;
- hazards visible;
- telegraphs clearly visible;
- no extreme visual noise;
- no text overlap;
- focused/active UI control visible;
- no large unintended empty/debug areas;
- no old floor tiles accidentally retained.

Save previews under:

`TestResults/PolishPreview/`

---

# PART C — ACCEPTANCE CRITERIA

This pass is complete only when ALL of the following are true:

## UI
- mouse click works on all standard buttons/tabs;
- mouse hover feedback exists;
- keyboard navigation still works;
- controller navigation still works;
- focus is visibly obvious;
- active tab/section is visibly obvious;
- pressed/disabled states are correct;
- no top-right text overlap;
- no display-name/profile overlap at max valid lengths;
- no duplicate panel rendering;
- no important text clipping at 640×360;
- content panels no longer present accidental giant blank areas where real content exists;
- Main Menu and Shelter share a coherent final visual language;
- Main Menu / Shelter clearly communicate “home base / shelter hub” presentation instead of just “debug utility menu”.

## Biomes
- Metro base floor no longer reads as generic noisy stone/asphalt;
- Rustworks no longer distributes orange noise uniformly across the whole floor;
- Labs no longer scatters green noise uniformly across every tile;
- each biome has calm base floor + localized detail;
- larger material shapes dominate over random micro-noise;
- all 63 rooms use the revised environment art where applicable;
- room logic/collision unchanged;
- telegraph readability preserved;
- no placeholder/debug tiles reintroduced;
- biome identity works without the depth label.

## Engineering
- no new test failures;
- EditMode green;
- PlayMode green;
- UI validators green;
- art/presentation validators green;
- content-count validators unchanged;
- release build succeeds;
- built-player smoke succeeds.

---

# PART D — DO NOT SELF-DECEIVE

Do not declare success merely because:
- new PNGs exist;
- tests compile;
- a tile reference is non-null;
- a button technically receives click events.

The visual/UX outcomes above are the actual acceptance target.

If a UI layout still visibly overlaps in captured screenshots, fix it.
If floor tiles still look like repeated noise wallpaper, revise them.
If mouse focus exists in code but no visible hover/focus state appears, fix it.
If the Shelter still feels like a sparse utility panel rather than a home-base front-end, keep iterating.

Iterate until the screenshots and objective checks agree.

---

# PART E — FINAL OUTPUT

At the end update:

`production/UI_AND_BIOME_POLISH_REPORT.md`

Report:

- exact UI bugs fixed;
- mouse interaction changes;
- focus/hover/active-state implementation;
- text-layout fixes;
- screens changed;
- shelter/menu presentation changes;
- tiles regenerated/repainted;
- room references updated;
- biome-specific visual changes;
- tests before/after;
- build result;
- smoke result;
- paths to preview screenshots;
- any genuine remaining blocker.

Terminal status:

`POLISH_PASS_COMPLETE`
only if all acceptance criteria are met.

`POLISH_PASS_INCOMPLETE`
if repository-local work still remains.

Do not create a new task series.
Do not ask for approval during execution.

BEGIN NOW.
