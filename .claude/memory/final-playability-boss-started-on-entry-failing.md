---
name: final-playability-boss-started-on-entry-failing
description: FinalPlayabilityProofTests.LiveRun_HeldWeapon_... fails at "the boss engagement started on entry" — predates the encounter-reward work; likely the boss intro hold
metadata:
  type: project
---

As of 2026-09-26 `FinalPlayabilityProofTests.LiveRun_HeldWeapon_Replica_HealthBars_RoomEntry_Pause_Inventory_AndCursors` fails deterministically at `Assert.IsTrue(bossBinding.Boss.IsStarted, "the boss engagement started on entry")`. It still fails with the encounter-reward Boss Cache change reverted, so that change is not the cause. Most likely cause (not confirmed): the boss intro hold (BossEngagement introHoldSeconds / BossIntroSequence) added earlier the same session — the test asserts IsStarted right after the teleport, before the intro ends.

**Why:** a later task touching bosses or rooms will see this failure and could blame itself.
**How to apply:** check this before attributing it; the fix belongs to the boss-intro work (the test likely needs to finish/skip the intro, e.g. BossIntroSequence.Current?.Finish(), before asserting). See [[coop-proof-discharge-roomsweep-failing]].
