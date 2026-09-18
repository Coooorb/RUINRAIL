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

## Feel

Player movement should remain locally responsive. This is a small PvE game, so do not build competitive-shooter-grade anti-cheat/prediction complexity before it is necessary.
