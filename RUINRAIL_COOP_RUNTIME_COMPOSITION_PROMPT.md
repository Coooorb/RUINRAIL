# RUINRAIL — CO-OP RUNTIME COMPOSITION PASS

## ROLE

You are performing the dedicated multiplayer/runtime-composition pass for RUINRAIL.

RUINRAIL's approved product concept is a 1–3 player PvE extraction roguelite with no PvP.

The repository already contains extensive multiplayer/networking infrastructure, but the previous full-game review found that the SHIPPING expedition composition is still solo-shaped:

- `ExpeditionScene` builds exactly one `PlayerRig`
- `PlayerPresenceService` is test-only
- `NgoPlayerEntityFactory` is test-only
- several runtime presentation paths are hardcoded with `isCoop: false`
- only the local player is registered in `PartyLifeRoster`
- room reveal is local-player-only
- party-size difficulty scaling can already activate before the run actually has matching player entities

This task must close that gap.

This is an IMPLEMENTATION + VALIDATION task.

Do not merely improve isolated network tests.
Do not stop after proving NGO components in isolation.
The goal is to make the ACTUAL run composition support 1, 2, or 3 players.

Work autonomously.
Do not pause for approval.
Do not ask intermediate questions.
Do not stop after static analysis.
Inspect the current repository and current multiplayer design docs before changing anything.

---

# REPOSITORY SAFETY — CRITICAL

The current working tree contains intentional uncommitted work and is authoritative.

NEVER use:

- `git checkout`
- `git restore`
- `git reset`
- `git clean`
- `git stash`
- destructive cleanup
- broad file reverts
- any command that discards unrelated uncommitted work

Do not “clean up” files outside this task.

If you need to undo your own edit, restore it manually from content you inspected during this task.

---

# AUTHORITATIVE PRODUCT DECISION

Co-op STAYS IN V1.

Do NOT de-scope multiplayer.

RUINRAIL must support:

- Solo
- 2-player co-op
- 3-player co-op
- PvE only
- host-authoritative gameplay
- private host/join-code flow
- no PvP
- no dedicated servers
- no host migration unless already implemented and approved
- no matchmaking unless already implemented and approved

Do not add scope beyond the existing multiplayer design documents.

---

# IMPORTANT CURRENT PROJECT STATE

The following multiplayer/network systems reportedly already exist and are tested in isolation:

- `PartyLobby`
- ready-state validation
- `MultiplayerTerminalService`
- Solo / Host / Join flow
- join codes
- error model
- `NetworkSessionController`
- `IMultiplayerServices`
- `UnityMultiplayerServices`
- `NgoNetworkDriver`
- host-authority contract
- `PlayerNetMotion`
- `NetworkPlayerMotion`
- `NetworkPlayerCombat`
- `EnemyNetSync`
- `HealthNetSync`
- `DungeonNetSync`
- `WeaponNetSync`
- `LootAuthority`
- `ReconnectGrace`
- `PartyReviveAuthority`
- transit voting
- `PlayerNetworkEntity.prefab`
- deterministic local multiplayer test doubles / harnesses

Do not rewrite these systems simply because they are not currently composed.

Prefer connecting the existing architecture.

---

# PREVIOUS REVIEW FINDINGS TO RE-VERIFY

The earlier full-game review found:

1. `ExpeditionScene` is the only run composer and builds one `PlayerRig`.
2. It passes `isCoop: false` in multiple places.
3. `PlayerPresenceService` and `NgoPlayerEntityFactory` are constructed only in tests.
4. `BaseSession` joins only the local client into the lobby path.
5. `PartyLifeRoster` receives only the local player.
6. room reveal is local-player-only.
7. party-size difficulty scaling can scale for trio while only one player exists.
8. UI pause semantics are solo-only.
9. several mutable static service locators are potential host/client correctness hazards.
10. loot pickup authority is already shared/host-owned, while inventory is per-player and `CarriedWallet` is per-expedition.

DO NOT assume these are still all true.
Re-verify against the current working tree before editing.

Create:

`TestResults/CoopRuntimeComposition/current_state_before.csv`

For every finding record:

- finding
- previous state
- current code path
- still true YES/NO
- evidence
- required action

---

# PRIMARY OBJECTIVE

At the end of this pass, the actual runtime expedition path must be capable of composing a 1-, 2-, or 3-player run.

That means:

- all connected party members have actual player entities in the dungeon
- each client controls only its own player
- host authority owns authoritative combat/world decisions
- remote players are visible and synchronized
- each client gets its own local presentation/UI without duplicating global run systems
- difficulty scaling uses the number of actual expedition participants
- downed/revive/wipe logic works in the real run
- shared world interactions are authority-safe
- transit voting works in the real run
- reconnect/disconnect semantics are wired
- solo behavior remains unchanged
- live UGS configuration remains an external environment dependency only, not a missing runtime-composition feature

---

# PHASE 1 — READ THE MULTIPLAYER DESIGN CONTRACT

Before implementing, inspect the authoritative multiplayer docs and current networking code.

At minimum determine the repository's intended rules for:

- lobby ownership
- party size
- ready/start conditions
- host/client authority
- inventory ownership
- loot pickup race
- carried coins / wallet ownership
- XP ownership
- death/downed/revive
- wipe
- disconnected player handling
- reconnect grace
- transit voting
- dead-player voting
- dungeon seed synchronization
- room reveal
- merchant interaction
- weapon cache/event choices
- extraction/return
- host failure
- save responsibility

Create:

`TestResults/CoopRuntimeComposition/multiplayer_contract_matrix.csv`

Columns:

- System
- DesignDocRule
- ExistingRuntimeRule
- ExistingNetworkRule
- DecisionForThisPass
- Evidence
- Notes

If design docs and implementation disagree:
- follow the approved design docs if the rule is explicit
- document the discrepancy
- do not invent a third rule

If the docs are genuinely silent:
- preserve the current authoritative gameplay/economy semantics
- choose the smallest multiplayer-safe behavior
- document it as a deferred design question
- do NOT redesign the economy

---

# PHASE 2 — SEPARATE RUN COMPOSITION FROM LOCAL PRESENTATION

The prior review identified `ExpeditionScene` as the major structural blocker.

Refactor ONLY as far as needed for real co-op composition.

Do not perform a general architecture beautification pass.

Preferred structure:

## A. Run-level composition

A run-level object/service owns systems that exist ONCE per expedition:

- generated dungeon
- seed/depth
- encounter director
- enemy authority
- loot authority
- dungeon synchronization
- shared transit state
- shared boss state
- shared run outcome
- party roster
- shared carried-wallet semantics if that is the current design
- room state
- music state where process-global
- world substrate
- run-level save/extraction transaction coordinator

Use a repository-consistent name such as:

`ExpeditionRuntime`
`ExpeditionRunContext`
`RunComposition`
or equivalent.

Do not force a specific class name if the project already has the right abstraction.

## B. Per-player runtime composition

Each actual player entity gets:

- player state
- health/life state
- inventory/loadout
- weapon runtime
- consumables
- stats
- player network entity
- network motion/combat bridge
- player visual composition
- presence identity
- authority identity

## C. Local presentation composition

Only the LOCAL player on a client gets:

- local input reader
- camera ownership/follow
- local HUD
- inventory UI
- pause UI
- Settings interaction
- local cursor
- local tutorials
- local screen navigation
- local aim-assist preference
- local audio listener relationship
- other client-local UX

Remote players must NOT create:
- extra cameras
- extra AudioListeners
- extra HUDs
- duplicate pause stacks
- duplicate local input
- duplicate local cursor services

The goal is not to reduce line count for its own sake.
The goal is to remove the “one player == the run” assumption.

---

# PHASE 3 — ACTUAL PLAYER PRESENCE COMPOSITION

Wire the existing player presence/factory architecture into the real expedition path.

Use the existing:

- `PlayerPresenceService`
- `NgoPlayerEntityFactory`
- `PlayerNetworkEntity.prefab`

where appropriate.

Requirements:

- host entity exists
- each connected client has exactly one authoritative network player entity
- no duplicate player for one client id
- no missing entity for a joined expedition participant
- host can distinguish local vs remote
- client can distinguish owned vs non-owned
- disconnect removes/marks presence according to existing reconnect design
- reconnect re-associates the correct player identity
- late/reconnect behavior does not mint an extra player
- scene/depth rebuild preserves party identity

Create:

`TestResults/CoopRuntimeComposition/player_presence_matrix.csv`

Cover:

- solo
- duo host
- duo client
- trio host
- trio client A
- trio client B
- disconnect
- reconnect inside grace
- reconnect after grace
- duplicate-join rejection
- depth transition

---

# PHASE 4 — PARTY SIZE MUST MATCH REAL PARTICIPANTS

Close the latent scaling bug.

The expedition may use co-op scaling ONLY when matching expedition player presence is actually established.

Do not allow:

party says 3
→ dungeon scales for 3
→ only one player entity exists

Required:

- participant snapshot is validated before expedition starts
- run composition expects the same participant count
- actual spawned presence reaches expected count before gameplay unlocks
- timeout/error returns a clear multiplayer error rather than silently starting underfilled
- disconnect AFTER the run starts follows the existing disconnect/reconnect design; do not live-rescale enemies unless design docs explicitly say so

Preserve the approved party scaling values.

Do not rebalance:
- threat scaling
- enemy HP co-op scaling
- boss HP co-op scaling

Create:

`TestResults/CoopRuntimeComposition/party_scaling_integrity.csv`

---

# PHASE 5 — LOCAL / REMOTE PLAYER VISUALS

Remote player visuals were previously partially handled through `PlayerVisualComposer` / replicated aim.

Re-verify the current path.

Every remote player must visibly have:

- body animation
- facing
- held weapon sprite
- replicated weapon id
- replicated aim direction
- muzzle alignment
- health/life-state presentation as designed
- downed/dead presentation
- no local-player-only cursor/aim marker unless design requires it

Do not redesign character/weapon art.

Do not create a separate remote art pipeline.

Use the same accepted visuals through the network representation.

Create:

`TestResults/CoopRuntimeComposition/remote_visual_matrix.csv`

---

# PHASE 6 — MOVEMENT / COMBAT AUTHORITY

Wire real expedition players through the existing authority model.

Requirements:

## Movement
- owner sends/owns input according to current design
- remote movement synchronizes
- interpolation works
- remote players respect world collision
- remote players cannot leave room containment through network drift
- no owner can move another player's entity

## Combat
- only owner may submit its own fire/reload/switch/consumable intents
- host validates/runs authoritative weapon result per current architecture
- damage authority remains host-owned
- remote muzzle/shot presentation matches replicated state
- no double damage
- no client-side damage minting
- no duplicate projectiles
- aim assist remains local preference only and must not alter host authority semantics

## Inventory / equipment
- each player's equipment state replicates enough for correct remote visuals
- one player's inventory UI cannot alter another player's inventory
- authoritative equip/unequip remains valid

Create:

`TestResults/CoopRuntimeComposition/authority_matrix.csv`

Include malicious/wrong-owner request tests.

---

# PHASE 7 — CAMERA / INPUT / PAUSE SEMANTICS

The previous runtime hardcoded solo pause semantics.

Fix the real run.

## Solo
Preserve current behavior.

If current solo pause/inventory intentionally pauses time, keep it.

## Co-op
Opening:
- inventory
- pause
- merchant
- weapon cache
- event choice
- settings
- help/codex

must NOT pause the shared world via `Time.timeScale`.

Instead:
- gate only that local player's gameplay input as appropriate
- world simulation continues
- remote players continue
- enemies continue
- shared timers continue

Do not allow a client to pause the host simulation.

If the host opens Pause in co-op, same rule: local UI only.

Camera:
- exactly one active gameplay camera per client
- follows local-owned player
- remote presence must not steal camera target
- local player death/spectator follows existing spectator design

Audio:
- exactly one AudioListener per process/client
- remote player composition must not add one

Create:

`TestResults/CoopRuntimeComposition/local_presentation_matrix.csv`

---

# PHASE 8 — ROOM ENTRY / REVEAL / ENCOUNTER ACTIVATION

The old review found room reveal was local-player-only.

Make room state party-aware.

Define behavior from current docs/current RoomRuntime semantics.

Requirements:

- room discovery should become visible to the party according to design
- entering with any valid party member activates the encounter exactly once
- encounter lock occurs exactly once
- a second player entering cannot duplicate spawn/lock/reward
- players already inside are recognized
- late player entry into an active room joins the same encounter state
- room clear is global/shared
- room doors unlock once
- unused exits remain sealed correctly
- minimap state is synchronized appropriately

Do not rebuild dungeon generation.

Create:

`TestResults/CoopRuntimeComposition/room_party_state_matrix.csv`

---

# PHASE 9 — LOOT / PICKUP AUTHORITY

Preserve the current intended rule that a world pickup is a single shared object unless the design docs explicitly say otherwise.

Use existing `LootAuthority`.

Required:

- two players racing for one pickup → exactly one succeeds
- item goes to the correct player's inventory
- failed claimant receives no duplicate
- pickup despawns once
- ammo respects that player's cap
- backpack capacity respects that player's inventory
- pickup attraction only attracts for the player who can actually collect it
- Magnetic Coil remains per-player
- Room Sweep / Legendary mechanics remain authoritative
- chests open once
- boss cache opens according to existing rule
- weapon cache choices cannot duplicate rewards

Do NOT add personal-instanced loot unless already specified by design.

Create:

`TestResults/CoopRuntimeComposition/loot_race_matrix.csv`

---

# PHASE 10 — CARRIED COINS / MERCHANT / ECONOMY OWNERSHIP

This is an important ambiguity identified by the review.

Re-verify the intended rule from design docs.

If `CarriedWallet` is deliberately PARTY-SHARED:
- preserve that
- make the UI truthful on every client
- purchases debit shared wallet exactly once
- simultaneous purchases serialize safely
- sell proceeds credit exactly once
- every client sees updated carried coins
- extraction/death semantics remain correct

If the approved docs explicitly require PER-PLAYER carried coins:
- implement the minimum migration/composition needed to honor that rule
- do not redesign prices or economy

If docs are silent:
- preserve current shared `CarriedWallet` semantics
- explicitly document that V1 co-op uses a shared expedition wallet
- do not invent per-player economy during this pass

Do not alter:
- item prices
- ammo prices
- event prices
- sell percentages
- post-D30 reward curve

Create:

`TestResults/CoopRuntimeComposition/economy_ownership_matrix.csv`

Cover simultaneous merchant requests.

---

# PHASE 11 — NON-COMBAT / EVENT / CHOICE AUTHORITY

Audit all current interactive room systems in co-op.

At minimum:

- Merchant
- Medical Station
- Broken Machine
- Locked Vault
- Weapon Cache
- Treasure/Loot interactions
- Transit
- any event-choice UI
- any other current special interactable

For each decide from current design/runtime:

- party-shared state?
- per-player state?
- who can open?
- who can choose?
- what happens if two interact simultaneously?
- what is replicated?
- does interaction lock while a choice is pending?

Do not duplicate rewards.

Do not allow two simultaneous decision screens to commit the same shared event.

Do not redesign event economics.

Create:

`TestResults/CoopRuntimeComposition/noncombat_authority_matrix.csv`

---

# PHASE 12 — DOWNED / REVIVE / WIPE

Wire the existing life-state systems into the ACTUAL co-op run.

Use:

- `PlayerLifeState`
- `PartyLifeRoster`
- `PartyReviveAuthority`
- existing bleedout/revive rules
- existing spectator behavior
- Defibrillator / Medical Station behavior

Requirements:

## Solo
Preserve current solo-death behavior.

## Co-op
A single player reaching 0 HP should enter the intended downed/dead path rather than immediately ending the expedition if living teammates remain.

Verify:

- Alive → Downed
- revive progress
- revive completes once
- revive health percent is correct
- bleedout
- Dead
- spectator follow
- Defibrillator path
- Medical Station path where relevant
- no self-revive unless designed
- no dead player acting through gameplay input
- no duplicate revive
- no revive after wipe
- team wipe ends the run exactly once
- living-player set drives wipe semantics
- disconnect while downed follows existing reconnect policy

Create:

`TestResults/CoopRuntimeComposition/revive_wipe_matrix.csv`

---

# PHASE 13 — TRANSIT VOTING / DESCEND / RETURN

Wire existing transit voting into the real runtime party.

Preserve approved policy.

Re-verify:
- who votes
- living/dead eligibility
- unanimity rule
- dead-return warning
- host role
- timeout if any

Requirements:

- solo behaves as before
- duo requires correct living-party approval
- trio requires correct living-party approval
- one player's vote cannot be counted twice
- disconnected players follow current policy
- dead players cannot incorrectly block/force a result if docs say living-only
- RETURN commits once
- DESCEND builds next depth once
- all connected players transition together
- deepest-depth arrival tracking remains correct
- post-D30 reward curve remains correct
- no duplicate save/loot commit

Create:

`TestResults/CoopRuntimeComposition/transit_vote_matrix.csv`

---

# PHASE 14 — DEPTH TRANSITIONS WITH MULTIPLE PLAYERS

Depth rebuild must preserve party state correctly.

Verify:

- all expected player identities survive the transition
- inventories survive
- equipped weapons survive
- ammo survives
- carried coins survive according to current ownership rule
- temporary depth-local state resets correctly
- health fills according to the already-approved depth-arrival rule
- life state is normalized according to current design
- local camera rebinds to local player
- remote visuals rebind
- room reveal state resets for the new depth
- no duplicate player entity
- no lost player entity
- no duplicate `AudioListener`
- no old-depth network object leaks
- no stale event subscriptions

Create:

`TestResults/CoopRuntimeComposition/depth_transition_matrix.csv`

---

# PHASE 15 — DISCONNECT / RECONNECT

Wire the existing `ReconnectGrace` semantics into the actual run composition.

Do not invent host migration.

Verify:

- client disconnect while Alive
- while Downed
- while Dead
- reconnect inside grace
- reconnect after grace
- identity preservation
- inventory preservation
- equipment preservation
- no duplicate entity
- no item duplication
- no extra vote
- no extra roster entry
- host sees correct status
- client sees correct restored local presentation

If HOST disconnects:
- follow existing approved host-failure semantics
- do not invent host migration
- return/fail gracefully as specified
- do not corrupt saves

Create:

`TestResults/CoopRuntimeComposition/reconnect_matrix.csv`

---

# PHASE 16 — STATIC / PROCESS-GLOBAL SERVICE HAZARD AUDIT

The review identified mutable static service locators as a correctness risk.

Do NOT broadly rewrite architecture.

Audit the current globals that affect expedition/network presentation, including examples such as:

- `ProjectileVisualCatalog.Active`
- `RoomDoorLock.SkinResolver`
- `WorldObjectVisual.Resolver`
- `NetworkPlayerObject.VisualComposer`
- `DamageAuthority.LocalIsAuthoritative`

For each:

- process-global by intention?
- per-run?
- per-player?
- reset/replace lifecycle?
- safe in host process with multiple players?
- safe across returning to menu/new run?
- test order leakage?

Fix only actual multiplayer/runtime lifetime defects.

Prefer explicit set/reset ownership in the composition root over a broad service-locator rewrite.

Create:

`TestResults/CoopRuntimeComposition/global_service_lifetime_matrix.csv`

---

# PHASE 17 — SOLO REGRESSION CONTRACT

This pass MUST NOT make solo worse.

Create:

`TestResults/CoopRuntimeComposition/solo_regression_matrix.csv`

Verify that Solo retains:

- one PlayerRig
- one camera
- one AudioListener
- current pause semantics
- current inventory behavior
- current Merchant
- current Weapon Cache
- current non-combat rooms
- current room reveal
- current enemy scaling
- current D1 ammo tuning
- current Field Knife
- current blaster tuning
- current boss behavior
- current depth/reward rules
- current Help/Codex
- current pickup attraction
- current grenade quick-use
- current Aim Assist
- current status chips
- current save/death/extraction behavior

---

# PHASE 18 — CO-OP UI / PLAYER IDENTITY

Do not create a giant new HUD.

Add only what is needed to understand the party.

Use existing RUINRAIL style.

Each local client should be able to identify:

- self
- teammate(s)
- teammate life state
- teammate health state if current design permits
- downed/dead status
- revive opportunity
- reconnect/disconnected state
- transit vote state

Prefer compact party rows/chips.

Do not clutter the accepted HUD.

Remote players should have a subtle world identifier only if needed for readability.

Do not redesign character art.

Create:

`TestResults/CoopRuntimeComposition/party_ui_matrix.csv`

Validate at 1 / 2 / 3 players and 640×360.

---

# PHASE 19 — MULTIPLAYER TERMINAL / START FLOW

The Multiplayer Terminal already supports Solo / Host / Join.

Wire the REAL expedition start to the real party.

Requirements:

## Host
- host session
- join code appears
- players join
- ready states update
- host cannot start with invalid party state
- start creates one coherent expedition for all participants

## Join
- join code accepted
- joins the same party/session
- receives run/depth state
- receives correct seed
- receives correct identity
- enters same expedition

## Solo
- remains local/no unnecessary network complexity if current architecture supports that

No matchmaking.
No public browser unless already approved.

Create:

`TestResults/CoopRuntimeComposition/start_flow_matrix.csv`

---

# PHASE 20 — UGS / RELAY HONESTY

Live Unity Services may still be unconfigured in the current environment.

This task must separate:

A. repository/runtime composition completeness
from
B. external live-service verification

If UGS project / credentials are unavailable:

- do NOT install/configure cloud credentials automatically
- do NOT fake a live Relay result
- report:
  `Live UGS Sessions/Relay: NOT RUN — project/service configuration unavailable`

But this is NOT permission to stop at test doubles.

The local runtime composition must still be proven with the strongest available method.

Preferred evidence hierarchy:

1. actual built-player host + built-player client(s) over a local/loopback transport if the repository supports it
2. actual NGO host/client in PlayMode with real NetworkObjects/NetworkManager
3. existing deterministic local service doubles only for service-layer pieces that cannot run offline

Do not claim “co-op playable” based only on mocks.

---

# PHASE 21 — REAL MULTI-PEER RUNTIME PROOF

This is mandatory.

Create at least one REAL networked host/client runtime path using the strongest repository-supported offline/local environment.

Minimum scenarios:

## Duo
- Host + Client
- both have distinct player entities
- both can move
- both can shoot
- one player damages enemy
- remote sees movement/weapon state
- shared room activates once
- one shared pickup has exactly one winner
- one player goes Downed
- teammate revives
- Transit vote succeeds
- descend together
- save/return path completes

## Trio
At minimum prove:
- three identities
- three entities
- correct party scaling
- no duplicate camera/listener on each client
- roster count 3
- room activation once
- shared pickup race safe
- vote semantics correct

If multi-process built-player trio is too constrained by the environment, a real NGO PlayMode trio may satisfy the trio proof, but the duo built-player proof should still be attempted if local transport permits it.

Create:

`TestResults/CoopRuntimeComposition/runtime_peer_matrix.csv`

Columns:

- Scenario
- Process/Peer
- ClientId
- OwnsPlayer
- PlayerEntityId
- PartySize
- CameraCount
- ListenerCount
- CanMove
- CanFire
- RemoteVisible
- LootResult
- LifeState
- VoteState
- Depth
- Result

---

# PHASE 22 — AUTHORITY / EXPLOIT TESTS

Because multiplayer exposes existing transactions to races, run targeted exploit tests.

At minimum:

- duplicate pickup request
- simultaneous pickup request
- wrong-owner inventory request
- wrong-owner weapon request
- duplicate merchant buy request
- simultaneous merchant buy
- duplicate event-choice commit
- duplicate revive completion
- duplicate Transit vote
- duplicate Return commit
- duplicate Descend commit
- reconnect duplication
- stale client request after reconnect
- dead player combat request
- disconnected player request
- client attempt to damage enemy directly
- client attempt to authoritatively move another player
- double boss-cache claim if shared
- transaction id collision across clients

Do not weaken existing host-authority restrictions.

Create:

`TestResults/CoopRuntimeComposition/authority_exploit_matrix.csv`

---

# PHASE 23 — PERFORMANCE

Measure actual co-op overhead.

Use representative:

- solo
- duo
- trio

Measure where available:

- CPU frame time
- allocations
- network messages/second
- bytes/second
- player sync frequency
- enemy sync frequency
- projectile/network object count
- live GameObjects
- memory across depth transition
- stale object count after return to menu

Do not prematurely micro-optimize.

Fix only clear regression/pathology.

Create:

`TestResults/CoopRuntimeComposition/coop_performance.csv`

---

# PHASE 24 — VALIDATOR

Add or extend a production validator such as:

`CoopRuntimeCompositionValidator`

It must verify actual runtime-composition contracts, not just type existence.

At minimum guard:

- co-op remains in V1 scope
- PlayerPresenceService has a real runtime composition caller
- NgoPlayerEntityFactory has a real runtime composition caller
- PlayerNetworkEntity prefab is registered
- expedition player count is not hardcoded to 1
- co-op UI/pause paths do not force `Time.timeScale = 0`
- run-level vs local-presentation composition is explicit
- remote player composition cannot create AudioListener
- remote player composition cannot create local input/camera
- PartyLifeRoster receives real party presence
- room state is party-aware
- transit voting is wired to real party
- reconnect service is wired
- party scaling checks actual expected/spawned participants
- no `isCoop: false` hardcoding remains in the real co-op path
- solo remains supported
- max party size remains 3
- PvP remains absent

Do not use source-text name matching as the only proof.
Combine:
- explicit contracts
- composition mappings
- runtime test fixtures

A deliberately broken fixture must fail.

---

# PHASE 25 — FROZEN-SYSTEM CHECK

Create:

`TestResults/CoopRuntimeComposition/frozen_systems_check.csv`

Explicitly verify unchanged:

- all 33 weapon balance values
- Field Knife
- blaster tuning
- D1 ammo tuning
- ammo caps
- D1–D30 difficulty scaling values
- post-D30 difficulty scaling
- post-D30 reward curve
- deepest-depth behavior
- boss seeded selection
- boss anti-kite
- elite frequency
- room depth gating
- biome identity
- world substrate
- audio mix
- music loop correction
- Shelter Trader presentation
- status-effect HUD
- pickup attraction values
- grenade behavior
- aim-assist default/strength
- low-ammo prompt
- Help/Codex
- prop dressing
- graphical inventory
- Dungeon Merchant
- progression
- run/depth HP rules
- save migration rules except additive multiplayer wiring if needed
- EncounterBounds
- death/extraction economics

Co-op party scaling values themselves are also frozen unless a literal wiring bug is found.

---

# PHASE 26 — AUTOMATED TESTS

Run at minimum:

- full EditMode
- full PlayMode
- all production validators
- FinalProductionValidator
- ContentCountValidator
- StatConsumerIntegrityValidator
- RunVarietyDepthRetentionValidator
- PresentationAudioUxQolValidator
- new co-op runtime validator
- persistence/save/migration suites
- multiplayer gate suites
- host authority suites
- reconnect suites
- revive/downed suites
- transit vote suites
- loot authority suites
- inventory/merchant authority suites
- combat/network sync suites
- dungeon generation/sync suites
- solo regression suites

Known pre-existing smoke flakes must be reported honestly.

Do not widen frozen tolerances merely to obtain green.

---

# PHASE 27 — BUILDS / SMOKE

Build the available target.

If Windows x64 module is unavailable:

`Windows x64: NOT RUN — module unavailable`

Do not install it automatically.

macOS non-development build is acceptable for local verification.

Required smoke categories:

## Solo built-player
Must still pass.

## Duo runtime
Use the strongest real local network path available.

## Trio runtime
Use the strongest real local network path available.

## Live UGS
Run only if configured.

If not:
`Live UGS Sessions/Relay: NOT RUN — project/service configuration unavailable`

Do not call that a repository failure if local runtime composition is complete.

---

# PHASE 28 — HUMAN CO-OP PLAYTEST CHECKLIST

Create:

`TestResults/CoopRuntimeComposition/HUMAN_PLAYTEST_CHECKLIST.md`

Keep it diagnostic.

Include approximately 20–30 checks such as:

- Can the host create a party and see a join code?
- Does a second player appear as a real character in the dungeon?
- Does each machine control only its own character?
- Do remote weapon aim and held-weapon visuals look correct?
- Does opening inventory on one player leave the other player/world running?
- Does a room activate only once when players enter at different times?
- Can two players race for one pickup without duplication?
- Does a shared wallet, if that is the chosen rule, remain understandable?
- Can both players use Merchant/Event UI without double-spending?
- Does a downed teammate create a clear revive opportunity?
- Does revive feel readable?
- Does a wipe happen only when the party is actually lost?
- Does spectator behavior work after death?
- Does Transit voting clearly show who has voted?
- Do all players descend together?
- Does depth transition preserve each player's inventory/loadout?
- Does a reconnect restore the right character?
- Does the host-failure path end gracefully?
- Does trio scaling feel like the existing authored trio scaling, not like a bug?
- Is party HUD readable at 640×360?
- Does solo still feel exactly as before?
- Did any accepted UI/art/audio/combat behavior regress?

Do not ask generic “is co-op fun?” questions.

---

# PHASE 29 — REQUIRED REPORT

Create:

`production/COOP_RUNTIME_COMPOSITION_REPORT.md`

Required structure:

# RUINRAIL — CO-OP RUNTIME COMPOSITION REPORT

## 1. Executive Summary
## 2. Previous Gap Reverification
## 3. Multiplayer Design Contract
## 4. Run-Level Composition
## 5. Per-Player Composition
## 6. Local Presentation Separation
## 7. Player Presence / Entity Factory Wiring
## 8. Party-Size Scaling Integrity
## 9. Movement / Combat Authority
## 10. Remote Visuals
## 11. Camera / Input / Pause Semantics
## 12. Room / Encounter Party State
## 13. Loot Authority
## 14. Coin / Merchant Ownership
## 15. Non-Combat / Choice Authority
## 16. Downed / Revive / Wipe
## 17. Transit Voting
## 18. Depth Transitions
## 19. Disconnect / Reconnect
## 20. Global Service Lifetime Audit
## 21. Solo Regression
## 22. Party UI
## 23. Multiplayer Terminal / Start Flow
## 24. Real Multi-Peer Runtime Proof
## 25. Authority / Exploit Tests
## 26. Performance
## 27. Validator / Contract Changes
## 28. Frozen-System Verification
## 29. Automated Tests
## 30. Built-Player / Runtime Smokes
## 31. Live UGS / Relay Status
## 32. Human Playtest Checklist
## 33. Known Pre-Existing Flakes
## 34. Files Changed
## 35. Deferred / External Dependencies
## 36. Final Status

Clearly distinguish:

- implemented and runtime-proven
- test-only proof
- built-player proof
- live-service NOT RUN
- deferred design question
- external dependency
- blocker

---

# REQUIRED ARTIFACTS

Create:

- `production/COOP_RUNTIME_COMPOSITION_REPORT.md`
- `TestResults/CoopRuntimeComposition/current_state_before.csv`
- `TestResults/CoopRuntimeComposition/multiplayer_contract_matrix.csv`
- `TestResults/CoopRuntimeComposition/player_presence_matrix.csv`
- `TestResults/CoopRuntimeComposition/party_scaling_integrity.csv`
- `TestResults/CoopRuntimeComposition/remote_visual_matrix.csv`
- `TestResults/CoopRuntimeComposition/authority_matrix.csv`
- `TestResults/CoopRuntimeComposition/local_presentation_matrix.csv`
- `TestResults/CoopRuntimeComposition/room_party_state_matrix.csv`
- `TestResults/CoopRuntimeComposition/loot_race_matrix.csv`
- `TestResults/CoopRuntimeComposition/economy_ownership_matrix.csv`
- `TestResults/CoopRuntimeComposition/noncombat_authority_matrix.csv`
- `TestResults/CoopRuntimeComposition/revive_wipe_matrix.csv`
- `TestResults/CoopRuntimeComposition/transit_vote_matrix.csv`
- `TestResults/CoopRuntimeComposition/depth_transition_matrix.csv`
- `TestResults/CoopRuntimeComposition/reconnect_matrix.csv`
- `TestResults/CoopRuntimeComposition/global_service_lifetime_matrix.csv`
- `TestResults/CoopRuntimeComposition/solo_regression_matrix.csv`
- `TestResults/CoopRuntimeComposition/party_ui_matrix.csv`
- `TestResults/CoopRuntimeComposition/start_flow_matrix.csv`
- `TestResults/CoopRuntimeComposition/runtime_peer_matrix.csv`
- `TestResults/CoopRuntimeComposition/authority_exploit_matrix.csv`
- `TestResults/CoopRuntimeComposition/coop_performance.csv`
- `TestResults/CoopRuntimeComposition/frozen_systems_check.csv`
- `TestResults/CoopRuntimeComposition/HUMAN_PLAYTEST_CHECKLIST.md`
- network logs / screenshots / captures as useful
- live UGS evidence only if actually available

---

# NON-GOALS

Do NOT in this pass:

- redesign weapon balance
- change Field Knife
- change blaster tuning
- change D1 ammo tuning
- change ammo caps
- change solo enemy/boss scaling
- rebalance authored co-op scaling values
- change post-D30 reward curve
- redesign bosses
- change room gating
- change biome identity
- change world substrate
- remix audio
- redesign Shelter Trader
- redesign inventory
- redesign Main Menu
- redesign character/weapon/VFX art
- add PvP
- add matchmaking
- add dedicated servers
- add host migration unless already approved and implemented
- add cross-platform services
- add voice chat
- add clans/friends systems
- add spectator features beyond current approved dead-player spectator behavior
- add personal-instanced loot unless design docs explicitly require it
- redesign economy ownership unless approved docs already require a different rule
- add start-at-depth
- rewrite save architecture
- perform a general `ExpeditionScene` refactor unrelated to multiplayer composition

---

# FINAL SUCCESS CONDITIONS

This task is COMPLETE only if:

1. The previous co-op findings were reverified against the current tree.
2. Co-op remains explicitly in V1 scope.
3. The actual expedition runtime can compose 1, 2, or 3 player entities.
4. `PlayerPresenceService` is used by real runtime composition.
5. `NgoPlayerEntityFactory` or the current equivalent is used by real runtime composition.
6. Party-size scaling cannot create a trio-scaled dungeon with only one composed player.
7. Run-level systems and local presentation are separated enough that remote players do not create duplicate camera/input/HUD/listener stacks.
8. Solo pause behavior remains correct.
9. Co-op UI does not pause the shared world.
10. Remote movement and held-weapon/aim visuals work.
11. Combat remains host-authoritative.
12. Wrong-owner requests are rejected.
13. Room activation/reveal/clear work for a party without duplication.
14. Shared pickup races resolve exactly once.
15. Coin/merchant ownership follows one explicit documented V1 rule.
16. Non-combat/event interactions are race-safe.
17. Downed/revive/dead/wipe work in the real run.
18. Transit voting works in the real run for duo/trio.
19. Depth transitions preserve all player identities/state.
20. Disconnect/reconnect is wired to actual expedition presence.
21. Host failure follows the existing rule without corruption.
22. Global static/service lifetimes are safe for actual co-op run lifecycle.
23. Party UI is readable at 1–3 players.
24. Multiplayer Terminal starts the real party expedition.
25. At least one real host/client runtime proof exists beyond pure mocks.
26. Trio runtime is proven using the strongest available real network environment.
27. Solo remains behaviorally intact.
28. Frozen gameplay/presentation values remain unchanged.
29. New validator protects the composition contract.
30. Full relevant test suites pass except honestly reported environment-gated/pre-existing cases.
31. Available-platform non-development build succeeds.
32. Live UGS/Relay is reported truthfully as RUN or NOT RUN.
33. Human playtest checklist exists.
34. No unrelated redesign occurred.

If complete, print exactly:

`COOP_RUNTIME_COMPOSITION_COMPLETE`

and:

`production/COOP_RUNTIME_COMPOSITION_REPORT.md`

If a repository-local requirement remains unresolved, print:

`COOP_RUNTIME_COMPOSITION_INCOMPLETE`

and list the exact blockers.

Live UGS/Relay being unavailable because the project/service environment is not configured is NOT by itself a repository-local blocker if the actual runtime composition is otherwise fully implemented and proven with a real local/NGO host-client path.
