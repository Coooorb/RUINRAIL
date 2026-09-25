---
name: batch-playmode-frame-loops
description: Batch PlayMode runs at thousands of fps; frame-count loops (for i<400; yield null) cover well under a second — use Time.time deadlines
metadata:
  type: feedback
---

In batch PlayMode (`filtered.sh`/harness) frames are uncapped, so a `for (i < 400) { ...; yield return null; }` loop lasts a
fraction of a second: a blaster fired 1 shot in 400 frames and never cooled. Loop on a `Time.time` deadline instead.

**Why:** cost several reruns while diagnosing "Update not running" that was really "no time passed".
**How to apply:** any PlayMode wait on weapon cadence, heat, cooldowns or room timing → time-based deadline or WaitForSeconds.
