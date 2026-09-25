# RUINRAIL — CO-OP RUNTIME COMPOSITION COMPLETION PASS

## ROLE

You are continuing the previously executed RUINRAIL co-op runtime composition pass.

The prior pass correctly ended with:

`COOP_RUNTIME_COMPOSITION_INCOMPLETE`

because host-side party composition and real multi-process player ownership were proven, but a joining client still does NOT compose and play the full expedition.

This continuation has ONE purpose:

> Close the remaining repository-local co-op gap so two and three real peers can play the SAME complete expedition end-to-end.

This is NOT a new multiplayer architecture review.

Do not redo already completed work.
Do not broaden into unrelated refactors.
Do not stop at partial networking proof.
Do not declare COMPLETE because player NetworkObjects move across peers.

The success condition is a real shared expedition.

Work autonomously.
Do not pause for approval.
Do not ask intermediate questions.
Do not stop after analysis.

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

Do not revert the completed first co-op pass.

If you need to undo your own edit, do so manually from content actually inspected during this task.

---

# AUTHORITATIVE CURRENT STATE

The previous co-op pass already implemented and verified:

- `ExpeditionParty`
- one party member per participant
- runtime use of `PlayerPresenceService`
- runtime use of `NgoPlayerEntityFactory`
- actual `PlayerNetworkEntity` ownership
- max party size 3
- underfilled-start rejection
- reconnect-only admission after expedition start
- `PartyLifeRoster` population
- `SessionPartyBridge`
- real socket-based 1/2/3-process peer proof
- one owned real `NetworkObject` per peer
- remote motion replication
- host processing of remote intents
- one `AudioListener` per process
- no remote camera/input/listener duplication
- host-arbitrated co-op pickups
- runtime `DamageAuthority.LocalIsAuthoritative`
- fixed release `NetworkPlayerMotion`
- fixed release movement config
- replicated player life state
- real runtime `NetworkManager`
- protection against client-supplied wrong inventory containers
- reconnect token duplication prevention
- failed-spawn phantom-member prevention

Preserve this work.

The previous pass also reported these gates green:

- PlayMode 823/823
- EditMode 974 passed / 0 failed / 2 environment-gated skips
- co-op validator 21/21
- macOS non-development build
- solo built-player smoke
- real 1/2/3-process socket networking proof

Do not lower these standards.

---

# THE ONE REMAINING BLOCKER

The previous pass identified this exact repository-local blocker:

> A joining client connects, is approved, becomes a party member, owns a real replicated character and can move it, but the client does not compose its own expedition scene from the host's run state.

The reported missing runtime pieces were:

1. host start-state publication through `NetworkDungeonSync`
2. a client branch in the run composition root
3. a `PlayerRig` attach-to-owned-NetworkObject path
4. per-client request routing to host `LootAuthorityService`
5. replicated depth / vote / room-resolution state
6. runtime enemy networking
7. `EnemyNetSync` real runtime composition
8. enemy prefabs registered with NGO

This task must close ALL of those necessary gaps.

---

# HARD COMPLETION RULE

Do not print COMPLETE unless a real built-player Duo session can do all of this:

1. Host creates/starts expedition.
2. Client joins that same expedition.
3. Both peers build the same D1 dungeon from authoritative host state.
4. Each peer controls exactly its own character.
5. Both see each other in the correct room.
6. Both can move and fight.
7. Enemies exist on both peers and are host-authoritative.
8. Room activation happens once.
9. Enemy death resolves once.
10. Doors unlock consistently on both peers.
11. Shared loot resolves once.
12. Host and client can use relevant shared interactions without duplication.
13. One player can be downed.
14. The other player can revive them.
15. Boss encounter exists and resolves consistently.
16. Transit voting works across both peers.
17. The party can RETURN together OR DESCEND together.
18. If DESCEND is chosen, both peers build the same next depth.
19. Player identity, inventory, equipment, ammo and life state survive the transition correctly.
20. If RETURN is chosen, extraction/save resolves once and both peers leave the expedition coherently.

A real built-player Trio proof must also demonstrate the same composition model with three participants, though the full boss-to-return path may be shorter if environment/runtime cost makes duplicating the entire duo script unreasonable.

Pure mocks are insufficient.
Pure motion replication is insufficient.
Pure NetworkObject existence is insufficient.

---

# PHASE 1 — RE-VERIFY THE BLOCKER

Before changing anything, reproduce the current incomplete state.

Create:

`TestResults/CoopRuntimeCompletion/current_blocker_before.csv`

Record:

- host process
- client process
- session accepted
- party member created
- owned NetworkObject exists
- movement replication works
- host expedition built
- client expedition built YES/NO
- dungeon seed known to client YES/NO
- depth known to client YES/NO
- biome known to client YES/NO
- room graph built on client YES/NO
- enemies built on client YES/NO
- encounter state replicated YES/NO
- loot request path available YES/NO
- transit vote path available YES/NO

The current failure should be proven before being fixed.

---

# PHASE 2 — AUTHORITATIVE EXPEDITION START STATE

The host must publish the minimum authoritative state required for every client to compose the same expedition.

Use the existing `NetworkDungeonSync` or current equivalent.

Do not invent a parallel sync service if the repository already has one.

At minimum synchronize:

- run seed
- current depth
- biome identity
- party size / expected participant set
- content/version discriminator if required by current deterministic generation
- dungeon generation-ready state
- run state phase
- any existing deterministic generation inputs not derivable from seed + depth

The client must NOT choose these independently.

The host is authoritative.

A client must not begin local dungeon composition before:

- party membership is valid
- its own player NetworkObject ownership is established
- authoritative run-start payload is available

A host must not unlock gameplay before all expected peers are ready according to the current start contract.

Create:

`TestResults/CoopRuntimeCompletion/start_state_sync_matrix.csv`

---

# PHASE 3 — CLIENT EXPEDITION COMPOSITION BRANCH

Add the missing client branch in the real expedition composition root.

The client must build the same deterministic dungeon from the host state.

Do not duplicate host authority.

Client composition should instantiate/render local representations of:

- dungeon geometry
- room layouts
- doors
- substrate
- hazards
- interactable presentation
- encounter presentation
- networked enemy representations
- boss representations
- minimap state
- local player presentation/UI

The client must NOT independently author:

- loot results
- enemy authority
- encounter completion
- shared event outcomes
- boss death
- room rewards
- transit result
- run return/extraction result

Create:

`TestResults/CoopRuntimeCompletion/client_composition_matrix.csv`

For solo/duo/trio verify:

- same seed
- same depth
- same biome
- same room node ids
- same room positions
- same door topology
- no duplicate authoritative systems on client
- no duplicate shared rewards
- no duplicate camera
- no duplicate AudioListener

---

# PHASE 4 — ATTACH LOCAL PLAYERRIG TO OWNED NETWORK OBJECT

The joining client already owns a real `PlayerNetworkEntity`.

Do NOT spawn a second gameplay player.

Implement a path where the local `PlayerRig` / local presentation attaches to the already-owned NetworkObject.

Requirements:

- one player identity
- one health/life-state representation
- one inventory
- one equipment state
- one owned network entity
- one local camera
- one local input reader
- one local HUD
- one local inventory UI
- one local cursor
- one local aim-assist preference
- one AudioListener per process

Do not mirror local player state into a second hidden character.

Create:

`TestResults/CoopRuntimeCompletion/local_player_attach_matrix.csv`

---

# PHASE 5 — RUNTIME ENEMY NETWORK COMPOSITION

The previous pass reported:

- `EnemyNetSync` has no real runtime caller
- enemy prefabs are not registered with NGO

Fix this.

All required networked enemy representations must be registered correctly.

Cover:

- 9 normal enemy archetypes
- 6 elite variants
- 6 bosses

If the architecture uses one network prefab plus definition id instead of one prefab per enemy definition, preserve that design.

Host owns:

- enemy spawn
- enemy AI
- target selection
- movement authority
- health
- damage dealing
- death
- loot unlock consequence
- boss phase state
- encounter completion

Clients receive replicated state/presentation.

Clients must not run a second authoritative AI simulation.

Clients must see:

- spawn
- transform/motion
- facing/animation state
- attack telegraphs
- attack presentation
- health changes where visible
- death
- elite/boss identity
- boss phase state
- boss bar relationship

Create:

`TestResults/CoopRuntimeCompletion/enemy_network_matrix.csv`

Include every normal archetype, elite and boss at least once through automated composition coverage.

---

# PHASE 6 — ROOM / ENCOUNTER STATE REPLICATION

Room state must be globally consistent.

Host-authoritative room states should include current architecture equivalents of:

- undiscovered
- discovered
- entered
- encounter active
- locked
- cleared
- reward available/consumed
- exits unlocked
- boss active/cleared

Requirements:

- first qualifying party member triggers room activation exactly once
- second/third entrant does not duplicate encounter
- client sees lock/unlock state
- client sees enemy set
- client sees clear state
- client minimap updates
- reconnecting player receives current state, not room-start state

Use existing `DungeonNetSync` / room sync architecture where appropriate.

Create:

`TestResults/CoopRuntimeCompletion/room_state_replication_matrix.csv`

---

# PHASE 7 — CLIENT REQUEST ROUTING TO HOST AUTHORITIES

The joining client must be able to actually play the run.

Wire request paths from client-owned gameplay to host-authoritative services.

At minimum:

## Combat
- fire
- reload
- weapon switch
- consumable use
- grenade use

## Loot
- pickup
- chest/open where applicable
- weapon cache claim
- boss cache claim

## Economy / non-combat
- merchant buy
- merchant sell
- medical/event action
- broken machine choice
- locked vault
- any current shared event choice

## Party
- revive request
- transit vote
- return/descend input where applicable

Requests must:

- identify sender by network ownership
- never trust a client-supplied inventory/container identity when host can resolve from sender
- reject wrong-owner targets
- reject stale requests
- reject dead/disconnected players where appropriate
- deduplicate transaction ids

Use existing authority services.

Do not add client-authoritative shortcuts.

Create:

`TestResults/CoopRuntimeCompletion/client_request_routing_matrix.csv`

---

# PHASE 8 — LOOT / PICKUP END-TO-END

Prove from a real joining client:

- host picks coin
- client picks coin
- both race same coin
- host picks ammo
- client picks ammo
- cap respected per player
- client with full backpack cannot steal/hide pickup
- client picks item into own inventory
- remote inventory remains unaffected
- pickup despawns on both peers
- reconnect does not respawn consumed pickup
- chest reward resolves once
- weapon cache reward resolves once

Do not change loot values.

Create:

`TestResults/CoopRuntimeCompletion/loot_end_to_end_matrix.csv`

---

# PHASE 9 — NON-COMBAT / MERCHANT END-TO-END

Wire and prove current V1 ownership semantics.

Do not redesign economy.

Test with real peers:

- host merchant purchase
- client merchant purchase
- simultaneous purchase attempt
- insufficient funds
- item goes to correct inventory
- shared wallet updates on all peers if V1 uses shared wallet
- sell
- duplicate request
- event choice
- Medical Station
- Broken Machine
- Locked Vault
- Weapon Cache
- any other current non-combat room action

Shared events must commit once.

Create:

`TestResults/CoopRuntimeCompletion/noncombat_end_to_end_matrix.csv`

---

# PHASE 10 — DOWNED / REVIVE / DEAD / WIPE END-TO-END

The existing life-state stack must work between real host/client player entities.

Real duo proof:

1. Host alive, client alive.
2. Client reaches 0 HP.
3. Client becomes Downed according to authored rule.
4. Host sees Downed state.
5. Client cannot perform prohibited gameplay actions.
6. Host begins revive.
7. Progress is authoritative.
8. Revive completes exactly once.
9. Correct health percent restored.
10. Repeat with bleedout to Dead.
11. Spectator behavior works.
12. Repeat until all relevant living players are lost.
13. Party wipe resolves exactly once.

Also cover:

- host downed, client revives host
- duplicate revive request
- disconnect while Downed
- reconnect inside grace
- Defibrillator path if currently available
- Medical Station interaction if applicable

Create:

`TestResults/CoopRuntimeCompletion/revive_end_to_end_matrix.csv`

---

# PHASE 11 — TRANSIT VOTING END-TO-END

Wire real peers to the existing vote authority.

Duo real proof:

- both reach Transit
- Host chooses DESCEND, Client has not voted → no transition
- Client votes DESCEND → transition occurs once
- conflicting vote behavior follows current design
- RETURN follows current rule
- dead/living eligibility follows current design
- reconnect/disconnect follows current vote policy

Trio proof:

- vote state visible on all peers
- no duplicate vote
- required approval threshold correct
- transition occurs once

Create:

`TestResults/CoopRuntimeCompletion/transit_end_to_end_matrix.csv`

---

# PHASE 12 — NETWORKED DEPTH TRANSITION

This is mandatory.

When DESCEND wins:

Host publishes next-depth state.

All connected clients:

- leave old room state
- release old depth-local representations
- build same new depth
- keep player identity
- keep inventory
- keep equipment
- keep ammo
- keep carried coins under current rule
- apply approved depth-arrival health rule
- rebind local camera/HUD
- preserve remote player mapping
- receive new enemy/room state
- do not duplicate AudioListener
- do not leak old depth network objects
- update deepest-depth only on successful arrival

Create:

`TestResults/CoopRuntimeCompletion/networked_depth_transition_matrix.csv`

Duo must prove at least D1 → D2.

---

# PHASE 13 — RETURN / EXTRACTION END-TO-END

Real duo proof must include a coherent RETURN path.

Verify:

- vote resolves according to current policy
- host commits extraction once
- carried risk state secures exactly once
- clients cannot duplicate commit
- all peers leave expedition
- local UI returns to correct scene/state
- save occurs at the correct authority/safe point
- personal-best depth remains correct
- no duplicate coin/item/XP grant
- session/run objects are cleaned up
- next Solo run still works

Create:

`TestResults/CoopRuntimeCompletion/return_extraction_matrix.csv`

---

# PHASE 14 — BOSS END-TO-END

The full duo proof must include an actual boss.

Verify:

- boss spawned authoritatively
- client sees boss
- boss movement replicates
- boss attack selection remains the accepted seeded system
- telegraphs visible on client
- boss damages players through host authority
- both peers can damage boss through allowed request paths
- boss HP consistent
- phase transitions consistent
- boss death once
- boss reward/cache once
- Transit available to both peers

Do NOT rebalance boss values.

Create:

`TestResults/CoopRuntimeCompletion/boss_end_to_end_matrix.csv`

---

# PHASE 15 — RECONNECT INTO A LIVE EXPEDITION

A reconnecting client must reconstruct CURRENT state.

Do not restart their local dungeon from room 1.

Verify:

- same run seed
- same depth
- same biome
- current room clear states
- consumed loot remains consumed
- current boss state where relevant
- current player inventory/equipment
- current life state
- party membership reused
- no duplicate player
- no duplicate votes
- no duplicate event rewards
- local camera/HUD attach to restored owned entity

Create:

`TestResults/CoopRuntimeCompletion/live_reconnect_matrix.csv`

---

# PHASE 16 — NETWORK PREFAB / REGISTRATION AUDIT

Create:

`TestResults/CoopRuntimeCompletion/network_prefab_matrix.csv`

For every runtime NetworkObject type record:

- prefab/type
- registered YES/NO
- spawned by host YES/NO
- spawned on client as replica YES/NO
- ownership model
- required sync components
- duplicate local-only components absent YES/NO

Must cover at minimum:

- player network entity
- normal enemy representation
- elite representation
- boss representation
- any networked projectile if architecture uses them
- any networked dungeon/state object required by runtime

Do not rely on editor-only registration absent from release builds.

---

# PHASE 17 — CLIENT-SIDE PRESENTATION IN A REAL RUN

Verify:

- one camera
- camera follows owned player
- one AudioListener
- local HUD binds owned player
- party UI shows all members
- remote health/life state is legible
- inventory is local player's inventory
- Merchant/Event screens send requests, not local commits
- pause/inventory/help/settings do NOT pause shared world
- local tutorial prompts do not duplicate across peers
- Aim Assist remains local preference
- graphical inventory unchanged
- status chips show only local player's timed effects
- minimap reflects shared dungeon state
- boss bar reflects shared boss
- death spectator behavior follows current design

Create:

`TestResults/CoopRuntimeCompletion/client_presentation_matrix.csv`

---

# PHASE 18 — REAL BUILT-PLAYER DUO PROOF

This is the primary completion gate.

Use TWO separate non-development built-player processes over the strongest real local socket/transport path already proven in the previous pass.

Do not substitute a mock.

Execute a scripted or semi-automated end-to-end run:

1. process A hosts
2. process B joins
3. both ready
4. expedition starts
5. both build identical D1
6. both player entities visible
7. host movement visible to client
8. client movement visible to host
9. both fire
10. shared combat room activates once
11. enemies synchronized
12. one pickup race resolves once
13. one non-combat/shared interaction resolves once
14. one player is downed
15. teammate revives
16. boss is reached
17. boss is killed
18. both reach Transit
19. DESCEND vote succeeds
20. both build identical D2
21. state survives transition
22. later RETURN vote succeeds
23. extraction/save resolves once
24. both return coherently
25. no uncaught exception
26. no duplicate grant
27. no orphan network objects

Create:

`TestResults/CoopRuntimeCompletion/duo_built_player_proof.csv`

Also preserve:

- host log
- client log
- network/session log
- screenshots/captures at key stages where practical

If this scenario does not complete, the pass is INCOMPLETE.

---

# PHASE 19 — REAL TRIO PROOF

Use THREE peers with the strongest real transport available.

At minimum prove:

- three connected identities
- three owned player entities
- same dungeon
- correct authored trio scaling
- room activation once
- enemies replicated
- all three can move/fire
- no duplicate camera/listener/input on any peer
- pickup race among three resolves once
- Downed/Revive works
- vote requires the correct trio policy
- one networked depth transition succeeds
- no player lost/duplicated

Create:

`TestResults/CoopRuntimeCompletion/trio_runtime_proof.csv`

A full second boss clear is not required if the duo built-player proof already demonstrates the complete loop and trio proves composition/authority/transition integrity.

---

# PHASE 20 — SOLO REGRESSION

Solo must remain behaviorally unchanged.

Verify:

- no unnecessary network wait
- one player
- one camera
- one listener
- normal pause behavior
- normal inventory
- normal Merchant/Event interaction
- normal enemies
- normal loot
- normal boss
- normal Transit
- normal Descend/Return
- normal save
- recent Presentation/Audio/QoL remains intact

Create:

`TestResults/CoopRuntimeCompletion/solo_regression_matrix.csv`

---

# PHASE 21 — AUTHORITY / EXPLOIT RETEST

Re-run and extend authority tests now that clients have full expedition request paths.

At minimum:

- wrong-owner movement
- wrong-owner fire
- direct client enemy damage
- duplicate pickup
- wrong inventory/container
- duplicate merchant transaction
- duplicate event commit
- duplicate boss cache
- duplicate revive
- duplicate vote
- duplicate descend
- duplicate return
- stale request from old client connection
- reconnect token replay
- disconnected client request
- dead client combat request
- client-authored enemy spawn
- client-authored room clear
- client-authored boss death
- client-authored depth transition

Create:

`TestResults/CoopRuntimeCompletion/authority_exploit_matrix.csv`

---

# PHASE 22 — PERFORMANCE / LEAK CHECK

Measure Solo / Duo / Trio through one depth transition.

Record:

- GameObject count
- NetworkObject count
- live player entities
- live enemy entities
- allocations
- memory
- messages/sec
- bytes/sec
- stale subscriptions
- stale old-depth objects
- listener count
- camera count

Create:

`TestResults/CoopRuntimeCompletion/coop_runtime_performance.csv`

Fix only actual pathologies.

---

# PHASE 23 — VALIDATOR UPGRADE

Extend the existing co-op validator.

It must now fail if any of these regress:

- client expedition composition path absent
- `NetworkDungeonSync`/equivalent has no runtime caller
- client does not consume authoritative run seed/depth
- local PlayerRig cannot attach to owned NetworkObject
- `EnemyNetSync`/equivalent has no runtime caller
- required network enemy prefabs/types unregistered
- clients can authoritatively resolve shared loot
- clients can authoritatively resolve room clear
- client-side co-op UI pauses global time
- depth transition only occurs on host
- reconnect does not restore current dungeon state
- host/client room state diverges in fixture
- duo/trio party composition no longer matches participant set
- remote player composition adds camera/listener/input
- real co-op runtime proof fixture absent

Do not weaken the previous rules.

Additive strengthening only.

A deliberately broken fixture must fail.

---

# PHASE 24 — FROZEN SYSTEMS

Create:

`TestResults/CoopRuntimeCompletion/frozen_systems_check.csv`

Verify unchanged:

- all weapon balance
- Field Knife
- blaster tuning
- D1 ammo tuning
- ammo caps
- D1–D30 difficulty scaling
- post-D30 difficulty scaling
- authored co-op scaling values
- post-D30 reward curve
- deepest-depth rules
- boss selection
- boss anti-kite
- elite frequency
- room depth gating
- biome identity
- world substrate
- audio mix
- music loop fix
- Shelter Trader
- status chips
- pickup attraction values
- grenade behavior
- aim-assist angles/default
- low-ammo prompt
- Codex
- prop dressing
- graphical inventory
- Dungeon Merchant
- progression
- run/depth health rules
- save transaction semantics
- EncounterBounds
- death/extraction economy

Any drift is a failure.

---

# PHASE 25 — TESTS / BUILD / SMOKE

Run:

- full PlayMode
- full EditMode
- all production validators
- FinalProductionValidator
- ContentCountValidator
- StatConsumerIntegrityValidator
- RunVarietyDepthRetentionValidator
- PresentationAudioUxQolValidator
- upgraded co-op validator
- all multiplayer gate tests
- authority tests
- loot authority tests
- revive/downed tests
- reconnect tests
- transit vote tests
- dungeon sync tests
- enemy network sync tests
- save/migration tests
- solo regression tests

Build:

- macOS non-development build on current machine if available
- Windows x64 only if module is installed

If Windows module unavailable:

`Windows x64: NOT RUN — module unavailable`

Do not install automatically.

Run:

- Solo built-player smoke
- Duo real built-player proof
- Trio real runtime proof

Known pre-existing smoke flakes must be reported honestly.

Do NOT widen frozen tolerances merely to achieve green.

---

# PHASE 26 — LIVE UGS / RELAY

If current environment is not configured:

`Live UGS Sessions/Relay: NOT RUN — project/service configuration unavailable`

Do not fake it.
Do not install credentials automatically.
Do not block repository completion solely because live cloud configuration is unavailable.

Local real multiplayer completion is mandatory and cannot be replaced by mocks.

---

# PHASE 27 — HUMAN PLAYTEST CHECKLIST

Create:

`TestResults/CoopRuntimeCompletion/HUMAN_PLAYTEST_CHECKLIST.md`

Include approximately 20–30 diagnostic checks:

- Does the joining player enter the same dungeon, not an empty/partial scene?
- Do both players see identical room topology?
- Does each player control only themselves?
- Are remote movement and aim visually stable?
- Do enemies appear and behave consistently on both machines?
- Does room lock/clear happen at the same time?
- Does loot disappear once for both peers?
- Does the correct player receive the item?
- Do merchants/events avoid duplicate outcomes?
- Does one player's inventory remain their own?
- Does a downed player look clearly downed on the teammate's screen?
- Does revive work from both host→client and client→host?
- Does party wipe happen only when appropriate?
- Does the boss look synchronized?
- Are telegraphs readable on the client?
- Does Transit clearly show vote state?
- Does D1→D2 happen on both machines together?
- Do inventory/ammo/equipment survive the transition?
- Does reconnect restore the current room state?
- Does RETURN end the run cleanly for both players?
- Is opening Inventory/Pause on one player non-pausing for the other?
- Does Solo still behave exactly as before?
- Does anything feel duplicated, delayed or desynchronized?
- Does the trio HUD remain readable at 640×360?

Do not ask generic “is co-op fun?” questions.

---

# PHASE 28 — REQUIRED REPORT

Create:

`production/COOP_RUNTIME_COMPOSITION_COMPLETION_REPORT.md`

Required structure:

# RUINRAIL — CO-OP RUNTIME COMPOSITION COMPLETION REPORT

## 1. Executive Summary
## 2. Previous Incomplete State Reproduction
## 3. Authoritative Start-State Sync
## 4. Client Expedition Composition
## 5. Local PlayerRig Attachment
## 6. Enemy Network Runtime Composition
## 7. Network Prefab Registration
## 8. Room / Encounter State Replication
## 9. Client Request Routing
## 10. Loot End-to-End
## 11. Non-Combat / Merchant End-to-End
## 12. Downed / Revive / Wipe End-to-End
## 13. Boss End-to-End
## 14. Transit Voting End-to-End
## 15. Networked Depth Transition
## 16. Return / Extraction
## 17. Live Reconnect
## 18. Client Presentation
## 19. Duo Built-Player Proof
## 20. Trio Runtime Proof
## 21. Solo Regression
## 22. Authority / Exploit Tests
## 23. Performance / Leak Check
## 24. Validator Changes
## 25. Frozen-System Verification
## 26. Automated Tests
## 27. Builds / Smokes
## 28. Live UGS / Relay Status
## 29. Human Playtest Checklist
## 30. Known Pre-Existing Flakes
## 31. Files Changed
## 32. Deferred External Dependencies
## 33. Final Status

Clearly distinguish:

- built-player proof
- PlayMode proof
- mock-only proof
- live-service NOT RUN
- deferred external dependency
- repository-local blocker

---

# REQUIRED ARTIFACTS

Create:

- `production/COOP_RUNTIME_COMPOSITION_COMPLETION_REPORT.md`
- `TestResults/CoopRuntimeCompletion/current_blocker_before.csv`
- `TestResults/CoopRuntimeCompletion/start_state_sync_matrix.csv`
- `TestResults/CoopRuntimeCompletion/client_composition_matrix.csv`
- `TestResults/CoopRuntimeCompletion/local_player_attach_matrix.csv`
- `TestResults/CoopRuntimeCompletion/enemy_network_matrix.csv`
- `TestResults/CoopRuntimeCompletion/room_state_replication_matrix.csv`
- `TestResults/CoopRuntimeCompletion/client_request_routing_matrix.csv`
- `TestResults/CoopRuntimeCompletion/loot_end_to_end_matrix.csv`
- `TestResults/CoopRuntimeCompletion/noncombat_end_to_end_matrix.csv`
- `TestResults/CoopRuntimeCompletion/revive_end_to_end_matrix.csv`
- `TestResults/CoopRuntimeCompletion/transit_end_to_end_matrix.csv`
- `TestResults/CoopRuntimeCompletion/networked_depth_transition_matrix.csv`
- `TestResults/CoopRuntimeCompletion/return_extraction_matrix.csv`
- `TestResults/CoopRuntimeCompletion/boss_end_to_end_matrix.csv`
- `TestResults/CoopRuntimeCompletion/live_reconnect_matrix.csv`
- `TestResults/CoopRuntimeCompletion/network_prefab_matrix.csv`
- `TestResults/CoopRuntimeCompletion/client_presentation_matrix.csv`
- `TestResults/CoopRuntimeCompletion/duo_built_player_proof.csv`
- `TestResults/CoopRuntimeCompletion/trio_runtime_proof.csv`
- `TestResults/CoopRuntimeCompletion/solo_regression_matrix.csv`
- `TestResults/CoopRuntimeCompletion/authority_exploit_matrix.csv`
- `TestResults/CoopRuntimeCompletion/coop_runtime_performance.csv`
- `TestResults/CoopRuntimeCompletion/frozen_systems_check.csv`
- `TestResults/CoopRuntimeCompletion/HUMAN_PLAYTEST_CHECKLIST.md`
- host/client/trio logs
- network logs
- screenshots/captures as useful

---

# NON-GOALS

Do NOT in this pass:

- redesign networking architecture from scratch
- replace NGO
- add PvP
- add matchmaking
- add dedicated servers
- add host migration
- add voice chat
- add cross-platform account systems
- add friends/clans
- redesign economy
- redesign loot
- rebalance weapons
- rebalance bosses
- change co-op scaling values
- change D1 ammo
- change Field Knife
- change blaster tuning
- change room gating
- change biome identity
- change post-D30 rewards
- redesign UI
- redesign art
- remix audio
- rewrite save architecture
- refactor unrelated `ExpeditionScene` code
- use this pass for general cleanup
- start the final release audit before co-op is truly complete

---

# FINAL SUCCESS CONDITIONS

This task is COMPLETE only if:

1. The previously reported incomplete state is reproduced and documented.
2. Host publishes authoritative run-start state.
3. Joining clients compose the same expedition.
4. Joining clients build the same deterministic dungeon.
5. Local PlayerRig attaches to the owned NetworkObject without duplicate player creation.
6. `NetworkDungeonSync` or equivalent has a real runtime caller.
7. `EnemyNetSync` or equivalent has a real runtime caller.
8. All required enemy/elite/boss network representations are registered.
9. Host owns enemy AI/health/death authority.
10. Clients see enemies and bosses consistently.
11. Room activation/lock/clear are shared and non-duplicating.
12. Client combat requests route through host authority.
13. Client loot requests route through host authority.
14. Client Merchant/Event requests route through host authority.
15. Shared pickup races resolve exactly once.
16. Downed/revive/dead/wipe works across real peers.
17. Boss fight works across real peers.
18. Transit voting works across real peers.
19. D1 → D2 networked descend succeeds on all peers.
20. Player identity/inventory/equipment/ammo persist through the transition.
21. RETURN/extraction resolves once and all peers exit coherently.
22. Reconnect restores the current expedition state.
23. Client presentation is complete and local-only.
24. Co-op menus do not pause the shared world.
25. Duo built-player end-to-end proof passes.
26. Trio real runtime proof passes.
27. Solo regression passes.
28. Authority/exploit tests pass.
29. No frozen balance/presentation values drift.
30. Full relevant PlayMode/EditMode/validator gates pass except honestly reported environment-gated cases.
31. Available-platform non-development build succeeds.
32. Live UGS/Relay status is reported truthfully.
33. No repository-local co-op blocker remains.

If ALL repository-local conditions above are satisfied, print exactly:

`COOP_RUNTIME_COMPOSITION_COMPLETE`

and:

`production/COOP_RUNTIME_COMPOSITION_COMPLETION_REPORT.md`

If ANY repository-local co-op condition remains unresolved, print exactly:

`COOP_RUNTIME_COMPOSITION_INCOMPLETE`

and list the blocker(s).

Do not print COMPLETE if two real peers still cannot play the same full expedition end-to-end.
