---
name: ruinrail-ui-visual
description: RUINRAIL UI, HUD, menus, pixel art presentation and any visual change. Load when a change affects what the player sees; requires screenshot inspection.
---

# RUINRAIL UI / visual

**Frame.** 640×360 reference resolution, pixel-perfect grid (`docs/design/art/101_PIXEL_GRID_AND_SCALE.md`). Use the
existing skin and pixel font (`Assets/Game/Scripts/UI/Theme/UiTheme.cs`, `UiFont.cs`, builders in UI) — never Unity's
LegacyRuntime font, never placeholder UI, no new ad-hoc styles. Fillable bars need a sprite (`UiBuild.Fillable`).
UI specs: `docs/design/ui/`; controls/rebinding: `docs/technical/116_INPUT_SYSTEM.md`.

**Input.** Every screen works with mouse, keyboard and controller: focus order, default selection, back/cancel,
no dead-end focus. Check navigation, not just clicks.

**Look at it.** Do not judge visual work from code when a capture is possible.
- menus/panels: `UiScreenCapture` (PlayMode, real view models) → `TestResults/PolishPreview/`
- in-world + HUD: `LiveDungeonCapture` → `TestResults/RegressionProof/`
- built player: `-smoke -screenshot file.png`
Loop: capture `before.png` → change → capture `after.png` (scratch under `TestResults/VisualReview/`, overwrite) →
open the image → critique → fix → recapture → stop. Keep permanent screenshots only for real baselines.
Overlay canvases lose their sorting layer when captured — compare like with like.

**Critique checklist.** Composition · visual hierarchy (what reads first) · text readability at 1× · silhouettes vs
background · contrast · spacing/alignment on the pixel grid · clipping/overflow (long item names, all languages of
numbers) · HUD obstruction of play space · interaction prominence (prompts, focus state) · consistency with existing
screens · visual noise · apparent polish.

**Done** = screenshot inspected after the final change, navigation checked, no clipping, no new console errors.
