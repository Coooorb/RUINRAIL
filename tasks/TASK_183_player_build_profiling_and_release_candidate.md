# TASK 183 — Player-Build Profiling and Release Candidate

> **Status:** Approved RUINRAIL completion/release task.
> **Execution mode:** Hardware/release gate.
> **Game/code language:** English.

## PREREQUISITES

- TASK 182 human playtest/balance gate approved.

## GOAL

Profile the real Windows player on intended target hardware, address release-blocking performance/stability defects, and produce the signed release candidate.

## REQUIREMENTS

1. Profile a non-development or profiling-equivalent Windows player on intended target hardware using a stress case at least as demanding as trio scaling / ~18 active enemies plus normal VFX/audio/UI.
2. Measure frame time/FPS distribution, CPU hotspots, GC/allocation spikes, memory growth, scene/load/generation times and long-run stability. Do not rely on the TASK-148 Mono allocation counter that returned non-useful zeroes.
3. Exercise keyboard/mouse and controller, supported resolution/fullscreen modes, save/quit/restart, multi-depth expedition, return/fail, and at least one live multiplayer session on the candidate.
4. Fix only demonstrated release-blocking performance/stability defects; preserve gameplay semantics.
5. Build fresh Windows x64 non-development release candidate and run built-player smoke + log audit.
6. Record whether Windows x64 is the only V1 release platform. Do not claim unbuilt platforms.
7. Produce `production/RELEASE_CANDIDATE_REPORT.md` with hardware, settings, metrics, known issues and exact candidate identifier/hash if available.

## ACCEPTANCE CRITERIA

1. Real player-build profiling ran on intended hardware.
2. No release-blocking performance/memory/stability defect remains.
3. Release candidate build and built-player smoke PASS.
4. Platform support statement is truthful and explicit.
5. Full regression green.

## EXTERNAL GATE

If intended target hardware is unavailable, stop `BLOCKED_EXTERNAL_HARDWARE`.
