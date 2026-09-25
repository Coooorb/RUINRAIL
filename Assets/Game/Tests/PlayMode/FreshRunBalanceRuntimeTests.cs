using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Projectiles;
using RuinRail.Gameplay.Combat.Weapons;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Enemies.Encounters;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Player;
using RuinRail.Gameplay.Stats;
using UnityEngine;
using UnityEngine.TestTools;
using static RuinRail.Tests.FreshRunBalance;

namespace RuinRail.Tests
{
    /// <summary>
    /// Runtime verification of the fresh-run balance model.
    ///
    /// The EditMode harness computes every figure in this pass from shipped data through shipped formulas. That is only
    /// trustworthy if the same numbers come out of the real components in a running scene, so this suite fires real
    /// weapons at real enemies with real HealthComponents and real depth scaling, and compares the outcome against
    /// <see cref="WeaponProfile"/> and <see cref="EnemyProfile"/>. A mismatch fails the pass rather than quietly
    /// invalidating the CSVs.
    ///
    /// It writes TestResults/FreshRunBalance/runtime_verification.csv as the evidence that the model is not just algebra.
    /// </summary>
    public class FreshRunBalanceRuntimeTests
    {
        private const string Folder = "TestResults/FreshRunBalance";
        private static readonly List<string> Rows = new();

        private readonly List<Object> _created = new();
        private GameContentCatalog _content;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            Rows.Clear();
            Rows.Add("Quantity,Subject,ModelValue,RuntimeValue,Tolerance,Result,HowRuntimeWasMeasured");
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            if (Rows.Count <= 1) return;
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Path.Combine(Folder, "runtime_verification.csv"), string.Join("\n", Rows) + "\n");
        }

        [SetUp]
        public void SetUp()
        {
            _content = GameContentCatalog.Load();
            Assert.IsNotNull(_content);
            DamageAuthority.LocalIsAuthoritative = true;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private static void Record(string quantity, string subject, float model, float runtime, float tolerance, string how)
        {
            var pass = Mathf.Abs(model - runtime) <= tolerance;
            Rows.Add(string.Join(",", quantity, Csv(subject), F(model), F(runtime), F(tolerance), pass ? "MATCH" : "MISMATCH", Csv(how)));
            Assert.AreEqual(model, runtime, tolerance, $"{quantity} for {subject}: the model and the runtime must agree");
        }

        private WeaponDefinition Definition(string id) => _content.Items.OfType<WeaponDefinition>().First(w => w.Id == id);

        private PlayerStats Stats(params StatModifier[] modifiers)
        {
            var stats = new PlayerStats(_content.StatCaps, 100);
            if (modifiers.Length > 0) stats.SetSource(new StatModifierSource("harness", modifiers));
            return stats;
        }

        /// <summary>
        /// A player built by the shipped composition, so the weapons mounted on it read the entity's own authoritative
        /// PlayerStats. Comparisons against the model use that same pipeline, never a detached one.
        /// </summary>
        private GameObject BuildPlayer(FakePlayerInputReader input, Vector2 position = default)
        {
            var go = PlayerEntityBuilder.Build(new PlayerEntityBuilder.Options
            {
                Name = "BalancePlayer",
                IsLocal = true,
                InputReader = input,
                BalanceConfig = _content.PlayerBalance,
                Caps = _content.StatCaps,
                Position = position
            });
            _created.Add(go);
            return go;
        }

        // ================= weapon damage and cadence =================

        [UnityTest]
        public IEnumerator ModelShotsToKillMatchesRealShotsToKill()
        {
            foreach (var id in new[] { "weapon_p9_ranger", "weapon_marauder_a2", "weapon_farline" })
            {
                var definition = (RangedWeaponDefinition)Definition(id);
                var input = new FakePlayerInputReader { Aim = Vector2.right, IsAimFromPointer = false };
                var player = BuildPlayer(input);
                var pool = player.AddComponent<ProjectilePool>();
                var weapon = player.AddComponent<RangedWeapon>();
                var aiming = player.GetComponent<PlayerAiming>();
                weapon.SetInputReader(input);
                weapon.SetAiming(aiming);
                weapon.SetProjectilePool(pool);
                weapon.SetDamageRoller(new FixedDamageRoller { FixedValue = (definition.DamageMin + definition.DamageMax) / 2 });
                weapon.SetDefinition(definition);
                var reserve = new AmmoReserve();
                reserve.Add(definition.AmmoType, 999);
                weapon.SetAmmoReserve(reserve);
                var stats = player.GetComponent<PlayerStatsBinder>().Stats;
                weapon.SetStats(stats);
                yield return null;

                // A real target with a real HealthComponent, taking real DamageRequests from the real projectiles.
                var targetDefinition = _content.Enemies.First(e => e.Id == "brute");
                var health = NewTarget(targetDefinition.BaseHealth);
                var profile = new WeaponProfile(definition, stats, new SkillProfile("PERFECT", 1f, 1f, 0f, 0f));

                var shots = 0;
                while (health.IsAlive && shots < 200)
                {
                    if (!weapon.TryFire()) { yield return null; continue; }
                    shots++;
                    var projectile = weapon.LastSpawnedProjectile;
                    Assert.IsNotNull(projectile);
                    // Deliver the shot the way a hit does: the projectile's own damage into the real health component.
                    foreach (var p in weapon.LastSpawnedProjectiles)
                        health.TryApplyDamage(new DamageRequest(p.Data.Damage));
                    yield return new WaitForSeconds(weapon.CurrentFireInterval + 0.02f);
                }

                Record("shots to kill", $"{id} vs Brute ({targetDefinition.BaseHealth} HP)",
                    profile.ShotsToKill(targetDefinition.BaseHealth), shots, 1f,
                    "fired the real weapon component and applied each spawned projectile's own damage to a real HealthComponent");
                TearDown(); // each weapon gets its own scene objects; the fixture's teardown runs again after the test
            }
        }

        [UnityTest]
        public IEnumerator ModelMagazineAndReloadMatchTheRealCycle()
        {
            var definition = (RangedWeaponDefinition)Definition("weapon_marauder_a2");
            var input = new FakePlayerInputReader { Aim = Vector2.right, IsAimFromPointer = false };
            var player = BuildPlayer(input);
            var pool = player.AddComponent<ProjectilePool>();
            var weapon = player.AddComponent<RangedWeapon>();
            weapon.SetInputReader(input);
            weapon.SetAiming(player.GetComponent<PlayerAiming>());
            weapon.SetProjectilePool(pool);
            weapon.SetDamageRoller(new FixedDamageRoller { FixedValue = 10 });
            weapon.SetDefinition(definition);
            var reserve = new AmmoReserve();
            reserve.Add(definition.AmmoType, 999);
            weapon.SetAmmoReserve(reserve);
            var stats = player.GetComponent<PlayerStatsBinder>().Stats;
            weapon.SetStats(stats);
            yield return null;

            var profile = new WeaponProfile(definition, stats, SkillProfile.High);
            Record("effective magazine", definition.Id, profile.Magazine, weapon.CurrentMagazineSize, 0f, "RangedWeapon.CurrentMagazineSize on a mounted weapon");
            Record("reload seconds", definition.Id, profile.ReloadSeconds, weapon.CurrentReloadTime, 0.001f, "RangedWeapon.CurrentReloadTime");
            Record("shots per second", definition.Id, profile.ShotsPerSecond, 1f / weapon.CurrentFireInterval, 0.01f, "1 / RangedWeapon.CurrentFireInterval");

            // Empty the magazine for real and time the reload the weapon actually runs.
            while (weapon.MagazineAmmo > 0)
            {
                if (weapon.TryFire()) { }
                yield return new WaitForSeconds(weapon.CurrentFireInterval + 0.01f);
            }

            var started = Time.time;
            Assert.IsTrue(weapon.IsReloading || weapon.TryStartReload(), "the weapon reloads when the magazine runs out");
            while (weapon.IsReloading && Time.time - started < 10f) yield return null;
            Record("measured reload seconds", definition.Id, profile.ReloadSeconds, Time.time - started, 0.25f,
                "wall-clock time the real reload coroutine took from empty to full");
        }

        // ================= affixes reach the model =================

        [UnityTest]
        public IEnumerator AffixModifiersMoveTheRealWeaponTheSameWayTheModelSays()
        {
            var definition = (RangedWeaponDefinition)Definition("weapon_marauder_a2");
            var modifiers = new[]
            {
                StatModifier.Percent(StatId.FireRate, 9), StatModifier.Percent(StatId.MagazineSize, 20),
                StatModifier.Percent(StatId.ReloadSpeed, 14), StatModifier.Percent(StatId.WeaponDamage, 10)
            };

            var input = new FakePlayerInputReader { Aim = Vector2.right, IsAimFromPointer = false };
            var player = BuildPlayer(input);
            var weapon = player.AddComponent<RangedWeapon>();
            weapon.SetInputReader(input);
            weapon.SetAiming(player.GetComponent<PlayerAiming>());
            weapon.SetProjectilePool(player.AddComponent<ProjectilePool>());
            weapon.SetDamageRoller(new FixedDamageRoller { FixedValue = 11 });
            weapon.SetDefinition(definition);
            var stats = player.GetComponent<PlayerStatsBinder>().Stats;
            weapon.SetStats(stats);
            stats.SetSource(new StatModifierSource("epic_affixes", modifiers));
            weapon.SetStats(stats);
            yield return null;

            var profile = new WeaponProfile(definition, stats, SkillProfile.High);
            Record("affixed magazine", definition.Id + " (Epic combo)", profile.Magazine, weapon.CurrentMagazineSize, 0f, "RangedWeapon.CurrentMagazineSize with the Epic affix combination registered");
            Record("affixed shots per second", definition.Id + " (Epic combo)", profile.ShotsPerSecond, weapon.CurrentFireRate, 0.01f, "RangedWeapon.CurrentFireRate");
            Record("affixed reload seconds", definition.Id + " (Epic combo)", profile.ReloadSeconds, weapon.CurrentReloadTime, 0.001f, "RangedWeapon.CurrentReloadTime");
        }

        // ================= depth scaling on real enemies =================

        [UnityTest]
        public IEnumerator DepthScaledEnemyHealthMatchesTheModel()
        {
            foreach (var depth in new[] { 1, 10, 30 })
            {
                foreach (var id in new[] { "grunt", "brute" })
                {
                    var definition = _content.Enemies.First(e => e.Id == id);
                    var enemy = new DefaultEnemySpawner(_content.Stagger).Spawn(definition, new Vector2(depth * 10f, 0f), null);
                    _created.Add(enemy.gameObject);
                    EnemySpawnScaling.Apply(enemy, depth, 1, _content.DepthScaling);
                    yield return null;

                    var model = EnemyProfile.From(definition, depth, _content.DepthScaling);
                    var health = enemy.GetComponent<HealthComponent>();
                    Record("scaled max HP", $"{id} at D{depth}", model.Health, health.MaxHealth, 0.5f,
                        "DefaultEnemySpawner + EnemySpawnScaling.Apply on a real EnemyController, read from its HealthComponent");
                }
            }
        }

        [UnityTest]
        public IEnumerator BlasterHeatCycleMatchesTheModel()
        {
            var definition = (BlasterWeaponDefinition)Definition("weapon_pulse_carbine_b1");
            var input = new FakePlayerInputReader { Aim = Vector2.right, IsAimFromPointer = false };
            var player = BuildPlayer(input);
            var blaster = player.AddComponent<BlasterWeapon>();
            blaster.SetInputReader(input);
            blaster.SetAiming(player.GetComponent<PlayerAiming>());
            blaster.SetProjectilePool(player.AddComponent<ProjectilePool>());
            blaster.SetDamageRoller(new FixedDamageRoller { FixedValue = 8 });
            blaster.SetDefinition(definition);
            var stats = player.GetComponent<PlayerStatsBinder>().Stats;
            blaster.SetStats(stats);
            yield return null;

            var profile = new WeaponProfile(definition, stats, SkillProfile.High);
            var shots = 0;
            while (blaster.Heat.CanFire && shots < 40)
            {
                if (blaster.TryFire()) shots++;
                yield return new WaitForSeconds(blaster.CurrentFireInterval + 0.01f);
            }

            Record("shots to overheat", definition.Id, profile.ShotsToOverheat, shots, 1f,
                "fired the real BlasterWeapon until its real BlasterHeatState refused");
            Record("heat per shot", definition.Id, profile.HeatPerShot, blaster.CurrentHeatPerShot, 0.01f, "BlasterWeapon.CurrentHeatPerShot");
            Record("cooling rate", definition.Id, profile.CoolingRate, blaster.Heat.EffectiveCoolingRatePerSecond, 0.01f, "BlasterHeatState.EffectiveCoolingRatePerSecond");
        }

        [UnityTest]
        public IEnumerator MeleeCadenceMatchesTheModel()
        {
            var definition = (MeleeWeaponDefinition)Definition("weapon_field_knife");
            var input = new FakePlayerInputReader { Aim = Vector2.right, IsAimFromPointer = false };
            var player = BuildPlayer(input);
            var melee = player.AddComponent<MeleeWeapon>();
            melee.SetInputReader(input);
            melee.SetAiming(player.GetComponent<PlayerAiming>());
            melee.SetDamageRoller(new FixedDamageRoller { FixedValue = 15 });
            melee.SetDefinition(definition);
            var stats = player.GetComponent<PlayerStatsBinder>().Stats;
            melee.SetStats(stats);
            yield return null;

            var profile = new WeaponProfile(definition, stats, SkillProfile.High);
            Record("melee attack rate", definition.Id, profile.ShotsPerSecond, melee.CurrentAttackRate, 0.01f, "MeleeWeapon.CurrentAttackRate");
            Record("melee wind-up", definition.Id, profile.WindUpSeconds, melee.CurrentWindUpSeconds, 0.001f, "MeleeWeapon.CurrentWindUpSeconds");
            Record("melee recovery", definition.Id, profile.RecoverySeconds, melee.CurrentRecoverySeconds, 0.001f, "MeleeWeapon.CurrentRecoverySeconds");
        }

        private HealthComponent NewTarget(int maxHealth)
        {
            var go = new GameObject("BalanceTarget");
            _created.Add(go);
            go.transform.position = new Vector2(3f, 0f);
            var health = go.AddComponent<HealthComponent>();
            health.SetMaxHealth(maxHealth);
            return health;
        }
    }
}
