# TASK 180 — Release Network Player Prefab and Live UGS Setup

> **Status:** Approved RUINRAIL completion/release task.
> **Execution mode:** Review-gated completion runner or supervised single-task execution.
> **Game/code language:** English.

## PREREQUISITES

- TASK 179 PASS with design sign-off recorded.
- User may need to link/authorize the Unity Gaming Services project. Never commit secrets.

## GOAL

Replace the TASK-148 release build's fake Host/Join composition with the real NGO + Unity Multiplayer Services/Sessions/Relay release path and author/register the final network player prefab.

## REQUIREMENTS

1. Inspect current `GameApp`, network bootstrap/composition, `FakeMultiplayerServices`, `FakeNetworkDriver`, `UnityMultiplayerServices`, `NgoNetworkDriver`, `NgoPlayerEntityFactory`, `NetworkManager` and `DefaultNetworkPrefabs.asset`.
2. Author the release network player prefab using the already-approved player presentation/gameplay components and required NGO components; register it in the actual release `NetworkManager` prefab list.
3. Wire release boot to real Unity multiplayer adapters when live services are enabled. Test/dev fake adapters may remain only behind explicit non-release/test configuration.
4. Link the Unity project to the correct UGS project/environment using user-authorized tooling/UI. Never write credentials/tokens into source control.
5. Preserve host authority, owner RPC restrictions, transaction idempotency, reconnect grace and player-count rules proven in TASK 001–148.
6. Run local/fake regression plus the repository's `RUINRAIL_LIVE_SERVICES=1` integration check if credentials/network are available.
7. If account/project linkage cannot be completed, stop `BLOCKED_EXTERNAL_SERVICE`; do not call it PASS.

## ACCEPTANCE CRITERIA

1. Release boot no longer silently routes Host/Join through fake services.
2. Final network player prefab exists and is registered.
3. Live-service integration check actually runs successfully, or task truthfully blocks on external service setup.
4. Full regression remains green.
