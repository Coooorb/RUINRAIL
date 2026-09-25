using System;
using System.Collections.Generic;
using RuinRail.Core.Input;
using RuinRail.Gameplay.Player;
using Unity.Netcode;
using UnityEngine;

namespace RuinRail.Networking
{
    /// <summary>
    /// What an owning client sends each tick: a clamped move input and a normalized world-space aim direction.
    /// Never raw OS pointer coordinates (the owner resolves its pointer to a world direction before sending) and
    /// never a position — the host simulates positions itself from these intents (82: clients send inputs).
    /// </summary>
    [Serializable]
    public struct MovementIntent : INetworkSerializable
    {
        public uint Sequence;
        public Vector2 Move;
        public Vector2 Aim;
        public bool FireHeld;
        public bool SpecialHeld;
        public bool InteractHeld;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Sequence);
            serializer.SerializeValue(ref Move);
            serializer.SerializeValue(ref Aim);
            serializer.SerializeValue(ref FireHeld);
            serializer.SerializeValue(ref SpecialHeld);
            serializer.SerializeValue(ref InteractHeld);
        }

        public static MovementIntent Create(uint sequence, Vector2 move, Vector2 aimDirection, bool fireHeld = false, bool specialHeld = false, bool interactHeld = false)
        {
            return new MovementIntent
            {
                Sequence = sequence,
                Move = Vector2.ClampMagnitude(move, 1f),
                Aim = aimDirection.sqrMagnitude > 0.0001f ? aimDirection.normalized : Vector2.zero,
                FireHeld = fireHeld,
                SpecialHeld = specialHeld,
                InteractHeld = interactHeld
            };
        }

        public bool IsValid => !float.IsNaN(Move.x) && !float.IsNaN(Move.y) && Move.sqrMagnitude <= 1.0001f && Aim.sqrMagnitude <= 1.0001f;
    }

    /// <summary>Edge-triggered weapon/consumable/interact intents (held fire/special travel in MovementIntent).</summary>
    public enum WeaponCommandKind
    {
        Reload,
        SelectPrimary,
        SelectSecondary,
        Swap,
        UseConsumable,
        Interact
    }

    [Serializable]
    public struct WeaponCommand : INetworkSerializable
    {
        public uint Sequence;
        public WeaponCommandKind Kind;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Sequence);
            var kind = (int)Kind;
            serializer.SerializeValue(ref kind);
            Kind = (WeaponCommandKind)kind;
        }
    }

    /// <summary>A dash request: sequence numbers make duplicates (re-sent or malicious) detectable.</summary>
    [Serializable]
    public struct DashRequest : INetworkSerializable
    {
        public uint Sequence;
        public Vector2 Direction;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Sequence);
            serializer.SerializeValue(ref Direction);
        }
    }

    public enum DashVerdict
    {
        Accepted,
        RejectedDuplicate,
        RejectedCooldown,
        RejectedAlreadyDashing,
        RejectedDirection,
        RejectedStale
    }

    /// <summary>
    /// Host-side authoritative state of one player, replicated to every peer. Positions come from the host's own
    /// simulation; the owner reconciles against it, replicas interpolate it.
    /// </summary>
    [Serializable]
    public struct PlayerNetState
    {
        public Vector2 Position;
        public Vector2 Velocity;
        public Vector2 AimDirection;
        public BodyFacing8 Facing;
        public bool IsDashing;
        public bool IsInvulnerable;
        public uint DashSequence;
        public uint LastIntentSequence;
        public double Time;
    }

    /// <summary>
    /// The input reader the host attaches to a remote owner's entity: it exposes the latest validated intent so the
    /// unchanged PlayerMovement/PlayerAiming/PlayerDash components simulate the remote player exactly like a local one.
    /// Aim is always a world direction (IsAimFromPointer = false).
    /// </summary>
    public sealed class RemoteIntentInputReader : IPlayerInputReader
    {
        public Vector2 Move { get; private set; }
        public Vector2 Aim { get; private set; }
        public bool IsAimFromPointer => false;
        public bool FireHeld { get; private set; }
        public bool SpecialHeld { get; private set; }
        public bool InteractHeld { get; private set; }
        public uint LastSequence { get; private set; }
        public int AppliedIntents { get; private set; }

#pragma warning disable CS0067
        public event Action Dash;
        public event Action Reload;
        public event Action Interact;
        public event Action Weapon1Selected;
        public event Action Weapon2Selected;
        public event Action WeaponSwapped;
        public event Action ConsumableUsed;
        public event Action QuickGrenadeUsed;
        public event Action InventoryToggled;
        public event Action PauseToggled;
#pragma warning restore CS0067

        /// <summary>Applies an intent; out-of-order (older sequence) intents are ignored.</summary>
        public bool Apply(in MovementIntent intent)
        {
            if (!intent.IsValid) return false;
            if (AppliedIntents > 0 && intent.Sequence <= LastSequence) return false;
            Move = intent.Move;
            if (intent.Aim.sqrMagnitude > 0.0001f) Aim = intent.Aim;
            FireHeld = intent.FireHeld;
            SpecialHeld = intent.SpecialHeld;
            InteractHeld = intent.InteractHeld;
            LastSequence = intent.Sequence;
            AppliedIntents++;
            return true;
        }

        private uint _lastCommandSequence;
        private bool _anyCommand;
        public int AppliedCommands { get; private set; }
        public int RejectedCommands { get; private set; }

        /// <summary>
        /// Applies an edge command exactly once (sequence-deduplicated): raises the same IPlayerInputReader event the
        /// local components already listen to, so reload/slot/consumable logic has one implementation.
        /// </summary>
        public bool ApplyCommand(in WeaponCommand command)
        {
            if (_anyCommand && command.Sequence <= _lastCommandSequence)
            {
                RejectedCommands++;
                return false;
            }

            _anyCommand = true;
            _lastCommandSequence = command.Sequence;
            AppliedCommands++;
            switch (command.Kind)
            {
                case WeaponCommandKind.Reload: Reload?.Invoke(); break;
                case WeaponCommandKind.SelectPrimary: Weapon1Selected?.Invoke(); break;
                case WeaponCommandKind.SelectSecondary: Weapon2Selected?.Invoke(); break;
                case WeaponCommandKind.Swap: WeaponSwapped?.Invoke(); break;
                case WeaponCommandKind.UseConsumable: ConsumableUsed?.Invoke(); break;
                case WeaponCommandKind.Interact: Interact?.Invoke(); break;
            }

            return true;
        }

        /// <summary>
        /// 85: the member's connection dropped (or came back under a new one). The held character stands still with
        /// nothing pressed, and the sequence windows restart so the reconnected owner's fresh intents and commands are
        /// accepted instead of being rejected as older than the previous connection's.
        /// </summary>
        public void Reset()
        {
            Move = Vector2.zero;
            FireHeld = false;
            SpecialHeld = false;
            InteractHeld = false;
            LastSequence = 0;
            AppliedIntents = 0;
            _lastCommandSequence = 0;
            _anyCommand = false;
            Resets++;
        }

        public int Resets { get; private set; }

        public void SetHeld(bool fire, bool special, bool interact = false)
        {
            FireHeld = fire;
            SpecialHeld = special;
            InteractHeld = interact;
        }

        public void Enable()
        {
        }

        public void Disable()
        {
        }
    }

    /// <summary>
    /// Host-side dash validation: one accepted request produces exactly one authoritative dash through PlayerDash
    /// (0.18 s movement, 0.10 s iFrames, 1.25 s cooldown live in one place). Duplicate or stale sequences, cooldown,
    /// an active dash and zero directions are rejected without side effects.
    /// </summary>
    public sealed class HostDashValidator
    {
        private readonly PlayerDash _dash;
        private uint _lastSequence;
        private bool _any;

        public HostDashValidator(PlayerDash dash)
        {
            _dash = dash != null ? dash : throw new ArgumentNullException(nameof(dash));
        }

        public uint LastAcceptedSequence { get; private set; }
        public int Accepted { get; private set; }
        public int Rejected { get; private set; }

        /// <summary>A new connection starts its dash sequence again (85 reconnect).</summary>
        public void Reset()
        {
            _any = false;
            _lastSequence = 0;
        }

        public DashVerdict Validate(in DashRequest request)
        {
            if (_any && request.Sequence == _lastSequence) return Reject(DashVerdict.RejectedDuplicate);
            if (_any && request.Sequence < _lastSequence) return Reject(DashVerdict.RejectedStale);
            _any = true;
            _lastSequence = request.Sequence;
            if (request.Direction.sqrMagnitude < 0.0001f || float.IsNaN(request.Direction.x) || float.IsNaN(request.Direction.y)) return Reject(DashVerdict.RejectedDirection);
            if (_dash.IsDashing) return Reject(DashVerdict.RejectedAlreadyDashing);
            if (!_dash.CanDash) return Reject(DashVerdict.RejectedCooldown);
            if (!_dash.TryStartDash(request.Direction)) return Reject(DashVerdict.RejectedCooldown);
            LastAcceptedSequence = request.Sequence;
            Accepted++;
            return DashVerdict.Accepted;
        }

        private DashVerdict Reject(DashVerdict verdict)
        {
            Rejected++;
            return verdict;
        }
    }

    /// <summary>
    /// Owner reconciliation policy: the owner predicts with the same components from its own input (responsive), the
    /// host's state is the truth. Small drift (below tolerance) is ignored; larger drift snaps the owner to the host.
    /// Deliberately no rewind/replay (82 "Feel": small PvE game, no competitive-grade prediction).
    /// </summary>
    public static class OwnerReconciliation
    {
        /// <summary>V1 FINAL (TASK 179) tolerance in tiles before the owner snaps to the authoritative position.</summary>
        public const float SnapToleranceTiles = 0.75f;

        public static bool NeedsCorrection(Vector2 predicted, Vector2 authoritative, float tolerance = SnapToleranceTiles)
        {
            return (predicted - authoritative).sqrMagnitude > tolerance * tolerance;
        }

        public static Vector2 Reconcile(Vector2 predicted, Vector2 authoritative, float tolerance = SnapToleranceTiles)
        {
            return NeedsCorrection(predicted, authoritative, tolerance) ? authoritative : predicted;
        }
    }

    /// <summary>
    /// Replica rendering policy: buffered interpolation of authoritative states a fixed delay behind the newest sample;
    /// a jump larger than the snap distance (dash, teleport) is not smoothed. Replicas never run input logic.
    /// </summary>
    public sealed class ReplicaInterpolator
    {
        /// <summary>V1 FINAL (TASK 179) render delay (seconds) — two 50 ms server ticks of buffer.</summary>
        public const double DefaultDelaySeconds = 0.1;
        public const float SnapDistanceTiles = 3f;

        private readonly List<PlayerNetState> _buffer = new();
        private readonly double _delay;

        public ReplicaInterpolator(double delaySeconds = DefaultDelaySeconds)
        {
            _delay = delaySeconds;
        }

        public int BufferedSamples => _buffer.Count;
        public PlayerNetState Latest => _buffer.Count > 0 ? _buffer[_buffer.Count - 1] : default;

        public void Push(in PlayerNetState state)
        {
            if (_buffer.Count > 0 && state.Time <= _buffer[_buffer.Count - 1].Time) return;
            _buffer.Add(state);
            while (_buffer.Count > 32) _buffer.RemoveAt(0);
        }

        /// <summary>Interpolated state for render time (now − delay). Aim/facing/dash flags come from the newer sample.</summary>
        public PlayerNetState Sample(double now)
        {
            if (_buffer.Count == 0) return default;
            var renderTime = now - _delay;
            if (renderTime <= _buffer[0].Time) return _buffer[0];
            for (var i = 1; i < _buffer.Count; i++)
            {
                var previous = _buffer[i - 1];
                var next = _buffer[i];
                if (renderTime > next.Time) continue;
                var span = next.Time - previous.Time;
                var t = span <= 0 ? 1f : (float)((renderTime - previous.Time) / span);
                var result = next;
                var jump = Vector2.Distance(previous.Position, next.Position);
                result.Position = jump > SnapDistanceTiles ? next.Position : Vector2.Lerp(previous.Position, next.Position, t);
                result.Velocity = Vector2.Lerp(previous.Velocity, next.Velocity, t);
                result.Time = renderTime;
                return result;
            }

            return Latest;
        }
    }
}
