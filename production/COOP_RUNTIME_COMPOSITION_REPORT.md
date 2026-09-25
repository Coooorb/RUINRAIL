# RUINRAIL — CO-OP RUNTIME COMPOSITION REPORT

> **SUPERSEDED — historical document (2026-09-25).** Current release status: `production/FINAL_RELEASE_CANDIDATE_AUDIT.md`. Its INCOMPLETE status was closed by production/COOP_RUNTIME_COMPOSITION_COMPLETION_REPORT.md. The body below is kept unchanged as the record of its time.


> Pass: co-op runtime composition (1–3 player expedition runtime).
> Environment: macOS, Unity 6000.3.24f1, NGO 2.7.0, com.unity.transport 2.6.0. Pinned versions unchanged.
> Evidence classes used below: **runtime-proven (built peers)**, **runtime-proven (in-process)**, **test-only**,
> **inspection**, **NOT RUN (environment)**, **open item**.

## 1. Executive Summary

The previously reported defect was confirmed exactly as described: every party system in the repository was correct,
unit-tested and party-aware, but the expedition runtime composed **one** player entity, so the whole party layer
behaved as solo by arithmetic. This pass composes the real party.

What is now real:

- `ExpeditionParty` composes one player entity per participant through the shipping `PlayerPresenceService`, adopting
  the run's own `PlayerRig` for this peer and building every other member with the null input reader. The composed
  count — never the promised one — feeds `DungeonRuntimeContext`, so a trio-scaled dungeon cannot exist without three
  composed players.
- Every member reaches `PartyLifeRoster` (Downed/revive/wipe, transit voters), the `LootAuthorityService` participant
  map (its own backpack and its own Carried wallet), and the HUD party rows.
- `DamageAuthority.LocalIsAuthoritative` is set from the session role at run composition and restored on teardown. It
  had never been assigned by any runtime composition: a client build would have applied damage locally.
- World pickups and coin piles in a co-op run resolve through the host arbiter (`PickupArbiter` → `LootAuthorityService`),
  so a shared object has exactly one winner and a coin pile is split across the party. Solo installs no arbiter at all.
- The shipped build now composes a real `NetworkManager` (a root object, as NGO requires in a player build) with
  `UnityTransport` and the registered network player prefab. Before this, live mode found no NetworkManager, returned a
  null driver and every "online" session silently fell back to `FakeMultiplayerServices`.
- `SessionPartyBridge` turns accepted connections into `PartyLobby` members, so READY/START and the expedition start
  snapshot describe the real party instead of one participant.
- The release network player prefab was missing `NetworkPlayerMotion` (no replicated motion at all), had **no**
  `PlayerBalanceConfig` (move speed 0, no dash/revive tuning) and did not replicate life state. All three are fixed.
- Real multi-peer proof: two and three **separate built-player processes** connect over a UDP socket on loopback, each
  owning exactly one real `NetworkObject` player, seeing the others as input-isolated kinematic replicas with no camera
  or listener, with motion replicated both ways and the host applying remote intents.

What is **not** finished, and is the one repository-local blocker: a joining client does not compose its own expedition
scene. The host composes and runs the whole party; a client peer connects, is admitted to the party, owns a replicated
character and can move it (proven across processes), but nothing on the client builds the dungeon from the host's
replicated start state, so two humans cannot yet play a full expedition end to end. Details and the exact remaining
work are in §36.

## 2. Previous Gap Reverification

Every previously reported finding was re-verified against the actual working tree before anything was changed
(`TestResults/CoopRuntimeComposition/current_state_before.csv`, 14 findings — 12 reverified, 2 found during this pass).

| # | Finding | Still true? | Status now |
|---:|---|---|---|
| 1 | `ExpeditionScene` is the only run composer and builds one `PlayerRig` | YES | Fixed — `ExpeditionParty` composes one entity per participant |
| 2 | It passes `isCoop: false` in multiple places (5 literal sites) | YES | Fixed — derived from the composed party |
| 3 | `PlayerPresenceService` and `NgoPlayerEntityFactory` are constructed only in tests | YES | Fixed — both have real runtime callers |
| 4 | `BaseSession` joins only the local client into the lobby path | YES | Fixed — `SessionPartyBridge` |
| 5 | `PartyLifeRoster` receives only the local player | YES | Fixed — every member is registered at spawn |
| 6 | Room reveal is local-player-only | YES | Fixed — discovery for any member; the reveal stays local |
| 7 | Party-size difficulty scaling can activate before matching player entities exist | YES | Fixed — scaling uses the composed count; an underfilled start is refused |
| 8 | UI pause semantics are solo-only | YES | Fixed — the real co-op flag reaches every run window |
| 9 | Mutable static service locators are a host/client correctness hazard | YES | Audited (§20); two real lifetime defects fixed with explicit ownership |
| 10 | Loot pickup authority is host-owned; inventory per player; the wallet was assumed per expedition | YES | `LootAuthorityService` composed and on the co-op pickup path; wallet ownership decided per 84 (§14) |
| 11 | *(found here)* `DamageAuthority.LocalIsAuthoritative` is never set from the network role | YES | Fixed — set at composition, restored on teardown |
| 12 | *(found here)* Transit voting is already party-aware and wired | NO — already correct | Unchanged; it only needed a populated roster |
| 13 | *(found here)* The release network player prefab is incomplete: no `NetworkPlayerMotion`, no `PlayerBalanceConfig` (move speed 0), no life-state replication | YES | Fixed — re-authored with balance/caps, motion and `NetworkPlayerLife` |
| 14 | *(found here)* A live build composes no `NetworkManager`, so every "online" session silently used the in-memory fake | YES | Fixed — the live path composes manager + transport + registered prefab |

## 3. Multiplayer Design Contract

`TestResults/CoopRuntimeComposition/multiplayer_contract_matrix.csv` holds the full 22-system matrix. The rules this
pass implements against, from the approved docs:

- **80**: co-op only, PvE, client-hosted, host-authoritative, maximum 3 players. No PvP, matchmaking, dedicated servers
  or host migration (all three `NetworkSessionController.Supports*` flags remain false).
- **81**: the host creates the session and receives a join code; only the host starts, only when every connected player
  is Ready with a valid loadout; a loadout change clears Ready.
- **82**: the host decides seed, room graph, enemy spawn/AI, damage, enemy death, chest/loot rolls, pickup validity,
  boss state, downed/death/revive, transit result and depth progression.
- **83**: Solo/Duo/Trio → Threat 100/140/175 %, enemy HP 100/120/135 %, boss HP 100/165/220 %, enemy damage always
  100 %. Fixed at expedition start; never reduced dynamically.
- **84**: 0 HP with a living teammate is Downed (20 s bleedout, 4 s revive, 30 % Max HP, ~1.5 s protection); a dead
  member's gear is not dropped; everyone Downed/Dead is a wipe and the run fails immediately.
- **85**: ~60 s reconnect grace with the character still represented and at risk; host disconnect ends the expedition.
- **86**: after the boss every living player votes; Continue needs unanimous approval; any Return returns the party;
  dead players have no vote.

## 4. Run-Level Composition

`Assets/Game/Scripts/App/ExpeditionParty.cs` (new) is the run-level party:

- `Compose(request, out error)` refuses a snapshot with more than 3 members (`TooManyMembers`), a snapshot that does not
  contain this peer (`NoLocalMember`) and a start that composes fewer entities than the snapshot promised
  (`Underfilled`) — the run fails rather than starting a party that is not there.
- It drives the existing `PlayerPresenceService`, which keeps its own one-entity-per-client invariant, and closes the
  party at start (`IsPartyClosed`): afterwards only a reconnecting member is admitted (81/83/85).
- A member whose entity could not be spawned is no longer counted at all (it used to produce a phantom member with a
  null GameObject, which would have let the dungeon scale for a player that does not exist).
- It registers every member with `PartyLifeRoster` and `LootAuthorityService`, provisions an expedition inventory for
  members that arrive without one, and composes `PartyCoinDistributor` across the party's wallets.
- It creates **no** camera, audio listener, local input reader or HUD; that separation is asserted by the validator.

Evidence: **runtime-proven (in-process)** — `player_presence_matrix.csv`, `party_scaling_integrity.csv`.

## 5. Per-Player Composition

One `PlayerEntityBuilder` composition per member, exactly as solo builds it (health, movement, aiming, dash, life state,
reviver, loot receiver, stats), with `IsLocal` false for every member but this peer's own — which is what makes a
replica input-isolated by construction (`NullPlayerInputReader`). The local member is the run's own `PlayerRig`, adopted
unchanged: same input, stats, progression and at-risk inventory as the solo path, and the party never destroys it.

On a live host session the members other than this peer are spawned as the release network player object through
`NgoPlayerEntityFactory` (injected as `PartyCompositionRequest.RemoteFactory`), so the other peers receive them.

Evidence: **runtime-proven (in-process)** for the composition; **runtime-proven (built peers)** for the networked
spawn (§24).

## 6. Local Presentation Separation

`local_presentation_matrix.csv`: for a composed trio, every member carries 0 cameras, 0 audio listeners, 0 HUDs, and
every non-local member carries 0 `PlayerInput` components. The whole party adds 0 listeners to the process; the one
listener lives on the persistent app root (`AudioListenerRig`), and the built-peer proof measured exactly 1 listener per
process at 1, 2 and 3 players. A remote member cannot be moved by this client's input (asserted over 12 physics steps).

## 7. Player Presence / Entity Factory Wiring

- `PlayerPresenceService`: runtime callers are `ExpeditionParty` (the run) and `CoopPeerHarness` (the built-player peer
  proof). Previously zero.
- `LocalPlayerEntityFactory` now also registers each spawned member with the party roster and its participant id.
- `NgoPlayerEntityFactory`: used by the live host path and by the peer harness. Previously zero callers.
- `ReconnectGraceService`: composed by the run with the 60 s default from 85, and by the peer harness.

## 8. Party-Size Scaling Integrity

`party_scaling_integrity.csv`. `ExpeditionParty.ScalingPartySize` is `Clamp(ComposedPartySize, 1, 3)` and is what
`DungeonRuntimeContext` receives; `IsFullyComposed` compares composed against expected and `Compose` refuses to return a
party when they disagree. A lobby that promises three members and can only compose one produces **no run**, not a
trio-scaled dungeon. Authored scaling values are untouched (§28).

## 9. Movement / Combat Authority

`authority_matrix.csv` + `authority_exploit_matrix.csv`.

- Movement: the owner predicts on its own input and sends intents; the host applies them through
  `RemoteIntentInputReader` and publishes state; replicas interpolate with a kinematic body. Proven across processes:
  each peer's own character moved ~10 units under its scripted input, and the host applied the client's intents for
  ~55 units of travel on the client's character (`HostAppliesRemoteIntents` true).
- No peer can move another player's entity: both `SubmitIntentRpc` and `RequestDashRpc` are `InvokePermission = Owner`
  (asserted by reflection), a replica has no `PlayerInput`, and the host-side intent reader exists only on the server.
- Damage stays host-owned: with `DamageAuthority.LocalIsAuthoritative` false, a client process applies no damage to an
  enemy and cannot down a teammate. The flag is now actually set from `NetworkSessionController.IsHostAuthority`.
- Aim assist remains a local preference and appears in no authority path.

## 10. Remote Visuals

`remote_visual_matrix.csv`. One composer for every peer (`NetworkPlayerObject.ComposeVisuals` → `PlayerVisualComposer`),
so a networked player gets the accepted body, animation driver and held weapon; the replicated weapon id drives the held
sprite and the replicated aim drives facing and the weapon pivot. Health already replicated; **life state did not** —
`NetworkPlayerLife` (new) publishes Downed/Dead and the bleedout clock and applies it through the existing
`PlayerLifeStateComponent.ApplyReplicatedState` seam, so a teammate bleeding out is visible as such on another peer.
No separate remote art path exists.

## 11. Camera / Input / Pause Semantics

- Solo is unchanged: `IsCoop` is false for a one-member party, so inventory/pause/merchant/cache/run-lost keep the
  accepted `TimeScalePause`.
- Co-op: every one of those windows receives the real co-op flag and does not touch `Time.timeScale`; the shared world,
  remote players, enemies and timers keep running, and only the local player's gameplay input is gated. A client cannot
  pause the host, and the host opening Pause is local UI.
- Camera: composed once by the run against `LocalEntity`; no member carries a camera, so a remote presence cannot steal
  or duplicate the camera target. Exactly one `AudioListener` per process, measured in every peer.

## 12. Room / Encounter Party State

`room_party_state_matrix.csv`, runtime-proven with three composed members in one room: one activation, one door lock,
one spawn set, one counted entry, one clear, doors unlock once, and members already inside are tracked as occupants. A
second or third member entering an active room changes nothing; a revisit after the clear re-raises only the
presentation entry. A client-authority room (`SetAuthoritative(false)`) never advances state locally. Map discovery is
now marked for **any** member's entry while the room-title reveal and `CurrentRoom` stay local to the owned player.

## 13. Loot Authority

`loot_race_matrix.csv`. Two members racing one pickup: exactly one `Accepted`, the other `AlreadyTaken`, the pickup
consumed once, no duplicate instance. A re-sent transaction id returns the stored result. The co-op run installs
`PickupArbiter`, so the **runtime interaction path** (walking onto the item and interacting) resolves through the host
arbiter — two members interacting with one pickup produce one winner. Solo installs no arbiter and takes the accepted
local path unchanged. Ammo caps and backpack capacity remain each player's own (the transfer service refuses what a
backpack cannot hold). No personal-instanced loot was added.

## 14. Coin / Merchant Ownership

`economy_ownership_matrix.csv`. The docs are not silent: **84** says a player who is still Dead when the team returns
loses all at-risk carried gear/loot/coins while living players secure theirs, which only has meaning if carried coins
are per member. So V1 co-op uses **per-member Carried wallets with an even split per 58**: a coin pile picked up by any
member is distributed by `PartyCoinDistributor` across the party's wallets (conserving the total exactly, remainder
rotating), and solo keeps the whole amount in the single wallet. Purchases debit only the buyer; two members buying one
offer serialise to one `Accepted` and one `AlreadyTaken`, and a replayed buy transaction never debits twice. No price,
sell percentage or event cost was changed.

## 15. Non-Combat / Choice Authority

`noncombat_authority_matrix.csv` covers Merchant, Medical Station, Broken Machine, Locked Vault, Weapon Cache, supply
chest/treasure, world pickups, coin piles, Transit and the event-choice windows: who may open (any member), who decides
(the interacting member), what happens on a simultaneous interaction (the second commit is refused, never charged and
never duplicated) and what locks while a choice is pending (the transaction id and the event's resolved state).

One honest limitation is recorded in that matrix: the in-world merchant/event/chest interactables still call their host
services directly rather than through `LootAuthorityService`. On the host process — the only process that runs an
expedition today — that is the same execution, and the arbiter's idempotency is proven for those request paths. Routing
them per client belongs with the client-side expedition composition (§36).

## 16. Downed / Revive / Wipe

`revive_wipe_matrix.csv`, runtime-proven with a composed trio: a member at 0 HP with living teammates goes **Downed**
and the run continues; a teammate's real 4 s revive channel returns them to Alive (one completion however often the
completing tick is replayed, and no second channel can open on the same target); the ~1.5 s revive protection exists and
lapses; when every member is Downed/Dead the roster reports a wipe and raises it exactly once. Solo is unchanged: 0 HP
is Dead, immediately. A Downed or Dead member performs no loot action (the authority gate refuses it), so a dead
teammate's carried gear cannot be salvaged. The paid Medical Station revive applies once per transaction.

## 17. Transit Voting

`transit_vote_matrix.csv`, with the voters derived from the composed party's roster: duo and trio need every living
player's approval to descend, one player's repeated vote stays one vote, any Return returns the whole party, and a
resolved decision does not change when votes arrive afterwards (one commit for Descend and for Return). Solo behaves as
before through the solo policy.

## 18. Depth Transitions

`depth_transition_matrix.csv`: the depth-arrival heal runs once for every **living** member to its own effective
maximum and leaves a Downed member alone; the party survives the transition with the same entities, participant ids,
roster membership and carried instances, and no member can add a camera or listener during a depth change. Depth-local
state (current room, revealed room, vote, notice, minimap depth) is cleared by `OnDepthEntered` (inspection).

## 19. Disconnect / Reconnect

`reconnect_matrix.csv`: a member disconnecting mid-expedition leaves the presence map but its character is held, alive
and at risk under its reconnect token (85); reconnecting with that token rebinds the same character to the new client id
without spawning anything (`SpawnCount` unchanged, `ReconnectCount` 1), and the party is whole again. A replayed token
cannot mint a second member. A request sent under the pre-reconnect client id is refused as unknown; the new id works.
The HUD shows a held member as DISCONNECTED. Host failure follows the existing rule — `NetworkSessionController` has no
migration path (the flag is const false), and the expedition ends through the one failure transaction that saves and
closes cleanly; no save schema changed in this pass.

## 20. Global Service Lifetime Audit

`global_service_lifetime_matrix.csv` audits nine process globals. Two were genuine co-op/runtime lifetime defects and
are fixed by explicit set/reset ownership in the composition root (not by a service-locator rewrite):
`DamageAuthority.LocalIsAuthoritative` (never set, now set from the role and restored to true on teardown) and the new
`PickupArbiter` (installed only for a co-op run, cleared on teardown). `RoomDoorLock.SkinResolver` and
`WorldObjectArt.Resolver` are cleared on teardown as before.

## 21. Solo Regression

`solo_regression_matrix.csv` — 21 solo aspects, each with the specific way this pass could have broken it and what
prevents that. The two changes that touch solo code paths at all are the pickup interaction (arbiter consulted only when
installed, and solo never installs it) and `PlayerInput.UseReader` (whose only caller in the repository is the
built-player peer harness).

## 22. Party UI

`party_ui_matrix.csv`, checked at 1, 2 and 3 members: one row per member including self, each named and showing that
member's health, a Downed teammate reading `DOWNED <n>s` from the authoritative bleedout, and a held connection reading
`DISCONNECTED`. The longest row stays within 32 characters, which fits the 640×360 reference HUD column; nothing was
added to the accepted HUD beyond the existing party block.

## 23. Multiplayer Terminal / Start Flow

`start_flow_matrix.csv`. Host: `SessionPartyBridge` adds every accepted connection to `PartyLobby`, refuses a fourth,
removes a member that leaves before the start, and the host can only start when everyone is Ready with a valid loadout.
The start snapshot then names the real party (`PartySize` 3 for three members), the expedition composes exactly those
members, and the dungeon is scaled for them. After the start the party is frozen: a late connection is not added, and a
stray connect during the expedition composes no entity. Solo remains local — no session, no transport.

Join: the join code path, session membership, identity and the client's owned character are real and proven across
processes (§24). What a joining client does **not** yet do is enter the expedition scene (§36).

## 24. Real Multi-Peer Runtime Proof

**Runtime-proven (built peers).** `runtime_peer_matrix.csv`.

The proof runs the shipped macOS player, once per peer, in separate operating-system processes:

```
RUINRAIL -batchmode -nographics -coop-peer host   -coop-port <p> -coop-size <n> -coop-out host.json
RUINRAIL -batchmode -nographics -coop-peer client -coop-port <p> -coop-size <n> -coop-out clientN.json
```

Each peer composes the shipping session path — `NgoConnectionEvents`, `SessionRoster`, connection approval,
`NgoPlayerEntityFactory`, `PlayerPresenceService` — over the `NetworkManager` the app's own live composition created
(with `UnityTransport` and the registered release prefab), and connects over a real UDP socket on 127.0.0.1. Nothing
about the composition is mocked: the player objects are real `NetworkObject`s and ownership is NGO's. Because the harness
uses the process manager the shipped boot path creates, this run also proves that composition works in a player build.

| Scenario | Peers | Party | Player objects | Owned per peer | Replicas seen | Own motion | Replicated motion observed | Cameras / listeners per process | Result |
|---|---:|---:|---:|---:|---:|---|---|---|---|
| solo | 1 process | 1 | 1 | 1 | 0 | 9.9 units | n/a (no other peer) | 0 / 1 | PASS |
| duo | 2 processes | 2 | 2 | 1 | 1 | 10.1 / 10.3 units | 54.6 units on the host, 29.6 on the client | 0 / 1 each | PASS |
| trio | 3 processes | 3 | 3 | 1 | 2 | 10.1 / 9.9 / 10.3 units | 55.0 units on the host, 29.2 and 29.5 on the clients | 0 / 1 each | PASS |

On every peer: exactly one owned player object, every replica with 0 cameras, 0 audio listeners, 0 local `PlayerInput`
components and a kinematic body, exactly one `AudioListener` in the process, the owned character Alive and able to act,
and on the host `HostAppliesRemoteIntents` true — the client's character moves on the host because the host applied the
intents the client sent. Approval sanitised each joining client's name through the display-name policy.

What this proof does **not** cover, stated plainly: the peers compose a session and player objects, not a dungeon. Loot
races, transit voting, depth transitions and life-state transitions are proven in-process against the real composition
(their own matrices), not across these peers, because a client does not yet build the expedition scene (§36). Live UGS
Relay is not involved — the peers exchange a direct address (§31).

Raw per-peer JSON reports and the peers' own log lines are kept as durable evidence in
`TestResults/CoopRuntimeComposition/peers/` (six peer reports, the log excerpts and the solo smoke result) and were
summarised into `runtime_peer_matrix.csv`.

## 25. Authority / Exploit Tests

**Runtime-proven (in-process)** — `authority_exploit_matrix.csv`, `CoopAuthorityExploitTests` (10 tests, all passing).
All 19 required scenarios plus three extras:

| Scenario | Outcome |
|---|---|
| duplicate pickup request | stored result returned; ledger holds one transaction |
| simultaneous pickup request | one `Accepted`, one `AlreadyTaken`, one copy of the item |
| wrong-owner inventory request | a member's loot never lands in another member's inventory |
| wrong-owner weapon request | **was exploitable** — `RequestDrop` trusted client-supplied containers, so a client could make a teammate drop an item. Fixed: the host resolves a drop only from the requester's own participant containers |
| duplicate merchant buy request | one debit; the replay returns the stored result |
| simultaneous merchant buy | second buyer sees it sold and is not charged |
| duplicate event-choice commit | one charge, one outcome; the other member is never charged |
| duplicate revive completion | one revive; a second channel cannot open on the same target |
| duplicate paid revive | one application |
| duplicate Transit vote | one counted vote per player |
| duplicate Return commit | one commit; the result does not change afterwards |
| duplicate Descend commit | one commit; later votes are ignored |
| reconnect duplication | **was exploitable** — a connect after a consumed token minted a new member mid-expedition. Fixed: the party is closed at expedition start, so only an existing member (or a reconnect that re-registers itself) is admitted |
| stale client request after reconnect | refused as unknown; the new client id works |
| dead player combat/loot request | refused (`participant cannot act (downed/dead)`) |
| disconnected player request | refused as unknown participant |
| client attempt to damage enemy directly | refused; enemy health unchanged |
| client attempt to down a teammate | refused; life state unchanged |
| client attempt to authoritatively move another player | owner-only RPC permission on both intent and dash |
| double boss-cache / shared cache claim | one payout |
| transaction id collision across clients | two independent transactions; neither client can read or block the other's |
| client resolves loot locally | `NotAuthority`; the pickup is not consumed and nothing is recorded |

No existing host-authority restriction was weakened; two were tightened.

## 26. Performance

`coop_performance.csv`, measured in-process on the real composition (120-frame samples, from the full PlayMode run):

| Scenario | Compose | Player objects | Live object delta | Avg frame | Alloc over 120 frames | Objects left after teardown |
|---|---|---:|---:|---|---|---:|
| solo | 0.1 ms | 1 | 0 | 0.19 ms | 12 KB | 0 |
| duo | 0.3 ms | 2 | 1 | 0.19 ms | 8 KB | 0 |
| trio | 0.4 ms | 3 | 2 | 0.20 ms | 12 KB | 0 |

No pathology: a trio does not cost a multiple of solo (0.20 ms vs 0.19 ms per frame), allocations over the sample stay
around 10 KB regardless of party size, and
every member the party created is destroyed with it (the adopted local rig survives by contract, since the run owns it).
Network messages/second and bytes/second are **NOT MEASURED** — this environment has no transport metrics tooling, and
estimating them would be inventing numbers. Nothing was micro-optimised.

## 27. Validator / Contract Changes

`Assets/Game/Scripts/Editor/Production/CoopRuntimeCompositionValidator.cs` (new) — **21 rules (16 static + 5 supplied by
real composition facts), all passing**, report at
`TestResults/coop_runtime_composition.md`, driven in the gate by `CoopRuntimeCompositionValidatorTests` (EditMode), which
also supplies composition facts by actually composing 1-, 2- and 3-player parties and by proving the underfilled start is
refused. The rules: scope (max 3, no migration/matchmaking/dedicated servers); a real runtime caller for
`PlayerPresenceService`, the player entity factory, `LootAuthorityService` and `ReconnectGraceService`; the network
prefab registered with NGO; no `isCoop: false` literal in the run; co-op pause gating; run-level vs local-presentation
separation; remote input isolation; party wiring; damage authority derived from the session role; room party state; the
loot arbiter installed for co-op and cleared on teardown; replicated life state and the prefab's network components;
and live session composition (NetworkManager + transport + lobby bridge).

## 28. Frozen-System Verification

`frozen_systems_check.csv` — every listed frozen system, either asserted here against the shipped data or pinned by a
named suite in the same gate. The co-op scaling table is asserted numerically (100/140/175, 100/120/135, 100/165/220,
damage 100 %, caps 10/14/18), as are the party limit of 3, the 60 s reconnect grace, 33 weapons in 11 classes, the Field
Knife's presence and the pickup-attraction radii. No frozen tolerance was widened and no authored value was changed; no
weapon, enemy, boss, economy, depth-scaling or audio asset was touched in this pass.

## 29. Automated Tests

Commands actually run, in the order the harness requires (PlayMode before EditMode, because the final-audit test reads
`TestResults/PlayMode-results.xml`):

```
./scripts/run-unity-tests.sh PlayMode
./scripts/run-unity-tests.sh EditMode
```

| Suite | Result |
|---|---|
| **PlayMode (full)** | **PASS — 823 passed / 823 discovered, 0 failed** |
| **EditMode (full)** | **PASS — 974 passed / 976 discovered, 0 failed, 2 skipped** |
| Co-op runtime composition validator (21 rules) | PASS — `TestResults/coop_runtime_composition.md` |

The two EditMode skips are both environment-gated and were skips before this pass:
`D1AmmoBlasterFineTuneTests.CalibrateLightAmmoQuantity` (a calibration helper) and
`MultiplayerTerminalTests.LiveSessionsRelay_IntegrationCheck_OrNotRun` — which is precisely the live-service check that
skips itself when no UGS project is configured (§31).

An earlier full PlayMode run in this pass finished 822/823 with `CombatAimCollisionProofTests` failing; it is the
documented macOS flake (§33) and it passed both in isolation and in the full rerun above. The rerun of EditMode after
that was green, including `FinalMvpAuditTests`, which requires the PlayMode suite to be green.

New suites in this pass, all passing:

| Suite | Tests | What it proves |
|---|---:|---|
| `CoopRuntimeCompositionTests` (PlayMode) | 9 | party composition for 1/2/3, refusals, remote isolation, loot race including the arbitrated runtime interaction, downed/revive/wipe, solo death, disconnect/reconnect, transit voting |
| `CoopAuthorityExploitTests` (PlayMode) | 10 | the 19 required exploit scenarios plus three extras (§25) |
| `CoopRoomAndStartFlowTests` (PlayMode) | 2 | one activation/lock/clear for a party; session membership → lobby → snapshot → composed party |
| `CoopEconomyDepthUiTests` (PlayMode) | 3 | coin split and wallet ownership, depth-transition survival and the arrival heal, party HUD at 1/2/3 |
| `CoopPerformanceTests` (PlayMode) | 1 | party cost and teardown |
| `CoopRuntimeCompositionValidatorTests` (EditMode) | 1 | the 21-rule contract with real composition facts, including the underfilled-start refusal |
| `CoopFrozenSystemsTests` (EditMode) | 1 | the frozen-value check that writes `frozen_systems_check.csv` |

Pre-existing suites covering this area, re-run as part of the full gate: `MultiplayerGateTests`, `PlayerPresenceTests`,
`DisconnectGraceTests`, `NetworkLootAuthorityTests`, `NetworkCombatTests`, `NetworkHealthTests`, `NetworkMotionTests`,
`NetworkEnemyAuthorityTests`, `PlayerReviveTests`, `CoopScalingRuntimeTests`, `RemotePlayerVisualTests`,
`CoopScalingTests`, `ExploitHardeningTests`, `PersistenceHardeningTests`, the save/migration suites,
`FinalMvpAuditTests`, `FinalProductionValidatorTests`, `ContentCountValidatorTests`, the stat-consumer, run-variety and
presentation validators, and the solo regression suites.

## 30. Built-Player / Runtime Smokes

**Build (available platform).**

```
Unity -batchmode -nographics -executeMethod RuinRail.EditorTools.Production.ReleaseBuildTool.BuildMacBatch
```

- **macOS StandaloneOSX, non-development: Succeeded** — 0 errors, 2 warnings, 175.8 MB, 37 s
  (`TestResults/build_report_macos.md`).
- `Windows x64: NOT RUN — module unavailable` (Windows Build Support is not installed and was not installed for this
  pass, per the task's rule). The release target remains Windows x64; the macOS build is the local verification
  substitute.

**Solo built-player smoke.** PASS on the first attempt with the final binary:

```
RUINRAIL -batchmode -nographics -smoke -savedir <fresh> -logFile <abs>
```

`Success: true`, stage `done`, no error. Scenes composed `MainMenu;Base;Dungeon;Base;Dungeon;Base;Dungeon;MainMenu`, 10
rooms, depth 3 reached and persisted, save written and reloaded, Pause → RETURN TO MAIN MENU verified, and every check
group populated: 15 playability, 12 combat, 26 loot, 24 audio, 35 inventory, 24 HUD, 15 merchant, 31 room/HUD, 41
economy/containment/projectile, 7 starter fallback, 9 death screen, 39 progression, 50 depth/settings/non-combat, 21
stat-consumer, 12 run-variety and 29 presentation/QoL checks. (`MusicPeakSamplesNonZero` is 0 in a `-nographics`
batch run, as in the previous pass; audio presence is verified outside Unity.)

**Duo runtime smoke.** PASS — two built-player processes, real NGO session over UnityTransport on loopback (§24).

**Trio runtime smoke.** PASS — three built-player processes (§24).

**Live UGS smoke.** `Live UGS Sessions/Relay: NOT RUN — project/service configuration unavailable` (§31).

## 31. Live UGS / Relay Status

`Live UGS Sessions/Relay: NOT RUN — project/service configuration unavailable`

No UGS project link or credentials exist in this environment. Per the task's rules nothing was installed or configured
and no live Relay result is claimed. What this means precisely: `UnityMultiplayerServices` is composed in a release build
and the transport now exists, but the Relay allocation handoff and join-code exchange through Unity's Sessions service
are unverified. The multi-peer proof deliberately bypasses Relay by connecting to a direct loopback address, which is why
it is reported as a local-network proof and not as a live-service proof.

## 32. Human Playtest Checklist

`TestResults/CoopRuntimeComposition/HUMAN_PLAYTEST_CHECKLIST.md` — 35 diagnostic checks covering party creation and join
codes, one character per participant, per-machine control, remote animation/aim/held weapon, co-op menus not pausing the
world, single room activation with staggered entry, pickup races, coin splitting, merchant/event double-spend, downed and
revive readability, wipe timing, spectator, transit voting and descending together, depth-transition state, reconnect,
host failure, trio feel, 640×360 party readability and a solo comparison pass. The header states which checks cannot yet
be answered from the client side.

## 33. Known Pre-Existing Flakes

- **Built-player smoke is seed-flaky on macOS**: historically it passes roughly one run in three on this machine,
  failing on generation-dependent checks (non-combat room mix, projectile/collision geometry, pack pursuit). This
  predates both this pass and the previous one; a fresh `-savedir` and an absolute `-logFile` per attempt are required.
  In this pass it passed on the **first** attempt with the final binary.
- **`CombatAimCollisionProofTests` is flaky on macOS**: it fails about half of full PlayMode runs even on a clean
  baseline; a single rerun is the established way to distinguish it from a regression. In this pass it failed one full
  run (on two different assertions across isolated repeats, which is the signature of the geometry flake, not of a
  deterministic break), passed in isolation, and passed in the full rerun that the reported result comes from.
- **The final-audit report is regenerated by EditMode**: `production/FINAL_MVP_COMPLETION_REPORT.md` is rewritten on
  every EditMode run and, on macOS, records the Windows build and its smoke as NOT RUN.
- Unity's batch harness cannot run while the Editor holds the project lock.

## 34. Files Changed

**New (runtime):**

- `Assets/Game/Scripts/App/ExpeditionParty.cs` — the run-level party composition.
- `Assets/Game/Scripts/App/CoopPeerRunner.cs` — built-player `-coop-peer` entry.
- `Assets/Game/Scripts/Multiplayer/CoopPeerHarness.cs` — the real host/client peer over UnityTransport.
- `Assets/Game/Scripts/Multiplayer/SessionPartyBridge.cs` — accepted connections become lobby members.
- `Assets/Game/Scripts/Multiplayer/NetworkPlayerLife.cs` — replicated life state and bleedout.
- `Assets/Game/Scripts/Loot/PickupArbiter.cs` — the co-op pickup arbitration seam.

**New (editor / tests):**

- `Assets/Game/Scripts/Editor/Production/CoopRuntimeCompositionValidator.cs`
- `Assets/Game/Tests/EditMode/CoopRuntimeCompositionValidatorTests.cs`
- `Assets/Game/Tests/EditMode/CoopFrozenSystemsTests.cs`
- `Assets/Game/Tests/PlayMode/CoopRuntimeCompositionTests.cs`
- `Assets/Game/Tests/PlayMode/CoopAuthorityExploitTests.cs`
- `Assets/Game/Tests/PlayMode/CoopRoomAndStartFlowTests.cs`
- `Assets/Game/Tests/PlayMode/CoopEconomyDepthUiTests.cs`
- `Assets/Game/Tests/PlayMode/CoopPerformanceTests.cs`

**Modified (runtime):**

- `Assets/Game/Scripts/App/ExpeditionScene.cs` — composes the party, the loot authority and the arbiter; damage
  authority from the session role; party HUD names and disconnect/reconnect rows; map discovery for any member; explicit
  global resets on teardown.
- `Assets/Game/Scripts/App/GameApp.cs` — live composition now creates the NetworkManager; `-coop-peer` entry.
- `Assets/Game/Scripts/App/GameContentCatalog.cs` — carries the network player prefab for runtime spawning.
- `Assets/Game/Scripts/App/BaseHubScreen.cs` — approval roster, connection payload and the lobby bridge.
- `Assets/Game/Scripts/Multiplayer/PlayerPresence.cs` — roster/participant wiring, closed party, no phantom member,
  editor-safe despawn.
- `Assets/Game/Scripts/Multiplayer/LootAuthority.cs` — a drop resolves only from the requester's own containers.
- `Assets/Game/Scripts/Multiplayer/LiveServiceConfiguration.cs` — composes NetworkManager + UnityTransport + prefab.
- `Assets/Game/Scripts/Multiplayer/NgoNetworkDriver.cs` — exposes live connections, the spawn factory, approval and the
  connection payload.
- `Assets/Game/Scripts/Multiplayer/ReconnectGrace.cs` — the 60 s default from 85.
- `Assets/Game/Scripts/Player/PlayerInput.cs` — `UseReader` (proof/diagnostic seam; only the peer harness calls it).
- `Assets/Game/Scripts/Loot/WorldItemPickup.cs`, `Assets/Game/Scripts/Loot/CoinPickup.cs` — consult the arbiter.

**Modified (editor / assets):**

- `Assets/Game/Scripts/Editor/Production/NetworkPlayerPrefabAuthoring.cs` — authors balance/caps, `NetworkPlayerMotion`
  and `NetworkPlayerLife` into the prefab.
- `Assets/Game/Scripts/Editor/Production/GameContentCatalogBuilder.cs` — fills the prefab reference.
- `Assets/Game/Prefabs/Network/PlayerNetworkEntity.prefab` — re-authored (balance + motion + life replication).
- `Assets/Game/Resources/GameContentCatalog.asset` — rebuilt by the builder (adds the prefab reference).

No weapon, enemy, boss, biome, economy, audio, art or save-schema asset was modified.

## 35. Deferred / External Dependencies

- **External dependency:** a UGS project link and credentials for live Sessions/Relay (§31). Not installable from here.
- **External dependency:** Windows Build Support module for the release platform build (§30). Not installed on request.
- **Deferred (design question):** a brand-new client connecting mid-expedition is refused, because 81/83 fix the party at
  start and the docs describe no join-in-progress. If join-in-progress is wanted, that is a design decision, not a bug.
- **Deferred (needs the client-side expedition):** routing merchant/event/chest requests per client through
  `LootAuthorityService`; replicating the lobby roster to clients so a joining player sees the party in the terminal;
  publishing the run seed/depth through the existing, still-uncomposed `NetworkDungeonSync`.

## 36. Final Status

**`COOP_RUNTIME_COMPOSITION_INCOMPLETE`**

Everything in §4–§28 is implemented and evidenced, both full suites are green (PlayMode 823/823; EditMode 974 passed,
0 failed, 2 environment-gated skips), the 21-rule composition validator passes, the available-platform build succeeds and
the solo built-player smoke passes. Of the 34 stated success conditions, 33 hold. One repository-local requirement from
Phase 19 ("Join … enters same expedition") remains unresolved, so this report does not claim completion — a second human
can join a session and control a real replicated character across processes, but cannot yet play an expedition.

### The blocker

**A joining client does not compose its own expedition scene.** `ExpeditionScene` is a host/solo composition root: it
builds its own `PlayerRig`, generates the depth locally and owns the expedition state. A client peer today:

- joins the session, is approved, is added to the party lobby and gets a sanitised identity — **works**;
- receives and owns exactly one real networked character, moves it through the host-validated intent path, and sees the
  other members as input-isolated replicas with replicated motion, weapon, health and (now) life state — **works,
  proven across processes**;
- but stays in the Shelter when the host starts: nothing on the client loads the Dungeon scene from the host's start
  state, so a second human cannot play a full expedition yet.

What that needs, concretely (none of it is started, all of it is repository-local):

1. Publish the run's start state to clients. `DungeonSyncPayload` / `NetworkDungeonSync` already exist for exactly this
   (seed, depth, biome, pool and layout fingerprints) and have **no runtime caller**; the host must own one and publish
   on start and on each depth.
2. A client branch in the run composition root: load the Dungeon scene on the replicated start, rebuild the depth from
   the replicated seed, verify the fingerprints before gameplay, and run rooms with `SetAuthoritative(false)` (already
   supported) so the client simulates nothing.
3. Adopt the owned `NetworkObject` as the local player instead of building a `PlayerRig` entity: `PlayerRig` needs an
   "attach to an existing object" path so the client's loadout, stats, consumables, camera, HUD and input bind to the
   replicated character it owns.
4. Route the client's loot/merchant/event/revive interactions to the host as requests through the already-composed
   `LootAuthorityService` (the arbiter and its guarantees exist; only the per-client request path is missing).
5. Mirror the expedition's shared state to clients: depth/transit/vote state, resolved room state (so a chest the host
   opened is not unopened on a client) and the ground-loot snapshot (`GroundLootSnapshot` already exists for late
   joiners).
6. Replicate enemies. Enemies are host-spawned plain GameObjects today; `EnemySpawnRecord` / `EnemyNetState` /
   `EnemyNetSync` exist as serialization with **no runtime caller**, and enemy prefabs are not registered with NGO. A
   client that entered the dungeon with the work above alone would see an empty, enemy-less copy of the host's depth.

That last point is why this is not a small remainder: steps 1–5 are composition work on existing seams, but step 6 is a
feature (networked enemy spawn/state) of comparable size to this whole pass. The same is true of the boss.

These are coherent next tasks, not loose ends of this one: they change the run composition root's lifecycle and add
networked enemies, and doing either unverified here would have put the green solo path at risk, which this task forbids.

### Status by category

- **Implemented and runtime-proven (built peers):** live session composition, connection approval and identity, one
  owned networked character per peer, remote input isolation, replicated motion both directions, host-applied remote
  intents, one camera/listener per process at 1–3 players, trio across three processes.
- **Implemented and runtime-proven (in-process, real composition):** party composition for 1/2/3, underfilled-start
  refusal, party scaling integrity, roster/loot registration, room activation and clear for a party, pickup race through
  the runtime interaction path, coin split, downed/revive/wipe, transit voting for duo/trio, depth-arrival heal and party
  survival, disconnect/reconnect identity, party HUD at 1–3 players, start flow from session membership to composed
  party, performance.
- **Test-only proof:** the authority/exploit matrix and the replicated-life-state and RPC-permission assertions (they
  assert host decisions and declarations, not cross-process behaviour).
- **Inspection:** depth-local state clearing, the non-combat interactables' current direct host calls, global lifetime
  ownership.
- **NOT RUN (environment):** live UGS Sessions/Relay; Windows x64 build and its smoke.
- **Deferred design question:** join-in-progress for a brand-new client.
- **Blocker:** client-side expedition composition (above), which also requires networked enemies.

### What is safe to do now

The host-side party runtime, the authority hardening, the prefab and live-session composition fixes and the solo
regression evidence stand on their own: solo is unchanged and green, and the host now really does compose and run a 1–3
player party. Nothing in this pass has to be reverted or held back while the client-side expedition work is scheduled.
