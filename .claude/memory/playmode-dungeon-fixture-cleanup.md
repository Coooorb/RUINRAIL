---
name: playmode-dungeon-fixture-cleanup
description: "PlayMode fixtures that enter the Dungeon scene must clear scene roots in TearDown, and overlay canvases lose their sorting layer when captured via a camera"
metadata: 
  node_type: memory
  type: project
  originSessionId: 81da767d-c3fc-43cb-b61d-2b2a9953d7bf
  modified: 2026-09-16T20:00:58.027Z
---

Two gotchas from the 2026-09-16 regression pass (see `production/archive/POST_POLISH_REGRESSION_FIX_REPORT.md`):

1. A PlayMode test that plays into the Dungeon scene and does not `Expedition.Return()` leaves the room tilemaps,
   player and camera loaded; later physics tests (ImpactReceiver, LegendarySpecials, NetworkCombat…) then collide with
   that geometry — 192 failures in one run. `DungeonRegressionProofTests.TearDown` destroys every active-scene root
   except the test runner (`IsTestRunner`); copy that pattern for any new dungeon-entering fixture.
2. Screen Space Overlay canvases drop their `sortingLayerName`; when `UiScreenCapture`/`LiveDungeonCapture` switch
   them to Screen Space Camera they land on `Default`, under every world layer. `LiveDungeonCapture` pins them to
   `ScreenUI` during the capture. A capture with UI hidden behind tiles is this, not a game bug.

**Why:** both produced convincing-looking false failures before the cause was found.
**How to apply:** reuse `LiveDungeonCapture` for live-run proof shots; run filtered iterations with Unity's
`-testFilter` but report only harness runs. Related: [[unity-needs-two-runs-smart-app-control]].
