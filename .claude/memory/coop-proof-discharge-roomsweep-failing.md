---
name: coop-proof-discharge-roomsweep-failing
description: Duo proof Discharge, Room Sweep and Adrenaline failures all FIXED 2026-09-26; baseline is host 66/66, client 10/10 — any of them failing now is a regression
metadata:
  type: project
---

Fixed 2026-09-26; duo proof (`run-coop-expedition-proof.sh <out> 2 11`) now ends host 64/64, client 10/10, reproduced twice.

- **Discharge (proof bug):** the client walked straight at the nearest replica, got pinned against an `Obstacles` collider 2.78 tiles short, and dashed into the wall (both bodies v=0). The shockwave fired correctly out of reach. Its `targets=+3` count was walls/triggers/hazards, because `ShockwaveResolver` counts any untagged collider. The host now places the member on an open lane with `DischargeLane`, inside the room's entry volume, and the client releases move right after `PressDash`.
- **Room Sweep (product bug):** the host player and the member's host copy stood 1 tile apart. Both `PickupAttractor`s pulled the coin toward themselves every step, so it stalled between them, outside both 0.35 collect distances. Now a static claim lets only one attractor pull a pickup at a time.

**Why:** a later session must not treat a new Discharge/Room Sweep failure as the old known issue. It is a regression now.
**How to apply:** room membership now lasts until the body leaves the whole interior (`RoomMembershipVolume`, 2026-09-26); entry/activation still needs the 2-tile-inset `RoomEntryTrigger`. "Adrenaline on a remote client" flake FIXED 2026-09-26 (product bug): the client stands pressed under an obstacle at (24.50, 5.60); its hand-height aim pivot (+0.55) was inside the wall, ShotSolver pulled the spawn back to it, and every shot died on the wall at distance 0 unless an enemy walked to 0.4 tiles. ShotSolver now spawns from the body when the spawn is inside an obstacle (DirectAimHitTests.PressedUnderAWall_*). A new Adrenaline failure is a regression. The grenade steps run after the reconnect step on purpose. See [[coop-expedition-proof-runner]].
