---
name: ugui-filled-image-needs-sprite
description: A uGUI Image with Type.Filled and no sprite silently draws a full quad and ignores fillAmount
metadata: 
  node_type: memory
  type: project
  originSessionId: df53a99d-547a-4ad9-b2c2-67f2063efa1a
  modified: 2026-09-18T12:30:40.890Z
---

In RUINRAIL's code-built uGUI screens, an `Image` with `type = Image.Type.Filled` but **no sprite** draws a plain
full-size quad: `Image.OnPopulateMesh` falls back to `base.OnPopulateMesh` when `activeSprite == null` and `type`,
`fillMethod` and `fillAmount` are all discarded. This caused the dash-cooldown "static grey block" bug
(2026-09-18) even though `Cooldown01` was correct the whole time, which is why the value-level tests passed.

**Why:** every HUD/menu graphic here is built in code from `UiBuild` primitives, so it is easy to create an Image
without ever assigning a sprite.

**How to apply:** build filled overlays and bars with `UiBuild.Fillable(...)`, which assigns `UiBuild.Solid()`
(a cached 1×1 white sprite). `RoomHudQolTests.TheHudSourceHasNoSpritelessFilledImage_...` fails the suite if any
source under `Assets/Game/Scripts/UI/Hud` declares `Image.Type.Filled` without giving the image a sprite.
See [[hud-top-band-layout-640x360]].
