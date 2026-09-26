# RUINRAIL — FINAL RELEASE CANDIDATE AUDIT

> **HISTORICAL — archived 2026-09-25.** Current release status: `production/CURRENT_RELEASE_STATUS.md`. Kept as the detailed evidence record of its pass.

> Date: 2026-09-25. Environment: macOS, Unity `6000.3.24f1` (pinned versions unchanged). Release target: Windows x64
> (module not installed on this machine — NOT RUN); local built-player substitute: macOS Standalone, non-development.
> Evidence: `TestResults/FinalReleaseAudit/` (21 matrices, the owner checklist, `built_player/` scenario runs, `iterations/`
> for the superseded intermediate builds, `smoke_before/` and `smoke_after/` for the flakiness measurements).
> Labels used below: **PASS**, **FIXED DURING AUDIT**, **NOT RUN — ENVIRONMENT**, **EXTERNAL CONTENT LIMITATION**,
> **NON-BLOCKING KNOWN ISSUE**, **RELEASE BLOCKER**. Nothing is labelled PASS on inference: every PASS row in the matrices
> is computed by `scripts/final_release_audit.py` from a test case, a built-player run result or a validator report on disk.

## 1. Final Status

**FINAL_RELEASE_CANDIDATE_COMPLETE** — every repository-local success condition is met on the final build:

| Gate | Result |
|---|---|
| Release smoke (built player, 4 deterministic scenarios × 10 consecutive runs, no retries) | **PASS — 40/40** (fresh 10/10, returning 10/10, death 10/10, cache 10/10) |
| Long run (12 descends, D1→D13, built player) | **PASS — 3/3**, identical samples each run |
| Duo co-op release smoke (real host + client processes, seed 46) | **PASS — 5/5** (host 46/46 steps, client 6/6, every run) |
| Trio co-op (three real processes, seed 46) | **PASS — 2/2** (host 38/38, clients 5/5 + 5/5) |
| PlayMode (full) | **PASS — 842/842 passed, 0 failed, 0 skipped** |
| EditMode (full) | **PASS — 978/980 passed, 0 failed, 2 skipped (environment/manual)** |
| FinalReleaseCandidateValidator (+ deliberately broken fixture) | **PASS — 50/50 rules**; the broken fixture fails all 16 rule families |
| macOS non-development build | **PASS** — Succeeded, 0 errors, 176.1 MB |
| Windows x64 | `Windows x64: NOT RUN — module unavailable` |
| Live UGS Sessions/Relay | `Live UGS Sessions/Relay: NOT RUN — project/service configuration unavailable` |

Three dead runtime seams (player impact receiver, Defibrillator, autosave flusher) and the flaky official smoke were
**FIXED DURING AUDIT**. No repository-local release blocker remains.

## 2. Reviewed Repository State

The uncommitted working tree after the balance, depth/retention, presentation/audio/UX and co-op completion passes was
treated as authoritative (HEAD `e1d41b0` plus those passes). No destructive git operation was used; no pinned version
changed; no frozen value changed (the frozen snapshot equals the committed baseline). Reports read: FINAL_MVP, stat
consumer, fresh-run balance, D1 ammo/blaster, run variety, presentation/audio/UX, co-op composition + completion, the
latest test/validator/smoke/build outputs, 06_OPEN_DECISIONS, 04_SCOPE, 00_INDEX, the multiplayer specs.

## 3. Current-Report Reconciliation

`current_state_reconciliation.csv` (15 areas). Contradictions found in CURRENT-status documents: audio/music/animation
"missing" (production copies of audits whose live output is complete), "co-op not connected / fake transport" (review,
MVP report, multiplayer gate, shippable report), the MVP report generator hard-coding a fake-transport line and an
always-on placeholder-art blocker, 131 listed as the production state, ENVIRONMENT "NOT YET VERIFIED", 06_OPEN_DECISIONS
missing the co-op / post-D30 / deepest-depth decisions. All reconciled (§24).

## 4. Runtime Composition / Dead-Seam Audit

`runtime_composition_matrix.csv` — 33 subsystems, each with provider, shipping composition caller, runtime consumer,
PlayMode consequence test and built-player consequence. Three **dead seams found and FIXED DURING AUDIT**:

1. **Player-side knockback/stagger (and Resilience).** `PlayerImpactReceiver` was on every player entity but no shipping
   code called `SetConfig`/`SetEvents`: every enemy, elite, boss and hazard impact on the player returned None, so the
   Resilience attribute (+2% knockback/stagger resistance per rank, combat/42 + player/13) and the Anchored / Shock
   Absorber / Exo Lock hooks had no effect. The PlayMode proof had wired the config itself, hiding it. Now
   `PlayerRig.ComposeGameplay` sets the authored `StaggerConfig` (the one enemies use — no new value) and the combat
   events; the host's copies of co-op members get the config too. Proof: `ReleaseSeamCompositionTests.LiveRun_PlayerImpactReceiver_…`.
2. **Defibrillator.** The rig configured consumables without a revive requester, so the lootable Defibrillator was always
   refused. Now `PlayerRig.ReviveRequester` → `ExpeditionScene.RequestDefibrillatorRevive` (solo/host: the party revive
   authority; co-op client: `req.revive` → host validates the member and its mirrored inventory → `res.revive`; the unit is
   spent only on acceptance). Proof: PlayMode `LiveRun_Defibrillator_…` and the trio built-player step (Dead teammate
   revived at 30/100, host accepted once, unit spent once, a second use with nobody to revive refused at no cost).
3. **Autosave flusher.** `AutosaveFlusher` existed with tests but was never composed, so the 113 autosave points (storage,
   trader, skill spending, base upgrades) reached disk only at the next explicit save. Now composed on `GameApp`, bound to
   the open session. Proof: `Shelter_AutosavePoints_ReachDisk_AtTheEndOfTheFrame_WithoutAnExplicitSave`.

Also proven end-to-end in this audit: the post-D30 reward curve through the composed service (`LiveRun_PostDepth30Rewards_…`:
30 real descends, XP unscaled at D30, ×1.07 once at D31). **Non-blocking:** `LootAuthority.RequestChestOpen /
RequestEventActivate / RequestMedicalRevive` are test-only APIs; the shipping co-op path resolves chests/events by host
replay of the member's Interact (proven by the duo proof), so no player-facing feature depends on them.

## 5. Fresh Profile End-to-End

`fresh_profile_end_to_end.csv` — all 29 steps **PASS**, in all 10 fresh runs (built player, seed 11): boot, Main Menu,
profile creation through the real Shelter onboarding (display name, starter kit — added to the smoke in this audit),
Character Station, Storage, Multiplayer Terminal (Solo unaffected), D1, combat, ammo, inventory, weapon switching,
quick-grenade, status chips, pickup attraction, Merchant, non-combat rooms, boss, Transit, RETURN, save, Shelter, banked
state; Shelter Trader, relaunch and profile restore by the returning scenario on that same save. Elite: deterministic
PlayMode fixtures (5 elite suites); the built-player smoke does not target elites.

## 6. Returning Profile / Progression

`returning_profile_end_to_end.csv` — 22/22 **PASS** (10 relaunches): current-document load, no migration, no quarantine,
live profile = saved profile, Storage item-for-item, level/cap/point conservation, Station previews, all six attributes'
runtime effects in the re-entered run (Handling asserted on its reload consumer; its weapon-switch share is an explicitly
deferred stat), Storage round trip, Trader buy + resale settled once, RETURN banks once, deepest depth monotonic, no
duplicate ids, tutorial state and settings (screen-shake 0.5 saved by the fresh run) survive the relaunch.

## 7. Save / Migration / Recovery

`save_integrity_matrix.csv` — 17 rows **PASS**: fresh/returning saves, v1 fixture migration, the migration chain, atomic
temp/current/backup recovery, malformed-document quarantine without overwrite, duplicate-id refusal, invalid values,
interrupted writes, saves after RETURN / death / progression / Shelter trade / co-op extraction, no session state in the
slot, extraction commits once, and (new) autosave points reach disk without an explicit save.

## 8. Solo Built-Player Proof

`solo_built_player_proof.csv` — 18/18 **PASS** over 10 runs per scenario: menu → profile → Shelter → D1 combat →
inventory → ammo → Merchant → events → boss → DESCEND → D2 → RETURN → save/reload; a death path inside the full smoke and
as an independent scenario; a settings change verified after relaunch; controller focus navigation; Weapon Cache (seed 31);
any error/exception log line fails a run; every run reached `done`.

## 9. Duo Co-op Proof

`coop_end_to_end.csv` (Duo rows) — 5 consecutive runs, seed 46, two separate non-development player processes over
UnityTransport on 127.0.0.1: host, join, ready/start, identical D1, one camera/listener/input per process, remote motion,
combat as host-validated requests, enemy replication, loot race, merchant/sell, chests/vault/cache, Downed, revive both
ways, boss with phase 2, unanimous DESCEND, same D2, state preserved, disconnect + reconnect inside grace, RETURN, each
peer's own transaction and save, no orphan network objects, no uncaught exception. **PASS 5/5.**

## 10. Trio Co-op Proof

`coop_end_to_end.csv` (Trio rows) — 2 runs, three processes: three identities and entities, identical dungeon, authored
trio scaling, shared encounter, three-way loot race, revive, 2-of-3 vote holds, one depth transition, bleedout → Dead →
spectator, **Defibrillator revive (new)**, wipe once, clean exit. **PASS 2/2** (38/38 host steps).

## 11. Controller / Input / Settings

`input_settings_matrix.csv` — 14/14 **PASS**. 14 actions, each with a rebind label and keyboard + gamepad glyphs; every
action has a rebind path except the two documented fixed ones: Pause, and Aim (the pointer position / right-stick axis —
not a key binding; now stated in ui/90 CONTROLS). Rebinding (both schemes, conflicts, reset, persistence), all volume /
video / accessibility / tutorial / aim-assist settings; Settings and Controls pages driven in the built player; settings
survive a relaunch. Design note (not a blocker): technical/116 said "all gameplay bindings must support rebinding"; the
axis-type Aim cannot be rebound to a key by construction. **Resolved** by `production/FINAL_RELEASE_CLEANUP_REPORT.md`
(technical/116 now states the rebinding contract; the validator checks it against the rebinder).

## 12. UI / Navigation / Resolution

`ui_navigation_matrix.csv` — 19/19 **PASS**: every shipping screen (Main Menu, onboarding, Shelter stations, Settings,
Controls, Help/Codex, HUD, inventory, Merchant, Weapon Cache, events, pause, Run Lost, summary, Transit vote, party HUD)
through its layout/interaction suites at 640×360, pixel font only, focus/no-trap/parity suites, the pixel-perfect camera at
16:9 scale-up and non-16:9 (letterboxed), and > 40 built-player captures per fresh run. The validator also requires every
screen to be composed by shipping code.

## 13. Presentation / Audio

`presentation_audio_matrix.csv` — 9/9 **PASS**: substrate under the floor with no collider, props, one AudioListener,
mix/variation/throttle, loops and ambience, Shelter/exploration/combat/boss/UI audio in the built player, output-device
recovery, volume persistence. No new audio content was produced.

## 14. Combat Regression

`combat_regression_matrix.csv` — 18/18 **PASS**: all 33 weapon fingerprints, Field Knife, 3 Blaster heat sets, D1 Supply
Light range, starter kit and ammo caps equal the release baseline; consumer, fire/reload/auto-reload, projectile,
spread, melee, bow, knockback/stagger, aim assist ON/OFF, remote weapon presentation suites green.

## 15. Depth / Economy Regression

`depth_economy_regression_matrix.csv` — 15/15 **PASS**: D1–D30/D31+ scaling, post-D30 continuation (only after D30,
capped), deepest depth (D2 per fresh run, D13 in the long run), elite frequency, room gating, biome weighting, boss HP,
seeded selection/anti-kite, merchant/coin/rarity/XP rules, RETURN/DESCEND, depth-arrival heal, co-op scaling.

## 16. Non-Combat / Events

`noncombat_regression_matrix.csv` — every event type **PASS**: Merchant, loot rooms, chests and the Weapon Cache in the
built player (seeds 11/31); Medical Station, Broken Machine, Locked Vault, Cursed Chest, Supply Signal and Treasure (not
generated on the pinned seeds' D1) in the live-run PlayMode matrix that drives every type end to end; co-op merchant,
chests, vault and cache under host authority in the duo proof.

## 17. Death / Extraction

`death_extraction_matrix.csv` — **PASS**: solo death loses carried risk state, keeps XP/ranks/Storage/banked, shows Run
Lost once, saves, and the next run can start; solo RETURN banks once; co-op downed/revive, bleedout → Dead → spectator,
Defibrillator, wipe once, RETURN commits once with every peer's own extraction and save.

## 18. Performance / Leak / Long-Run

`performance_longrun.csv`: built-player long run, 12 consecutive descends × 3 runs (identical): D1 compose 151 ms, depth
rebuild median 20 ms (max 124 ms), live GameObjects 452–545 with no trend, 16 pooled projectiles throughout, one camera,
one listener, no network objects and no stale enemies at every depth, projectile-channel subscribers constant, 50 inventory
+ 50 pause open/close cycles leave the object count unchanged (1762 → 1762), save 0.6 ms, load 0.4 ms. Mono heap
9.6 → 10.7 MB over 12 depths (≈0.09 MB/depth after a forced GC) — **NON-BLOCKING KNOWN ISSUE** (observation: small and
bounded, no object growth; worth a look if multi-hour sessions show more). Duo/trio host samples: 2–4 network objects
during the run, 1 (the session link) after Return, one camera/listener per process. Nothing was optimised.

## 19. Smoke Flakiness Before

`smoke_stability.csv` (BEFORE rows) — the smoke took its run seed from the clock: **2/10** on this audit's baseline,
failing in five different stages (direct-hit reference shot ×2, knife dummy, knockback target, containment overshoot
0.065/0.083, "a non-combat room exists" ×2); earlier passes measured 3/10 and 3/16 and accepted "rerun until green". Seed 31
failed 3/3 on an ordered-stage coupling. PlayMode `CombatAimCollisionProofTests` failed in ~half of full runs.

## 20. Smoke Stabilization

Root causes and fixes (harness/test side only; no product tolerance changed):

| Root cause | Class | Fix |
|---|---|---|
| clock run seed | test seed nondeterminism | every release scenario pins its seed (`scripts/run-release-smoke.sh`) |
| Weapon Cache stage consumed the real cache, the non-combat stage then failed on its prompt (seed 31) | ordered-stage coupling | the non-combat stage asserts the spent cache's used state |
| knockback target = first `FindObjectsByType` result (could be the frozen dummy) | ordered-stage coupling | dynamic, alive target with open floor, instance-ID order |
| knife dummy placed on an aim still easing after the camera (46–49° off a ±40° arc) | timing | clear lane; dummy held on the aim through the wind-up |
| live pack crossing the reference-shot lane | timing/physics | pack held for the reference shots; enemy- and hazard-free lane |
| "a non-combat room exists" asserted, but dungeon/55 ranges start at 0 | seed-dependent precondition | every room present must be driven; the pinned seed keeps coverage |
| PlayMode CombatAim: clock seed; the encounter's next wave walking into the line of fire; whole-pack pile-up pushing one body ~0.4 tiles into a wall/door for one physics step | seed + test precondition + physics settling | seed 11; later waves cleared before each shot; one pursuer at the wall/door (the test's own Charger-lane rule); 0.12 bound unchanged |
| fresh profile creation never exercised | coverage gap | the fresh smoke performs the real onboarding |
| monolithic smoke hid death/returning coverage | structure | independent scenarios: fresh, returning (relaunch), death, cache, longrun; manifest maps 29 critical paths |

## 21. Final Smoke Stability

`smoke_stability.csv` / `release_smoke_manifest.csv` — on the final build, without retries or repository changes between
runs: fresh 10/10, returning 10/10, death 10/10, cache 10/10, longrun 3/3, duo 5/5, trio 2/2 — all **FinalDeterministic
YES**; 29/29 manifest paths PASS. PlayMode CombatAim: 6/6 isolated and green in both final full runs. Unpinned clock seeds
(not a release gate) rose from 2/10 to 6/10; the residue is layout-dependent (direct-hit reference placement — cause not
fully isolated — and the doorway pile-up transient) and is recorded, not hidden.

## 22. Build Pipeline

`build_pipeline_matrix.csv` — Unity 6000.3.24f1; scenes Bootstrap, MainMenu, Base, Dungeon in order, no test scene;
options None (non-development); Succeeded with 0 errors (2 warnings: UGS project link, symbol-upload token — no missing
script/reference); network prefabs registered (player entity + session link); the fake driver only with
`-offline-multiplayer`, which the release path never passes; catalog and settings defaults validated.
`Windows x64: NOT RUN — module unavailable` (macOS Standalone used as the local substitute; not installed by instruction).

## 23. External Services

`external_services_status.csv` — `Live UGS Sessions/Relay: NOT RUN — project/service configuration unavailable` (no
project link or credentials; none installed). The terminal reports the failure in words and Solo is unaffected
(MultiplayerTerminalTests, RecoverableErrorTests). The local real-network runtime is complete (duo/trio above).

## 24. Documentation Consistency

`documentation_consistency.csv` — 11 superseded current-status documents now open with a `SUPERSEDED` banner pointing
here (the validator enforces it); the FINAL_MVP generator no longer hard-codes fake transport or placeholder art (its
blockers derive from the asset manifest and audits; the report now reads COMPLETE with NOT RUN lines kept separate);
00_INDEX has a "Current Release Status" entry; 06_OPEN_DECISIONS records the co-op / post-D30 / deepest-depth decisions;
ENVIRONMENT's package verification is updated (no version change); 123_POST_MVP marks the personal deepest-depth record
as shipped; ui/90 documents Aim as fixed. Historical task reports were left as they are.

## 25. Final Validator

`FinalReleaseCandidateValidator` (Editor) + `FinalReleaseCandidateValidatorTests` — aggregates FinalProduction,
ContentCount, StatConsumerIntegrity, RunVariety, PresentationAudioUxQol and CoopRuntimeComposition, and adds: approved
scenes in order / no test scene, save version + unbroken migration chain + deepest-depth field, starter kit, player-entity
and session-link registration, enemy replication ids unique, one AudioListener site, the pixel UI font, 17 shipping
screens present and composed, input labels/glyphs/rebind paths, the frozen snapshot vs the committed baseline (with named
guards for D1 ammo, Field Knife, Blasters, depth scaling, co-op scaling), the release-smoke manifest's required scenarios
and critical paths, and superseded-status banners. Real project: 50/50 PASS. Deliberately broken fixture: every rule
family fails; a missing baseline fails.

## 26. Frozen Release Snapshot

`frozen_release_snapshot.csv` (100 values, written by the EditMode gate) equals `production/FINAL_RELEASE_FROZEN_BASELINE.csv`
(committed): player HP 100, dash 17/0.18/1.4706/0.1, ammo caps 180/120/60/40, starter kit, Supply Light 26–46 (w4), 33
weapon fingerprints, Field Knife 14–17 @3.5, Blasters (10/35, 7/35, 8/40; max 100, delay 0.5, lockout 1.9), 6 boss HP
(1000–1350), depth scaling samples, reward curve (×1.07 at D31, cap 1.75), elite frequency 8/18/25%, 9 gated rooms,
biome weights, max level 61, rank cap 10, backpack 8, party 3, co-op scaling, revive/bleedout 20 s / 4 s / 30%, pickup
1.25/2.0 (+3 Magnetic Coil), aim assist 18°/24°, 53 audio events, 63 rooms, 72 items, 33 weapons, 9/6/6 enemies/elites/bosses.

## 27. Automated Test Results

Final gates, run after the last source/data change (`./scripts/run-unity-tests.sh PlayMode` then `EditMode`):

- **PlayMode: PASS — 842/842 passed, 0 failed, 0 skipped**
- **EditMode: PASS — 978/980 passed, 0 failed, 2 skipped (environment/manual)** — skipped by design: `D1AmmoBlasterFineTuneTests.CalibrateLightAmmoQuantity` (manual calibration)
  and `MultiplayerTerminalTests.LiveSessionsRelay_IntegrationCheck_OrNotRun` (**NOT RUN — ENVIRONMENT**, live UGS).

New in this audit: PlayMode `ReleaseSeamCompositionTests` (4); EditMode `FinalReleaseCandidateValidatorTests` (3);
`FinalMvpAuditTests` rewritten to derive external content. The same final build's chain also ran PlayMode 841/841 and
EditMode 978/980 before the post-D30 test was added.

## 28. Built-Player Scenarios

`TestResults/FinalReleaseAudit/built_player/` (final build): A. Solo release (fresh, seed 11: D1 → boss → descend → D2 →
return → save/reload) 10/10; B. Solo death (fresh save) 10/10; C. Returning profile (progress, six attributes, Storage,
Trader, re-entered run) 10/10; D. Duo release (host/client: combat, loot, revive, boss, vote, descend, reconnect, return)
5/5; E. Trio (3 peers: composition, scaling, combat, vote, transition, bleedout, Defibrillator, wipe) 2/2; plus the cache
scenario 10/10 and the long run 3/3. Each run keeps its `smoke_result.json` / peer JSON, player logs and captures.

## 29. Owner Manual Checklist

`TestResults/FinalReleaseAudit/OWNER_RELEASE_CHECKLIST.md` — 39 observational checks in the requested groups, including
the two feel changes this audit introduces (player knockback/stagger now active; the Defibrillator now usable).

## 30. Known Non-Blocking External Limitations

- **NOT RUN — ENVIRONMENT:** Windows x64 build and smoke (module unavailable); live UGS Sessions/Relay (no project
  configuration); Windows device profiling.
- **EXTERNAL CONTENT LIMITATION:** none open — the asset manifest reports 340 INTEGRATED / 0 PLACEHOLDER / 0 MISSING; no
  newly authored alternative SFX or longer music was in scope.

## 31. Known Repository-Local Issues

All **NON-BLOCKING KNOWN ISSUES** (none meets the release-blocker policy):
- Pack pile-up transient: a body pressed by the pack can sit up to ~0.4 tiles inside a wall/door collider for one physics
  step before `EncounterBounds` pulls it back (its documented sub-step correction); bodies never leave the room.
- Mono heap drift ≈0.09 MB per depth in the long run (no object growth).
- Unpinned layouts: the smoke's direct-hit reference placement can still miss on some clock seeds (not a release gate).
- For co-op clients, the accessory reactive hooks on incoming impacts (Anchored negates one stagger, Shock Absorber
  ignores explosion knockback, Exo Lock) do not act: the host simulates the member's body and its copy carries the
  member's stats (so Resilience and resistance affixes apply) but not the member's passive event hub. **Resolved** by
  `production/FINAL_RELEASE_CLEANUP_REPORT.md`, which also found and fixed that Solo/host Anchored never recharged and Exo
  Lock never triggered (the passive registrar was never ticked and nothing raised DamageTaken).
- technical/116 wording vs the fixed Aim axis (§11) — **resolved** (cleanup report).
- Player knockback/stagger is now live (it was specified but inert); it changes feel and is on the owner checklist.

## 32. Files Changed

Runtime: `App/PlayerRigComposer.cs`, `App/ExpeditionScene.cs`, `App/ExpeditionScene.Coop.cs`,
`App/ExpeditionScene.Defibrillator.cs` (new), `App/GameApp.cs`, `Multiplayer/CoopMessages.cs`,
`Multiplayer/CoopHostWorld.cs`, `Multiplayer/CoopClientWorld.cs`, `App/CoopExpeditionProof.cs`, `App/SmokeRunner.cs`,
`App/SmokeRunner.Returning.cs` (new), `App/SmokeRunner.DeathScenario.cs` (new), `App/SmokeRunner.LongRun.cs` (new),
`App/SmokeRunner.DepthSettingsNonCombat.cs`, `App/SmokeRunner.StatConsumers.cs`, `App/SmokeRunner.LootAudio.cs`.
Editor: `Production/FinalMvpAudit.cs`, `Production/FinalReleaseCandidateValidator.cs` (new),
`Production/FinalReleaseSnapshot.cs` (new). Tests: PlayMode `ReleaseSeamCompositionTests.cs` (new),
`CombatAimCollisionProofTests.cs`; EditMode `FinalReleaseCandidateValidatorTests.cs` (new), `FinalMvpAuditTests.cs`.
Scripts: `run-release-smoke.sh`, `run-coop-release-smoke.sh`, `run-smoke-loop.sh`, `release_smoke_manifest.csv`,
`final_release_audit.py` (all new). Docs: this report and `FINAL_RELEASE_FROZEN_BASELINE.csv` (new), `00_INDEX.md`,
`06_OPEN_DECISIONS.md`, `ENVIRONMENT.md`, `ui/90_UI_UX_OVERVIEW.md`, `production/123_POST_MVP.md`, SUPERSEDED banners on
11 production documents; `FINAL_MVP_COMPLETION_REPORT.md` regenerated by the EditMode gate (as every run does).
Generated by test runs: `GameContentCatalog.asset` (appended sprite lists — expected noise, not a content change).

## 33. Final Release Status

**FINAL_RELEASE_CANDIDATE_COMPLETE.** All 30 success conditions hold with on-disk evidence: reconciled current documents,
no dead runtime seam for a shipping player-facing system, fresh and returning profiles, save/migration/recovery, Solo
built-player core loop, real-peer Duo full loop and Trio proof, input/settings, UI, presentation/audio, frozen combat and
depth/economy values, every event type, death/extraction, bounded long-run behaviour, a deterministic official smoke
(10 consecutive passes per solo scenario, 5 Duo runs) with no loosened tolerance, a successful release build on the
available platform, truthful Windows and Live UGS status, the final release validator, full EditMode (environment skips
only) and full PlayMode. Windows x64 and Live UGS/Relay remain external, reported NOT RUN.
