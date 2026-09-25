# RUINRAIL — FINAL RELEASE CLEANUP: CO-OP CLIENT REACTIVE PASSIVES + AIM REBIND DOC

## ROLE

You are performing one narrowly scoped cleanup pass after the completed final release-candidate audit.

The previous audit reached:

`FINAL_RELEASE_CANDIDATE_COMPLETE`

and identified only two remaining cleanup items worth addressing before the owner performs the final manual release playtest:

1. Co-op client accessory reactive hooks on incoming impacts are not fully active.
2. The wording in `technical/116` says all gameplay bindings are rebindable, while Aim is intentionally a fixed pointer/right-stick axis.

This task exists ONLY to close those two items.

Do not broaden this into another release audit.
Do not redesign combat.
Do not rebalance accessories.
Do not refactor networking generally.
Do not touch unrelated systems.

Work autonomously.
Do not ask for approval.
Do not ask intermediate questions.
Inspect the current working tree first.

---

# REPOSITORY SAFETY — CRITICAL

The current uncommitted working tree is authoritative.

NEVER use:

- `git checkout`
- `git restore`
- `git reset`
- `git clean`
- `git stash`
- destructive cleanup
- broad reverts
- discarding unrelated user/worktree changes

Only touch files required for this cleanup.

---

# AUTHORITATIVE CURRENT STATE

The final release audit reported:

## A. Player impact composition is now live

`PlayerImpactReceiver` is configured in shipping runtime.

Resilience and resistance-affix effects are active for Solo and host-owned players.

## B. Remaining co-op client limitation

For co-op clients, incoming-impact accessory reactive hooks are not fully active because the host simulates the client's body and carries the client's stats, but not the client's passive-event hub.

Affected hooks explicitly named by the audit:

- `Anchored`
- `Shock Absorber`
- `Exo Lock`

The result is:

- Resilience / resistance math works
- reactive accessory effects tied to the passive-event hub do not trigger for remote client-owned players
- Solo and host-player behavior are already correct

## C. Aim rebinding wording mismatch

The current controls architecture intentionally treats Aim as:

- mouse pointer position on keyboard/mouse
- right-stick axis on gamepad

It is not a discrete key binding.

The final audit flagged that `technical/116` wording says all gameplay bindings must support rebinding, which conflicts with this intentional fixed-axis behavior.

This is a documentation-contract cleanup, not an input-system redesign.

---

# PRIMARY OBJECTIVES

By the end of this task:

1. Remote client-owned players receive the same valid incoming-impact reactive accessory behavior as Solo/host players.
2. Host authority remains intact.
3. No passive triggers twice.
4. No passive state is trusted from the client when the host can derive/own it.
5. `Anchored`, `Shock Absorber`, and `Exo Lock` behave correctly for remote client-owned players.
6. Resilience behavior remains unchanged.
7. Existing Solo and host behavior remains unchanged.
8. `technical/116` clearly documents that all discrete gameplay bindings are rebindable while pointer/right-stick Aim is an intentional fixed axis.
9. No other release-candidate behavior changes.

---

# PHASE 1 — REVERIFY THE CO-OP PASSIVE GAP

Before editing, reproduce the limitation in the current tree.

Create:

`TestResults/FinalReleaseCleanup/reactive_passives_before.csv`

For each of:

- Anchored
- Shock Absorber
- Exo Lock

record:

- Solo works YES/NO
- Host-owned player works YES/NO
- Remote client-owned player works YES/NO
- Host has remote player's stat provider YES/NO
- Host has remote player's passive state/event hub YES/NO
- trigger source
- current failure point

Do not assume the final-audit observation is still current.

---

# PHASE 2 — FIND THE CORRECT AUTHORITATIVE SEAM

Inspect the existing implementations of:

- accessory passive ownership
- passive event hub
- `PlayerImpactReceiver`
- `PartyLifeRoster`
- co-op member mirrors / host copies
- player stats composition
- equipment/inventory replication
- incoming knockback/stagger/explosion processing
- current host/client combat authority

Use the existing architecture.

Do NOT create a second passive system.

The correct fix should make the host authoritative simulation able to evaluate the remote player's equipped reactive accessory state.

Preferred approaches, in order:

1. compose the existing passive runtime/event source for the host-side mirrored player using authoritative replicated equipment
2. expose the minimal existing passive state needed by `PlayerImpactReceiver`
3. route the existing reactive-passive evaluation through an authoritative per-player runtime context

Avoid:

- client telling host "my passive triggered"
- client-supplied passive ids being blindly trusted
- duplicate local and host application
- syncing transient effect results when the host can compute them

---

# PHASE 3 — ANCHORED

Verify the actual authored Anchored contract from current item/passive definitions and tests.

Do not change its values.

For a remote client-owned player:

- host must know whether Anchored is equipped/active
- the existing trigger condition must be evaluated on the host
- the intended stagger/knockback suppression or one-shot behavior must occur exactly once
- any cooldown/charge/consumption state must remain authoritative
- client presentation must reflect the result naturally through replicated motion/life state

Add a real host/client consequence test.

Create result rows in:

`TestResults/FinalReleaseCleanup/reactive_passives_after.csv`

---

# PHASE 4 — SHOCK ABSORBER

Verify the current authored Shock Absorber behavior.

Do not rebalance it.

For a remote client-owned player:

- explosion/impact classification must be host-derived
- the passive must trigger under the same conditions as Solo/host
- knockback/stagger modification must apply once
- no client-side duplicate suppression
- no change to explosion damage unless the current passive already defines one

Add a real host/client consequence test.

---

# PHASE 5 — EXO LOCK

Verify the current authored Exo Lock behavior.

Do not change its values.

For a remote client-owned player:

- host evaluates the passive from authoritative equipment/runtime state
- the same incoming-impact rule as Solo/host applies
- any cooldown/stateful behavior remains authoritative
- no duplicate application between host mirror and client presentation

Add a real host/client consequence test.

---

# PHASE 6 — EQUIPMENT / PASSIVE STATE REPLICATION CONTRACT

If the host currently lacks enough information to construct the remote player's reactive passive runtime:

add the MINIMUM authoritative equipment/passive-state replication needed.

Requirements:

- stable item/passive ids
- host-owned canonical state
- reconnect restores correct passive composition
- equipment changes refresh the passive runtime
- depth transitions preserve it
- no duplicate passive runtime per player
- no stale passive after unequip
- no client trust hole

Do not replicate the entire inventory again if existing equipment replication already provides the needed source of truth.

Create:

`TestResults/FinalReleaseCleanup/passive_replication_matrix.csv`

Cover:

- join with accessory equipped
- equip during run if currently supported
- unequip/change if currently supported
- depth transition
- disconnect/reconnect
- host
- remote client A
- remote client B in trio

---

# PHASE 7 — DOUBLE-APPLICATION / AUTHORITY TESTS

Explicitly prove:

- Solo: one trigger
- Host player in co-op: one trigger
- Remote client player: one trigger
- no trigger on wrong accessory
- no trigger from client spoof/request
- no duplicate event after reconnect
- no stale passive after equipment change
- no passive effect on another player's impact
- repeated impacts follow the authored cooldown/charge rule

Create:

`TestResults/FinalReleaseCleanup/reactive_passive_authority_matrix.csv`

---

# PHASE 8 — SOLO / HOST REGRESSION

The fix must not alter existing correct behavior.

Verify unchanged:

- Resilience
- Anchored Solo
- Shock Absorber Solo
- Exo Lock Solo
- host-owned co-op player behavior
- player knockback/stagger magnitudes
- enemy/boss/hazard impact data
- PlayerImpactReceiver config
- damage
- stagger thresholds
- knockback distance
- all accessory numeric values

Create:

`TestResults/FinalReleaseCleanup/frozen_impact_passive_check.csv`

---

# PHASE 9 — AIM REBIND DOCUMENTATION CLEANUP

Update the current authoritative controls/input documentation, especially `technical/116` or its current equivalent.

The intended contract should become unambiguous:

- discrete gameplay actions are rebindable
- mouse pointer Aim is positional input, not a key binding
- gamepad Aim is an analog axis, not a button binding
- Aim therefore does not appear as a discrete rebindable action
- this is intentional, not missing functionality
- all other currently approved rebindable gameplay actions remain rebindable

Do NOT change the input architecture.

Do NOT add arbitrary Aim-axis rebinding just to satisfy wording.

If another CURRENT-status doc repeats the inaccurate blanket statement, update that wording too.

Historical reports remain historical.

Create:

`TestResults/FinalReleaseCleanup/input_doc_consistency.csv`

---

# PHASE 10 — VALIDATOR / REGRESSION GUARD

Extend the appropriate existing co-op/final validator.

It should detect regression of the actual contract.

At minimum fail if:

- remote co-op player impact composition lacks reactive passive support
- host cannot derive remote equipped reactive passive state
- remote passive evaluation becomes client-authoritative
- one incoming impact can apply the same reactive passive twice
- Anchored / Shock Absorber / Exo Lock lack a remote-client consequence proof
- controls docs again claim every gameplay input including pointer/axis Aim is a discrete rebindable binding

Do not create a huge new validator if existing validators can be extended cleanly.

A deliberately broken fixture must fail.

---

# PHASE 11 — TARGETED CO-OP RUNTIME PROOF

Run a real host/client proof using the existing local real-network harness.

Duo must prove:

1. Host and client enter same expedition.
2. Remote client owns a player with one of the affected accessories.
3. Host applies the correct incoming impact.
4. Reactive passive changes the outcome exactly as authored.
5. Host sees authoritative result.
6. Client sees replicated result.
7. Repeating the impact respects cooldown/charge semantics.
8. No duplicate effect occurs.
9. Swap to another affected accessory and prove that behavior.
10. Reconnect and prove the passive is restored correctly.

If practical, cover all three passives in one deterministic run.

Create:

`TestResults/FinalReleaseCleanup/duo_reactive_passive_proof.csv`

Trio does not need a full run, but verify two remote clients can hold different reactive accessories without cross-talk.

Create:

`TestResults/FinalReleaseCleanup/trio_reactive_passive_proof.csv`

---

# PHASE 12 — FINAL GATES

Run after the last source/doc change:

- full PlayMode
- full EditMode
- FinalProductionValidator
- StatConsumerIntegrityValidator
- CoopRuntimeCompositionValidator
- FinalReleaseCandidateValidator
- relevant accessory/passive tests
- PlayerImpactReceiver tests
- network authority tests
- reconnect tests
- equipment replication tests
- real Duo reactive-passive proof
- Trio cross-talk proof
- at least 2 consecutive Duo release smokes
- 1 Solo release smoke

Do not rerun-until-green and hide failures.

If Windows module is unavailable:

`Windows x64: NOT RUN — module unavailable`

Do not install it.

If Live UGS is unavailable:

`Live UGS Sessions/Relay: NOT RUN — project/service configuration unavailable`

Do not treat those environment limitations as repository-local failures.

---

# PHASE 13 — REPORT

Create:

`production/FINAL_RELEASE_CLEANUP_REPORT.md`

Required structure:

# RUINRAIL — FINAL RELEASE CLEANUP REPORT

## 1. Final Status
## 2. Scope
## 3. Reactive Passive Gap Reverification
## 4. Authoritative Runtime Fix
## 5. Anchored
## 6. Shock Absorber
## 7. Exo Lock
## 8. Passive / Equipment Replication
## 9. Authority / Double-Application Proof
## 10. Solo / Host Regression
## 11. Aim Rebinding Documentation
## 12. Validator Changes
## 13. Duo Runtime Proof
## 14. Trio Cross-Talk Proof
## 15. Automated Tests
## 16. Release-Smoke Results
## 17. Files Changed
## 18. Remaining External Limitations
## 19. Final Result

---

# REQUIRED ARTIFACTS

Create:

- `production/FINAL_RELEASE_CLEANUP_REPORT.md`
- `TestResults/FinalReleaseCleanup/reactive_passives_before.csv`
- `TestResults/FinalReleaseCleanup/reactive_passives_after.csv`
- `TestResults/FinalReleaseCleanup/passive_replication_matrix.csv`
- `TestResults/FinalReleaseCleanup/reactive_passive_authority_matrix.csv`
- `TestResults/FinalReleaseCleanup/frozen_impact_passive_check.csv`
- `TestResults/FinalReleaseCleanup/input_doc_consistency.csv`
- `TestResults/FinalReleaseCleanup/duo_reactive_passive_proof.csv`
- `TestResults/FinalReleaseCleanup/trio_reactive_passive_proof.csv`
- relevant logs/results

---

# NON-GOALS

Do NOT:

- rebalance Anchored
- rebalance Shock Absorber
- rebalance Exo Lock
- change Resilience values
- change knockback/stagger values
- change player movement
- change enemy attacks
- change boss attacks
- change damage
- change inventory design
- change co-op economy
- change loot
- add new accessories
- add new input actions
- redesign rebinding UI
- add Aim-axis remapping
- alter audio
- alter art/VFX
- alter depth/economy
- alter save architecture
- alter room generation
- refactor unrelated multiplayer systems
- repeat the entire final release audit

---

# FINAL SUCCESS CONDITIONS

This cleanup is COMPLETE only if:

1. The reported remote-client reactive-passive gap is reproduced first.
2. Anchored works for remote client-owned players through host authority.
3. Shock Absorber works for remote client-owned players through host authority.
4. Exo Lock works for remote client-owned players through host authority.
5. No passive triggers twice.
6. No client can spoof its reactive passive.
7. Reconnect restores the correct passive runtime.
8. Equipment/passive changes do not leave stale state.
9. Solo behavior is unchanged.
10. Host-player behavior is unchanged.
11. Resilience behavior is unchanged.
12. Impact/balance values are unchanged.
13. `technical/116` accurately distinguishes discrete rebindable actions from fixed positional/analog Aim.
14. Current-status input docs are consistent.
15. Validator/regression coverage exists.
16. Real Duo runtime proof passes.
17. Trio cross-talk proof passes.
18. Full relevant test gates pass.
19. At least 2 consecutive Duo release smokes pass without retries.
20. No unrelated change was made.

If complete, print exactly:

`FINAL_RELEASE_CLEANUP_COMPLETE`

and:

`production/FINAL_RELEASE_CLEANUP_REPORT.md`

If any repository-local blocker remains, print exactly:

`FINAL_RELEASE_CLEANUP_INCOMPLETE`

and list the exact blocker(s).
