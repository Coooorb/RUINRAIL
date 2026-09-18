# RUINRAIL Final Release Completion Audit — TASK 184

> **Terminal status:** `BLOCKED_EXTERNAL_DEPENDENCY`
> **Date:** 2026-09-15
> **Candidate:** `Builds/Windows64/RUINRAIL.exe` · SHA-256 `e1a9d9e062bbcdb91f76983fae8bb1e4fb8036d55289318ef91033f130d38ecc`

This is the terminal task of the TASK 149–184 completion phase. Every number below comes from a check re-run during this audit.

## 1. Terminal Status and Why

`RELEASE_COMPLETE` requires, per `production/132`: all mandatory asset roles integrated and approved, no unresolved prototype-value or deprecated-API blocker, final loading/error presentation, live Sessions/Relay proven with two clients, target-hardware profiling run, human playtest approved, full regression green, and a fresh build passing smoke.

**Five of those eight are met. Three are not, and none of the three is an engineering defect** — each is an external dependency the runner has no authorized path to supply:

| Blocker | Class | What is needed |
|---|---|---|
| **306 asset roles unfilled** (art, animation, VFX, UI, font, audio) | External content | Original RUINRAIL production art and audio per `art/106` |
| **Live Sessions/Relay unproven** | External service | A Unity Gaming Services project link (owner's account) |
| **No human playtest sign-off** | External human | A person to play it — and final content for them to judge |

Hence `BLOCKED_EXTERNAL_DEPENDENCY` rather than `INCOMPLETE`: the engineering work of this phase is done and green; what remains cannot be produced by the runner.

## 2. Requirement-by-Requirement

### R1 — Full suites re-run

| Suite | Discovered | Passed | Failed | Skipped |
|---|---:|---:|---:|---:|
| EditMode | 723 | **722** | **0** | 1 |
| PlayMode | 503 | **503** | **0** | 0 |

**1,225 passing tests, 0 failures.** The single skipped test is the `RUINRAIL_LIVE_SERVICES=1` Sessions/Relay check (R8). Net +63 tests over the TASK-148 baseline of 1,159.

### R2 — All validators re-run

| Validator | Result |
|---|---|
| `FinalProductionValidator` | **PASS** — 9/9 sections, 0 errors |
| `ContentCountValidator` | **PASS** — 52/52 exact, 0 problems |
| `PresentationValidator` | **PASS** — 8 checks, 0 problems |
| `AssetPipelineValidator` | **PASS** — 6 checks, 0 problems |
| `ArtProductionContract` | **PASS** — 10 checks, 0 problems |
| `CompletionAssetManifest` | **RECONCILED** — 13/14 exact, 1 explained delta |
| `AnimationAssetAudit` | **BLOCKED_EXTERNAL_ASSET** — 0/22 sets, 0/33 weapon sprites |
| `AudioAssetAudit` | contract COMPLETE 53/53; content **BLOCKED** — 0/53 clips |
| `MusicAssetAudit` | routing COMPLETE; content **BLOCKED** — 0/11, 0/6, 0/3 |
| `ReleasePathVisualScan` | **262 placeholder references**, 0 missing references |
| `VisualSliceGate` | **BLOCKED_EXTERNAL_ASSET** — 0/23 slice roles |

### R3 — Content counts exact ✅

**52/52 counts exact.** 33 weapons (11 classes × 22 regular + 11 Legendary), 9 armor, 16 accessories, 10 consumables, 9 normal enemies, 6 Elites (2/biome), 6 Bosses (2/biome), 63 rooms (21/biome with the approved category distribution), 6 dungeon event kinds. **No approved content count changed anywhere in TASK 149–184.**

### R4 — Asset manifest ❌ **NOT MET**

**306 roles: 0 INTEGRATED, 47 PLACEHOLDER, 259 MISSING.**

| Category | Roles | Outstanding |
|---|---:|---:|
| Character sprites + animation sets (22 actors = **1,056 clip roles**) | 44 | 44 |
| Weapon sprites | 33 | 33 |
| Item icons | 72 | 72 |
| Biome tiles / props / lighting (3 biomes) | 36 | 36 |
| World objects and base presentation | 18 | 18 |
| UI skin incl. pixel font | 16 | 16 |
| VFX | 13 | 13 |
| SFX / Music / Stingers / Ambience | 73 | 73 |
| Network prefab | 1 | **0 — delivered** |

TASK-149 reconciliation held throughout: 22 sets × 48 clips, 33 weapons, 72 icons, 53 SFX, 11 music, 6 stingers, 3 ambience all confirmed from code. The one explained delta is **VFX 12 → 13** (`AttackMotion` has five values; `production/135`'s list omitted `Stationary`).

### R5 — PROTOTYPE markers ✅ **34/35 dispositioned**

TASK 179 recorded all 35 in `production/137`: 30 `KEEP_FINAL`, 4 `SPEC_SOURCE`, 1 `BLOCKED_REVIEW`, **0 `TUNE_FINAL`** — because no playtest or profiling evidence existed to justify changing any value, and `production/135` forbids inventing replacements to delete the word. **Zero values changed.**

**1 marker remains by design:** the biome lighting tints, `BLOCKED_REVIEW` pending TASK 159–161 art. It keeps its marker and says so in the code rather than being converted to make this audit read clean.

### R6 — Deprecated Physics2D ✅

**12 `Physics2D.*NonAlloc` calls across 7 files → 0.** The only surviving mention in release code is a doc comment explaining the migration. Each call moved to the supported `ContactFilter2D` overload via `Physics2DQueries.LegacyQueryFilter()`, which reads `Physics2D.queriesHitTriggers` — a default-constructed filter would have set `useTriggers = false` and silently broken every trigger-based pickup, interactable and hazard. Pinned by 5 new PlayMode tests.

**Release warnings classified:** build reports **0 errors, 1 warning**. That warning is `[ServicesCore]: …link your Unity project to a project ID` — **external/configuration**, not project-owned, and it is the R8 blocker surfacing at build time. **Project-owned code warnings: zero.**

### R7 — Loading / transition / error presentation ✅ *(structure)*

Present in the final build. All six scene paths are covered; the load happens only once the screen is fully covered and the cover lifts only after the destination composes, so no raw frame is presented. Progress is **indeterminate** — `SceneManager.LoadScene` exposes no real metric and a fake bar would be a lie. Input is dead from cover start until compose (double-submit guard). Five recoverable error kinds each carry a human-readable message and at least one safe action; the original failure detail is preserved for diagnostics and never shown to the player.

**Caveat:** it renders through the placeholder `UiKit` skin and Unity's builtin `LegacyRuntime.ttf`. Final UI art and font are TASK 163, unfilled.

### R8 — Live Sessions/Relay ❌ **NOT MET**

**No live evidence exists for this candidate or any other.** The UGS project link is absent; `RUINRAIL_LIVE_SERVICES=1` was never satisfiable; no two-client Relay smoke ran. **No mock or fake-transport result is offered in its place** — `production/133` forbids it.

Ready for the moment it unblocks: `PlayerNetworkEntity.prefab` authored and registered with **0 missing scripts**, and release boot composing `UnityMultiplayerServices` + `NgoNetworkDriver` by default.

### R9 — Human playtest ❌ / Hardware profile ✅

**Playtest: NOT PERFORMED.** Beyond needing a person, the artefacts it must judge do not exist — telegraphs have no final frames, biomes no final tilesets, effects are white squares, audio is silent, text uses a placeholder font. `production/HUMAN_PLAYTEST_AND_BALANCE_REPORT.md` was deliberately **not** written; a report of findings from a playtest that did not happen would be fabricated evidence.

**Profiling: RUN**, in the built non-development player on AMD Ryzen 7 9800X3D / RTX 5070 / 31.9 GB / Win 11:

| Metric | Value |
|---|---|
| Frames measured | 21,482 over 90 s |
| Average | 4.17 ms (240.0 fps) |
| p99 / worst | **4.20 ms / 4.26 ms** |
| Frames over 16.7 ms | **0** |
| Heap growth | **−0.109 MB** |
| Live GameObjects | **266, constant** |

No hitch, no leak, no GC pause. Limits are recorded honestly in `production/RELEASE_CANDIDATE_REPORT.md` §7: this is **not** the ≥18-enemy stress case the task describes, the shipping content load is absent, the hardware is high-end, and VSync capped the result.

### R10 — Fresh build + smoke + log audit ✅

- Build: **Succeeded** — exit 0, **0 errors, 1 external warning**, 103.4 MB.
- Smoke: **exit 0** — `MainMenu;Base;Dungeon;Base;`, 9 rooms (Rustworks), banked 25 coins, **save reloaded and verified**.
- Player log: **0 exceptions, 0 NullReferences, 0 missing-reference errors.**

### R11 — Transaction integrity, authority, determinism ✅

Re-audited green via the existing hardening suites within the 1,225-test run: atomic persistence, v1→v2 migration, abandoned-expedition resolution, replay-idempotent Return/Fail/storage/merchant/chest/transit/reconnect transactions, owner-restricted client→server RPC boundaries, host authority and deterministic seeded generation. **No duplication, item-loss or authority defect.** No save format or network semantic changed in TASK 149–184.

### R12 — Platform scope ✅

**Windows x64 standalone is the only V1 release platform.** It is the only target built and the only target validated. No other platform is claimed or implied.

### R14 — No TASK185 ✅

Not created.

## 3. Acceptance Criteria

| Criterion | Status |
|---|---|
| All mandatory fresh checks PASS | **PARTIAL** — every engineering check passes; asset/service/human checks blocked |
| Zero mandatory placeholder/missing asset roles | ❌ **306 outstanding** |
| Live multiplayer actually verified | ❌ **NOT RUN** |
| Human playtest and target-device profile approved | ❌ playtest not performed · ✅ profile run |
| Zero unresolved prototype marker and release-blocking warning | ✅ 34/35 dispositioned, 1 documented `BLOCKED_REVIEW`; 0 owned warnings |
| Fresh build / smoke / log audit PASS | ✅ |
| Report reproducible and truthful | ✅ |

## 4. What This Phase Delivered

Closed in TASK 149–184, with evidence:

1. **A generated, reconciled asset manifest** (306 roles) with per-role delivery path and binding point — replacing prose gap lists.
2. **An asset pipeline** that lets final art replace placeholders without touching gameplay code, with a six-check gate and mandatory provenance.
3. **Deprecated Physics2D API eliminated** — 12 → 0, trigger semantics preserved and pinned by test.
4. **Loading, transition and recoverable-error presentation**, previously absent entirely.
5. **35 PROTOTYPE values dispositioned** with rationale, zero values invented.
6. **The release network player prefab** — and with it a **latent serialization defect** found and fixed: seven components could never have been serialized into any prefab because their class names did not match their file names. Live multiplayer would have failed at spawn time.
7. **Release boot no longer presents fake multiplayer as online play.**
8. **Real in-player profiling on target hardware**, replacing editor measurements that reported unusable zeroes.
9. **+63 tests** (1,159 → 1,225), all green.

## 5. Exact Path to `RELEASE_COMPLETE`

1. **Produce the 306 asset roles** per `art/106` and `production/136`, recording provenance. `production/COMPLETION_ASSET_MANIFEST.md` lists every path and binding point; the twelve `TestResults/ArtBatches/*.md` gates accept them.
2. **Link the UGS project** (*Project Settings → Services*), then run TASK 181: `RUINRAIL_LIVE_SERVICES=1` plus a real two-built-client host/join over Relay.
3. **Re-open TASK 152 / 165 / 176** visual and audio gates against real content.
4. **Resolve the last `BLOCKED_REVIEW`** — biome lighting tints, once TASK 159–161 art exists.
5. **Run TASK 182** — a human plays it and signs off.
6. **Re-run TASK 183** with a genuine ≥18-enemy stress case under final content load.
7. **Re-run this audit.**

Steps 1, 2 and 5 require the project owner. Nothing in them is blocked by engineering.

---

**Terminal status: `BLOCKED_EXTERNAL_DEPENDENCY`.** The engineering baseline is green, the release candidate is stable and leak-free, and every remaining blocker is production content, a service linkage or a human judgment that automation must not fabricate.
