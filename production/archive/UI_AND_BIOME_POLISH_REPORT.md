# RUINRAIL — UI and Biome Polish Pass

> **Scope:** Main Menu / Shelter hub presentation and UX, mouse support and control states, and the
> Ruined Metro / Rustworks / Overgrown Labs floor and tile language.
> **Out of scope and unchanged:** character art, weapon art, VFX, audio, gameplay balance, enemy stats and movesets,
> player movement and combat, item definitions, room topology, dungeon generation, save format, networking, stable IDs.
> **Terminal status:** `POLISH_PASS_COMPLETE`

| Gate | Before | After |
|---|---|---|
| EditMode | 723 passed / 724 discovered, 0 failed | **751 passed / 752 discovered, 0 failed** (1 ignored, as before) |
| PlayMode | 503 passed / 503 discovered, 0 failed | **526 passed / 526 discovered, 0 failed** |
| Release build | — | **Succeeded**, 0 errors, 1 warning, 115.1 MB |
| Built-player smoke | — | **Success** — `MainMenu;Base;Dungeon;Base;`, 10 rooms, save reloaded |

All runs used the repo harness (`./scripts/run-unity-tests.ps1`) and Unity `6000.3.24f1`. No pinned package or Editor
version changed.

---

## 1. Environment note: Unity needs two runs after a source change

Smart App Control is enforcing on this machine and blocks Unity's own unsigned build backend:

```
Code Integrity determined that a process (…\Editor\Data\netcorerun\netcorerun.exe)
attempted to load (…\Editor\Data\Tools\BuildPipeline\Bee.TundraBackend.dll)
that did not meet the Enterprise signing level requirements.
```

`VerifiedAndReputablePolicyState = 1`, `Bee.TundraBackend.dll` is **NotSigned**, and Unity `6000.6.0f1` (also
installed) ships the same unsigned binary, so a version switch would not help.

The practical effect: the first Unity launch after a source change regenerates the build graph and dies at that block;
the **second** launch finds the graph valid and compiles and runs normally. Every result in this report was produced
by running the harness twice. Nothing in the repository was changed to work around it, and the harness was **not**
given a retry — hiding a failed run inside a retry loop is exactly what the test rules forbid. Turning Smart App
Control off would remove the double run; that is a system-wide, irreversible security decision and was deliberately
left to the machine's owner.

---

## 2. Toolchain and font fixes

### 2.1 `scripts/run-unity-tests.ps1` did not wait for Unity

`& $UnityExecutable @unityArgs` returns immediately for `Unity.exe`, a GUI-subsystem binary, leaving `$LASTEXITCODE`
unset. The harness threw `"failed with exit code "` (empty) while Unity was still running, and would equally have
validated a stale `TestResults/*-results.xml` from a previous run.

Now `Start-Process -PassThru` + `WaitForExit()`, with an explicit guard for a null exit code. Every validation rule
below it — result file present, `total > 0`, `passed > 0`, no failures — is untouched, so what counts as a PASS has
not moved. The bash harness already waited correctly and was not changed.

### 2.2 Four separate defects in the generated pixel font

The font is a bitmap face assembled in script, and `Font.lineHeight`, `Font.fontSize` and `Font.ascent` are read-only
in Unity's public API — so a font built purely through that API ships with all three wrong. Every one of these was
live in the release path, and together they are what made the Shelter text unreadable.

| Defect | Effect | Fix |
|---|---|---|
| `m_LineSpacing = 0.1` | Every line of a multi-line label drew a tenth of a pixel below the one above — i.e. on top of it. **This is the reported "top-right text overlaps other text".** | `PixelFontAssetBuilder.ApplyFaceMetrics` writes the authored line height (9) through `SerializedObject` |
| `m_FontSize = 0` | No reference size for the face | Written as the authored 7 |
| `m_Ascent = 0` | Unity measures the first baseline down from the ascent; at zero the baseline sits on the rect's top edge and the **whole line drew above its box**, 7 px out of place | Written as the authored cap height |
| Atlas cells abutted with no gutter | A quad's right edge sampled the first column of the *next* glyph. Invisible between letters (the following quad paints over it) and plainly visible as a stray vertical tick after the last letter of every line | 1 px transparent gutter per cell, the quad widened to take it in, and the UV stopped half a texel short of the cell boundary |

The charset was also missing `…`, `·` and `—`. `TextFit.Clamp` appends an ellipsis to **every** string it truncates
and the dungeon HUD writes an em dash for an empty weapon or consumable slot, so the most common punctuation in the
whole UI had no glyph at all. All three were added to the face.

`PixelFontRenderingTests` (PlayMode, new) now measures these off the rendered frame rather than trusting the numbers:
it renders known strings, reads the pixels back, and asserts the advance, the cap height, the two-line box, that the
first line lands inside its rectangle, and — first, because nothing downstream means anything otherwise — that a
one-pixel plate rasterises across exactly one pixel.

---

## 3. Part A — Main Menu and Shelter

### 3.1 Root cause: the project had no EventSystem

`grep -rn "EventSystem" Assets/` returned nothing across every script and scene. uGUI performs **no pointer raycasts
at all** without one, so every `Button.onClick` the menus registered was unreachable. The menus were not "missing some
mouse support" — they were completely mouse-dead, and no amount of button wiring would have changed that.

`UiKit.EnsureEventSystem()` creates one (once per process, `DontDestroyOnLoad`) from `UiKit.Canvas`, using
`InputSystemUIInputModule`. That module is required, not preferred: `ProjectSettings` has `activeInputHandler: 1`
(Input System package only), where the legacy standalone module throws on its first frame. Adding it at runtime makes
it assign the package's own default UI actions, so no project input asset is touched and no gameplay binding is
involved.

### 3.2 New UI foundation

| File | What it is |
|---|---|
| `UI/Theme/UiTheme.cs` | Spec section 18 as runtime colours and metrics: 4 px grid, band heights, the state styling table |
| `UI/Theme/ScreenLayout.cs` | The measured geometry of the front-end — header, tab bar, content columns, footer — as pure arithmetic |
| `UI/Theme/UiControl.cs` | One interactive control: pointer handlers plus the focus/active state machine |
| `UI/Theme/FocusWindow.cs` | Scrolls a long focus list through a fixed set of rows, by focus rather than by a scrollbar |
| `UI/Theme/UiSkin.cs` | Resources-side binding to the generated UI art, the split `GameContentCatalog` already uses |
| `UI/Base/StationPresentation.cs` | Each station's title, purpose line and data rows, derived from the existing view models |

`UiControl` replaces the uGUI `Button` the screens used. `Button` carries its own selection and colour-tint state
machine, which competes with the focus list over "what is selected" and offers no persistent selected state at all —
which is precisely why the tab bar looked identical whether a tab was active or not. Now the focus list is the only
owner of focus, the view model the only owner of which section is open, and the control renders those two facts.

### 3.3 Mouse, keyboard and controller

- Hover, press, release and left-click are handled on the control itself, and the click target is the visible plate —
  no invisible margin around a control, no dead zone inside one.
- A click runs `FocusItem.TryActivate`, the identical path Enter and the A button take. One click is one activation.
- A click also takes focus, so the next arrow keypress continues from what was clicked.
- **Hover deliberately does not steal focus.** The pointer passing over a control must not drag the keyboard cursor
  away from where the player left it, and it keeps Hover and Focused distinguishable as separate states.
- A disabled control ignores hover and swallows the click rather than passing it through.
- Device switching needs no click first in either direction; `MenuInput` also watches mouse movement and clicks.
- `MenuInput` gained horizontal steps (arrows / A-D / Q-E / D-pad / shoulders) and an `InputBlocked` hook so a scene
  transition owns the screen.

Navigation hint, from `UiPrompts.Footer()`:

```
Enter / A: Confirm   Esc / B: Back   Mouse: Select   Arrows / D-Pad: Navigate
```

Keyboard glyphs are listed first regardless of the active device: a hint line that reorders itself under the player is
harder to read than one that stays put, and the point of the combined form is that it never tells a pad player the
menu is arrows-only.

### 3.4 Visual states

Six states, each differing from Normal in fill **and** in at least one non-colour cue, so they survive a grayscale or
colour-impaired read (spec 18.4):

| State | Cue |
|---|---|
| Normal | dark charcoal plate, low-contrast border |
| Hover | lighter plate, steel edge |
| Focused | amber edge **plus 3 px corner brackets** |
| Active / Selected | strongest plate, amber edge, **persistent 2 px side notch** surviving pointer and focus leaving |
| Pressed | darker plate, **label inset 1 px down and right** |
| Disabled | reduced contrast, still readable, no hover, no activation |

### 3.5 Top navigation

- Tabs are sized to the width each label actually needs — `MULTIPLAYER` and `TRADER` no longer have to agree.
- Exactly one primary section is active at a time, driven by `hub.Current`, and it stays marked when focus moves.
- `LEAVE` is an action, not the eighth content tab: pinned to the right margin, separated by a gutter, own role.
- `TRANSIT` is styled as the primary action, so the heaviest control in the bar is the one that starts an expedition.

Two focus bugs were found and fixed by the interaction tests:

- **Switching section left the tab cursor behind.** Closing a station panel pops it off the focus stack, and popping
  restores the tab the panel was pushed with — right for Back, wrong for a switch, because it dragged the cursor back
  to the section being left. The tab bar is now re-pointed at whatever is actually open, which makes the click, the
  confirm and the horizontal-step paths agree.
- **The tab bar went keyboard-inert whenever a station was open**, because horizontal steps were gated on the bar
  owning the focus. A horizontal step now switches section directly from anywhere; stepping onto LEAVE only moves the
  focus there, so leaving the Shelter stays a deliberate confirm.

### 3.6 Screen layout and the header bug

Four zones at 640×360, adding up to the screen exactly:

```
y   0– 30  Header    identity + profile, measured against each other
y  30– 52  Tab bar   7 station tabs, LEAVE pinned right
y  52–342  Content   left column | station panel | expedition card, over the Shelter backdrop
y 342–360  Footer    control hints (left) | station feedback (right, own reserved strip)
```

The header bug had **two** root causes, both fixed at the source:

1. The font line spacing of 0.1 px (section 2.2) stacked the three profile lines on top of each other.
2. The profile block was a fixed-width label at a fixed coordinate (`x=400, w=230`, three lines tall) sitting over a
   600 px-wide onboarding prompt at `y=-34`. A long name or a three-digit level simply drew through it.

`ScreenLayout.BuildHeader` now measures the profile strings first, reserves their width against the right margin,
gives the identity block what is left minus a gutter, and truncates each string to its own reservation. The two blocks
cannot meet, whatever the name length, level or coin count.

Two further layout defects surfaced and were fixed:

- **The layout clipped at any aspect ratio but 16:9.** The canvas scaler matched width and height equally, so a 4:3
  window produced a canvas only ~554 reference pixels wide and everything authored across the full 640 — the tab bar
  reaching the right margin, the footer spanning the screen — was cut off. The scaler now uses `Expand` and each
  screen builds inside a fixed 640×360 `ReferenceRoot` centred in the canvas, so the authored layout always fits and
  the surplus becomes margin. At 16:9 the two modes are identical, so nothing changed for the target resolution.
- **Every tab showed a truncated label** ("STORA…", "MULTIPLAY…") because the tab bar sized each tab for its label
  with 10 px of padding while the control inset its text by 12. The two now agree, and a test asserts each tab shows
  its whole label.

The right-hand column is a read-only expedition card by design: `TRANSIT` is already a control in the tab bar, and a
second button firing the same action would give the screen two places to start a run and two things for focus to sit
on. The card reports; the tab acts.

### 3.7 Content presentation

`StationPresentation` gives every station a title, a one-line statement of what it does in terms of the approved
rules, and structured key/value rows read from the view model that already owns each number:

| Station | Rows |
|---|---|
| Storage | capacity, filter, sort, stored items with rarity labels |
| Loadout | five equipment slots, backpack occupancy and contents |
| Trader | banked coins, offers with prices |
| Character | level, XP into level, skill points, respec price, six attribute ranks |
| Workshop | banked coins, storage tier / slots / next cost, trader level / next cost |
| Multiplayer | terminal state, party size, join code, error, roster with ready state |
| Transit | party size, ready count, local ready, departure clearance, roster |

Nothing is fabricated to fill space. Where a station genuinely holds nothing it says so in a sentence ("Storage is
empty. Extract with loot and deposit it here.") instead of presenting an empty frame. With no station open the middle
column removes its frame entirely rather than showing an empty plate, so the Shelter backdrop reads as the room, with
a short guidance strip at the bottom.

### 3.8 Hub presentation and mood

`Editor/ArtGen/ShelterSceneFactory.cs` generates two full-screen 640×360 backdrops at screen pixels (PPU 1), from
spec section 16:

- **`ui_shelter_backdrop.png`** — a staged Shelter interior: concrete floor with a worn traffic lane and stencilled
  markings, salvaged bulkhead panelling with structural beams and welded patches, conduit runs across the ceiling,
  catenary cable spans, a storage bay of shelving and crates on the left, a workbench with a tool board and a small
  powered terminal on the right, and a heavy powered transit bulkhead dead centre. Three practical amber lamps are
  the only warm light and are what give the floor its shape.
- **`ui_menu_backdrop.png`** — the approach to the Shelter: a transit tunnel in one-point perspective with rings,
  rails, sleepers and receding emergency lamps, ending at the same lit bulkhead door.

Both are composed for the UI on top of them: the storage bay and work bay sit behind the left and right columns, the
transit door sits in the centre window the content plate leaves open, and both carry a vignette so thin 1 px chrome
stays readable at the edges. After a first pass read too murky for a "warmer, lived-in safe hub", the wall, floor and
beam tones were lifted, the grime density roughly halved with larger clusters, the practical lamps' glow and cast
cones strengthened and the vignette reduced from 0.62 to 0.40.

The art stays at its convention path under `Assets/Game/Art/UI/`, where the naming convention and import validators
expect it; `Assets/Game/Resources/UiSkin.asset` carries the references across for the player build — the same split
`GameContentCatalog` already uses. No logo, layout, UI element or visual design was taken from any external game; only
the presentation idea of hub-as-menu with a top nav, a left info stack, a right action area and a centre sense of
place.

### 3.9 Main Menu

Rebuilt on the same foundation: menu backdrop, `RUINRAIL` wordmark at an integer ×4 scale of the pixel face over an
amber rule, the approved terminology line `SHELTER · EXPEDITION · EXTRACTION`, `PLAY` as a visibly heavier primary
control with a live hint reading "Continue your profile" or "Start a new profile", and a `PROFILE` card populated from
`GameApp.ProbeSave()` — the same snapshot the smoke run uses, so the card cannot disagree with what `PLAY` is about to
load. With no save it says so plainly instead of showing zeroes.

Settings is a panel over the menu, scrolled by focus through `FocusWindow` rather than by a scrollbar, because a
scrollbar is a pointer-only affordance and the front-end has to work identically from all three devices.

---

## 4. Part B — biome floor and tile overhaul

### 4.1 Why the floors read as wallpaper

The old `TileFactory.Fill` laid 2×2 random noise clusters at 5–12% density over every tile, and each floor role had
exactly **one** tile. Random per-cell noise is *identical in every copy of the tile*, so the eye finds the repeat
immediately; on top of that, Metro carried the same yellow safety dash and Rustworks the same rust spray in every
cell. That is the "every 32×32 tile showing identical crack placement" failure the pass rejects.

### 4.2 The new construction, and what iterating on it taught

`TileFactory.Material` replaces the speckle with **value noise on a lattice that wraps at the tile edge**: 8 px
feature size, quantised to three values of one ramp, so a laid-out floor has no seam artefacts.

Broad shapes alone were not enough, and the screenshots said so. Three rounds of correction, each one visible in the
previews:

1. **Large high-contrast blobs are worse than fine noise.** A big shape repeated every 32 px is a stamped grid, and
   the first pass traded speckle for exactly that. The mottling ramp was cut to a small luminance delta, and
   `BiomeFloorOverhaulTests` gained an interior-luminance-spread metric so this is caught by a number rather than by
   eye — flatness alone cannot see it.
2. **A fixed lerp factor is not a fixed amount of contrast.** Darkening the pale Labs panel by 7.5% toward black moves
   it nearly twice as far in luminance as darkening dark Metro concrete by the same factor, which is why Labs still
   showed a visible blob at a setting where Metro did not. `RampToDelta` now targets the luminance delta directly, so
   all three biomes get the same amount of mottling whatever value their material sits at.
3. **A periodic field cannot hide a loud pattern.** Rustworks' heat damage was first an orange gradient anchored to
   one edge, which made every cluster resolve into clean vertical stripes; rebuilding it from the wrapping noise field
   joined the clusters up but turned them into a polka-dot grid, because a field that wraps over a 32 px tile is
   periodic by construction. The answer was not a better pattern but less contrast: the heat is now a burnt *material
   tint* at the same quiet level as the rest of the floor, so a cluster reads as a warm zone near the machinery, and
   full-strength orange stays where spec 14 puts it — hazards and machinery.

The same lesson removed the Labs access panel (a panel centred in the tile made a cluster read as a grid of stamped
squares; the grooves now run edge to edge and join up) and softened the Labs broken panel (a near-black hole in a pale
panel sat at the same spot in every copy of the one Cracked tile).

Each biome now ships a five-member floor family and a four-member detail layer:

| Member | Ruined Metro | Rustworks | Overgrown Labs |
|---|---|---|---|
| Base | poured concrete slab, cast seam on two edges | large blackened steel plate, two rivets | clean-room panel, one thin seam |
| Worn | broad polished region, no new edges | soot-stained industrial concrete | aged panel with a broad stain |
| Cracked | one crack system with a branch, chipped corner | heat-tinted plate, burnt discolouration | crack network plus a broken panel |
| Utility | maintenance access panel with directional grooves | service walkway grating with cross bearers | ribbed maintenance flooring |
| Accent | station platform tile, 8 px grout, one chipped square | oil pool with a shallow edge | clustered moss colony with runners |

Detail layer (`Grime` / `Marking` / `Service` / `Residue`) is mostly transparent by construction, so it reads as
something lying on the floor rather than as a second full-coverage texture — the point of spec B6. Markings run the
full tile width so consecutive tiles form one continuous line, with long chips rather than per-pixel gaps.

Specific fixes the pass asked for:

- **Metro** — the repeated yellow dash is gone from the base floor entirely; it is now a `Marking` on the detail
  layer. The slab seam tiles into a real slab lattice rather than reading as texture.
- **Rustworks** — rust and heat are no longer sprayed across every tile. The base, worn and utility members are
  asserted to be under 4% warm pixels, and the detail layer's rust bloom was darkened and shrunk after the previews
  showed it putting saturated orange marks on ordinary floor.
- **Labs** — scattered green pixels are gone. Vegetation is a colony with a centre and runners, present only in the
  Accent member and the Residue detail; the base, worn and utility members are asserted under 3% green.

### 4.3 Room distribution

`Editor/ArtGen/BiomeFloorPlan.cs` decides which family member each cell receives. Picking per cell at random
satisfies neither requirement, so the plan is **quota-based and zone-ranked**:

- Every floor cell is scored against two independent low-frequency noise fields — one for constructed elements
  (Utility, Accent), one for wear (Cracked, Worn) — so a service panel has nothing to do with where the floor happens
  to be worn.
- Cells are ranked by those scores and quotas are handed out from the top down. Ranking by a *smooth* field means the
  winners are neighbours, which produces zones instead of a sprinkle; filling a *fixed* quota means the proportions
  are exact in every room rather than approximately right on average.

Quotas: Base 66%, Worn 15%, Cracked 9%, Utility 6%, Accent 4%. The actual repaint across all 63 rooms measured
**Base 67%, Worn 15%, Cracked 9%, Utility 6%, Accent 4%** — inside the 60–75% calm navigable floor spec B7 asks for.

Everything is a pure function of the cell coordinate and a per-room seed derived from the prefab's asset path, so a
room bakes identically every time and nothing is decided at play time.

### 4.4 Repainting the 63 rooms

`RoomTileRepainter` distributes the family across each room's floor and detail layers: **22,606 cells repainted across
63 room prefabs**. It still changes only which `Tile` asset a populated cell points at — cell positions, tilemap
layers, colliders, `RoomRoot` and every marker component are untouched, and every member of a floor family carries the
same `ColliderType.None`, so swapping one for another cannot change what a room does.

It is also re-runnable now: `RoleOf` recognises the tiles the repainter itself wrote as well as the original
`Placeholder_*` names, so a revised tile set lands on rooms that have already been repainted once. Variant 0 keeps the
bare role name (`ruinedmetro_floor`) so the asset naming convention, the completion manifest and existing prefab
references all still resolve.

---

## 5. Tests

51 new cases (28 EditMode, 23 PlayMode), counted as the difference between the baseline and final discovered totals.
The measurements below are the ones that make the visual claims checkable.

**`UiLayoutTests`** (EditMode) drives `ScreenLayout` with the extremes: header overlap across 4 names × 4 levels × 4
coin values including a 16-character name at level 999 with 999,999 coins; containment of every header text inside its
own block and the header band; truncation instead of overflow; clamping of an over-long name; tab layout with no
overlap and every label fitting its own tab; LEAVE pinned apart; content columns tiling the band; the four bands
summing to 360 px exactly; row capacity agreeing with row generation; every station description fitting its panel
header; the footer naming all three devices on one line; and the pixel-face metrics.

**`BiomeFloorOverhaulTests`** (EditMode) measures the art rather than asserting about it. *Flatness* is the share of a
tile whose four neighbours match; *interior luminance spread* is the standard deviation inside the authored seam;
*clustering* is the share of a family's cells touching another of the same family. It asserts every base floor is
≥60% flat with ≤22% edge density and ≤0.018 spread; that no member contributes more noise to a room than its share can
afford (spread × quota under one budget, which is why the 4%-coverage Accent may be the loudest member and the
66%-coverage Base may not); that wear and damage stay within 0.07 luminance of the base so zones are not blotches;
that all five members exist and differ from the base in >10% of pixels; that floors are opaque and detail tiles are
not; that the base tile wraps without a hard grid line; that the three biomes differ in >80% of pixels and in the
expected brightness order; that Rustworks calm members stay under 4% warm and Labs under 3% green while the Accent
carries a real colony; that room plans hit 60–75% Base across four sizes and three seeds; that detail clusters
measurably more than the same quota scattered at random; and that plans are deterministic and order-independent.

**`ShelterUiInteractionTests`** (PlayMode) drives the real screens through the real pointer handlers: an EventSystem
with an input module and a raycaster on every canvas; a click doing exactly what confirm does, once; a click taking
focus; switching section leaving the cursor on the section that opened; horizontal steps switching section while a
panel owns the focus; hover visible and distinct from focus; focus readable by shape; the active tab staying marked
after pointer and focus leave; pressed inset; a disabled control ignoring hover and swallowing the click; every
station reachable by stepping; device switching both ways without a click first; one canvas and one station panel at a
time across all seven stations; every tab showing its whole label; every label inside its box and inside the 640×360
frame for all seven stations; and no header overlap with a maximum-length name and a six-figure balance.

**`PixelFontRenderingTests`** (PlayMode) measures the face off the rendered frame — pixel alignment first, then
advance, cap height, two-line box, line placement inside the box, integer scaling, and that the release path is not
falling back to `LegacyRuntime.ttf`.

**`PolishPreviewCaptureTests`** (PlayMode) captures the nine UI screens and checks each: a frame >72% one colour did
not render, fewer than 25 distinct colours is chrome without content, and a connected flat block over 34% of the frame
is the empty content area section A4 forbids.

---

## 6. Screenshots

All under `TestResults/PolishPreview/`, at the 640×360 reference with a nearest-neighbour `_x2` copy beside each.

| # | Shot | File |
|---|---|---|
| 1 | Ruined Metro, normal combat | `biome_RuinedMetro_combat.png` |
| 2 | Ruined Metro, telegraph-heavy | `biome_RuinedMetro_telegraphs.png` |
| 3 | Rustworks, normal combat | `biome_Rustworks_combat.png` |
| 4 | Rustworks, telegraph-heavy | `biome_Rustworks_telegraphs.png` |
| 5 | Overgrown Labs, normal combat | `biome_OvergrownLabs_combat.png` |
| 6 | Overgrown Labs, telegraph-heavy | `biome_OvergrownLabs_telegraphs.png` |
| 7 | Shelter, hub view | `ui_shelter_main.png` |
| 8–14 | Storage / Loadout / Trader / Character / Workshop / Multiplayer / Transit | `ui_shelter_<station>.png` |
| 15 | Main Menu (and its settings page) | `ui_main_menu.png`, `ui_main_menu_settings.png` |

Supporting: `font_specimen_runtime.png` (the face as the game draws it), the `font_probe_*.png` measurement frames,
and `TestResults/ArtPreview/floorfield_<Biome>.png` — an 8×5 field of the base floor alone, which is the honest
repetition test for the tile the player spends nearly every frame looking at.

The biome scenes are composed by `GameplayMockRenderer` from the *shipped* assets, and the floor is laid out by the
same `BiomeFloorPlan` the room repainter uses — same quotas, same zone clustering — so the preview answers the
wallpaper question rather than dodging it. The UI screens are captures of the real screens built by the real view
models, not mocks.

---

## 7. Acceptance criteria

| Criterion | Status |
|---|---|
| Mouse click works on all standard buttons/tabs | **Met** — EventSystem added; asserted against the view models |
| Mouse hover feedback exists | **Met** — distinct Hover state, asserted |
| Keyboard / controller navigation still works | **Met** — every station reachable by stepping, asserted |
| Focus visibly obvious | **Met** — corner brackets, readable without colour |
| Active tab/section visibly obvious | **Met** — persistent side notch, survives focus and pointer leaving |
| Pressed / disabled states correct | **Met** — pressed inset; disabled ignores hover and swallows clicks |
| No top-right text overlap | **Met** — two root causes fixed (font line spacing, unmeasured header) |
| No display-name/profile overlap at max lengths | **Met** — asserted at 16 chars, level 999, 999,999 coins |
| No duplicate panel rendering | **Met** — one canvas, one station panel, asserted across all seven |
| No important text clipping at 640×360 | **Met** — asserted per label per station; tab truncation fixed |
| No accidental giant blank areas | **Met** — idle column reframed; largest flat block asserted under 34% |
| Main Menu and Shelter share a visual language | **Met** — both on `UiTheme` / `ScreenLayout` / `UiControl` |
| Shelter reads as a home base, not a debug menu | **Met** — staged Shelter backdrop, four-zone composition |
| Metro floor no longer generic noisy stone | **Met** — slab lattice, family distribution, markings on the detail layer |
| Rustworks no longer uniformly orange | **Met** — calm members asserted <4% warm; orange confined to hazards/machinery |
| Labs no longer scatters green | **Met** — calm members asserted <3% green; vegetation is a colony |
| Calm base + localized detail per biome | **Met** — 67% Base measured across the 63 rooms |
| Larger material shapes over micro-noise | **Met** — 8 px wrapping value noise replaces 2 px speckle |
| All 63 rooms use the revised art | **Met** — 22,606 cells repainted across 63 prefabs |
| Room logic / collision unchanged | **Met** — tile reference only; identical collider types |
| Telegraph readability preserved | **Met** — telegraph-heavy scene per biome |
| No placeholder/debug tiles reintroduced | **Met** — `ReleasePathVisualScanTests` green |
| Biome identity without the depth label | **Met** — >80% pixel difference and the expected brightness order |
| No new test failures, EditMode and PlayMode green | **Met** — 751/752 and 526/526, 0 failed |
| Release build succeeds | **Met** — Succeeded, 0 errors |
| Built-player smoke succeeds | **Met** — full loop, save reloaded |

---

## 8. Known limitations and follow-ups

- **`DungeonHudView.cs:97` and `InventoryView.cs:66` still call `Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")`.**
  That contradicts FINAL_ART_PRODUCTION_SPEC 29.12, which requires the builtin font gone from the release path. It is
  **not** fixed here: the dungeon HUD and the inventory overlay are outside this pass's scope, and switching their
  typeface changes their text metrics and therefore their layout — a change that deserves its own pass with its own
  HUD tests. They do now benefit from nothing; they are simply unchanged. Flagged for a follow-up.
- **The base-floor mottling is deliberately close to flat.** With one tile per family member, any feature loud enough
  to recognise is loud enough to see repeating. The floors carry their structure in the seams, the plate and panel
  edges, the five-member family and the detail layer instead. If more surface character is wanted later, the way to
  get it without reintroducing the grid is more members per family, not more contrast per tile.
- **Non-16:9 windows letterbox rather than reflow.** The layout is a fixed 640×360 frame centred in the canvas, which
  is right for a pixel front-end and means a 4:3 window shows margin above and below rather than a rearranged UI.
- **The double Unity run** described in section 1 is an environment constraint, not a repository one.

---

## 9. Terminal status

`POLISH_PASS_COMPLETE`
