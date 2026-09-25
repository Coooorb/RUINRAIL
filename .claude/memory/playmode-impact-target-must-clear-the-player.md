---
name: playmode-impact-target-must-clear-the-player
description: A PlayMode ImpactReceiver target placed at the player's position (or reused across loop iterations) is shoved by collider overlap and under-measures knockback by half; give each one its own lane away from the origin
metadata:
  node_type: memory
  type: feedback
---

In `StatConsumerRuntimeTests.ImpactRowFor` the first loop iteration put its dummy enemies at `x = 0`, where the rig's
player also stands. The measured displacement came out at exactly half the correct value (0.75 instead of
`6 knockback x UnitsPerKnockbackPoint 0.25 = 1.5`) while later iterations at `x = 40, 80, …` measured exactly right
(rocket 3.0, spear 2.0). Reusing the same coordinates across iterations of one `[UnityTest]` produced the same class
of error: bodies from the previous iteration are still alive (teardown runs per test, not per iteration) and lean on
the new ones, which made a zero-knockback knife appear to move 0.04 tiles.

**Why:** the dummy is a Dynamic `Rigidbody2D` with a `CircleCollider2D`. Overlapping another collider makes Unity's
depenetration move it, and that motion is indistinguishable from knockback when the test measures start-to-end
distance. `ImpactReceiver` drives knockback by setting `linearVelocity` each `FixedUpdate`, so it cannot detect or
compensate for the overlap.

**How to apply:** give each measured target its own lane clear of the player and of every other iteration
(`var lane = 20f + _impactLane++ * 40f;`). A displacement that is a clean fraction of the expected value is the tell —
check for collider overlap before suspecting the impact math. See also [[playmode-dungeon-fixture-cleanup]].
