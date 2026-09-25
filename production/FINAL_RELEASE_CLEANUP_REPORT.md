# RUINRAIL — FINAL RELEASE CLEANUP REPORT

> Date: 2026-09-25. Scope: the two cleanup items left by `production/FINAL_RELEASE_CANDIDATE_AUDIT.md` — co-op client
> reactive armor passives on incoming impacts, and the Aim rebinding wording. Unity `6000.3.24f1`, pinned versions
> unchanged, no destructive git operation, no value changed. Evidence: `TestResults/FinalReleaseCleanup/`.

## 1. Final Status

**FINAL_RELEASE_CLEANUP_COMPLETE.** Anchored, Shock Absorber and Exo Lock now act for remote client-owned players through
the host's authoritative simulation, exactly once, from host-owned state; the controls documentation states the real
rebinding contract and a validator guards both. Real Duo and Trio proofs, 2 consecutive Duo release smokes, the Solo
release smoke, full PlayMode (PASS — 846/846 passed, 0 failed, 0 skipped) and full EditMode (PASS — 978/980 passed, 0 failed, 2 skipped (environment/manual)) pass.

One correction to the brief: re-verification showed the three passives were **not** correct for Solo/host either (below).
Fixing the remote client alone would have copied a broken contract, so the shared passive clock and damage signal were
wired; this is the only Solo/host behaviour change, and it brings those passives to their authored contract.

## 2. Scope

In scope and done: the three incoming-impact armor passives (the audit called them "accessory" hooks — they are the
Legendary passives of Riot Armor, Blast Suit and Reinforced Exo-Rig) for remote co-op members, the minimum composition to
make them host-authoritative, their proofs and a regression guard; the technical/116 rebinding wording (+ ui/90).
Not touched: any value, input architecture, rebinding UI, networking beyond the member mirror, other passives' logic.

## 3. Reactive Passive Gap Reverification

`reactive_passives_before.csv`, measured on the unmodified tree by a temporary probe (`probe_before_raw.csv`, real boot,
live run + the host's `CoopMemberMirror` for a remote member; the probe file was removed afterwards):

| Passive | Solo / host (before) | Remote client (before) | Failure point |
|---|---|---|---|
| Anchored (Riot Armor) | negated the first stagger, then never again (1 negation over 9 s; authored 8 s cooldown → 2) | 0 | `EquipmentPassiveRegistrar.Tick` never called in shipping code; the host copy has no event hub/passive |
| Shock Absorber (Blast Suit) | works | 0 | host copy has no event hub/passive |
| Exo Lock (Reinforced Exo-Rig) | never (+0% after a 25-damage hit) | 0 | `PlayerCombatEvents.DamageTaken` has no producer; no tick; no host-side passive |

The host already held the member's stat provider (mirrored loadout + ranks) but no passive state or event hub.

## 4. Authoritative Runtime Fix

Using the existing passive system only (no second passive system, no client-reported triggers):

- `EquipmentPassiveRegistrar` (existing) gained an optional **admit** filter, the named set `IncomingImpactMechanics`
  (anchored, shock_absorber, exo_lock) with `IsIncomingImpactMechanic` / `IsMemberRigMechanic`, and a change-aware
  `Refresh()` (re-reads armor/accessory after a snapshot; an unchanged item instance keeps its running passive, so
  re-sending equipment cannot reset a cooldown).
- `EquipmentPassiveTicker` (new component) drives the registrar every frame — the registrar's documented "drive Tick from
  the player" that nothing did. Scaled time: a paused solo world pauses the passives.
- `PlayerRig`: ticks its registrar and raises `DamageTaken` from the player's authoritative `HealthComponent.Damaged`.
- **Host copy of a remote member** (`CoopMemberMirror`): its own `PlayerCombatEvents` hub on the copy's
  `PlayerImpactReceiver`, `Damaged → DamageTaken`, and a registrar built on the **mirrored equipment** admitting only the
  incoming-impact mechanics; ticked on the host; refreshed on every applied inventory snapshot; disposed with the mirror.
- **Co-op client's own rig**: `PassiveAdmit = IsMemberRigMechanic` — it runs every other passive but never these three
  (on the client a replicated health drop also raises `Damaged`, so running Exo Lock there would trigger it twice).

Authority: impacts are classified (stagger / explosion / damage) and resolved only on the host's copy; cooldowns and
buffs exist only there; no client→host message kind can trigger a passive. The equipment itself is the member's own
inventory as reported by snapshot — the existing co-op ownership model (each peer owns its inventory); the host derives
the mechanic from the item definition, never from a client-supplied passive id.

## 5. Anchored

Authored: fully negate one incoming stagger every 8 s, damage still applies (unchanged). Remote client (Duo proof): 1st
host-applied stagger negated, 2nd staggers while on cooldown, negated again after 8 s; the client's own rig reports no
Anchored. PlayMode: `RemoteMember_HostCopy_…`, `Solo_IncomingImpactPassives_…`. Result rows: `reactive_passives_after.csv`.

## 6. Shock Absorber

Authored: ignore knockback caused by explosions, damage still applies (unchanged, stateless). Remote client: two
host-classified explosion knockbacks ignored (host body moved 0.00), no stale Anchored after the swap, an ordinary
knockback still moves the body and the client sees it (owner snaps to the host position). Explosion damage untouched.

## 7. Exo Lock

Authored: a hit of 20+ final damage grants +30% stagger and knockback resistance for 5 s, 8 s cooldown (unchanged).
Remote client: a 19 hit does nothing, a 25 hit grants the buff once (0% → 30%), a second hit inside the cooldown does
not extend it (expired on schedule), after 8 s it triggers again. The resistance is capped at 50% by the existing global
stat caps (the Exo-Rig's own +25% plus the buff reaches the cap in the Solo probe — a cap, not a change).

## 8. Passive / Equipment Replication

`passive_replication_matrix.csv`: no new replication was needed — the member's existing `InventorySnapshotMessage`
(already mirrored for loot/stat validation) is the source of truth; `Refresh()` applies it. Covered: join with the armor
equipped, equip during the run, swap (no stale passive), unequip, same armor re-sent (cooldown kept), depth transition
(the mirror is created once per run; the D2 steps used it), disconnect/reconnect (same mirror re-keyed: one ticker entry,
one passive, triggers once), host, remote client A, remote client B (trio).

## 9. Authority / Double-Application Proof

`reactive_passive_authority_matrix.csv`: one trigger in Solo, for the host player and for a remote client; no trigger on
the wrong armor (and Heavy Plate's Last Stand is never run on the host copy); no spoofable client message; no duplicate
after reconnect; no stale passive after an equipment change; no effect on another member's impact (trio); cooldown rules
respected on repeated impacts.

## 10. Solo / Host Regression

`frozen_impact_passive_check.csv`: every numeric value unchanged — Anchored 8 s; Exo Lock 20 / 30% / 5 s / 8 s; stagger
threshold 10 / recovery 5 / duration 0.6 / immunity 1; knockback 0.25 per point / max 4 / 0.15 s / min 0.05; Resilience;
the PlayerImpactReceiver config; the full frozen release snapshot still equals `FINAL_RELEASE_FROZEN_BASELINE.csv`.
**Behaviour change (intended, authored contract restored):** Solo/host Anchored now recharges after 8 s and Exo Lock now
triggers. Because the registrar clock is shared, the other passives whose trigger already had a shipping producer — Second
Pulse, Wallbreaker, Arc Stagger — now also run their authored timers/cooldowns instead of freezing once triggered.

## 11. Aim Rebinding Documentation

`input_doc_consistency.csv`. technical/116 now has a **Rebinding Contract**: every discrete binding is rebindable (all
button actions and the four keyboard Move directions); **Aim is positional/analog input — mouse pointer position /
right-stick axis — not a key binding, fixed by design, not missing functionality**; the gamepad Move stick is fixed; Pause
is fixed so the menu is always reachable. ui/90 (CONTROLS) matches. The release audit's two notes are annotated as
resolved. No input code, action or rebinding UI changed.

## 12. Validator Changes

`FinalReleaseCandidateValidator` extended (55 rules): **reactive passives** — remote member composition, host derives the
member's passive from mirrored equipment (+ snapshot refresh), host-authoritative/applied once (client rig excludes the
three, no mechanic admitted on both sides, no passive-trigger client→host kind), remote-client consequence proofs present
for all three (PlayMode test + built-player proof step); **rebinding contract** — technical/116 must not claim every
binding is rebindable, must state the fixed Aim axis, and the rebinder's real fixed bindings must equal the documented
set (Keyboard&Mouse: Aim, Pause; Gamepad: Move, Aim, Pause). The deliberately broken fixture fails both new rule
families (and every subject of the passive rule).

## 13. Duo Runtime Proof

`duo_reactive_passive_proof.csv` — real host + client processes (seed 46), the passive steps inside the full expedition
proof: host 50/50 steps, client 6/6 — Anchored (negations +2: first, refused on cooldown, again after 8 s), Shock Absorber (explosions negated +2, host body moved 0.00; ordinary hit moved it 1.35 and the client snapped 8.75 → 8.04), Exo Lock (0% → 30%, expired after 5 s, re-triggered after 8 s), after-reconnect (same passive, 1 ticker entry, 1 passive, triggers once); every equip ack reported `rigPassives=[]` on the client.

## 14. Trio Cross-Talk Proof

`trio_reactive_passive_proof.csv` — three processes: client 1 wears Riot Armor, client 2 Blast Suit; staggers negated
only on client 1, explosion knockback ignored only on client 2 (host 39/39, clients 5/5 + 5/5; client 1: staggers negated +1, explosions +0; client 2: staggers +0, explosions +1).

## 15. Automated Tests

Run after the last source/doc change:

- **PlayMode: PASS — 846/846 passed, 0 failed, 0 skipped** (new: `CoopReactivePassiveTests`, 4 tests)
- **EditMode: PASS — 978/980 passed, 0 failed, 2 skipped (environment/manual)** — skipped by design: `D1AmmoBlasterFineTuneTests.CalibrateLightAmmoQuantity` (manual) and
  `MultiplayerTerminalTests.LiveSessionsRelay_IntegrationCheck_OrNotRun` (live UGS, NOT RUN — environment).
- Validators in the EditMode gate: FinalProductionValidator, StatConsumerIntegrityValidator,
  CoopRuntimeCompositionValidator, FinalReleaseCandidateValidator (55/55) — all PASS; armor/accessory passive,
  PlayerImpactReceiver, network authority, reconnect and equipment suites inside the two runs.

## 16. Release-Smoke Results

Final build: Duo release smoke **2/2 consecutive** (host 50/50, client 6/6 each), Solo release smoke fresh / returning /
death / cache **1/1 each**, Duo proof with the passive steps and Trio proof as above. No retries.

## 17. Files Changed

Runtime: `Items/Passives/EquipmentPassiveRegistrar.cs`, `App/EquipmentPassiveTicker.cs` (new), `App/PlayerRigComposer.cs`,
`App/ExpeditionScene.cs`, `App/ExpeditionScene.Coop.cs`, `App/CoopExpeditionProof.cs`. Editor:
`Production/FinalReleaseCandidateValidator.cs`. Tests: PlayMode `CoopReactivePassiveTests.cs` (new); EditMode
`FinalReleaseCandidateValidatorTests.cs`. Docs: `technical/116_INPUT_SYSTEM.md`, `ui/90_UI_UX_OVERVIEW.md`,
`production/FINAL_RELEASE_CANDIDATE_AUDIT.md` (annotations), this report. Script: `scripts/final_release_cleanup_matrices.py`.

## 18. Remaining External Limitations

- `Windows x64: NOT RUN — module unavailable`
- `Live UGS Sessions/Relay: NOT RUN — project/service configuration unavailable`
- Observed, not changed (existing policy): a host-side knockback below the owner's frozen 0.75-tile reconciliation
  tolerance is not mirrored on the client's screen (a 0.73-tile hit in the first proof iteration, kept under
  `iteration_duo_sub_tolerance_check/`); larger ones snap the owner.
- Found, out of this scope: most other `PlayerCombatEvents` (Dashed, EnemyKilled, HealthChanged, WeaponSwapped,
  ReloadCompleted, weapon/blaster/bow/ammo hooks …) still have no shipping producer, so Legendary passives listening only to
  them remain inert in every mode. Not a regression; a follow-up for the owner.

## 19. Final Result

**FINAL_RELEASE_CLEANUP_COMPLETE** — all 20 success conditions hold with on-disk evidence.
