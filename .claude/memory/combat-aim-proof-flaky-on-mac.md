---
name: combat-aim-proof-flaky-on-mac
description: "CombatAimCollisionProofTests live run was flaky (clock seed, later waves in the line of fire, pack pile-ups); fixed 2026-09-25 by seed 11 + clearing later waves + one pursuer at wall/door — if it fails now, it is a real regression"
metadata:
  node_type: memory
  type: project
  originSessionId: 3c983042-2863-4932-8556-406d3ad0e678
  modified: 2026-09-24T22:55:23.042Z
---

`CombatAimCollisionProofTests.LiveRun_DirectHit_Assist_AutoReload_NoQuad_Doors_WallsAndDoorContainment` failed in
roughly half of full PlayMode runs (2026-09-19 → 09-24), in isolation too. Three separate causes, all test-side:

1. **Clock seed** — every run built a different dungeon. Now `EnterDungeon` pins `LiveRunSeed = 11`.
2. **Later encounter waves** — destroying the first wave for the frozen-dummy aim proofs lets the room spawn its next
   wave; those enemies were not in the test's `enemies` list and one walked into the line of fire (a second hurtbox
   in `[PROOF] 01 pre-fire … onLine`). Now `ClearOthers()` runs before every reference shot.
3. **Pack pile-up** — with the whole pack chasing, bodies pressing on the one at the wall/door push it ~0.4 tiles into
   the collider for ONE physics step; `EncounterBounds` pulls it back on the next (its documented sub-step correction).
   Wall and door sections now use one melee pursuer with the pack held (same rule as the Charger lane). The pile-up
   transient is a known non-blocking physics issue — never "fix" it by widening the 0.12 bound.

After the fix: 6/6 isolated runs and the full suite passed on 2026-09-25. PlayMode must run before EditMode
(FinalMvpAuditTests reads `TestResults/PlayMode-results.xml`). Diagnostics: `[PROOF] 01 pre-fire`, `[PROOF] 10 wall …
pursuer … worst penetration … against <collider>` in `TestResults/PlayMode-unity.log`. Do not stash with `-u` while
`RUINRAIL.slnx` is untracked. See [[built-player-smoke-seed-flaky-on-mac]].
