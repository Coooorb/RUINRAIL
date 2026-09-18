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
    /// <summary>TASK 059 — the first four catalog specials running through the shared controller from their authored assets.</summary>
    public class LegendarySpecialsITests
    {
        private GameObject _playerObject;
        private ProjectilePool _pool;
        private PlayerAiming _aiming;
        private FakePlayerInputReader _input;
        private RangedWeapon _weapon;
        private LegendarySpecialController _controller;
        private PlayerInventory _inventory;
        private readonly List<Object> _created = new();
        private float _previousCaptureDeltaTime;

        [SetUp]
        public void SetUp()
        {
            var catalog = AssetDatabase.FindAssets("t:ItemDefinition").Select(g => AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(d => d != null).ToList();
            var registry = ItemDefinitionRegistry.Build(catalog);
            var ammoBalance = AssetDatabase.LoadAssetAtPath<AmmoBalanceConfig>("Assets/Game/ScriptableObjects/Items/AmmoBalanceConfig.asset");
            _inventory = PlayerInventory.FromRegistry(registry, ammoBalance);
            _inventory.Add(AmmoType.Light, 60);
            _inventory.Add(AmmoType.Medium, 60);

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
            _weapon = _playerObject.AddComponent<RangedWeapon>();
            _weapon.SetInputReader(_input);
            _weapon.SetProjectilePool(_pool);
            _weapon.SetAiming(_aiming);
            _weapon.SetAmmoReserve(_inventory);
            _weapon.SetDamageRoller(new UnityRandomDamageRoller());
            _controller = _playerObject.AddComponent<LegendarySpecialController>();
            _controller.SetInputReader(_input);
            _previousCaptureDeltaTime = Time.captureDeltaTime;
        }

        [TearDown]
        public void TearDown()
        {
            Time.captureDeltaTime = _previousCaptureDeltaTime;
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private void Arm(string weaponFile)
        {
            var definition = AssetDatabase.LoadAssetAtPath<RangedWeaponDefinition>($"Assets/Game/ScriptableObjects/Items/{weaponFile}.asset");
            Assert.IsNotNull(definition);
            _weapon.SetDefinition(definition);
            _weapon.OnEquipped();
            var specials = new LegendarySpecialRegistry(AssetDatabase.FindAssets("t:LegendarySpecialDefinition").Select(g => AssetDatabase.LoadAssetAtPath<LegendarySpecialDefinition>(AssetDatabase.GUIDToAssetPath(g))));
            var special = specials.CreateFor(definition.LegendaryMechanicId);
            Assert.IsNotNull(special, definition.LegendaryMechanicId);
            _controller.Configure(special, _weapon, () => new SpecialContext(_playerObject, _playerObject.GetComponent<Rigidbody2D>(), () => _aiming.AimDirection, () => _playerObject.transform.position, _pool, new UnityRandomDamageRoller()));
        }

        private List<Projectile> Active() => _pool.GetComponentsInChildren<Projectile>(true).Where(p => p.gameObject.activeSelf).ToList();

        private TestDamageableTarget Enemy(Vector2 position)
        {
            var go = new GameObject("Enemy");
            _created.Add(go);
            go.transform.position = position;
            go.AddComponent<CircleCollider2D>().radius = 0.3f;
            go.AddComponent<TeamMember>().SetTeam(DamageTeam.Enemy);
            return go.AddComponent<TestDamageableTarget>();
        }

        // ---- Acceptance 1 + 3: exact counts/damage/cooldowns; independent of normal fire; no resource ----

        [UnityTest]
        public IEnumerator Snapfire_Fires8RapidShotsOf10To12_Cooldown10()
        {
            Arm("Quickfang");
            Time.captureDeltaTime = 0.02f;
            Assert.IsTrue(_controller.TryActivate());
            Assert.AreEqual(10f, _controller.State.CooldownRemaining, 0.001f);
            for (var i = 0; i < 30; i++)
            {
                yield return null; // 0.6 s > 8 x 0.06
                foreach (var p in Active()) Assert.IsTrue(p.Data.Damage >= 10 && p.Data.Damage <= 12 && p.Data.Speed == 20f && p.Data.SourceTeam == DamageTeam.Player);
            }

            Assert.AreEqual(8, _pool.SpawnCount, "Exactly eight special projectiles.");
            var shots = Active();
            Assert.IsTrue(shots.All(p => p.Data.Damage >= 10 && p.Data.Damage <= 12));
            Assert.IsTrue(shots.All(p => p.Data.Speed == 20f && p.Data.SourceTeam == DamageTeam.Player));
            Assert.AreEqual(12, _weapon.MagazineAmmo, "No magazine cost.");
            Assert.AreEqual(60, _inventory.Get(AmmoType.Light), "No reserve cost.");
            Assert.IsTrue(_weapon.TryFire(), "Normal fire is independent of the special.");
            Assert.IsFalse(_controller.TryActivate(), "Cooldown gates the special, not the magazine.");
            Assert.AreEqual(10f - 0.6f, _controller.State.CooldownRemaining, 0.1f);
        }

        [Test]
        public void LeadBloom_Fires24ProjectilesInAFan_Cooldown14()
        {
            Arm("Buzzsaw");
            Assert.IsTrue(_controller.TryActivate());
            var shots = Active();
            Assert.AreEqual(24, shots.Count);
            Assert.IsTrue(shots.All(p => p.Data.Damage >= 7 && p.Data.Damage <= 9), "SMG normal damage per projectile.");
            var angles = shots.Select(p => Mathf.Atan2(p.Data.Direction.y, p.Data.Direction.x) * Mathf.Rad2Deg).OrderBy(a => a).ToList();
            Assert.AreEqual(-45f, angles.First(), 0.5f);
            Assert.AreEqual(45f, angles.Last(), 0.5f);
            Assert.AreEqual(24, angles.Distinct().Count(), "A fan, not a stack.");
            Assert.AreEqual(14f, _controller.State.CooldownRemaining, 0.001f);
            Assert.AreEqual(36, _weapon.MagazineAmmo);
        }

        [UnityTest]
        public IEnumerator Overrun_Fires12ShotsOf10To12_Cooldown13()
        {
            Arm("Vanguard");
            Time.captureDeltaTime = 0.01f;
            Assert.IsTrue(_controller.TryActivate());
            for (var i = 0; i < 70; i++)
            {
                yield return null; // 0.7 s > 12 x 0.05
                foreach (var p in Active()) Assert.IsTrue(p.Data.Damage >= 10 && p.Data.Damage <= 12);
            }

            Assert.AreEqual(12, _pool.SpawnCount, "Exactly twelve hyper-burst projectiles.");
            Assert.AreEqual(13f - 0.7f, _controller.State.CooldownRemaining, 0.1f);
            Assert.AreEqual(30, _weapon.MagazineAmmo);
        }

        // ---- Acceptance 2: Piercing Line ----

        [UnityTest]
        public IEnumerator PiercingLine_PassesThroughEveryEnemyOnce_AndEndsOnWalls_Cooldown12()
        {
            Arm("Judicator");
            var a = Enemy(new Vector2(2f, 0f));
            var b = Enemy(new Vector2(4f, 0.1f));
            var c = Enemy(new Vector2(6f, -0.1f));
            var wall = new GameObject("Wall");
            _created.Add(wall);
            wall.transform.position = new Vector2(8f, 0f);
            wall.AddComponent<BoxCollider2D>().size = new Vector2(0.2f, 3f);
            wall.AddComponent<EnvironmentObstacle>();
            var behind = Enemy(new Vector2(10f, 0f));

            Assert.IsTrue(_controller.TryActivate());
            var shot = Active().Single();
            Assert.IsTrue(shot.Data.Piercing);
            Assert.IsTrue(shot.Data.Damage >= 55 && shot.Data.Damage <= 65);
            Assert.AreEqual(26f, shot.Data.Speed);
            for (var i = 0; i < 25; i++) yield return new WaitForFixedUpdate();

            Assert.AreEqual(1, a.HitCount);
            Assert.AreEqual(1, b.HitCount);
            Assert.AreEqual(1, c.HitCount);
            Assert.AreEqual(0, behind.HitCount, "Environment terminates the line.");
            Assert.IsFalse(shot.gameObject.activeSelf);
            Assert.AreEqual(12f, _controller.State.CooldownSeconds);
            Assert.AreEqual(10, _weapon.MagazineAmmo);
        }

        // ---- Acceptance 4: usable through the generic loadout/inventory ----

        [Test]
        public void LegendaryDefinitions_EquipThroughTheGenericInventory_InEitherSlot()
        {
            foreach (var id in new[] { "weapon_quickfang", "weapon_buzzsaw", "weapon_vanguard", "weapon_judicator" })
            {
                var primary = new ItemInstance(id, 1, Rarity.Legendary);
                Assert.IsTrue(_inventory.TryEquip(primary, EquippedSlot.PrimaryWeapon), id);
                Assert.AreSame(primary, _inventory.Unequip(EquippedSlot.PrimaryWeapon));
                Assert.IsTrue(_inventory.TryEquip(primary, EquippedSlot.SecondaryWeapon), $"{id} in Secondary.");
                _inventory.Unequip(EquippedSlot.SecondaryWeapon);
            }
        }
    }
}
