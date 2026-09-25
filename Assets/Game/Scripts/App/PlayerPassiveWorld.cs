using System;
using System.Collections.Generic;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Area;
using RuinRail.Gameplay.Combat.Impact;
using RuinRail.Gameplay.Items.Passives;
using RuinRail.Gameplay.Player;
using UnityEngine;

namespace RuinRail.App
{
    /// <summary>
    /// The world side of the Legendary passives' <see cref="PassiveWorldActions"/> (items/34). The requests existed and
    /// Emergency Vent, Discharge, Arc Stagger and Room Sweep issued them, but no composition ever supplied them, so every
    /// passive context fell back to <see cref="PassiveWorldActions.None"/> and the effects did nothing.
    ///
    /// One instance per passive context (the player's rig, and the host's copy of each co-op member). Everything is
    /// resolved from the body at call time through the existing combat seams, so a co-op client's pulse and shockwave
    /// reach the host only as the ordinary validated hit/impact requests, and the host-resolved ones (Arc Stagger,
    /// Room Sweep) act on the host's authoritative world directly.
    /// </summary>
    public sealed class PlayerPassiveWorld
    {
        private readonly GameObject _body;
        private readonly Func<IEnumerable<GameObject>> _groundPickups;
        private readonly IDamageRoller _roller = new UnityRandomDamageRoller();

        public PlayerPassiveWorld(GameObject body, Func<IEnumerable<GameObject>> groundPickups)
        {
            _body = body;
            _groundPickups = groundPickups;
            Actions = new PassiveWorldActions
            {
                AreaDamage = AreaDamage,
                Shockwave = Shockwave,
                PullCoinAndAmmoPickups = PullCoinAndAmmoPickups
            };
        }

        public PassiveWorldActions Actions { get; }

        /// <summary>Diagnostics / proof: requests executed and targets they reached.</summary>
        public int AreaPulses { get; private set; }
        public int AreaTargetsHit { get; private set; }
        public int Shockwaves { get; private set; }
        public int ShockwaveTargets { get; private set; }
        public int Sweeps { get; private set; }
        public int SweptPickups { get; private set; }

        private IImpactAttackerFeedback Feedback => _body != null ? _body.GetComponent<PlayerImpactReceiver>() : null;

        private void AreaDamage(float radius, int min, int max)
        {
            if (_body == null) return;
            AreaPulses++;
            var centre = (Vector2)_body.transform.position;
            var feedback = Feedback;
            foreach (var target in AreaDamageResolver.CollectTargets(centre, radius, DamageTeam.Player))
            {
                if (target is not Component struck || (target is HealthComponent health && !health.IsAlive)) continue;
                // Outward, never zero: a directionless Normal hit is what the host reads as a melee kill.
                var direction = Outward(centre, struck);
                if (!target.TryApplyDamage(new DamageRequest(_roller.Roll(min, max), DamageKind.Normal, 0f, direction))) continue;
                AreaTargetsHit++;
                var struckHealth = struck.GetComponentInParent<HealthComponent>();
                if (feedback != null && struckHealth != null && !struckHealth.IsAlive) feedback.OnTargetKilled(false);
            }
        }

        private void Shockwave(float radius, float knockback, float staggerPower)
        {
            if (_body == null) return;
            Shockwaves++;
            ShockwaveTargets += ShockwaveResolver.Emit(_body.transform.position, radius, knockback, staggerPower, DamageTeam.Player, _body, Feedback);
        }

        private void PullCoinAndAmmoPickups()
        {
            if (_body == null || _groundPickups == null) return;
            var attractor = _body.GetComponent<PickupAttractor>();
            var relay = _body.GetComponent<PlayerRoomEventsRelay>();
            var room = relay != null ? relay.LastClearedRoom : null;
            if (attractor == null || room == null) return;
            Sweeps++;
            var bounds = room.InteriorWorldBounds;
            var inRoom = new List<GameObject>();
            foreach (var go in _groundPickups())
            {
                if (go != null && bounds.Contains(go.transform.position)) inRoom.Add(go);
            }

            SweptPickups += attractor.SweepAll(inRoom);
        }

        private static Vector2 Outward(Vector2 centre, Component struck)
        {
            var direction = (Vector2)struck.transform.position - centre;
            return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.up;
        }
    }
}
