# Gate Report — TASK 118: Rustworks Full-Run Gate

> Generated 2026-09-15 by the autonomous task runner from real harness runs. Run-by-run data: `TestResults/gate_118_rustworks_runs.md`, `TestResults/gate_118_perf.md`; multiplayer scenario data: `TestResults/gate_108_multiplayer.md`.

## Verdict: engineering **PASS** — external art/audio remain **placeholders** (see §5). Proceed to TASK 119+.

## 1. Automated suites (full harness)

Command: `UNITY_PATH="/c/Program Files/Unity/Hub/Editor/6000.3.24f1/Editor/Unity.exe" bash scripts/run-unity-tests.sh All`

| Suite | Result | Discovered | Passed | Failed | Skipped |
|---|---|---:|---:|---:|---:|
| EditMode | PASS | 594 | 593 | 0 | 1 (live Sessions/Relay check, NOT RUN — no UGS credentials) |
| PlayMode | PASS | 429 | 429 | 0 | 0 |

## 2. Complete seeded solo expeditions (`Assets/Game/Tests/PlayMode/RustworksFullGateTests.cs`, the biome-parameterized TASK 090 battery)

Each run: real 21-room Rustworks pool → `DungeonGenerationPipeline` → instantiated room prefabs → `DungeonRoomRuntimeComposer` (encounters, Elite rooms, chests, merchant, events, boss roster) → every room visited (combat cleared, chests opened and picked up, merchant purchase, event resolved, boss defeated, Boss Cache opened, Transit boarded) → **Descend** to depth 2 → same again → **Return to Shelter**.

| Seeds played | Depths each | Rooms visited | Result |
|---:|---:|---:|---|
| 11 (2026, 7, 13, 21, 42, 99, 104, 313, 5, 64, 77) | 2 | 214 | PASS (every run first-round generation) |

Conservation asserted per run: no duplicate ownership across equipped/backpack/ground at every step; item units conserved across pickups; merchant/event/cache charged or paid exactly once; Carried Coins banked exactly once on Return (Banked untouched mid-run); every carried instance secured once; Return replay idempotent; XP committed to the profile; `BossesDefeated == 2`.

Failure path (`FailedExpedition_LosesCarriedLootAndCoins_KeepsBankedAndXp`): carried coins and every carried instance lost exactly once, Banked unchanged, XP kept, Fail replay idempotent — PASS.

## 3. Content coverage

| Item | Coverage | Result |
|---|---|---|
| Room pool | exactly 21 Rustworks rooms (2/5/4/2/1/2/1/1/1/2), 0 rejected; 200 seeded assemblies (40 seeds × 5 depths) valid and deterministic (`RustworksRoomSetIntegrationTests`) | PASS |
| Room categories reached in full runs | Start, Combat, Merchant, Event, Loot, Treasure, MedicalRecovery, Boss | PASS |
| Bosses | The Foundry Titan and Scrap King both fought across the seeded runs (arena tag binding); phase 2 once at 50%, cache/transit/XP hooks once (`FoundryTitanBossTests`, `ScrapKingBossTests`) | PASS |
| Elites | Scrap Executioner fought in a seeded run; Scrap Executioner (D3) and Crusher Unit (D12) both run as Elite room engagements in the dedicated fixture with depth-scaled HP and XP 325 each; seeded pick never returns a Metro Elite | PASS |
| Hazards | Furnace Grate hazard (Rustworks definition) painted in combat/treasure/boss rooms; Start/Merchant rooms hazard-free (room-set tests) | PASS |
| Events resolved in seeded runs | Supply Signal, Medical Station, Weapon Cache (Cursed Chest, Locked Vault, Broken Machine covered by TASK 077/078 fixtures and the TASK 082 gate) | PASS |
| Depth + party scaling | applied to spawned enemies, Elites (normal curve) and bosses (boss curve); party multipliers 1.00/1.40/1.75 threat, 1.00/1.20/1.35 normal HP, 1.00/1.65/2.20 boss HP, damage unscaled (`CoopScalingTests`, `MultiplayerGateTests`) | PASS |

## 4. Multiplayer state convergence (biome-agnostic, `MultiplayerGateTests` TASK 108)

Session join / ready-start / host-simulated intents / dash validation / client mutation refused / shared dungeon rebuild from payload / loot race with one winner / coin split conserved / downed-revive-dead-defibrillator / vote / reconnect grace / extraction conservation — 157 checks PASS across party sizes 1–3. The dungeon sync path (`DungeonSync.HostGenerate/ClientRebuild`) is pool-generic; Rustworks pool determinism is asserted by `RustworksRoomSetIntegrationTests.Assembly_IsDeterministic_ForTheSameSeedAndDepth`.

Live Relay / Sessions smoke: **NOT RUN** (no signed-in Unity Gaming Services credentials; `RUINRAIL_LIVE_SERVICES=1` unset).

## 5. Engineering vs external presentation status

| Area | Engineering | External asset |
|---|---|---|
| Rustworks rooms (21) | final geometry, sockets, markers, hazards, validator-ready | **BLOCKED_EXTERNAL_ASSET**: Rustworks tileset (floor/wall/detail), machinery/press/pipe/conveyor props, furnace grate art, explosive environmental props, boss arena dressing |
| Elites (Scrap Executioner, Crusher Unit) | stats, movesets, telegraphs, XP, encounter hooks final | **BLOCKED_EXTERNAL_ASSET**: sprites/animation, telegraph VFX, SFX |
| Bosses (The Foundry Titan, Scrap King) | stats, movesets, phase 2, arena binding, hooks final | **BLOCKED_EXTERNAL_ASSET**: sprites/animation, VFX (rocket marks, furnace cone, reactor burn, grenade), boss music/stingers |
| Hazard (Furnace Grate) | damage/tick logic final (PROTOTYPE band 5–8 per 1 s shared with the rail) | **BLOCKED_EXTERNAL_ASSET**: hazard tile art/VFX |

## 6. Known non-blockers carried forward

- PROTOTYPE tunables (documented in the run log): attack timings/radii inside approved bands, furnace hazard band, crawl/revive reach values.
- Boss "less defense" in Scrap King phase 2 is expressed through faster timing (no per-phase resistance field in the shared framework).
- No blocker other than external presentation assets remains for Rustworks.
