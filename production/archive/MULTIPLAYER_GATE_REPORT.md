# MULTIPLAYER GATE REPORT — TASK 108

> **SUPERSEDED — historical document (2026-09-25).** Current release status: `production/CURRENT_RELEASE_STATUS.md` (detailed audit: `production/archive/FINAL_RELEASE_CANDIDATE_AUDIT.md`). Its PASS was over deterministic doubles (FakeMultiplayerServices / FakeNetworkDriver); real-runtime co-op is proven in production/COOP_RUNTIME_COMPOSITION_COMPLETION_REPORT.md. The body below is kept unchanged as the record of its time.


> **Status:** Hard autonomous checkpoint (production/130_AUTONOMOUS_TASK_RUNNER.md). Generated 2026-09-15 by the autonomous runner from real harness runs; nothing below is inferred.

## Verdict

**PASS (local deterministic networking).** Live Unity Relay / Sessions smoke: **NOT RUN** (no signed-in Unity Gaming Services credentials in the autonomous environment; `RUINRAIL_LIVE_SERVICES=1` not set). No known item/Coin/XP duplication, unintended persistent loss, host-authority bypass or deterministic-sync defect remains open.

## Harness (exact)

```
UNITY_PATH="/c/Program Files/Unity/Hub/Editor/6000.3.24f1/Editor/Unity.exe" bash scripts/run-unity-tests.sh All
```

| Platform | Result |
|---|---|
| EditMode | PASS — 575 passed / 576 discovered (1 skipped = `MultiplayerTerminalTests` live Sessions/Relay check, NOT RUN) |
| PlayMode | PASS — 411 passed / 411 discovered |

Gate scenario report (written by `Assets/Game/Tests/PlayMode/MultiplayerGateTests.cs`): `TestResults/gate_108_multiplayer.md` — **157 checks PASS, 0 FAIL** across party sizes 1, 2 and 3.

## Requirement 1 — scenarios (offline / host+client / multi-client)

Each row is exercised for Solo, Duo and Trio by `MultiplayerGateTests.SoloDuoTrio_HostAuthoritativeExpeditionFlow_PassesEveryGateCheck` over the deterministic doubles (`FakeMultiplayerServices`, `FakeNetworkDriver`, `FakeConnectionEvents`, `RemoteIntentInputReader`, `LootAuthorityService`), plus the dedicated suites listed.

| Scenario | Result | Evidence |
|---|---|---|
| Session host / join by code / party limit 3 / 4th join refused (SessionFull) | PASS | gate rows `session:*`; `NetworkBootstrapTests`, `MultiplayerTerminalTests` |
| Ready / loadout / host-only start / duplicate start idempotent / party size captured | PASS | gate rows `lobby:*`, `start:*`; `PartyLobbyTests` (13) |
| Movement from owner intents, stale intents ignored, dash validated once (duplicate/stale/already-dashing rejected) | PASS | gate rows `motion:*`, `dash:*`; `NetworkMotionTests` |
| Combat authority: client process cannot apply damage/heal; host applies | PASS | gate rows `combat:*`; `NetworkHealthTests`, `NetworkCombatTests` |
| Shared dungeon: clients rebuild the host layout from the seed payload, pool mismatch detected | PASS | gate row `dungeon:*`; `DungeonNetSyncTests` |
| Loot race: one winner, ledger replay, client cannot resolve, item count conserved | PASS | gate rows `loot:*`; `NetworkLootAuthorityTests` |
| Coin pile split once and evenly (remainder rotation), conserved | PASS | gate row `coins:*`; `CoinDistributionTests`, `CoinPickupTests` |
| Boss on the boss HP curve, defeated once, vote opened | PASS | gate rows `boss:*`; `TunnelMawBossTests`, `CoopScalingRuntimeTests` |
| Downed (20 s) → hold-to-revive 4 s at 30% + protection → bleedout Dead (no drop) → spectator → Defibrillator revive | PASS | gate rows `life:*`; `PlayerLifeStateTests` (8), `PlayerReviveTests` (9), `DeadSpectatorTests` (5), `DeadReturnSourcesTests` (13) |
| Solo: 0 HP = Dead + immediate failure, never Downed | PASS | gate row `life: solo`; `PlayerLifeStateTests` |
| Vote: living-only voters, unanimous Descend → Depth 2 for every peer, one Return → party Return, dead-return warning | PASS | gate rows `vote:*`; `TransitVotingTests` (7), `PartyTransitTests` (4) |
| Disconnect: held at risk during the expedition, reconnect rebinds the same entity once, grace expiry → Dead, host loss → failure for every peer, no host migration | PASS | gate rows `disconnect:*`, `session: dropping after extraction`; `DisconnectGraceTests` (6) |
| Live Relay / Sessions smoke | **NOT RUN** | `MultiplayerTerminalTests` live check ignored unless `RUINRAIL_LIVE_SERVICES=1`; no UGS credentials available |

## Requirement 2 — no duplication / loss / unauthorized mutation

| Invariant | Result | Evidence |
|---|---|---|
| Item instance ids unique across the party at start; no instance secured by two peers; the raced prize ends with exactly one peer | PASS | gate rows `start: item instance ids unique`, `extract: no item instance secured by two peers`, `extract: the prize ended with exactly one peer` |
| Item count conserved through the pickup race (backpacks + ground) | PASS | gate row `loot: item count conserved` |
| Coins: pile total equals the sum of shares; each peer banks exactly its carried share once; CoinsLost 0 on extraction | PASS | gate rows `coins:*`, `extract: * banked its carried coins once` |
| XP: committed once per peer, permanent, equal to the recorded awards | PASS | gate rows `xp:*`, `extract: * XP permanent and exact` |
| Replayed Return/Fail on a closed transaction returns the same summary, changes nothing | PASS | gate row `extract: * replayed Return is the same summary`; `ExpeditionServiceTests` |
| Client process cannot apply damage/heal, cannot resolve loot, cannot advance a revive channel or bleedout, cannot start the expedition | PASS | gate rows `combat: a client process cannot mutate health`, `loot: a client cannot resolve loot`, `lobby: a client cannot start`; `PlayerReviveTests`, `PlayerLifeStateTests` |
| Downed/Dead participant refused by the loot authority; Dead never drops gear; teammates cannot salvage | PASS | gate rows `life: downed player refused`, `life: bleedout -> Dead, gear kept, no drop`; `DeadSpectatorTests` |
| Dead player's at-risk state lost on party Return (intentional rule), living peers extract normally | PASS | `DeadSpectatorTests`, `PartyTransitTests` |
| Session loss after extraction cannot re-open or fail the closed transaction | PASS | gate row `session: dropping after extraction` |

## Requirement 3 — scaling and caps

| Party | Threat | Normal HP | Boss HP | Damage | Active cap | Gate detail |
|---|---|---|---|---|---|---|
| Solo | x1.00 | x1.00 | x1.00 (1150) | x1.00 | 10 | `x1 cap 10 spawned 7` |
| Duo | x1.40 | x1.20 | x1.65 (1898) | x1.00 | 14 | `x1,4 cap 14 spawned 12` |
| Trio | x1.75 | x1.35 | x2.20 (2530) | x1.00 | 18 | `x1,75 cap 18` |

Composition: base × depth curve × party multiplier, rounded once (`DepthScaling.ScaledHealth`); captured at expedition start (`ExpeditionState.StartingPartySize`), never re-read from the live roster (`CoopScalingTests`, `CoopScalingRuntimeTests`). Elites and boss summons use the normal curve.

## Requirement 4 — live services

`NOT RUN`. Reason: the autonomous environment has no signed-in Unity Gaming Services account; `MultiplayerTerminalTests.LiveSessionsRelay_*` is `[Ignore]`d unless `RUINRAIL_LIVE_SERVICES=1`. The live adapter (`UnityMultiplayerServices`: `UnityServices.InitializeAsync`, anonymous sign-in, `CreateSessionAsync(...).WithRelayNetwork`, `JoinSessionByCodeAsync`) compiles against the pinned packages (Services Multiplayer 1.2.0, Netcode 2.7.0, Transport 2.6.0) and is exercised only through the fake.

## Defects found and fixed on the way to this gate (TASK 100–107)

- Friendly fire in projectile direct hits / melee (TASK 095–096): fixed, `DamageTeam` checks.
- Boss summons received no depth/party scaling (TASK 101): fixed, normal curve.
- `PlayerEntityBuilder` composed `HealthComponent` before `PlayerDash`, so dash iFrames never reached health on built players (TASK 103): fixed via invulnerability composition + refresh.
- `new SessionRequest()` (readonly struct default) meant MaxPlayers 0, so any parameterless host session refused every joiner; an existing test passed for the wrong reason (TASK 106): fixed, test hardened.
- `NetworkPlayerMotion.SendIntent` sent move/aim only (held fire/special never travelled) (TASK 103): fixed.

## Open items carried forward (not gate blockers)

- NGO scene/prefab wiring (network player prefab, RPC transport for life state, revive progress, votes, reconnect token delivery, `DontDestroyWithOwner` for held players) — assigned to the network scene task.
- PROTOTYPE tunables flagged in the run log: Downed crawl multiplier 0.35, revive/defibrillator reach 1.5 tiles (spec gives no numbers).
- Presentation (art/audio/VFX) placeholders remain `BLOCKED_EXTERNAL_ASSET` per earlier gates.
