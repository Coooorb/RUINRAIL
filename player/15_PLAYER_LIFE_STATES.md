# Player Life States

> **Status:** Approved design specification unless explicitly marked as tunable.
> **Game language:** English. Planning discussions may be in German, but all player-facing text, code naming, comments, and Claude implementation specs should be English.

## Read With

- `multiplayer/84_DOWNED_REVIVE_DEATH.md`
Player life states are `Alive`, `Downed` (co-op only), and `Dead`.

## Solo

At 0 HP: immediate death and expedition failure. There is no solo Downed state. Defibrillators do not drop in solo.

## Co-op

At 0 HP while another teammate is alive: enter Downed state. Detailed timings and rules are in `multiplayer/84_DOWNED_REVIVE_DEATH.md`.

A generic Health component should not own co-op Downed logic. Player-specific life-state logic handles Downed/Dead transitions.
