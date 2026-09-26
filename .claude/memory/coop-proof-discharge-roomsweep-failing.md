---
name: coop-proof-discharge-roomsweep-failing
description: Duo co-op expedition proof (seed 11) deterministically fails 2 steps — Discharge impactsSent=+0 and Room Sweep coinsCollected=False — independent of the inventory drop step
metadata:
  node_type: memory
  type: project
  originSessionId: 9c5f8ead-3365-45b8-8b36-6144500b01e0
  modified: 2026-09-25T22:12:21.911Z
---

As of 2026-09-26 the built-player duo proof (`run-coop-expedition-proof.sh <out> 2 11`) ends host 56/58: "Discharge on a remote client" (client shockwave hits 3 targets but sends 0 impact requests) and "Room Sweep on a remote client" (pile swept, ammo consumed, coin pile never collected). Reproduced identically 3 times, including a build with the new `DropSteps` disabled (54/56), so the inventory swap/drop work did not cause it. Not bisected against the pre-session tree (the tree carries uncommitted earlier passes, e.g. ImpactReceiver `IsInert`).

**Why:** a later session will see these two failures and could wrongly blame its own change or retry until green.
**How to apply:** treat them as a known open issue until fixed; compare the failing set against this before attributing a proof failure to new work. See [[coop-expedition-proof-runner]].

Update (same day, encounter-reward task): reproduced again at host 57/59 and 59/61 (steps added since). "Adrenaline on a remote client" failed once and passed on an identical rerun of the same build — treat it as intermittent (timing), not a regression, but classify with one rerun rather than ignoring it.
