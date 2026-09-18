using RuinRail.Gameplay.Player;
using Unity.Netcode;
using UnityEngine;

namespace RuinRail.Networking
{
    /// <summary>NGO serialization of the authoritative motion state.</summary>
    public struct PlayerNetStatePayload : INetworkSerializable
    {
        public PlayerNetState State;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref State.Position);
            serializer.SerializeValue(ref State.Velocity);
            serializer.SerializeValue(ref State.AimDirection);
            var facing = (int)State.Facing;
            serializer.SerializeValue(ref facing);
            State.Facing = (BodyFacing8)facing;
            serializer.SerializeValue(ref State.IsDashing);
            serializer.SerializeValue(ref State.IsInvulnerable);
            serializer.SerializeValue(ref State.DashSequence);
            serializer.SerializeValue(ref State.LastIntentSequence);
            serializer.SerializeValue(ref State.Time);
        }
    }

    /// <summary>
    /// Player motion over NGO (82 + 11/14):
    /// • the owner runs the unchanged movement/aim/dash components on its own input (responsive) and sends
    ///   MovementIntent every tick and DashRequest on press — intents, never positions or pointer coordinates;
    /// • the host applies intents to the same components through a RemoteIntentInputReader, validates dashes with
    ///   HostDashValidator, and publishes PlayerNetState (position, velocity, world aim, facing, dash flags);
    /// • the owner reconciles (snap beyond tolerance, otherwise trusts its prediction);
    /// • other replicas interpolate the buffered state and never run input logic.
    /// The host's own player is simply local + authoritative.
    /// </summary>
    public sealed class NetworkPlayerMotion : NetworkBehaviour
    {
        private readonly NetworkVariable<PlayerNetStatePayload> _state = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private PlayerMovement _movement;
        private PlayerAiming _aiming;
        private PlayerDash _dash;
        private Rigidbody2D _body;
        private RemoteIntentInputReader _remoteReader;
        private HostDashValidator _dashValidator;
        private readonly ReplicaInterpolator _interpolator = new();
        private uint _intentSequence;
        private uint _dashSequence;
        private uint _lastAppliedDashSequence;

        public PlayerNetState State => _state.Value.State;
        public RemoteIntentInputReader RemoteReader => _remoteReader;
        public HostDashValidator DashValidator => _dashValidator;

        public override void OnNetworkSpawn()
        {
            _movement = GetComponent<PlayerMovement>();
            _aiming = GetComponent<PlayerAiming>();
            _dash = GetComponent<PlayerDash>();
            _body = GetComponent<Rigidbody2D>();

            if (IsServer && !IsOwner)
            {
                // Host simulates a remote owner from its intents through the very same components.
                _remoteReader = new RemoteIntentInputReader();
                _movement?.SetInputReader(_remoteReader);
                _aiming?.SetInputReader(_remoteReader);
                _dash?.SetInputReader(_remoteReader);
                GetComponent<PlayerLifeStateComponent>()?.SetInputReader(_remoteReader);
                GetComponent<PlayerReviver>()?.SetInputReader(_remoteReader);
                GetComponent<DeadSpectatorFollow>()?.SetInputReader(_remoteReader);
            }

            if (IsServer && _dash != null)
            {
                _dashValidator = new HostDashValidator(_dash);
                if (IsOwner) _dash.DashStarted += (_, _) => _dashSequence++;
            }

            if (!IsServer && !IsOwner && _body != null)
            {
                // Pure replica: kinematic body driven by interpolation, no local gameplay input.
                _body.bodyType = RigidbodyType2D.Kinematic;
            }

            if (IsOwner && !IsServer && _dash != null)
            {
                _dash.DashStarted += OnOwnerDashStarted;
            }
        }

        private void OnOwnerDashStarted(PlayerDash dash, Vector2 direction)
        {
            RequestDashRpc(new DashRequest { Sequence = ++_dashSequence, Direction = direction });
        }

        private void FixedUpdate()
        {
            if (!IsSpawned) return;
            if (IsServer)
            {
                PublishState();
            }

            if (IsOwner && !IsServer)
            {
                SendIntent();
                ReconcileOwner();
            }
            else if (!IsOwner && !IsServer)
            {
                InterpolateReplica();
            }
        }

        private void SendIntent()
        {
            var input = GetComponent<PlayerInput>()?.Reader;
            if (input == null || _aiming == null) return;
            SubmitIntentRpc(MovementIntent.Create(++_intentSequence, input.Move, _aiming.AimDirection, input.FireHeld, input.SpecialHeld, input.InteractHeld));
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void SubmitIntentRpc(MovementIntent intent)
        {
            _remoteReader?.Apply(intent);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestDashRpc(DashRequest request)
        {
            if (_dashValidator == null) return;
            if (_dashValidator.Validate(request) == DashVerdict.Accepted) _lastAppliedDashSequence = request.Sequence;
        }

        private void PublishState()
        {
            var state = new PlayerNetState
            {
                Position = _body != null ? _body.position : (Vector2)transform.position,
                Velocity = _body != null ? _body.linearVelocity : Vector2.zero,
                AimDirection = _aiming != null ? _aiming.AimDirection : Vector2.right,
                Facing = _aiming != null ? _aiming.BodyFacing : BodyFacing8.E,
                IsDashing = _dash != null && _dash.IsDashing,
                IsInvulnerable = _dash != null && _dash.IsInvulnerable,
                DashSequence = IsOwner ? _dashSequence : _lastAppliedDashSequence,
                LastIntentSequence = _remoteReader?.LastSequence ?? _intentSequence,
                Time = NetworkManager.ServerTime.Time
            };
            _state.Value = new PlayerNetStatePayload { State = state };
        }

        private void ReconcileOwner()
        {
            if (_body == null) return;
            var authoritative = _state.Value.State.Position;
            if (OwnerReconciliation.NeedsCorrection(_body.position, authoritative)) _body.position = authoritative;
        }

        private void InterpolateReplica()
        {
            _interpolator.Push(_state.Value.State);
            var sample = _interpolator.Sample(NetworkManager.ServerTime.Time);
            if (_body != null) _body.MovePosition(sample.Position);
            else transform.position = sample.Position;
            _aiming?.ApplyReplicatedAim(sample.AimDirection);
        }
    }
}
