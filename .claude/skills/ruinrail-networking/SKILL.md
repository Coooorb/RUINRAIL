---
name: ruinrail-networking
description: RUINRAIL co-op / multiplayer work — NGO host authority, ownership, replication, sessions, revive, transit vote, reconnect. HIGH_RISK by default; load with ruinrail-qa.
---

# RUINRAIL networking

**Model.** 1–3 players, PvE only, no PvP. Host-authoritative NGO over UnityTransport. Specs:
`docs/design/multiplayer/80–86` (authority: `82_NETWORK_AUTHORITY.md`). Code: `Assets/Game/Scripts/Multiplayer/`
(`HostAuthority`, `CoopHostWorld`, `CoopClientWorld`, `CoopMessages`, `*NetSync`), composition in `App/ExpeditionScene.Coop.cs`.

**Authority rules.**
- The host simulates shared state (enemies, loot, damage, economy, room/transit state). Clients send *requests*;
  never trust client-supplied authoritative values (damage, grants, positions of others, inventory results).
- Check the sender owns what it asks to change (wrong-owner requests are rejected, not applied). Add a wrong-owner test
  when adding a request type.
- The host simulates each remote member's body with that member's stats and passives (derived from mirrored
  equipment); effects apply **once** — never on both client rig and host copy.
- Separate local presentation (owner-predicted visuals, UI) from shared simulation. Visual-only replication must not
  mutate gameplay state.

**Verification.** Targeted EditMode/PlayMode first (existing `Coop*Tests`). When the risk is real cross-process
behaviour, run a real-peer proof: `scripts/run-coop-expedition-proof.sh` (seed 11) or `run-coop-release-smoke.sh`.
Both peers need identical NetworkConfig; the host must outlive the client's sample (see memory notes).
Solo must stay unchanged — check it.

**Truth.** Live UGS Sessions/Relay is not configured: report `Live UGS Sessions/Relay: NOT RUN — project/service
configuration unavailable`; direct-address peers are the local proof.
