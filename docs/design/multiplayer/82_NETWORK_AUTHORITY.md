# Network Authority

> **Status:** Approved design specification unless explicitly marked as tunable.
> **Game language:** English. Planning discussions may be in German, but all player-facing text, code naming, comments, and Claude implementation specs should be English.
## Host-Authoritative Gameplay

The host decides/validates:
- Run/dungeon seed.
- Room graph/selection.
- Enemy spawning.
- Enemy AI state.
- Damage application/validation.
- Enemy death.
- Chest opening and loot rolls.
- World item pickup validity.
- Boss state.
- Downed/death/revive state.
- Transit decision result.
- Depth progression.

Clients send inputs/requests such as move/aim/fire/interact/open/pickup; they do not authoritatively declare loot or kills.

Another peer's shots and grenade throws are presentation only on every other peer: each is drawn from the thrower's announcement — grenades after the host validates the throw (a grenade in its catalog, a living member at that position, a landing inside the throw range) — with its real path, landing effect and lasting area, and never simulated a second time. Damage and zones exist only in the thrower's own run and reach the host as validated requests.

## Feel

Player movement should remain locally responsive. This is a small PvE game, so do not build competitive-shooter-grade anti-cheat/prediction complexity before it is necessary.

The owner predicts its own movement from its intents; the host simulates the member from the same intents and publishes the position with the intent sequence that produced it. The owner compares that position with where it predicted itself after the same intent: errors under 0.05 tiles are ignored, larger ones are eased out (10/s, no visible jump), and errors beyond 0.75 tiles snap. Drift therefore never persists, so a client is shown where the host judges its hits (2026-10-03: drift below the old snap tolerance, e.g. a host copy shoved by enemy bodies, used to persist and let a client see itself outside a telegraph it was hit by).
