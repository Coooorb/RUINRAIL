using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Core;
using RuinRail.Dungeon.Generation;
using RuinRail.Dungeon.Rooms;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Base;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Enemies.Attacks;
using RuinRail.Gameplay.Events;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Items.Passives;
using RuinRail.Gameplay.Loot;
using RuinRail.Gameplay.Player;
using RuinRail.Gameplay.Stats;
using RuinRail.Networking;
using RuinRail.Presentation;
using RuinRail.Presentation.Animation;
using RuinRail.Presentation.Vfx;
using RuinRail.UI.Base;
using UnityEngine;

namespace RuinRail.App
{
    /// <summary>How this process takes part in the expedition (82): alone, as the party's host, or as a joined client.</summary>
    public enum CoopRunMode
    {
        Solo,
        Host,
        Client
    }

    /// <summary>
    /// The co-op half of the run's composition root. Solo never reaches any of this. A host composes every member as a
    /// real network player object, publishes its depth and world (<see cref="CoopHostWorld"/>) and validates every
    /// client request; a client rebuilds the host's depth from the published payload, attaches its own run player to
    /// the character the host spawned for it, and mirrors the host's world (<see cref="CoopClientWorld"/>), turning its
    /// own actions into requests. Every decision stays in the existing services — this only routes them.
    /// </summary>
    public sealed partial class ExpeditionScene
    {
        public const float ClientComposeTimeoutSeconds = 45f;

        private CoopHostWorld _coopHost;
        private CoopClientWorld _coopClient;
        private RunStartMessage _run;
        private bool _awaitingHostDepth;
        private bool _clientGameplayHeld;
        private float _clientGameplayHeldAt;
        private LootSpawner _clientLoot;
        private readonly Dictionary<ulong, CoopMemberMirror> _mirrors = new();
        /// <summary>Co-op member: drop requests still waiting for the host (transaction id → item instance id).</summary>
        private readonly Dictionary<string, string> _pendingDrops = new();
        private readonly HostDecidedTransitPolicy _hostDecided = new();
        private NetworkPlayerObject _ownedNet;
        private int _mirroredCoins;
        private string _clientBossName;

        /// <summary>Solo, co-op host or co-op client — decided once when the run composes.</summary>
        public CoopRunMode Mode { get; private set; } = CoopRunMode.Solo;

        public CoopHostWorld CoopHost => _coopHost;
        public CoopClientWorld CoopClient => _coopClient;
        public RunStartMessage CoopRun => _run;
        public IReadOnlyDictionary<ulong, CoopMemberMirror> MemberMirrors => _mirrors;

        /// <summary>Why the last client depth rebuild was refused (seed/biome/pool/layout mismatch); null when it matched.</summary>
        public string LastDepthDesync { get; private set; }

        /// <summary>The layout fingerprint of the depth this peer built (compared across peers by the proof).</summary>
        public string LayoutFingerprint { get; private set; } = string.Empty;

        /// <summary>True while this client waits for the host's release (every peer built the depth).</summary>
        public bool CoopGameplayHeld => _clientGameplayHeld || (_coopHost != null && _coopHost.GameplayHeld);

        private CoopSessionService Coop => _app != null ? _app.Coop : null;

        private static CoopRunMode ResolveRunMode(GameApp app)
        {
            var coop = app.Coop;
            if (coop == null || !coop.IsLive || coop.CurrentRun == null) return CoopRunMode.Solo;
            return coop.IsHost ? CoopRunMode.Host : CoopRunMode.Client;
        }

        private IAuthorityContext CoopAuthority() => Mode == CoopRunMode.Client ? new CoopClientAuthority() : CoopHostAuthority.Instance;

        // ---------------------------------------------------------------- client: wait for the host

        private IEnumerator BuildWhenClientReady(GameApp app)
        {
            var coop = app.Coop;
            _run = coop.CurrentRun;
            var deadline = Time.realtimeSinceStartup + ClientComposeTimeoutSeconds;
            GameObject owned = null;
            while (Time.realtimeSinceStartup < deadline)
            {
                owned = CoopPlayerDirectory.Owned();
                var everyone = _run != null && _run.Members.All(m => CoopPlayerDirectory.Of(m.ClientId) != null);
                if (owned != null && everyone && coop.Link != null) break;
                yield return null;
            }

            if (owned == null || coop.Link == null)
            {
                Debug.LogError($"COOP-CLIENT gave up composing: owned={(owned != null)} link={(coop.Link != null)} after {ClientComposeTimeoutSeconds} s.");
                if (_expedition.IsExpeditionActive) _expedition.Fail();
                else _app.LoadScene(SceneNames.Base);
                yield break;
            }

            Debug.Log($"COOP-CLIENT composing on owned player {owned.name} (party {_run?.Members.Count}).");
            BuildCore(app, owned);
        }

        // ---------------------------------------------------------------- party composition

        private string ParticipantOfClient(ulong clientId) => _run?.MemberFor(clientId)?.ParticipantId ?? clientId.ToString();

        /// <summary>
        /// Host: one network player object per member through the shipping presence service (82). The host's own run
        /// player is composed onto its object before the party registers it with the loot authority, so the member
        /// the arbiter knows is the one carrying the at-risk inventory; every other member's host copy carries a
        /// mirror of that member's loadout and ranks (capacity, stats, max HP) — never a second set of weapons.
        /// </summary>
        private ExpeditionParty ComposeHostParty(GameApp app, BaseSession session)
        {
            var coop = app.Coop;
            _run = coop.CurrentRun;
            var content = app.Content;
            var driver = app.Network?.Driver as NgoNetworkDriver;
            if (_run == null || driver == null) return null;
            var state = _expedition.State;
            var ngo = driver.CreatePlayerFactory(content.NetworkPlayerEntity, identity => (Vector3)RemoteSpawnOffset(identity.ClientId), identity => ParticipantOfClient(identity.ClientId), _roster);
            if (ngo == null) return null;
            var factory = new ComposingPlayerFactory(ngo, (go, identity, isLocal) =>
            {
                if (isLocal)
                {
                    _rig.Attach(go, state, _roster, state.TransactionId, session.Profile.Skills);
                    return;
                }

                coop.MemberProfiles.TryGetValue(identity.ClientId, out var profile);
                // The member's Carried wallet on the host starts with the Banked Coins its lobby entry took into the run.
                _mirrors[identity.ClientId] = new CoopMemberMirror(go, identity.ClientId, content, app.Registry, profile, () => _services.GroundLoot.Tracked,
                    _run.MemberFor(identity.ClientId)?.CarriedCoins ?? 0);
            });
            var grace = new ReconnectGraceService(ReconnectGraceService.DefaultGraceSeconds, () => _expedition != null && _expedition.IsExpeditionActive);
            var request = new PartyCompositionRequest
            {
                Members = _run.Members.Select(m => new PartyMemberDescriptor(m.ClientId, m.DisplayName, m.ParticipantId, m.IsHost)).ToList(),
                LocalClientId = coop.LocalClientId,
                LocalDisplayName = session.Profile.DisplayName ?? "Player 1",
                LocalParticipantId = state.TransactionId,
                LocalEntity = null,
                IsHost = true,
                Balance = content.PlayerBalance,
                Caps = content.StatCaps,
                LifeRoster = _roster,
                Loot = _lootAuthority,
                RemoteFactory = factory,
                LiveConnection = driver.Connections,
                Items = app.Registry,
                Ammo = content.AmmoBalance,
                Grace = grace,
                DisplayNamePolicy = content.DisplayNamePolicy,
                Decorate = (go, isLocal) => PlayerVisualComposer.Compose(go, content)
            };

            var party = ExpeditionParty.Compose(request, out var error);
            if (party == null)
            {
                Debug.LogError($"COOP-HOST party composition failed ({error}) for {_run.Members.Count} member(s).");
                return null;
            }

            Debug.Log($"COOP-HOST composed party of {party.ComposedPartySize} network player object(s).");
            return party;
        }

        /// <summary>
        /// Client: the party is the replicas the host spawned (82). The local member is this peer's own object, already
        /// carrying the run player; the others are adopted as they are, input-isolated, with no camera or listener.
        /// </summary>
        private ExpeditionParty ComposeClientParty(GameApp app, BaseSession session, GameObject localPlayer)
        {
            var coop = app.Coop;
            var content = app.Content;
            _run = coop.CurrentRun;
            if (_run == null) return null;
            var request = new PartyCompositionRequest
            {
                Members = _run.Members.Select(m => new PartyMemberDescriptor(m.ClientId, m.DisplayName, m.ParticipantId, m.IsHost)).ToList(),
                LocalClientId = coop.LocalClientId,
                LocalDisplayName = session.Profile.DisplayName ?? "Player",
                LocalParticipantId = _expedition.State.TransactionId,
                LocalEntity = localPlayer,
                // The local presence view adopts objects the host already spawned; nothing is spawned on the network.
                IsHost = true,
                Balance = content.PlayerBalance,
                Caps = content.StatCaps,
                LifeRoster = _roster,
                Loot = null,
                RemoteFactory = new ReplicatedPlayerFactory(CoopPlayerDirectory.Of, _roster),
                LiveConnection = null,
                Items = app.Registry,
                Ammo = content.AmmoBalance,
                Grace = null,
                DisplayNamePolicy = content.DisplayNamePolicy
            };

            var party = ExpeditionParty.Compose(request, out var error);
            if (party == null) Debug.LogError($"COOP-CLIENT party view failed ({error}).");
            return party;
        }

        // ---------------------------------------------------------------- runtime composition

        private void ComposeCoopRuntime(GameApp app, GameObject player)
        {
            if (Mode == CoopRunMode.Solo) return;
            var coop = app.Coop;
            if (Mode == CoopRunMode.Host)
            {
                var party = new CoopHostParty
                {
                    EntityOf = clientId => _party?.Presence.Entities.TryGetValue(clientId, out var e) == true ? e.GameObject : null,
                    ParticipantOf = ParticipantOfClient,
                    MaxHitOf = clientId => clientId == coop.LocalClientId ? CoopHostParty.DefaultMaxHit : _mirrors.TryGetValue(clientId, out var m) ? m.MaxHit() : CoopHostParty.DefaultMaxHit
                };
                _coopHost = new CoopHostWorld(coop.Bus, party, _lootAuthority, _expedition, app.Configs.Resolve);
                _coopHost.InventoryMirrorReceived += (clientId, message) => { if (_mirrors.TryGetValue(clientId, out var mirror)) mirror.Apply(message); };
                _coopHost.DropRequested += OnClientDropRequested;
                _coopHost.ReviveRequested += OnClientReviveRequested;
                _coopHost.RemoteShot += DrawRemoteShot;
                _coopHost.ProofReportReceived += (clientId, report) => CoopProofReport?.Invoke(clientId, report);
                _party.Presence.Reconnected += OnHostMemberReconnected;
                if (_party.Presence.Grace != null)
                {
                    // 85: a dropped member's character stays represented and at risk — standing still, not walking on
                    // its last intent — and becomes Dead if the grace runs out.
                    _party.Presence.Grace.Held += OnHostMemberHeld;
                    _party.Presence.Grace.GraceExpired += OnHostMemberGraceExpired;
                }

                foreach (var member in _party.RemoteMembers) coop.SendReconnectToken(member.OwnerClientId, member.ReconnectToken, ParticipantOfClient(member.OwnerClientId));
                return;
            }

            // ---- client ----
            _ownedNet = player.GetComponent<NetworkPlayerObject>();
            _coopClient = new CoopClientWorld(coop.Bus, _dungeonRootParent());
            _coopClient.InstallRelays(player);
            _coopClient.BindInventoryMirror(() => _expedition.State.Inventory.ToSnapshot(), () => CoopMemberProfile.RanksOf(app.Menu.Session?.Profile.Skills),
                () => _expedition.State.CoinsBroughtIn);
            _expedition.State.Inventory.EquippedChanged += OnClientInventoryChanged;
            _expedition.State.Inventory.BackpackChanged += OnClientInventoryChanged2;
            _coopClient.ActorSpawned += BindReplicaPresentation;
            _coopClient.Xp += OnClientXp;
            _coopClient.TransitOpened += OnClientTransitOpened;
            _coopClient.TransitVotes += OnClientTransitVotes;
            _coopClient.TransitResolved += OnClientTransitResolved;
            _coopClient.Granted += OnClientGranted;
            _coopClient.Revoked += OnClientRevoked;
            _coopClient.TradeResult += OnClientTradeResult;
            _coopClient.CacheResult += OnClientCacheResult;
            _coopClient.ReviveResult += OnClientReviveResult;
            _coopClient.KillConfirmed += OnClientKillConfirmed;
            _coopClient.Notice += n =>
            {
                if (n == null) return;
                if (!string.IsNullOrEmpty(n.TransactionId)) _pendingDrops.Remove(n.TransactionId);
                Notify(n.Text, n.IsProblem);
            };
            _coopClient.GameplayReleased += _ => ReleaseClientGameplay();
            _coopClient.RunEnded += OnClientRunEnded;
            _coopClient.BossState += OnClientBossState;
            _coopClient.ProofCommand += command => CoopProofCommand?.Invoke(command);
            coop.Bus.DepthPayloadChanged += OnHostDepthPayload;
            coop.LinkLost += OnClientLinkLost;
            // Only purely local presentation runs on this peer when Interact is pressed (opening a trade or choice
            // screen); chests, events, pickups, transit and revives are resolved by the host from the same press.
            PlayerInteractor.LocalInteractionFilter = (target, interactor) => target is DungeonMerchantInteractable
                || (target is DungeonEventInteractable ev && ev.Event is WeaponCacheEvent);
            if (_ownedNet != null)
            {
                _ownedNet.CarriedCoinsChanged += OnOwnedCoinsChanged;
                ApplyMirroredCoins(_ownedNet.CarriedCoins);
            }
        }

        private Transform _dungeonRootParent()
        {
            var root = new GameObject("CoopReplicas");
            root.transform.SetParent(transform, false);
            return root.transform;
        }

        /// <summary>The member's player entity on this peer (host: authoritative copy; client: replica or own).</summary>
        public GameObject MemberEntity(ulong clientId) => _party?.Presence.Entities.TryGetValue(clientId, out var e) == true ? e.GameObject : null;

        /// <summary>The run's ground-loot registry (host: authoritative pickups; client: presentation of the host's).</summary>
        public GroundLootRegistry GroundLoot => _services?.GroundLoot;

        /// <summary>A loot spawner bound to the run's ground registry (proof/diagnostics place real pickups through it).</summary>
        public LootSpawner CreateLootSpawnerFor(GameObject host) => _services?.CreateLootSpawner(host);

        /// <summary>Host: the member mirror of a joined client (its reported inventory), or null.</summary>
        public PlayerInventory MirrorInventoryOf(ulong clientId) => _mirrors.TryGetValue(clientId, out var mirror) ? mirror.Inventory : null;

        /// <summary>Proof channel hooks (built-player proof only; nothing in the game listens otherwise).</summary>
        public event Action<ulong, ProofMessage> CoopProofReport;
        public event Action<ProofMessage> CoopProofCommand;

        // ---------------------------------------------------------------- depth

        private bool TryGetHostDepthPayload(ExpeditionState state, out DungeonSyncPayload payload)
        {
            payload = Coop?.Bus != null ? Coop.Bus.DepthPayload : default;
            return payload.IsValid && payload.Depth == state.Depth && payload.RunSeed == state.RunSeed;
        }

        private void OnHostDepthPayload(DungeonSyncPayload payload)
        {
            if (!_awaitingHostDepth || _expedition == null || !_expedition.IsExpeditionActive) return;
            if (payload.Depth != _expedition.State.Depth) return;
            BuildDepth();
        }

        /// <summary>Why this peer's rebuild differs from the host's depth, or null when every fingerprint matches.</summary>
        private string ClientDepthDesync(in DungeonSyncPayload payload, string poolFingerprint, bool built)
        {
            var state = _expedition.State;
            if (!payload.IsValid) return "the host published no valid depth";
            if (payload.RunSeed != state.RunSeed) return $"seed {state.RunSeed} but the host published {payload.RunSeed}";
            if (payload.Biome != (int)state.Biome) return $"biome {state.Biome} but the host published {(RuinRail.Core.Biome)payload.Biome}";
            if (poolFingerprint != payload.PoolFingerprint.ToString()) return $"room pool {poolFingerprint} but the host has {payload.PoolFingerprint} (different content build)";
            if (Generation == null || !Generation.Success || !built) return "the host's generation round did not build here: " + (Generation?.Error ?? "exit validation failed");
            var layout = DungeonFingerprints.Layout(Generation.Layout);
            if (layout != payload.LayoutFingerprint.ToString() || Generation.Rounds != payload.Rounds) return $"layout {layout}/{Generation.Rounds} but the host built {payload.LayoutFingerprint}/{payload.Rounds}";
            return null;
        }

        private void BindCoopDepth(RoomPool pool, string poolFingerprint)
        {
            if (Mode == CoopRunMode.Solo || Generation?.Layout == null) return;
            var state = _expedition.State;
            LayoutFingerprint = DungeonFingerprints.Layout(Generation.Layout);
            if (Mode == CoopRunMode.Host)
            {
                var payload = new DungeonSyncPayload
                {
                    RunSeed = state.RunSeed,
                    Depth = state.Depth,
                    Biome = (int)state.Biome,
                    Rounds = Generation.Rounds,
                    PoolFingerprint = new Unity.Collections.FixedString128Bytes(poolFingerprint),
                    LayoutFingerprint = new Unity.Collections.FixedString128Bytes(LayoutFingerprint),
                    IsValid = true
                };
                _coopHost?.BindDepth(state.Depth, Rooms, _services.GroundLoot, payload, _rig.Player);
                Debug.Log($"COOP-HOST depth {state.Depth} {state.Biome} published: layout {LayoutFingerprint} round {Generation.Rounds}, {Rooms.Count} rooms.");
                return;
            }

            if (_clientLoot == null)
            {
                var lootHost = new GameObject("CoopLootPresentation");
                lootHost.transform.SetParent(transform, false);
                _clientLoot = _services.CreateLootSpawner(lootHost);
            }

            _coopClient.BindDepth(state.Depth, Rooms, _clientLoot, _app.Configs.Resolve);
            // Nothing on the client acts until the host has every peer's depth (82: gameplay starts together).
            HoldClientGameplay();
            _coopClient.ReportDepth(state.Depth, true, LayoutFingerprint, null);
            _coopClient.RequestResync();
            Debug.Log($"COOP-CLIENT depth {state.Depth} {state.Biome} rebuilt: layout {LayoutFingerprint} matches the host, {Rooms.Count} rooms.");
        }

        private void HoldClientGameplay()
        {
            if (_clientGameplayHeld) return;
            _clientGameplayHeld = true;
            _clientGameplayHeldAt = Time.realtimeSinceStartup;
            RuinRail.Core.Input.GameplayInputGate.Hold();
        }

        private void ReleaseClientGameplay()
        {
            if (!_clientGameplayHeld) return;
            _clientGameplayHeld = false;
            RuinRail.Core.Input.GameplayInputGate.Release();
        }

        /// <summary>
        /// Every member starts on the Start room's spawn markers in the run's member order. The host places every
        /// member (it is the authority for positions); a client places its own player on the same marker, so the
        /// host's first published position agrees with what the client already shows.
        /// </summary>
        private Vector2 PlaceCoopMembersAtStart(DungeonLayout layout, IReadOnlyDictionary<int, RoomRoot> rooms)
        {
            var points = PartySpawnPoints.ForStart(layout, rooms);
            var members = _run != null ? _run.Members.OrderBy(m => m.ClientId).ToList() : new List<RunStartMessage.RunMember>();
            var local = Coop != null ? Coop.LocalClientId : 0;
            var localPosition = points.Count > 0 ? points[0] : Vector2.zero;
            for (var i = 0; i < members.Count; i++)
            {
                var clientId = members[i].ClientId;
                var point = PartySpawnPoints.ForMember(points, i);
                if (clientId == local) localPosition = point;
                if (Mode == CoopRunMode.Client && clientId != local) continue;
                var entity = clientId == local ? _rig.Player : _party?.Presence.Entities.TryGetValue(clientId, out var e) == true ? e.GameObject : null;
                if (entity == null) continue;
                entity.transform.position = point;
                var body = entity.GetComponent<Rigidbody2D>();
                if (body != null) body.position = point;
            }

            return localPosition;
        }

        // ---------------------------------------------------------------- host handlers

        public int HostHolds { get; private set; }
        public int HostReconnects { get; private set; }
        public int HostGraceExpiries { get; private set; }

        private void OnHostMemberHeld(PendingReconnect pending)
        {
            HostHolds++;
            pending?.Entity?.GameObject?.GetComponent<NetworkPlayerMotion>()?.ResetRemoteInput();
            _hud?.SetMemberConnected(pending?.Entity?.GameObject?.GetComponent<PlayerLifeStateComponent>()?.ParticipantId, false);
        }

        private void OnHostMemberGraceExpired(PendingReconnect pending)
        {
            // 85: "If the player does not reconnect, treat them as Dead; their gear is not dropped to teammates."
            HostGraceExpiries++;
            pending?.Entity?.GameObject?.GetComponent<PlayerLifeStateComponent>()?.MarkDeadByAuthority("disconnect_grace_expired");
        }

        private void OnHostMemberReconnected(NetworkPlayerEntity entity, ulong newClientId)
        {
            // 85: the held character goes back to the member under its new connection, the same object, nothing new.
            HostReconnects++;
            var driver = _app.Network?.Driver as NgoNetworkDriver;
            driver?.TransferOwnership(entity?.GameObject, newClientId);
            entity?.GameObject?.GetComponent<NetworkPlayerMotion>()?.ResetRemoteInput();
            var held = _mirrors.Where(pair => pair.Value.Entity == entity?.GameObject).Select(pair => (ulong?)pair.Key).FirstOrDefault();
            if (held.HasValue && held.Value != newClientId && _mirrors.TryGetValue(held.Value, out var mirror))
            {
                _mirrors.Remove(held.Value);
                mirror.ClientId = newClientId;
                _mirrors[newClientId] = mirror;
            }

            // The loot authority knows members by connection: the returning member is the same participant (same
            // backpack mirror, same wallet) under its new connection, and its old connection id resolves nothing now.
            var receiver = entity?.GameObject != null ? entity.GameObject.GetComponent<PlayerLootReceiver>() : null;
            var life = entity?.GameObject != null ? entity.GameObject.GetComponent<PlayerLifeStateComponent>() : null;
            if (_lootAuthority != null && receiver?.Inventory != null)
            {
                if (held.HasValue && held.Value != newClientId) _lootAuthority.UnregisterParticipant(held.Value);
                _lootAuthority.RegisterParticipant(new LootParticipant(newClientId, life != null ? life.ParticipantId : newClientId.ToString(),
                    receiver.Backpack, receiver.Wallet, entity.GameObject, receiver.CarriedContainers));
            }

            var member = _run?.Members.FirstOrDefault(m => m.ParticipantId == entity?.GameObject.GetComponent<PlayerLifeStateComponent>()?.ParticipantId);
            if (member != null) member.ClientId = newClientId;
            Coop?.ResendRunStart(newClientId);
            Coop?.SendReconnectToken(newClientId, entity?.ReconnectToken, member?.ParticipantId);
        }

        private void OnClientDropRequested(ulong clientId, TradeRequestMessage request)
        {
            if (request == null || _lootAuthority == null) return;
            var entity = _party?.Presence.Entities.TryGetValue(clientId, out var e) == true ? e.GameObject : null;
            // 84: a Downed/Dead member drops nothing — the host checks its own copy of the member, not the request.
            var result = entity == null || entity.GetComponentInParent<IPlayerActionGate>()?.CanAct == false
                ? null
                : _lootAuthority.RequestDrop(request.TransactionId, clientId, null, request.InstanceId, request.Quantity, entity.transform.position);
            // The host copy of the item is now the ground pickup; the member's own copy leaves only through this revoke.
            if (result != null && result.Verdict == LootVerdict.Accepted) _coopHost.SendRevoke(clientId, request.InstanceId, result.Quantity, request.TransactionId);
            else _coopHost.SendNotice(clientId, "Could not drop that.", true, request.TransactionId);
        }

        /// <summary>
        /// The co-op member's drop route (82): the host drops from its copy of this inventory and revokes the item here.
        /// One request per item at a time, so a drop cannot be asked twice before the host's answer arrives.
        /// </summary>
        private bool RequestHostDrop(string instanceId, int quantity)
        {
            if (_coopClient == null || string.IsNullOrEmpty(instanceId) || _pendingDrops.ContainsValue(instanceId)) return false;
            _pendingDrops[_coopClient.SendDrop(instanceId, quantity)] = instanceId;
            return true;
        }

        // ---------------------------------------------------------------- client handlers

        private void OnClientInventoryChanged(EquippedSlot slot, ItemInstance item) => _coopClient?.MarkInventoryDirty();
        private void OnClientInventoryChanged2() => _coopClient?.MarkInventoryDirty();

        private void OnOwnedCoinsChanged(NetworkPlayerObject _, int coins) => ApplyMirroredCoins(coins);

        /// <summary>58/84: this member's Carried Coins are what the host's wallet for it holds; the local wallet mirrors it.</summary>
        private void ApplyMirroredCoins(int coins)
        {
            var wallet = _expedition?.State?.CarriedWallet;
            if (wallet == null || !_expedition.IsExpeditionActive) return;
            _mirroredCoins = coins;
            var delta = coins - wallet.Balance;
            if (delta > 0) wallet.Credit(delta, "coop_host_wallet");
            else if (delta < 0) wallet.Debit(-delta, "coop_host_wallet");
        }

        private void OnClientXp(XpMessage message)
        {
            if (message == null || _expedition == null || !_expedition.IsExpeditionActive) return;
            // Every member earns the kill XP on its own profile (13: permanent), counted once per host-side death.
            if (message.Kind == (int)CoopActorKind.Elite) _expedition.RecordEliteDefeated(message.Amount);
            else _expedition.RecordEnemyDefeated(message.Amount);
        }

        public const float ReconnectWindowSeconds = 50f;
        private bool _reconnecting;

        /// <summary>How often this client lost its link mid-expedition and how often it came back (85).</summary>
        public int ClientLinkLosses { get; private set; }
        public int ClientReconnectAttempts { get; private set; }

        private void OnClientLinkLost()
        {
            if (_ended || _reconnecting || Mode != CoopRunMode.Client || _expedition == null || !_expedition.IsExpeditionActive) return;
            ClientLinkLosses++;
            StartCoroutine(ReconnectLoop());
        }

        /// <summary>
        /// 85 client side: the connection dropped mid-expedition. This peer keeps its own at-risk state (inventory,
        /// wallet, transaction — none of it lives on the network object) and reconnects with the token the host issued;
        /// the host hands back the same character, and the run recomposes from the host's current state (never from
        /// the depth's start). If no reconnect succeeds inside the window the expedition fails for this peer.
        /// </summary>
        private IEnumerator ReconnectLoop()
        {
            _reconnecting = true;
            HoldClientGameplay();
            Notify("CONNECTION LOST — RECONNECTING…", true);
            var coop = Coop;
            var driver = _app.Network?.Driver as NgoNetworkDriver;
            var token = coop?.ReconnectToken?.Token;
            var deadline = Time.realtimeSinceStartup + ReconnectWindowSeconds;
            var back = false;
            while (driver != null && !string.IsNullOrEmpty(token) && Time.realtimeSinceStartup < deadline)
            {
                var settle = Time.realtimeSinceStartup + 5f;
                while (driver.IsListening && Time.realtimeSinceStartup < settle) yield return null;
                ClientReconnectAttempts++;
                driver.SetConnectionPayload(_app.Menu.Session?.Profile.DisplayName ?? "Player", token);
                var started = driver.StartClient();
                var wait = Time.realtimeSinceStartup + 10f;
                while (started.Success && !(coop.IsClient) && Time.realtimeSinceStartup < wait) yield return null;
                if (coop.IsClient) { back = true; break; }
                driver.Shutdown();
                var pause = Time.realtimeSinceStartup + 2f;
                while (Time.realtimeSinceStartup < pause) yield return null;
            }

            if (!back)
            {
                Debug.LogError($"COOP-CLIENT could not reconnect within {ReconnectWindowSeconds} s (token={(string.IsNullOrEmpty(token) ? "none" : "held")}): the expedition fails for this player.");
                _reconnecting = false;
                ReleaseClientGameplay();
                if (_expedition.IsExpeditionActive) _expedition.Fail();
                yield break;
            }

            Debug.Log($"COOP-CLIENT reconnected as client {coop.LocalClientId} after {ClientReconnectAttempts} attempt(s): recomposing from the host's current state.");
            // The run recomposes on the character the host handed back, from the host's current depth and world.
            _app.LoadScene(SceneNames.Dungeon);
        }

        private void OnClientTransitOpened(TransitOpenMessage message)
        {
            if (message == null || _expedition == null || !_expedition.IsExpeditionActive) return;
            if (_expedition.Transit != null && _expedition.Transit.State != TransitDecisionState.Closed)
            {
                // A recomposed run (reconnect) takes the still-open decision back into its vote screen.
                if (_vote == null && _expedition.Transit.State == TransitDecisionState.Open)
                {
                    _expedition.Transit.Submitted -= OnClientVoteSubmitted;
                    _expedition.Transit.Submitted += OnClientVoteSubmitted;
                    OnTransitOpened(_expedition.Transit);
                }

                return;
            }
            // The host's voters, the host's dead list, and a policy that never resolves here: the host's result does.
            var living = message.Living ?? Array.Empty<string>();
            var dead = message.Dead ?? Array.Empty<string>();
            _expedition.SetPartyTransit(() => living, () => dead, _hostDecided);
            var decision = _expedition.RecordBossDefeated(message.BossXp);
            if (decision != null) decision.Submitted += OnClientVoteSubmitted;
            MarkLocalBossDefeatedPresentation();
        }

        private void OnClientVoteSubmitted(TransitDecision decision, string voter, TransitChoice choice)
        {
            // Only this player's own vote travels; the host counts it once under this player's participant id.
            if (voter != _expedition?.State?.TransactionId) return;
            _coopClient?.SendVote(choice);
        }

        private void OnClientTransitVotes(TransitVotesMessage message)
        {
            var decision = _expedition?.Transit;
            if (message == null || decision == null || decision.State != TransitDecisionState.Open) return;
            var local = _expedition.State.TransactionId;
            for (var i = 0; i < message.Voters.Length && i < message.Choices.Length; i++)
            {
                if (message.Voters[i] == local) continue;
                decision.MirrorVote(message.Voters[i], (TransitChoice)message.Choices[i]);
            }
        }

        private void OnClientTransitResolved(TransitResolvedMessage message)
        {
            var decision = _expedition?.Transit;
            if (message == null || decision == null) return;
            // This peer's own Descend or Return transaction runs from the host's single result (86).
            decision.ResolveFromAuthority((TransitChoice)message.Choice);
        }

        private void OnClientRunEnded(RunEndedMessage message)
        {
            if (message == null || _expedition == null || !_expedition.IsExpeditionActive) return;
            // 85: the party failed (wipe / host quit): this peer's at-risk state is lost with it, exactly once.
            if (message.Outcome != 0) _expedition.Fail();
        }

        private void OnClientGranted(GrantMessage grant)
        {
            if (grant?.Item == null || _expedition?.State?.Inventory == null) return;
            var inventory = _expedition.State.Inventory;
            var item = ItemInstance.FromSnapshot(grant.Item);
            item.IsAtRisk = true;
            if (!inventory.TryAddToBackpack(item))
                Debug.LogWarning($"COOP-CLIENT grant {grant.Item.DefinitionId} x{grant.Item.Quantity} ({grant.Source}) did not fit the backpack; the host's mirror will reconcile.");
            RefreshInventoryUi();
            _merchant?.Refresh();
        }

        private void OnClientRevoked(RevokeMessage revoke)
        {
            if (revoke == null) return;
            if (!string.IsNullOrEmpty(revoke.TransactionId)) _pendingDrops.Remove(revoke.TransactionId);
            if (_expedition?.State?.Inventory == null) return;
            // The host already owns what it revokes (a sale, or a drop now lying on the ground): it leaves wherever this
            // member keeps it by now — a backpack slot or a worn slot — whole, or only the revoked part of a stack.
            var inventory = _expedition.State.Inventory;
            var removed = false;
            for (var i = 0; i < inventory.BackpackSlots.Count && !removed; i++)
            {
                var item = inventory.BackpackSlots[i];
                if (item == null || item.InstanceId != revoke.InstanceId) continue;
                if (revoke.Quantity > 0 && revoke.Quantity < item.Quantity) { item.SetQuantity(item.Quantity - revoke.Quantity); _coopClient?.MarkInventoryDirty(); }
                else inventory.RemoveFromBackpack(i);
                removed = true;
            }

            foreach (EquippedSlot slot in Enum.GetValues(typeof(EquippedSlot)))
            {
                var item = removed ? null : inventory.GetEquipped(slot);
                if (item == null || item.InstanceId != revoke.InstanceId) continue;
                if (revoke.Quantity > 0 && revoke.Quantity < item.Quantity) { item.SetQuantity(item.Quantity - revoke.Quantity); _coopClient?.MarkInventoryDirty(); }
                else inventory.Unequip(slot);
                removed = true;
            }

            _merchant?.Refresh();
        }

        /// <summary>The host attributed a kill to this member's hit: its own rig's kill passives (Adrenaline, Flow State).</summary>
        private void OnClientKillConfirmed(KillMessage kill)
        {
            if (kill == null || _rig?.CombatEvents == null) return;
            ClientKillsConfirmed++;
            if (kill.Melee) _rig.CombatEvents.RaiseMeleeKill();
            else _rig.CombatEvents.RaiseEnemyKilled();
        }

        /// <summary>Kills the host attributed to this client member (diagnostics / proof).</summary>
        public int ClientKillsConfirmed { get; private set; }

        private void OnClientTradeResult(TradeResultMessage result)
        {
            if (result == null || _merchant == null) return;
            var error = result.Verdict == (int)LootVerdict.Accepted ? TradeError.None
                : Enum.TryParse<TradeError>(result.Error, out var parsed) ? parsed : TradeError.AlreadySold;
            if (result.Verdict == (int)LootVerdict.AlreadyTaken) error = TradeError.AlreadySold;
            _merchant.ReportRemoteResult(result.IsBuy, error, result.ItemName, result.Coins);
        }

        private void OnClientCacheResult(CacheResultMessage result)
        {
            if (result == null || _weaponCache == null) return;
            _weaponCache.ReportRemoteResult(result.Accepted, result.AlreadyTaken, result.ItemName);
        }

        private void OnClientBossState(BossStateMessage state, EnemyReplica replica)
        {
            if (state == null || replica == null || _hud == null) return;
            var node = state.RoomNode;
            var room = Rooms != null && Rooms.TryGetValue(node, out var r) ? r : null;
            _hud.BindBoss(_clientBossName ?? "BOSS", replica.Health, () => room != null && room.Lifecycle == RoomLifecycleState.Active && !(Coop?.Bus != null && _coopClient?.LastBossState?.Defeated == true));
            if (state.Started && !_bossMusicStarted) { _bossMusicStarted = true; _app.MusicBinder.ObserveBossRoomEntered(); }
        }

        private bool _bossMusicStarted;

        private void MarkLocalBossDefeatedPresentation()
        {
            _bossMusicStarted = false;
        }

        /// <summary>
        /// A host replica's presentation on this client: the same body art, bars, hit flash, telegraph marker, damage
        /// numbers and audio a host-side actor gets, driven by the replicated state instead of a controller.
        /// </summary>
        private void BindReplicaPresentation(EnemyReplica replica, EnemySpawnMessage spawn)
        {
            if (replica == null || spawn == null) return;
            var content = _app.Content;
            var kind = (CoopActorKind)spawn.Kind;
            EnemyDefinition enemy = null;
            IReadOnlyList<EnemyAttackDefinition> moveset = Array.Empty<EnemyAttackDefinition>();
            string displayName = spawn.DefinitionId;
            switch (kind)
            {
                case CoopActorKind.Boss:
                    var boss = content.Bosses.FirstOrDefault(b => b != null && b.Id == spawn.DefinitionId);
                    if (boss != null)
                    {
                        moveset = boss.Moveset.Concat(boss.PhaseTwoArenaHazards ?? (IReadOnlyList<EnemyAttackDefinition>)Array.Empty<EnemyAttackDefinition>()).ToList();
                        displayName = boss.DisplayName;
                        _clientBossName = boss.DisplayName;
                    }

                    break;
                case CoopActorKind.Elite:
                    var elite = content.Elites.FirstOrDefault(e => e != null && e.Id == spawn.DefinitionId);
                    if (elite != null) moveset = elite.Moveset.ToList();
                    break;
                default:
                    enemy = content.Enemies.FirstOrDefault(e => e != null && e.Id == spawn.DefinitionId);
                    break;
            }

            replica.ConfigureCombatPresence(kind, spawn.RoomNode, enemy, moveset);
            replica.gameObject.name = $"Replica_{spawn.NetId}_{displayName}";
            var effects = FindFirstObjectByType<EffectPool>();
            var numbers = effects != null ? effects.GetComponent<DamageNumberPool>() : null;
            numbers?.Bind(replica.Health);
            var body = CharacterVisual.Attach(replica.gameObject, content.AnimationSetFor(spawn.DefinitionId));
            replica.gameObject.AddComponent<EnemyAnimationDriver>().ConfigureReplica(body, replica);
            if (kind == CoopActorKind.Normal) replica.gameObject.AddComponent<WorldHealthBar>().Configure(replica.Health, WorldHealthBar.Style.Normal);
            else if (kind == CoopActorKind.Elite) replica.gameObject.AddComponent<WorldHealthBar>().Configure(replica.Health, WorldHealthBar.Style.Elite, 1.7f, content.Feedback != null ? content.Feedback.EliteBossTelegraphColor : (Color?)null);
            var replicaFlash = replica.gameObject.AddComponent<HitFlash>();
            replicaFlash.Configure(content.Feedback, replica.Health, null, body != null ? body.Renderer : null);
            if (kind == CoopActorKind.Boss) replicaFlash.UseBossProfile(); // strength from the replicated HP drop
            replica.gameObject.AddComponent<TelegraphIndicator>().ConfigureReplica(content.Feedback, effects, replica);
            _app.AudioBinder.Attach(replica.Health, false);
            _tutorial?.ObserveEnemySpawned();
        }

        /// <summary>Another peer's shot, drawn on this peer: zero damage, the real path and art.</summary>
        private ProjectilePoolHolder _shotPresentation;

        private void DrawRemoteShot(ShotNetRecord shot)
        {
            _shotPresentation ??= new ProjectilePoolHolder(transform);
            _shotPresentation.Draw(shot);
        }

        /// <summary>Wraps the zero-damage pool the host draws client shots with.</summary>
        private sealed class ProjectilePoolHolder
        {
            private readonly RuinRail.Gameplay.Combat.Projectiles.ProjectilePool _pool;

            public ProjectilePoolHolder(Transform parent)
            {
                var go = new GameObject("CoopShotPresentation");
                go.transform.SetParent(parent, false);
                _pool = go.AddComponent<RuinRail.Gameplay.Combat.Projectiles.ProjectilePool>();
                _pool.IsPresentationOnly = true;
            }

            public int Drawn { get; private set; }

            public void Draw(ShotNetRecord shot)
            {
                Drawn++;
                _pool.Spawn(shot.Origin, new RuinRail.Gameplay.Combat.Projectiles.ProjectileSpawnData(0, shot.Speed, shot.Range, 0f, 0f, shot.Direction, null, null, 0f,
                    (DamageTeam)shot.Team, false, shot.Visual.ToString()));
            }
        }

        /// <summary>The room node an interactable belongs to (for the client's trade/cache requests).</summary>
        private int NodeOf(Component interactable)
        {
            if (interactable == null || Rooms == null) return -1;
            var room = interactable.GetComponentInParent<RoomRuntime>();
            return room != null ? room.State.NodeId : -1;
        }

        private void TickCoop(float deltaTime)
        {
            _coopHost?.Tick(deltaTime);
            if (Mode == CoopRunMode.Host) _party?.Presence.Grace?.Tick();
            if (_coopClient == null) return;
            _coopClient.Tick(deltaTime);
            // The host's release normally arrives within a second; the fallback never leaves a player frozen.
            if (_clientGameplayHeld && Time.realtimeSinceStartup - _clientGameplayHeldAt > CoopHostWorld.GameplayReleaseTimeoutSeconds + 5f) ReleaseClientGameplay();
        }

        private void DisposeCoop()
        {
            if (_expedition?.State?.Inventory != null)
            {
                _expedition.State.Inventory.EquippedChanged -= OnClientInventoryChanged;
                _expedition.State.Inventory.BackpackChanged -= OnClientInventoryChanged2;
            }

            if (_ownedNet != null) _ownedNet.CarriedCoinsChanged -= OnOwnedCoinsChanged;
            if (Coop?.Bus != null) Coop.Bus.DepthPayloadChanged -= OnHostDepthPayload;
            if (Coop != null) Coop.LinkLost -= OnClientLinkLost;
            if (_party?.Presence.Grace != null)
            {
                _party.Presence.Grace.Held -= OnHostMemberHeld;
                _party.Presence.Grace.GraceExpired -= OnHostMemberGraceExpired;
            }
            _coopHost?.Dispose();
            _coopHost = null;
            _coopClient?.Dispose();
            _coopClient = null;
            foreach (var mirror in _mirrors.Values) mirror.Dispose();
            _mirrors.Clear();
            ReleaseClientGameplay();
            if (Mode == CoopRunMode.Client) PlayerInteractor.LocalInteractionFilter = null;
            DamageAuthority.ClearRelays();
            RuinRail.Gameplay.Combat.Impact.ImpactDispatcher.RemoteImpactRelay = null;
        }
    }

    /// <summary>
    /// The host's copy of a joined member (82): that member's loadout and attribute ranks as the member reported them,
    /// so the copy the host simulates has the member's stats (max HP, speed, damage reduction, pickup reach) and the
    /// loot authority checks the member's real backpack capacity. It carries no weapons — the member fires its own, and
    /// its hits arrive as requests the host validates against these very items.
    /// </summary>
    public sealed class CoopMemberMirror : IDisposable
    {
        private readonly ItemDefinitionRegistry _registry;
        private readonly LoadoutStatRegistrar _registrar;
        private readonly PlayerStatsBinder _binder;

        public CoopMemberMirror(GameObject entity, ulong clientId, GameContentCatalog content, ItemDefinitionRegistry registry, CoopMemberProfile profile,
            Func<IEnumerable<GameObject>> groundPickups = null, int coinsBroughtIn = 0)
        {
            Entity = entity;
            ClientId = clientId;
            _registry = registry;
            Inventory = PlayerInventory.FromRegistry(registry, content.AmmoBalance);
            if (profile?.Loadout != null) Inventory.RestoreFromSnapshot(profile.Loadout);
            var receiver = entity.GetComponent<PlayerLootReceiver>();
            receiver?.SetInventory(Inventory);
            // 58/84: the host holds this member's Carried Coins. Coins the member took from its bank start there, once,
            // when the copy is composed for the run (the copy lives for the whole run; reconnects reuse it).
            _wallet = receiver != null ? receiver.Wallet : null;
            SeededCoins = Math.Max(0, coinsBroughtIn);
            if (SeededCoins > 0) _wallet?.Credit(SeededCoins, "carried_in");
            // The host simulates this member's body, so enemy impacts land on this copy: it needs the same authored
            // stagger config as every other player (its resistances come from the member's stats bound below).
            entity.GetComponent<PlayerImpactReceiver>()?.SetConfig(content.Stagger);
            _binder = entity.GetComponent<PlayerStatsBinder>();
            if (_binder != null)
            {
                _binder.ApplyProgression(profile != null ? profile.Skills() : null);
                var affixes = AffixRegistry.FromDefinitions(registry?.Definitions);
                _registrar = new LoadoutStatRegistrar(Inventory, _binder.Stats, Resolve, affixes.Get);
                Inventory.SetAmmoCapacityBonusProvider(() => _binder.Stats.GetPercent(StatId.AmmoStackCapacity));
                _binder.Bind();
                var health = entity.GetComponent<HealthComponent>();
                // A new expedition starts every member at its true effective maximum, exactly as the solo rig does.
                health?.SetMaxHealth(_binder.Stats.MaxHealth);

                // The member's incoming-impact passives (Anchored, Shock Absorber, Exo Lock) belong where the impacts
                // land: on this copy the host simulates. The existing registrar composes them from the member's
                // mirrored equipment against the host's own event hub; cooldowns and buffs live only here, the member's
                // own rig does not run them, and nothing the member sends can trigger one.
                Events = new PlayerCombatEvents();
                entity.GetComponent<PlayerImpactReceiver>()?.SetEvents(Events);
                // The member's pickups are resolved on this copy (Scavenger's Reserve sizes the stack here).
                receiver?.SetCombatEvents(Events);
                _health = health;
                if (_health != null)
                {
                    _health.Damaged += Events.RaiseDamageTaken;
                    _health.Damaged += RaiseHealthChanged;
                    _health.Healed += RaiseHealthChanged;
                }

                // Arc Stagger's shockwave and Room Sweep's pull act here, on the host's world around this body; the room
                // lifecycle reaches this copy's hub through its own relay (the member's rig hears it on the client).
                _roomRelay = entity.GetComponent<RuinRail.Dungeon.Runtime.PlayerRoomEventsRelay>();
                if (_roomRelay == null) _roomRelay = entity.AddComponent<RuinRail.Dungeon.Runtime.PlayerRoomEventsRelay>();
                _roomRelay.SetEvents(Events);
                World = new PlayerPassiveWorld(entity, groundPickups);
                Passives = new EquipmentPassiveRegistrar(Inventory, Resolve,
                    new PassiveContext(_binder.Stats, Events, () => _health != null ? _health.CurrentHealth : 0, () => _health != null ? _health.MaxHealth : 0, _ => false, null, World.Actions),
                    EquipmentPassiveRegistrar.IsHostResolvedMechanic);
                _ticker = EquipmentPassiveTicker.On(entity);
                _ticker.Add(Passives);
            }
        }

        /// <summary>The host-side event hub of this member's body (incoming stagger / explosion knockback / damage).</summary>
        public PlayerCombatEvents Events { get; }
        /// <summary>The member's incoming-impact passives, host-owned (null when the copy has no stats binder).</summary>
        public EquipmentPassiveRegistrar Passives { get; }
        /// <summary>The world side of the member's host-resolved passives (Arc Stagger, Room Sweep).</summary>
        public PlayerPassiveWorld World { get; }
        private readonly RuinRail.Dungeon.Runtime.PlayerRoomEventsRelay _roomRelay;
        private readonly HealthComponent _health;
        private readonly EquipmentPassiveTicker _ticker;

        public ulong ClientId { get; set; }
        public GameObject Entity { get; }
        public PlayerInventory Inventory { get; }
        public uint LastVersion { get; private set; }
        public int Applied { get; private set; }
        public int Stale { get; private set; }

        private ItemDefinition Resolve(string id) => _registry != null && _registry.TryGet(id, out var definition) ? definition : null;

        /// <summary>The member's own report of its inventory (newest wins); ranks may change only between runs.</summary>
        public bool Apply(InventorySnapshotMessage message)
        {
            if (message?.Inventory == null) return false;
            if (Applied > 0 && message.Version <= LastVersion) { Stale++; return false; }
            LastVersion = message.Version;
            Applied++;
            ReconcileBroughtCoins(message.CoinsBroughtIn);
            Inventory.RestoreFromSnapshot(message.Inventory);
            // A restored snapshot raises no EquippedChanged: re-read armor/accessory (an unchanged item keeps its state).
            Passives?.Refresh();
            return true;
        }

        private readonly RuinRail.Gameplay.Economy.CoinWallet _wallet;

        /// <summary>Banked Coins this copy's wallet was seeded with at composition (the lobby's captured value).</summary>
        public int SeededCoins { get; }
        /// <summary>Coins removed again because the member's own bank could not cover the captured value (diagnostics).</summary>
        public int CoinsShortfallRemoved { get; private set; }
        public bool CoinsReconciled { get; private set; }

        /// <summary>
        /// Once per run: the member reports what its own Start really moved out of its bank. The seed can only come
        /// down to that (a member's report never raises its host wallet), so a spend in the moment between the lobby
        /// capture and the start can never leave the member with coins its bank did not pay for.
        /// </summary>
        private void ReconcileBroughtCoins(int reported)
        {
            if (CoinsReconciled || reported < 0 || _wallet == null) return;
            CoinsReconciled = true;
            var shortfall = SeededCoins - reported;
            if (shortfall <= 0) return;
            var remove = Math.Min(shortfall, _wallet.Balance);
            if (remove > 0 && _wallet.Debit(remove, "carried_in_shortfall").Success) CoinsShortfallRemoved = remove;
        }

        /// <summary>The largest single hit this member's carried weapons and grenades could deal (validation ceiling).</summary>
        public int MaxHit()
        {
            var best = 0;
            foreach (EquippedSlot slot in Enum.GetValues(typeof(EquippedSlot)))
            {
                var item = Inventory.GetEquipped(slot);
                if (item != null) best = Math.Max(best, MaxDamageOf(Resolve(item.DefinitionId)));
            }

            foreach (var item in Inventory.BackpackSlots)
            {
                if (item != null) best = Math.Max(best, MaxDamageOf(Resolve(item.DefinitionId)));
            }

            // Stat bonuses, Legendary specials and wall-impact bonuses can multiply a hit; a generous ceiling still
            // refuses the one thing that matters — an arbitrary number (82: no competitive-grade anti-cheat).
            return Math.Max(50, best * 4 + 50);
        }

        private static int MaxDamageOf(ItemDefinition definition) => definition switch
        {
            BlasterWeaponDefinition blaster => blaster.DamageMax,
            BowWeaponDefinition bow => Math.Max(bow.FullDrawDamageMax, bow.QuickDamageMax),
            MeleeWeaponDefinition melee => melee.DamageMax,
            RangedWeaponDefinition ranged => ranged.DamageMax,
            RuinRail.Gameplay.Items.Consumables.ConsumableDefinition consumable => consumable.Grenade.DamageMax,
            _ => 0
        };

        private void RaiseHealthChanged(int amount) { if (_health != null) Events?.RaiseHealthChanged(_health.CurrentHealth, _health.MaxHealth); }

        public void Dispose()
        {
            _registrar?.Dispose();
            if (_ticker != null) _ticker.Remove(Passives);
            if (_health != null && Events != null)
            {
                _health.Damaged -= Events.RaiseDamageTaken;
                _health.Damaged -= RaiseHealthChanged;
                _health.Healed -= RaiseHealthChanged;
            }

            if (Entity != null)
            {
                var lootReceiver = Entity.GetComponent<PlayerLootReceiver>();
                if (lootReceiver != null) lootReceiver.SetCombatEvents(null);
            }
            if (_roomRelay != null) _roomRelay.SetEvents(null);
            Passives?.Dispose();
        }
    }
}
