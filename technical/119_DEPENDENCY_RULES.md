# Code Dependency Rules

> **Status:** Project control document.
> **Game language:** English.

Keep dependencies directional and understandable.

- Core/data abstractions should not depend on concrete UI.
- Gameplay can expose events/state; UI observes it.
- Persistence serializes plain data and resolves stable definition IDs; it should not serialize scene objects.
- Dungeon generation selects definitions/encounters; it does not contain enemy AI implementation.
- Networking validates/synchronizes gameplay state but should not become the owner of all game design logic.
- Unity service APIs should be wrapped by project services at the multiplayer boundary.
- Editor-only validation code belongs in editor assemblies and must not leak into runtime builds.

Avoid circular assembly dependencies. If two systems need each other, introduce a small interface/data boundary instead of making both reference concrete implementations.
