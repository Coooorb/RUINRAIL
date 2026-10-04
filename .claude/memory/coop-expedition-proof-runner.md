---
name: coop-expedition-proof-runner
description: How to run the built-player co-op expedition proof (-coop-expedition host|client) and the traps that cost full build+run cycles
metadata:
  node_type: memory
  type: project
  originSessionId: 3c983042-2863-4932-8556-406d3ad0e678
  modified: 2026-09-28T22:10:55.440Z
---

The co-op completion pass (2026-09-24) added a full-expedition proof to the shipped player:

```
RUINRAIL -batchmode -nographics -coop-expedition host   -coop-port P -coop-size N -seed 11 -coop-out host.json   -savedir <fresh> -logFile <abs>
RUINRAIL -batchmode -nographics -coop-expedition client -coop-index 1 -coop-port P -coop-size N -seed 11 -coop-out client1.json -savedir <fresh> -logFile <abs>
```
Start the host ~3 s before clients; a duo takes ~4 min, a trio ~5 min. Seed 11 has a merchant on D1 (the duo merchant step needs one). Never pass `-offline-multiplayer` (it swaps in the fake driver). Evidence goes to `TestResults/CoopRuntimeCompletion/`.

Traps found the hard way:
- A network player object spawns before the run composes, so its movement/aim/interact/revive components hold the spawn-time reader; `PlayerRig.Attach` must rebind them to the rig reader (it does now).
- The host copy of a client's character must have its `PlayerInteractor` on the remote intent reader, or the client's Interact command reaches nobody.
- Proof commands/reports must go over `CoopRunLink` directly, not through the run's host/client world: Return tears the run down before the last messages.
- A `MonoBehaviour` on a prefab must live in a file named after it (`NetworkDungeonSync` was inside `DungeonNetSync.cs` → "missing script" on the link prefab).
- Stray client processes survive a failed proof; `pgrep -fl RUINRAIL.app` and kill them before the next run.

Secure Relay scenario (2026-09-27): `./scripts/run-coop-expedition-proof.sh <out> 2 6 <port> relay` (seed 6 picks a relay on D1 since 2026-09-28, when the relay joined the equal-weight Event-room pick; 180 was the seed under the old 8% pre-roll; unverified in a real-peer run until the player is rebuilt). The host withholds the client's verdict (`CoopHostWorld.HoldRelayResults`, proof-only) and forces a real token reconnect; expected host 26/26, client 11/11.

Four biomes (2026-09-29): seed 11's D1 is now Overgrown Labs; the proofs assert host/client agreement, never a specific biome, and event kinds/graphs don't depend on the biome, so seeds 11/31/6 stay valid. Cryo Vaults duo proof: `./scripts/run-coop-expedition-proof.sh <out> 2 27 7940` (seed 27: D1 Cryo with a merchant and Subject Zero; host 67/67, client 10/10, ~3.5 min).

See [[coop-peer-proof-gotchas]] and [[built-player-smoke-seed-flaky-on-mac]].

Telegraph scenario (2026-10-03): `./scripts/run-coop-expedition-proof.sh <out> 2 27 <port> telegraph` — host and client log every telegraph (net id, server time, shape, replicated length, shown/lingering time, impacts) in a busy fight and against Subject Zero (phase 1 and forced phase 2), host matches them 1:1; plus a paint-cost benchmark. Expected host 24/24, client 9/9, ~4 min after a ~2 min build. Traps: a raycast buffer of 8 with triggers saturates in the host's crowded physics (lane clip diverged); a no-lock shooter may not fire (client must follow replicated shots); a ~300 ms host frame at the start of the first scripted fight is NOT telegraph work (attributed: GC 0, no telegraph slices) — still unidentified.

Shot-feel scenario (2026-10-03): `./scripts/run-coop-expedition-proof.sh <out> 2 11 <port> shotfeel` — host then client fire a shotgun, blaster, rocket and pistol at a frozen target; both peers log flashes (by kind), gameplay vs presentation-pool projectiles, profiles, impacts and blasts; host requires each exactly once. Expected host 26/26, client 9/9. Trap: a weapon equipped fresh for the proof has no ammo reserve — end each peer on the starter pistol or the boss step deals 0 damage.
