---
name: live-run-descend-and-pickup-prompts
description: In live ExpeditionScene tests/smokes descend via run.Vote.Vote(), and a dropped reward becomes the nearest interact prompt
metadata:
  type: project
---

Two traps in live-run PlayMode proofs / SmokeRunner stages (found 2026-09-20):
- `run.Expedition.ChooseTransit(...)` returns false in a real run (the service's local id is not the voter id); descend/return through `run.Vote.Vote(TransitChoice.X)` (+ `ConfirmReturn()` if `AwaitingReturnConfirmation`), i.e. the same path the UI uses.
- After an event/chest drops loot, `PlayerInteractor.TryInteract()` / `run.CurrentInteractionPrompt` target the dropped pickup (`[E] TAKE ...`), not the spent object. Assert idempotency on the object itself (`handle.CanInteract(player)`, `((IInteractionPrompt)handle).PromptFor(player)`), never on a second `TryInteract()`.
- Smoke seeds: the previous-pass containment check is seed-sensitive (seed 23 fails with a 0.119-tile push); seeds 79/31/11 pass. `TestResults/DepthSettingsDescriptionsNonCombatProof/event_seed_scan.txt` lists which seeds place which event kinds on depths 1-2.

**Why:** each cost a build+smoke cycle (~5 min) or a PlayMode filtered run to diagnose.
**How to apply:** reuse the patterns in `DepthSettingsDescriptionsNonCombatProofTests` / `SmokeRunner.DepthSettingsNonCombat.cs`.
