using System;
using RuinRail.Core.Input;
using RuinRail.Gameplay.Player;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RuinRail.Networking
{
    /// <summary>
    /// Network identity component on the replicated player object: the host writes the sanitized display name and
    /// owner client id; on spawn the owner binds local input, everyone else gets the null reader (input isolation).
    /// </summary>
    public sealed class NetworkPlayerObject : NetworkBehaviour
    {
        private readonly NetworkVariable<FixedString64Bytes> _displayName = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public string DisplayName => _displayName.Value.ToString();
        public bool IsLocalOwner => IsOwner;

        /// <summary>
        /// Presentation composition for every replicated player object, on every peer (host, owner, remote replica):
        /// the app registers the same body + held-weapon composer the solo expedition uses, so a networked player is
        /// never a bare collider. Registered by the composition root; null in tests that only exercise authority.
        /// </summary>
        public static Action<GameObject> VisualComposer { get; set; }

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

        public override void OnNetworkSpawn()
        {
            var reader = IsOwner ? (GetComponent<PlayerInput>() != null ? GetComponent<PlayerInput>().Reader : gameObject.AddComponent<PlayerInput>().Reader) : NullPlayerInputReader.Instance;
            if (!IsOwner)
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
            RuinRail.Gameplay.Combat.CombatLayers.TagPlayerBody(gameObject);
            ComposeVisuals();
        }
    }
}
