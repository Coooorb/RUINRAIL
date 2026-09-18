# Sessions and Join Codes

> **Status:** Approved design specification unless explicitly marked as tunable.
> **Game language:** English. Planning discussions may be in German, but all player-facing text, code naming, comments, and Claude implementation specs should be English.
## Base Flow

At the Multiplayer Terminal:
- SOLO.
- HOST CO-OP.
- JOIN CO-OP.

Host creates a session and receives a short join code. Friends enter the code to join.

## Ready Flow

Each player manages their own loadout, then marks Ready. Only the host can start the expedition when all connected players are Ready. Changing loadout after Ready automatically clears that player's Ready state.

Players may be represented visually in the base, but synchronizing a fully interactive shared hub is not required to be an MVP blocker if party/loadout/ready flow works reliably.
