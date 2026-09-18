using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Projectiles;
using RuinRail.Gameplay.Combat.Weapons;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Player;
using RuinRail.Gameplay.Stats;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>Consumers read final values from the pipeline; baseline unchanged without sources.</summary>
    public class PlayerStatsWiringTests
    {
        private readonly List<Object> _created = new();
        private GameObject _player;
        private PlayerStatsBinder _binder;
        private PlayerMovement _movement;
        private PlayerDash _dash;
        private HealthComponent _health;
        private RangedWeapon _weapon;
        private FakePlayerInputReader _input;
        private PlayerBalanceConfig _config;

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<PlayerBalanceConfig>();
            _created.Add(_config);
            var caps = ScriptableObject.CreateInstance<GlobalStatCapsConfig>();
            _created.Add(caps);

            _player = new GameObject("Player");
            _created.Add(_player);
            _player.AddComponent<Rigidbody2D>();
            _player.AddComponent<BoxCollider2D>().size = new Vector2(0.8f, 0.8f);
            _health = _player.AddComponent<HealthComponent>();
            _movement = _player.AddComponent<PlayerMovement>();
            _dash = _player.AddComponent<PlayerDash>();
            var pool = _player.AddComponent<ProjectilePool>();
            var aiming = _player.AddComponent<PlayerAiming>();
            _weapon = _player.AddComponent<RangedWeapon>();
            _binder = _player.AddComponent<PlayerStatsBinder>();

            _input = new FakePlayerInputReader { Aim = Vector2.right, IsAimFromPointer = false };
            aiming.SetInputReader(_input);
            _movement.SetInputReader(_input);
            _movement.SetBalanceConfig(_config);
            _dash.SetInputReader(_input);
            _dash.SetBalanceConfig(_config);
            _movement.SetMovementOverride(_dash);
            _health.SetMaxHealth(100);

            var definition = ScriptableObject.CreateInstance<RangedWeaponDefinition>();
            _created.Add(definition);
            Set(definition, "_damageMin", 12);
            Set(definition, "_damageMax", 14);
            Set(definition, "_fireRate", 4f);
            Set(definition, "_magazineSize", 12);
            Set(definition, "_reloadTime", 1.2f);
            Set(definition, "_range", 10f);
            Set(definition, "_projectileSpeed", 20f);
            _weapon.SetInputReader(_input);
            _weapon.SetProjectilePool(pool);
            _weapon.SetAiming(aiming);
            _weapon.SetAmmoReserve(new AmmoReserve());
            _weapon.SetDamageRoller(new FixedDamageRoller { FixedValue = 13 });
            _weapon.SetDefinition(definition);

            _binder.Configure(caps, _config);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private static void Set(object target, string field, object value)
        {
            target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);
        }

        [Test]
        public void Baseline_WithoutSources_MatchesConfigValues()
        {
            Assert.AreEqual(_config.MoveSpeed, _movement.CurrentMoveSpeed, 0.0001f);
            Assert.AreEqual(1.4706f, _dash.CurrentDashCooldown, 0.0001f);
            Assert.AreEqual(_config.DashSpeed, _dash.CurrentDashSpeed, 0.0001f);
            Assert.AreEqual(1.2f, _weapon.CurrentReloadTime, 0.0001f);
            Assert.AreEqual(100, _health.MaxHealth);
            Assert.IsTrue(_weapon.TryFire());
            Assert.AreEqual(13, _weapon.LastSpawnedProjectile.Data.Damage);
            Assert.IsTrue(_health.TryApplyDamage(new DamageRequest(30)));
            Assert.AreEqual(70, _health.CurrentHealth);
        }

        [Test]
        public void Consumers_ReadCappedValues_WithoutOwnCapMath()
        {
            _binder.Stats.SetSource(new StatModifierSource("test",
                StatModifier.Percent(StatId.MovementSpeed, 45),
                StatModifier.Percent(StatId.DashCooldownReduction, 60),
                StatModifier.Percent(StatId.DashDistance, 50),
                StatModifier.Percent(StatId.ReloadSpeed, 100),
                StatModifier.Percent(StatId.WeaponDamage, 80),
                StatModifier.Percent(StatId.GeneralDamageReduction, 70),
                StatModifier.Flat(StatId.MaxHealth, 40)));

            Assert.AreEqual(_config.MoveSpeed * 1.3f, _movement.CurrentMoveSpeed, 0.0001f, "Movement capped at +30%.");
            Assert.AreEqual(1.4706f * 0.65f, _dash.CurrentDashCooldown, 0.0001f, "Dash CD reduction capped at 35%.");
            Assert.AreEqual(_config.DashSpeed * 1.3f, _dash.CurrentDashSpeed, 0.0001f, "Dash distance capped at +30%.");
            Assert.AreEqual(1.2f / 1.4f, _weapon.CurrentReloadTime, 0.0001f, "Reload speed capped at +40%.");
            Assert.AreEqual(140, _health.MaxHealth, "Max HP synced from the pipeline.");
            Assert.AreEqual(100, _health.CurrentHealth, "Resizing never refills or drops current health.");

            Assert.IsTrue(_weapon.TryFire());
            Assert.AreEqual(Mathf.RoundToInt(13 * 1.5f), _weapon.LastSpawnedProjectile.Data.Damage, "Weapon damage capped at +50%, integer result.");

            Assert.IsTrue(_health.TryApplyDamage(new DamageRequest(100)));
            Assert.AreEqual(40, _health.CurrentHealth, "100 damage at 40% DR cap → 60 applied.");

            _binder.Stats.RemoveSource("test");
            Assert.AreEqual(_config.MoveSpeed, _movement.CurrentMoveSpeed, 0.0001f);
            Assert.AreEqual(100, _health.MaxHealth);
            Assert.AreEqual(40, _health.CurrentHealth);
        }

        [UnityTest]
        public IEnumerator DashDuration_StaysAtApprovedValue_WhileDistanceBonusApplies()
        {
            _binder.Stats.SetSource(new StatModifierSource("test", StatModifier.Percent(StatId.DashDistance, 30)));
            _input.Move = Vector2.right;
            yield return new WaitForFixedUpdate();
            _input.RaiseDash();
            Assert.IsTrue(_dash.IsDashing);
            var start = _player.transform.position.x;
            yield return new WaitForSeconds(0.18f + 0.05f);
            Assert.IsFalse(_dash.IsDashing, "0.18s duration is untouched.");
            var travelled = _player.transform.position.x - start;
            Assert.Greater(travelled, _config.DashSpeed * 0.18f * 1.15f, "Distance grew with the capped +30% bonus.");
        }
    }
}
