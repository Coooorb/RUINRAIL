# Gate Report — TASK 090: Ruined Metro Full-Run Gate

> Generated 2026-09-15 by the autonomous task runner from real harness runs. Run-by-run data: `TestResults/gate_090_metro_runs.md`, `TestResults/gate_090_perf.md`.

## Verdict: engineering **PASS** — external art/audio remain **placeholders** (see §5). Proceed to TASK 091+.

## 1. Automated suites (full harness)

Command: `UNITY_PATH="/c/Program Files/Unity/Hub/Editor/6000.3.24f1/Editor/Unity.exe" bash scripts/run-unity-tests.sh All`

| Suite | Result | Discovered | Passed | Failed | Skipped |
|---|---|---:|---:|---:|---:|
| EditMode | PASS | 522 | 522 | 0 | 0 |
| PlayMode | PASS | 323 | 323 | 0 | 0 |

## 2. Complete seeded solo expeditions (`Assets/Game/Tests/PlayMode/MetroFullGateTests.cs`)

Each run: real 21-room Ruined Metro pool → `DungeonGenerationPipeline` → instantiated room prefabs → `DungeonRoomRuntimeComposer` (encounters, Elite rooms, chests, merchant, events, boss roster) → every room visited (combat cleared, chests opened and picked up, merchant purchase, event resolved, boss defeated, Boss Cache opened, Transit boarded) → **Descend** to depth 2 → same again → **Return to Shelter**.

| Seeds played | Depths each | Rooms visited | Result |
|---:|---:|---:|---|
| 11 (2026, 7, 13, 21, 42, 99, 104, 313, 5, 64, 77) | 2 | 214 | PASS (every run first-round generation, no assembly retries) |

Conservation asserted per run: no duplicate ownership across equipped/backpack/ground at every step; item units conserved across pickups; merchant/event/cache charged or paid exactly once; Carried Coins banked exactly once on Return (Banked untouched mid-run); every carried instance secured once (SafeLoadout has no duplicates); Return replay idempotent; XP committed to the profile; `BossesDefeated == 2`.

Failure path (`FailedExpedition_LosesCarriedLootAndCoins_KeepsBankedAndXp`): carried coins and every carried instance lost exactly once, Banked unchanged, XP kept, Fail replay idempotent — PASS.

## 3. Content coverage

| Item | Coverage | Result |
|---|---|---|
| Room pool | exactly 21 Ruined Metro rooms, 0 rejected (TASK 087 integration test) | PASS |
| Room categories reached in full runs | Start, Combat, Merchant, Event, Loot, Treasure, MedicalRecovery, Boss | PASS |
| Bosses | The Conductor and Tunnel Maw both fought across the seeded runs (arena tag binding) | PASS |
| Elites | Tunnel Stalker fought in a seeded run; Tunnel Stalker (D3) and Railguard (D12) both run as Elite room engagements in the dedicated fixture with depth-scaled HP and XP 250/300 | PASS |
| Events resolved in seeded runs | Supply Signal, Medical Station, Weapon Cache; Cursed Chest, Locked Vault, Broken Machine covered by TASK 077/078 fixtures and the TASK 082 gate | PASS |
| Depth scaling | applied to spawned enemies, Elites and bosses (asserted in Elite fixture; boss curve in TASK 089 tests) | PASS |

## 4. Performance smoke (no optimisation pass)

10 active enemies (solo cap) + 60 pooled projectiles + 40 world pickups over 120 editor test-runner frames: average 0.1 ms, worst 0.8 ms per frame — PASS (smoke budget 50 ms). Not a shipping benchmark; a build-side profile belongs to the production validation tasks.

## 5. External content placeholders (labelled, not hidden)

- **Art:** every Ruined Metro room uses placeholder dev tiles (`Placeholder_Floor/FloorDetail/Wall/Obstacle/Hazard`); enemies, Elites, bosses, pickups and events have no sprites/animations — `BLOCKED_EXTERNAL_ASSET` for final art (presentation tasks).
- **Audio:** no music roles, stingers or SFX exist yet — `BLOCKED_EXTERNAL_ASSET` (audio tasks).
- **VFX:** telegraphs, landing zones, aim lines, burrow emergence are logic-only.

## 6. NOT RUN

- Relay / Sessions live checks (networking begins at TASK 091).
- Release build (TASK 147).
- Co-op scaling in live sessions (party size exercised only through the pure scaling services).

## 7. Notes carried forward

- The gate fixture shortens the Supply Signal to 1.5 s (authored 30 s stays untouched) and funds Carried Coins to exercise paid content; totals therefore exceed a natural run.
- `RoomRuntime` now clears immediately when an engagement is already complete at entry (defensive; surfaced by the gate).
