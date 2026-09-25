---
name: coop-expedition-proof-runner
description: How to run the built-player co-op expedition proof (-coop-expedition host|client) and the traps that cost full build+run cycles
metadata:
  node_type: memory
  type: project
  originSessionId: 3c983042-2863-4932-8556-406d3ad0e678
  modified: 2026-09-24T19:16:29.536Z
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

See [[coop-peer-proof-gotchas]] and [[built-player-smoke-seed-flaky-on-mac]].
