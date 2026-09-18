using System;
using System.Collections.Generic;
using RuinRail.Core.Input;
using RuinRail.Gameplay.Combat;
using UnityEngine;

namespace RuinRail.Gameplay.Player
{
    public enum ReviveCancelReason
    {
        None,
        Released,
        MovedAway,
        Attacked,
        Dashed,
        TookDamage,
        ReviverCannotAct,
        TargetNotDowned
    }

    /// <summary>One in-progress standard revive: exactly one reviver on exactly one Downed target.</summary>
    public sealed class ReviveChannel
    {
        public ReviveChannel(PlayerReviver reviver, PlayerLifeStateComponent target, float duration)
        {
            Reviver = reviver;
            Target = target;
            Duration = Mathf.Max(0.01f, duration);
        }

        public PlayerReviver Reviver { get; }
        public PlayerLifeStateComponent Target { get; }
        public float Duration { get; }
        public float Elapsed { get; internal set; }
        public float Progress => Mathf.Clamp01(Elapsed / Duration);
        public bool IsComplete => Elapsed >= Duration;
    }

    /// <summary>
    /// Host arbitration of standard revives (84): one channel per target and per reviver, so two players holding
    /// Interact on the same Downed teammate can never double-complete; success is applied exactly once
    /// (30% Max HP via the life-state seam, then revive protection) and the channel is gone before anyone else
    /// could finish it. Clients never arbitrate (DamageAuthority).
    /// </summary>
    public sealed class ReviveArbiter
    {
        private readonly List<ReviveChannel> _channels = new();

        public IReadOnlyList<ReviveChannel> Channels => _channels;
        public int Completed { get; private set; }

        public event Action<ReviveChannel> ChannelStarted;
        public event Action<ReviveChannel, ReviveCancelReason> ChannelCancelled;
        public event Action<ReviveChannel> ChannelCompleted;

        public bool IsActive(ReviveChannel channel) => channel != null && _channels.Contains(channel);

        public ReviveChannel ChannelFor(PlayerReviver reviver)
        {
            foreach (var channel in _channels) if (channel.Reviver == reviver) return channel;
            return null;
        }

        public ReviveChannel ChannelOn(PlayerLifeStateComponent target)
        {
            foreach (var channel in _channels) if (channel.Target == target) return channel;
            return null;
        }

        /// <summary>Starts a channel if the target is Downed, the reviver can act and neither is already busy.</summary>
        public ReviveChannel TryBegin(PlayerReviver reviver, PlayerLifeStateComponent target, float duration)
        {
            if (!DamageAuthority.LocalIsAuthoritative || reviver == null || target == null) return null;
            if (!target.IsDowned || !reviver.CanRevive) return null;
            if (ChannelFor(reviver) != null || ChannelOn(target) != null) return null;
            var channel = new ReviveChannel(reviver, target, duration);
            _channels.Add(channel);
            ChannelStarted?.Invoke(channel);
            return channel;
        }

        public bool Cancel(ReviveChannel channel, ReviveCancelReason reason)
        {
            if (channel == null || !_channels.Remove(channel)) return false;
            ChannelCancelled?.Invoke(channel, reason);
            return true;
        }

        /// <summary>Advances one channel; validity is re-checked every tick and completion applies once.</summary>
        public void Advance(ReviveChannel channel, float deltaTime)
        {
            if (channel == null || !_channels.Contains(channel) || !DamageAuthority.LocalIsAuthoritative) return;
            if (!channel.Target.IsDowned)
            {
                Cancel(channel, ReviveCancelReason.TargetNotDowned);
                return;
            }

            if (!channel.Reviver.CanRevive)
            {
                Cancel(channel, ReviveCancelReason.ReviverCannotAct);
                return;
            }

            channel.Elapsed += Mathf.Max(0f, deltaTime);
            if (!channel.IsComplete) return;

            // Remove first: nothing can complete this channel twice, whatever the callbacks do.
            _channels.Remove(channel);
            var balance = channel.Reviver.Balance;
            var health = channel.Target.GetComponent<HealthComponent>();
            var percent = balance != null ? balance.ReviveHealthPercent : 30;
            var restored = Mathf.Max(1, Mathf.RoundToInt((health != null ? health.MaxHealth : 1) * percent / 100f));
            if (channel.Target.ReturnToAlive(restored))
            {
                var protection = channel.Target.GetComponent<ReviveProtection>();
                if (protection != null) protection.Begin(balance != null ? balance.ReviveProtectionSeconds : 1.5f);
                Completed++;
                ChannelCompleted?.Invoke(channel);
            }
            else
            {
                ChannelCancelled?.Invoke(channel, ReviveCancelReason.TargetNotDowned);
            }
        }
    }

}
