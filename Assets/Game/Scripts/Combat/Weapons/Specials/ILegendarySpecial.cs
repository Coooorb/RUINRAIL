using System;
using RuinRail.Gameplay.Combat.Impact;
using RuinRail.Gameplay.Combat.Projectiles;
using UnityEngine;

namespace RuinRail.Gameplay.Combat.Weapons.Specials
{
    /// <summary>
    /// Everything a special may touch when it fires: the wielder, the aim, the projectile pool and the damage roller.
    /// Specials use fixed catalog damage (25: the same named Legendary always has the same Special), so no weapon affix
    /// multipliers are applied here; only the attacker-side impact feedback rides along for wearer effects.
    /// </summary>
    public sealed class SpecialContext
    {
        public SpecialContext(GameObject owner, Rigidbody2D body, Func<Vector2> aimDirection, Func<Vector2> spawnPosition, ProjectilePool pool, IDamageRoller roller,
            DamageTeam sourceTeam = DamageTeam.Player, IImpactAttackerFeedback feedback = null)
        {
            Owner = owner;
            Body = body;
            AimDirectionProvider = aimDirection ?? (() => Vector2.right);
            SpawnPositionProvider = spawnPosition ?? (() => owner != null ? (Vector2)owner.transform.position : Vector2.zero);
            Pool = pool;
            Roller = roller ?? new UnityRandomDamageRoller();
            SourceTeam = sourceTeam;
            Feedback = feedback;
        }

        public GameObject Owner { get; }
        public Rigidbody2D Body { get; }
        public Func<Vector2> AimDirectionProvider { get; }
        public Func<Vector2> SpawnPositionProvider { get; }
        public ProjectilePool Pool { get; }
        public IDamageRoller Roller { get; }
        public DamageTeam SourceTeam { get; }
        public IImpactAttackerFeedback Feedback { get; }

        public Vector2 AimDirection
        {
            get
            {
                var aim = AimDirectionProvider();
                return aim.sqrMagnitude > 0.0001f ? aim.normalized : Vector2.right;
            }
        }

        public Vector2 SpawnPosition => SpawnPositionProvider();
    }

    /// <summary>A special that is still running (multi-frame bursts, dashes). Ticked by the controller until complete.</summary>
    public interface ISpecialExecution
    {
        bool IsComplete { get; }

        /// <summary>True while the execution owns the wielder's body (dash/charge specials).</summary>
        bool LocksMovement { get; }
        void Tick(float deltaTime);
    }

    /// <summary>
    /// Fixed RMB Legendary special (25_LEGENDARY_WEAPON_SPECIALS): cooldown-only, no resource of its own, one per named
    /// Legendary. Implementations are reusable primitives parameterised by data, never a switch on a weapon id.
    /// </summary>
    public interface ILegendarySpecial
    {
        string Id { get; }
        float CooldownSeconds { get; }

        /// <summary>Starts the special. Returns the running execution, or null when the effect completed instantly.</summary>
        ISpecialExecution Begin(SpecialContext context);
    }

    /// <summary>Per-weapon-instance cooldown state: keeps ticking while holstered (25: one state per Legendary weapon).</summary>
    public sealed class LegendarySpecialState
    {
        public LegendarySpecialState(float cooldownSeconds)
        {
            CooldownSeconds = Mathf.Max(0f, cooldownSeconds);
        }

        public float CooldownSeconds { get; }
        public float CooldownRemaining { get; private set; }
        public bool IsReady => CooldownRemaining <= 0f;
        public int Uses { get; private set; }

        public void Tick(float deltaTime)
        {
            if (deltaTime > 0f && CooldownRemaining > 0f) CooldownRemaining = Mathf.Max(0f, CooldownRemaining - deltaTime);
        }

        /// <summary>Consumes the ready state; false (and nothing changes) while cooling down.</summary>
        public bool TryUse()
        {
            if (!IsReady) return false;
            CooldownRemaining = CooldownSeconds;
            Uses++;
            return true;
        }

        public void Reset()
        {
            CooldownRemaining = 0f;
        }
    }
}
