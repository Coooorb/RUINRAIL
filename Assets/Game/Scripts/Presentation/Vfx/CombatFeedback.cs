using System;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Area;
using RuinRail.Gameplay.Combat.Impact;
using RuinRail.Gameplay.Combat.Projectiles;
using RuinRail.Gameplay.Combat.Weapons;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Items.Consumables;
using RuinRail.Gameplay.Loot;
using RuinRail.Presentation.Animation;
using RuinRail.Core.Rendering;
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
        private readonly List<(ThrownGrenade grenade, PooledEffect visual)> _grenades = new();
        // Another peer's throws: presentation-only flights (no ThrownGrenade, no zone, no damage) and their lasting areas.
        private sealed class RemoteFlight
        {
            public GrenadeData Data;
            public Vector2 Origin;
            public Vector2 Landing;
            public float Flight;
            public float Elapsed;
            public PooledEffect Visual;
        }

        private readonly List<RemoteFlight> _remoteFlights = new();
        private readonly List<(PooledEffect effect, string kind)> _remoteAreas = new();
        public int RemoteAreasShowing => _remoteAreas.Count(a => a.effect != null && a.effect.IsActive && a.effect.Kind == a.kind);
        public int RemoteGrenadesShown { get; private set; }
        public int RemoteFlightsInAir => _remoteFlights.Count;
        private EffectPool _groundPool;

        public FeedbackConfig Config => _config;
        public EffectPool Pool => _pool;
        public CameraShake Shake => _shake;
        public IReadOnlyDictionary<string, int> Counts => _counts;
        public int CountOf(string kind) => _counts.TryGetValue(kind, out var c) ? c : 0;

        public void Configure(FeedbackConfig config, EffectPool pool, CameraShake shake, EffectPool groundPool = null)
        {
            _config = config;
            _pool = pool;
            _shake = shake;
            _groundPool = groundPool;
        }

        // ---- Effects (pooled; explicit placeholder sprites) ----

        private PooledEffect Play(string kind, Vector2 position, float seconds, Color color, float scale, float rotation = 0f)
        {
            _counts[kind] = CountOf(kind) + 1;
            return _pool != null ? _pool.Spawn(kind, position, seconds, color, scale, rotation) : null;
        }

        public void MuzzleFlash(Vector2 position, Vector2 direction) => Play("muzzle", position, _config.MuzzleFlashSeconds, new Color(1f, 0.95f, 0.6f), 0.75f, Angle(direction));
        public void Impact(Vector2 position, bool onTarget) => Play("impact", position, _config.ImpactSeconds, onTarget ? new Color(1f, 0.8f, 0.5f) : new Color(0.8f, 0.8f, 0.8f), 0.5f);

        // ---- Shot feel: the weapon's projectile profile names its muzzle flash, impact and kick (presentation only) ----

        /// <summary>An effect drawn at its sprite's native pixel size (no resampling on the pixel-perfect camera).</summary>
        private PooledEffect PlayNative(string kind, Vector2 position, float seconds, Color color, float rotation)
        {
            var effect = Play(kind, position, seconds, color, 1f, rotation);
            if (effect != null) effect.transform.localScale = Vector3.one;
            return effect;
        }

        /// <summary>The flash where a shot really left (the solved spawn point), in the shot's direction, sized by its profile.</summary>
        public void MuzzleFlash(ProjectileVisualCatalog.Profile profile, Vector2 position, Vector2 direction)
        {
            var kind = profile != null && !string.IsNullOrEmpty(profile.MuzzleKind) ? profile.MuzzleKind : null;
            if (kind == null) { MuzzleFlash(position, direction); return; }
            // "muzzle" counts every shot's flash (whatever kind it draws), like "impact" counts every landing.
            if (kind != "muzzle") _counts["muzzle"] = CountOf("muzzle") + 1;
            if (kind == NoEffect) return;
            PlayNative(kind, position, profile.MuzzleSeconds > 0f ? profile.MuzzleSeconds : _config.MuzzleFlashSeconds, Color.white, Angle(direction));
        }

        /// <summary>
        /// A player shot landing: on a target the profile's full burst facing back along the shot, on a wall a dimmer,
        /// shorter puff of the same kind (the environment answers, the enemy hit reads first).
        /// </summary>
        public void ShotImpact(ProjectileVisualCatalog.Profile profile, Vector2 position, Vector2 direction, bool onTarget)
        {
            var kind = profile != null && !string.IsNullOrEmpty(profile.ImpactKind) ? profile.ImpactKind : null;
            if (kind == null || kind == NoEffect) { Impact(position, onTarget); return; }
            if (kind != "impact") _counts["impact"] = CountOf("impact") + 1;
            var seconds = profile.ImpactSeconds > 0f ? profile.ImpactSeconds : _config.ImpactSeconds;
            PlayNative(kind, position, onTarget ? seconds : seconds * 0.75f, onTarget ? Color.white : new Color(0.82f, 0.8f, 0.76f, 0.9f), Angle(-direction));
        }

        /// <summary>Profile value that turns a stage of the stack off (a bow has no muzzle flash).</summary>
        public const string NoEffect = "none";

        private void OnAnyImpacted(Projectile projectile, Vector2 at, bool onTarget)
        {
            if (projectile == null || projectile.Data.SourceTeam != DamageTeam.Player) return; // hostile shots keep their own read
            var profile = ProjectileVisualCatalog.Active?.Resolve(projectile.Data.VisualId, DamageTeam.Player);
            ShotImpact(profile, at, projectile.Data.Direction, onTarget);
        }

        private void OnAnyExploded(Projectile projectile, Vector2 at)
        {
            if (projectile == null) return;
            Explosion(at, projectile.Data.ExplosionRadius);
            _shake?.Request(ShakeKind.Explosion);
        }

        private bool _projectileHooks;

        /// <summary>Every pooled projectile's impacts and detonations (the pools recycle instances, so per-instance hooks would miss shots).</summary>
        public CombatFeedback ObserveAllProjectiles()
        {
            if (_projectileHooks) return this;
            _projectileHooks = true;
            Projectile.AnyImpacted += OnAnyImpacted;
            Projectile.AnyExploded += OnAnyExploded;
            _unsubscribe.Add(() => { Projectile.AnyImpacted -= OnAnyImpacted; Projectile.AnyExploded -= OnAnyExploded; _projectileHooks = false; });
            return this;
        }
        /// <summary>art/104: large enough to feel powerful, short-lived so hazards/projectiles are not hidden for seconds.</summary>
        public void Explosion(Vector2 position, float radiusTiles)
        {
            // A radius-true blast sheet exists for this radius (the rockets): drawn at native pixels, rim on the real radius.
            var blast = "explosion_r" + Mathf.RoundToInt(radiusTiles * SortingConvention.PixelsPerUnit);
            if (_pool != null && _pool.HasArtFor(blast))
            {
                _counts["explosion"] = CountOf("explosion") + 1;
                PlayNative(blast, position, _config.ExplosionSeconds, Color.white, 0f);
                return;
            }

            Play("explosion", position, _config.ExplosionSeconds, new Color(1f, 0.6f, 0.2f, 0.85f), Mathf.Max(1f, radiusTiles * 2f) * 4f);
        }
        public void MeleeArc(Vector2 position, Vector2 direction, float arcDegrees, float reachTiles) => Play("melee", position + direction.normalized * (reachTiles * 0.5f), _config.MeleeArcSeconds, new Color(0.9f, 0.95f, 1f, 0.8f), Mathf.Max(0.5f, reachTiles) * 4f, Angle(direction));
        public void Stagger(Vector2 position) => Play("stagger", position + Vector2.up * 0.6f, _config.StaggerSeconds, new Color(1f, 0.85f, 0.4f), 0.6f);
        public void Heal(Vector2 position) => Play("heal", position + Vector2.up * 0.4f, _config.HealSeconds, new Color(0.55f, 1f, 0.55f, 0.8f), 1f);
        // ---- Consumable world effects (items/31): drawn from the grenade's own data, so size and time are the gameplay's ----

        /// <summary>Longest a thrown grenade can be in the air before it lands (its visual is returned on landing).</summary>
        private const float GrenadeFlightCapSeconds = 5f;

        private PooledEffect PlayArea(EffectPool pool, string kind, Vector2 position, float seconds, Color color, float radiusTiles)
        {
            _counts[kind] = CountOf(kind) + 1;
            var effect = pool != null ? pool.Spawn(kind, position, seconds, color) : null;
            effect?.SetWorldSize(Vector2.one * (radiusTiles * 2f));
            return effect;
        }

        /// <summary>
        /// What a grenade shows where it lands: the blast at its real radius, and the lasting area for exactly its duration.
        /// <paramref name="lateSeconds"/> is how long ago it actually landed (a late co-op message): a burst that is already
        /// over is not drawn, and a lasting area shows only what is left of it. Returns the lasting area's effect, if any.
        /// </summary>
        public PooledEffect GrenadeLanded(GrenadeData data, Vector2 at, float lateSeconds = 0f)
        {
            var burstOver = lateSeconds > _config.ExplosionSeconds;
            switch (data.Kind)
            {
                case GrenadeEffectKind.Frag:
                    if (burstOver) return null;
                    Explosion(at, data.RadiusTiles);
                    _shake?.Request(ShakeKind.Explosion);
                    return null;
                case GrenadeEffectKind.Shock:
                    if (burstOver) return null;
                    PlayArea(_pool, "shock", at, _config.ExplosionSeconds, Color.white, data.RadiusTiles);
                    _shake?.Request(ShakeKind.Explosion);
                    return null;
                case GrenadeEffectKind.Incendiary:
                {
                    if (!burstOver)
                    {
                        Explosion(at, data.RadiusTiles);
                        _shake?.Request(ShakeKind.Explosion);
                    }

                    // Burning ground sits under the actors (a hazard, not a cloud); it lasts the burn's whole-second ticks.
                    var left = Mathf.Max(0f, Mathf.Round(data.BurnDurationSeconds)) - lateSeconds;
                    return left > 0.05f ? PlayArea(_groundPool != null ? _groundPool : _pool, "fire_zone", at, left, Color.white, data.RadiusTiles) : null;
                }
                case GrenadeEffectKind.Smoke:
                {
                    // A translucent cloud over the area: dithered art plus alpha keeps actors inside readable.
                    var left = data.SmokeDurationSeconds - lateSeconds;
                    return left > 0.05f ? PlayArea(_pool, "smoke_cloud", at, left, new Color(1f, 1f, 1f, 0.82f), data.RadiusTiles) : null;
                }
            }

            return null;
        }

        /// <summary>
        /// Another peer's grenade (co-op): drawn from the host-validated throw — origin, the host's landing and how long
        /// ago it left — flying at the grenade's real speed, then the same landing effect a local throw shows. It is
        /// presentation only: no grenade, no zone and no damage exist for it on this peer.
        /// </summary>
        public void ShowRemoteGrenade(GrenadeData data, Vector2 origin, Vector2 landing, float elapsedSeconds)
        {
            RemoteGrenadesShown++;
            var flight = data.ThrowSpeed > 0.01f ? Vector2.Distance(origin, landing) / data.ThrowSpeed : 0f;
            var elapsed = Mathf.Max(0f, elapsedSeconds);
            if (elapsed >= flight)
            {
                TrackRemoteArea(GrenadeLanded(data, landing, elapsed - flight));
                return;
            }

            _counts["grenade"] = CountOf("grenade") + 1;
            var visual = _pool != null ? _pool.Spawn("grenade", Vector2.Lerp(origin, landing, elapsed / flight), GrenadeFlightCapSeconds, Color.white, 1f) : null;
            visual?.SetWorldSize(Vector2.one * 0.375f);
            _remoteFlights.Add(new RemoteFlight { Data = data, Origin = origin, Landing = landing, Flight = flight, Elapsed = elapsed, Visual = visual });
        }

        private void TrackRemoteArea(PooledEffect area)
        {
            if (area != null) _remoteAreas.Add((area, area.Kind));
        }

        /// <summary>A new depth (or the run's end): other peers' grenades in the air and their lasting areas are cleared.</summary>
        public void ClearRemoteGrenades()
        {
            foreach (var flight in _remoteFlights) if (flight.Visual != null && flight.Visual.IsActive) _pool?.Return(flight.Visual);
            _remoteFlights.Clear();
            foreach (var (area, kind) in _remoteAreas)
            {
                if (area == null || !area.IsActive || area.Kind != kind) continue; // recycled into something else meanwhile
                if (kind == "fire_zone" && _groundPool != null) _groundPool.Return(area); else _pool?.Return(area);
            }

            _remoteAreas.Clear();
        }

        /// <summary>A thrower's grenades: the canister is drawn along its real flight, then its landing effect plays once.</summary>
        public CombatFeedback Attach(GrenadeLauncher launcher)
        {
            if (launcher == null) return this;
            Action<ThrownGrenade> thrown = grenade =>
            {
                if (grenade == null) return;
                _counts["grenade"] = CountOf("grenade") + 1;
                var visual = _pool != null ? _pool.Spawn("grenade", grenade.transform.position, GrenadeFlightCapSeconds, Color.white, 1f) : null;
                visual?.SetWorldSize(Vector2.one * 0.375f);
                _grenades.Add((grenade, visual));
                grenade.Resolved += (g, _) =>
                {
                    var i = _grenades.FindIndex(e => e.grenade == g);
                    if (i >= 0) { if (_grenades[i].visual != null && _grenades[i].visual.IsActive) _pool?.Return(_grenades[i].visual); _grenades.RemoveAt(i); }
                    GrenadeLanded(g.Data, g.LandingPoint);
                };
            };
            launcher.Thrown += thrown;
            _unsubscribe.Add(() => launcher.Thrown -= thrown);
            return this;
        }

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

        /// <summary>The active weapon's last solved shot (spawn point and direction) and its definition.</summary>
        private static (ShotSolution? shot, WeaponDefinition weapon) LastShotOf(IEquippableWeapon weapon) => weapon switch
        {
            RangedWeapon r => (r.LastShot, r.Definition),
            BlasterWeapon b => (b.LastShot, b.Definition),
            BowWeapon b => (b.LastShot, b.Definition),
            _ => (null, null)
        };

        public static ShakeKind ShakeFor(IEquippableWeapon weapon) => weapon switch
        {
            RangedWeapon r when r.Definition != null && r.Definition.IsExplosive => ShakeKind.Explosion,
            RangedWeapon r when r.Definition != null && r.Definition.ProjectilesPerShot > 1 => ShakeKind.Shotgun,
            _ => ShakeKind.SmallGun
        };

        public void Tick(float deltaTime)
        {
            for (var i = _grenades.Count - 1; i >= 0; i--)
            {
                var (grenade, visual) = _grenades[i];
                if (grenade == null || visual == null || !visual.IsActive) { _grenades.RemoveAt(i); continue; }
                visual.transform.position = grenade.transform.position;
            }

            for (var i = _remoteFlights.Count - 1; i >= 0; i--)
            {
                var flight = _remoteFlights[i];
                flight.Elapsed += deltaTime;
                if (flight.Elapsed < flight.Flight)
                {
                    if (flight.Visual != null && flight.Visual.IsActive) flight.Visual.transform.position = Vector2.Lerp(flight.Origin, flight.Landing, flight.Elapsed / flight.Flight);
                    continue;
                }

                _remoteFlights.RemoveAt(i);
                if (flight.Visual != null && flight.Visual.IsActive) _pool?.Return(flight.Visual);
                TrackRemoteArea(GrenadeLanded(flight.Data, flight.Landing, flight.Elapsed - flight.Flight));
            }

            _remoteAreas.RemoveAll(a => a.effect == null || !a.effect.IsActive || a.effect.Kind != a.kind);

            for (var i = 0; i < _weapons.Count; i++)
            {
                var (driver, shots, swings) = _weapons[i];
                if (driver == null) continue;
                if (driver.ShotsShown > shots)
                {
                    var (shot, weapon) = LastShotOf(driver.ActiveWeapon);
                    if (shot.HasValue)
                        MuzzleFlash(ProjectileVisualCatalog.Active?.Find(ProjectileVisualCatalog.ResolveWeaponVisualId(weapon)), shot.Value.SpawnPosition, shot.Value.Direction);
                    else MuzzleFlash(driver.transform.position, driver.transform.right);
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
