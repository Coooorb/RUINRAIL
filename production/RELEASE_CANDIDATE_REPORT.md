# RUINRAIL Release Candidate Report — TASK 183

> **Status:** Measured on real target hardware. Every number below came from a run that actually happened.
> **Verdict:** Engineering candidate is stable and fast. **Not shippable as V1** — see §7.

## 1. Candidate Identity

| | |
|---|---|
| Executable | `Builds/Windows64/RUINRAIL.exe` |
| SHA-256 | `e1a9d9e062bbcdb91f76983fae8bb1e4fb8036d55289318ef91033f130d38ecc` |
| Build type | **Non-development release** (`BuildOptions.None`, `Debug.isDebugBuild == false` confirmed in-player) |
| Size | 103.4 MB |
| Unity | 6000.3.24f1 |
| Scenes | Bootstrap, MainMenu, Base, Dungeon |
| Build result | Succeeded — 0 errors, 1 warning |

The one warning is the external UGS project-link notice (TASK 180/181 blocker), not project-owned code. **Zero project-owned code warnings** since TASK 177.

## 2. Target Hardware

Reported by the player itself via `SystemInfo`, not by the host shell:

| | |
|---|---|
| CPU | AMD Ryzen 7 9800X3D, 8 cores / 16 threads |
| GPU | NVIDIA GeForce RTX 5070 |
| Graphics API | Direct3D12 |
| System memory | 31,860 MB |
| Motherboard | B850 AORUS ELITE WIFI7 ICE |
| OS | Windows 11 Home 10.0.26200 |

This is a high-end machine. It establishes that the candidate is stable and leak-free; it does **not** establish minimum-spec performance. See §7.

## 3. Method

Profiling runs inside the **built, non-development player with graphics enabled** (`-profile`), not the editor. The TASK-143 numbers came from the editor test runner whose managed-allocation counter reported zeroes and could not carry a release decision; `production/135` explicitly required not relying on it.

Per-frame `Time.unscaledDeltaTime` is sampled after a 120-frame warm-up (shader and asset warm-up is not steady state), and the heap is read with `Profiler.GetMonoUsedSizeLong()`, which **returns real values in the player** — the concern in `production/135` is resolved.

Percentiles are reported because an average hides hitches: a 60 fps average with a 120 ms p99 is a game that stutters.

## 4. Measured Results

### 4.1 Primary run — 1920×1080 windowed, 90 s

| Metric | Value |
|---|---|
| Frames measured | **21,482** |
| Duration | 90 s |
| Average frame time | **4.17 ms** (240.0 fps) |
| p50 | 4.16 ms |
| p95 | 4.19 ms |
| p99 | **4.20 ms** |
| Worst frame | **4.26 ms** |
| 1% low | **237.8 fps** |
| Frames over 16.7 ms (60 fps budget) | **0** |
| Frames over 33.3 ms (30 fps budget) | **0** |

The distribution is extremely tight: **p99 sits 0.03 ms from p50, and the worst frame of 21,482 is 4.26 ms.** There is no hitching, no frame-time spike and no GC pause visible anywhere in the sample.

VSync was on (`vSyncCount = 1`) against a 240 Hz display, so 240 fps is the **refresh ceiling, not the engine's limit**. What this proves is that the frame budget is never missed, with large headroom — not the maximum achievable throughput.

### 4.2 Secondary run — 1280×720 fullscreen, 20 s

| Metric | Value |
|---|---|
| Frames measured | 4,681 |
| Resolution / mode | 1280×720, `FullScreenWindow` |
| Average | 240.0 fps |
| p99 | 4.20 ms |
| Exceptions in player log | **0** |

Fullscreen and a second resolution behave identically. Resolution and fullscreen-mode switching are covered.

### 4.3 Memory and long-run stability

| Metric | Value |
|---|---|
| Mono heap at start | 4.957 MB |
| Mono heap at end | 4.848 MB |
| **Heap growth over 90 s** | **−0.109 MB** |
| `GC.GetTotalMemory` delta | −114,688 bytes |
| Live GameObjects | **266, constant across all 6 samples** |

Heap **shrank** slightly over the session and the live-object count did not move by a single object across six samples spanning 90 seconds. **No leak, no unbounded growth, no allocation creep.** The pooling work from TASK 143 holds up in a real player.

### 4.4 Load and composition times

| Stage | Time |
|---|---|
| Boot → MainMenu composed | 63 ms |
| MainMenu → Shelter composed | 125 ms |
| Shelter → Dungeon composed (incl. generation of 10 rooms) | **276 ms** |

All well under a second. The TASK-178 transition overlay covers each of these, so none is presented as a raw frame.

### 4.5 Functional verification under load

The profiling run does not only render — it completes the loop. After 90 s of sampling it performed a real extraction (`Expedition.Return()`) and a real save, both of which succeeded. A failure in either would have failed the run.

**Built-player smoke (`-smoke`), separate run:** exit 0, `MainMenu;Base;Dungeon;Base;`, 10 rooms, save reloaded and verified, **0 exceptions in the player log**.

## 5. Regression at Candidate

| Suite | Result |
|---|---|
| EditMode | **722 passed / 723 discovered / 0 failed / 1 skipped** |
| PlayMode | **503 passed / 503 discovered / 0 failed / 0 skipped** |
| `FinalProductionValidator` | PASS — 9/9 sections, 0 errors |
| `ContentCountValidator` | PASS — 52/52 exact |
| `PresentationValidator` | PASS — 8 checks, 0 problems |
| `AssetPipelineValidator` | PASS — 6 checks, 0 problems |
| `ArtProductionContract` | PASS — 10 checks, 0 problems |

The 1 skipped test is the live Sessions/Relay check, which requires the UGS link blocked in TASK 180.

## 6. Release Platform Scope

**Windows x64 standalone is the only V1 release platform.** It is the only target built and the only target validated in this repository. No other platform is claimed, implied or supported.

## 7. Known Issues and Honest Limits

Requirement 4 permits fixing only **demonstrated** release-blocking defects. The profiling found none: no stability defect, no leak, no frame-time defect. **No code was changed as a result of profiling.**

These limits are on the profiling evidence itself and must not be read as a performance clearance for V1:

1. **This is not the demanding stress case requirement 1 describes.** It asks for a scenario at least as heavy as trio scaling with ~18 active enemies plus normal VFX/audio/UI. The profiled run is a real expedition at depth 1 with naturally spawned enemies and no combat input driving it. The trio/18-enemy stress numbers remain the TASK-143 **editor** measurements, which requirement 2 says not to rely on. **A genuine in-player stress profile is still outstanding.**

2. **The shipping content load is not represented.** 0 of 22 animation sets, 0 of 33 weapon sprites, 0 of 13 VFX and 0 of 73 audio clips exist. The measured run renders placeholder tiles and white-square effects and plays silence. Final art, animation and audio will add real GPU, memory and audio-voice cost that **this profile cannot predict**.

3. **High-end hardware only.** A 9800X3D with an RTX 5070 says nothing about minimum spec. No minimum-spec target is defined for V1, and none was profiled.

4. **VSync-capped.** 240 fps is the display ceiling. Maximum throughput and true headroom were not measured.

5. **Multi-depth descent was not driven.** `ExpeditionService.Descend` correctly requires the depth's boss to be defeated, which needs real combat input. Rather than bypass a gameplay rule to make a profiling number, the run profiles steady state at one depth. **Multi-depth load and generation cost across depths remain unprofiled in-player.**

6. **No live multiplayer session on the candidate** (requirement 3). Blocked by TASK 181 / UGS linkage.

7. **Keyboard/controller input was not exercised** (requirement 3). The profiling run drives the game through view models, not synthetic input devices.

## 8. Conclusion

The candidate is **stable, leak-free and far inside its frame budget** on the tested machine, and completes the full loop with zero exceptions. As an *engineering* release candidate it is sound.

It is **not a V1 release candidate**: 306 asset roles are unfilled, live multiplayer is unproven, and no human has played it. Those are recorded in TASK 184.
