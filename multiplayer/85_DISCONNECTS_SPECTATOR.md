# Disconnects and Spectator Mode

> **Status:** Approved design specification unless explicitly marked as tunable.
> **Game language:** English. Planning discussions may be in German, but all player-facing text, code naming, comments, and Claude implementation specs should be English.
## Dead Spectator

Dead players spectate living teammates through a follow camera and can cycle between teammates. No free-roam camera that scouts undiscovered areas.

A Dead player may continue into the next depth as spectator if the living team descends. The team can attempt to revive them later through a Medical Station or Defibrillator.

## Client Disconnect

Initial reconnect grace target: ~60 s. A disconnected character remains represented/at risk during the grace period. If the player does not reconnect, treat them as Dead; their gear is not dropped to teammates.

## Host Disconnect

For MVP, host disconnect ends the expedition/session for everyone and is treated as expedition failure for at-risk carried state. Host migration is explicitly post-MVP.

## Solo Quit / Crash Rule

A solo expedition is not resumable after a full application restart in MVP. Quitting during an expedition results in expedition failure/at-risk loot loss, preventing Alt+F4 extraction exploits.
