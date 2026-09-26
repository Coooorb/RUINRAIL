---
name: release-smoke-and-dead-seams-2026-09-25
description: "Final release audit tooling (release smoke scripts, FinalReleaseCandidateValidator + frozen baseline, audit generator) and the three dead seams it fixed (player impact receiver, Defibrillator, AutosaveFlusher)"
metadata:
  node_type: memory
  type: project
  originSessionId: 3c983042-2863-4932-8556-406d3ad0e678
  modified: 2026-09-24T22:55:23.053Z
---

Final release-candidate audit (2026-09-25) left this tooling:
- `scripts/run-release-smoke.sh <out> 10 [scenarios]` (fresh/returning/death/cache/longrun), `scripts/run-coop-release-smoke.sh`,
  `scripts/run-smoke-loop.sh` (repro loop, `clock` = unseeded), `scripts/release_smoke_manifest.csv` (critical path → scenario),
  `production/archive/scripts/final_release_audit.py` (archived one-off: built the `TestResults/FinalReleaseAudit/*.csv` matrices).
- `FinalReleaseCandidateValidator` (Editor) aggregates the pass validators and compares `FinalReleaseSnapshot` against the
  committed `production/FINAL_RELEASE_FROZEN_BASELINE.csv`. A deliberate value change must regenerate the baseline via
  `-executeMethod RuinRail.EditorTools.Production.FinalReleaseCandidateValidator.WriteBaselineBatch` — never silently.
- Archived status docs (`production/archive/`) carry a `SUPERSEDED` banner that the validator checks; the current status is
  `production/CURRENT_RELEASE_STATUS.md`.

Dead seams fixed (tests had hidden them by wiring the seam themselves):
- `PlayerImpactReceiver` never got `SetConfig`/`SetEvents` → player knockback/stagger, Resilience, Anchored/Shock
  Absorber/Exo Lock were inert. Now wired in `PlayerRig.ComposeGameplay` (+ host copies in `CoopMemberMirror`).
- Defibrillator: `PlayerRig.ReviveRequester` → `ExpeditionScene.Defibrillator.cs`; co-op client sends `req.revive`,
  host validates and answers `res.revive`, client spends the unit only on acceptance.
- `AutosaveFlusher` was never composed → Shelter autosave points (113) only hit disk at the next explicit save.
  Now on `GameApp`, bound to the open session each frame.

- (cleanup, same day) `EquipmentPassiveRegistrar` was never ticked and `PlayerCombatEvents.DamageTaken` had no producer →
  Anchored recharged never, Exo Lock never fired. Now `EquipmentPassiveTicker` + `health.Damaged → RaiseDamageTaken` in
  the rig; the host's `CoopMemberMirror` runs a registrar admitting only `IncomingImpactMechanics` (anchored,
  shock_absorber, exo_lock), the client rig admits `IsMemberRigMechanic`. (2026-09-25, later) every remaining producer-less event is now wired: body events via `App/PlayerCombatEventProducers`
  (dash/movement/health/swap), weapons via `SetCombatEvents` from `PlayerStatsBinder`, kills via
  `IImpactAttackerFeedback.OnTargetKilled` (+ host→client `res.kill` for members), ammo via `IPickupQuantityHook`.
  Host-resolved member passives = `EquipmentPassiveRegistrar.HostResolvedMechanics`. Proof: `PassiveEventProducerTests`
  + Duo proof step 8c. `PassiveWorldActions` was never supplied (contexts fell back to `.None`); now `App/PlayerPassiveWorld` is passed to the rig's and
  `CoopMemberMirror`'s contexts. Vent/Discharge run owner-side (client → validated hit/impact requests); Arc Stagger and
  Room Sweep are host-resolved (mirror gets its own `PlayerRoomEventsRelay`). Duo proof step "WorldActionSteps".

**Why:** the recurring bug class is "system + tests exist, no shipping caller". **How to apply:** when adding a seam, add a
PlayMode test that goes through the real boot/composition (see `ReleaseSeamCompositionTests`), never a hand-built player.
See [[built-player-smoke-seed-flaky-on-mac]] and [[combat-aim-proof-flaky-on-mac]].
