using System;
using System.Collections.Generic;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Impact;
using RuinRail.Gameplay.Combat.Projectiles;
using RuinRail.Gameplay.Combat.Weapons;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Items.Consumables;
using RuinRail.Gameplay.Loot;
using RuinRail.Presentation.Animation;
using UnityEngine;

namespace RuinRail.Presentation.Vfx
{
    /// <summary>
    /// art/104 layered feedback over authoritative gameplay events: muzzle flash, impact, explosion, melee arc, stagger,
    /// heal, status and Legendary-drop glow — all pooled placeholder sprites (art BLOCKED_EXTERNAL_ASSET) — plus the
    /// controlled screen shake. Every hook is a read-only subscription; nothing here applies damage or changes state.
    /// </summary>
    public sealed class CombatFeedback : MonoBehaviour
    {
        [SerializeField] private FeedbackConfig _config;
        [SerializeField] private EffectPool _pool;
        [SerializeField] private CameraShake _shake;

        private readonly List<Action> _unsubscribe = new();
        private readonly List<(WeaponVisualDriver driver, int shots, int swings)> _weapons = new();
        private readonly Dictionary<WorldItemPickup, PooledEffect> _glows = new();
        private readonly Dictionary<string, int> _counts = new();

        public FeedbackConfig Config => _config;
        public EffectPool Pool => _pool;
        public CameraShake Shake => _shake;
        public IReadOnlyDictionary<string, int> Counts => _counts;
        public int CountOf(string kind) => _counts.TryGetValue(kind, out var c) ? c : 0;

        public void Configure(FeedbackConfig config, EffectPool pool, CameraShake shake)
        {
            _config = config;
            _pool = pool;
            _shake = shake;
        }

        // ---- Effects (pooled; explicit placeholder sprites) ----

        private PooledEffect Play(string kind, Vector2 position, float seconds, Color color, float scale, float rotation = 0f)
        {
            _counts[kind] = CountOf(kind) + 1;
            return _pool != null ? _pool.Spawn(kind, position, seconds, color, scale, rotation) : null;
        }

        public void MuzzleFlash(Vector2 position, Vector2 direction) => Play("muzzle", position, _config.MuzzleFlashSeconds, new Color(1f, 0.95f, 0.6f), 0.75f, Angle(direction));
        public void Impact(Vector2 position, bool onTarget) => Play("impact", position, _config.ImpactSeconds, onTarget ? new Color(1f, 0.8f, 0.5f) : new Color(0.8f, 0.8f, 0.8f), 0.5f);
        /// <summary>art/104: large enough to feel powerful, short-lived so hazards/projectiles are not hidden for seconds.</summary>
        public void Explosion(Vector2 position, float radiusTiles) => Play("explosion", position, _config.ExplosionSeconds, new Color(1f, 0.6f, 0.2f, 0.85f), Mathf.Max(1f, radiusTiles * 2f) * 4f);
        public void MeleeArc(Vector2 position, Vector2 direction, float arcDegrees, float reachTiles) => Play("melee", position + direction.normalized * (reachTiles * 0.5f), _config.MeleeArcSeconds, new Color(0.9f, 0.95f, 1f, 0.8f), Mathf.Max(0.5f, reachTiles) * 4f, Angle(direction));
        public void Stagger(Vector2 position) => Play("stagger", position + Vector2.up * 0.6f, _config.StaggerSeconds, new Color(1f, 0.85f, 0.4f), 0.6f);
        public void Heal(Vector2 position) => Play("heal", position + Vector2.up * 0.4f, _config.HealSeconds, new Color(0.55f, 1f, 0.55f, 0.8f), 1f);
        public void Status(Vector2 position) => Play("status", position + Vector2.up * 0.9f, _config.StatusSeconds, new Color(0.6f, 0.8f, 1f, 0.8f), 0.5f);

        /// <summary>art/104: Legendary glow clearly stronger than lower rarities, never overwhelming; Common gets none.</summary>
        public float GlowScaleFor(Rarity rarity) => rarity switch
        {
            Rarity.Legendary => _config.LegendaryGlowScale,
            Rarity.Common => 0f,
            _ => _config.RareGlowScale
        };

        // ---- Hooks ----

        public CombatFeedback Attach(Projectile projectile)
        {
            if (projectile == null) return this;
            Action<Projectile, Vector2, bool> impacted = (_, p, onTarget) => Impact(p, onTarget);
            Action<Projectile, Vector2> exploded = (pr, p) => { Explosion(p, pr.Data.ExplosionRadius); _shake?.Request(ShakeKind.Explosion); };
            projectile.Impacted += impacted;
            projectile.Exploded += exploded;
            _unsubscribe.Add(() => { projectile.Impacted -= impacted; projectile.Exploded -= exploded; });
            return this;
        }

        /// <summary>Muzzle/melee effects and shake from the weapon visual driver's shot/swing counters (which read weapon state only).</summary>
        public CombatFeedback Attach(WeaponVisualDriver driver)
        {
            if (driver != null) _weapons.Add((driver, driver.ShotsShown, driver.SwingsShown));
            return this;
        }

        public CombatFeedback Attach(ImpactReceiver receiver)
        {
            if (receiver == null) return this;
            Action<ImpactReceiver> staggered = r => Stagger(r.transform.position);
            receiver.Staggered += staggered;
            _unsubscribe.Add(() => receiver.Staggered -= staggered);
            return this;
        }

        public CombatFeedback Attach(HealthComponent health)
        {
            if (health == null) return this;
            Action<int> healed = _ => Heal(health.transform.position);
            health.Healed += healed;
            _unsubscribe.Add(() => health.Healed -= healed);
            return this;
        }

        public CombatFeedback Attach(ConsumableEffectRunner effects, Transform owner)
        {
            if (effects == null) return this;
            Action<ConsumableDefinition> buff = _ => Status(owner != null ? (Vector2)owner.position : Vector2.zero);
            effects.BuffStarted += buff;
            _unsubscribe.Add(() => effects.BuffStarted -= buff);
            return this;
        }

        public CombatFeedback Attach(WorldItemPickup pickup)
        {
            if (pickup == null || pickup.Item == null || _glows.ContainsKey(pickup)) return this;
            var scale = GlowScaleFor(pickup.Item.Rarity);
            if (scale <= 0f) return this;
            var color = pickup.Item.Rarity == Rarity.Legendary ? new Color(1f, 0.75f, 0.2f, 0.7f) : new Color(0.6f, 0.75f, 1f, 0.5f);
            var glow = Play("loot_glow", pickup.transform.position, 3600f, color, scale);
            _glows[pickup] = glow;
            Action<WorldItemPickup> picked = p =>
            {
                if (_glows.TryGetValue(p, out var g)) { if (g != null && g.IsActive) _pool?.Return(g); _glows.Remove(p); }
            };
            pickup.PickedUp += picked;
            _unsubscribe.Add(() => pickup.PickedUp -= picked);
            return this;
        }

        /// <summary>Boss slams and other heavy gameplay events reported by scene wiring.</summary>
        public void ReportHeavyImpact(Vector2 position) { Explosion(position, 1f); _shake?.Request(ShakeKind.BossSlam); }

        public static ShakeKind ShakeFor(IEquippableWeapon weapon) => weapon switch
        {
            RangedWeapon r when r.Definition != null && r.Definition.IsExplosive => ShakeKind.Explosion,
            RangedWeapon r when r.Definition != null && r.Definition.ProjectilesPerShot > 1 => ShakeKind.Shotgun,
            _ => ShakeKind.SmallGun
        };

        public void Tick(float deltaTime)
        {
            for (var i = 0; i < _weapons.Count; i++)
            {
                var (driver, shots, swings) = _weapons[i];
                if (driver == null) continue;
                if (driver.ShotsShown > shots)
                {
                    var origin = driver.transform.position;
                    var direction = driver.transform.right;
                    MuzzleFlash(origin, direction);
                    _shake?.Request(ShakeFor(driver.ActiveWeapon));
                }

                if (driver.SwingsShown > swings) MeleeArc(driver.transform.position, driver.transform.right, driver.SwingArcDegrees, driver.SwingReachTiles);
                _weapons[i] = (driver, driver.ShotsShown, driver.SwingsShown);
            }
        }

        private void Update() => Tick(Time.deltaTime);

        private void OnDestroy()
        {
            foreach (var u in _unsubscribe) u();
            _unsubscribe.Clear();
        }

        private static float Angle(Vector2 direction) => direction.sqrMagnitude > 0.0001f ? Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg : 0f;
    }
}
