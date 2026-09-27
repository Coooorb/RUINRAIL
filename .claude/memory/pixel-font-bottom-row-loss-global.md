---
name: pixel-font-bottom-row-loss-global
description: UI text must be created as PixelText (not Text) — plain Text on a pixel-perfect canvas samples the pixel-font atlas on texel boundaries and drops glyph rows
metadata:
  type: project
---

Fixed 2026-09-27. Cause: Unity's text generator places glyph quads half a pixel off-grid with half-texel-padded UVs;
the pixel-perfect canvas rounds them onto whole pixels, so every pixel centre sampled a texel boundary of the
point-filtered atlas and a row was dropped depending on screen Y. `UI/Theme/PixelText` undoes the vertical rounding and
lifts the cell top onto the rect top (face ascent 7, cells −1..+6). `UiBuild.Label` and the HUD `Label` create it.

**Why:** a new text creation site using `AddComponent<Text>()` brings the clipping back on that label.
**How to apply:** create UI text through `UiBuild.Label` (or `AddComponent<PixelText>()`); `PixelFontRenderingTests`
guards cap height / first row = box top. See [[ugui-filled-image-needs-sprite]].
