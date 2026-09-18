# Multiplayer Architecture

> **Status:** Approved design specification unless explicitly marked as tunable.
> **Game language:** English. Planning discussions may be in German, but all player-facing text, code naming, comments, and Claude implementation specs should be English.
## Target

Online solo/duo/trio. Co-op only.

Use Unity's GameObject-oriented multiplayer stack with Netcode for GameObjects and current Unity Multiplayer Services/Sessions + Relay abstractions. Do not pin the design docs to a fragile package version; verify compatible versions at implementation time.

## Host Model

Client-hosted/host-authoritative. No dedicated server required for MVP.

Gameplay code should access Unity multiplayer services through a project-owned service abstraction rather than scattering direct service calls throughout the game.

## Party Limits
Maximum 3 players total.
