---
name: audio-silence-verify-outside-unity
description: In-engine audio checks cannot prove audibility; use scripts/probe-audio-session.ps1 and check the default playback endpoint
metadata: 
  node_type: memory
  type: project
  originSessionId: df53a99d-547a-4ad9-b2c2-67f2063efa1a
  modified: 2026-09-18T12:30:49.257Z
---

"The player hears nothing" in RUINRAIL cannot be settled from inside Unity. `isPlaying`, mixer gains and even
`AudioListener.GetOutputData` sample the engine's own graph and stay true when Unity is mixing into a device nobody
is listening on.

**Why:** two passes (2026-09-17 and 2026-09-18) reported healthy audio while the owner heard silence.

**How to apply:**
- Run `./scripts/probe-audio-session.ps1 -Seconds 30` alongside the shipped player. It reads the Windows audio-session
  peak meter for the process on the **current default render endpoint** (Core Audio interop; no ffmpeg on this
  machine, so loopback capture is unavailable).
- On this machine the Windows default playback endpoint was **"Realtek Digital Output" (S/PDIF)** — if nothing is
  connected to that optical port, every application is silent. Check that before suspecting the game.
- Runtime state is dumped by `AudioRuntimeDiagnostics.Capture()`; its `Problems()` list names every in-engine cause.
- Terminal status for an audio task stays INCOMPLETE until a human confirms audibility — that is the prompt's own
  rule, not a hedge. See [[ruinrail-audio-runtime-seams]].
