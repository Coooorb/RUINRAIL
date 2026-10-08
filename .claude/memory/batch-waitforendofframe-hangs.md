---
name: batch-waitforendofframe-hangs
description: WaitForEndOfFrame never resumes under run-unity-tests.sh (-nographics) — the PlayMode run hangs forever
metadata:
  node_type: memory
  type: feedback
  originSessionId: 219ced19-a9db-4b13-a441-ca060b30c729
  modified: 2026-10-08T20:32:41.652Z
---

`yield return new WaitForEndOfFrame()` in a PlayMode test never resumes under the batch harness
(`run-unity-tests.sh` passes `-nographics`): the run hangs with no timeout (seen 2026-10-08, DashFeelLiveTests).

**Why:** cost a 10-minute stall plus a killed Unity process; the killed run left an
`Assets/InitTestScene<guid>.unity(+.meta)` behind that must be deleted, not committed.

**How to apply:** to capture after this frame's LateUpdate (e.g. effects a presenter emits in LateUpdate), use one
extra `yield return null` before `LiveDungeonCapture.Capture`. Related: [[batch-playmode-frame-loops]].
