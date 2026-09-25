using System;
using RuinRail.Core.Input;
using RuinRail.Gameplay.Player;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RuinRail.Networking
{
    /// <summary>
    /// Network identity component on the replicated player object: the host writes the sanitized display name, the
    /// member's party participant id and its Carried Coins; on spawn the owner binds local input, everyone else gets
    /// the null reader (input isolation).
    /// </summary>
    public sealed class NetworkPlayerObject : NetworkBehaviour
    {
        private readonly NetworkVariable<FixedString64Bytes> _displayName = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<FixedString64Bytes> _participantId = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> _carriedCoins = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public string DisplayName => _displayName.Value.ToString();
        public bool IsLocalOwner => IsOwner;

        /// <summary>The member's party participant id (revive, vote and loot lookups), as the host assigned it.</summary>
        public string ParticipantId => _participantId.Value.ToString();

        /// <summary>58/84: this member's Carried Coins as the host's authoritative wallet holds them.</summary>
        public int CarriedCoins => _carriedCoins.Value;

        public event Action<NetworkPlayerObject, string> ParticipantIdChanged;
        public event Action<NetworkPlayerObject, int> CarriedCoinsChanged;

        /// <summary>
        /// Presentation composition for every replicated player object, on every peer (host, owner, remote replica):
        /// the app registers the same body + held-weapon composer the solo expedition uses, so a networked player is
        /// never a bare collider. Registered by the composition root; null in tests that only exercise authority.
        /// </summary>
        public static Action<GameObject> VisualComposer { get; set; }

        /// <summary>Raised on every peer when a player object spawns (owned or replica), after input isolation.</summary>
        public static event Action<NetworkPlayerObject> Spawned;
        public static event Action<NetworkPlayerObject> Despawned;

        public int VisualCompositions { get; private set; }

        /// <summary>Runs the registered composer once for this object (called on spawn; idempotent composers expected).</summary>
        public void ComposeVisuals()
        {
            if (VisualComposer == null) return;
            VisualComposer(gameObject);
            VisualCompositions++;
        }

        /// <summary>Host-only: assigns the sanitized name (82: identity is session information the host validates).</summary>
        public void SetDisplayName(string sanitizedName)
        {
            if (!IsServer) throw new AuthorityViolationException(AuthoritativeDomain.PartyRoster, NetworkRole.Client);
            _displayName.Value = new FixedString64Bytes(sanitizedName ?? string.Empty);
        }

        /// <summary>Host-only: the participant id the party roster, the transit vote and the loot authority use.</summary>
        public void SetParticipantId(string participantId)
        {
            if (!IsServer) throw new AuthorityViolationException(AuthoritativeDomain.PartyRoster, NetworkRole.Client);
            _participantId.Value = new FixedString64Bytes(participantId ?? string.Empty);
            ApplyParticipantId();
        }

        /// <summary>Host-only: publishes the member's authoritative Carried Coins.</summary>
        public void SetCarriedCoins(int coins)
        {
            if (!IsServer) throw new AuthorityViolationException(AuthoritativeDomain.WorldPickupValidity, NetworkRole.Client);
            if (_carriedCoins.Value != coins) _carriedCoins.Value = Mathf.Max(0, coins);
        }

        public override void OnNetworkSpawn()
        {
            // A client receives player objects while it may still be in the Shelter; the Dungeon scene load must not
            // destroy them under NGO (only the host despawns a player object).
            if (!IsServer && transform.parent == null) DontDestroyOnLoad(gameObject);
            gameObject.name = $"Player_{OwnerClientId}{(IsOwner ? "_Local" : string.Empty)}";

            BindInput(IsOwner);
            RuinRail.Gameplay.Combat.CombatLayers.TagPlayerBody(gameObject);
            ComposeVisuals();

            _participantId.OnValueChanged += OnParticipantIdChanged;
            _carriedCoins.OnValueChanged += OnCarriedCoinsChanged;
            ApplyParticipantId();
            Spawned?.Invoke(this);
        }

        /// <summary>Raised on a client when it becomes this object's owner after the spawn (85: a reclaimed character).</summary>
        public static event Action<NetworkPlayerObject> OwnershipGained;

        /// <summary>
        /// 85 on a client: a reconnecting member receives its held character first as the server's object and only then
        /// as its own. From that moment it is this peer's owned character — local input on, replica isolation off —
        /// exactly as if it had spawned owned. The host never takes local input on a character it holds.
        /// </summary>
        public override void OnGainedOwnership()
        {
            if (IsServer) return;
            BindInput(true);
            gameObject.name = $"Player_{OwnerClientId}_Local";
            OwnershipGained?.Invoke(this);
        }

        public override void OnLostOwnership()
        {
            if (IsServer) return;
            BindInput(false);
            gameObject.name = $"Player_{OwnerClientId}";
        }

        /// <summary>The owner binds its local input; everything else reads the null reader (input isolation).</summary>
        private void BindInput(bool owned)
        {
            var reader = owned ? (GetComponent<PlayerInput>() != null ? GetComponent<PlayerInput>().Reader : gameObject.AddComponent<PlayerInput>().Reader) : NullPlayerInputReader.Instance;
            if (!owned)
            {
                var input = GetComponent<PlayerInput>();
                if (input != null) Destroy(input);
            }

            GetComponent<PlayerMovement>()?.SetInputReader(reader);
            GetComponent<PlayerAiming>()?.SetInputReader(reader);
            GetComponent<PlayerDash>()?.SetInputReader(reader);
            GetComponent<PlayerInteractor>()?.SetInputReader(reader);
            GetComponent<PlayerLifeStateComponent>()?.SetInputReader(reader);
            GetComponent<PlayerReviver>()?.SetInputReader(reader);
            GetComponent<DeadSpectatorFollow>()?.SetInputReader(reader);
        }

        public override void OnNetworkDespawn()
        {
            _participantId.OnValueChanged -= OnParticipantIdChanged;
            _carriedCoins.OnValueChanged -= OnCarriedCoinsChanged;
            Despawned?.Invoke(this);
        }

        private void OnParticipantIdChanged(FixedString64Bytes previous, FixedString64Bytes next)
        {
            ApplyParticipantId();
            ParticipantIdChanged?.Invoke(this, next.ToString());
        }

        private void OnCarriedCoinsChanged(int previous, int next) => CarriedCoinsChanged?.Invoke(this, next);

        private void ApplyParticipantId()
        {
            var id = ParticipantId;
            if (string.IsNullOrEmpty(id)) return;
            GetComponent<PlayerLifeStateComponent>()?.SetParticipantId(id);
        }
    }
}
