---
name: ruinrail-gameplay
description: RUINRAIL gameplay work — combat, weapons, enemies, items, affixes, Legendary passives, stats, economy behaviour, game feel. Load for gameplay bugs or small gameplay features (pair with ruinrail-qa).
---

# RUINRAIL gameplay

**Balance is frozen.** Values in `production/FINAL_RELEASE_FROZEN_BASELINE.csv` and the design docs are accepted.
Do not rebalance, "improve" numbers or add mechanics unless the task says so. A missing gameplay parameter → config
value, labelled temporary, reported.

**Shipping composition is the product.** A system that exists and passes unit tests but is never composed by the
shipping runtime is a bug. Trace the path from the composition roots before calling anything done:
- player rig: `Assets/Game/Scripts/App/PlayerRigComposer.cs`, visuals `PlayerVisualComposer.cs`
- expedition: `App/ExpeditionScene*.cs`, app/scene flow `App/GameApp.cs`
- passives/events: `App/PlayerCombatEventProducers.cs`, `App/PlayerPassiveWorld.cs`, `App/EquipmentPassiveTicker.cs`
- co-op host copy of a member: see `ruinrail-networking`

**Consumer awareness.** Every stat, affix, attribute and passive needs a live consumer. When touching one, find its
producer *and* consumer (`rg` the id/stat name). `StatConsumerIntegrityValidator` guards this; keep it green.
Class tuning stays in data tables — an EditMode test forbids `WeaponClass.X` switches under `Combat/`.

**Consequence testing.** Assert the in-world effect (damage dealt, enemy staggered, coins granted, ammo consumed),
not that a method was called. Prefer existing PlayMode fixtures near the system (`rg -l <System> Assets/Game/Tests`).

**Feel loop** (weapons, knockback, hit response, telegraphs, readability): change → run → observe (PlayMode run,
capture via `LiveDungeonCapture`, logs) → critique timing/feedback/readability → fix → verify. Judge hit feedback,
wind-up/recovery timing, knockback distance and enemy readability at runtime, not from code. Never widen a tolerance
to make a test pass.

**Scope.** One mechanic per task. No speculative rebalance, no refactor of neighbouring working systems.
