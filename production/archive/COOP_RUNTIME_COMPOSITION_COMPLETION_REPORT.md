# RUINRAIL — CO-OP RUNTIME COMPOSITION COMPLETION REPORT

> Pass: co-op runtime composition **completion** (continuation of `COOP_RUNTIME_COMPOSITION_REPORT.md`).
> Environment: macOS, Unity 6000.3.24f1, NGO 2.7.0, com.unity.transport 2.6.0 — pinned versions unchanged.
> Evidence classes used below: **built-player** (separate OS processes of the shipped non-development player over
> UnityTransport on loopback), **PlayMode** (in-process, real runtime classes over the loopback bus or a real run),
> **EditMode / static** (validator rules over the real sources and assets), **mock-only** (none relied on for a
> completion claim), **NOT RUN** (environment), **deferred external dependency**, **repository-local blocker** (none left).

## 1. Executive Summary

The previous pass ended `COOP_RUNTIME_COMPOSITION_INCOMPLETE` with one blocker: a joining client connected, owned a
real replicated character and could move it, but never composed the expedition. That blocker is closed.

A joined client now composes and plays the host's expedition end to end, in the shipped player, as a separate process:

- The host's start is authoritative and published once (seed, first biome, party, a unique participant id per member);
  every client starts **its own** transaction from it (its own inventory, wallet, XP and save) and never picks a seed.
- The host publishes each depth through `NetworkDungeonSync` (seed, depth, biome, generation round, room-pool and
  layout fingerprints); the client rebuilds the same depth and refuses to play on any fingerprint mismatch.
- The client's run player is composed onto the `PlayerNetworkEntity` it already owns (`PlayerRig.Attach`) — no second
  player; the host's own player is a network object too, so every peer sees every member.
- Every enemy, elite and boss is host-simulated and replicated by definition id through one session channel
  (`CoopRunLink`): spawn, 15 Hz motion/state (incl. moveset attack and telegraph direction), boss phase, death once.
- Rooms, doors, chests, events, merchant stock, ground loot, Transit votes and results are host decisions mirrored to
  the clients; a client's hits, heals, trades, cache choices, votes and presses are requests the host validates.
- Downed/revive both ways, bleedout to Dead with spectating, party wipe once, the boss (with its phase change), the
  unanimous DESCEND, an identical D2, a mid-expedition disconnect/reconnect into the current state, and RETURN with each
  peer's own extraction and save — all run across real processes.

The built-player duo proof (host + client, two seeds) and trio proof (host + two clients) pass every step; the full
PlayMode and EditMode gates, the upgraded co-op validator (35 rules + a deliberately broken fixture), the macOS
non-development build and the solo built-player smoke are reported in §26–§27 with their exact results.

## 2. Previous Incomplete State Reproduction

Reproduced on the pre-change build before any edit (`TestResults/CoopRuntimeCompletion/before/`,
`current_blocker_before.csv`): the duo peer proof passed (host + client processes, session accepted, party member,
owned NetworkObject, motion replicated both ways), while statically `NetworkDungeonSync`, `DungeonSync.*`,
`AuthoritativeEnemySpawner`, `EnemyReplicaRegistry`, `BossReplica`, `GroundLootSnapshot`, `RequestMerchantBuy` and
`RoomRuntime.SetAuthoritative(false)` had **no runtime caller**, `ExpeditionScene` had no client branch, and only the
player prefab was registered with NGO. Client expedition built / seed / depth / biome / room graph / enemies /
encounter state / loot request path / vote path: all **NO**.

## 3. Authoritative Start-State Sync

`start_state_sync_matrix.csv`.

- **Lobby relay** (`CoopSessionService`): a client's Shelter Ready, loadout and attribute ranks travel to the host
  (`lobby.member`) and are applied to the host's `PartyLobby` through its own `SetLoadout`/`SetReady` (81: a changed
  loadout still clears Ready). A joined client cannot start a run of its own (`TransitPanelViewModel.StartGate`).
- **Run start** (`run.start`): published once when the host's own expedition starts — seed, first biome, party size,
  content version and every member with a unique participant id (the host's is its own transaction id). Every client
  applies it through `ExpeditionStartCoordinator.Apply(..., participantId)` exactly once per start id.
- **Depth payload** (`NetworkDungeonSync` on the session link): seed, depth, biome, the generation round the host kept,
  room-pool fingerprint and layout fingerprint, published on every depth by the host's `CoopHostWorld.BindDepth`.
- **Composition gate**: a client composes only once the link exists, the start has arrived, its own player object is
  owned and every member's object exists (`BuildWhenClientReady`), and builds its depth only when the payload for exactly
  that depth is present. The host holds gameplay input until every client reports the depth built (`depth.ready`) and
  then releases everyone together (`depth.release`, 25 s fallback).

## 4. Client Expedition Composition

`client_composition_matrix.csv`. `ExpeditionScene` resolves `CoopRunMode` once (Solo / Host / Client) from the live
session; Solo takes exactly the old path. In Client mode it:

- rebuilds the depth from the host payload (starting at the host's generation round) and verifies seed, biome, pool and
  layout fingerprints (`ClientDepthDesync`) — a mismatch fails loudly instead of desynchronising;
- composes rooms non-authoritatively (`SetAuthoritative(false)`), with a room spawner that refuses every spawn
  (`AuthoritativeEnemySpawner` + `CoopClientAuthority`) and no boss spawner;
- renders geometry, doors, substrate, prop dressing, chests, events, the merchant and the transit car locally from the
  same deterministic composition, and applies the host's room records onto them (`CoopClientWorld.ApplyRoom`);
- composes networked enemy/boss representations from host records (§6), the minimap from the same layout, and the
  local HUD/camera for its own character.

It never authors loot, enemy authority, encounter completion, event outcomes, boss death, rewards, the Transit result or
the Return. Built-player: identical D1 and D2 (seed/depth/biome/layout fingerprint/room ids+positions+doors) on all
peers, one camera/listener/input/owned character per process.

## 5. Local PlayerRig Attachment

`local_player_attach_matrix.csv`. `PlayerRig.Attach(owned, state, roster, participantId, skills)` composes the run
player onto the object the peer already owns — the release prefab is `PlayerEntityBuilder`'s composition, so Attach
adds exactly what `Build` adds on top (weapons from the at-risk inventory, stats, passives, consumables, grenades, the
Legendary special) and rebinds every input consumer to the run's one reader. One identity, one life state, one
inventory, one equipment state, one owned entity, one camera, one input, one HUD/inventory UI, one cursor, one aim-assist
preference, one AudioListener per process. The host composes its own player the same way inside the spawn, before the
loot authority registers it. After a reconnect the run recomposes on the handed-back object (no second player).

## 6. Enemy Network Runtime Composition

`enemy_network_matrix.csv`, `enemy_actor_coverage.csv`. The architecture is **one registered network prefab
(`CoopRunLink`) plus definition ids**, as the existing `EnemySpawnRecord`/`EnemyNetState` design intended — no per-enemy
prefab can be missing from a release build.

- Host (`CoopHostWorld`): registers every actor (room encounters, event waves, elites, the boss at compose time, summons
  via a periodic sweep), sends `enemy.spawn`, 15 Hz `EnemyNetState` batches (unreliable; position, facing — the locked
  attack direction while telegraphing — state, HP, moveset attack slot), `boss.state` (phase/started/defeated) and
  `enemy.gone` once (with the kill XP for the clients' own profiles). AI, targeting, movement, health, damage, death,
  unlock consequences, boss phase and encounter completion stay on the host.
- Client (`CoopClientWorld` + `EnemyReplica`): one replica per net id (duplicates ignored), no AI, no attack behaviour,
  no physics body; enemy team + actor-class hurtbox so local shots land on it (and become requests). Presentation reuses
  the accepted components: body art, `EnemyAnimationDriver.ConfigureReplica`, `TelegraphIndicator.ConfigureReplica`
  (real attack shapes), world/elite bars, hit flash, damage numbers, audio; the boss bar binds the boss replica.
- Coverage: every one of the **9 normal archetypes, 6 elites and 6 bosses** is spawned on a host world and replicated
  (spawn, state, moveset slots resolvable on the client, death once) by
  `EveryArchetypeEliteAndBoss_ReplicatesSpawnStateAndDeath_Once`; live built-player runs replicated grunt, swarm,
  shooter, brute-class and four bosses across processes.

## 7. Network Prefab Registration

`network_prefab_matrix.csv`. Registered in `Assets/DefaultNetworkPrefabs.asset`, carried by `GameContentCatalog`, and
added by every composition that builds a NetworkManager (`LiveServiceConfiguration` + `CoopLinkSpawner.Install`, the
peer harness): `PlayerNetworkEntity` (now `DontDestroyWithOwner` so the reconnect grace has a character to hold) and
`CoopRunLink` (`NetworkObject` + `CoopRunLink` + `NetworkDungeonSync`; spawned once by the host when it starts
listening, `DontDestroyOnLoad`). A release-build defect was found and fixed on the way: `NetworkDungeonSync` lived in
`DungeonNetSync.cs`, so Unity could not reference it on a prefab ("missing script" in the build); it now has its own
file and the validator fails on any missing script on the link prefab.

## 8. Room / Encounter State Replication

`room_state_replication_matrix.csv`. The host sends each room's record whenever it changes (lifecycle, entry count,
spawned/defeated/remaining, door lock, resolved interactions, merchant sold offers, boss-cache lock, transit active,
event phase; versioned, stale records ignored). The client applies it through the room's own `RestoreState` (so the
clear raises `Cleared` exactly once there too — the client records its own RoomsCleared), then onto its copies of the
room's objects (chest opened, event used, offer sold, cache unlocked, transit active). First entrant activates once;
later entrants change nothing; lock/unlock, enemy set and clear are identical on every peer; a reconnecting client gets
the current records, never the room-start state.

## 9. Client Request Routing

`client_request_routing_matrix.csv`. Every request is tagged with the sender NGO reports (never a payload id), the link
drops any kind arriving from the wrong side (`CoopKinds.IsClientToHost`), and each is resolved by the existing
authority services:

| Area | Request path | Host decision |
|---|---|---|
| Hits / grenade blasts | `DamageAuthority.RemoteDamageRelay` → `req.hit`; `ImpactDispatcher.RemoteImpactRelay` → `req.impact` | `CoopHostWorld.HandleHit/HandleImpact`: sender alive/can act, target replicated & alive, reach, size ≤ the member's weapons (`CoopMemberMirror.MaxHit`), ≤ 60/s |
| Heal (consumable) | `DamageAuthority.RemoteHealRelay` → `req.heal` | sender's own character, alive, cooldown, capped |
| Pickups / chests / events / transit / boss cache | the Interact press travels as the existing `WeaponCommand.Interact`; the host copy's `PlayerInteractor` resolves it on the host's objects | `PickupArbiter` → `LootAuthorityService`; chest/event one-time rules with the member's own wallet and backpack |
| Merchant buy / sell | local trade screen → `req.buy` / `req.sell` | `LootAuthorityService.RequestMerchantBuy/RequestMerchantSell` (member's own wallet, sold once, tx dedupe) |
| Weapon Cache | local choice screen → `req.cache` | `LootAuthorityService.RequestCacheChoose` (once for the party) |
| Revive | `InteractHeld` in the owner's intents | host `PlayerReviver` channel |
| Transit vote | `TransitDecision.Submitted` → `req.vote` | host `TransitDecision.Submit` under the sender's participant id; living voters only |

Reload, weapon switch and grenade throws are owner-local (the owner's own weapon state and ammo, 82 "Feel"); what they
do to shared state arrives as the requests above. A client's local `PlayerInteractor` only runs the two purely
presentational interactions (opening the trade screen, opening the cache choice); a client `PickupArbiter` refuses
everything locally.

## 10. Loot End-to-End

`loot_end_to_end_matrix.csv` — built-player: item race (one winner, consumed once, gone on every peer), client-only
item into the client's own inventory (host unaffected), coin pile split once across every member's own wallet (duo and
trio), client ammo pickup (pulled and resolved on the host), chests and the boss cache opened once by the client's
press; PlayMode: full-backpack refusal (pickup stays), presentation pickups resolve nothing locally, reconnect never
respawns a consumed pickup. No loot value changed.

## 11. Non-Combat / Merchant End-to-End

`noncombat_end_to_end_matrix.csv` — built-player: client purchase (own wallet debited, item in own inventory, host
wallet untouched), simultaneous host/client purchase of one offer (one sale, loser not charged), replayed transaction id
(one resolution), client sale, Weapon Cache (client choice, once), Locked Vault (seed 46) and Medical Station / Broken
Machine (seed 59) pressed by the client and paid from its own wallet, chests; PlayMode: insufficient funds, wrong-owner
sale refused. Found and fixed: `DungeonEventInteractable.ActorFor` named every interactor `"local"`, so in co-op the
Medical Station's per-participant heal limit was shared by the whole party; the actor is now the member's participant id.
Economy unchanged: per-member Carried wallets (as decided in the previous pass from 84/58), no price or cost changed.

## 12. Downed / Revive / Wipe End-to-End

`revive_end_to_end_matrix.csv` — built-player: client to 0 HP → Downed on host and client; a Downed client's hits
never reach the host; host revives the client (authoritative 4 s channel, once, 30 % HP); client revives the host;
(trio) a Downed member left alone bleeds out to Dead, its camera spectates a living teammate, the others see it Dead;
(trio) every member down → the wipe resolves exactly once and every peer fails its own run once. Disconnect: the held
character stays represented, at risk and still. Defibrillator: **not currently available** — `PlayerRig` composes the
consumable user without a revive hook (pre-existing; the Defibrillator is service-level only), so it is not claimed.

## 13. Boss End-to-End

`boss_end_to_end_matrix.csv` — built-player: boss spawned by the host and replicated (HP, max HP, phase); both players
damage it through their allowed paths (client damage counted per sender on the host); HP agrees on every peer; phase two
on the host is phase two on every client; boss death once, arena clear once, Transit on every peer; boss cache claimed
once by the client. Boss selection, attack choice and values are unchanged (the host runs the accepted seeded system;
clients draw its telegraphs).

## 14. Transit Voting End-to-End

`transit_end_to_end_matrix.csv` — built-player duo: host DESCEND alone → no transition; client DESCEND → transition
once; client RETURN → the party returns. Trio: host alone and 2 of 3 → no transition; all three → descends once. The
client's `TransitDecision` runs `HostDecidedTransitPolicy` (never resolves locally), mirrors other members' votes for the
vote screen, and takes the host's single result through `ResolveFromAuthority`, which runs the client's own Descend or
Return transaction.

## 15. Networked Depth Transition

`networked_depth_transition_matrix.csv` — the host descends, publishes the new payload and releases gameplay when every
peer reports it; each client leaves its old room state, releases every old-depth replica and pickup, builds the same next
depth, keeps its identity, inventory, equipment, ammo and coins, records the deepest depth only on arrival, rebinds its
camera/HUD and keeps the remote player mapping; no old-depth network object leaks; one camera and listener. The
depth-arrival heal runs once, on the host.

## 16. Return / Extraction

`return_extraction_matrix.csv` — the RETURN resolves once; the host commits its own Return once; every client runs its
own Return (or failure if it is still Dead, 84) from the same result, saves at its own safe point, and returns to the
Shelter; banked coins rise once per peer by exactly its carried coins; the save marker closes; deepest depth recorded;
the host despawns every player object, leaving only the session link; the next Solo run is covered by the solo
regression (§21).

## 17. Live Reconnect

`live_reconnect_matrix.csv` — built-player: the client's transport is shut down mid-D2; the host holds the character
(represented, at risk, standing still; `RemoteIntentInputReader.Reset`), the client reconnects with its host-issued
token (`run.token`), the host hands back **the same object** (`ChangeOwnership`, no new spawn, `ReconnectCount` 1) and
the client recomposes its run from the host's current state (resync of rooms, actors, loot, boss and an open vote).
Found and fixed while proving it: the reclaimed character reached the returning client first as the server's object, and
`NetworkPlayerObject`/`NetworkPlayerMotion`/`NetworkPlayerCombat` only chose owner-vs-replica wiring at spawn, so it
stayed a replica (no commands, kinematic body); all three now handle `OnGainedOwnership`/`OnLostOwnership`, and
`NetworkPlayerCombat` unsubscribes from its reader on despawn. The grace now also expires: an unreturned member is marked
Dead (85). A brand-new player joining mid-run is still refused (deferred design question, unchanged).

## 18. Client Presentation

`client_presentation_matrix.csv` — one camera following the owned player, one AudioListener, local HUD/inventory bound to
the owned player, party rows for every member with legible remote life state, inventory as the local player's own,
merchant/cache screens that send requests, inventory/pause that never pause the shared world in co-op (built-player
measured `Time.timeScale` 1.0 on both peers with the inventory open), boss bar on the shared boss, spectator on Dead.
Aim assist, status chips, tutorial prompts and the graphical inventory are untouched local presentation.

## 19. Duo Built-Player Proof

`duo_built_player_proof.csv` (27 required items), `duo_run_seed46/`, `duo_run_seed59/` (host/client JSON and logs).
Command: `./scripts/run-coop-expedition-proof.sh <out> 2 <seed> <port>` (trio: size 3) (two separate non-development player processes,
UnityTransport over 127.0.0.1). Result: see §27.

## 20. Trio Runtime Proof

`trio_runtime_proof.csv`, `trio_run_seed46/` — three processes: three identities and owned entities, identical dungeon, trio
scaling (party 3 on every room), one activation, replicated enemies, all three moving and firing, one camera/listener/
input per peer, a three-way pickup race, revive, the trio vote policy (2 of 3 holds), one networked descend, bleedout,
spectator, wipe once, clean exit, no orphan objects. Result: see §27.

## 21. Solo Regression

`solo_regression_matrix.csv` — PlayMode on a real solo run (seed 11): Solo mode, no session and no network wait, one
player/camera/listener/input, local damage authority with no relays, no interaction filter or pickup arbiter, every room
authoritative, inventory pauses the solo world and resumes it, boss → Transit → Descend (depth built once, no stale
enemies) → Return → save (banked once, marker closed, deepest 2); plus the full solo built-player smoke (§27).

## 22. Authority / Exploit Tests

`authority_exploit_matrix.csv` — all 20 required scenarios with their evidence: wrong-owner movement (owner-only RPCs),
wrong-owner fire and direct client damage (hits are requests tagged with the NGO sender; replica HP never changes
locally), oversized/negative/out-of-reach hits and hit floods, duplicate pickup/merchant/event/boss-cache/revive/vote/
descend/return, stale request from an old connection, token replay, disconnected and dead senders, and client-authored
enemy spawn, room clear, boss death and depth transition (wrong-direction kinds dropped by the link; `PublishDepth`
throws on a client). No existing restriction was weakened.

## 23. Performance / Leak Check

`coop_runtime_performance.csv` — solo (PlayMode sample) and duo/trio host views through the run: GameObjects,
NetworkObjects (2–4: players + link), player entities, live enemies, allocated/mono memory, link messages and bytes per
second, listeners and cameras; after the Return every peer holds 0 player objects and 1 network object (the link). No
pathology found; nothing was micro-optimised. Enemy motion is batched (≤20 states/message, 15 Hz, unreliable).

## 24. Validator Changes

`CoopRuntimeCompositionValidator` — additive: 14 new rules (client composition, depth sync caller + consumption +
fingerprints, local player attach, enemy sync callers + client spawner refusal, link registration/catalog/no missing
scripts, client loot authority, client room authority, co-op time, networked depth transition, live reconnect, runtime
proof fixture, and three fixture rules fed by real host/client objects: room-state divergence, client party view for
duo/trio, resync duplicates/local hits). Report: `TestResults/coop_runtime_composition.md` (35 rules). A deliberately
broken fixture (client branch removed, depth publication removed, client spawner left authoritative, diverging facts)
fails the expected rules (`CoopCompletionRules_FailOnADeliberatelyBrokenFixture`). No previous rule was weakened.

## 25. Frozen-System Verification

`frozen_systems_check.csv` — the frozen list asserted against the shipped data or pinned by named suites in the same
gate, plus this pass's rows (network player gameplay values from the authored config, merchant prices, transit, revive,
save semantics). No balance, content or presentation asset was edited: the only assets changed are the two network
prefabs, `DefaultNetworkPrefabs.asset` and the catalog's link reference (a room prefab's timestamp changed during a test
run; its content is byte-identical to HEAD).

## 26. Automated Tests

Commands actually run (repo harness, macOS, PlayMode before EditMode because `FinalMvpAudit` reads the PlayMode XML):

| Command | Result |
|---|---|
| `./scripts/run-unity-tests.sh PlayMode` | **PASS — 838/838 passed, 0 failed, 0 skipped** (`PlayMode-results.xml`, 2026-09-24 20:08–20:16Z) |
| `./scripts/run-unity-tests.sh EditMode` | **PASS — 975/977 passed, 0 failed, 2 skipped** (`EditMode-results.xml`, 2026-09-24 20:22–20:26Z) |

The two EditMode skips are environment-gated by design and existed before this pass:
`D1AmmoBlasterFineTuneTests.CalibrateLightAmmoQuantity` (manual calibration) and
`MultiplayerTerminalTests.LiveSessionsRelay_IntegrationCheck_OrNotRun` (live UGS, §28).

New suites in the gate: PlayMode `CoopExpeditionRuntimeTests` (14, incl. every-actor coverage and capacity/funds) and
`CoopSoloRegressionTests`; EditMode `CoopRuntimeCompositionValidatorTests` (real facts + deliberately broken fixture) and
`CoopFrozenSystemsTests`. Validators inside the EditMode gate, all PASS: FinalProductionValidator, ContentCount,
StatConsumerIntegrity, RunVariety, PresentationAudioUxQol, CoopRuntimeCompositionValidator (35 rules,
`coop_runtime_composition.md`), CoopFrozenSystems, FinalMvpAudit.

An earlier full PlayMode run in this pass was 835/836 with the single failure in `CombatAimCollisionProofTests`
(known macOS flake, §30); the final run above is the one reported. Copies of the result XML and Unity logs are in
`TestResults/CoopRuntimeCompletion/`.

## 27. Builds / Smokes

| Gate | Command / evidence | Result |
|---|---|---|
| macOS non-development player | `ReleaseBuildTool.BuildMacBatch` → `build_report_macos.md` | **PASS** — succeeded, 0 errors, 2 service/symbol warnings, 176.0 MB |
| Windows x64 release build | — | **NOT RUN — Windows Build Support module unavailable** (external, §32) |
| Duo built-player proof, seed 46 | `run-coop-expedition-proof.sh … 2 46 …` → `duo_run_seed46/` | **PASS** — host 46/46 steps, client 6/6 |
| Duo built-player proof, seed 59 | `run-coop-expedition-proof.sh … 2 59 …` → `duo_run_seed59/` | **PASS** — host 45/45 steps, client 6/6 |
| Trio built-player proof, seed 46 | `run-coop-expedition-proof.sh … 3 46 …` → `trio_run_seed46/` | **PASS** — host 37/37, client1 5/5, client2 5/5 |
| Solo built-player smoke, seed 11 | `-smoke` on the built `.app` → `solo_smoke_result.json`, `solo_smoke.log` | **PASS** — stage `done`, first attempt |
| Solo built-player smoke, seed 31 | `solo_smoke_seed31_preexisting/` | FAIL ×3 at `noncombat: labs_event_01: WeaponCache prompt ''` — pre-existing smoke-stage ordering (the Weapon Cache stage consumes the real cache first); not a co-op regression (§30) |

## 28. Live UGS / Relay Status

`Live UGS Sessions/Relay: NOT RUN — project/service configuration unavailable`

No UGS project link or credentials exist here and none were installed. The built-player proofs join by direct address
(`NgoNetworkDriver.SetDirectAddress`) instead of a Relay join code; everything above the transport — approval, the
session link, every record and request — is the same code a Relay session runs.

## 29. Human Playtest Checklist

`TestResults/CoopRuntimeCompletion/HUMAN_PLAYTEST_CHECKLIST.md` — 28 diagnostic checks (same dungeon, topology, own
control, remote motion/aim, enemy consistency, lock/clear timing, loot once, correct recipient, no duplicate outcomes,
own inventory, Downed readability, revive both ways, wipe timing, spectator, boss sync, telegraph readability, vote
state, D1→D2 together, state survival, reconnect, RETURN, co-op menus not pausing, Solo unchanged, duplication/delay,
trio HUD at 640×360).

## 30. Known Pre-Existing Flakes

- `CombatAimCollisionProofTests` (PlayMode) fails in roughly half of full PlayMode runs on macOS, baseline included;
  it failed once in this pass (835/836) and passed in the final gate (838/838).
- The solo built-player smoke is seed-sensitive on macOS: seed 31 stops at the non-combat Weapon Cache prompt because an
  earlier smoke stage already used the real cache; seed 11 passes. Tolerances were not widened.
- `production/FINAL_MVP_COMPLETION_REPORT.md` is regenerated by the EditMode gate; on macOS its Windows build/smoke lines
  read NOT RUN. This is expected noise, not a content change.
- The Defibrillator consumable has no runtime revive hook in any mode (`revive_end_to_end_matrix.csv`: 1 NOT RUN row).

## 31. Files Changed

This pass only (the working tree also carries earlier passes' uncommitted work, preserved untouched).

**New runtime:** `Multiplayer/CoopMessages.cs`, `Multiplayer/CoopBus.cs`, `Multiplayer/CoopRunLink.cs`,
`Multiplayer/NetworkDungeonSync.cs` (split from `DungeonNetSync.cs`), `Multiplayer/CoopSession.cs`,
`Multiplayer/CoopHostWorld.cs`, `Multiplayer/CoopClientWorld.cs`, `Multiplayer/CoopPlayerDirectory.cs`,
`Enemies/IReplicatedActorView.cs`, `App/ExpeditionScene.Coop.cs`, `App/CoopExpeditionProof.cs`, `App/ProofInputReader.cs`.

**Modified runtime:** `Combat/DamageAuthority.cs`, `Combat/HealthComponent.cs`, `Combat/Impact/ImpactRequest.cs`,
`Combat/Projectiles/ProjectilePool.cs`, `Player/PlayerInteractor.cs`, `Player/DeadSpectator.cs`,
`Expedition/TransitDecision.cs`, `Expedition/ExpeditionService.cs`, `Multiplayer/PartyLobby.cs`,
`Loot/DungeonMerchantService.cs`, `Multiplayer/LootAuthority.cs`, `UI/Merchant/MerchantViewModel.cs`,
`UI/WeaponCache/WeaponCacheViewModel.cs`, `Presentation/Animation/EnemyAnimationDriver.cs`, `Presentation/Vfx/TelegraphIndicator.cs`,
`Multiplayer/EnemyNetSync.cs`, `Multiplayer/DungeonNetSync.cs`, `Multiplayer/NetworkPlayerObject.cs`,
`Multiplayer/NetworkPlayerMotion.cs`, `Multiplayer/NetworkPlayerCombat.cs`, `Multiplayer/PlayerNetMotion.cs`,
`Multiplayer/NgoPlayerPresence.cs`, `Multiplayer/NgoNetworkDriver.cs`, `Multiplayer/LiveServiceConfiguration.cs`,
`Multiplayer/HostAuthority.cs`, `Multiplayer/CoopPeerHarness.cs`, `App/CoopPeerRunner.cs`,
`App/PlayerRigComposer.cs`, `Events/DungeonEventInteractable.cs`, `App/ExpeditionScene.cs`, `App/BaseHubScreen.cs`,
`UI/Base/BaseHubViewModel.cs`, `App/GameApp.cs`, `App/GameContentCatalog.cs`, `App/Game.App.asmdef`.

**Editor:** `Editor/Production/NetworkPlayerPrefabAuthoring.cs`, `Editor/Production/GameContentCatalogBuilder.cs`,
`Editor/Production/CoopRuntimeCompositionValidator.cs`.

**Tests:** PlayMode `CoopExpeditionRuntimeTests.cs`, `CoopSoloRegressionTests.cs` (new); EditMode
`CoopRuntimeCompositionValidatorTests.cs`, `CoopFrozenSystemsTests.cs`, `StatConsumerIntegrityTests.cs`.

**Assets:** `Prefabs/Network/PlayerNetworkEntity.prefab`, `Prefabs/Network/CoopRunLink.prefab` (new),
`Assets/DefaultNetworkPrefabs.asset`, `Resources/GameContentCatalog.asset` (link reference only).

**Scripts:** `scripts/run-coop-expedition-proof.sh`, `scripts/coop_proof_matrices.py` (new).

**Reports/artifacts:** this report; `TestResults/CoopRuntimeCompletion/` (all matrices, run folders, logs, checklist).

## 32. Deferred External Dependencies

- **External dependency:** a UGS project link and credentials for live Sessions/Relay (§28).
- **External dependency:** Windows Build Support module for the release-platform build (§27).
- **Deferred design question (unchanged):** a brand-new player joining mid-expedition (81/83 fix the party at start).
- **Not currently available (pre-existing, not a co-op regression):** the Defibrillator consumable has no runtime revive
  hook in any mode.

## 33. Final Status

**COOP_RUNTIME_COMPOSITION_COMPLETE.** Every repository-local condition has passing evidence: real built-player Duo
(two seeds) and Trio runs cover host/join → identical D1 → synchronized enemies → authoritative loot/interactions →
down/revive → boss → Transit vote → shared descend to D2 → preserved state → reconnect → coherent RETURN/extraction;
PlayMode 838/838 and EditMode 975/977 (2 environment skips) pass; the macOS non-development build and the solo smoke
pass. Remaining NOT RUN items are external or pre-existing: live UGS/Relay (no project configuration), the Windows x64
build (module unavailable), and the Defibrillator revive hook (absent in every mode).
