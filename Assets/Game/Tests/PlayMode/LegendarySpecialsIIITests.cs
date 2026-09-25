using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Projectiles;
using RuinRail.Gameplay.Combat.Weapons;
using RuinRail.Gameplay.Combat.Weapons.Specials;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Player;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>TASK 061 — Overcharge Barrage, Blink Strike, Impaling Charge; 11 specials in total.</summary>
    public class LegendarySpecialsIIITests
    {
        private GameObject _playerObject;
        private ProjectilePool _pool;
        private LegendarySpecialRegistry _registry;
        private LegendarySpecialController _controller;
        private FakePlayerInputReader _input;
        private PlayerAiming _aiming;
        private readonly List<Object> _created = new();

        [SetUp]
        public void SetUp()
        {
            _registry = new LegendarySpecialRegistry(AssetDatabase.FindAssets("t:LegendarySpecialDefinition").Select(g => AssetDatabase.LoadAssetAtPath<LegendarySpecialDefinition>(AssetDatabase.GUIDToAssetPath(g))));
            _playerObject = new GameObject("TestPlayer");
            _created.Add(_playerObject);
            _playerObject.AddComponent<TeamMember>().SetTeam(DamageTeam.Player);
            var rb = _playerObject.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.bodyType = RigidbodyType2D.Kinematic;
            _pool = _playerObject.AddComponent<ProjectilePool>();
            _aiming = _playerObject.AddComponent<PlayerAiming>();
            _input = new FakePlayerInputReader { Aim = Vector2.right };
            _aiming.SetInputReader(_input);
            _controller = _playerObject.AddComponent<LegendarySpecialController>();
            _controller.SetInputReader(_input);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private SpecialContext Context() => new(_playerObject, _playerObject.GetComponent<Rigidbody2D>(), () => _aiming.AimDirection, () => _playerObject.transform.position, _pool, new UnityRandomDamageRoller());

        private TestDamageableTarget Enemy(Vector2 position, int colliders = 1)
        {
            var go = new GameObject("Enemy");
            _created.Add(go);
            go.transform.position = position;
            for (var i = 0; i < colliders; i++)
            {
                var host = i == 0 ? go : new GameObject($"Hurtbox{i}");
                if (i > 0) host.transform.SetParent(go.transform, false);
                host.AddComponent<CircleCollider2D>().radius = 0.3f + 0.1f * i;
            }

            go.AddComponent<TeamMember>().SetTeam(DamageTeam.Enemy);
            return go.AddComponent<TestDamageableTarget>();
        }

        private GameObject Wall(Vector2 position)
        {
            var wall = new GameObject("Wall");
            _created.Add(wall);
            wall.transform.position = position;
            wall.AddComponent<BoxCollider2D>().size = new Vector2(0.2f, 4f);
            wall.AddComponent<EnvironmentObstacle>();
            return wall;
        }

        // ---- Acceptance 4: exactly 11 specials, one per Legendary, 33 weapons total ----

        [Test]
        public void ElevenSpecials_OnePerLegendaryWeapon_And33WeaponsInTotal()
        {
            Assert.AreEqual(11, _registry.Count);
            var weapons = AssetDatabase.FindAssets("t:WeaponDefinition", new[] { "Assets/Game/ScriptableObjects/Items" })
                .Select(g => AssetDatabase.LoadAssetAtPath<WeaponDefinition>(AssetDatabase.GUIDToAssetPath(g))).ToList();
            Assert.AreEqual(33, weapons.Count, "33: two regular plus one Legendary per class.");
            var legendaries = weapons.Where(w => !string.IsNullOrEmpty(w.LegendaryMechanicId)).ToList();
            Assert.AreEqual(11, legendaries.Count);
            foreach (WeaponClass cls in System.Enum.GetValues(typeof(WeaponClass)))
            {
                Assert.AreEqual(1, legendaries.Count(w => w.WeaponClass == cls), $"{cls}: exactly one Legendary.");
            }

            CollectionAssert.AreEquivalent(_registry.Definitions.Select(d => d.Id), legendaries.Select(w => w.LegendaryMechanicId), "Every Legendary names a distinct special and every special is used.");
            foreach (var w in legendaries) Assert.IsNotNull(_registry.CreateFor(w.LegendaryMechanicId), w.Id);
            var kinds = _registry.Definitions.Select(d => d.Kind).Distinct().ToList();
            CollectionAssert.AreEquivalent(System.Enum.GetValues(typeof(SpecialKind)).Cast<SpecialKind>(), kinds, "All six primitives are exercised by the catalog.");
        }

        [Test]
        public void ThreeDefinitions_MatchTheCatalog()
        {
            Assert.IsTrue(_registry.TryGet("overcharge_barrage", out var barrage));
            Assert.AreEqual((SpecialKind.Burst, 14f, 16, 8, 10), (barrage.Kind, barrage.CooldownSeconds, barrage.Shots, barrage.DamageMin, barrage.DamageMax));
            Assert.IsTrue(_registry.TryGet("blink_strike", out var blink));
            Assert.AreEqual((SpecialKind.DashStrike, 10f, 4f, 40, 50), (blink.Kind, blink.CooldownSeconds, blink.DistanceTiles, blink.DamageMin, blink.DamageMax));
            Assert.IsTrue(_registry.TryGet("impaling_charge", out var charge));
            Assert.AreEqual((SpecialKind.DashStrike, 12f, 5f, 50, 60), (charge.Kind, charge.CooldownSeconds, charge.DistanceTiles, charge.DamageMin, charge.DamageMax));

            var redline = AssetDatabase.LoadAssetAtPath<BlasterWeaponDefinition>("Assets/Game/ScriptableObjects/Items/Redline.asset");
            Assert.AreEqual((WeaponClass.Blaster, 8, 10, 9f, 8f, 40f, 100f, 0.5f, 1.9f, "overcharge_barrage", "pool_blaster"), (redline.WeaponClass, redline.DamageMin, redline.DamageMax, redline.FireRate, redline.HeatPerShot, redline.CoolingRatePerSecond, redline.MaxHeat, redline.CoolingDelaySeconds, redline.OverheatLockoutSeconds, redline.LegendaryMechanicId, redline.AffixPool.Id));
            var ghostedge = AssetDatabase.LoadAssetAtPath<MeleeWeaponDefinition>("Assets/Game/ScriptableObjects/Items/Ghostedge.asset");
            Assert.AreEqual((WeaponClass.Knife, 15, 18, 3.8f, 1.2f, 80f, "blink_strike", "pool_melee"), (ghostedge.WeaponClass, ghostedge.DamageMin, ghostedge.DamageMax, ghostedge.AttackRate, ghostedge.AttackRange, ghostedge.AttackArcDegrees, ghostedge.LegendaryMechanicId, ghostedge.AffixPool.Id));
            var railspike = AssetDatabase.LoadAssetAtPath<MeleeWeaponDefinition>("Assets/Game/ScriptableObjects/Items/Railspike.asset");
            Assert.AreEqual((WeaponClass.Spear, 26, 30, 1.7f, 3.0f, 20f, "impaling_charge", "pool_melee_heavy"), (railspike.WeaponClass, railspike.DamageMin, railspike.DamageMax, railspike.AttackRate, railspike.AttackRange, railspike.AttackArcDegrees, railspike.LegendaryMechanicId, railspike.AffixPool.Id));
        }

        // ---- Redline: 16 bolts, no heat ----

        [UnityTest]
        public IEnumerator OverchargeBarrage_Fires16BoltsOf8To10_WithoutTouchingBlasterHeat()
        {
            var blaster = _playerObject.AddComponent<BlasterWeapon>();
            blaster.SetDefinition(AssetDatabase.LoadAssetAtPath<BlasterWeaponDefinition>("Assets/Game/ScriptableObjects/Items/Redline.asset"));
            blaster.SetProjectilePool(_pool);
            blaster.SetAiming(_aiming);
            blaster.SetInputReader(_input);
            blaster.OnEquipped();
            Assert.IsTrue(blaster.TryFire());
            var heatAfterOneShot = blaster.Heat.Heat;
            Assert.AreEqual(8f, heatAfterOneShot, 0.001f);
            var spawnedByBlaster = _pool.SpawnCount;

            _controller.Configure(_registry.CreateFor("overcharge_barrage"), blaster, Context);
            Assert.IsTrue(_controller.TryActivate());
            var start = Time.time;
            while (_controller.IsRunning && Time.time - start < 2f) yield return null;
            Assert.AreEqual(16, _pool.SpawnCount - spawnedByBlaster, "Sixteen energy bolts.");
            foreach (var p in _pool.GetComponentsInChildren<Projectile>(true).Where(p => p.gameObject.activeSelf && p.Data.Damage <= 10))
            {
                Assert.IsTrue(p.Data.Damage >= 8 && p.Data.Damage <= 10);
            }

            Assert.LessOrEqual(blaster.Heat.Heat, heatAfterOneShot, "The special adds no heat; the barrage never overheats the blaster.");
            Assert.AreEqual(14f, _controller.State.CooldownSeconds);
        }

        // ---- Blink Strike / Impaling Charge: movement override, wall-safe, one hit per target, dash untouched ----

        [UnityTest]
        public IEnumerator BlinkStrike_Dashes4Tiles_HitsEachEnemyOnce_AndLeavesTheNormalDashAlone()
        {
            var balance = AssetDatabase.LoadAssetAtPath<PlayerBalanceConfig>("Assets/Game/ScriptableObjects/Player/PlayerBalanceConfig.asset");
            var dash = _playerObject.AddComponent<PlayerDash>();
            dash.SetBalanceConfig(balance);
            dash.SetInputReader(_input);
            var movement = _playerObject.AddComponent<PlayerMovement>();
            movement.SetBalanceConfig(balance);
            movement.SetInputReader(_input);
            movement.RefreshMovementOverrides();
            var knife = _playerObject.AddComponent<MeleeWeapon>();
            knife.SetDefinition(AssetDatabase.LoadAssetAtPath<MeleeWeaponDefinition>("Assets/Game/ScriptableObjects/Items/Ghostedge.asset"));
            knife.SetAiming(_aiming);
            knife.SetInputReader(_input);
            knife.OnEquipped();
            _controller.Configure(_registry.CreateFor("blink_strike"), knife, Context);

            var crossed = Enemy(new Vector2(1.2f, 0f), colliders: 3);
            var alsoCrossed = Enemy(new Vector2(3.5f, 0.3f));
            var beyond = Enemy(new Vector2(6f, 0f));
            yield return null;

            Assert.AreEqual(0f, dash.CooldownRemaining);
            Assert.IsFalse(dash.IsDashing);
            Assert.IsTrue(_controller.TryActivate());
            Assert.IsTrue(_controller.IsActive, "The special owns movement through IMovementOverride.");
            Assert.IsTrue(movement.IsOverridden);
            Assert.IsFalse(dash.IsDashing, "The normal Dash is not started by the special.");
            Assert.IsFalse(dash.IsInvulnerable, "No borrowed iFrames.");
            yield return new WaitForSeconds(0.35f);

            Assert.IsFalse(_controller.IsRunning);
            Assert.IsFalse(movement.IsOverridden);
            Assert.AreEqual(4f, _playerObject.transform.position.x, 0.25f, "Exactly four tiles.");
            Assert.AreEqual(1, crossed.HitCount, "Three colliders, one hit.");
            Assert.IsTrue(crossed.LastDamageAmount >= 40 && crossed.LastDamageAmount <= 50);
            Assert.AreEqual(1, alsoCrossed.HitCount);
            Assert.AreEqual(0, beyond.HitCount);
            Assert.AreEqual(0f, dash.CooldownRemaining, "Normal Dash cooldown untouched.");
            Assert.AreEqual(10f - 0.35f, _controller.State.CooldownRemaining, 0.1f);
        }

        [UnityTest]
        public IEnumerator ImpalingCharge_Charges5Tiles_StopsAtWalls_NeverDoubleHits()
        {
            var spear = _playerObject.AddComponent<MeleeWeapon>();
            spear.SetDefinition(AssetDatabase.LoadAssetAtPath<MeleeWeaponDefinition>("Assets/Game/ScriptableObjects/Items/Railspike.asset"));
            spear.SetAiming(_aiming);
            spear.SetInputReader(_input);
            spear.OnEquipped();
            _controller.Configure(_registry.CreateFor("impaling_charge"), spear, Context);

            var first = Enemy(new Vector2(2f, 0f));
            var second = Enemy(new Vector2(4.5f, -0.2f));
            yield return null;
            Assert.IsTrue(_controller.TryActivate());
            yield return new WaitForSeconds(0.45f);
            Assert.AreEqual(5f, _playerObject.transform.position.x, 0.3f, "Five tiles.");
            Assert.AreEqual(1, first.HitCount);
            Assert.AreEqual(1, second.HitCount);
            Assert.IsTrue(first.LastDamageAmount >= 50 && first.LastDamageAmount <= 60);

            // Into a wall: the charge ends in front of it, hitting only what it actually reaches.
            Wall(new Vector2(7f, 0f));
            var behindWall = Enemy(new Vector2(9f, 0f));
            second.transform.position = new Vector2(0f, 10f); // out of the new charge's reach
            Physics2D.SyncTransforms();
            yield return null;
            _controller.State.Reset();
            Assert.IsTrue(_controller.TryActivate());
            yield return new WaitForSeconds(0.45f);
            Assert.LessOrEqual(_playerObject.transform.position.x, 6.95f, "Stopped by the wall.");
            Assert.AreEqual(0, behindWall.HitCount);
            Assert.AreEqual(1, second.HitCount, "An enemy not crossed by the new charge is not hit again.");
            Assert.AreEqual(12f, _controller.State.CooldownSeconds);
        }
    }
}
