---
name: pickup-attraction-steals-interact-prompt
description: An un-takeable pickup pulled onto the player becomes the nearest interactable and blocks every prompt
metadata:
  type: project
---

`PickupAttractor` must gate acquisition on `IAttractablePickup.CanBeCollectedBy(interactor)` — capacity included —
not on `CanInteract`. With a full backpack, a stack that cannot be taken was dragged onto the player, failed to be
collected, and then sat at their feet as the nearest interactable, silently taking the interaction prompt away from
the chest, weapon cache or event object they were standing at.

**Why:** the defect is invisible in a unit test (the pickup "is not collected", which looks correct) and only shows up
as an unrelated-looking failure — the built-player smoke reported `noncombat: <room>: WeaponCache prompt ''` twice.

**How to apply:** any new pull/vacuum/magnet behaviour must ask whether the thing can actually be *taken* before it is
*moved*. Same shape as [[live-run-descend-and-pickup-prompts]] (dropped loot stealing the interact prompt).
